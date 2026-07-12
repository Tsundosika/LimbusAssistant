using OpenCvSharp;

namespace Tsundosika.LimbusAssistant.Vision;

public static class DockScanner
{
    const int MaxSlots = 6;

    public static readonly NormalizedRect DockBand = new(0.20, 0.90, 0.75, 0.10);

    public static readonly NormalizedRect FieldBand = new(0.05, 0.20, 0.90, 0.60);

    public static List<PixelRect> FindSanityCircles(Mat frameBgra, PixelRect content) =>
        FindSanityCircles(frameBgra, content, DockBand);

    public static List<PixelRect> FindSanityCircles(Mat frameBgra, PixelRect content, NormalizedRect searchBand)
    {
        var band = searchBand.ToPixelsWithin(content);
        if (band.Width < 8 || band.Height < 8)
        {
            return [];
        }
        using var view = frameBgra[new Rect(band.X, band.Y, band.Width, band.Height)];
        using var bgr = new Mat();
        Cv2.CvtColor(view, bgr, ColorConversionCodes.BGRA2BGR);
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(95, 110, 100), new Scalar(135, 255, 255), mask);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var circles = new List<PixelRect>();
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            var area = rect.Width * rect.Height;
            var aspect = rect.Height == 0 ? 0 : rect.Width / (double)rect.Height;
            if (area < 400 || area > 6000 || aspect < 0.6 || aspect > 1.6 || rect.Width > 66 || rect.Height > 66)
            {
                continue;
            }
            circles.Add(new PixelRect(
                band.X + rect.X - 8,
                band.Y + rect.Y - 8,
                rect.Width + 16,
                rect.Height + 16));
        }
        return circles
            .OrderBy(rect => rect.X)
            .Take(MaxSlots)
            .ToList();
    }

    public static Mat ComposeStrip(Mat frameBgra, IReadOnlyList<PixelRect> circles)
    {
        const int tileHeight = 64;
        const int gap = 20;
        var tiles = new List<Mat>();
        var totalWidth = gap;
        foreach (var circle in circles)
        {
            var x = Math.Clamp(circle.X, 0, Math.Max(0, frameBgra.Width - 2));
            var y = Math.Clamp(circle.Y, 0, Math.Max(0, frameBgra.Height - 2));
            var width = Math.Clamp(circle.Width, 2, frameBgra.Width - x);
            var height = Math.Clamp(circle.Height, 2, frameBgra.Height - y);
            using var view = frameBgra[new Rect(x, y, width, height)];
            using var bgr = new Mat();
            Cv2.CvtColor(view, bgr, ColorConversionCodes.BGRA2BGR);
            var tile = new Mat();
            var tileWidth = Math.Max(2, width * tileHeight / height);
            Cv2.Resize(bgr, tile, new Size(tileWidth, tileHeight), interpolation: InterpolationFlags.Cubic);
            tiles.Add(tile);
            totalWidth += tileWidth + gap;
        }
        var strip = new Mat(tileHeight + gap * 2, Math.Max(2, totalWidth), MatType.CV_8UC3, new Scalar(0, 0, 0));
        var offset = gap;
        foreach (var tile in tiles)
        {
            tile.CopyTo(strip[new Rect(offset, gap, tile.Width, tile.Height)]);
            offset += tile.Width + gap;
            tile.Dispose();
        }
        return strip;
    }
}
