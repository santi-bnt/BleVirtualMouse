using System.IO;

namespace BleVirtualMouse.Services;

public sealed class ControllerLog
{
    private readonly object _sync = new();
    public string Root { get; } = Environment.GetEnvironmentVariable("BLE_MOUSE_ROOT") ?? Directory.GetCurrentDirectory();
    public event Action<string>? Line;
    public ControllerLog()
    {
        Directory.CreateDirectory(Path.Combine(Root, "logs"));
        File.WriteAllText(Path.Combine(Root, "logs", "latest.log"), "");
    }
    public void Write(string category, string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {message}";
        lock (_sync) File.AppendAllText(Path.Combine(Root, "logs", "latest.log"), line + Environment.NewLine);
        Line?.Invoke(line);
    }
}
