namespace DynamicIsland.Views;

/// <summary>
/// How the light runs across a sung line. Not evenly by letters: singers pause at a dash between words
/// ("Я иду — и вдруг"), a comma or an ellipsis, so the light waits there instead of running on into words not yet
/// sung. A hyphen inside a word ("кто-то") is just a letter.
/// </summary>
public static class KaraokeTiming
{
    /// <summary>Seconds per unit of weight (one letter), and the extra a line takes to start.</summary>
    public const double PerUnit = 0.11, Lead = 1.0;

    /// <summary>The weight of each character: how long singing it takes, in letters.</summary>
    public static double[] Weights(string text)
    {
        var w = new double[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool spaceBefore = i > 0 && char.IsWhiteSpace(text[i - 1]), spaceAfter = i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]);
            w[i] = c switch
            {
                '—' or '–' => 6,                                       // a dash: a real pause
                '-' when spaceBefore || spaceAfter || i == 0 => 6,     // " - " typed as a hyphen
                '…' or '⋯' => 5,
                '.' when i + 1 < text.Length && text[i + 1] == '.' => 2.5,   // "..." → about 5 in all
                ',' or ';' or ':' or '，' or '、' or '。' or '：' or '；' => 3,   // full-width marks of Chinese and Japanese
                '!' or '?' or '！' or '？' => 2,
                _ when char.IsWhiteSpace(c) => 0.6,
                _ => 1,
            };
        }
        return w;
    }

    /// <summary>The natural time to sing the line, seconds (before it is fitted to the gap to the next line).</summary>
    public static double Natural(string text) => Lead + Weights(text).Sum() * PerUnit;

    /// <summary>The slowest a line is sung (long held notes): the light never takes longer than this.</summary>
    public static double Slowest(string text) => Lead + Weights(text).Sum() * 0.3;

    /// <summary>
    /// How long the light takes over a line, seconds. Most of the time to the next line is singing, so the light
    /// fills it (a little less: a breath before the next line). Only a long instrumental gap is cut to the slowest
    /// singing, so the light doesn't crawl for a minute. Without the next line's time: the natural speed.
    /// </summary>
    public static double Duration(string text, double gapToNext) =>
        Math.Max(0.8, gapToNext > 0.3 ? Math.Min(gapToNext * 0.9, Slowest(text)) : Natural(text));

    /// <summary>
    /// Where the light is, in characters (fractional), when a share <paramref name="progress"/> (0..1) of the line's
    /// time has passed. Pauses hold it right after their mark.
    /// </summary>
    public static double CharAt(double[] weights, double progress)
    {
        double total = weights.Sum();
        if (total <= 0) return 0;
        double target = Math.Clamp(progress, 0, 1) * total, sum = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            double w = weights[i];
            if (sum + w >= target)
            {
                // a pause mark lights up at once and then holds; a letter fills over its time
                bool pause = w > 1.5;
                return pause ? i + 1 : i + (w > 0 ? (target - sum) / w : 1);
            }
            sum += w;
        }
        return weights.Length;
    }
}
