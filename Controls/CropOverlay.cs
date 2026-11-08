using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace ImageResize.Controls;

/// <summary>
/// 裁剪选框覆盖层（移植自参考应用 CropOverlayView）。
/// 在 ImageRect（图片显示区，控件坐标）内维护归一化到 [0,1] 的选框，
/// 与位图实际尺寸解耦。
/// 手势：拖动四角缩放选框；在选框内拖动整体移动；在选框外按下并拖动重新框选。
/// 覆盖层画在照片上，配色固定不随深浅色主题（白框 + 深色描边 + 三分网格）。
/// </summary>
public sealed class CropOverlay : Grid
{
    private enum DragMode { None, Move, Tl, Tr, Bl, Br, New }

    private const double MinSizePx = 48;      // 最小选区（逻辑像素）
    private const double CornerLenPx = 22;    // 角手柄长度
    private const double CornerHitRadius = 18;// 角命中半径

    private Rect _imageRect = Rect.Empty;     // 图片显示区，控件坐标；Empty 表示没有图片
    private Rect _cropNorm = new(0, 0, 1, 1); // 归一化选框：X=left, Y=top
    private Rect _dragStartNorm;
    private Point _downPoint;
    private Point _moveOffset;
    private DragMode _mode = DragMode.None;

    /// <summary>选框变化回调（归一化坐标）。</summary>
    public event Action<Rect>? CropChanged;

    private readonly Canvas _canvas = new();
    private readonly Rectangle[] _dims = new Rectangle[4];      // 上/下/左/右压暗块
    private readonly Line[] _gridLines = new Line[4];           // 三分辅助线：竖×2 + 横×2
    private readonly Rectangle _frameShadow = new();            // 白框下的深色描边
    private readonly Rectangle _frame = new();
    private readonly Polyline[] _corners = new Polyline[8];     // 0..3 深色垫层，4..7 白色 L 手柄

    private static readonly SolidColorBrush DimBrush =
        new(Microsoft.UI.ColorHelper.FromArgb(0xB3, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush FrameBrush =
        new(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush FrameShadowBrush =
        new(Microsoft.UI.ColorHelper.FromArgb(0x59, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush GridBrush =
        new(Microsoft.UI.ColorHelper.FromArgb(0x59, 0xFF, 0xFF, 0xFF));

    public CropOverlay()
    {
        // Grid 需要显式背景才会命中整块区域的指针事件
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        for (int i = 0; i < _dims.Length; i++)
        {
            _dims[i] = new Rectangle { Fill = DimBrush };
            _canvas.Children.Add(_dims[i]);
        }
        for (int i = 0; i < _gridLines.Length; i++)
        {
            _gridLines[i] = new Line { Stroke = GridBrush, StrokeThickness = 0.75 };
            _canvas.Children.Add(_gridLines[i]);
        }
        _frameShadow.Stroke = FrameShadowBrush;
        _frameShadow.StrokeThickness = 4;
        _canvas.Children.Add(_frameShadow);
        _frame.Stroke = FrameBrush;
        _frame.StrokeThickness = 1.5;
        _canvas.Children.Add(_frame);
        for (int i = 0; i < _corners.Length; i++)
        {
            bool shadow = i < 4;
            _corners[i] = new Polyline
            {
                Stroke = shadow ? FrameShadowBrush : FrameBrush,
                StrokeThickness = shadow ? 6 : 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            _canvas.Children.Add(_corners[i]);
        }
        Children.Add(_canvas);

        // WinUI3 的指针事件是路由事件（无 OnXxx 虚方法重写），改用订阅
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerCanceled;
    }

    /// <summary>更新图片显示区域；传 null 表示没有图片。</summary>
    public void SetImageRect(Rect? rect)
    {
        _imageRect = rect ?? Rect.Empty;
        Render();
    }

    /// <summary>选框重置为全图。</summary>
    public void ResetToFull()
    {
        _cropNorm = new Rect(0, 0, 1, 1);
        Render();
        NotifyChanged();
    }

    /// <summary>当前选框（归一化坐标）。</summary>
    public Rect CropRectNorm => _cropNorm;

    // ---------------------------------------------------------------- 绘制

    private void Render()
    {
        bool noImage = _imageRect.IsEmpty || _imageRect.Width <= 0 || _imageRect.Height <= 0;
        _canvas.Visibility = noImage ? Visibility.Collapsed : Visibility.Visible;
        if (noImage) return;

        Rect c = PixelRect();

        // 压暗选框外区域（上、下、左、右四块）
        LayoutDim(_dims[0], _imageRect.X, _imageRect.Y, _imageRect.Width, c.Top - _imageRect.Y);
        LayoutDim(_dims[1], _imageRect.X, c.Bottom, _imageRect.Width, _imageRect.Bottom - c.Bottom);
        LayoutDim(_dims[2], _imageRect.X, c.Top, c.X - _imageRect.X, c.Height);
        LayoutDim(_dims[3], c.Right, c.Top, _imageRect.Right - c.Right, c.Height);

        // 三分辅助线
        LayoutLine(_gridLines[0], c.X + c.Width / 3, c.Top, c.X + c.Width / 3, c.Bottom);
        LayoutLine(_gridLines[1], c.X + c.Width * 2 / 3, c.Top, c.X + c.Width * 2 / 3, c.Bottom);
        LayoutLine(_gridLines[2], c.X, c.Top + c.Height / 3, c.Right, c.Top + c.Height / 3);
        LayoutLine(_gridLines[3], c.X, c.Top + c.Height * 2 / 3, c.Right, c.Top + c.Height * 2 / 3);

        // 边框与四角手柄（先深色描边，再叠白线）
        LayoutRect(_frameShadow, c);
        LayoutRect(_frame, c);
        LayoutCorner(_corners[0], c, left: true, top: true);
        LayoutCorner(_corners[1], c, left: false, top: true);
        LayoutCorner(_corners[2], c, left: true, top: false);
        LayoutCorner(_corners[3], c, left: false, top: false);
        LayoutCorner(_corners[4], c, left: true, top: true);
        LayoutCorner(_corners[5], c, left: false, top: true);
        LayoutCorner(_corners[6], c, left: true, top: false);
        LayoutCorner(_corners[7], c, left: false, top: false);
    }

    private void LayoutDim(Rectangle r, double x, double y, double w, double h)
    {
        if (w <= 0 || h <= 0)
        {
            r.Visibility = Visibility.Collapsed;
            return;
        }
        LayoutRect(r, new Rect(x, y, w, h));
    }

    private void LayoutRect(Rectangle r, Rect rect)
    {
        r.Visibility = Visibility.Visible;
        Canvas.SetLeft(r, rect.X);
        Canvas.SetTop(r, rect.Y);
        r.Width = rect.Width;
        r.Height = rect.Height;
    }

    private void LayoutLine(Line line, double x1, double y1, double x2, double y2)
    {
        line.Visibility = Visibility.Visible;
        line.X1 = x1; line.Y1 = y1; line.X2 = x2; line.Y2 = y2;
    }

    private void LayoutCorner(Polyline corner, Rect c, bool left, bool top)
    {
        double x = left ? c.Left : c.Right;
        double y = top ? c.Top : c.Bottom;
        double dx = left ? CornerLenPx : -CornerLenPx;
        double dy = top ? CornerLenPx : -CornerLenPx;
        corner.Visibility = Visibility.Visible;
        corner.Points = new PointCollection
        {
            // 角点必须在中间：横臂 → 角 → 竖臂，否则会连出斜边
            new(x + dx, y),
            new(x, y),
            new(x, y + dy),
        };
    }

    // ---------------------------------------------------------------- 手势

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_imageRect.IsEmpty || _imageRect.Width <= 0 || _imageRect.Height <= 0)
        {
            return;
        }
        Point p = e.GetCurrentPoint(this).Position;
        BeginDrag(p);
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_mode == DragMode.None)
        {
            return;
        }
        Point p = e.GetCurrentPoint(this).Position;
        DragTo(p);
        Render();
        NotifyChanged();
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndDrag();
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        EndDrag();
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void BeginDrag(Point p)
    {
        _downPoint = p;
        _dragStartNorm = _cropNorm;
        Rect c = PixelRect();

        DragMode corner = HitCorner(p, c);
        if (corner != DragMode.None)
        {
            _mode = corner;
            return;
        }
        if (c.Contains(p))
        {
            _mode = DragMode.Move;
            _moveOffset = new Point(p.X - c.X, p.Y - c.Y);
            return;
        }
        _mode = DragMode.New;
        double x = NormX(p.X), y = NormY(p.Y);
        _cropNorm = new Rect(x, y, 0, 0);
    }

    private void DragTo(Point p)
    {
        double sMinW = Math.Clamp(MinSizePx / _imageRect.Width, 0, 1);
        double sMinH = Math.Clamp(MinSizePx / _imageRect.Height, 0, 1);

        switch (_mode)
        {
            case DragMode.Move:
            {
                double dx = (p.X - _downPoint.X) / _imageRect.Width;
                double dy = (p.Y - _downPoint.Y) / _imageRect.Height;
                Rect s = _dragStartNorm;
                double left = Math.Clamp(s.X + dx, 0, 1 - s.Width);
                double top = Math.Clamp(s.Y + dy, 0, 1 - s.Height);
                _cropNorm = new Rect(left, top, s.Width, s.Height);
                break;
            }
            case DragMode.Tl:
            {
                double l = Math.Clamp(NormX(p.X), 0, _dragStartNorm.Right - sMinW);
                double t = Math.Clamp(NormY(p.Y), 0, _dragStartNorm.Bottom - sMinH);
                _cropNorm = FromEdges(l, t, _dragStartNorm.Right, _dragStartNorm.Bottom);
                break;
            }
            case DragMode.Tr:
            {
                double r = Math.Clamp(NormX(p.X), _dragStartNorm.X + sMinW, 1);
                double t = Math.Clamp(NormY(p.Y), 0, _dragStartNorm.Bottom - sMinH);
                _cropNorm = FromEdges(_dragStartNorm.X, t, r, _dragStartNorm.Bottom);
                break;
            }
            case DragMode.Bl:
            {
                double l = Math.Clamp(NormX(p.X), 0, _dragStartNorm.Right - sMinW);
                double b = Math.Clamp(NormY(p.Y), _dragStartNorm.Y + sMinH, 1);
                _cropNorm = FromEdges(l, _dragStartNorm.Y, _dragStartNorm.Right, b);
                break;
            }
            case DragMode.Br:
            {
                double r = Math.Clamp(NormX(p.X), _dragStartNorm.X + sMinW, 1);
                double b = Math.Clamp(NormY(p.Y), _dragStartNorm.Y + sMinH, 1);
                _cropNorm = FromEdges(_dragStartNorm.X, _dragStartNorm.Y, r, b);
                break;
            }
            case DragMode.New:
            {
                double x0 = NormX(_downPoint.X), y0 = NormY(_downPoint.Y);
                double x1 = NormX(p.X), y1 = NormY(p.Y);
                _cropNorm = FromEdges(
                    Math.Min(x0, x1), Math.Min(y0, y1),
                    Math.Max(x0, x1), Math.Max(y0, y1));
                break;
            }
        }
    }

    private void EndDrag()
    {
        if (_mode == DragMode.New &&
            (_cropNorm.Width < Math.Clamp(MinSizePx / Math.Max(_imageRect.Width, 1), 0, 1) ||
             _cropNorm.Height < Math.Clamp(MinSizePx / Math.Max(_imageRect.Height, 1), 0, 1)))
        {
            // 点击空白或拖动距离过小：恢复原选框
            _cropNorm = _dragStartNorm;
            Render();
            NotifyChanged();
        }
        _mode = DragMode.None;
    }

    private DragMode HitCorner(Point p, Rect c)
    {
        bool Near(double px, double py) =>
            (p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py) <= CornerHitRadius * CornerHitRadius;
        if (Near(c.Left, c.Top)) return DragMode.Tl;
        if (Near(c.Right, c.Top)) return DragMode.Tr;
        if (Near(c.Left, c.Bottom)) return DragMode.Bl;
        if (Near(c.Right, c.Bottom)) return DragMode.Br;
        return DragMode.None;
    }

    private void NotifyChanged() => CropChanged?.Invoke(_cropNorm);

    // ---------------------------------------------------------------- 坐标换算

    private Rect PixelRect() => new(
        _imageRect.X + _cropNorm.X * _imageRect.Width,
        _imageRect.Y + _cropNorm.Y * _imageRect.Height,
        _cropNorm.Width * _imageRect.Width,
        _cropNorm.Height * _imageRect.Height);

    private double NormX(double x) => Math.Clamp((x - _imageRect.X) / _imageRect.Width, 0, 1);

    private double NormY(double y) => Math.Clamp((y - _imageRect.Y) / _imageRect.Height, 0, 1);

    private static Rect FromEdges(double left, double top, double right, double bottom) =>
        new(left, top, right - left, bottom - top);
}
