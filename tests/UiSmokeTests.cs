using BleVirtualMouse;
using BleVirtualMouse.Controls;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

public static class UiSmokeTests
{
    public static Task<int> Run(string root)
    {
        var finished = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                string output = Path.Combine(root, "artifacts", "ui-smoke");
                Directory.CreateDirectory(output); Environment.SetEnvironmentVariable("BLE_MOUSE_ROOT", output);
                var app = new Application(); var window = new MainWindow();
                window.Closed += (_, _) => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                window.Show();
                window.Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        ((Button)window.FindName("EmergencyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await Task.Delay(150);
                        if (!window.IsEmergencyStopped) throw new Exception("Emergency button did not latch.");
                        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape)
                        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                        window.RaiseEvent(key); await Task.Delay(150);
                        if (!key.Handled || !window.IsEmergencyStopped) throw new Exception("ESC route not handled.");
                        var screen = (ScreenView)window.FindName("PhoneScreen");
                        byte[] pixels = new byte[64 * 128 * 4];
                        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 110; pixels[i + 1] = 200; pixels[i + 2] = 40; pixels[i + 3] = 255; }
                        screen.SetFrame(BitmapSource.Create(64, 128, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4));
                        window.UpdateLayout();
                        var video = new RenderTargetBitmap((int)screen.ActualWidth, (int)screen.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        video.Render(screen);
                        byte[] captured = new byte[video.PixelWidth * video.PixelHeight * 4]; video.CopyPixels(captured, video.PixelWidth * 4, 0);
                        int center = ((video.PixelHeight / 2) * video.PixelWidth + video.PixelWidth / 2) * 4;
                        if (captured[center + 1] < 150 || captured[0] > 40) throw new Exception("ScreenView video/letterbox pixel check failed.");
                        Console.WriteLine("PASS: ScreenView nonblank frame and letterbox pixel checks (synthetic image)");
                        screen.ShowDebug = true; screen.DebugText = "Synthetic rendering test\nNot an iPhone connection";
                        foreach (var size in new[] { (1100, 820), (820, 660) })
                        {
                            window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout();
                            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var stream = File.Create(Path.Combine(output, $"window-{size.Item1}.png")); encoder.Save(stream);
                        }
                        Console.WriteLine("PASS: WPF Emergency button, ESC routing, desktop/minimum-size rendering (in-process smoke test)");
                        finished.TrySetResult(2);
                    }
                    catch (Exception ex) { finished.TrySetException(ex); }
                    finally { window.Close(); }
                }));
                Dispatcher.Run();
            }
            catch (Exception ex) { finished.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return finished.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
