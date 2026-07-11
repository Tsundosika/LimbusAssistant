namespace Tsundosika.LimbusAssistant.Vision;

public sealed record CaptureFrame(byte[] PixelsBgra, int Width, int Height)
{
    public int Stride => Width * 4;
}
