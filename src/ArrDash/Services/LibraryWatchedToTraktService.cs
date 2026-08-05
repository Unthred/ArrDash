using System.Text.Json;
using ArrDash.Data;
using ArrDash.Data.Entities;
using ArrDash.Models;
using ArrDash.Services.Clients;
using Microsoft.EntityFrameworkCore;

namespace ArrDash.Services;

public sealed record LibraryWatchedSyncResult(
    int HistoryAdded,
    int CollectionAdded,
    int SkippedNoIds,
    int AlreadyLinked,
    IReadOnlyList<string> Samples);

/// <summary>
/// Pushes Emby/Plex library Played / viewCount items to Trakt history + collection (Squiggley only).
/// Caps per run; resumes via <see cref="TraktLibrarySyncLinkEntity"/>.
/// </summary>
public sealed class LibraryWatchedToTraktService(
    IDbContextFactory<ArrDashDbContext> dbFactory,
    TraktClient trakt,
    EmbyPlaybackReportingClient emby,
    PlexHistoryClient plex,
    LayoutPreferencesService prefs,
    ILogger<LibraryWatchedToTraktService> logger)
{
    public const int HistoryCapPerRun = 500;
    public const int CollectionCapPerRun = 500;

    public async Task<LibraryWatchedSyncResult> SyncAsync(
        TraktAccountEntity account,
        string accessToken,
        bool previewOnly,
        CancellationToken ct,
        Action<string>? onProgress = null)
    {
        if (!account.PushToTrakt)
            return new LibraryWatchedSyncResult(0, 0, 0, 0, []);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var excluded = prefs.Current.WatchStatsExcludedLibraries;

        onProgress?.Invoke("Loading Trakt collection…");
        var collectionKeys = await LoadCollectionKeysAsync(accessToken, ct);

        onProgress?.Invoke("Loading library watched sync links…");
        var existingLinks = await db.TraktLibrarySyncLinks.AsNoTracking()
            .Where(l => l.AccountId == account.Id)
            .Select(l => new { l.Server, l.ServerItemId, l.Direction })
            .ToListAsync(ct);
        var historyLinked = existingLinks
            .Where(l => l.Direction == "history")
            .Select(l => LinkKey(l.Server, l.ServerItemId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var collectionLinked = existingLinks
            .Where(l => l.Direction == "collection")
            .Select(l => LinkKey(l.Server, l.ServerItemId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        onProgress?.Invoke("Reading Emby/Plex library watched…");
        var watched = new List<LibraryWatchedItem>();
        watched.AddRange(await FetchEmbyWatchedAsync(account, excluded, ct));
        watched.AddRange(await plex.FetchWatchedLibraryItemsAsync(excluded, ct));

        // Prefer movies first, oldest watched first — same house rule as mark batching.
        watched = watched
            .OrderBy(w => w.MediaType == "movie" ? 0 : 1)
            .ThenBy(w => w.WatchedAt ?? DateTimeOffset.MaxValue)
            .ToList();

        var moviesHistory = new List<object>();
        var episodesHistory = new List<object>();
        var showsHistory = new Dictionary<string, ShowBucket>(StringComparer.OrdinalIgnoreCase);
        var moviesCollection = new List<object>();
        var episodesCollection = new List<object>();
        var showsCollection = new Dictionary<string, ShowCollectionBucket>(StringComparer.OrdinalIgnoreCase);

        var pendingHistoryLinks = new List<TraktLibrarySyncLinkEntity>();
        var pendingCollectionLinks = new List<TraktLibrarySyncLinkEntity>();
        var samples = new List<string>();
        var skippedNoIds = 0;
        var alreadyLinked = 0;

        foreach (var item in watched)
        {
            ct.ThrowIfCancellationRequested();
            if (item.MediaType == "movie" && !account.SyncMovies)
                continue;
            if (item.MediaType == "episode" && !account.SyncEpisodes)
                continue;

            var ids = BuildIds(item);
            if (ids is null)
            {
                skippedNoIds++;
                continue;
            }

            var key = CanonicalMediaKeyBuilder.Build(
                item.MediaType, item.ImdbId, item.TmdbId, item.TvdbId, item.TraktId,
                item.Title, item.Year, item.SeasonNumber, item.EpisodeNumber);
            var linkKey = LinkKey(item.Source, item.ServerItemId);
            var watchedAt = FormatWatchedAt(item.WatchedAt ?? DateTimeOffset.UtcNow);

            var needHistory = !historyLinked.Contains(linkKey)
                              && pendingHistoryLinks.Count < HistoryCapPerRun;
            var needCollection = !collectionLinked.Contains(linkKey)
                                 && !ProviderInCollection(collectionKeys, item)
                                 && pendingCollectionLinks.Count < CollectionCapPerRun;

            if (!needHistory && !needCollection)
            {
                if (historyLinked.Contains(linkKey) || collectionLinked.Contains(linkKey)
                    || ProviderInCollection(collectionKeys, item))
                    alreadyLinked++;
                continue;
            }

            if (needHistory)
            {
                var queued = false;
                if (item.MediaType == "movie")
                {
                    moviesHistory.Add(new { watched_at = watchedAt, ids });
                    queued = true;
                }
                else if (item.SeasonNumber is int sn && item.EpisodeNumber is int en)
                {
                    AddShowEpisode(showsHistory, ids, item, sn, en);
                    queued = true;
                }
                else if (item.TraktId is int epTrakt)
                {
                    episodesHistory.Add(new { watched_at = watchedAt, ids = new { trakt = epTrakt } });
                    queued = true;
                }

                if (queued)
                {
                    pendingHistoryLinks.Add(MakeLink(account.Id, item, "history", key));
                    historyLinked.Add(linkKey);
                }
                else
                {
                    needHistory = false;
                }
            }

            if (needCollection)
            {
                if (item.MediaType == "movie")
                {
                    moviesCollection.Add(new { ids });
                    pendingCollectionLinks.Add(MakeLink(account.Id, item, "collection", key));
                    collectionLinked.Add(linkKey);
                    AddProviderKeys(collectionKeys, item);
                }
                else if (item.SeasonNumber is int sn && item.EpisodeNumber is int en)
                {
                    AddShowEpisodeToCollection(showsCollection, ids, item, sn, en);
                    pendingCollectionLinks.Add(MakeLink(account.Id, item, "collection", key));
                    collectionLinked.Add(linkKey);
                    AddProviderKeys(collectionKeys, item);
                }
                else
                {
                    needCollection = false;
                }
            }

            if ((needHistory || needCollection) && samples.Count < 8)
                samples.Add($"{item.Source}: {item.Title}");
        }

        var historyCount = pendingHistoryLinks.Count;
        var collectionCount = pendingCollectionLinks.Count;

        if (previewOnly || (historyCount == 0 && collectionCount == 0))
            return new LibraryWatchedSyncResult(historyCount, collectionCount, skippedNoIds, alreadyLinked, samples);

        if (historyCount > 0)
        {
            onProgress?.Invoke($"Pushing {historyCount:N0} library watches to Trakt history…");
            var showPayload = showsHistory.Values
                .Select(s => new
                {
                    ids = s.Ids,
                    seasons = s.Seasons.Select(season => new
                    {
                        number = season.Key,
                        episodes = season.Value.Select(ep => new
                        {
                            number = ep.Episode,
                            watched_at = FormatWatchedAt(ep.WatchedAt)
                        }).ToList()
                    }).ToList()
                })
                .ToList();

            var result = await trakt.AddToHistoryAsync(
                accessToken,
                new { movies = moviesHistory, episodes = episodesHistory, shows = showPayload },
                ct);
            if (result is not null)
            {
                db.TraktLibrarySyncLinks.AddRange(pendingHistoryLinks);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                logger.LogWarning("Library→Trakt history push failed; links not saved");
                historyCount = 0;
            }
        }

        if (collectionCount > 0)
        {
            onProgress?.Invoke($"Adding {collectionCount:N0} items to Trakt collection…");
            var result = await trakt.AddToCollectionAsync(
                accessToken,
                new
                {
                    movies = moviesCollection,
                    episodes = episodesCollection,
                    shows = showsCollection.Values.Select(s => new
                    {
                        ids = s.Ids,
                        seasons = s.Seasons.Select(season => new
                        {
                            number = season.Key,
                            episodes = season.Value.Select(ep => new { number = ep }).ToList()
                        }).ToList()
                    }).ToList()
                },
                ct);
            if (result is not null)
            {
                db.TraktLibrarySyncLinks.AddRange(pendingCollectionLinks);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                logger.LogWarning("Library→Trakt collection push failed; links not saved");
                collectionCount = 0;
            }
        }

        return new LibraryWatchedSyncResult(historyCount, collectionCount, skippedNoIds, alreadyLinked, samples);
    }

    private async Task<IReadOnlyList<LibraryWatchedItem>> FetchEmbyWatchedAsync(
        TraktAccountEntity account,
        IReadOnlyList<string>? excluded,
        CancellationToken ct)
    {
        if (!emby.IsConfigured)
            return [];

        var users = await emby.ListUsersAsync(ct);
        var names = LocalUserNames(account);
        var matched = users.Where(u => names.Contains(u.Name)).ToList();
        if (matched.Count == 0)
            return [];

        var libs = await emby.FetchLibrariesAsync(ct);
        var results = new List<LibraryWatchedItem>();
        foreach (var user in matched)
        {
            var includedLibs = libs
                .Where(l => !WatchStatsLibraryFilter.IsExcluded(excluded, WatchStatsSources.Emby, l.ExternalId))
                .ToList();

            if (includedLibs.Count == 0)
            {
                // No library catalog / nothing excluded — scan entire user library.
                results.AddRange(await emby.FetchPlayedItemsAsync(user.Id, libraryParentId: null, ct));
                continue;
            }

            foreach (var lib in includedLibs)
            {
                ct.ThrowIfCancellationRequested();
                results.AddRange(await emby.FetchPlayedItemsAsync(user.Id, lib.ExternalId, ct));
            }
        }

        return results;
    }

    private async Task<HashSet<string>> LoadCollectionKeysAsync(string accessToken, CancellationToken ct)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var movies = await trakt.GetCollectionAsync(accessToken, "movies", ct);
            foreach (var m in movies)
                AddIds(keys, m.Movie?.Ids, "movie");

            var shows = await trakt.GetCollectionAsync(accessToken, "shows", ct);
            foreach (var s in shows)
                AddIds(keys, s.Show?.Ids, "show");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load Trakt collection; will push without existing-collection skip");
        }

        return keys;
    }

    private static void AddIds(HashSet<string> keys, TraktIds? ids, string kind)
    {
        if (ids is null)
            return;
        if (!string.IsNullOrWhiteSpace(ids.Imdb))
            keys.Add($"{kind}:imdb:{ids.Imdb}");
        if (ids.Tmdb is int tmdb)
            keys.Add($"{kind}:tmdb:{tmdb}");
        if (ids.Tvdb is int tvdb)
            keys.Add($"{kind}:tvdb:{tvdb}");
        if (ids.Trakt is int trakt)
            keys.Add($"{kind}:trakt:{trakt}");
    }

    private static void AddProviderKeys(HashSet<string> keys, LibraryWatchedItem item)
    {
        var kind = item.MediaType == "movie" ? "movie" : "show";
        if (!string.IsNullOrWhiteSpace(item.ImdbId))
            keys.Add($"{kind}:imdb:{item.ImdbId}");
        if (item.TmdbId is int tmdb)
            keys.Add($"{kind}:tmdb:{tmdb}");
        if (item.TvdbId is int tvdb)
            keys.Add($"{kind}:tvdb:{tvdb}");
        if (item.TraktId is int trakt)
            keys.Add($"{kind}:trakt:{trakt}");
    }

    private static bool ProviderInCollection(HashSet<string> keys, LibraryWatchedItem item)
    {
        var kind = item.MediaType == "movie" ? "movie" : "show";
        if (!string.IsNullOrWhiteSpace(item.ImdbId) && keys.Contains($"{kind}:imdb:{item.ImdbId}"))
            return true;
        if (item.TmdbId is int tmdb && keys.Contains($"{kind}:tmdb:{tmdb}"))
            return true;
        if (item.TvdbId is int tvdb && keys.Contains($"{kind}:tvdb:{tvdb}"))
            return true;
        if (item.TraktId is int trakt && keys.Contains($"{kind}:trakt:{trakt}"))
            return true;
        return false;
    }

    private static string? ProviderKey(LibraryWatchedItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ImdbId))
            return $"imdb:{item.ImdbId}";
        if (item.TmdbId is int tmdb)
            return $"tmdb:{tmdb}";
        if (item.TvdbId is int tvdb)
            return $"tvdb:{tvdb}";
        if (item.TraktId is int trakt)
            return $"trakt:{trakt}";
        return null;
    }

    internal static object? BuildIds(LibraryWatchedItem item)
    {
        if (item.TraktId is int trakt)
            return new { trakt };
        if (!string.IsNullOrWhiteSpace(item.ImdbId))
            return new { imdb = item.ImdbId };
        if (item.TmdbId is int tmdb)
            return new { tmdb };
        if (item.TvdbId is int tvdb)
            return new { tvdb };
        return null;
    }

    private static void AddShowEpisode(
        Dictionary<string, ShowBucket> shows,
        object ids,
        LibraryWatchedItem item,
        int season,
        int episode)
    {
        var key = ProviderKey(item) ?? $"{item.Title}|{item.Year}";
        if (!shows.TryGetValue(key, out var bucket))
        {
            bucket = new ShowBucket(ids);
            shows[key] = bucket;
        }

        if (!bucket.Seasons.TryGetValue(season, out var eps))
        {
            eps = [];
            bucket.Seasons[season] = eps;
        }

        eps.Add((episode, item.WatchedAt ?? DateTimeOffset.UtcNow));
    }

    private static void AddShowEpisodeToCollection(
        Dictionary<string, ShowCollectionBucket> shows,
        object ids,
        LibraryWatchedItem item,
        int season,
        int episode)
    {
        var key = ProviderKey(item) ?? $"{item.Title}|{item.Year}";
        if (!shows.TryGetValue(key, out var bucket))
        {
            bucket = new ShowCollectionBucket(ids);
            shows[key] = bucket;
        }

        if (!bucket.Seasons.TryGetValue(season, out var eps))
        {
            eps = [];
            bucket.Seasons[season] = eps;
        }

        if (!eps.Contains(episode))
            eps.Add(episode);
    }

    private static TraktLibrarySyncLinkEntity MakeLink(
        string accountId,
        LibraryWatchedItem item,
        string direction,
        string canonicalKey) =>
        new()
        {
            AccountId = accountId,
            Server = item.Source,
            ServerItemId = item.ServerItemId,
            Direction = direction,
            CanonicalMediaKey = canonicalKey,
            MediaType = item.MediaType,
            LinkedAtUtc = DateTimeOffset.UtcNow
        };

    private static string LinkKey(string server, string itemId) => $"{server}:{itemId}";

    private static string FormatWatchedAt(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static HashSet<string> LocalUserNames(TraktAccountEntity account)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { account.CanonicalUserName };
        try
        {
            var users = JsonSerializer.Deserialize<List<TraktMappedUser>>(account.MappedUsersJson) ?? [];
            foreach (var u in users)
            {
                if (!string.IsNullOrWhiteSpace(u.UserName)
                    && (string.IsNullOrWhiteSpace(u.Source)
                        || u.Source.Equals(WatchStatsSources.Emby, StringComparison.OrdinalIgnoreCase)
                        || u.Source.Equals("emby", StringComparison.OrdinalIgnoreCase)))
                    set.Add(u.UserName);
            }
        }
        catch
        {
            // ignore malformed map
        }

        return set;
    }

    private sealed class ShowBucket(object ids)
    {
        public object Ids { get; } = ids;
        public Dictionary<int, List<(int Episode, DateTimeOffset WatchedAt)>> Seasons { get; } = new();
        public int EpisodeCount => Seasons.Values.Sum(v => v.Count);
    }

    private sealed class ShowCollectionBucket(object ids)
    {
        public object Ids { get; } = ids;
        public Dictionary<int, List<int>> Seasons { get; } = new();
    }
}
