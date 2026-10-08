using BleVirtualMouse.Controllers;
using BleVirtualMouse.Services;
using System.Diagnostics;
using System.IO;
using System.Text;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}
var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
try
{
    Check(IPhoneCoordinateMapper.Normalize(10, 50, 200, 200, 100, 200) is null, "letterbox rejected");
    Check(IPhoneCoordinateMapper.Normalize(100, 100, 200, 200, 100, 200) == new CursorPoint(.5, .5), "letterbox center");
    Check(IPhoneCoordinateMapper.Normalize(50, 0, 200, 200, 100, 200) == new CursorPoint(0, 0), "top-left");
    Check(IPhoneCoordinateMapper.Normalize(150, 200, 200, 200, 100, 200) == new CursorPoint(1, 1), "bottom-right");
    Check(IPhoneCoordinateMapper.Normalize(0, 0, 0, 0, 0, 0) is null, "zero-size rejected");
    var mapper = new IPhoneCoordinateMapper(); mapper.AnchorTopLeft(); mapper.RecordMove(240, 480); mapper.SaveBottomRight(temp, .5);
    Check(mapper.MapPoint(.5, .5) == new CursorPoint(120, 240), "calibration math");
    mapper.Load(temp); Check(mapper.IsCalibrated && !mapper.IsSynchronized, "persistence never restores unknown pose");
    mapper.AnchorTopLeft(); var mouse = new FakeMouse(); var gestures = new GestureController(mouse, mapper);
    var clock = Stopwatch.StartNew();
    await gestures.SwipeNormalized(.5, .8, .5, .2, 400);
    Check(mapper.Cursor == new CursorPoint(120, 96), "swipe final target");
    var down = mouse.Commands.FindIndex(c => c.Kind == "DOWN");
    var up = mouse.Commands.FindIndex(c => c.Kind == "UP");
    var dragMoves = mouse.Commands.Skip(down + 1).Take(up - down - 1).Where(c => c.Kind == "MOVE").ToList();
    Check(dragMoves.Count >= 15 && dragMoves.All(c => Math.Abs(c.X) <= 24 && Math.Abs(c.Y) <= 24), "swipe interpolated and bounded");
    Check(dragMoves.All(c => c.Y <= 0), "swipe monotonic");
    Check(clock.ElapsedMilliseconds >= 400, "swipe paced");
    Check(mouse.Commands[up].Time - mouse.Commands[down].Time >= 400, "button held for requested duration");
    mouse.Commands.Clear();
    var swipe = gestures.SwipeNormalized(.5, .2, .5, .8, 1000);
    await Task.Delay(100); await gestures.EmergencyStop();
    try { await swipe; } catch (OperationCanceledException) { }
    int count = mouse.Commands.Count; await Task.Delay(80);
    Check(count == mouse.Commands.Count && mouse.Commands.Last().Kind == "RIGHT_UP", "stop leaves no queued movement and releases both buttons");
    Check(gestures.IsStopped && !mapper.IsSynchronized, "stop latch and invalidation");
    bool blocked = false; try { await gestures.TapNormalized(.5, .5); } catch (InvalidOperationException) { blocked = true; }
    Check(blocked, "stop rejects new gestures");
    gestures.Resume(); mapper.AnchorTopLeft(); mouse.Commands.Clear();
    var tap = gestures.TapNormalized(0, 0);
    while (!mouse.Commands.Any(c => c.Kind == "DOWN")) await Task.Delay(1);
    await gestures.EmergencyStop();
    try { await tap; } catch (OperationCanceledException) { }
    Check(mouse.Commands.Any(c => c.Kind == "DOWN") && mouse.Commands.Any(c => c.Kind == "UP"), "cancelled tap releases button");
    using var multipart = new MemoryStream(Encoding.ASCII.GetBytes("--iphoneframe\r\nContent-Type: image/jpeg\r\nContent-Length: 3\r\n\r\nabc\r\n--iphoneframe\r\nContent-Length: 2\r\n\r\nde"));
    Check(Encoding.ASCII.GetString(await MultipartFrameReader.Read(multipart, default)) == "abc", "multipart framing");
    Check(Encoding.ASCII.GetString(await MultipartFrameReader.Read(multipart, default)) == "de", "multipart next frame");
    bool invalid = false;
    try { await MultipartFrameReader.Read(new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: 99999999\r\n\r\n")), default); } catch (InvalidDataException) { invalid = true; }
    Check(invalid, "untrusted frame length bounded");
    await PointerControllerTests.Run(Check);
    int integrationChecks = args.Contains("--airplay") ? await AirPlaySmokeTests.Run(Directory.GetCurrentDirectory()) : 0;
    int uiChecks = await UiSmokeTests.Run(Directory.GetCurrentDirectory());
    Console.WriteLine($"ALL TESTS PASSED: {passed + integrationChecks + uiChecks} checks (skipped tests not counted)");
}
finally { File.Delete(temp); }

sealed class FakeMouse : IMouseTransport
{
    public bool IsReady => true;
    public List<(string Kind, int X, int Y, long Time)> Commands { get; } = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Task Record(string kind, int x = 0, int y = 0) { Commands.Add((kind, x, y, _clock.ElapsedMilliseconds)); return Task.CompletedTask; }
    public Task MoveAsync(int x, int y) => Record("MOVE", x, y);
    public Task SetButtonAsync(bool pressed) => Record(pressed ? "DOWN" : "UP");
    public Task SetRightButtonAsync(bool pressed) => Record(pressed ? "RIGHT_DOWN" : "RIGHT_UP");
    public Task ScrollAsync(int amount) => Record("SCROLL", 0, amount);
}
