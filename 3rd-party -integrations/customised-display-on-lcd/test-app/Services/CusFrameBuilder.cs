using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CusProtocolTester.Services;

/// <summary>
/// Builds <c>cus</c> + big-endian UInt16 length + UTF-8 JSON per docs/host_usb_cdc_customised.md
/// </summary>
public static class CusFrameBuilder
{
    public const int MaxPayloadLen = 2048;
    public const int MaxDeviceReassemblyPayload = 2043;

    /// <summary>Options used for <see cref="CusPayload"/> and <see cref="BuildFrame(JsonNode, out string?)"/>.</summary>
    public static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static byte[] BuildFrame(ReadOnlySpan<byte> utf8Payload, out string? warning)
    {
        warning = null;
        if (utf8Payload.Length < 1 || utf8Payload.Length > MaxPayloadLen)
        {
            throw new ArgumentOutOfRangeException(nameof(utf8Payload),
                $"Payload length must be between 1 and {MaxPayloadLen} bytes.");
        }

        if (utf8Payload.Length > MaxDeviceReassemblyPayload)
        {
            warning =
                $"Payload length {utf8Payload.Length} exceeds typical device reassembly limit ({MaxDeviceReassemblyPayload}); transmission may fail.";
        }

        var frame = new byte[3 + 2 + utf8Payload.Length];
        frame[0] = (byte)'c';
        frame[1] = (byte)'u';
        frame[2] = (byte)'s';
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3), (ushort)utf8Payload.Length);
        utf8Payload.CopyTo(frame.AsSpan(5));
        return frame;
    }

    public static byte[] BuildFrame(CusPayload payload, out string? warning)
    {
        var utf8 = JsonSerializer.SerializeToUtf8Bytes(payload, PayloadJsonOptions);
        return BuildFrame(utf8, out warning);
    }

    /// <summary>Serializes a JSON object (e.g. merged payload + optional border fields) to a frame.</summary>
    public static byte[] BuildFrame(JsonNode root, out string? warning)
    {
        var utf8 = JsonSerializer.SerializeToUtf8Bytes(root, PayloadJsonOptions);
        return BuildFrame(utf8, out warning);
    }

    public static byte[] BuildFrameFromJsonText(string json, out string? warning)
    {
        var utf8 = Encoding.UTF8.GetBytes(json);
        return BuildFrame(utf8, out warning);
    }
}

/// <summary>JSON object keys match firmware customised_app_handle_usb_json expectations.</summary>
public sealed class CusPayload
{
    public int x { get; set; }
    public int y { get; set; }
    public int w { get; set; } = 480;
    public int h { get; set; } = 200;
    public string text { get; set; } = "";
    public string fg { get; set; } = "#FFFFFF";
    public string bg { get; set; } = "#000000";
    public string align { get; set; } = "LEFT";

    [JsonPropertyName("long_mode")]
    public string long_mode { get; set; } = "WRAP";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? cmd { get; set; }

    public bool activate { get; set; } = true;

    [JsonPropertyName("clear_canvas")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool clear_canvas { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool fullscreen { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? image { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CusLedsPayload? leds { get; set; }
}

public sealed class CusLedsPayload
{
    public bool on { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string?[]? keys { get; set; }
}

/// <summary>Helpers for layout:grid setup/update frames.</summary>
public static class CusGridExamples
{
    public static readonly string[] SymbolTokens =
    {
        "ok", "play", "pause", "wifi", "battery_full", "settings", "home", "refresh",
        "left", "right", "up", "down", "warning", "audio", "close",
    };

    public static JsonObject BuildDefaultSetup(bool fullscreen = true)
    {
        var widgets = new JsonArray();
        for (var c = 0; c < 4; c++)
        {
            widgets.Add(new JsonObject
            {
                ["id"] = c,
                ["type"] = "text",
                ["row"] = 0,
                ["col"] = c,
                ["align"] = "CENTER",
                ["text"] = $"A{c + 1}",
            });
        }

        widgets.Add(new JsonObject
        {
            ["id"] = 4,
            ["type"] = "text",
            ["row"] = 1,
            ["col"] = 0,
            ["col_span"] = 4,
            ["align"] = "LEFT",
            ["long_mode"] = "SCROLL_CIRCULAR",
            ["text"] = "Rolling status line — updates can replace or append",
        });

        for (var c = 0; c < 4; c++)
        {
            widgets.Add(new JsonObject
            {
                ["id"] = 5 + c,
                ["type"] = "text",
                ["row"] = 2,
                ["col"] = c,
                ["align"] = "CENTER",
                ["text"] = $"B{c + 1}",
            });
        }

        return new JsonObject
        {
            ["cmd"] = "start",
            ["layout"] = "grid",
            ["fullscreen"] = fullscreen,
            ["grid"] = new JsonObject
            {
                ["cols"] = new JsonArray { "*", "*", "*", "*" },
                ["rows"] = new JsonArray { "40", "*", "40" },
                ["gap"] = 4,
                ["pad"] = 6,
            },
            ["widgets"] = widgets,
        };
    }

    public static JsonObject BuildSetUpdate(int id, string text, string textOp = "replace", string? fg = null)
    {
        var item = new JsonObject
        {
            ["id"] = id,
            ["text"] = text,
            ["text_op"] = textOp,
        };
        if (!string.IsNullOrWhiteSpace(fg))
            item["fg"] = fg;

        return new JsonObject
        {
            ["cmd"] = "update",
            ["layout"] = "grid",
            ["set"] = new JsonArray(item),
        };
    }
}
