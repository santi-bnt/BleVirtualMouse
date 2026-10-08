using BleVirtualMouse.Controllers;
using BleVirtualMouse.Services;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BleVirtualMouse;

public partial class MainWindow : Window
{
    private readonly Esp32MouseClient _client = new();
    private readonly ControllerLog _log = new();
    private readonly IPhoneCoordinateMapper _mapper = new();
    private readonly AirPlayReceiverService _airplay = new();
    private readonly AutomationController _automation = new();
    private readonly BluetoothMouseService _mouse;
    private readonly IPhonePointerController _pointer;
    private bool _connecting, _closing, _closed, _pointerHeld, _pointerBusy, _wasReady, _calibrating;
    private CursorPoint _latestDrag;
    private BitmapSource? _latestFrame;
    private int _framePending;
    private string ConfigPath => Path.Combine(_log.Root, "config.json");
    public bool IsEmergencyStopped => _pointer.EmergencyStopped;

    public MainWindow()
    {
        InitializeComponent();
        _mouse = new(_client); _pointer = new(_mouse, _mapper);
        _log.Line += line => Ui(() => { if (LogTextBox.Text.Length > 80000) LogTextBox.Clear(); LogTextBox.AppendText(line + Environment.NewLine); LogTextBox.ScrollToEnd(); });
        _client.Log += (_, line) => _log.Write("BLE", line);
        _client.StatusChanged += (_, _) => Ui(() =>
        {
            bool ready = _mouse.IsReady;
            if (_wasReady && !ready) { _mapper.Invalidate(); _ = RunUiAsync(StopEverything); }
            if (!_wasReady && ready && _pointer.EmergencyStopped) _ = RunUiAsync(StopEverything);
            _wasReady = ready; UpdateStatus();
        });
        _pointer.Log += (category, line) => _log.Write(category, line);
        _pointer.Changed += () => Ui(UpdateStatus);
        _airplay.Log += line => _log.Write("AIRPLAY", line);
        _airplay.Changed += () => Ui(() =>
        {
            if (!_airplay.HasVideo) { PhoneScreen.SetFrame(null); if (_pointerHeld || _pointer.IsBusy) _ = RunUiAsync(StopEverything); }
            UpdateStatus();
        });
        _airplay.Frame += frame =>
        {
            Interlocked.Exchange(ref _latestFrame, frame);
            if (_closed || Interlocked.Exchange(ref _framePending, 1) != 0) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                Interlocked.Exchange(ref _framePending, 0);
                var latest = Interlocked.Exchange(ref _latestFrame, null);
                if (_closed || !_airplay.HasVideo || latest is null) return;
                if (PhoneScreen.SetFrame(latest)) { _mapper.Invalidate(); _ = RunUiAsync(StopEverything); }
                if (!_calibrating && _mapper.Profile.VideoAspect > 0 && Math.Abs(_mapper.Profile.VideoAspect - PhoneScreen.Aspect) > .02)
                    _mapper.Invalidate();
                UpdateStatus();
            }));
        };
        PhoneScreen.Pressed += point =>
        {
            if (_pointerBusy || _pointer.IsBusy || !CanControlScreen) return;
            _pointerHeld = true; _latestDrag = point; _ = RunUiAsync(() => PointerSession(point));
        };
        PhoneScreen.Dragged += point => _latestDrag = point;
        PhoneScreen.Released += () => _pointerHeld = false;
        PhoneScreen.Scrolled += amount => { if (CanControlScreen) _ = RunUiAsync(() => _pointer.ScrollAsync(amount)); };
        PhoneScreen.PointerChanged += UpdateDebug;
        try { _mapper.Load(ConfigPath); _mapper.Save(ConfigPath); } catch (Exception ex) { _log.Write("ERROR", ex.ToString()); }
        RefreshPorts(); UpdateStatus(); _log.Write("INPUT", "Aplicacion iniciada. Automatizacion deshabilitada.");
    }
    private bool CanControlScreen => !_calibrating && _mouse.IsReady && _airplay.HasVideo && _mapper.IsCalibrated && _pointer.Homed && !_pointer.EmergencyStopped;
    private void Ui(Action action) { if (!_closed && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => { if (!_closed) action(); })); }
    private async Task PointerSession(CursorPoint start)
    {
        _pointerBusy = true;
        UpdateStatus();
        var windowPoint = PhoneScreen.LastWindowPoint;
        _log.Write("INPUT", $"window=({windowPoint.X:F1},{windowPoint.Y:F1}) normalized=({start.X:F3},{start.Y:F3})");
        try
        {
            await _pointer.BeginDragAsync(start.X, start.Y);
            var last = start;
            while (_pointerHeld && CanControlScreen)
            {
                var next = _latestDrag;
                if (next != last) { await _pointer.UpdateDragAsync(next.X, next.Y); last = next; }
                await Task.Delay(20);
            }
            if (CanControlScreen && last != _latestDrag)
                await _pointer.UpdateDragAsync(_latestDrag.X, _latestDrag.Y);
        }
        finally { await _pointer.EndDragAsync(); _pointerHeld = false; _pointerBusy = false; }
    }
    private async Task StopEverything()
    {
        _pointerHeld = false; PhoneScreen.ReleaseMouseCapture(); _automation.Disable();
        await _pointer.EmergencyStopAsync(); UpdateStatus();
    }
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closing) { e.Cancel = !_closed; base.OnClosing(e); return; }
        e.Cancel = true; _closing = true;
        IsEnabled = false;
        await Task.Yield();
        await RunUiAsync(StopEverything); await RunUiAsync(_airplay.Stop);
        _closed = true; _client.Dispose(); Close();
    }
    private async void Emergency_Click(object sender, RoutedEventArgs e) => await RunUiAsync(StopEverything);
    private async void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; await RunUiAsync(StopEverything); } }
    private async void Resume_Click(object sender, RoutedEventArgs e) => await RunUiAsync(_pointer.ResumeAsync);
    private void Debug_Changed(object sender, RoutedEventArgs e) { if (PhoneScreen is not null) { PhoneScreen.ShowDebug = DebugToggle.IsChecked == true; UpdateDebug(); } }
    private void UpdateDebug()
    {
        var p = PhoneScreen.LastNormalized;
        var target = p.HasValue && _mapper.IsCalibrated ? _mapper.MapPoint(p.Value.X, p.Value.Y) : (CursorPoint?)null;
        PhoneScreen.DebugText = $"window=({PhoneScreen.LastWindowPoint.X:F1},{PhoneScreen.LastWindowPoint.Y:F1})\nnormalized={(p.HasValue ? $"({p.Value.X:F3},{p.Value.Y:F3})" : "--")}\nBLE target={(target.HasValue ? $"({target.Value.X:F0},{target.Value.Y:F0})" : "--")}\nFPS recibido={_airplay.Fps:F1}\nBLE={_mouse.IsReady}\nAirPlay={_airplay.Status}";
        PhoneScreen.InvalidateVisual();
    }
    private async void StartMirror_Click(object sender, RoutedEventArgs e) => await RunUiAsync(async () =>
    {
        string executable = Environment.GetEnvironmentVariable("UXPLAY_EXE") ?? Path.Combine(_log.Root, "tools", "uxplay", "uxplay.exe");
        string runtimeFile = Path.Combine(_log.Root, "tools", "uxplay", "runtime.txt");
        if (File.Exists(runtimeFile)) Environment.SetEnvironmentVariable("UXPLAY_RUNTIME", File.ReadAllText(runtimeFile).Trim());
        await _airplay.Start(executable); UpdateStatus();
    });
    private async void StopMirror_Click(object sender, RoutedEventArgs e) => await RunUiAsync(async () => { await StopEverything(); await _airplay.Stop(); });
    private void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        if (_pointer.IsBusy || _pointerBusy) { _log.Write("ERROR", "Termina el gesto antes de calibrar."); return; }
        _calibrating = true; CalibrationPanel.Visibility = Visibility.Visible; UpdateStatus();
    }
    private void Anchor_Click(object sender, RoutedEventArgs e)
    {
        if (!_mouse.IsReady || _pointer.EmergencyStopped || _pointer.IsBusy || _pointerBusy) { _log.Write("ERROR", "Conecta BLE, reanuda y termina el gesto antes de sincronizar."); return; }
        _mapper.AnchorTopLeft(); _log.Write("INPUT", "Referencia arriba-izquierda confirmada por usuario."); UpdateStatus();
    }
    private void SaveCalibration_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_pointerBusy || _pointer.IsBusy || !_mouse.IsReady) throw new InvalidOperationException("Espera al gesto y verifica BLE.");
            _mapper.SaveBottomRight(ConfigPath, PhoneScreen.Aspect);
            _calibrating = false; CalibrationPanel.Visibility = Visibility.Collapsed; _log.Write("INPUT", "Calibracion guardada: " + ConfigPath); UpdateStatus();
        }
        catch (Exception ex) { _log.Write("ERROR", ex.Message); }
    }
    private void UseCalibration_Click(object sender, RoutedEventArgs e)
    {
        if (!_mapper.IsCalibrated || !_pointer.Homed || _pointer.IsBusy || _pointer.EmergencyStopped)
        { _log.Write("ERROR", "Necesitas una calibracion guardada y la referencia arriba-izquierda."); return; }
        if (_mapper.Profile.VideoAspect > 0 && PhoneScreen.Aspect > 0 && Math.Abs(_mapper.Profile.VideoAspect - PhoneScreen.Aspect) > .02)
        { _log.Write("ERROR", "Cambio de orientacion: guarda ambas esquinas nuevamente."); return; }
        _calibrating = false; CalibrationPanel.Visibility = Visibility.Collapsed;
        _log.Write("INPUT", "Calibracion existente reutilizada con nueva referencia."); UpdateStatus();
    }
    private void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();
    private void RefreshPorts()
    {
        string? previous = PortComboBox.SelectedItem as string;
        var ports = Esp32MouseClient.GetAvailablePorts(); PortComboBox.ItemsSource = ports;
        PortComboBox.SelectedItem = previous is not null && ports.Contains(previous) ? previous : ports.FirstOrDefault();
    }
    private async void ConnectEsp32_Click(object sender, RoutedEventArgs e)
    {
        _connecting = true; UpdateStatus();
        try { await RunUiAsync(() => _client.ConnectAsync(PortComboBox.SelectedItem as string ?? "")); }
        finally { _connecting = false; UpdateStatus(); }
    }
    private async void DisconnectEsp32_Click(object sender, RoutedEventArgs e) => await RunUiAsync(async () => { await StopEverything(); _client.Disconnect(); UpdateStatus(); });
    private async void ClearBonds_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Borrar los vinculos de la ESP32 y reiniciarla?", "Confirmar", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            await RunUiAsync(async () => { await StopEverything(); await _client.ClearBondsAsync(); });
    }
    private async void MoveUp_Click(object sender, RoutedEventArgs e) => await RunUiAsync(() => _pointer.JogAsync(0, -12));
    private async void MoveDown_Click(object sender, RoutedEventArgs e) => await RunUiAsync(() => _pointer.JogAsync(0, 12));
    private async void MoveLeft_Click(object sender, RoutedEventArgs e) => await RunUiAsync(() => _pointer.JogAsync(-12, 0));
    private async void MoveRight_Click(object sender, RoutedEventArgs e) => await RunUiAsync(() => _pointer.JogAsync(12, 0));
    private async void LeftClick_Click(object sender, RoutedEventArgs e) => await RunUiAsync(_pointer.ClickAsync);
    private async void RightClick_Click(object sender, RoutedEventArgs e) => await RunUiAsync(_pointer.RightClickAsync);
    private async void ScrollUp_Click(object sender, RoutedEventArgs e) => await RunUiAsync(() => _pointer.ScrollAsync(3));
    private async void ScrollDown_Click(object sender, RoutedEventArgs e) => await RunUiAsync(() => _pointer.ScrollAsync(-3));
    private async void TapCenter_Click(object sender, RoutedEventArgs e) => await RunUiAsync(() => _pointer.TapNormalizedAsync(.5, .5));
    private async void SwipeUp_Click(object sender, RoutedEventArgs e) => await RunUiAsync(() => _pointer.DragNormalizedAsync(.5, .8, .5, .2, 400));
    private async void SwipeDown_Click(object sender, RoutedEventArgs e) => await RunUiAsync(() => _pointer.DragNormalizedAsync(.5, .2, .5, .8, 400));
    private async void SwipeLeft_Click(object sender, RoutedEventArgs e) => await RunUiAsync(() => _pointer.DragNormalizedAsync(.8, .5, .2, .5, 400));
    private async void SwipeRight_Click(object sender, RoutedEventArgs e) => await RunUiAsync(() => _pointer.DragNormalizedAsync(.2, .5, .8, .5, 400));
    private async void Home_Click(object sender, RoutedEventArgs e) => await RunUiAsync(_pointer.HomePointerAsync);
    private async void PositionTest_Click(object sender, RoutedEventArgs e) => await RunUiAsync(_pointer.PositionTestAsync);
    private async void DragTest_Click(object sender, RoutedEventArgs e) => await RunUiAsync(_pointer.DragTestAsync);
    private async void ScrollTest_Click(object sender, RoutedEventArgs e) => await RunUiAsync(_pointer.ScrollTestAsync);
    private async void EmergencyTest_Click(object sender, RoutedEventArgs e) => await RunUiAsync(_pointer.EmergencyTestAsync);
    private async void DriftTest_Click(object sender, RoutedEventArgs e) => await RunUiAsync(_pointer.DriftTestAsync);
    private void UpdateStatus()
    {
        MouseControls.IsEnabled = _mouse.IsReady && !_pointer.EmergencyStopped && !_pointerBusy && !_pointer.IsBusy;
        HomeButton.IsEnabled = MouseControls.IsEnabled;
        GestureControls.IsEnabled = !_calibrating && _mouse.IsReady && _airplay.HasVideo && _mapper.IsCalibrated && !_pointer.EmergencyStopped && !_pointerBusy && !_pointer.IsBusy;
        SwipeControls.IsEnabled = GestureControls.IsEnabled;
        ConnectButton.IsEnabled = !_connecting && !_client.IsPortOpen;
        DisconnectButton.IsEnabled = !_connecting && _client.IsPortOpen;
        ClearBondsButton.IsEnabled = !_connecting && _client.IsFirmwareVerified;
        PortComboBox.IsEnabled = !_connecting && !_client.IsPortOpen;
        StatusTextBlock.Text = _connecting ? "Verificando firmware..." : _mouse.IsReady ? "BLE: emparejado y suscrito" : _client.IsIphonePaired ? "BLE: esperando suscripcion HID" : _client.IsEsp32Connected ? "BLE: esperando emparejamiento" : "BLE: desconectado / esperando";
        AirPlayStatus.Text = "AirPlay: " + _airplay.Status;
        AccessPassword.Text = _airplay.IsRunning ? "Clave de acceso: " + _airplay.Password : "";
        CalibrationStatus.Text = _pointer.EmergencyStopped ? "PARADA ACTIVA" : !_mapper.IsCalibrated ? "Sin calibracion" : _pointer.Homed ? "Cursor sincronizado (aproximado)" : "HOME POINTER o marca arriba-izquierda";
        MetricsText.Text = $"Pointer Mode: {_pointer.PointerMode}\nHomed (estimado): {_pointer.Homed}\nEstimated X: {_pointer.EstimatedPosition.X:F0}\nEstimated Y: {_pointer.EstimatedPosition.Y:F0}\nBLE subscribed: {_client.IsInputSubscribed}\nCommands sent (USB): {_pointer.CommandsSent}\nCommands failed (USB): {_pointer.CommandsFailed}\nCurrent gesture: {_pointer.CurrentGesture}\nEmergencyStopped: {_pointer.EmergencyStopped}\nMirror FPS: {_airplay.Fps:F1}";
        UpdateDebug();
    }
    private async Task RunUiAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { _log.Write("GESTURE", "Cancelado."); }
        catch (Exception ex) { _log.Write("ERROR", ex.ToString()); }
        finally { if (!_closed) UpdateStatus(); }
    }
}
