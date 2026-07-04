using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArrDash.Services;

public static partial class HistoryTitleFormatter
{
    public static string Format(JsonElement rec)
    {
        if (TryBookAuthor(rec, out var bookTitle))
            return bookTitle;

        var sourceTitle = rec.TryGetProperty("sourceTitle", out var st) ? st.GetString() : null;
        return string.IsNullOrWhiteSpace(sourceTitle) ? "Unknown" : FormatSourceTitle(sourceTitle);
    }

    public static string FormatSourceTitle(string sourceTitle)
    {
        var text = sourceTitle.Trim();

        if (IsPathLike(text))
            return FormatPathTitle(text);

        if (LooksFriendlyTitle(text))
            return text;

        var tvMatch = TvBeforeEpisode().Match(text);
        if (tvMatch.Success)
        {
            var show = HumanizeDottedTitle(TrimTrailingYear(tvMatch.Groups[1].Value));
            return $"{show} - {tvMatch.Groups[2].Value.ToUpperInvariant()}";
        }

        var epMatch = EpisodeToken().Match(text);
        if (epMatch.Success)
        {
            var prefix = text[..epMatch.Index].TrimEnd('.');
            var show = HumanizeDottedTitle(TrimTrailingYear(prefix));
            return string.IsNullOrWhiteSpace(show)
                ? epMatch.Value.ToUpperInvariant()
                : $"{show} - {epMatch.Value.ToUpperInvariant()}";
        }

        var movieMatch = MovieYearQuality().Match(text);
        if (movieMatch.Success)
            return $"{HumanizeDottedTitle(movieMatch.Groups[1].Value)} ({movieMatch.Groups[2].Value})";

        var yearMatch = TitleYear().Match(text);
        if (yearMatch.Success)
            return $"{HumanizeDottedTitle(yearMatch.Groups[1].Value)} ({yearMatch.Groups[2].Value})";

        var stripped = QualityTail().Replace(text, "");
        if (!string.Equals(stripped, text, StringComparison.Ordinal))
            return FormatSourceTitle(stripped);

        return HumanizeDottedTitle(text);
    }

    private static bool TryBookAuthor(JsonElement rec, out string title)
    {
        title = string.Empty;
        if (!rec.TryGetProperty("book", out var book) || book.ValueKind != JsonValueKind.Object)
            return false;

        if (!book.TryGetProperty("title", out var bookTitleEl) ||
            string.IsNullOrWhiteSpace(bookTitleEl.GetString()))
            return false;

        title = bookTitleEl.GetString()!;
        if (rec.TryGetProperty("author", out var author) &&
            author.TryGetProperty("authorName", out var authorNameEl) &&
            !string.IsNullOrWhiteSpace(authorNameEl.GetString()))
            title = $"{title} — {authorNameEl.GetString()}";

        return true;
    }

    private static bool IsPathLike(string text) =>
        text.StartsWith('/') || text.StartsWith('\\') || (text.Length > 2 && text[1] == ':');

    private static string FormatPathTitle(string text)
    {
        var fileName = text.TrimEnd('/', '\\');
        var lastSlash = Math.Max(fileName.LastIndexOf('/'), fileName.LastIndexOf('\\'));
        fileName = lastSlash >= 0 ? fileName[(lastSlash + 1)..] : fileName;

        var pathMatch = PathEpisode().Match(fileName);
        if (pathMatch.Success)
            return $"{pathMatch.Groups[1].Value.Trim()} - {pathMatch.Groups[2].Value.ToUpperInvariant()}";

        var withoutExt = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(withoutExt) ? text : withoutExt.Trim();
    }

    private static bool LooksFriendlyTitle(string text) =>
        text.Contains(" - ", StringComparison.Ordinal) &&
        !EpisodeToken().IsMatch(text) &&
        !MovieYearQuality().IsMatch(text) &&
        text.Count(c => c == '.') <= 3;

    private static string TrimTrailingYear(string dotted)
    {
        var match = TrailingYear().Match(dotted);
        return match.Success ? dotted[..^5] : dotted;
    }

    private static string HumanizeDottedTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (value.Contains(' ') && !value.Contains('.'))
            return value.Trim();

        var spaced = value.Replace('.', ' ').Replace('_', ' ');
        return MultiSpace().Replace(spaced, " ").Trim();
    }

    [GeneratedRegex(@"\bS\d{1,2}E\d{1,2}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeToken();

    [GeneratedRegex(@"^(.+?)\.(S\d{1,2}E\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TvBeforeEpisode();

    [GeneratedRegex(@"^(.+?)\s-\s(S\d{1,2}E\d{1,2})\s-\s(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PathEpisode();

    [GeneratedRegex(@"^(.+?)\.(\d{4})\.(?:1080p|2160p|720p|480p|Hybrid|WEB|BluRay|WEBRip|WEB\.DL|WEB-DL|UHD)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MovieYearQuality();

    [GeneratedRegex(@"^(.+?)\.(\d{4})(?:\.|$)", RegexOptions.CultureInvariant)]
    private static partial Regex TitleYear();

    [GeneratedRegex(@"\.(\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingYear();

    [GeneratedRegex(@"\.(1080p|2160p|720p|480p|BluRay|WEBRip|WEB\.DL|WEB-DL|x265|x264|DDP|H\.265|HDR|SDR).*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QualityTail();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiSpace();
}
