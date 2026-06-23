using System.Text.RegularExpressions;
using ArrDash.Models;

namespace ArrDash.Services;

public static class AudiobookMergeService
{
    public const string ChaptarrNotInAbsNote = "Chaptarr import — not in AudioBookShelf";
    public const string AbsNoRecentChaptarrNote = "AudioBookShelf — no recent Chaptarr import";

    private static readonly Regex ChapterPrefix = new(@"^\d+\s*[-._]?\s*", RegexOptions.Compiled);
    private static readonly Regex ParenContent = new(@"\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex BracketContent = new(@"\[[^\]]*\]", RegexOptions.Compiled);
    private static readonly Regex SeriesPrefix = new(
        @"^(?:(?:book|volume|vol\.?|part|pt\.?)\s*\d+|#\d+)\s*[-:._]?\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AudiobookSuffix = new(
        @"\s*[-–—:]\s*(?:audiobook|unabridged|abridged)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<DownloadItem> Merge(
        IReadOnlyList<DownloadItem> chaptarr,
        IReadOnlyList<DownloadItem> library,
        int limit)
    {
        var libraryByAsin = BuildAsinIndex(library);
        var libraryByKey = library
            .GroupBy(x => MatchKey(x.Title, x.Subtitle))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Timestamp).First(), StringComparer.OrdinalIgnoreCase);

        var libraryByTitle = library
            .GroupBy(x => TitleKey(x.Title))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Timestamp).First(), StringComparer.OrdinalIgnoreCase);

        var matchedLibraryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<DownloadItem>();
        var seenChaptarrBooks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var download in chaptarr.OrderByDescending(x => x.Timestamp))
        {
            var dedupeKey = !string.IsNullOrWhiteSpace(download.Asin)
                ? AsinKey(download.Asin)
                : TitleKey(download.Title);
            if (!seenChaptarrBooks.Add(dedupeKey))
                continue;

            if (TryFindLibraryMatch(download, library, libraryByAsin, libraryByKey, libraryByTitle, out var libraryItem))
            {
                matchedLibraryIds.Add(libraryItem.Id);
                merged.Add(download with
                {
                    PosterUrl = libraryItem.PosterUrl ?? download.PosterUrl,
                    ExternalUrl = libraryItem.AudiobookShelfUrl ?? libraryItem.ExternalUrl ?? download.ExternalUrl,
                    AudiobookShelfUrl = libraryItem.AudiobookShelfUrl ?? libraryItem.ExternalUrl,
                    ChaptarrUrl = download.ChaptarrUrl ?? download.ExternalUrl,
                    GoodreadsUrl = download.GoodreadsUrl,
                    HardcoverUrl = download.HardcoverUrl,
                    StatusNote = null
                });
            }
            else
            {
                merged.Add(download with { StatusNote = ChaptarrNotInAbsNote });
            }
        }

        foreach (var libraryItem in library.OrderByDescending(x => x.Timestamp))
        {
            if (matchedLibraryIds.Contains(libraryItem.Id))
                continue;

            if (merged.Any(x => x.Source == MediaSource.Chaptarr && ItemsMatch(x, libraryItem)))
                continue;

            merged.Add(libraryItem with { StatusNote = AbsNoRecentChaptarrNote });
        }

        return merged
            .OrderByDescending(x => x.Timestamp)
            .Take(limit)
            .ToList();
    }

    private static Dictionary<string, DownloadItem> BuildAsinIndex(IReadOnlyList<DownloadItem> library)
    {
        var index = new Dictionary<string, DownloadItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in library)
        {
            if (string.IsNullOrWhiteSpace(item.Asin))
                continue;

            var key = AsinKey(item.Asin);
            if (!index.ContainsKey(key))
                index[key] = item;
        }

        return index;
    }

    private static bool TryFindLibraryMatch(
        DownloadItem download,
        IReadOnlyList<DownloadItem> library,
        IReadOnlyDictionary<string, DownloadItem> libraryByAsin,
        IReadOnlyDictionary<string, DownloadItem> libraryByKey,
        IReadOnlyDictionary<string, DownloadItem> libraryByTitle,
        out DownloadItem libraryItem)
    {
        if (!string.IsNullOrWhiteSpace(download.Asin) &&
            libraryByAsin.TryGetValue(AsinKey(download.Asin), out libraryItem!))
            return true;

        var key = MatchKey(download.Title, download.Subtitle);
        if (libraryByKey.TryGetValue(key, out libraryItem!))
            return true;

        var titleKey = TitleKey(download.Title);
        if (libraryByTitle.TryGetValue(titleKey, out libraryItem!))
            return AuthorsCompatible(download.Subtitle, libraryItem.Subtitle);

        libraryItem = library.FirstOrDefault(item => ItemsMatch(download, item))!;
        return libraryItem is not null;
    }

    private static bool ItemsMatch(DownloadItem left, DownloadItem right)
    {
        if (!string.IsNullOrWhiteSpace(left.Asin) &&
            !string.IsNullOrWhiteSpace(right.Asin) &&
            string.Equals(AsinKey(left.Asin), AsinKey(right.Asin), StringComparison.OrdinalIgnoreCase))
            return true;

        return TitlesMatch(left.Title, right.Title) && AuthorsCompatible(left.Subtitle, right.Subtitle);
    }

    private static bool TitlesMatch(string left, string right)
    {
        var a = TitleKey(left);
        var b = TitleKey(right);
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        if (a.Length >= 10 && b.Length >= 10 &&
            (a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
             b.Contains(a, StringComparison.OrdinalIgnoreCase)))
            return true;

        return WordOverlapScore(StripTitleNoise(left), StripTitleNoise(right)) >= 0.82;
    }

    private static bool AuthorsCompatible(string? left, string? right)
    {
        var a = AuthorKey(left);
        var b = AuthorKey(right);
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return true;

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        if (a.Length >= 5 && b.Length >= 5 &&
            (a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
             b.Contains(a, StringComparison.OrdinalIgnoreCase)))
            return true;

        var leftAuthor = left?.Contains(',') == true
            ? $"{left.Split(',', 2)[1].Trim()} {left.Split(',', 2)[0].Trim()}"
            : left ?? string.Empty;
        var rightAuthor = right?.Contains(',') == true
            ? $"{right.Split(',', 2)[1].Trim()} {right.Split(',', 2)[0].Trim()}"
            : right ?? string.Empty;

        return WordOverlapScore(leftAuthor, rightAuthor) >= 0.66;
    }

    private static double WordOverlapScore(string left, string right)
    {
        var leftTokens = WordTokens(left);
        var rightTokens = WordTokens(right);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
            return 0;

        var overlap = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        return (2.0 * overlap) / (leftTokens.Count + rightTokens.Count);
    }

    private static HashSet<string> WordTokens(string value) =>
        value
            .ToLowerInvariant()
            .Split([' ', '-', ':', '.', ',', '–', '—', '(', ')', '[', ']'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(token => token.Length >= 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string MatchKey(string title, string? author) =>
        $"{TitleKey(title)}|{AuthorKey(author)}";

    private static string AsinKey(string asin) =>
        Normalize(asin);

    private static string TitleKey(string title) => Normalize(StripTitleNoise(title));

    private static string AuthorKey(string? author)
    {
        if (string.IsNullOrWhiteSpace(author))
            return string.Empty;

        var trimmed = author.Trim();
        if (trimmed.Contains(','))
        {
            var parts = trimmed.Split(',', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
                trimmed = $"{parts[1]} {parts[0]}";
        }

        return Normalize(trimmed);
    }

    private static string StripTitleNoise(string title)
    {
        var value = ChapterPrefix.Replace(title.Trim(), string.Empty);
        value = ParenContent.Replace(value, string.Empty);
        value = BracketContent.Replace(value, string.Empty);
        value = AudiobookSuffix.Replace(value, string.Empty);
        value = SeriesPrefix.Replace(value, string.Empty);

        var colonIndex = value.IndexOf(':');
        if (colonIndex > 0 && colonIndex < value.Length - 1)
            value = value[..colonIndex];

        value = value.Trim(' ', '-', ':', '.', '–', '—');
        return MoveLeadingArticle(value);
    }

    private static string MoveLeadingArticle(string title)
    {
        if (title.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
            return $"{title[4..].Trim()} The";

        if (title.StartsWith("A ", StringComparison.OrdinalIgnoreCase))
            return $"{title[2..].Trim()} A";

        return title;
    }

    private static string Normalize(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }
}
