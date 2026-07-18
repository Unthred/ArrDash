using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ArrDash.Services;

public static class CanonicalMediaKeyBuilder
{
    private static readonly Regex NonAlpha = new(@"[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Build(
        string mediaType,
        string? imdbId = null,
        int? tmdbId = null,
        int? tvdbId = null,
        int? traktId = null,
        string? title = null,
        int? year = null,
        int? season = null,
        int? episode = null)
    {
        var type = NormalizeType(mediaType);

        if (!string.IsNullOrWhiteSpace(imdbId))
            return AppendEpisode($"imdb:{imdbId.Trim().ToLowerInvariant()}", type, season, episode);
        if (traktId is > 0)
            return AppendEpisode($"trakt:{traktId}", type, season, episode);
        if (tmdbId is > 0)
            return AppendEpisode($"tmdb:{tmdbId}", type, season, episode);
        if (tvdbId is > 0)
            return AppendEpisode($"tvdb:{tvdbId}", type, season, episode);

        var slug = Slug(title);
        var yearPart = year is > 0 ? year.Value.ToString(CultureInfo.InvariantCulture) : "0";
        return AppendEpisode($"title:{slug}:{yearPart}", type, season, episode);
    }

    private static string AppendEpisode(string baseKey, string type, int? season, int? episode)
    {
        if (type != "episode")
            return $"{type}:{baseKey}";
        return $"{type}:{baseKey}:s{season ?? 0}e{episode ?? 0}";
    }

    private static string NormalizeType(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "movie" => "movie",
        "episode" => "episode",
        "show" => "episode",
        "music" or "track" => "music",
        _ => "other"
    };

    private static string Slug(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "unknown";
        var normalized = title.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return NonAlpha.Replace(sb.ToString().Normalize(NormalizationForm.FormC), "-").Trim('-');
    }
}
