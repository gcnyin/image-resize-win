using SkiaSharp;
using ImageResize.Models;

namespace ImageResize.Services;

/// <summary>图片加载流程：解码 → EXIF 摆正 → 限制工作尺寸。</summary>
public static class ImageLoader
{
    /// <summary>加载工作区的最长边上限，避免超大图片占用工作内存。</summary>
    public const int MaxWorkDim = 4096;

    public static LoadedImage Load(string path, int maxDim = MaxWorkDim)
    {
        var (kind, header) = ImageFormat.Detect(path);
        var fileBytes = new FileInfo(path).Length;

        using var codec = SKCodec.Create(path) ?? throw new InvalidOperationException("无法解析图片");

        // 摆正后的原始尺寸（宽高互换对应 90°/270° 方向）
        var origin = codec.EncodedOrigin;
        bool swap = origin is SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.LeftBottom;
        var sourceWidth = swap ? codec.Info.Height : codec.Info.Width;
        var sourceHeight = swap ? codec.Info.Width : codec.Info.Height;

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        // 不使用 using：raw 经 ExifOrientation 摆正后直接作为结果返回给调用方
        var raw = new SKBitmap(info);
        var result = codec.GetPixels(info, raw.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            throw new InvalidOperationException($"解码失败：{result}");

        var oriented = ExifOrientation.Apply(raw, codec.EncodedOrigin);
        var bitmap = ImageOps.DownsampleToCap(oriented, maxDim);

        return new LoadedImage(
            Bitmap: bitmap,
            Format: kind,
            WebpLossless: kind == ImageFormatKind.Webp && ImageFormat.IsWebpLossless(header),
            SourceWidth: sourceWidth,
            SourceHeight: sourceHeight,
            SourceFileBytes: fileBytes);
    }
}
