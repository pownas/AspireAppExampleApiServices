using System.Globalization;

namespace AspireApp1.Web.Insights;

/// <summary>
/// Formatting helpers shared by the insight chart components.
/// </summary>
public static class InsightFormat
{
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>Formats a number for SVG attributes (invariant culture, two decimals).</summary>
    /// <param name="value">Coordinate or length.</param>
    /// <returns>E.g. "12.50".</returns>
    public static string Svg(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Formats a duration in milliseconds for display.</summary>
    /// <param name="ms">Milliseconds.</param>
    /// <returns>"850 ms" or "1,25 s".</returns>
    public static string Duration(double ms) =>
        ms < 1000 ? $"{ms.ToString("0", Swedish)} ms" : $"{(ms / 1000).ToString("0.##", Swedish)} s";

    /// <summary>Formats a 0-1 ratio as a percentage.</summary>
    /// <param name="ratio">Ratio between 0 and 1.</param>
    /// <returns>E.g. "12,5 %".</returns>
    public static string Percent(double ratio) => $"{(ratio * 100).ToString("0.#", Swedish)} %";

    /// <summary>Formats a timestamp as local time for axis labels.</summary>
    /// <param name="value">Timestamp.</param>
    /// <param name="includeDate">Whether to include the date (for long windows).</param>
    /// <returns>E.g. "14:05" or "30/9 14:05".</returns>
    public static string Time(DateTimeOffset value, bool includeDate = false) =>
        value.ToLocalTime().ToString(includeDate ? "d/M HH:mm" : "HH:mm", Swedish);

    /// <summary>CSS class for a status (fill colour from the fixed status palette).</summary>
    /// <param name="status">Status.</param>
    /// <returns>A class name defined in app.css.</returns>
    public static string StatusClass(InsightStatus status) => status switch
    {
        InsightStatus.Ok => "insight-ok",
        InsightStatus.Warning => "insight-warning",
        InsightStatus.Error => "insight-error",
        _ => "insight-nodata"
    };

    /// <summary>Icon + label for a status so colour is never the only cue.</summary>
    /// <param name="status">Status.</param>
    /// <returns>E.g. "✅ OK".</returns>
    public static string StatusLabel(InsightStatus status) => status switch
    {
        InsightStatus.Ok => "✅ OK",
        InsightStatus.Warning => "⚠️ Varning",
        InsightStatus.Error => "❌ Fel",
        _ => "– Ingen data"
    };
}
