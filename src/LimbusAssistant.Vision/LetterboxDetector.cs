namespace Tsundosika.LimbusAssistant.Vision;

public static class LetterboxDetector
{
    const byte BlackThreshold = 18;
    const int SampleStep = 8;

    public static PixelRect DetectContent(CaptureFrame frame)
    {
        var top = FindEdge(frame, 0, frame.Height, 1, RowHasContent);
        var bottom = FindEdge(frame, frame.Height - 1, -1, -1, RowHasContent);
        var left = FindEdge(frame, 0, frame.Width, 1, ColumnHasContent);
        var right = FindEdge(frame, frame.Width - 1, -1, -1, ColumnHasContent);
        if (top < 0 || bottom <= top || left < 0 || right <= left)
        {
            return new PixelRect(0, 0, frame.Width, frame.Height);
        }
        return new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }

    static int FindEdge(CaptureFrame frame, int start, int end, int step, Func<CaptureFrame, int, bool> hasContent)
    {
        for (var index = start; index != end; index += step)
        {
            if (hasContent(frame, index))
            {
                return index;
            }
        }
        return -1;
    }

    static bool RowHasContent(CaptureFrame frame, int row)
    {
        var offset = row * frame.Stride;
        for (var x = 0; x < frame.Width; x += SampleStep)
        {
            if (IsBright(frame.PixelsBgra, offset + x * 4))
            {
                return true;
            }
        }
        return false;
    }

    static bool ColumnHasContent(CaptureFrame frame, int column)
    {
        var offset = column * 4;
        for (var y = 0; y < frame.Height; y += SampleStep)
        {
            if (IsBright(frame.PixelsBgra, y * frame.Stride + offset))
            {
                return true;
            }
        }
        return false;
    }

    static bool IsBright(byte[] pixels, int index) =>
        pixels[index] > BlackThreshold
        || pixels[index + 1] > BlackThreshold
        || pixels[index + 2] > BlackThreshold;
}
