using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Services;

/// <summary>
/// Where the photo widget's pictures come from and how they are framed. The file lists and dates are read in the
/// background; pictures are decoded at the widget's size only (never whole 24-megapixel originals in memory).
/// </summary>
public static class PhotoLibrary
{
    public static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".heic"];

    public static bool IsPicture(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>The pictures in a folder (and its subfolders, if asked), at most <paramref name="limit"/>.</summary>
    public static List<string> Scan(string folder, bool subfolders, int limit = 3000)
    {
        var found = new List<string>();
        if (!Directory.Exists(folder)) return found;
        var opts = new EnumerationOptions { RecurseSubdirectories = subfolders, MaxRecursionDepth = 6, IgnoreInaccessible = true };
        foreach (var f in Directory.EnumerateFiles(folder, "*", opts))
        {
            if (IsPicture(f)) found.Add(f);
            if (found.Count >= limit) break;
        }
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    /// <summary>The order to show them in: as they are, or shuffled (the same shuffle for the same seed).</summary>
    public static List<string> Order(IReadOnlyList<string> files, bool shuffle, int seed)
    {
        var list = files.ToList();
        if (!shuffle) return list;
        var rnd = new Random(seed);
        for (int i = list.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        return list;
    }

    /// <summary>EXIF "2019:06:14 18:03:22" (DateTimeOriginal) to a date; null when it isn't one.</summary>
    public static DateTime? ParseExifDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().TrimEnd('\0');
        foreach (var f in new[] { "yyyy:MM:dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy:MM:dd", "yyyy-MM-ddTHH:mm:ss" })
            if (DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year > 1900) return d;
        return null;
    }

    /// <summary>When a photo was taken: EXIF first (read without decoding the picture), else the file's date.</summary>
    public static DateTime TakenOn(string path, out bool fromExif)
    {
        fromExif = false;
        try
        {
            using var fs = File.OpenRead(path);
            var frame = BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            if (frame.Metadata is BitmapMetadata m)
            {
                // System.Photo.DateTaken comes as a FILETIME-ish string in WPF's DateTaken; the raw EXIF tag is safer
                foreach (var q in new[] { "/app1/ifd/exif/{ushort=36867}", "/app1/ifd/exif/{ushort=36868}", "/app1/ifd/{ushort=306}" })
                {
                    try
                    {
                        if (m.ContainsQuery(q) && ParseExifDate(m.GetQuery(q) as string) is { } d) { fromExif = true; return d; }
                    }
                    catch { /* this format has no such block */ }
                }
                if (m.DateTaken is { } dt && DateTime.TryParse(dt, out var parsed)) { fromExif = true; return parsed; }
            }
        }
        catch (Exception e) { Log.Write($"photos: date of {Path.GetFileName(path)}: {e.Message}"); }
        try { return File.GetLastWriteTime(path); } catch { return DateTime.MinValue; }
    }

    /// <summary>"This day": photos taken on today's day and month in earlier years, newest year first.</summary>
    public static List<string> ThisDay(IEnumerable<(string Path, DateTime Taken)> photos, DateTime today) =>
        photos.Where(p => p.Taken.Month == today.Month && p.Taken.Day == today.Day && p.Taken.Year < today.Year)
              .OrderByDescending(p => p.Taken).Select(p => p.Path).ToList();

    /// <summary>"3 года назад", "год назад" — for the "this day" caption.</summary>
    public static string YearsAgo(DateTime taken, DateTime today)
    {
        int y = today.Year - taken.Year;
        if (y <= 1) return L.T("год назад");
        if (y % 10 == 1 && y % 100 != 11) return L.F("{0} год назад", y);
        if (y % 10 is >= 2 and <= 4 && y % 100 is < 12 or > 14) return L.F("{0} года назад", y);
        return L.F("{0} лет назад", y);
    }

    /// <summary>
    /// The part of the picture to show (relative to the picture, 0..1) so that it fills a view of this shape with the
    /// focus point as close to the middle as the picture allows — nothing important is cut when the widget changes size.
    /// </summary>
    public static Rect Crop(double imageW, double imageH, double viewW, double viewH, double fx, double fy)
    {
        if (imageW <= 0 || imageH <= 0 || viewW <= 0 || viewH <= 0) return new Rect(0, 0, 1, 1);
        double imageAspect = imageW / imageH, viewAspect = viewW / viewH;
        double w = 1, h = 1;
        if (imageAspect > viewAspect) w = viewAspect / imageAspect; else h = imageAspect / viewAspect;
        double x = Math.Clamp(Math.Clamp(fx, 0, 1) - w / 2, 0, 1 - w), y = Math.Clamp(Math.Clamp(fy, 0, 1) - h / 2, 0, 1 - h);
        return new Rect(x, y, w, h);
    }

    /// <summary>The middle of the faces found (relative 0..1), weighted by their size; null when there are none.</summary>
    public static Point? FacesCenter(IReadOnlyList<Rect> faces, double imageW, double imageH)
    {
        if (faces.Count == 0 || imageW <= 0 || imageH <= 0) return null;
        double sx = 0, sy = 0, sw = 0;
        foreach (var f in faces)
        {
            double a = f.Width * f.Height;
            sx += (f.X + f.Width / 2) * a; sy += (f.Y + f.Height / 2) * a; sw += a;
        }
        // a face sits in the upper part of the subject: aim a little below it, so heads aren't cut at the chin
        return new Point(Math.Clamp(sx / sw / imageW, 0, 1), Math.Clamp(sy / sw / imageH + 0.06, 0, 1));
    }

    /// <summary>The faces' centre in a picture, by Windows' own face detector (null when none or it fails).</summary>
    public static async Task<Point?> DetectFacesAsync(string path)
    {
        try
        {
            if (!Windows.Media.FaceAnalysis.FaceDetector.IsSupported) return null;
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            // small and grey is all the detector needs (and cheap)
            double k = Math.Min(1, 640.0 / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var transform = new Windows.Graphics.Imaging.BitmapTransform { ScaledWidth = (uint)(decoder.PixelWidth * k), ScaledHeight = (uint)(decoder.PixelHeight * k) };
            using var bitmap = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Gray8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
                                                                     transform, Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation,
                                                                     Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
            var detector = await Windows.Media.FaceAnalysis.FaceDetector.CreateAsync();
            var faces = await detector.DetectFacesAsync(bitmap);
            var rects = faces.Select(f => new Rect(f.FaceBox.X, f.FaceBox.Y, f.FaceBox.Width, f.FaceBox.Height)).ToList();
            return FacesCenter(rects, bitmap.PixelWidth, bitmap.PixelHeight);
        }
        catch (Exception e) { Log.Write($"photos: faces in {Path.GetFileName(path)}: {e.Message}"); return null; }
    }
}
