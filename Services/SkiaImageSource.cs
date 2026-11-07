using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ImageResize.Services;

/// <summary>SKBitmap → WinUI 位图桥接。</summary>
public static class SkiaImageSource
{
    /// <summary>
    /// 把 Bgra8888/Premul 位图转换为可在 UI 线程使用的 SoftwareBitmapSource。
    /// 须在 UI 线程调用（SetBitmapAsync 要求 XAML 线程）。
    /// </summary>
    public static async Task<SoftwareBitmapSource> ToImageSourceAsync(SKBitmap bitmap)
    {
        byte[] pixels = CopyPixels(bitmap);

        // IBuffer 在 CsWinRT 下没有 AsStream 扩展，用 DataWriter 装载像素
        var writer = new DataWriter();
        writer.WriteBytes(pixels);
        var buffer = writer.DetachBuffer();
        writer.Dispose();

        var software = SoftwareBitmap.CreateCopyFromBuffer(
            buffer, BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height,
            BitmapAlphaMode.Premultiplied);

        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(software);
        return source;
    }

    /// <summary>拷出像素并剔除行尾填充（RowBytes &gt; 4×宽时压缩为紧凑行）。</summary>
    private static byte[] CopyPixels(SKBitmap bitmap)
    {
        int srcStride = bitmap.RowBytes;
        int dstStride = bitmap.Width * 4;
        var managed = new byte[bitmap.Height * srcStride];
        Marshal.Copy(bitmap.GetPixels(), managed, 0, managed.Length);
        if (srcStride == dstStride) return managed;

        var compact = new byte[bitmap.Height * dstStride];
        for (int y = 0; y < bitmap.Height; y++)
        {
            System.Buffer.BlockCopy(managed, y * srcStride, compact, y * dstStride, dstStride);
        }
        return compact;
    }
}
