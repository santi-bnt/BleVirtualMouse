using System.IO;
using System.Text.Json.Serialization;

namespace BleVirtualMouse.Controllers;

public class PointerOptions
{
    [JsonPropertyName("pointerStep")] public int PointerStep { get; set; } = 24;
    [JsonPropertyName("pointerDelayMs")] public int PointerDelayMs { get; set; } = 20;
    [JsonPropertyName("homingIterations")] public int HomingIterations { get; set; } = 96;
    [JsonPropertyName("tapDelayMs")] public int TapDelayMs { get; set; } = 40;
    [JsonPropertyName("dragUpdateHz")] public int DragUpdateHz { get; set; } = 50;
    [JsonPropertyName("pointerMode")] public string PointerMode { get; set; } = "Precise";
    public void Validate()
    {
        if (PointerStep < 1 || PointerStep > 127 || PointerDelayMs < 20 || PointerDelayMs > 1000
            || HomingIterations < 1 || HomingIterations > 1000 || TapDelayMs < 20 || TapDelayMs > 2000
            || DragUpdateHz < 30 || DragUpdateHz > 60 || (PointerMode != "Fast" && PointerMode != "Precise"))
            throw new InvalidDataException("Opciones invalidas: step 1..127, delay 20..1000 ms, homing 1..1000, tap 20..2000 ms, Hz 30..60, modo Fast/Precise.");
    }
}
