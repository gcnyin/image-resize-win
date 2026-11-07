namespace ImageResize.Services;

/// <summary>目标尺寸与质量搜索的纯数学逻辑，独立于 Skia，便于单元测试。</summary>
public static class ImageMath
{
    /// <summary>按百分比计算目标边长，四舍五入，至少 1px。</summary>
    public static int TargetSize(int original, int percent) =>
        Math.Max(1, (int)MathF.Round(original * percent / 100.0f));

    /// <summary>解码降采样系数：最大的 2 的幂，使最长边不超过 <paramref name="maxDim"/>。</summary>
    public static int InSampleSizeForCap(int width, int height, int maxDim)
    {
        var sample = 1;
        var max = Math.Max(width, height);
        while (max / sample > maxDim) sample *= 2;
        return sample;
    }

    /// <summary>
    /// 在 [minQuality, maxQuality] 内二分搜索不超过 <paramref name="targetBytes"/> 的最大质量。
    /// <paramref name="encode"/> 必须是关于质量的单调（非严格）递增的字节数函数。
    /// 全部超目标时返回 minQuality，全部达标时返回 maxQuality。
    /// </summary>
    public static int SolveQuality(Func<int, long> encode, long targetBytes, int minQuality, int maxQuality)
    {
        if (minQuality > maxQuality) throw new ArgumentException("质量区间不合法");
        var lo = minQuality;
        var hi = maxQuality;
        var best = minQuality;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (encode(mid) <= targetBytes)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best;
    }
}
