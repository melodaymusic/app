using System.Security.Cryptography;
using System.Text;
using MelodayMusic.Core;
using MelodayMusic.Core.Playlists;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace MelodayMusic.Services;

public static class EssentialsCover
{
    private const float Size = 1000, Margin = 40, Join = 500;
    private const int Version = 18;

    public static async Task<string?> RenderAsync(string identity, string artistName, byte[]? backdrop, ILogger log)
    {
        var folder = Path.Combine(AppPaths.ArtworkCache, "essentials");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + "|" + artistName + "|" + (backdrop is null ? "-" : Convert.ToHexString(SHA256.HashData(backdrop))))));
        var path = Path.Combine(folder, $"v{Version}-{hash}.png");
        if (File.Exists(path)) return path;
        try
        {
            var device = CanvasDevice.GetSharedDevice();
            CanvasBitmap? picture = null;
            if (backdrop is not null)
            {
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(backdrop));
                stream.Seek(0);
                picture = await CanvasBitmap.LoadAsync(device, stream);
            }
            using var _ = picture;
            var colour = picture is null ? 0xA6C9BEu : Colour(device, picture);

            using var target = new CanvasRenderTarget(device, Size, Size, 96);
            using (var ds = target.CreateDrawingSession())
            {
                ds.Clear(Rgb(0xFFFFFF));
                ds.FillRectangle(0, Join, Size, Size - Join, Rgb(colour));
                if (picture is not null)
                {
                    var width = Size - 2 * Margin;
                    var height = width * 9 / 16;
                    var destination = new Rect(Margin, Join - height / 2, width, height);
                    ds.DrawImage(picture, destination, Crop(picture.SizeInPixels.Width, picture.SizeInPixels.Height, width / height), 1f, CanvasImageInterpolation.HighQualityCubic);
                }
                MelodayCover.DrawLabels(ds, "Essentials", string.Empty, Size, Size, log, Rgb(0x000000));
                var baseline = MelodayCover.DrawLabels(ds, string.Empty, artistName, Size, Size, log, Rgb(Essentials.InkIsDark(colour) ? 0x000000u : 0xFFFFFFu), labelLines: 1);
                CoverEdges.Draw(ds, colour, Size, baseline);
            }
            await CoverSymbol.DrawAsync(target);
            Directory.CreateDirectory(folder);
            await target.SaveAsync(path, CanvasBitmapFileFormat.Png);
            return path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "The Essentials cover for {Artist} could not be drawn", artistName);
            return null;
        }
    }

    private static Rect Crop(uint width, uint height, double frame)
    {
        double w = width, h = height;
        return w / h > frame
            ? new Rect((w - h * frame) / 2, 0, h * frame, h)
            : new Rect(0, (h - w / frame) / 2, w, w / frame);
    }

    private static uint Colour(CanvasDevice device, CanvasBitmap picture)
    {
        const int Small = 96;
        using var small = new CanvasRenderTarget(device, Small, Small * 9 / 16, 96);
        using (var ds = small.CreateDrawingSession())
            ds.DrawImage(picture, new Rect(0, 0, Small, Small * 9 / 16), Crop(picture.SizeInPixels.Width, picture.SizeInPixels.Height, 16.0 / 9));
        return Essentials.CoverColour(small.GetPixelBytes());
    }

    private static Windows.UI.Color Rgb(uint rgb) => Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
