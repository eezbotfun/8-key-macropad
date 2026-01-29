"""
CPU and GPU Temperature Monitor with Named Pipe IPC Communication
Reads PC status and sends it to EezBotFun Configurator Application
"""

import time
import sys
import json
import struct
import threading
from typing import Optional, Dict, Any

try:
    import psutil
except ImportError:
    psutil = None

try:
    import pynvml
    NVML_AVAILABLE = True
except ImportError:
    NVML_AVAILABLE = False

try:
    import wmi
    WMI_AVAILABLE = True
except ImportError:
    WMI_AVAILABLE = False

try:
    import serial
    import serial.tools.list_ports
    SERIAL_AVAILABLE = True
except ImportError:
    SERIAL_AVAILABLE = False

try:
    import win32pipe
    import win32file
    import win32event
    import pywintypes
    NAMED_PIPE_AVAILABLE = True
except ImportError:
    NAMED_PIPE_AVAILABLE = False


class TemperatureMonitor:
    """Monitor CPU and GPU temperatures and system status"""
    
    def __init__(self):
        self.nvml_initialized = False
        self.cpu_temp_max = 0.0
        self.gpu_temp_max = 0.0
        self.gpu_handle = None
        self.tick_counter = 0
        
        if NVML_AVAILABLE:
            try:
                pynvml.nvmlInit()
                self.nvml_initialized = True
                device_count = pynvml.nvmlDeviceGetCount()
                if device_count > 0:
                    self.gpu_handle = pynvml.nvmlDeviceGetHandleByIndex(0)
            except Exception as e:
                print(f"Warning: Could not initialize NVIDIA ML: {e}")
    
    def get_cpu_temperature(self) -> Optional[float]:
        """
        Get CPU temperature in Celsius
        Returns None if unable to read
        """
        # Method 1: Try using WMI on Windows
        if WMI_AVAILABLE:
            try:
                w = wmi.WMI(namespace="root\\wmi")
                temperature_info = w.MSAcpi_ThermalZoneTemperature()
                if temperature_info:
                    # WMI returns temperature in tenths of Kelvin
                    temp_kelvin = temperature_info[0].CurrentTemperature / 10.0
                    temp_celsius = temp_kelvin - 273.15
                    return round(temp_celsius, 1)
            except Exception as e:
                pass
        
        # Method 2: Try using psutil (works on Linux, limited on Windows)
        if psutil:
            try:
                # On some systems, psutil can access sensors
                if hasattr(psutil, "sensors_temperatures"):
                    temps = psutil.sensors_temperatures()
                    if temps:
                        # Try to find CPU temperature
                        for name, entries in temps.items():
                            if 'cpu' in name.lower() or 'core' in name.lower():
                                for entry in entries:
                                    if entry.current:
                                        return round(entry.current, 1)
            except Exception:
                pass
        
        return None
    
    def get_gpu_temperature(self) -> Optional[float]:
        """
        Get GPU temperature in Celsius
        Returns None if unable to read
        """
        if not self.nvml_initialized:
            return None
        
        try:
            device_count = pynvml.nvmlDeviceGetCount()
            if device_count > 0:
                # Get temperature of first GPU
                handle = pynvml.nvmlDeviceGetHandleByIndex(0)
                temp = pynvml.nvmlDeviceGetTemperature(handle, pynvml.NVML_TEMPERATURE_GPU)
                return round(float(temp), 1)
        except Exception as e:
            return None
        
        return None
    
    def get_gpu_name(self) -> Optional[str]:
        """Get the name of the first GPU"""
        if not self.nvml_initialized or not self.gpu_handle:
            return None
        
        try:
            name = pynvml.nvmlDeviceGetName(self.gpu_handle)
            return name.decode('utf-8') if isinstance(name, bytes) else name
        except Exception:
            pass
        
        return None
    
    def get_cpu_load(self) -> float:
        """Get CPU load percentage"""
        if psutil:
            return round(psutil.cpu_percent(interval=0.1), 6)
        return 0.0
    
    def get_cpu_power_consume(self) -> float:
        """Get CPU power consumption in watts (estimated)"""
        # This is an approximation - actual power consumption requires specific hardware support
        cpu_load = self.get_cpu_load()
        # Rough estimation: base power + load-dependent power
        base_power = 10.0
        load_power = cpu_load * 0.2
        return round(base_power + load_power, 6)
    
    def get_cpu_tjmax(self) -> int:
        """Get CPU TjMax (junction temperature max) - typically 100-105°C for modern CPUs"""
        return 100  # Default value, can be adjusted based on CPU model
    
    def get_cpu_core_temps(self) -> Dict[str, float]:
        """Get individual CPU core temperatures"""
        core_temps = {}
        if psutil and hasattr(psutil, "sensors_temperatures"):
            try:
                temps = psutil.sensors_temperatures()
                core_idx = 1
                for name, entries in temps.items():
                    if 'core' in name.lower():
                        for entry in entries:
                            if entry.current:
                                core_temps[f"core{core_idx}"] = round(entry.current, 1)
                                core_idx += 1
            except Exception:
                pass
        
        # If no core temps found, estimate from CPU temp
        if not core_temps:
            cpu_temp = self.get_cpu_temperature()
            if cpu_temp:
                # Estimate core1 temp (usually higher than package temp)
                core_temps["core1"] = round(cpu_temp + 30, 1)
        
        return core_temps
    
    def get_gpu_load(self) -> float:
        """Get GPU utilization percentage"""
        if not self.nvml_initialized or not self.gpu_handle:
            return 0.0
        
        try:
            util = pynvml.nvmlDeviceGetUtilizationRates(self.gpu_handle)
            return round(float(util.gpu), 1)
        except Exception:
            return 0.0
    
    def get_gpu_power_consume(self) -> float:
        """Get GPU power consumption in watts"""
        if not self.nvml_initialized or not self.gpu_handle:
            return 0.0
        
        try:
            power = pynvml.nvmlDeviceGetPowerUsage(self.gpu_handle)
            return round(float(power) / 1000.0, 3)  # Convert mW to W
        except Exception:
            return 0.0
    
    def get_gpu_fan_rpm(self) -> float:
        """Get GPU fan RPM"""
        if not self.nvml_initialized or not self.gpu_handle:
            return 0.0
        
        try:
            fan_speed = pynvml.nvmlDeviceGetFanSpeed(self.gpu_handle)
            return round(float(fan_speed), 1)
        except Exception:
            return 0.0
    
    def get_gpu_memory_info(self) -> Dict[str, float]:
        """Get GPU memory usage"""
        if not self.nvml_initialized or not self.gpu_handle:
            return {"used": 0.0, "total": 0.0}
        
        try:
            mem_info = pynvml.nvmlDeviceGetMemoryInfo(self.gpu_handle)
            used_mb = round(float(mem_info.used) / (1024 * 1024), 1)
            total_mb = round(float(mem_info.total) / (1024 * 1024), 1)
            return {"used": used_mb, "total": total_mb}
        except Exception:
            return {"used": 0.0, "total": 0.0}
    
    def get_gpu_frequency(self) -> float:
        """Get GPU clock frequency in MHz"""
        if not self.nvml_initialized or not self.gpu_handle:
            return 0.0
        
        try:
            clock = pynvml.nvmlDeviceGetClockInfo(self.gpu_handle, pynvml.NVML_CLOCK_GRAPHICS)
            return round(float(clock), 1)
        except Exception:
            return 0.0
    
    def get_memory_info(self) -> Dict[str, float]:
        """Get system memory information"""
        if not psutil:
            return {"used": 0.0, "avail": 0.0, "percent": 0.0}
        
        try:
            mem = psutil.virtual_memory()
            used_gb = round(float(mem.used) / (1024 ** 3), 6)
            avail_gb = round(float(mem.available) / (1024 ** 3), 6)
            percent = round(mem.percent, 5)
            return {"used": used_gb, "avail": avail_gb, "percent": percent}
        except Exception:
            return {"used": 0.0, "avail": 0.0, "percent": 0.0}
    
    def get_storage_info(self) -> Dict[str, float]:
        """Get storage information"""
        if not psutil:
            return {"temp": 0.0, "read": 0.0, "write": 0.0, "percent": 0.0}
        
        try:
            # Get primary disk (usually C: on Windows)
            disk = psutil.disk_usage('/')
            if sys.platform == 'win32':
                disk = psutil.disk_usage('C:\\')
            
            percent = round(disk.percent, 5)
            
            # Get disk I/O
            io_counters = psutil.disk_io_counters()
            read_mb = round(float(io_counters.read_bytes) / (1024 ** 2), 1) if io_counters else 0.0
            write_mb = round(float(io_counters.write_bytes) / (1024 ** 2), 1) if io_counters else 0.0
            
            # Storage temperature is typically not available via standard APIs
            # Using a placeholder or trying to get from sensors
            storage_temp = 0.0
            if psutil and hasattr(psutil, "sensors_temperatures"):
                try:
                    temps = psutil.sensors_temperatures()
                    for name, entries in temps.items():
                        if 'nvme' in name.lower() or 'ssd' in name.lower() or 'disk' in name.lower():
                            for entry in entries:
                                if entry.current:
                                    storage_temp = round(entry.current, 1)
                                    break
                except Exception:
                    pass
            
            return {
                "temp": storage_temp,
                "read": read_mb,
                "write": write_mb,
                "percent": percent
            }
        except Exception:
            return {"temp": 0.0, "read": 0.0, "write": 0.0, "percent": 0.0}
    
    def get_network_info(self) -> Dict[str, float]:
        """Get network upload/download statistics"""
        if not psutil:
            return {"up": 0.0, "down": 0.0}
        
        try:
            net_io = psutil.net_io_counters()
            up_mb = round(float(net_io.bytes_sent) / (1024 ** 2), 1) if net_io else 0.0
            down_mb = round(float(net_io.bytes_recv) / (1024 ** 2), 1) if net_io else 0.0
            return {"up": up_mb, "down": down_mb}
        except Exception:
            return {"up": 0.0, "down": 0.0}
    
    def get_board_rpm(self) -> float:
        """Get board/system fan RPM (estimated or from sensors)"""
        # This is typically not directly available, using GPU fan as approximation
        return self.get_gpu_fan_rpm()
    
    def collect_pc_status(self, cmd: int = 1230) -> Dict[str, Any]:
        """Collect all PC status information and format as JSON structure"""
        current_time = int(time.time())
        self.tick_counter += 1
        
        # CPU information
        cpu_temp = self.get_cpu_temperature() or 0.0
        if cpu_temp > self.cpu_temp_max:
            self.cpu_temp_max = cpu_temp
        
        cpu_core_temps = self.get_cpu_core_temps()
        core1_temp = cpu_core_temps.get("core1", cpu_temp + 30)
        tjmax = self.get_cpu_tjmax()
        core1_distance = max(0, tjmax - core1_temp)
        
        # GPU information
        gpu_temp = self.get_gpu_temperature() or 0.0
        if gpu_temp > self.gpu_temp_max:
            self.gpu_temp_max = gpu_temp
        
        gpu_mem = self.get_gpu_memory_info()
        
        # Build the status dictionary
        status = {
            "time": current_time,
            "board": {
                "temp": round(cpu_temp, 1),  # Using CPU temp as board temp approximation
                "rpm": round(self.get_board_rpm(), 4),
                "tick": self.tick_counter
            },
            "cpu": {
                "temp": round(cpu_temp, 1),
                "tempMax": round(self.cpu_temp_max, 1),
                "load": self.get_cpu_load(),
                "consume": self.get_cpu_power_consume(),
                "tjMax": tjmax,
                "core1DistanceToTjMax": round(core1_distance, 1),
                "core1Temp": round(core1_temp, 1)
            },
            "gpu": {
                "temp": round(gpu_temp, 1),
                "tempMax": round(self.gpu_temp_max, 3),
                "load": self.get_gpu_load(),
                "consume": self.get_gpu_power_consume(),
                "rpm": round(self.get_gpu_fan_rpm(), 1),
                "memUsed": gpu_mem["used"],
                "memTotal": gpu_mem["total"],
                "freq": self.get_gpu_frequency()
            },
            "storage": self.get_storage_info(),
            "memory": self.get_memory_info(),
            "network": self.get_network_info(),
            "cmd": cmd
        }
        
        return status
    
    def display_temperatures(self, continuous: bool = False, interval: float = 1.0):
        """
        Display CPU and GPU temperatures
        
        Args:
            continuous: If True, continuously update temperatures
            interval: Update interval in seconds (for continuous mode)
        """
        if continuous:
            try:
                while True:
                    self._print_temperatures()
                    time.sleep(interval)
                    # Clear previous line (works in most terminals)
                    sys.stdout.write('\033[2K\r')
            except KeyboardInterrupt:
                print("\n\nMonitoring stopped.")
        else:
            self._print_temperatures()
    
    def _print_temperatures(self):
        """Print current temperatures"""
        cpu_temp = self.get_cpu_temperature()
        gpu_temp = self.get_gpu_temperature()
        gpu_name = self.get_gpu_name()
        
        print("=" * 50)
        print("Temperature Monitor")
        print("=" * 50)
        
        if cpu_temp is not None:
            print(f"CPU Temperature: {cpu_temp}°C")
        else:
            print("CPU Temperature: Unable to read")
        
        if gpu_temp is not None:
            gpu_display = f"{gpu_name}: " if gpu_name else ""
            print(f"GPU Temperature: {gpu_display}{gpu_temp}°C")
        else:
            print("GPU Temperature: Unable to read (NVIDIA GPU required)")
        
        print("=" * 50)
    
    def __del__(self):
        """Cleanup on deletion"""
        if self.nvml_initialized:
            try:
                pynvml.nvmlShutdown()
            except Exception:
                pass


class NamedPipeSender:
    """Handle named pipe communication"""
    
    def __init__(self, pipe_name: str = "ezb-macropad"):
        self.pipe_name = pipe_name
        self.pipe_path = f"\\\\.\\pipe\\{pipe_name}"
        self.pipe_handle = None
        self.read_thread = None
        self.read_thread_running = False
        self.write_thread = None
        self.write_thread_running = False
        self.write_queue = None
        self.debug_mode = False
        self.log_file = None
        self.read_callback = None
        
        if not NAMED_PIPE_AVAILABLE:
            raise ImportError("pywin32 is not installed. Install it with: pip install pywin32")
    
    def list_ports(self) -> list:
        """For compatibility - named pipes don't need listing"""
        # Return the pipe name as a "port" for compatibility
        return [self.pipe_name]
    
    def connect(self, pipe_name: Optional[str] = None, retry_count: int = 5, retry_delay: float = 0.5) -> bool:
        """Connect to named pipe with retry logic
        
        Args:
            pipe_name: Name of the pipe (default: ezb-macropad)
            retry_count: Number of retry attempts
            retry_delay: Delay between retries in seconds
        """
        if pipe_name:
            self.pipe_name = pipe_name
            self.pipe_path = f"\\\\.\\pipe\\{pipe_name}"
        
        for attempt in range(retry_count):
            if attempt > 0:
                print(f"Connection attempt {attempt + 1}/{retry_count}...")
            try:
                # Open named pipe (client mode) with overlapped I/O flag
                # Use FILE_SHARE_READ | FILE_SHARE_WRITE for better compatibility
                # FILE_FLAG_OVERLAPPED is required for async I/O operations
                self.pipe_handle = win32file.CreateFile(
                    self.pipe_path,
                    win32file.GENERIC_READ | win32file.GENERIC_WRITE,
                    win32file.FILE_SHARE_READ | win32file.FILE_SHARE_WRITE,  # Allow sharing
                    None,  # Default security
                    win32file.OPEN_EXISTING,
                    win32file.FILE_FLAG_OVERLAPPED,  # Enable overlapped I/O
                    None  # No template
                )
                print(f"✓ Successfully connected to named pipe '{self.pipe_name}'")
                return True
            except pywintypes.error as e:
                error_code = e.winerror
                
                if error_code == 2:  # ERROR_FILE_NOT_FOUND
                    if attempt < retry_count - 1:
                        if self.debug_mode:
                            print(f"Pipe not found, retrying... (attempt {attempt + 1}/{retry_count})")
                        time.sleep(retry_delay)
                        continue
                    else:
                        print(f"Error: Named pipe '{self.pipe_name}' not found after {retry_count} attempts.")
                        print(f"Make sure the pipe server is running and creating the pipe '{self.pipe_name}'.")
                        return False
                
                elif error_code == 231:  # ERROR_PIPE_BUSY
                    if attempt < retry_count - 1:
                        if self.debug_mode:
                            print(f"Pipe is busy, waiting and retrying... (attempt {attempt + 1}/{retry_count})")
                        time.sleep(retry_delay)
                        continue
                    else:
                        print(f"Error: Named pipe '{self.pipe_name}' is busy after {retry_count} attempts.")
                        print(f"Another process may be using the pipe. Try again later.")
                        return False
                
                elif error_code == 5:  # ERROR_ACCESS_DENIED
                    if attempt < retry_count - 1:
                        if self.debug_mode:
                            print(f"Access denied, retrying... (attempt {attempt + 1}/{retry_count})")
                        time.sleep(retry_delay)
                        continue
                    else:
                        print(f"Error: Access denied to named pipe '{self.pipe_name}'.")
                        print(f"This may mean:")
                        print(f"  - The pipe server is not running")
                        print(f"  - The pipe exists but is not accepting connections")
                        print(f"  - Permission issues (try running as administrator)")
                        print(f"  - Another process has exclusive access to the pipe")
                        return False
                
                else:
                    print(f"Error connecting to named pipe '{self.pipe_name}': {e}")
                    print(f"Windows error code: {error_code}")
                    if attempt < retry_count - 1:
                        time.sleep(retry_delay)
                        continue
                    return False
                    
            except Exception as e:
                print(f"Error connecting to named pipe '{self.pipe_name}': {e}")
                if attempt < retry_count - 1:
                    time.sleep(retry_delay)
                    continue
                return False
        
        return False
    
    def start_read_thread(self, debug: bool = False, log_file=None, callback=None):
        """Start the read thread for continuous named pipe reading
        
        Args:
            debug: Enable debug logging
            log_file: File to log received data
            callback: Optional callback function(data: bytes) for received data
        """
        if self.read_thread and self.read_thread.is_alive():
            return  # Thread already running
        
        self.debug_mode = debug
        self.log_file = log_file
        self.read_callback = callback
        self.read_thread_running = True
        self.read_thread = threading.Thread(target=self._read_loop, daemon=True)
        self.read_thread.start()
    
    def stop_read_thread(self):
        """Stop the read thread"""
        self.read_thread_running = False
        
        # Cancel any pending I/O operations on the pipe handle
        if self.pipe_handle:
            try:
                win32file.CancelIo(self.pipe_handle)
            except:
                pass
        
        # Don't wait for thread - it's a daemon thread and will exit when I/O is cancelled
        # Just set the flag and move on
    
    def start_write_thread(self):
        """Start the write thread for non-blocking writes"""
        if self.write_thread and self.write_thread.is_alive():
            return  # Thread already running
        
        import queue
        self.write_queue = queue.Queue()
        self.write_thread_running = True
        self.write_thread = threading.Thread(target=self._write_loop, daemon=True)
        self.write_thread.start()
    
    def stop_write_thread(self):
        """Stop the write thread"""
        self.write_thread_running = False
        
        # Cancel any pending I/O operations on the pipe handle
        if self.pipe_handle:
            try:
                win32file.CancelIo(self.pipe_handle)
            except:
                pass
        
        # Try to wake up the thread with a sentinel (non-blocking)
        if self.write_queue:
            try:
                self.write_queue.put_nowait(None)
            except:
                pass
        
        # Don't wait - threads are daemon and will exit when I/O is cancelled
        self.write_queue = None
    
    def _write_loop(self):
        """Continuous write loop running in separate thread"""
        while self.write_thread_running:
            try:
                if self.write_queue:
                    try:
                        # Get message from queue with timeout
                        message_data = self.write_queue.get(timeout=0.1)
                        if message_data is None:  # Sentinel to stop
                            break
                        
                        complete_message, json_str, header, debug = message_data
                        
                        if self.pipe_handle and self.write_thread_running:
                            try:
                                # Write with timeout using overlapped I/O
                                import win32event
                                overlapped = win32file.OVERLAPPED()
                                overlapped.hEvent = win32event.CreateEvent(None, True, False, None)
                                
                                try:
                                    win32file.WriteFile(self.pipe_handle, complete_message, overlapped)
                                    
                                    # Wait for completion with shorter timeout, checking flag periodically
                                    # Use 100ms intervals to check write_thread_running flag
                                    result = None
                                    for _ in range(10):  # 10 * 100ms = 1 second max
                                        if not self.write_thread_running:
                                            # Thread should stop, cancel I/O
                                            win32file.CancelIo(self.pipe_handle)
                                            break
                                        result = win32event.WaitForSingleObject(overlapped.hEvent, 100)
                                        if result == win32event.WAIT_OBJECT_0:
                                            break
                                    
                                    if result == win32event.WAIT_OBJECT_0:
                                        # Write completed - verify with GetOverlappedResult
                                        try:
                                            bytes_written = win32file.GetOverlappedResult(self.pipe_handle, overlapped, True)
                                            total_bytes_written = bytes_written
                                            
                                            if bytes_written != len(complete_message):
                                                print(f"Warning: Only {bytes_written} of {len(complete_message)} bytes written")
                                            
                                            # Log to file if specified (no console output)
                                            if self.log_file:
                                                timestamp = time.strftime('%Y-%m-%d %H:%M:%S')
                                                self.log_file.write(f"[{timestamp}] TX Raw ({total_bytes_written} bytes):\n")
                                                self.log_file.write(f"[{timestamp}] TX Header (hex): {header.hex()}\n")
                                                self.log_file.write(f"[{timestamp}] TX Full message (hex): {complete_message.hex()}\n")
                                                self.log_file.write(f"[{timestamp}] TX JSON: {json_str}\n")
                                                self.log_file.flush()
                                            
                                            if debug:
                                                print(f"DEBUG: Total bytes written: {total_bytes_written}")
                                                print(f"DEBUG: Full JSON string sent:\n{json_str}\n")
                                        except Exception as e:
                                            print(f"Error getting overlapped result for write: {e}")
                                            if self.debug_mode:
                                                import traceback
                                                traceback.print_exc()
                                    else:
                                        # Timeout
                                        print(f"Warning: Write operation timed out after 1 second")
                                        win32file.CancelIo(self.pipe_handle)
                                finally:
                                    win32event.CloseHandle(overlapped.hEvent)
                                    
                            except pywintypes.error as e:
                                print(f"Error writing to named pipe: {e}")
                            except Exception as e:
                                if self.debug_mode:
                                    print(f"DEBUG: Error in write thread: {e}")
                    except:
                        # Queue timeout or empty, continue
                        continue
                else:
                    time.sleep(0.01)
            except Exception as e:
                if self.debug_mode:
                    print(f"DEBUG: Error in write thread loop: {e}")
                time.sleep(0.1)
    
    def _read_loop(self):
        """Continuous read loop running in separate thread"""
        while self.read_thread_running:
            try:
                if self.pipe_handle and self.read_thread_running:
                    # Use overlapped I/O with timeout for non-blocking reads
                    try:
                        import win32event
                        overlapped = win32file.OVERLAPPED()
                        overlapped.hEvent = win32event.CreateEvent(None, True, False, None)
                        
                        try:
                            # Start async read
                            win32file.ReadFile(self.pipe_handle, 4096, overlapped)
                            
                            # Wait with short timeout, checking flag
                            result = None
                            for _ in range(10):  # 10 * 100ms = 1 second max
                                if not self.read_thread_running:
                                    win32file.CancelIo(self.pipe_handle)
                                    break
                                result = win32event.WaitForSingleObject(overlapped.hEvent, 100)
                                if result == win32event.WAIT_OBJECT_0:
                                    break
                            
                            if result == win32event.WAIT_OBJECT_0:
                                # Read completed
                                try:
                                    result_code, data = win32file.GetOverlappedResult(self.pipe_handle, overlapped, True)
                                    if data and len(data) > 0:
                                        self._log_received_data(data)
                                        # Call callback if provided
                                        if self.read_callback:
                                            try:
                                                self.read_callback(data)
                                            except Exception as e:
                                                if self.debug_mode:
                                                    print(f"DEBUG: Error in read callback: {e}")
                                except:
                                    pass
                        finally:
                            win32event.CloseHandle(overlapped.hEvent)
                            
                    except pywintypes.error as e:
                        if e.winerror != 109 and e.winerror != 995:  # ERROR_BROKEN_PIPE, ERROR_OPERATION_ABORTED
                            if self.debug_mode:
                                print(f"DEBUG: Error reading from pipe: {e}")
                        if not self.read_thread_running:
                            break
                        time.sleep(0.1)
                    except Exception as e:
                        if self.debug_mode:
                            print(f"DEBUG: Error in read loop: {e}")
                        if not self.read_thread_running:
                            break
                        time.sleep(0.1)
                else:
                    if not self.read_thread_running:
                        break
                    time.sleep(0.1)
            except Exception as e:
                if self.debug_mode:
                    print(f"DEBUG: Error in read thread: {e}")
                if not self.read_thread_running:
                    break
                time.sleep(0.1)
    
    def _log_received_data(self, data: bytes):
        """Log received data"""
        if not data:
            return
        
        timestamp = time.strftime('%Y-%m-%d %H:%M:%S')
        
        # Try to decode as UTF-8 string
        try:
            data_str = data.decode('utf-8', errors='replace')
            print(f"[{timestamp}] Received ({len(data)} bytes): {data_str}")
            
            if self.debug_mode:
                print(f"DEBUG: Received data hex: {data.hex()}")
                print(f"DEBUG: Received data bytes: {data}")
            
            # Log to file if specified
            if self.log_file:
                self.log_file.write(f"[{timestamp}] RX: {data_str}\n")
                self.log_file.write(f"[{timestamp}] RX (hex): {data.hex()}\n")
                self.log_file.flush()
        except Exception:
            # If decoding fails, log as hex
            print(f"[{timestamp}] Received ({len(data)} bytes, hex): {data.hex()}")
            
            if self.debug_mode:
                print(f"DEBUG: Received data (raw bytes): {data}")
            
            if self.log_file:
                self.log_file.write(f"[{timestamp}] RX (hex): {data.hex()}\n")
                self.log_file.write(f"[{timestamp}] RX (bytes): {data}\n")
                self.log_file.flush()
    
    def send_json(self, data: Dict[str, Any], debug: bool = False) -> bool:
        """Send JSON data through named pipe"""
        if not self.pipe_handle:
            print("Named pipe not connected")
            return False
        
        try:
            # Verify cmd field exists
            if 'cmd' not in data:
                print("WARNING: 'cmd' field is missing from data dictionary!")
                print(f"Available keys: {list(data.keys())}")
            
            json_str = json.dumps(data, separators=(',', ':'))
            
            # Debug logging
            if debug:
                print("\n" + "=" * 70)
                print("DEBUG: JSON String to be sent:")
                print("=" * 70)
                print(json_str)
                print("=" * 70)
                print(f"JSON Length: {len(json_str)} characters")
                print(f"Contains 'cmd': {'cmd' in json_str}")
                if 'cmd' in data:
                    print(f"cmd value: {data['cmd']}")
                print("=" * 70 + "\n")
            
            json_bytes = json_str.encode('utf-8')
            json_length = len(json_bytes)
            
            # Check payload length (uint32 max is 4GB, but we'll use a practical limit)
            if json_length > 10 * 1024 * 1024:  # 10MB limit
                print(f"ERROR: JSON message too long ({json_length} bytes). Maximum is 10MB.")
                return False
            
            # EZBF IPC Protocol header format (12 bytes):
            # Offset 0-3:   Magic "EZBF" (4 bytes)
            # Offset 4:     ProtocolVersion (1 byte) = 1
            # Offset 5:     MessageType (1 byte) = 0x20 (EVENT)
            # Offset 6:     Flags (1 byte) = 0
            # Offset 7:     Reserved (1 byte) = 0
            # Offset 8-11:  PayloadLength (4 bytes, uint32, little-endian)
            
            # Build header
            magic = b'EZBF'  # 4 bytes ASCII
            protocol_version = 1
            message_type = 0x20  # EVENT message type
            flags = 0
            reserved = 0
            
            # Pack header: Magic (4 bytes) + ProtocolVersion (1) + MessageType (1) + Flags (1) + Reserved (1) + PayloadLength (4 bytes, little-endian)
            header = magic + struct.pack('<B B B B I', protocol_version, message_type, flags, reserved, json_length)
            
            # Build complete message: [12-byte header][JSON payload]
            complete_message = header + json_bytes
            
            if debug:
                print(f"DEBUG: JSON byte length: {json_length} bytes")
                print(f"DEBUG: Total bytes to send: {len(complete_message)} bytes")
                print(f"DEBUG: Complete message structure:")
                print(f"  - Header: {len(header)} bytes")
                print(f"  - JSON payload: {json_length} bytes")
                print(f"DEBUG: First 150 bytes of complete message: {complete_message[:150]}")
                print(f"DEBUG: First 150 bytes as hex: {complete_message[:150].hex()}")
                print(f"DEBUG: First 150 bytes as string (where printable): {complete_message[:150]}")
            
            # Queue message for async write (non-blocking)
            if not self.write_queue:
                print("Warning: Write queue not initialized. Call start_write_thread() first.")
                return False
            
            try:
                # Put message in queue (non-blocking, with timeout)
                self.write_queue.put((complete_message, json_str, header, debug), timeout=0.1)
                return True
            except:
                print("Warning: Write queue is full, message dropped")
                return False
        except Exception as e:
            print(f"Error sending data: {e}")
            import traceback
            traceback.print_exc()
            return False
    
    
    def disconnect(self):
        """Close named pipe connection and stop read/write threads (non-blocking)"""
        # Set flags to stop threads
        self.write_thread_running = False
        self.read_thread_running = False
        
        # Cancel all pending I/O operations immediately
        if self.pipe_handle:
            try:
                win32file.CancelIo(self.pipe_handle)
            except:
                pass
        
        # Try to wake up write thread (non-blocking)
        if self.write_queue:
            try:
                self.write_queue.put_nowait(None)
            except:
                pass
        self.write_queue = None
        
        # Close pipe handle immediately (this will unblock any waiting I/O)
        if self.pipe_handle:
            try:
                win32file.CloseHandle(self.pipe_handle)
            except:
                pass
            self.pipe_handle = None
    
    def __enter__(self):
        """Context manager entry"""
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        """Context manager exit"""
        self.disconnect()


def main():
    """Main function"""
    import argparse
    
    parser = argparse.ArgumentParser(
        description="Monitor PC status and send to device via USB serial"
    )
    parser.add_argument(
        '-c', '--continuous',
        action='store_true',
        help='Continuously monitor and send data'
    )
    parser.add_argument(
        '-i', '--interval',
        type=float,
        default=1.0,
        help='Update interval in seconds (default: 1.0)'
    )
    parser.add_argument(
        '-p', '--pipe',
        type=str,
        default='ezb-macropad',
        help='Named pipe name (default: ezb-macropad)'
    )
    parser.add_argument(
        '-l', '--list-pipes',
        action='store_true',
        help='List named pipe information and exit'
    )
    parser.add_argument(
        '-d', '--display-only',
        action='store_true',
        help='Display status without sending to serial'
    )
    parser.add_argument(
        '--cmd',
        type=int,
        default=1230,
        help='Command value in JSON (default: 1230)'
    )
    parser.add_argument(
        '--debug',
        action='store_true',
        help='Enable debug logging (show full JSON content)'
    )
    parser.add_argument(
        '--log-file',
        type=str,
        default=None,
        help='Log JSON content to file (for debugging)'
    )
    
    args = parser.parse_args()
    
    # List pipes if requested
    if args.list_pipes:
        if NAMED_PIPE_AVAILABLE:
            print(f"Named pipe: {args.pipe}")
            print(f"Full path: \\\\.\\pipe\\{args.pipe}")
        else:
            print("pywin32 not available. Install with: pip install pywin32")
        return
    
    monitor = TemperatureMonitor()
    
    # If display only, use old display method
    if args.display_only:
        monitor.display_temperatures(
            continuous=args.continuous,
            interval=args.interval
        )
        return
    
    # Named pipe communication mode
    if not NAMED_PIPE_AVAILABLE:
        print("Error: pywin32 is not installed.")
        print("Install it with: pip install pywin32")
        return
    
    sender = NamedPipeSender(pipe_name=args.pipe)
    
    # Connect to named pipe
    if not sender.connect():
        print(f"Failed to connect to named pipe '{args.pipe}'")
        print(f"Make sure the pipe server is running and the pipe name is correct.")
        return
    
    print(f"Connected to named pipe: {args.pipe}")
    
    # Open log file if specified
    log_file = None
    if args.log_file:
        try:
            log_file = open(args.log_file, 'a', encoding='utf-8')
            print(f"Logging to file: {args.log_file}")
        except Exception as e:
            print(f"Warning: Could not open log file {args.log_file}: {e}")
    
    # Start read and write threads for continuous pipe reading/writing
    sender.start_read_thread(debug=args.debug, log_file=log_file)
    sender.start_write_thread()
    
    print("Sending PC status data... (Press Ctrl+C to stop)")
    
    try:
        while True:
            status = monitor.collect_pc_status(cmd=args.cmd)
            
            # Debug: Verify cmd field before sending
            if args.debug:
                print(f"\nDEBUG: Status dictionary keys: {list(status.keys())}")
                print(f"DEBUG: cmd value in status: {status.get('cmd', 'MISSING!')}")
            
            # Log to file if specified
            if log_file:
                json_str = json.dumps(status, separators=(',', ':'))
                log_file.write(f"{time.strftime('%Y-%m-%d %H:%M:%S')} TX: {json_str}\n")
                log_file.flush()
            
            # Send via named pipe (write thread is main thread)
            if sender.send_json(status, debug=args.debug):
                if not args.debug:  # Only show summary if not in debug mode
                    print(f"Sent: time={status['time']}, CPU={status['cpu']['temp']}°C, GPU={status['gpu']['temp']}°C, cmd={status.get('cmd', 'N/A')}")
            else:
                print("Failed to send data")
            
            if not args.continuous:
                break
            
            time.sleep(args.interval)
            
    except KeyboardInterrupt:
        print("\n\nStopped by user")
    finally:
        if log_file:
            log_file.close()
            print(f"Log file closed: {args.log_file}")
        sender.disconnect()
        print("Named pipe connection closed")


if __name__ == "__main__":
    main()
