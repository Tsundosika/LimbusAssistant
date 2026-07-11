namespace Tsundosika.LimbusAssistant.Vision;

public static class FrameSourceFactory
{
    public static IFrameSource Create(IntPtr windowHandle)
    {
        if (WgcFrameSource.IsSupported)
        {
            try
            {
                return new WgcFrameSource(windowHandle);
            }
            catch (Exception)
            {
            }
        }
        return new GdiFrameSource(windowHandle);
    }
}
