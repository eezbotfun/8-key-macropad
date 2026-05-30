using System.Text.Json.Serialization;

namespace HardwareMonitor.Windows.Models;

internal sealed class PcStatusPayload
{
    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("board")]
    public BoardSection Board { get; set; } = new();

    [JsonPropertyName("cpu")]
    public CpuSection Cpu { get; set; } = new();

    [JsonPropertyName("gpu")]
    public GpuSection Gpu { get; set; } = new();

    [JsonPropertyName("storage")]
    public StorageSection Storage { get; set; } = new();

    [JsonPropertyName("memory")]
    public MemorySection Memory { get; set; } = new();

    [JsonPropertyName("network")]
    public NetworkSection Network { get; set; } = new();

    [JsonPropertyName("cmd")]
    public int Cmd { get; set; }
}

internal sealed class BoardSection
{
    [JsonPropertyName("temp")]
    public double Temp { get; set; }

    [JsonPropertyName("rpm")]
    public double Rpm { get; set; }

    [JsonPropertyName("tick")]
    public int Tick { get; set; }
}

internal sealed class CpuSection
{
    [JsonPropertyName("temp")]
    public double Temp { get; set; }

    [JsonPropertyName("tempMax")]
    public double TempMax { get; set; }

    [JsonPropertyName("load")]
    public double Load { get; set; }

    [JsonPropertyName("consume")]
    public double Consume { get; set; }

    [JsonPropertyName("tjMax")]
    public int TjMax { get; set; }

    [JsonPropertyName("core1DistanceToTjMax")]
    public double Core1DistanceToTjMax { get; set; }

    [JsonPropertyName("core1Temp")]
    public double Core1Temp { get; set; }
}

internal sealed class GpuSection
{
    [JsonPropertyName("temp")]
    public double Temp { get; set; }

    [JsonPropertyName("tempMax")]
    public double TempMax { get; set; }

    [JsonPropertyName("load")]
    public double Load { get; set; }

    [JsonPropertyName("consume")]
    public double Consume { get; set; }

    [JsonPropertyName("rpm")]
    public double Rpm { get; set; }

    [JsonPropertyName("memUsed")]
    public double MemUsed { get; set; }

    [JsonPropertyName("memTotal")]
    public double MemTotal { get; set; }

    [JsonPropertyName("freq")]
    public double Freq { get; set; }
}

internal sealed class StorageSection
{
    [JsonPropertyName("temp")]
    public double Temp { get; set; }

    [JsonPropertyName("read")]
    public double Read { get; set; }

    [JsonPropertyName("write")]
    public double Write { get; set; }

    [JsonPropertyName("percent")]
    public double Percent { get; set; }
}

internal sealed class MemorySection
{
    [JsonPropertyName("used")]
    public double Used { get; set; }

    [JsonPropertyName("avail")]
    public double Avail { get; set; }

    [JsonPropertyName("percent")]
    public double Percent { get; set; }
}

internal sealed class NetworkSection
{
    /// <summary>Uplink speed in kilobytes per second.</summary>
    [JsonPropertyName("up")]
    public double Up { get; set; }

    /// <summary>Downlink speed in kilobytes per second.</summary>
    [JsonPropertyName("down")]
    public double Down { get; set; }

    /// <summary>1 when at least one non-loopback adapter is up, else 0.</summary>
    [JsonPropertyName("linkUp")]
    public double LinkUp { get; set; }
}
