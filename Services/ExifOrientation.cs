using SkiaSharp;

namespace ImageResize.Services;

/// <summary>按 EXIF 方向标记摆正位图。</summary>
public static class ExifOrientation
{
    public static SKBitmap Apply(SKBitmap src, SKEncodedOrigin origin)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopLeft:
                return src;
            case SKEncodedOrigin.TopRight:
                return Flip(src, horizontal: true);
            case SKEncodedOrigin.BottomRight:
                return Rotate(src, 180);
            case SKEncodedOrigin.BottomLeft:
                return Flip(src, horizontal: false);
            case SKEncodedOrigin.LeftTop:
                return Transpose(src);
            case SKEncodedOrigin.RightTop:
                return Rotate(src, 90);
            case SKEncodedOrigin.RightBottom:
                return Transverse(src);
            case SKEncodedOrigin.LeftBottom:
                return Rotate(src, 270);
            default:
                return src;
        }
    }

    /// <summary>顺时针旋转 90/180/270 度。</summary>
    private static SKBitmap Rotate(SKBitmap src, int degrees)
    {
        switch (degrees)
        {
            case 180:
            {
                // src(x, y) → dst(W-1-x, H-1-y)
                var dst = NewBitmap(src.Width, src.Height);
                var s = src.GetPixelSpan();
                var d = dst.GetPixelSpan();
                for (var y = 0; y < src.Height; y++)
                for (var x = 0; x < src.Width; x++)
                    CopyPixel(s, src.Width, x, y, d, src.Width, src.Width - 1 - x, src.Height - 1 - y);
                return dst;
            }
            case 90:
            {
                // 顺时针 90°：src(x, y) → dst(H-1-y, x)，dst 尺寸 H×W
                var dst = NewBitmap(src.Height, src.Width);
                var s = src.GetPixelSpan();
                var d = dst.GetPixelSpan();
                for (var y = 0; y < src.Height; y++)
                for (var x = 0; x < src.Width; x++)
                    CopyPixel(s, src.Width, x, y, d, src.Height, src.Height - 1 - y, x);
                return dst;
            }
            case 270:
            {
                // 顺时针 270°（逆时针 90°）：src(x, y) → dst(y, W-1-x)，dst 尺寸 H×W
                var dst = NewBitmap(src.Height, src.Width);
                var s = src.GetPixelSpan();
                var d = dst.GetPixelSpan();
                for (var y = 0; y < src.Height; y++)
                for (var x = 0; x < src.Width; x++)
                    CopyPixel(s, src.Width, x, y, d, src.Height, y, src.Width - 1 - x);
                return dst;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(degrees), "仅支持 90/180/270");
        }
    }

    private static SKBitmap Flip(SKBitmap src, bool horizontal)
    {
        var dst = NewBitmap(src.Width, src.Height);
        var s = src.GetPixelSpan();
        var d = dst.GetPixelSpan();
        for (var y = 0; y < src.Height; y++)
        for (var x = 0; x < src.Width; x++)
        {
            var sx = horizontal ? src.Width - 1 - x : x;
            var sy = horizontal ? y : src.Height - 1 - y;
            CopyPixel(s, src.Width, sx, sy, d, src.Width, x, y);
        }
        return dst;
    }

    /// <summary>沿主对角线转置：dst(x, y) = src(y, x)。</summary>
    private static SKBitmap Transpose(SKBitmap src)
    {
        var dst = NewBitmap(src.Height, src.Width);
        var s = src.GetPixelSpan();
        var d = dst.GetPixelSpan();
        for (var y = 0; y < src.Height; y++)
        for (var x = 0; x < src.Width; x++)
            CopyPixel(s, src.Width, x, y, d, src.Height, y, x);
        return dst;
    }

    /// <summary>沿副对角线转置：src(x, y) → dst(H-1-y, W-1-x)，dst 尺寸 H×W。</summary>
    private static SKBitmap Transverse(SKBitmap src)
    {
        var dst = NewBitmap(src.Height, src.Width);
        var s = src.GetPixelSpan();
        var d = dst.GetPixelSpan();
        for (var y = 0; y < src.Height; y++)
        for (var x = 0; x < src.Width; x++)
            CopyPixel(s, src.Width, x, y, d, src.Height, src.Height - 1 - y, src.Width - 1 - x);
        return dst;
    }

    private static SKBitmap NewBitmap(int width, int height) =>
        new(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));

    private static void CopyPixel(ReadOnlySpan<byte> src, int srcW, int sx, int sy,
        Span<byte> dst, int dstW, int dx, int dy)
    {
        var si = (sy * srcW + sx) * 4;
        var di = (dy * dstW + dx) * 4;
        dst[di] = src[si];
        dst[di + 1] = src[si + 1];
        dst[di + 2] = src[si + 2];
        dst[di + 3] = src[si + 3];
    }
}
