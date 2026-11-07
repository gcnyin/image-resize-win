namespace ImageResize.Services;

public enum ImageFormatKind
{
    Jpeg,
    Png,
    Webp,
    Heif,
    Unknown,
}

/// <summary>图片格式识别（按文件头魔数），用于保存时还原原格式。</summary>
public static class ImageFormat
{
    private static readonly HashSet<string> HeifBrands = new(StringComparer.Ordinal)
    {
        "heic", "heix", "hevc", "hevx", "heim", "heis", "hevm", "hevs", "mif1", "msf1",
    };

    /// <summary>按文件头魔数判断格式。</summary>
    public static ImageFormatKind Sniff(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return ImageFormatKind.Jpeg;
        if (header.Length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return ImageFormatKind.Png;
        if (header.Length >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            return ImageFormatKind.Webp;
        if (header.Length >= 12 &&
            header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
        {
            var brand = System.Text.Encoding.ASCII.GetString(header.Slice(8, 4));
            return HeifBrands.Contains(brand) ? ImageFormatKind.Heif : ImageFormatKind.Unknown;
        }
        return ImageFormatKind.Unknown;
    }

    /// <summary>WebP 是否为无损（存在 VP8L chunk）：简单 WebP 的 chunk 在偏移 12，扩展 WebP（VP8X 头之后）约 30。</summary>
    public static bool IsWebpLossless(ReadOnlySpan<byte> header)
    {
        static bool Vp8l(ReadOnlySpan<byte> h, int offset) =>
            offset + 4 <= h.Length &&
            h[offset] == 0x56 && h[offset + 1] == 0x50 && h[offset + 2] == 0x38 && h[offset + 3] == 0x4C;
        return Vp8l(header, 12) || Vp8l(header, 30);
    }

    /// <summary>探测文件的格式：先魔数嗅探，UNKNOWN 时按扩展名兜底。返回格式与读取到的文件头。</summary>
    public static (ImageFormatKind Kind, byte[] Header) Detect(string path)
    {
        var header = ReadHeader(path, 48);
        var kind = Sniff(header);
        if (kind != ImageFormatKind.Unknown) return (kind, header);

        kind = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ImageFormatKind.Jpeg,
            ".png" => ImageFormatKind.Png,
            ".webp" => ImageFormatKind.Webp,
            ".heic" or ".heif" => ImageFormatKind.Heif,
            _ => ImageFormatKind.Unknown,
        };
        return (kind, header);
    }

    private static byte[] ReadHeader(string path, int count)
    {
        using var fs = File.OpenRead(path);
        var buf = new byte[count];
        var off = 0;
        while (off < count)
        {
            var n = fs.Read(buf, off, count - off);
            if (n <= 0) break;
            off += n;
        }
        return off == buf.Length ? buf : buf[..off];
    }
}
