namespace Tsundosika.LimbusAssistant.Vision;

public static class FrameHash
{
    const ulong OffsetBasis = 14695981039346656037;
    const ulong Prime = 1099511628211;

    public static ulong SampleFrame(CaptureFrame frame)
    {
        var hash = OffsetBasis;
        for (var y = 0; y < frame.Height; y += 16)
        {
            var rowOffset = y * frame.Stride;
            for (var x = 0; x < frame.Width; x += 32)
            {
                hash = (hash ^ frame.PixelsBgra[rowOffset + x * 4 + 1]) * Prime;
            }
        }
        return hash;
    }

    public static ulong SampleRegion(CaptureFrame frame, PixelRect region)
    {
        var hash = OffsetBasis;
        var maxY = Math.Min(frame.Height, region.Y + region.Height);
        var maxX = Math.Min(frame.Width, region.X + region.Width);
        for (var y = Math.Max(0, region.Y); y < maxY; y += 2)
        {
            var rowOffset = y * frame.Stride;
            for (var x = Math.Max(0, region.X); x < maxX; x += 2)
            {
                hash = (hash ^ frame.PixelsBgra[rowOffset + x * 4 + 1]) * Prime;
            }
        }
        return hash;
    }
}
