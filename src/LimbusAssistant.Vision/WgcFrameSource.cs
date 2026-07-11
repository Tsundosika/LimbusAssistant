using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Tsundosika.LimbusAssistant.Vision;

public sealed class WgcFrameSource : IFrameSource
{
    readonly ID3D11Device _device;
    readonly ID3D11DeviceContext _context;
    readonly IDirect3DDevice _winRtDevice;
    readonly GraphicsCaptureItem _item;
    readonly Direct3D11CaptureFramePool _framePool;
    readonly GraphicsCaptureSession _session;
    ID3D11Texture2D? _staging;
    SizeInt32 _size;
    bool _disposed;

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    public WgcFrameSource(IntPtr windowHandle)
    {
        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            null,
            out ID3D11Device? device).CheckError();
        _device = device ?? throw new InvalidOperationException("Direct3D 11 device creation failed.");
        _context = _device.ImmediateContext;
        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        _winRtDevice = CaptureInterop.CreateDirect3DDevice(dxgiDevice.NativePointer);
        _item = CaptureInterop.CreateItemForWindow(windowHandle);
        _size = _item.Size;
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winRtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            _size);
        _session = _framePool.CreateCaptureSession(_item);
        DisableCaptureDecorations();
        _session.StartCapture();
    }

    public CaptureFrame? TryCapture()
    {
        if (_disposed)
        {
            return null;
        }
        using var frame = _framePool.TryGetNextFrame();
        if (frame is null)
        {
            return null;
        }
        if (frame.ContentSize.Width != _size.Width || frame.ContentSize.Height != _size.Height)
        {
            _size = frame.ContentSize;
            _framePool.Recreate(_winRtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
            return null;
        }
        return CopyToFrame(frame);
    }

    CaptureFrame CopyToFrame(Direct3D11CaptureFrame frame)
    {
        var texturePointer = CaptureInterop.GetDxgiInterfacePointer(frame.Surface, CaptureInterop.D3D11Texture2DIid);
        using var texture = new ID3D11Texture2D(texturePointer);
        var description = texture.Description;
        EnsureStaging(description);
        _context.CopyResource(_staging!, texture);
        var width = Math.Min(_size.Width, (int)description.Width);
        var height = Math.Min(_size.Height, (int)description.Height);
        var pixels = new byte[width * height * 4];
        var mapped = _context.Map(_staging!, 0, MapMode.Read);
        try
        {
            unsafe
            {
                fixed (byte* destination = pixels)
                {
                    var source = (byte*)mapped.DataPointer;
                    for (var row = 0; row < height; row++)
                    {
                        Buffer.MemoryCopy(
                            source + (long)row * mapped.RowPitch,
                            destination + (long)row * width * 4,
                            width * 4,
                            width * 4);
                    }
                }
            }
        }
        finally
        {
            _context.Unmap(_staging!, 0);
        }
        return new CaptureFrame(pixels, width, height);
    }

    void EnsureStaging(Texture2DDescription source)
    {
        if (_staging is not null
            && _staging.Description.Width == source.Width
            && _staging.Description.Height == source.Height)
        {
            return;
        }
        _staging?.Dispose();
        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = source.Width,
            Height = source.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = source.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        });
    }

    void DisableCaptureDecorations()
    {
        try
        {
            _session.IsCursorCaptureEnabled = false;
        }
        catch (Exception)
        {
        }
        try
        {
            _session.IsBorderRequired = false;
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _session.Dispose();
        _framePool.Dispose();
        _winRtDevice.Dispose();
        _staging?.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
