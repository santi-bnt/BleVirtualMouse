namespace BleVirtualMouse.Services;

public interface IMouseTransport
{
    bool IsReady { get; }
    Task MoveAsync(int x, int y);
    Task SetButtonAsync(bool pressed);
    Task SetRightButtonAsync(bool pressed);
    Task ScrollAsync(int amount);
}

// Keep the proven serial client and firmware protocol unchanged.
public sealed class BluetoothMouseService(Esp32MouseClient client) : IMouseTransport
{
    public Esp32MouseClient Client { get; } = client;
    public bool IsReady => Client.IsFirmwareVerified && Client.IsIphonePaired && Client.IsInputSubscribed;
    public Task MoveAsync(int x, int y) => Client.MoveAsync(x, y);
    public Task SetButtonAsync(bool pressed) => Client.SetButtonAsync("LEFT", pressed);
    public Task SetRightButtonAsync(bool pressed) => Client.SetButtonAsync("RIGHT", pressed);
    public Task ScrollAsync(int amount) => Client.ScrollAsync(amount);
}
