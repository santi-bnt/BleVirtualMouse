using BleVirtualMouse.Services;

namespace BleVirtualMouse.Controllers;

// This is the app's input boundary; the existing gesture engine still owns pacing and cancellation.
public sealed class IPhonePointerController
{
    private readonly GestureController _gestures;
    private readonly IPhoneCoordinateMapper _mapper;
    private readonly CountingTransport _transport;
    public event Action<string, string>? Log;
    public event Action? Changed;
    public bool Homed => _mapper.IsSynchronized;
    public bool EmergencyStopped => _gestures.IsStopped;
    public bool IsBusy => _gestures.IsBusy;
    public string CurrentGesture => _gestures.CurrentGesture;
    public CursorPoint EstimatedPosition => _mapper.Cursor;
    public string PointerMode => _mapper.Profile.PointerMode;
    public long CommandsSent => _transport.Sent;
    public long CommandsFailed => _transport.Failed;

    public IPhonePointerController(IMouseTransport transport, IPhoneCoordinateMapper mapper)
    {
        _mapper = mapper; _transport = new(transport, () => Changed?.Invoke());
        _gestures = new(_transport, mapper, () => mapper.Profile);
        _gestures.Log += message => Log?.Invoke(message.StartsWith("ERROR") ? "ERROR" : "GESTURE", message);
        _gestures.PointerLog += message => Log?.Invoke("POINTER", $"{message} commands sent={CommandsSent} failed={CommandsFailed}");
        _gestures.EmergencyLog += message => Log?.Invoke("EMERGENCY", message);
        _gestures.Changed += () => Changed?.Invoke();
    }
    public Task HomePointerAsync() => _gestures.HomePointerAsync();
    public Task MoveToNormalizedAsync(double x, double y) => _gestures.MoveToNormalizedAsync(x, y);
    public Task TapNormalizedAsync(double x, double y) => _gestures.TapNormalized(x, y);
    public Task DragNormalizedAsync(double sx, double sy, double ex, double ey, int durationMs) => _gestures.SwipeNormalized(sx, sy, ex, ey, durationMs);
    public Task BeginDragAsync(double x, double y) => _gestures.BeginDrag(x, y);
    public Task UpdateDragAsync(double x, double y) => _gestures.UpdateDrag(x, y);
    public Task EndDragAsync() => _gestures.EndDrag();
    public Task JogAsync(int x, int y) => _gestures.Jog(x, y);
    public Task ClickAsync() => _gestures.Click();
    public Task RightClickAsync() => _gestures.ClickRight();
    public Task ScrollAsync(int amount) => _gestures.Scroll(amount);
    public Task EmergencyStopAsync() => _gestures.EmergencyStop();
    public Task ResumeAsync() => _gestures.ResumeAsync();
    public Task PositionTestAsync() => _gestures.PositionTestAsync();
    public Task DriftTestAsync() => _gestures.DriftTestAsync();
    public Task ScrollTestAsync() => _gestures.ScrollTestAsync();
    public Task DragTestAsync() => DragNormalizedAsync(.5, .75, .5, .25, 1000);
    public Task EmergencyTestAsync() => DragNormalizedAsync(.5, .75, .5, .25, 5000);

    private sealed class CountingTransport(IMouseTransport inner, Action changed) : IMouseTransport
    {
        private long _sent, _failed;
        public long Sent => Interlocked.Read(ref _sent);
        public long Failed => Interlocked.Read(ref _failed);
        public bool IsReady => inner.IsReady;
        private async Task Send(Func<Task> action)
        {
            try { await action(); Interlocked.Increment(ref _sent); }
            catch { Interlocked.Increment(ref _failed); throw; }
            finally { changed(); }
        }
        public Task MoveAsync(int x, int y) => Send(() => inner.MoveAsync(x, y));
        public Task SetButtonAsync(bool down) => Send(() => inner.SetButtonAsync(down));
        public Task SetRightButtonAsync(bool down) => Send(() => inner.SetRightButtonAsync(down));
        public Task ScrollAsync(int amount) => Send(() => inner.ScrollAsync(amount));
    }
}
