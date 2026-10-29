using System.Runtime.InteropServices;
using System.Text;
using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Playback;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.DirectX;
using Windows.Foundation;

namespace MelodayMusic.Controls;

internal sealed class ProjectMPainter(VisualizerStyle style) : IVisualPainter, IDisposable
{
    private const string Library = "Meloday.ProjectM.dll";
    private IntPtr _host;
    private CanvasBitmap? _bitmap;
    private byte[] _pixels = [];
    private readonly float[] _pcm = new float[2048];
    private readonly StringBuilder _error = new(1024);
    private int _width, _height;

    public void Draw(CanvasDrawingSession ds, VisualInput input, float width, float height)
    {
        if (_host == IntPtr.Zero)
        {
            var preset = Path.Combine(AppContext.BaseDirectory, "Assets", "ProjectM", "Presets", VisualizerStyles.PresetFile(style)!);
            _error.Clear();
            _host = Create(preset, _error, _error.Capacity);
            if (_host == IntPtr.Zero) throw new InvalidOperationException(_error.ToString());
        }
        var scale = Math.Min(input.RenderScale, Math.Min(1, Math.Min(1280 / Math.Max(width, height), Math.Sqrt(921600 / (width * height)))));
        int w = Math.Max(1, (int)Math.Ceiling(width * scale)), h = Math.Max(1, (int)Math.Ceiling(height * scale));
        if (_bitmap is null || w != _width || h != _height)
        {
            _bitmap?.Dispose();
            _pixels = new byte[w * h * 4];
            _bitmap = CanvasBitmap.CreateFromBytes(ds.Device, _pixels, w, h, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Ignore);
            (_width, _height) = (w, h);
        }
        var count = ProjectMAudio.CopyStereoWindow(input.Pcm, input.SampleRate, _pcm);
        _error.Clear();
        if (Render(_host, _pcm, count == 0 ? _pcm.Length / 2 : count, w, h, input.FramesPerSecond, _pixels, _error, _error.Capacity) == 0)
            throw new InvalidOperationException(_error.ToString());
        _bitmap.SetPixelBytes(_pixels);
        ds.DrawImage(_bitmap, new Rect(0, 0, width, height), new Rect(0, 0, w, h));
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        if (_host != IntPtr.Zero) Destroy(_host);
        _host = IntPtr.Zero;
    }

    [DllImport(Library, EntryPoint = "meloday_projectm_create", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern IntPtr Create(string preset, [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder error, int capacity);
    [DllImport(Library, EntryPoint = "meloday_projectm_render", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Render(IntPtr host, [In] float[] pcm, int count, int width, int height, int fps, [Out] byte[] pixels, [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder error, int capacity);
    [DllImport(Library, EntryPoint = "meloday_projectm_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Destroy(IntPtr host);
}
