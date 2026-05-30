"""
PC Status Monitor GUI
Graphical user interface for displaying PC status in real-time
"""

import tkinter as tk
from tkinter import ttk, scrolledtext
import threading
import time
import sys
import os
from temperature_monitor import TemperatureMonitor, NamedPipeSender, NAMED_PIPE_AVAILABLE

# Windows registry support for auto-start
try:
    import winreg
    REGISTRY_AVAILABLE = True
except ImportError:
    REGISTRY_AVAILABLE = False

class PCStatusGUI:
    """GUI application for PC status monitoring"""
    
    def __init__(self, root):
        self.root = root
        self.root.title("PC Status Monitor For EezBotFun MacroPad")
        self.root.geometry("900x700")
        
        self.monitor = TemperatureMonitor()
        self.sender = None
        self.monitoring = True  # Start monitoring by default
        self.pipe_connected = False
        self.update_interval = 3.0
        
        # Get application path for auto-start
        if getattr(sys, 'frozen', False):
            # Running as compiled executable - quote if path contains spaces
            exe_path = os.path.abspath(sys.executable)
            if ' ' in exe_path:
                self.app_path = f'"{exe_path}"'
            else:
                self.app_path = exe_path
        else:
            # Running as script - use Python executable with script path
            script_path = os.path.abspath(sys.argv[0])
            python_exe = os.path.abspath(sys.executable)
            self.app_path = f'"{python_exe}" "{script_path}"'
        
        self.startup_key_name = "PCStatusMonitorForEezBotFun"
        
        # Create UI
        self.create_widgets()
        
        # Auto-connect to named pipe if available
        if NAMED_PIPE_AVAILABLE:
            try:
                self.sender = NamedPipeSender(pipe_name="ezb-macropad")
                if self.sender.connect():
                    self.sender.start_read_thread(debug=False, log_file=None, 
                                                 callback=self.on_pipe_received)
                    self.sender.start_write_thread()
                    self.pipe_connected = True
                    self.update_pipe_status()
                    self.log_message("Connected to named pipe")
                else:
                    self.pipe_connected = False
                    self.update_pipe_status()
                    self.log_message("Failed to connect to named pipe")
            except Exception as e:
                self.pipe_connected = False
                self.update_pipe_status()
                self.log_message(f"Error connecting to named pipe: {e}")
        
        # Start monitoring
        self.start_monitoring()
        
        # Handle window close
        self.root.protocol("WM_DELETE_WINDOW", self.on_closing)
    
    def create_widgets(self):
        """Create and layout all UI widgets"""
        
        # Main container
        main_frame = ttk.Frame(self.root, padding="10")
        main_frame.grid(row=0, column=0, sticky=(tk.W, tk.E, tk.N, tk.S))
        
        # Configure grid weights
        self.root.columnconfigure(0, weight=1)
        self.root.rowconfigure(0, weight=1)
        main_frame.columnconfigure(1, weight=1)
        
        # Title
        title_label = ttk.Label(main_frame, text="PC Status Monitor V1.0", 
                               font=("Arial", 16, "bold"))
        title_label.grid(row=0, column=0, columnspan=2, pady=(0, 20))
        
        # Left panel - Status display
        status_frame = ttk.LabelFrame(main_frame, text="System Status", padding="10")
        status_frame.grid(row=1, column=0, columnspan=2, sticky=(tk.W, tk.E, tk.N, tk.S), pady=5)
        status_frame.columnconfigure(0, weight=1)
        
        # Create status display areas
        self.create_status_sections(status_frame)
        
        # Right panel - Controls and Serial
        control_frame = ttk.LabelFrame(main_frame, text="Controls", padding="10")
        control_frame.grid(row=1, column=2, sticky=(tk.W, tk.E, tk.N, tk.S), padx=(10, 0))
        
        self.create_control_panel(control_frame)
        
        # Communication log area
        log_frame = ttk.LabelFrame(main_frame, text="Communication Log", padding="10")
        log_frame.grid(row=2, column=0, columnspan=3, sticky=(tk.W, tk.E, tk.N, tk.S), pady=(10, 0))
        log_frame.columnconfigure(0, weight=1)
        log_frame.rowconfigure(0, weight=1)
        
        self.serial_log = scrolledtext.ScrolledText(log_frame, height=8, width=80, wrap=tk.WORD)
        self.serial_log.grid(row=0, column=0, sticky=(tk.W, tk.E, tk.N, tk.S))
        
        # Status bar at bottom
        status_bar_frame = ttk.Frame(main_frame)
        status_bar_frame.grid(row=3, column=0, columnspan=3, sticky=(tk.W, tk.E), pady=(10, 0))
        status_bar_frame.columnconfigure(1, weight=1)
        
        ttk.Label(status_bar_frame, text="Pipe Status:", font=("Arial", 9)).grid(row=0, column=0, sticky=tk.W, padx=(0, 5))
        self.pipe_status_label = ttk.Label(status_bar_frame, text="Disconnected", foreground="red", font=("Arial", 9, "bold"))
        self.pipe_status_label.grid(row=0, column=1, sticky=tk.W)
        
        # Configure row weights
        main_frame.rowconfigure(1, weight=1)
        main_frame.rowconfigure(2, weight=0)
        main_frame.rowconfigure(3, weight=0)
    
    def create_status_sections(self, parent):
        """Create status display sections"""
        
        # Create notebook for tabs
        notebook = ttk.Notebook(parent)
        notebook.grid(row=0, column=0, sticky=(tk.W, tk.E, tk.N, tk.S))
        parent.rowconfigure(0, weight=1)
        parent.columnconfigure(0, weight=1)
        
        # CPU Tab
        cpu_frame = ttk.Frame(notebook, padding="10")
        notebook.add(cpu_frame, text="CPU")
        self.create_cpu_display(cpu_frame)
        
        # GPU Tab
        gpu_frame = ttk.Frame(notebook, padding="10")
        notebook.add(gpu_frame, text="GPU")
        self.create_gpu_display(gpu_frame)
        
        # System Tab
        system_frame = ttk.Frame(notebook, padding="10")
        notebook.add(system_frame, text="System")
        self.create_system_display(system_frame)
        
        # Board Tab
        board_frame = ttk.Frame(notebook, padding="10")
        notebook.add(board_frame, text="Board")
        self.create_board_display(board_frame)
    
    def create_cpu_display(self, parent):
        """Create CPU status display"""
        row = 0
        
        # Temperature
        ttk.Label(parent, text="Temperature:", font=("Arial", 10, "bold")).grid(row=row, column=0, sticky=tk.W, pady=5)
        self.cpu_temp_label = ttk.Label(parent, text="--°C", font=("Arial", 12))
        self.cpu_temp_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        ttk.Label(parent, text="Max Temperature:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.cpu_temp_max_label = ttk.Label(parent, text="--°C")
        self.cpu_temp_max_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        # Load
        ttk.Label(parent, text="CPU Load:", font=("Arial", 10, "bold")).grid(row=row, column=0, sticky=tk.W, pady=(10, 5))
        self.cpu_load_label = ttk.Label(parent, text="--%", font=("Arial", 12))
        self.cpu_load_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        self.cpu_load_bar = ttk.Progressbar(parent, length=200, mode='determinate')
        self.cpu_load_bar.grid(row=row, column=0, columnspan=2, sticky=(tk.W, tk.E), padx=10, pady=5)
        row += 1
        
        # Power Consumption
        ttk.Label(parent, text="Power Consumption:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.cpu_power_label = ttk.Label(parent, text="--W")
        self.cpu_power_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        # Core Temperature
        ttk.Label(parent, text="Core 1 Temperature:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.cpu_core1_temp_label = ttk.Label(parent, text="--°C")
        self.cpu_core1_temp_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        ttk.Label(parent, text="Distance to TjMax:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.cpu_tjmax_dist_label = ttk.Label(parent, text="--°C")
        self.cpu_tjmax_dist_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        ttk.Label(parent, text="TjMax:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.cpu_tjmax_label = ttk.Label(parent, text="--°C")
        self.cpu_tjmax_label.grid(row=row, column=1, sticky=tk.W, padx=10)
    
    def create_gpu_display(self, parent):
        """Create GPU status display"""
        row = 0
        
        # Temperature
        ttk.Label(parent, text="Temperature:", font=("Arial", 10, "bold")).grid(row=row, column=0, sticky=tk.W, pady=5)
        self.gpu_temp_label = ttk.Label(parent, text="--°C", font=("Arial", 12))
        self.gpu_temp_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        ttk.Label(parent, text="Max Temperature:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.gpu_temp_max_label = ttk.Label(parent, text="--°C")
        self.gpu_temp_max_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        # Load
        ttk.Label(parent, text="GPU Load:", font=("Arial", 10, "bold")).grid(row=row, column=0, sticky=tk.W, pady=(10, 5))
        self.gpu_load_label = ttk.Label(parent, text="--%", font=("Arial", 12))
        self.gpu_load_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        self.gpu_load_bar = ttk.Progressbar(parent, length=200, mode='determinate')
        self.gpu_load_bar.grid(row=row, column=0, columnspan=2, sticky=(tk.W, tk.E), padx=10, pady=5)
        row += 1
        
        # Power Consumption
        ttk.Label(parent, text="Power Consumption:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.gpu_power_label = ttk.Label(parent, text="--W")
        self.gpu_power_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        # Memory
        ttk.Label(parent, text="Memory Used:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.gpu_mem_used_label = ttk.Label(parent, text="--MB")
        self.gpu_mem_used_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        ttk.Label(parent, text="Memory Total:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.gpu_mem_total_label = ttk.Label(parent, text="--MB")
        self.gpu_mem_total_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        # Fan Speed
        ttk.Label(parent, text="Fan Speed:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.gpu_fan_label = ttk.Label(parent, text="--RPM")
        self.gpu_fan_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        # Frequency
        ttk.Label(parent, text="Frequency:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.gpu_freq_label = ttk.Label(parent, text="--MHz")
        self.gpu_freq_label.grid(row=row, column=1, sticky=tk.W, padx=10)
    
    def create_system_display(self, parent):
        """Create system status display"""
        row = 0
        
        # Memory
        ttk.Label(parent, text="Memory Usage:", font=("Arial", 10, "bold")).grid(row=row, column=0, sticky=tk.W, pady=5)
        self.mem_percent_label = ttk.Label(parent, text="--%", font=("Arial", 12))
        self.mem_percent_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        self.mem_bar = ttk.Progressbar(parent, length=200, mode='determinate')
        self.mem_bar.grid(row=row, column=0, columnspan=2, sticky=(tk.W, tk.E), padx=10, pady=5)
        row += 1
        
        ttk.Label(parent, text="Used:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.mem_used_label = ttk.Label(parent, text="--GB")
        self.mem_used_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        ttk.Label(parent, text="Available:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.mem_avail_label = ttk.Label(parent, text="--GB")
        self.mem_avail_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        # Storage
        ttk.Label(parent, text="Storage Usage:", font=("Arial", 10, "bold")).grid(row=row, column=0, sticky=tk.W, pady=(10, 5))
        self.storage_percent_label = ttk.Label(parent, text="--%", font=("Arial", 12))
        self.storage_percent_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        self.storage_bar = ttk.Progressbar(parent, length=200, mode='determinate')
        self.storage_bar.grid(row=row, column=0, columnspan=2, sticky=(tk.W, tk.E), padx=10, pady=5)
        row += 1
        
        ttk.Label(parent, text="Temperature:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.storage_temp_label = ttk.Label(parent, text="--°C")
        self.storage_temp_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        ttk.Label(parent, text="Read I/O:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.storage_read_label = ttk.Label(parent, text="--MB")
        self.storage_read_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        ttk.Label(parent, text="Write I/O:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.storage_write_label = ttk.Label(parent, text="--MB")
        self.storage_write_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        # Network
        ttk.Label(parent, text="Network:", font=("Arial", 10, "bold")).grid(row=row, column=0, sticky=tk.W, pady=(10, 5))
        row += 1
        
        ttk.Label(parent, text="Upload:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.net_up_label = ttk.Label(parent, text="--MB")
        self.net_up_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        ttk.Label(parent, text="Download:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.net_down_label = ttk.Label(parent, text="--MB")
        self.net_down_label.grid(row=row, column=1, sticky=tk.W, padx=10)
    
    def create_board_display(self, parent):
        """Create board status display"""
        row = 0
        
        ttk.Label(parent, text="Board Temperature:", font=("Arial", 10, "bold")).grid(row=row, column=0, sticky=tk.W, pady=5)
        self.board_temp_label = ttk.Label(parent, text="--°C", font=("Arial", 12))
        self.board_temp_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        ttk.Label(parent, text="Fan RPM:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.board_rpm_label = ttk.Label(parent, text="--RPM")
        self.board_rpm_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        ttk.Label(parent, text="Tick Counter:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W)
        self.board_tick_label = ttk.Label(parent, text="--")
        self.board_tick_label.grid(row=row, column=1, sticky=tk.W, padx=10)
        row += 1
        
        ttk.Label(parent, text="Last Update:", font=("Arial", 9)).grid(row=row, column=0, sticky=tk.W, pady=(10, 5))
        self.last_update_label = ttk.Label(parent, text="--")
        self.last_update_label.grid(row=row, column=1, sticky=tk.W, padx=10)
    
    def create_control_panel(self, parent):
        """Create control panel"""
        row = 0
        
        # Update interval
        ttk.Label(parent, text="Update Interval (s):").grid(row=row, column=0, sticky=tk.W, pady=5)
        self.interval_var = tk.StringVar(value="3.0")
        interval_spin = ttk.Spinbox(parent, from_=1.0, to=60.0, increment=1.0, 
                                   textvariable=self.interval_var, width=10)
        interval_spin.grid(row=row, column=1, sticky=tk.W, padx=5)
        row += 1
        
        # Auto-start on boot
        if REGISTRY_AVAILABLE and sys.platform == 'win32':
            self.autostart_var = tk.BooleanVar()
            self.autostart_var.set(self.is_autostart_enabled())
            autostart_check = ttk.Checkbutton(parent, text="Start on Windows boot", 
                                              variable=self.autostart_var,
                                              command=self.toggle_autostart)
            autostart_check.grid(row=row, column=0, columnspan=2, sticky=tk.W, pady=5)
            row += 1
        
        # Named pipe connection (hidden - auto-connect on start)
        if NAMED_PIPE_AVAILABLE:
            self.pipe_var = tk.StringVar(value="ezb-macropad")
            # UI elements hidden - pipe connection handled automatically
        
        # Monitoring control
        self.monitor_btn = ttk.Button(parent, text="Stop Monitoring", 
                                     command=self.toggle_monitoring)
        self.monitor_btn.grid(row=row, column=0, columnspan=2, sticky=(tk.W, tk.E), pady=5)
        row += 1
        
        # Status indicator
        self.status_label = ttk.Label(parent, text="Status: Monitoring", 
                                      foreground="green")
        self.status_label.grid(row=row, column=0, columnspan=2, pady=5)
    
    def toggle_monitoring(self):
        """Toggle monitoring on/off"""
        self.monitoring = not self.monitoring
        if self.monitoring:
            self.monitor_btn.config(text="Stop Monitoring")
            self.status_label.config(text="Status: Monitoring", foreground="green")
            self.start_monitoring()
        else:
            self.monitor_btn.config(text="Start Monitoring")
            self.status_label.config(text="Status: Stopped", foreground="red")
    
    def toggle_pipe(self):
        """Toggle named pipe connection"""
        if not NAMED_PIPE_AVAILABLE:
            self.log_message("Named pipe communication not available")
            return
        
        if not self.pipe_connected:
            # Connect
            pipe_name = self.pipe_var.get()
            if not pipe_name:
                self.log_message("No pipe name specified")
                return
            
            try:
                self.sender = NamedPipeSender(pipe_name=pipe_name)
                if self.sender.connect():
                    # Start read and write threads
                    self.sender.start_read_thread(debug=False, log_file=None, 
                                                 callback=self.on_pipe_received)
                    self.sender.start_write_thread()
                    self.pipe_connected = True
                    self.pipe_btn.config(text="Disconnect Pipe")
                    self.log_message(f"Connected to named pipe '{pipe_name}'")
                else:
                    self.log_message(f"Failed to connect to named pipe '{pipe_name}'")
            except Exception as e:
                self.log_message(f"Error connecting: {e}")
        else:
            # Disconnect
            if self.sender:
                self.sender.disconnect()
                self.sender = None
            self.pipe_connected = False
            self.pipe_btn.config(text="Connect Pipe")
            self.log_message("Named pipe disconnected")
    
    def start_monitoring(self):
        """Start the monitoring update loop"""
        if self.monitoring:
            self.update_status()
            self.root.after(int(self.update_interval * 1000), self.start_monitoring)
    
    def update_status(self):
        """Update all status displays"""
        try:
            status = self.monitor.collect_pc_status()
            
            # Update CPU
            self.cpu_temp_label.config(text=f"{status['cpu']['temp']}°C")
            self.cpu_temp_max_label.config(text=f"{status['cpu']['tempMax']}°C")
            self.cpu_load_label.config(text=f"{status['cpu']['load']:.1f}%")
            self.cpu_load_bar['value'] = status['cpu']['load']
            self.cpu_power_label.config(text=f"{status['cpu']['consume']:.2f}W")
            self.cpu_core1_temp_label.config(text=f"{status['cpu']['core1Temp']}°C")
            self.cpu_tjmax_dist_label.config(text=f"{status['cpu']['core1DistanceToTjMax']}°C")
            self.cpu_tjmax_label.config(text=f"{status['cpu']['tjMax']}°C")
            
            # Update GPU
            self.gpu_temp_label.config(text=f"{status['gpu']['temp']}°C")
            self.gpu_temp_max_label.config(text=f"{status['gpu']['tempMax']}°C")
            self.gpu_load_label.config(text=f"{status['gpu']['load']:.1f}%")
            self.gpu_load_bar['value'] = status['gpu']['load']
            self.gpu_power_label.config(text=f"{status['gpu']['consume']:.2f}W")
            self.gpu_mem_used_label.config(text=f"{status['gpu']['memUsed']:.1f}MB")
            self.gpu_mem_total_label.config(text=f"{status['gpu']['memTotal']:.1f}MB")
            self.gpu_fan_label.config(text=f"{status['gpu']['rpm']:.0f}RPM")
            self.gpu_freq_label.config(text=f"{status['gpu']['freq']:.0f}MHz")
            
            # Update System
            self.mem_percent_label.config(text=f"{status['memory']['percent']:.1f}%")
            self.mem_bar['value'] = status['memory']['percent']
            self.mem_used_label.config(text=f"{status['memory']['used']:.2f}GB")
            self.mem_avail_label.config(text=f"{status['memory']['avail']:.2f}GB")
            
            self.storage_percent_label.config(text=f"{status['storage']['percent']:.1f}%")
            self.storage_bar['value'] = status['storage']['percent']
            self.storage_temp_label.config(text=f"{status['storage']['temp']}°C")
            self.storage_read_label.config(text=f"{status['storage']['read']:.1f}MB")
            self.storage_write_label.config(text=f"{status['storage']['write']:.1f}MB")
            
            self.net_up_label.config(text=f"{status['network']['up']:.1f}MB")
            self.net_down_label.config(text=f"{status['network']['down']:.1f}MB")
            
            # Update Board
            self.board_temp_label.config(text=f"{status['board']['temp']}°C")
            self.board_rpm_label.config(text=f"{status['board']['rpm']:.1f}RPM")
            self.board_tick_label.config(text=f"{status['board']['tick']}")
            self.last_update_label.config(text=time.strftime('%H:%M:%S'))
            
            # Send via serial if connected
            if self.pipe_connected and self.sender:
                self.sender.send_json(status, debug=False)
                self.log_message(f"Sent: CPU={status['cpu']['temp']}°C, GPU={status['gpu']['temp']}°C")
            
            # Update interval
            try:
                self.update_interval = float(self.interval_var.get())
            except:
                pass
                
        except Exception as e:
            self.log_message(f"Error updating status: {e}")
    
    def log_message(self, message):
        """Add message to communication log"""
        timestamp = time.strftime('%H:%M:%S')
        self.serial_log.insert(tk.END, f"[{timestamp}] {message}\n")
        self.serial_log.see(tk.END)
    
    def on_pipe_received(self, data: bytes):
        """Callback for received pipe data"""
        try:
            data_str = data.decode('utf-8', errors='replace')
            self.log_message(f"RX: {data_str}")
        except:
            self.log_message(f"RX: {data.hex()}")
    
    def update_pipe_status(self):
        """Update the pipe status bar"""
        if hasattr(self, 'pipe_status_label'):
            if self.pipe_connected:
                self.pipe_status_label.config(text="Connected", foreground="green")
            else:
                self.pipe_status_label.config(text="Disconnected", foreground="red")
    
    def is_autostart_enabled(self):
        """Check if auto-start is enabled in registry"""
        if not REGISTRY_AVAILABLE or sys.platform != 'win32':
            return False
        try:
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                                r"Software\Microsoft\Windows\CurrentVersion\Run",
                                0, winreg.KEY_READ)
            try:
                value, _ = winreg.QueryValueEx(key, self.startup_key_name)
                winreg.CloseKey(key)
                # Compare the stored value with our app_path
                # Normalize both by removing surrounding quotes and comparing
                value_normalized = value.strip().strip('"')
                app_path_normalized = self.app_path.strip().strip('"')
                # For executable paths, compare absolute paths
                if getattr(sys, 'frozen', False):
                    return os.path.abspath(value_normalized) == os.path.abspath(app_path_normalized)
                else:
                    # For script commands, compare the full command string
                    return value_normalized == app_path_normalized
            except FileNotFoundError:
                winreg.CloseKey(key)
                return False
        except Exception as e:
            # Log error for debugging
            if hasattr(self, 'log_message'):
                self.log_message(f"Error checking auto-start: {e}")
            return False
    
    def toggle_autostart(self):
        """Toggle auto-start on boot"""
        if not REGISTRY_AVAILABLE or sys.platform != 'win32':
            self.log_message("Auto-start not available on this platform")
            return
        
        try:
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                                r"Software\Microsoft\Windows\CurrentVersion\Run",
                                0, winreg.KEY_SET_VALUE)
            
            if self.autostart_var.get():
                # Enable auto-start
                winreg.SetValueEx(key, self.startup_key_name, 0, winreg.REG_SZ, self.app_path)
                self.log_message(f"Auto-start on boot enabled: {self.app_path}")
            else:
                # Disable auto-start
                try:
                    winreg.DeleteValue(key, self.startup_key_name)
                    self.log_message("Auto-start on boot disabled")
                except FileNotFoundError:
                    # Already disabled
                    pass
            
            winreg.CloseKey(key)
        except Exception as e:
            self.log_message(f"Error toggling auto-start: {e}")
            import traceback
            self.log_message(f"Traceback: {traceback.format_exc()}")
    
    def on_closing(self):
        """Handle window closing"""
        self.monitoring = False
        if self.sender:
            self.sender.disconnect()
            self.pipe_connected = False
            self.update_pipe_status()
        self.root.destroy()


def main():
    """Main entry point for GUI application"""
    try:
        root = tk.Tk()
        app = PCStatusGUI(root)
        root.mainloop()
    except Exception as e:
        print(f"Error starting GUI: {e}")
        import traceback
        traceback.print_exc()
        input("Press Enter to exit...")


if __name__ == "__main__":
    main()
