using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BleVirtualMouse.Services;

public sealed class Esp32MouseClient : IDisposable
{
    private const int BaudRate = 115200;
    private readonly object _sync = new();
    private readonly StringBuilder _receiveBuffer = new();
    private SerialPort? _serialPort;
    private TaskCompletionSource<bool>? _pongWaiter;
    private bool _firmwareVerified;

    public event EventHandler<string>? Log;
    public event EventHandler? StatusChanged;

    public bool IsPortOpen => _serialPort?.IsOpen == true;
    public bool IsFirmwareVerified => _firmwareVerified && IsPortOpen;
    public bool IsEsp32Connected { get; private set; }
    public bool IsIphonePaired { get; private set; }
    public bool IsInputSubscribed { get; private set; }
    public bool HasStoredBond { get; private set; }
    public string? PortName => _serialPort?.PortName;

    public static IReadOnlyList<string> GetAvailablePorts() =>
        SerialPort.GetPortNames().OrderBy(port => port, StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task ConnectAsync(string portName)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new ArgumentException("Selecciona un puerto COM.", nameof(portName));
        }

        Disconnect();

        SerialPort port = new(portName, BaudRate, Parity.None, 8, StopBits.One)
        {
            Encoding = Encoding.ASCII,
            NewLine = "\n",
            ReadTimeout = 500,
            WriteTimeout = 1000,
            DtrEnable = false,
            RtsEnable = false
        };

        TaskCompletionSource<bool> pongWaiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pongWaiter = pongWaiter;
        _serialPort = port;
        port.DataReceived += OnDataReceived;

        try
        {
            port.Open();
            StatusChanged?.Invoke(this, EventArgs.Empty);
            WriteLog($"Puerto {portName} abierto a {BaudRate} baud.");

            for (int attempt = 1; attempt <= 6 && !pongWaiter.Task.IsCompleted; attempt++)
            {
                SendRaw("PING", logCommand: false);
                await Task.WhenAny(pongWaiter.Task, Task.Delay(700));
            }

            if (!pongWaiter.Task.IsCompleted)
            {
                throw new IOException($"El dispositivo en {portName} no respondio como BleVirtualMouse ESP32.");
            }

            _firmwareVerified = true;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            WriteLog("ESP32 verificado correctamente.");
            SendRaw("STATUS");
        }
        catch
        {
            Disconnect();
            throw;
        }
    }

    public Task RequestStatusAsync() => SendCommandAsync("STATUS");
    public Task MoveAsync(int x, int y) => SendCommandAsync($"MOVE {x} {y}");
    public Task ClickAsync(string button) => SendCommandAsync($"CLICK {button.ToUpperInvariant()}");
    public Task ScrollAsync(int amount) => SendCommandAsync($"SCROLL {amount}");
    public Task SetButtonAsync(string button, bool pressed) =>
        SendCommandAsync($"BUTTON {button.ToUpperInvariant()} {(pressed ? "DOWN" : "UP")}");

    public Task ClearBondsAsync() => SendCommandAsync("CLEAR_BONDS");

    public Task SendCommandAsync(string command)
    {
        if (!_firmwareVerified)
        {
            throw new InvalidOperationException("Conecta y verifica primero el ESP32.");
        }

        SendRaw(command);
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        SerialPort? port = _serialPort;
        _serialPort = null;
        _firmwareVerified = false;
        _pongWaiter = null;
        lock (_receiveBuffer)
        {
            _receiveBuffer.Clear();
        }

        if (port is not null)
        {
            try
            {
                port.DataReceived -= OnDataReceived;
                if (port.IsOpen)
                {
                    port.Close();
                }
            }
            finally
            {
                port.Dispose();
            }
        }

        IsEsp32Connected = false;
        IsIphonePaired = false;
        IsInputSubscribed = false;
        HasStoredBond = false;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Disconnect();

    private void SendRaw(string command, bool logCommand = true)
    {
        lock (_sync)
        {
            if (_serialPort?.IsOpen != true)
            {
                throw new InvalidOperationException("El puerto del ESP32 no esta abierto.");
            }

            _serialPort.WriteLine(command);
        }

        if (logCommand)
        {
            WriteLog("PC -> ESP32: " + command);
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            string received;
            lock (_sync)
            {
                if (_serialPort?.IsOpen != true)
                {
                    return;
                }

                received = _serialPort.ReadExisting();
            }

            List<string> lines = [];
            lock (_receiveBuffer)
            {
                _receiveBuffer.Append(received.Replace("\r", string.Empty, StringComparison.Ordinal));
                while (true)
                {
                    string buffered = _receiveBuffer.ToString();
                    int newline = buffered.IndexOf('\n');
                    if (newline < 0)
                    {
                        break;
                    }

                    lines.Add(buffered[..newline].Trim());
                    _receiveBuffer.Remove(0, newline + 1);
                }
            }

            foreach (string line in lines.Where(line => line.Length > 0))
            {
                ProcessLine(line);
            }
        }
        catch (Exception ex)
        {
            WriteLog("Error leyendo el ESP32: " + ex.Message);
        }
    }

    private void ProcessLine(string line)
    {
        WriteLog("ESP32 -> PC: " + line);

        if (line.StartsWith("PONG BleVirtualMouseESP32", StringComparison.Ordinal))
        {
            _pongWaiter?.TrySetResult(true);
            return;
        }

        if (line.StartsWith("STATUS ", StringComparison.Ordinal))
        {
            Dictionary<string, string> values = line[7..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

            IsEsp32Connected = values.TryGetValue("connected", out string? connected) && connected == "1";
            IsIphonePaired = values.TryGetValue("paired", out string? paired) && paired == "1";
            HasStoredBond = values.TryGetValue("bonded", out string? bonded) && bonded == "1";
            IsInputSubscribed = values.TryGetValue("subscribed", out string? subscribed) && subscribed == "1";
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (line == "EVENT CONNECTED")
        {
            IsEsp32Connected = true;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (line is "EVENT DISCONNECTED" or "EVENT UNPAIRED")
        {
            IsEsp32Connected = line != "EVENT DISCONNECTED" && IsEsp32Connected;
            IsIphonePaired = false;
            IsInputSubscribed = false;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (line == "EVENT PAIRED")
        {
            IsEsp32Connected = true;
            IsIphonePaired = true;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (line is "EVENT SUBSCRIBED" or "EVENT UNSUBSCRIBED")
        {
            IsInputSubscribed = line == "EVENT SUBSCRIBED";
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void WriteLog(string message) => Log?.Invoke(this, message);
}
