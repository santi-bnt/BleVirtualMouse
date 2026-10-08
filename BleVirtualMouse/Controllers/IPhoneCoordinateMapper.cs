using System.IO;
using System.Text.Json;

namespace BleVirtualMouse.Controllers;

public readonly record struct CursorPoint(double X, double Y);

public sealed class CalibrationProfile : PointerOptions
{
    public double WidthUnits { get; set; }
    public double HeightUnits { get; set; }
    public double VideoAspect { get; set; }
}

public sealed class IPhoneCoordinateMapper
{
    public CalibrationProfile Profile { get; private set; } = new();
    public bool IsCalibrated => Profile.WidthUnits > 0 && Profile.HeightUnits > 0;
    public bool IsSynchronized { get; private set; }
    public CursorPoint Cursor { get; private set; }

    public static CursorPoint? Normalize(double x, double y, double viewWidth, double viewHeight,
        double imageWidth, double imageHeight)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(viewWidth) || !double.IsFinite(viewHeight)
            || !double.IsFinite(imageWidth) || !double.IsFinite(imageHeight)
            || viewWidth <= 0 || viewHeight <= 0 || imageWidth <= 0 || imageHeight <= 0) return null;
        double scale = Math.Min(viewWidth / imageWidth, viewHeight / imageHeight);
        double width = imageWidth * scale, height = imageHeight * scale;
        double nx = (x - (viewWidth - width) / 2) / width;
        double ny = (y - (viewHeight - height) / 2) / height;
        return nx < 0 || nx > 1 || ny < 0 || ny > 1 ? null : new(nx, ny);
    }

    public CursorPoint MapPoint(double nx, double ny)
    {
        if (!IsCalibrated) throw new InvalidOperationException("Primero calibra el cursor.");
        if (!double.IsFinite(nx) || !double.IsFinite(ny) || nx < 0 || nx > 1 || ny < 0 || ny > 1)
            throw new ArgumentOutOfRangeException(nameof(nx));
        return new(nx * Profile.WidthUnits, ny * Profile.HeightUnits);
    }

    public void AnchorTopLeft() { Cursor = new(0, 0); IsSynchronized = true; }
    public void Invalidate() => IsSynchronized = false;
    public void RecordMove(int x, int y) => Cursor = new(Cursor.X + x, Cursor.Y + y);
    public void SaveBottomRight(string path, double aspect)
    {
        if (!IsSynchronized || Cursor.X <= 0 || Cursor.Y <= 0 || Cursor.X > 100000 || Cursor.Y > 100000 || !double.IsFinite(aspect) || aspect < 0)
            throw new InvalidOperationException("Marca arriba-izquierda y mueve el cursor a abajo-derecha usando los controles.");
        Profile.WidthUnits = Cursor.X; Profile.HeightUnits = Cursor.Y; Profile.VideoAspect = aspect;
        Save(path);
    }
    public void Save(string path)
    {
        Profile.Validate();
        File.WriteAllText(path, JsonSerializer.Serialize(Profile, new JsonSerializerOptions { WriteIndented = true }));
    }
    public void Load(string path)
    {
        if (!File.Exists(path)) return;
        var profile = JsonSerializer.Deserialize<CalibrationProfile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (profile is null || !double.IsFinite(profile.WidthUnits) || !double.IsFinite(profile.HeightUnits)
            || profile.WidthUnits < 0 || profile.HeightUnits < 0 || profile.WidthUnits > 100000 || profile.HeightUnits > 100000
            || ((profile.WidthUnits == 0) != (profile.HeightUnits == 0)) || !double.IsFinite(profile.VideoAspect) || profile.VideoAspect < 0)
            throw new InvalidDataException("Calibracion invalida.");
        profile.Validate();
        Profile = profile;
        Invalidate(); // Saved travel is reusable; the current physical cursor position is unknown.
    }
}
