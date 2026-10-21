using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

internal sealed class PreviewBitmapLodCache
{
    private const long PixelBudget = 8 * 1024 * 1024;
    private readonly ConditionalWeakTable<RenderTargetBitmap, Dictionary<int, LinkedListNode<Entry>>> _levels = new();
    private readonly LinkedList<Entry> _lru = new();
    private long _pixels;

    internal RenderTargetBitmap Get(RenderTargetBitmap source, double width, double height, double zoom)
    {
        double safeZoom = zoom > 0 ? zoom : 1;
        double ratio = Math.Min(source.PixelSize.Width / Math.Max(1, width * safeZoom),
            source.PixelSize.Height / Math.Max(1, height * safeZoom));
        if (ratio < 2 || !double.IsFinite(ratio)) return source;
        int level = Math.Clamp((int)Math.Floor(Math.Log2(ratio)), 1, 30);
        var levels = _levels.GetValue(source, _ => new());
        if (levels.TryGetValue(level, out var cached))
        {
            _lru.Remove(cached);
            _lru.AddLast(cached);
            return cached.Value.Bitmap;
        }
        double divisor = Math.Pow(2, level);
        int w = Math.Max(1, (int)Math.Ceiling(source.PixelSize.Width / divisor));
        int h = Math.Max(1, (int)Math.Ceiling(source.PixelSize.Height / divisor));
        if ((long)w * h > PixelBudget) return source;
        RenderTargetBitmap? bitmap = null;
        try
        {
            bitmap = new RenderTargetBitmap(new PixelSize(w, h));
            using (var context = bitmap.CreateDrawingContext())
                context.DrawImage(source, new Rect(0, 0, w, h));
        }
        catch
        {
            bitmap?.Dispose();
            return source;
        }
        var entry = new Entry(levels, level, bitmap, (long)w * h);
        levels.Add(level, _lru.AddLast(entry));
        _pixels += entry.Pixels;
        while ((_pixels > PixelBudget || _lru.Count > 64) && _lru.First is { } oldest)
        {
            _lru.RemoveFirst();
            oldest.Value.Levels.Remove(oldest.Value.Level);
            _pixels -= oldest.Value.Pixels;
            // Recorded frames can still hold this bitmap for a short time.
            var evicted = oldest.Value.Bitmap;
            _ = Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(
                _ => Dispatcher.UIThread.Post(evicted.Dispose, DispatcherPriority.Background), TaskScheduler.Default);
        }
        return bitmap;
    }

    private sealed record Entry(Dictionary<int, LinkedListNode<Entry>> Levels, int Level, RenderTargetBitmap Bitmap, long Pixels);
}
