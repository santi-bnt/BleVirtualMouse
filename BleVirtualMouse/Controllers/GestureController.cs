using BleVirtualMouse.Services;

namespace BleVirtualMouse.Controllers;

public sealed class GestureController(IMouseTransport mouse, IPhoneCoordinateMapper mapper, Func<PointerOptions>? settings = null)
{
    private readonly PointerOptions _legacyOptions = new() { PointerMode = "Fast" };
    private PointerOptions Options => settings?.Invoke() ?? _legacyOptions;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource _cancel = new();
    private volatile bool _stopped;
    private bool _down;
    private bool _rightDown;
    private int _stopGeneration;
    public bool IsStopped => _stopped;
    public bool IsBusy => _gate.CurrentCount == 0;
    public event Action<string>? Log;
    public event Action<string>? PointerLog;
    public event Action<string>? EmergencyLog;
    public event Action? Changed;
    public string CurrentGesture { get; private set; } = "none";

    public void Resume()
    {
        if (_gate.CurrentCount == 0) throw new InvalidOperationException("Espera a que termine la parada.");
        if (_down || _rightDown) throw new InvalidOperationException("Reconecta BLE y pulsa Emergency Stop para solicitar la liberacion antes de reanudar.");
        UnlockInputs();
    }
    private void UnlockInputs()
    {
        _cancel.Dispose(); _cancel = new(); _stopped = false;
        Log?.Invoke("Reanudado; sincroniza el cursor antes de usar coordenadas.");
        Changed?.Invoke();
    }
    public async Task ResumeAsync()
    {
        int generation = Volatile.Read(ref _stopGeneration);
        if (!await _gate.WaitAsync(0)) throw new InvalidOperationException("Espera a que termine la parada.");
        try
        {
            if (_down || _rightDown) await Release();
            if (_down || _rightDown) throw new InvalidOperationException("No se pudieron enviar UP. Reconecta el control USB/BLE antes de reanudar.");
            if (generation != Volatile.Read(ref _stopGeneration)) throw new InvalidOperationException("Se solicito otra parada; vuelve a reanudar cuando termine.");
            UnlockInputs();
        }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    private async Task Run(Func<CancellationToken, Task> action, string name = "manual")
    {
        if (_stopped) throw new InvalidOperationException("Emergency Stop activo.");
        var token = _cancel.Token;
        // Do not accumulate queued gestures or stale pointer positions.
        if (!await _gate.WaitAsync(0)) throw new InvalidOperationException("Otro gesto esta en curso.");
        try
        {
            CurrentGesture = name; Changed?.Invoke(); Options.Validate();
            token.ThrowIfCancellationRequested();
            if (!mouse.IsReady) throw new InvalidOperationException("BLE sin emparejar o sin suscripcion HID.");
            await action(token);
        }
        catch
        {
            await Release();
            mapper.Invalidate();
            throw;
        }
        finally { CurrentGesture = _down ? "manual drag" : "none"; _gate.Release(); Changed?.Invoke(); }
    }

    public Task Jog(int x, int y) => Run(async token =>
    {
        token.ThrowIfCancellationRequested();
        await mouse.MoveAsync(Math.Clamp(x, -127, 127), Math.Clamp(y, -127, 127));
        mapper.RecordMove(Math.Clamp(x, -127, 127), Math.Clamp(y, -127, 127));
        await Task.Delay(Options.PointerDelayMs, token);
    });

    public Task Click() => Run(async token =>
    {
        _down = true;
        try { await mouse.SetButtonAsync(true); await Task.Delay(Options.TapDelayMs, token); }
        finally { await Release(); }
    });
    public Task ClickRight() => Run(async token =>
    {
        _rightDown = true;
        try { await mouse.SetRightButtonAsync(true); await Task.Delay(Options.TapDelayMs, token); }
        finally { await Release(); }
    });

    private async Task MoveCursorToTarget(CursorPoint target, CancellationToken token, int minimumSteps = 1, int? intervalMs = null)
    {
        if (!mapper.IsSynchronized) throw new InvalidOperationException("Coloca el cursor arriba-izquierda y marca la referencia.");
        var start = mapper.Cursor;
        int steps = Math.Max(minimumSteps, (int)Math.Ceiling(Math.Max(Math.Abs(target.X - start.X), Math.Abs(target.Y - start.Y)) / Options.PointerStep));
        for (int i = 1; i <= steps; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!mouse.IsReady) throw new InvalidOperationException("Se perdio la suscripcion HID.");
            int dx = (int)Math.Round(start.X + (target.X - start.X) * i / steps - mapper.Cursor.X);
            int dy = (int)Math.Round(start.Y + (target.Y - start.Y) * i / steps - mapper.Cursor.Y);
            if (dx != 0 || dy != 0)
            {
                await mouse.MoveAsync(dx, dy);
                mapper.RecordMove(dx, dy);
            }
            await Task.Delay(intervalMs ?? Options.PointerDelayMs, token);
        }
    }

    public Task TapNormalized(double nx, double ny) => Run(async token =>
    {
        Log?.Invoke($"tap normalized=({nx:F3},{ny:F3})");
        var target = mapper.MapPoint(nx, ny);
        if (Options.PointerMode == "Precise") await HomeCore(token);
        await MoveCursorToTarget(target, token);
        token.ThrowIfCancellationRequested();
        _down = true;
        try
        {
            await mouse.SetButtonAsync(true);
            await Task.Delay(Options.TapDelayMs, token);
        }
        finally { await Release(); }
    }, "tap");

    public Task BeginDrag(double nx, double ny) => Run(async token =>
    {
        await MoveCursorToTarget(mapper.MapPoint(nx, ny), token);
        token.ThrowIfCancellationRequested();
        _down = true;
        await mouse.SetButtonAsync(true);
        await Task.Delay(Options.PointerDelayMs, token);
        Log?.Invoke("drag down");
    });
    public Task UpdateDrag(double nx, double ny) => Run(async token =>
    {
        if (_down) await MoveCursorToTarget(mapper.MapPoint(nx, ny), token);
    });
    public async Task EndDrag()
    {
        await _gate.WaitAsync();
        try { await Release(); Log?.Invoke("drag up"); }
        finally { CurrentGesture = "none"; _gate.Release(); Changed?.Invoke(); }
    }
    public Task Scroll(int amount) => Run(async token =>
    {
        token.ThrowIfCancellationRequested();
        await mouse.ScrollAsync(Math.Clamp(amount, -127, 127));
        await Task.Delay(Options.PointerDelayMs, token);
    });
    public Task SwipeNormalized(double sx, double sy, double ex, double ey, int durationMs) => Run(async token =>
    {
        if (durationMs < 20 || durationMs > 10000) throw new ArgumentOutOfRangeException(nameof(durationMs));
        Log?.Invoke($"swipe ({sx:F3},{sy:F3})->({ex:F3},{ey:F3}) duration={durationMs}");
        var start = mapper.MapPoint(sx, sy); var end = mapper.MapPoint(ex, ey);
        if (!mapper.IsSynchronized) await HomeCore(token);
        await MoveCursorToTarget(start, token);
        _down = true;
        try
        {
            token.ThrowIfCancellationRequested();
            await mouse.SetButtonAsync(true);
            int interval = (int)Math.Ceiling(1000.0 / Math.Min(50, Options.DragUpdateHz));
            await Task.Delay(interval, token);
            await MoveCursorToTarget(end, token, (int)Math.Ceiling(durationMs / (double)interval), interval);
        }
        finally { await Release(); }
    }, "drag");

    public Task HomePointerAsync() => Run(HomeCore, "homing");
    private async Task HomeCore(CancellationToken token)
    {
        mapper.Invalidate(); PointerLog?.Invoke("homing started"); Changed?.Invoke();
        _down = true; _rightDown = true; await Release();
        if (_down || _rightDown) throw new InvalidOperationException("Homing bloqueado: liberacion de botones pendiente.");
        await Task.Delay(Options.PointerDelayMs, token);
        for (int i = 0; i < Options.HomingIterations; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!mouse.IsReady) throw new InvalidOperationException("Se perdio BLE durante homing.");
            await mouse.MoveAsync(-Options.PointerStep, -Options.PointerStep);
            await Task.Delay(Options.PointerDelayMs, token);
        }
        token.ThrowIfCancellationRequested(); mapper.AnchorTopLeft();
        PointerLog?.Invoke("homing complete"); PointerLog?.Invoke("estimated position=(0,0)"); Changed?.Invoke();
    }
    public Task MoveToNormalizedAsync(double x, double y) => Run(token => MoveNormalizedCore(x, y, token), "move");
    private async Task MoveNormalizedCore(double x, double y, CancellationToken token)
    {
        PointerLog?.Invoke($"target normalized=({x:F3},{y:F3})");
        await MoveCursorToTarget(mapper.MapPoint(x, y), token);
        PointerLog?.Invoke($"estimated position=({mapper.Cursor.X:F0},{mapper.Cursor.Y:F0})");
    }
    public Task PositionTestAsync() => Run(async token =>
    {
        _ = mapper.MapPoint(0, 0); await HomeCore(token);
        int position = 0;
        foreach (double y in new[] { .1, .5, .9 })
            foreach (double x in new[] { .1, .5, .9 })
            {
                await MoveNormalizedCore(x, y, token);
                PointerLog?.Invoke($"position test {++position}/9"); await Task.Delay(650, token);
            }
    }, "9 positions");
    public Task DriftTestAsync() => Run(async token =>
    {
        _ = mapper.MapPoint(0, 0); await HomeCore(token);
        CursorPoint[] points = [new(.25, .25), new(.75, .25), new(.75, .75), new(.25, .75), new(.5, .5)];
        for (int i = 0; i < 50; i++)
        {
            var p = points[i % points.Length]; await MoveNormalizedCore(p.X, p.Y, token);
            PointerLog?.Invoke($"drift movements={i + 1}/50"); await Task.Delay(100, token);
        }
        PointerLog?.Invoke($"drift complete estimated final=({mapper.Cursor.X:F0},{mapper.Cursor.Y:F0})");
    }, "drift");
    public Task ScrollTestAsync() => Run(async token =>
    {
        Log?.Invoke("scroll up"); await mouse.ScrollAsync(3); await Task.Delay(650, token);
        token.ThrowIfCancellationRequested(); Log?.Invoke("scroll down"); await mouse.ScrollAsync(-3);
        await Task.Delay(Options.PointerDelayMs, token);
    }, "scroll test");

    private async Task Release()
    {
        if (!_down && !_rightDown) return;
        try
        {
            if (_down) { await mouse.SetButtonAsync(false); _down = false; }
            if (_rightDown) { await Task.Delay(20); await mouse.SetRightButtonAsync(false); _rightDown = false; }
        }
        catch (Exception ex)
        {
            _stopped = true; _cancel.Cancel(); mapper.Invalidate();
            Log?.Invoke("ERROR: liberacion no enviada; control bloqueado: " + ex.Message);
        }
    }
    public async Task EmergencyStop()
    {
        EmergencyLog?.Invoke("stop requested"); Interlocked.Increment(ref _stopGeneration);
        _stopped = true;
        _cancel.Cancel();
        Changed?.Invoke();
        await _gate.WaitAsync();
        try
        {
            _stopped = true; _cancel.Cancel();
            EmergencyLog?.Invoke("current gesture cancelled");
            // Send UP even if a previous press failed halfway through the serial write.
            _down = true;
            _rightDown = true;
            await Release();
            mapper.Invalidate();
            EmergencyLog?.Invoke(_down || _rightDown ? "mouse release NOT sent; reconnect required" : "mouse released (UP written; delivery not confirmed)");
            EmergencyLog?.Invoke("input locked");
            Log?.Invoke("Emergency Stop: cancelado, UP solicitado; nuevas entradas bloqueadas.");
        }
        finally { CurrentGesture = "none"; _gate.Release(); Changed?.Invoke(); }
    }
}
