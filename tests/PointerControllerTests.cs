using BleVirtualMouse.Controllers;
using BleVirtualMouse.Services;
using System.Collections.Concurrent;
using System.IO;

public static class PointerControllerTests
{
    public static async Task Run(Action<bool, string> check)
    {
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(file, "{\"WidthUnits\":240,\"HeightUnits\":480,\"VideoAspect\":0.5}");
            var mapper = new IPhoneCoordinateMapper(); mapper.Load(file);
            check(mapper.IsCalibrated && mapper.Profile.PointerMode == "Precise", "legacy calibration loads with Precise defaults");
            mapper.Profile.HomingIterations = 3; mapper.Profile.PointerStep = 12; mapper.Profile.PointerDelayMs = 20;
            mapper.Profile.TapDelayMs = 60; mapper.Profile.DragUpdateHz = 40; mapper.Save(file); mapper.Load(file);
            check(mapper.Profile.HomingIterations == 3 && mapper.Profile.PointerStep == 12 && mapper.Profile.TapDelayMs == 60
                && mapper.Profile.DragUpdateHz == 40 && !mapper.IsSynchronized, "config options roundtrip and unknown initial pose");
            string json = File.ReadAllText(file);
            check(json.Contains("\"pointerStep\"") && json.Contains("\"pointerDelayMs\"") && json.Contains("\"homingIterations\"")
                && json.Contains("\"tapDelayMs\"") && json.Contains("\"dragUpdateHz\"") && json.Contains("\"pointerMode\""), "required config names");
            foreach (var options in new PointerOptions[]
            {
                new() { PointerStep = 128 }, new() { PointerDelayMs = 0 }, new() { HomingIterations = 0 },
                new() { TapDelayMs = 0 }, new() { DragUpdateHz = 61 }, new() { PointerMode = "Unsafe" }
            })
            {
                bool rejected = false; try { options.Validate(); } catch (InvalidDataException) { rejected = true; }
                check(rejected, "unsafe configuration rejected " + System.Text.Json.JsonSerializer.Serialize(options));
            }
            var mouse = new RecordingMouse(); var pointer = new IPhonePointerController(mouse, mapper);
            var logs = new ConcurrentQueue<string>(); pointer.Log += (category, line) => logs.Enqueue($"[{category}] {line}");
            check(!pointer.Homed && !pointer.EmergencyStopped, "pointer initial states");
            await pointer.HomePointerAsync();
            var commands = mouse.Snapshot(); var moves = commands.Where(c => c.Kind == "MOVE").ToArray();
            check(pointer.Homed && pointer.EstimatedPosition == new CursorPoint(0, 0), "homing sets estimated origin");
            check(moves.Length == 3 && moves.All(c => c.X == -12 && c.Y == -12), "homing uses configured repeated steps");
            check(!commands.Any(c => c.Kind == "DOWN"), "homing never clicks");
            check(pointer.CommandsSent == commands.Length && pointer.CommandsFailed == 0, "USB command metrics");
            check(logs.Any(s => s.Contains("[POINTER] homing complete")), "homing structured log");
            await pointer.MoveToNormalizedAsync(0, 0); await pointer.MoveToNormalizedAsync(1, 1);
            check(pointer.EstimatedPosition == new CursorPoint(240, 480), "normalized inclusive bounds map correctly");
            foreach (var point in new[] { new CursorPoint(-.01, .5), new CursorPoint(1.01, .5), new CursorPoint(.5, -.1), new CursorPoint(.5, 1.1), new CursorPoint(double.NaN, .5), new CursorPoint(.5, double.PositiveInfinity) })
            {
                int before = mouse.Snapshot().Length; bool rejected = false;
                try { await pointer.TapNormalizedAsync(point.X, point.Y); } catch (ArgumentOutOfRangeException) { rejected = true; }
                check(rejected && mouse.Snapshot().Length == before, "invalid normalized point causes no HID command " + point);
            }
            mouse.Clear(); await pointer.TapNormalizedAsync(.5, .5);
            commands = mouse.Snapshot();
            check(commands.Count(c => c.Kind == "MOVE" && c.X == -12 && c.Y == -12) == 3, "Precise tap homes before moving");
            check(commands.Count(c => c.Kind == "DOWN") == 1 && commands.Last().Kind == "UP", "tap single down and finally up");
            check(pointer.EstimatedPosition == new CursorPoint(120, 240), "tap center estimated target");
            mapper.Profile.PointerMode = "Fast"; mouse.Clear(); await pointer.TapNormalizedAsync(.5, .5);
            check(!mouse.Snapshot().Any(c => c.Kind == "MOVE" && c.X < 0 && c.Y < 0), "Fast tap does not home");
            mouse.Clear(); var drag = pointer.DragNormalizedAsync(.5, .75, .5, .25, 1000);
            await mouse.WaitForDown(); await Task.Delay(90); await pointer.EmergencyStopAsync();
            bool cancelled = false; try { await drag; } catch (OperationCanceledException) { cancelled = true; }
            commands = mouse.Snapshot();
            check(cancelled && pointer.EmergencyStopped && !pointer.Homed, "drag cancelled by emergency and pose invalidated");
            check(commands.Any(c => c.Kind == "DOWN") && commands.Any(c => c.Kind == "UP") && commands.Last().Kind == "RIGHT_UP", "cancelled drag releases both buttons");
            int stoppedCount = commands.Length; await Task.Delay(80);
            check(mouse.Snapshot().Length == stoppedCount, "no movement after emergency completion");
            bool locked = false; try { await pointer.HomePointerAsync(); } catch (InvalidOperationException) { locked = true; }
            check(locked, "emergency rejects homing");
            check(logs.Any(s => s.Contains("[EMERGENCY] stop requested")) && logs.Any(s => s.Contains("[EMERGENCY] input locked")), "emergency structured logs");
            await pointer.ResumeAsync(); check(!pointer.EmergencyStopped && !pointer.Homed, "resume unlocks without inventing pose");
            await pointer.HomePointerAsync(); check(pointer.Homed, "home after resume");
            mouse.Clear(); var cancelledHome = pointer.HomePointerAsync(); await Task.Delay(60); await pointer.EmergencyStopAsync();
            try { await cancelledHome; } catch (OperationCanceledException) { }
            check(!pointer.Homed && pointer.EmergencyStopped, "cancelled homing does not set Homed");
            await pointer.ResumeAsync(); await pointer.HomePointerAsync(); mouse.Clear();
            mouse.FailMoveAfterDown = true;
            bool failed = false; try { await pointer.DragNormalizedAsync(.5, .75, .5, .25, 400); } catch (IOException) { failed = true; }
            check(failed && mouse.Snapshot().Last().Kind == "UP" && !pointer.Homed, "exception during drag still sends UP");
            check(pointer.CommandsFailed == 1, "failed transport write metric");
            mouse.FailMoveAfterDown = false; mouse.FailRelease = true; await pointer.EmergencyStopAsync();
            bool blockedResume = false; try { await pointer.ResumeAsync(); } catch (InvalidOperationException) { blockedResume = true; }
            check(blockedResume && pointer.EmergencyStopped, "resume blocked when release fails");
            mouse.FailRelease = false; await pointer.ResumeAsync();
            check(!pointer.EmergencyStopped, "resume retries pending release safely");
            mapper.Profile.PointerStep = 24; mapper.Profile.DragUpdateHz = 50; mapper.Profile.PointerDelayMs = 20;
            await pointer.HomePointerAsync(); mouse.Clear(); await pointer.DragTestAsync();
            commands = mouse.Snapshot(); int down = Array.FindIndex(commands, c => c.Kind == "DOWN");
            int up = Array.FindLastIndex(commands, c => c.Kind == "UP");
            moves = commands.Skip(down + 1).Take(up - down - 1).Where(c => c.Kind == "MOVE").ToArray();
            check(moves.Length >= 30 && moves.All(c => c.Y <= 0 && Math.Abs(c.X) <= 24 && Math.Abs(c.Y) <= 24), "drag test interpolates bounded monotonic movements");
            check(commands[up].Time - commands[down].Time >= 1000 && pointer.EstimatedPosition == new CursorPoint(120, 120), "drag test duration and endpoint");
            mouse.Clear(); await pointer.ScrollTestAsync();
            commands = mouse.Snapshot();
            check(commands.Length == 2 && commands[0].Kind == "SCROLL" && commands[0].Y == 3 && commands[1].Y == -3, "scroll test reuses wheel in both directions");
            check(commands[1].Time - commands[0].Time >= 650, "scroll test pause");
            mouse.Clear(); await pointer.PositionTestAsync();
            check(!mouse.Snapshot().Any(c => c.Kind == "DOWN") && logs.Count(s => s.Contains("position test ")) == 9, "position test visits all nine positions without clicks");
            check(pointer.EstimatedPosition == new CursorPoint(216, 432), "position test final estimated target");
            mouse.Clear(); await pointer.DriftTestAsync();
            check(!mouse.Snapshot().Any(c => c.Kind == "DOWN") && logs.Count(s => s.Contains("drift movements=")) == 50, "drift test exactly fifty visits without clicks");
            check(pointer.EstimatedPosition == new CursorPoint(120, 240), "drift test final estimated center");
            mapper.AnchorTopLeft(); mapper.RecordMove(300, 600); mapper.SaveBottomRight(file, .5); mapper.Load(file);
            check(mapper.Profile.PointerStep == 24 && mapper.Profile.HomingIterations == 3 && mapper.Profile.PointerMode == "Fast", "recalibration preserves pointer settings");
        }
        finally { File.Delete(file); }
    }

    private sealed class RecordingMouse : IMouseTransport
    {
        private readonly ConcurrentQueue<(string Kind, int X, int Y, long Time)> _commands = new();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private TaskCompletionSource _pressed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _down;
        public bool IsReady => true;
        public bool FailMoveAfterDown { get; set; }
        public bool FailRelease { get; set; }
        public (string Kind, int X, int Y, long Time)[] Snapshot() => _commands.ToArray();
        public void Clear() { _commands.Clear(); _pressed = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public Task WaitForDown() => _pressed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        private Task Record(string kind, int x = 0, int y = 0) { _commands.Enqueue((kind, x, y, _clock.ElapsedMilliseconds)); return Task.CompletedTask; }
        public Task MoveAsync(int x, int y) => FailMoveAfterDown && _down ? Task.FromException(new IOException("simulated move failure")) : Record("MOVE", x, y);
        public Task SetButtonAsync(bool pressed)
        {
            if (!pressed && FailRelease) return Task.FromException(new IOException("simulated release failure"));
            _down = pressed; var result = Record(pressed ? "DOWN" : "UP");
            if (pressed) _pressed.TrySetResult(); return result;
        }
        public Task SetRightButtonAsync(bool pressed) => Record(pressed ? "RIGHT_DOWN" : "RIGHT_UP");
        public Task ScrollAsync(int amount) => Record("SCROLL", 0, amount);
    }
}
