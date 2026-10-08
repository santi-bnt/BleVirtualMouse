using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace BleVirtualMouse.Services;

public sealed class AirPlayReceiverService : IAsyncDisposable
{
    private Process? _process;
    private TcpListener? _listener;
    private CancellationTokenSource? _cancel;
    private Task? _reader;
    private DateTime _lastFrame;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly System.Windows.Threading.DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromSeconds(1) };
    public string Status { get; private set; } = "desconectado";
    public string Password { get; private set; } = "";
    public bool IsRunning => _process is { HasExited: false };
    public bool HasVideo => Status == "recibiendo video";
    public double Fps { get; private set; }
    public event Action<string>? Log;
    public event Action? Changed;
    public event Action<BitmapSource>? Frame;

    public AirPlayReceiverService()
    {
        _watchdog.Tick += (_, _) =>
        {
            if (IsRunning && HasVideo && DateTime.UtcNow - _lastFrame > TimeSpan.FromSeconds(5))
            { Fps = 0; SetStatus("sin video / esperando reconexion"); }
            if (_process is { HasExited: true }) { Fps = 0; SetStatus("desconectado"); }
        };
    }
    private void SetStatus(string status)
    {
        if (Status == status) return;
        Status = status; Log?.Invoke(status); Changed?.Invoke();
    }

    public async Task Start(string executable)
    {
        await _lifecycle.WaitAsync();
        try { await StartCore(executable); }
        finally { _lifecycle.Release(); }
    }
    private async Task StartCore(string executable)
    {
        if (IsRunning) return;
        if (_process is not null) await StopCore();
        if (!File.Exists(executable)) throw new FileNotFoundException("Falta UxPlay. Ejecuta setup-airplay.ps1.", executable);
        _cancel = new();
        _listener = new(IPAddress.Loopback, 0);
        _listener.Start();
        int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Password = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (var argument in new[] { "-n", "iPhone Controller", "-pw", Password, "-p", "7000", "-vsync", "no",
            "-as", "fakesink", "-avdec", "-vs",
            $"jpegenc quality=80 ! multipartmux boundary=iphoneframe ! tcpclientsink host=127.0.0.1 port={port}" })
            start.ArgumentList.Add(argument);
        string? runtime = File.Exists(Path.Combine(start.WorkingDirectory, "libgstreamer-1.0-0.dll"))
            ? start.WorkingDirectory : Environment.GetEnvironmentVariable("UXPLAY_RUNTIME");
        if (runtime is not null) start.Environment["PATH"] = runtime + ";" + Environment.GetEnvironmentVariable("PATH");
        _process = new() { StartInfo = start };
        string password = Password;
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) Log?.Invoke(e.Data.Replace(password, "[oculto]")); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Log?.Invoke(e.Data.Replace(password, "[oculto]")); };
        try
        {
            _process.Start(); _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
            var listener = _listener; var token = _cancel.Token;
            _reader = Task.Run(() => Receive(listener, token));
            SetStatus("esperando"); _watchdog.Start(); Changed?.Invoke();
        }
        catch { _listener.Stop(); _cancel.Cancel(); _process.Dispose(); _process = null; throw; }
    }

    private async Task Receive(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(token);
                using var stream = client.GetStream();
                int frames = 0; var clock = Stopwatch.StartNew();
                while (!token.IsCancellationRequested)
                {
                    var bytes = await MultipartFrameReader.Read(stream, token);
                    if (bytes.Length < 2 || bytes[0] != 0xff || bytes[1] != 0xd8) throw new InvalidDataException("Se esperaba JPEG.");
                    using var memory = new MemoryStream(bytes);
                    var decoder = BitmapDecoder.Create(memory, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    var info = decoder.Frames[0];
                    if (info.PixelWidth > 8192 || info.PixelHeight > 8192 || (long)info.PixelWidth * info.PixelHeight > 16000000)
                        throw new InvalidDataException("Video demasiado grande.");
                    memory.Position = 0;
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = memory;
                    bitmap.DecodePixelWidth = Math.Min(1080, info.PixelWidth);
                    bitmap.EndInit(); bitmap.Freeze();
                    if (bitmap.PixelWidth > 8192 || bitmap.PixelHeight > 8192) throw new InvalidDataException("Video demasiado grande.");
                    _lastFrame = DateTime.UtcNow;
                    if (!HasVideo) { SetStatus("iPhone conectado"); SetStatus("recibiendo video"); }
                    frames++;
                    if (clock.Elapsed.TotalSeconds >= 1)
                    { Fps = frames / clock.Elapsed.TotalSeconds; frames = 0; clock.Restart(); Changed?.Invoke(); }
                    Frame?.Invoke(bitmap);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (!token.IsCancellationRequested)
            { Fps = 0; SetStatus("desconectado / esperando"); Log?.Invoke("ERROR video: " + ex.Message); }
            catch { break; }
        }
    }
    public async Task Stop()
    {
        await _lifecycle.WaitAsync();
        try { await StopCore(); }
        finally { _lifecycle.Release(); }
    }
    private async Task StopCore()
    {
        _watchdog.Stop();
        _cancel?.Cancel(); _listener?.Stop();
        if (_process is not null)
        {
            if (!_process.HasExited) { _process.Kill(entireProcessTree: true); await _process.WaitForExitAsync(); }
            _process.Dispose(); _process = null;
        }
        if (_reader is not null) await _reader;
        _reader = null; _listener = null; _cancel?.Dispose(); _cancel = null;
        Password = ""; Fps = 0; SetStatus("desconectado"); Changed?.Invoke();
    }
    public async ValueTask DisposeAsync() => await Stop();
}

public static class MultipartFrameReader
{
    public static async Task<byte[]> Read(Stream stream, CancellationToken token)
    {
        int? length = null;
        int headerBytes = 0;
        while (true)
        {
            string line = await ReadLine(stream, token);
            headerBytes += line.Length + 2;
            if (headerBytes > 8192) throw new InvalidDataException("Cabecera multipart demasiado larga.");
            if (line.Length == 0 && length.HasValue) break;
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(line.AsSpan(15).Trim(), out int value) || value < 1 || value > 8 * 1024 * 1024)
                    throw new InvalidDataException("Longitud JPEG invalida.");
                length = value;
            }
        }
        byte[] bytes = new byte[length!.Value];
        await stream.ReadExactlyAsync(bytes, token);
        return bytes;
    }
    private static async Task<string> ReadLine(Stream stream, CancellationToken token)
    {
        using var memory = new MemoryStream();
        byte[] one = new byte[1];
        while (memory.Length < 4096)
        {
            await stream.ReadExactlyAsync(one, token);
            if (one[0] == 10) return Encoding.ASCII.GetString(memory.ToArray()).TrimEnd('\r');
            memory.WriteByte(one[0]);
        }
        throw new InvalidDataException("Linea multipart demasiado larga.");
    }
}
