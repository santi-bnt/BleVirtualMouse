using BleVirtualMouse.Services;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Media.Imaging;

public static class AirPlaySmokeTests
{
    public static async Task<int> Run(string root)
    {
        string executable = Path.Combine(root, "tools", "uxplay", "uxplay.exe");
        string runtimeFile = Path.Combine(root, "tools", "uxplay", "runtime.txt");
        if (!File.Exists(executable) || !File.Exists(runtimeFile))
        { Console.WriteLine("SKIP: UxPlay not installed; run setup-airplay.ps1"); return 0; }
        string runtime = File.ReadAllText(runtimeFile).Trim();
        Environment.SetEnvironmentVariable("UXPLAY_RUNTIME", runtime);
        await using var receiver = new AirPlayReceiverService();
        var messages = new List<string>();
        receiver.Log += line => { lock (messages) messages.Add(line); };
        await receiver.Start(executable); await Task.Delay(2500);
        if (!receiver.IsRunning) throw new Exception("UxPlay exited: " + string.Join("\n", messages));
        if (receiver.Status != "esperando") throw new Exception("Idle UxPlay falsely reported video: " + receiver.Status);
        Console.WriteLine("PASS: native UxPlay initialized, still waiting (NO iPhone connection tested)");
        await Task.WhenAll(receiver.Stop(), receiver.Stop());
        if (receiver.IsRunning) throw new Exception("Concurrent stop left UxPlay running.");
        Console.WriteLine("PASS: concurrent UxPlay stop serialized; no owned receiver left running");
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var start = new ProcessStartInfo(Path.Combine(runtime, "gst-launch-1.0.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        start.Environment["PATH"] = runtime + ";" + Environment.GetEnvironmentVariable("PATH");
        foreach (string arg in new[] { "-q", "videotestsrc", "num-buffers=5", "!", "video/x-raw,width=64,height=128,framerate=30/1", "!", "videoconvert", "!", "jpegenc", "quality=80", "!", "multipartmux", "boundary=iphoneframe", "!", "tcpclientsink", "host=127.0.0.1", $"port={port}", "sync=false" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync(); var stdout = process.StandardOutput.ReadToEndAsync();
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var client = await listener.AcceptTcpClientAsync(cancel.Token);
            for (int i = 0; i < 5; i++)
            {
                byte[] bytes = await MultipartFrameReader.Read(client.GetStream(), cancel.Token);
                using var memory = new MemoryStream(bytes);
                var decoder = BitmapDecoder.Create(memory, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                if (decoder.Frames[0].PixelWidth != 64 || decoder.Frames[0].PixelHeight != 128) throw new Exception("Wrong JPEG dimensions.");
            }
            await process.WaitForExitAsync(cancel.Token);
            if (process.ExitCode != 0) throw new Exception(await error);
            Console.WriteLine("PASS: actual GStreamer -> local TCP -> multipart parser -> JPEG decoding, 5 synthetic frames");
        }
        finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } }
        await stdout;
        return 3;
    }
}
