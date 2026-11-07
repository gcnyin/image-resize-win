using ImageResize.Controls;
using ImageResize.Models;
using ImageResize.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace ImageResize;

/// <summary>界面与编辑流程编排（对应参考应用 MainActivity）。</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>当前编辑中的位图（加载 → 剪裁/缩放循环替换）。</summary>
    private SkiaSharp.SKBitmap? _current;

    /// <summary>本次加载的源图片信息。</summary>
    private LoadedImage? _source;

    private string? _sourcePath;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        Title = "图片工具";

        // WinUI 运行时不接受 XAML 里的 IsChecked="True"（ToggleButton.IsChecked 赋值失败），在代码中设置
        ModeCrop.IsChecked = true;
        CropOverlay.CropChanged += _ => UpdateInfo();

        // AppWindow.Resize 用物理像素，需按显示缩放换算；在内容加载后取实际缩放比
        ((FrameworkElement)Content).Loaded += async (_, _) =>
        {
            try
            {
                double scale = ((FrameworkElement)Content).XamlRoot.RasterizationScale;
                AppWindow.Resize(new SizeInt32((int)(1100 * scale), (int)(760 * scale)));
                if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.PreferredMinimumWidth = (int)(860 * scale);
                    presenter.PreferredMinimumHeight = (int)(620 * scale);
                }
            }
            catch
            {
                // 缩放信息不可用时跳过
            }

            // 支持命令行直接打开图片（如 ImageResize.exe 图片路径），也便于自动化验证
            var argv = Environment.GetCommandLineArgs();
            if (argv.Length > 1 && File.Exists(argv[1]))
            {
                await LoadAsync(argv[1]);
            }
        };

        UpdateModeUi();
    }

    // ---------------------------------------------------------------- 加载

    private async void BtnPick_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        foreach (var ext in new[]
                 { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".heic", ".heif", ".tif", ".tiff" })
        {
            picker.FileTypeFilter.Add(ext);
        }
        var file = await picker.PickSingleFileAsync();
        if (file != null) await LoadAsync(file.Path);
    }

    private async Task LoadAsync(string path)
    {
        if (_busy) return;
        SetBusy(true);
        HideFeedback();
        InfoText.Text = "加载中…";
        try
        {
            var img = await Task.Run(() => ImageLoader.Load(path));
            var old = _current;
            _current = img.Bitmap;
            _source = img;
            _sourcePath = path;
            await ShowBitmapAsync();
            old?.Dispose();
            CropOverlay.ResetToFull();
            SyncImageRect();
        }
        catch (Exception ex)
        {
            ShowFeedback($"加载失败：{ex.Message}", InfoBarSeverity.Error, autoClose: false);
            UpdateInfo();
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------------------------------------------------------------- 剪裁 / 缩放

    private void BtnApplyCrop_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        Rect norm = CropOverlay.CropRectNorm;
        if (norm.Width >= 0.999 && norm.Height >= 0.999)
        {
            ShowFeedback("选框已覆盖全图，无需剪裁", InfoBarSeverity.Informational);
            return;
        }
        var normRect = new SKRect((float)norm.Left, (float)norm.Top, (float)norm.Right, (float)norm.Bottom);
        RunImageOp("剪裁", bmp => ImageOps.Crop(bmp, normRect));
    }

    private void BtnApplyResize_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        int percent = (int)PercentSlider.Value;
        if (percent >= 100)
        {
            ShowFeedback("比例已是 100%，无需缩放", InfoBarSeverity.Informational);
            return;
        }
        RunImageOp("缩放", bmp => ImageOps.ScaleByPercent(bmp, percent));
    }

    private async void RunImageOp(string name, Func<SkiaSharp.SKBitmap, SkiaSharp.SKBitmap> op)
    {
        var bmp = _current;
        if (bmp == null || _busy) return;
        SetBusy(true);
        try
        {
            var next = await Task.Run(() => op(bmp));
            var old = _current;
            _current = next;
            await ShowBitmapAsync();
            old?.Dispose();
            SyncImageRect();
            CropOverlay.ResetToFull();
            ShowFeedback($"{name}完成：{next.Width}×{next.Height}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowFeedback($"{name}失败：{ex.Message}", InfoBarSeverity.Error, autoClose: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------------------------------------------------------------- 保存

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var bmp = _current;
        var img = _source;
        if (bmp == null || img == null || _busy) return;

        // 保存保持原格式：HEIF 源无法编码，回退 JPEG（与参考实现一致）
        var (choiceLabel, ext) = img.Format switch
        {
            ImageFormatKind.Png => ("PNG 图片", ".png"),
            ImageFormatKind.Webp => ("WebP 图片", ".webp"),
            _ => ("JPEG 图片", ".jpg"),
        };

        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        var stem = _sourcePath is null ? "image" : System.IO.Path.GetFileNameWithoutExtension(_sourcePath);
        picker.SuggestedFileName = $"{stem}-edit";
        picker.FileTypeChoices.Add(choiceLabel, new List<string> { ext });

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        SetBusy(true);
        try
        {
            // 恒定压缩率编码：与源图保持相同的每像素字节
            var encoded = await Task.Run(() => ImageEncoder.Encode(
                bmp, img.Format, img.SourceFileBytes,
                (long)img.SourceWidth * img.SourceHeight, img.WebpLossless));
            await File.WriteAllBytesAsync(file.Path, encoded.Bytes);
            long kb = (encoded.Bytes.Length + 1023) / 1024;
            ShowFeedback($"已保存：{file.Path}（{kb} KB）", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowFeedback($"保存失败：{ex.Message}", InfoBarSeverity.Error, autoClose: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------------------------------------------------------------- 拖放

    private void CanvasHost_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
    }

    private async void CanvasHost_Drop(object sender, DragEventArgs e)
    {
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var file = items.OfType<Windows.Storage.StorageFile>().FirstOrDefault();
            if (file != null) await LoadAsync(file.Path);
            e.Handled = true;
        }
        catch
        {
            // 非文件拖放，忽略
        }
    }

    // ---------------------------------------------------------------- UI 状态

    private SoftwareBitmapSource? _imageSource;

    private async Task ShowBitmapAsync()
    {
        _imageSource?.Dispose();
        _imageSource = null;
        if (_current == null)
        {
            ImageCtrl.Source = null;
            return;
        }
        var source = await SkiaImageSource.ToImageSourceAsync(_current);
        _imageSource = source;
        ImageCtrl.Source = source;
    }

    /// <summary>计算位图在画布中的实际显示区域（fitCenter），同步给剪裁覆盖层。</summary>
    private void SyncImageRect()
    {
        var bmp = _current;
        double dw = CanvasHost.ActualWidth;
        double dh = CanvasHost.ActualHeight;
        if (bmp == null || dw <= 0 || dh <= 0)
        {
            CropOverlay.SetImageRect(null);
            return;
        }
        double scale = Math.Min(dw / bmp.Width, dh / bmp.Height);
        double w = bmp.Width * scale;
        double h = bmp.Height * scale;
        double left = (dw - w) / 2;
        double top = (dh - h) / 2;
        CropOverlay.SetImageRect(new Rect(left, top, w, h));
    }

    private void CanvasHost_SizeChanged(object sender, SizeChangedEventArgs e) => SyncImageRect();

    private void BtnFull_Click(object sender, RoutedEventArgs e)
    {
        CropOverlay.ResetToFull();
        UpdateInfo();
    }

    private void BtnQuick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && int.TryParse(b.Tag?.ToString(), out var percent))
        {
            PercentSlider.Value = percent;
            PercentText.Text = $"{percent}%";
            UpdateInfo();
        }
    }

    private void PercentSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (PercentText == null) return;
        PercentText.Text = $"{(int)PercentSlider.Value}%";
        UpdateInfo();
    }

    private void Mode_Checked(object sender, RoutedEventArgs e) => UpdateModeUi();

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BtnPick.IsEnabled = !busy;
        UpdateModeUi();
    }

    private void UpdateModeUi()
    {
        bool has = _current != null;
        bool cropMode = ModeCrop.IsChecked == true;
        bool enabled = has && !_busy;

        EmptyHint.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        CropOverlay.Visibility = has && cropMode ? Visibility.Visible : Visibility.Collapsed;
        CropControls.Visibility = has && cropMode ? Visibility.Visible : Visibility.Collapsed;
        ResizeControls.Visibility = has && !cropMode ? Visibility.Visible : Visibility.Collapsed;

        CropOverlay.IsHitTestVisible = has && cropMode && !_busy;
        BtnSave.IsEnabled = enabled;
        BtnFull.IsEnabled = enabled;
        BtnApplyCrop.IsEnabled = enabled;
        BtnApplyResize.IsEnabled = enabled;
        Btn25.IsEnabled = enabled;
        Btn50.IsEnabled = enabled;
        Btn75.IsEnabled = enabled;
        PercentSlider.IsEnabled = enabled;
        ModeCrop.IsEnabled = !_busy;
        ModeResize.IsEnabled = !_busy;
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        var bmp = _current;
        var img = _source;
        if (bmp == null || img == null)
        {
            InfoText.Text = "请选择一张图片开始";
            return;
        }
        string label = FormatLabel(img);
        if (ModeCrop.IsChecked == true)
        {
            Rect n = CropOverlay.CropRectNorm;
            int cw = Math.Max(1, (int)Math.Round(n.Width * bmp.Width));
            int ch = Math.Max(1, (int)Math.Round(n.Height * bmp.Height));
            InfoText.Text = $"当前 {bmp.Width}×{bmp.Height} · 剪裁区域 {cw}×{ch}" +
                            $" · 原图 {img.SourceWidth}×{img.SourceHeight} {label}";
        }
        else
        {
            int p = (int)PercentSlider.Value;
            int tw = ImageMath.TargetSize(bmp.Width, p);
            int th = ImageMath.TargetSize(bmp.Height, p);
            InfoText.Text = $"比例 {p}%：{bmp.Width}×{bmp.Height} → {tw}×{th}" +
                            $" · 原图 {img.SourceWidth}×{img.SourceHeight} {label}";
        }
    }

    private static string FormatLabel(LoadedImage img) => img.Format switch
    {
        ImageFormatKind.Jpeg => "JPEG",
        ImageFormatKind.Png => "PNG",
        ImageFormatKind.Webp => img.WebpLossless ? "WebP(无损)" : "WebP",
        ImageFormatKind.Heif => "HEIF→JPEG",
        _ => "JPEG",
    };

    private void ShowFeedback(string message, InfoBarSeverity severity, bool autoClose = true)
    {
        Feedback.Severity = severity;
        Feedback.Message = message;
        Feedback.IsOpen = true;
        if (!autoClose) return;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(4);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (Feedback.Message == message) Feedback.IsOpen = false;
        };
        timer.Start();
    }

    private void HideFeedback() => Feedback.IsOpen = false;
}
