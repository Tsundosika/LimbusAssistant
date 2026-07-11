namespace Tsundosika.LimbusAssistant.Vision;

public sealed record NormalizedRect(double X, double Y, double Width, double Height)
{
    public PixelRect ToPixels(int frameWidth, int frameHeight)
    {
        var x = Math.Clamp((int)Math.Round(X * frameWidth), 0, Math.Max(0, frameWidth - 1));
        var y = Math.Clamp((int)Math.Round(Y * frameHeight), 0, Math.Max(0, frameHeight - 1));
        var width = Math.Clamp((int)Math.Round(Width * frameWidth), 1, frameWidth - x);
        var height = Math.Clamp((int)Math.Round(Height * frameHeight), 1, frameHeight - y);
        return new PixelRect(x, y, width, height);
    }

    public PixelRect ToPixelsWithin(PixelRect content)
    {
        var inner = ToPixels(content.Width, content.Height);
        return new PixelRect(content.X + inner.X, content.Y + inner.Y, inner.Width, inner.Height);
    }
}
