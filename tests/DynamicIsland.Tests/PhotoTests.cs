using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class PhotoLibraryTests
{
    [Theory]
    [InlineData("2019:06:14 18:03:22", 2019, 6, 14)]
    [InlineData("2019-06-14 18:03:22", 2019, 6, 14)]
    [InlineData("2021:12:31 00:00:00\0", 2021, 12, 31)]
    public void ParsesExifDates(string s, int y, int m, int d) => Assert.Equal(new DateTime(y, m, d), PhotoLibrary.ParseExifDate(s)!.Value.Date);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000:00:00 00:00:00")]
    [InlineData("вчера")]
    public void RejectsNonDates(string? s) => Assert.Null(PhotoLibrary.ParseExifDate(s));

    [Fact]
    public void ThisDayTakesEarlierYearsOnly_NewestFirst()
    {
        var today = new DateTime(2026, 11, 18);
        var photos = new (string, DateTime)[]
        {
            ("a", new DateTime(2019, 11, 18, 9, 0, 0)),
            ("b", new DateTime(2023, 11, 18, 20, 0, 0)),
            ("c", new DateTime(2026, 11, 18)),        // today itself: not a memory
            ("d", new DateTime(2024, 11, 17)),        // another day
            ("e", new DateTime(2022, 10, 18)),        // another month
        };
        Assert.Equal(["b", "a"], PhotoLibrary.ThisDay(photos, today));
        Assert.Empty(PhotoLibrary.ThisDay([], today));
    }

    [Theory]
    [InlineData(2025, "год назад")]
    [InlineData(2024, "2 года назад")]
    [InlineData(2021, "5 лет назад")]
    [InlineData(2005, "21 год назад")]
    [InlineData(2015, "11 лет назад")]
    [InlineData(2004, "22 года назад")]
    [InlineData(2014, "12 лет назад")]
    public void YearsAgoInRussian(int year, string text) =>
        Assert.Equal(text, PhotoLibrary.YearsAgo(new DateTime(year, 11, 18), new DateTime(2026, 11, 18)));

    [Fact]
    public void CropKeepsTheFocusInTheMiddleWhenItCan()
    {
        // a wide 4000×2000 picture in a square widget: half the width is shown
        var r = PhotoLibrary.Crop(4000, 2000, 160, 160, 0.5, 0.5);
        Assert.Equal(new Rect(0.25, 0, 0.5, 1), r);
        // focus on the right third: the window moves right
        Assert.Equal(0.45, PhotoLibrary.Crop(4000, 2000, 160, 160, 0.7, 0.5).X, 9);
        // focus at the very edge: as far as the picture goes, no further
        Assert.Equal(new Rect(0.5, 0, 0.5, 1), PhotoLibrary.Crop(4000, 2000, 160, 160, 1.0, 0.5));
        // a tall picture in a wide widget: cut top and bottom around the focus
        var t = PhotoLibrary.Crop(1000, 2000, 336, 160, 0.5, 0.2);
        Assert.Equal(0, t.X); Assert.Equal(1, t.Width);
        Assert.Equal(0.2380952, t.Height, 5);
        Assert.Equal(0.0809524, t.Y, 5);
        // same shape: the whole picture
        Assert.Equal(new Rect(0, 0, 1, 1), PhotoLibrary.Crop(1600, 1600, 160, 160, 0.9, 0.1));
        Assert.Equal(new Rect(0, 0, 1, 1), PhotoLibrary.Crop(0, 0, 160, 160, 0.5, 0.5));
    }

    [Fact]
    public void FacesCenterIsWeightedAndAimsBelowTheFaces()
    {
        Assert.Null(PhotoLibrary.FacesCenter([], 100, 100));
        var one = PhotoLibrary.FacesCenter([new Rect(60, 20, 20, 20)], 100, 100)!.Value;
        Assert.Equal(0.7, one.X, 6); Assert.Equal(0.36, one.Y, 6);
        // a big face near the left and a small one on the right: the big one wins
        var two = PhotoLibrary.FacesCenter([new Rect(0, 0, 40, 40), new Rect(80, 0, 10, 10)], 100, 100)!.Value;
        Assert.True(two.X < 0.35);
    }

    [Fact]
    public void ShuffleIsStableForTheSameSeed()
    {
        var files = Enumerable.Range(0, 30).Select(i => $"f{i}.jpg").ToList();
        Assert.Equal(files, PhotoLibrary.Order(files, false, 1));
        var a = PhotoLibrary.Order(files, true, 7); var b = PhotoLibrary.Order(files, true, 7);
        Assert.Equal(a, b);
        Assert.NotEqual(files, a);
        Assert.Equal(files.OrderBy(x => x), a.OrderBy(x => x));
    }

    [Fact]
    public void ScansPicturesAndReadsTheExifDate()
    {
        var dir = Directory.CreateTempSubdirectory("atoll-photos-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            var withExif = Path.Combine(dir, "sub", "shot.jpg");
            WriteJpeg(withExif, "2019:06:14 18:03:22");
            WriteJpeg(Path.Combine(dir, "plain.jpg"), null);
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "not a picture");
            Assert.Single(PhotoLibrary.Scan(dir, subfolders: false));
            Assert.Equal(2, PhotoLibrary.Scan(dir, subfolders: true).Count);
            Assert.Empty(PhotoLibrary.Scan(Path.Combine(dir, "missing"), true));
            var taken = PhotoLibrary.TakenOn(withExif, out bool exif);
            Assert.True(exif);
            Assert.Equal(new DateTime(2019, 6, 14, 18, 3, 22), taken);
            PhotoLibrary.TakenOn(Path.Combine(dir, "plain.jpg"), out bool none);
            Assert.False(none);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>A tiny JPEG, with an EXIF "taken" date when given.</summary>
    private static void WriteJpeg(string path, string? exifDate)
    {
        var bmp = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgr24, null, new byte[8 * 8 * 3], 8 * 3);
        BitmapMetadata? meta = null;
        if (exifDate != null)
        {
            meta = new BitmapMetadata("jpg");
            meta.SetQuery("/app1/ifd/exif/{ushort=36867}", exifDate);
        }
        var enc = new JpegBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp, null, meta, null));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
