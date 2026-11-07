using SkiaSharp;

namespace ImageResize.Services;

/// <summary>
/// 恒定压缩率编码：目标字节数 = max(4096, 源文件字节数 × 输出像素 / 源像素)。
/// 保证缩小后的文件不会比原图大，且信息密度与原图一致。
/// PNG / 无损 WebP 直通无损；JPEG / 有损 WebP / HEIF / 未知 → 按目标大小二分搜质量。
/// </summary>
public static class ImageEncoder
{
    /// <summary>质量下限：避免画质崩坏出现明显色块。</summary>
    public const int MinQuality = 40;

    /// <summary>质量上限：平台编码器已能达到的极限清晰度。</summary>
    public const int MaxQuality = 97;

    public sealed record Encoded(byte[] Bytes, string Mime, string Ext);

    public static Encoded Encode(SKBitmap bitmap, ImageFormatKind sourceKind,
        long sourceFileBytes, long sourcePixels, bool sourceWebpLossless)
    {
        var outPixels = (long)bitmap.Width * bitmap.Height;
        // 与源图保持相同的每像素字节数，即相同压缩率 / 信息密度。
        var targetBytes = Math.Max(4096L, sourceFileBytes * outPixels / sourcePixels);

        return sourceKind switch
        {
            ImageFormatKind.Png => EncodePng(bitmap),
            ImageFormatKind.Webp when sourceWebpLossless => EncodeWebpLossless(bitmap),
            ImageFormatKind.Webp => EncodeByTarget(bitmap, SKEncodedImageFormat.Webp, targetBytes, "image/webp", "webp"),
            // JPEG、HEIF、未知格式 → JPEG
            _ => EncodeByTarget(bitmap, SKEncodedImageFormat.Jpeg, targetBytes, "image/jpeg", "jpg"),
        };
    }

    /// <summary>无损格式（PNG）无论缩多少倍，都保持无损直通编码。</summary>
    private static Encoded EncodePng(SKBitmap bitmap) =>
        new(Compress(bitmap, SKEncodedImageFormat.Png, 100), "image/png", "png");

    /// <summary>无损 WebP 源保持无损编码（SKImage 无 WebP 选项重载，使用 SKPixmap.Encode）。</summary>
    private static Encoded EncodeWebpLossless(SKBitmap bitmap)
    {
        using var pixmap = bitmap.PeekPixels() ?? throw new InvalidOperationException("编码失败");
        using var data = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100))
            ?? throw new InvalidOperationException("WebP 编码失败");
        return new Encoded(data.ToArray(), "image/webp", "webp");
    }

    /// <summary>恒定压缩率：二分搜索不超过目标字节数的最高质量。</summary>
    private static Encoded EncodeByTarget(SKBitmap bitmap, SKEncodedImageFormat format,
        long targetBytes, string mime, string ext)
    {
        var quality = ImageMath.SolveQuality(
            encode: q => Compress(bitmap, format, q).LongLength,
            targetBytes: targetBytes,
            minQuality: MinQuality,
            maxQuality: MaxQuality);
        return new Encoded(Compress(bitmap, format, quality), mime, ext);
    }

    private static byte[] Compress(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality) ?? throw new InvalidOperationException("编码失败");
        return data.ToArray();
    }
}
