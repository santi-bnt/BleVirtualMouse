using BleVirtualMouse.Controllers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BleVirtualMouse.Controls;

public sealed class ScreenView : FrameworkElement
{
    private BitmapSource? _frame;
    private Point? _marker;
    private DateTime _markerUntil;
    public bool ShowDebug { get; set; }
    public string DebugText { get; set; } = "";
    public CursorPoint? LastNormalized { get; private set; }
    public Point LastWindowPoint { get; private set; }
    public event Action<CursorPoint>? Pressed;
    public event Action<CursorPoint>? Dragged;
    public event Action? Released;
    public event Action<int>? Scrolled;
    public event Action? PointerChanged;
    public double Aspect => _frame is null ? 0 : (double)_frame.PixelWidth / _frame.PixelHeight;
    public ScreenView()
    {
        Focusable = true; ClipToBounds = true;
        MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            UpdatePointer(e.GetPosition(this));
            if (LastNormalized is not { } point) return;
            Focus(); CaptureMouse(); _marker = LastWindowPoint; _markerUntil = DateTime.UtcNow.AddSeconds(1);
            InvalidateVisual(); Pressed?.Invoke(point); e.Handled = true;
            _ = HideMarker();
        };
        MouseMove += (_, e) =>
        {
            UpdatePointer(e.GetPosition(this));
            if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed && LastNormalized is { } point) Dragged?.Invoke(point);
        };
        MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Left && IsMouseCaptured) { ReleaseMouseCapture(); Released?.Invoke(); e.Handled = true; } };
        LostMouseCapture += (_, _) => Released?.Invoke();
        MouseWheel += (_, e) => { Scrolled?.Invoke(Math.Sign(e.Delta) * 3); e.Handled = true; };
    }
    private async Task HideMarker() { await Task.Delay(1100); InvalidateVisual(); }
    private void UpdatePointer(Point point)
    {
        LastWindowPoint = point;
        LastNormalized = _frame is null ? null : IPhoneCoordinateMapper.Normalize(point.X, point.Y,
            ActualWidth, ActualHeight, _frame.PixelWidth, _frame.PixelHeight);
        PointerChanged?.Invoke(); InvalidateVisual();
    }
    public bool SetFrame(BitmapSource? frame)
    {
        bool changed = _frame is not null && frame is not null && Math.Abs(Aspect - (double)frame.PixelWidth / frame.PixelHeight) > .02;
        _frame = frame; InvalidateVisual(); return changed;
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 24, 27)), null, new Rect(RenderSize));
        if (_frame is not null)
        {
            double scale = Math.Min(ActualWidth / _frame.PixelWidth, ActualHeight / _frame.PixelHeight);
            double w = _frame.PixelWidth * scale, h = _frame.PixelHeight * scale;
            dc.DrawImage(_frame, new Rect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h));
        }
        else DrawText(dc, "Sin video", new Point(20, 20), Brushes.White);
        if (_marker is { } marker && DateTime.UtcNow < _markerUntil)
            dc.DrawEllipse(null, new Pen(Brushes.LimeGreen, 2), marker, 12, 12);
        if (ShowDebug)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)), null, new Rect(8, 8, Math.Max(0, Math.Min(470, ActualWidth - 16)), 115));
            DrawText(dc, DebugText, new Point(16, 16), Brushes.White);
        }
    }
    private void DrawText(DrawingContext dc, string text, Point point, Brush brush)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 12, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(1, ActualWidth - 36) };
        dc.DrawText(formatted, point);
    }
}
