using SkiaSharp;

namespace ImageResize.Services;

/// <summary>缩放与按百分比缩小操作。</summary>
public static class ImageOps
{
    private static readonly SKSamplingOptions LinearSampling =
        new(SKFilterMode.Linear, SKMipmapMode.None);

    /// <summary>
    /// 按百分比缩小图片。为避免一次缩小带来的锯齿，先按 1/2 逐级下采样；
    /// 目标尺寸到达后，再做一次线性插值缩放到精确目标尺寸。
    /// 缩放不变形，只改变边长尺寸。
    /// </summary>
    public static SKBitmap ScaleByPercent(SKBitmap src, int percent)
    {
        if (percent is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(percent), "百分比须在 1~100 之间");
        var targetW = ImageMath.TargetSize(src.Width, percent);
        var targetH = ImageMath.TargetSize(src.Height, percent);
        if (targetW == src.Width && targetH == src.Height) return src;

        var cur = src;
        while (cur.Width / 2 >= targetW && cur.Height / 2 >= targetH)
        {
            cur = Scale(cur, cur.Width / 2, cur.Height / 2);
        }
        if (cur.Width != targetW || cur.Height != targetH)
        {
            cur = Scale(cur, targetW, targetH);
        }
        return cur;
    }

    /// <summary>裁剪一个 [0,1] 归一化矩形内的位图。若覆盖全图则原样返回。</summary>
    public static SKBitmap Crop(SKBitmap src, SKRect norm)
    {
        var x = Math.Clamp((int)MathF.Round(norm.Left * src.Width), 0, src.Width - 1);
        var y = Math.Clamp((int)MathF.Round(norm.Top * src.Height), 0, src.Height - 1);
        var w = Math.Clamp((int)MathF.Round(norm.Width * src.Width), 1, src.Width - x);
        var h = Math.Clamp((int)MathF.Round(norm.Height * src.Height), 1, src.Height - y);
        if (x == 0 && y == 0 && w == src.Width && h == src.Height) return src;

        var sub = new SKBitmap();
        if (!src.ExtractSubset(sub, new SKRectI(x, y, x + w, y + h)))
            throw new InvalidOperationException("裁剪失败");
        var copy = sub.Copy();
        if (copy == null) throw new InvalidOperationException("裁剪失败");
        sub.Dispose();
        return copy;
    }

    /// <summary>逐级 1/2 下采样，直到最长边不超过 <paramref name="maxDim"/>。</summary>
    public static SKBitmap DownsampleToCap(SKBitmap src, int maxDim)
    {
        var cur = src;
        while (Math.Max(cur.Width, cur.Height) > maxDim)
        {
            var w = Math.Max(1, cur.Width / 2);
            var h = Math.Max(1, cur.Height / 2);
            cur = Scale(cur, w, h);
        }
        return cur;
    }

    private static SKBitmap Scale(SKBitmap src, int width, int height)
    {
        var dst = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var srcPixmap = src.PeekPixels();
        using var dstPixmap = dst.PeekPixels();
        if (srcPixmap == null || dstPixmap == null || !srcPixmap.ScalePixels(dstPixmap, LinearSampling))
        {
            dst.Dispose();
            throw new InvalidOperationException("缩放失败");
        }
        return dst;
    }
}
