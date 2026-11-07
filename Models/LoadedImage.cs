using SkiaSharp;

namespace ImageResize.Models;

/// <summary>本次加载的源图片信息与当前工作区位图。</summary>
public sealed record LoadedImage(
    SKBitmap Bitmap,
    ImageResize.Services.ImageFormatKind Format,
    bool WebpLossless,
    int SourceWidth,
    int SourceHeight,
    long SourceFileBytes);
