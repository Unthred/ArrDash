using System.Text.RegularExpressions;

namespace ArrDash.Services;

public static partial class QualityDisplayHelper
{
    public static string Format(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
            return string.Empty;

        var trimmed = quality.Trim();
        var match = QualityPattern().Match(trimmed);
        if (!match.Success)
            return FormatToken(trimmed);

        var source = match.Groups["source"].Value;
        var detail = match.Groups["detail"].Value;
        if (IsResolution(detail))
            return $"{detail} · {FormatSource(source)}";

        return $"{FormatToken(detail)} · {FormatSource(source)}";
    }

    private static bool IsResolution(string value) =>
        ResolutionPattern().IsMatch(value);

    private static string FormatSource(string source) => source.ToUpperInvariant() switch
    {
        "WEBDL" => "Web-DL",
        "WEBRIP" => "Web-Rip",
        "BLURAY" => "Blu-ray",
        "HDTV" => "HDTV",
        "DVD" => "DVD",
        "CAM" => "Cam",
        "TELESYNC" => "Telesync",
        "WORKPRINT" => "Workprint",
        "REMUX" => "Remux",
        "SCREENER" => "Screener",
        "R5" => "R5",
        "TV" => "TV",
        _ => source
    };

    private static string FormatToken(string token) => token.ToUpperInvariant() switch
    {
        "MP3" => "MP3",
        "M4B" => "M4B",
        "FLAC" => "FLAC",
        "AAC" => "AAC",
        "ALAC" => "ALAC",
        "OPUS" => "Opus",
        "OGG" => "OGG",
        "WAV" => "WAV",
        _ => token
    };

    [GeneratedRegex(@"^(?<source>[A-Za-z0-9]+)-(?<detail>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex QualityPattern();

    [GeneratedRegex(@"^(?:\d+p|4K|SD)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResolutionPattern();
}
