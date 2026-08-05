using System.Text.Json;
using ArrDash.Data;
using ArrDash.Data.Entities;
using ArrDash.Models;
using ArrDash.Services.Clients;
using Microsoft.EntityFrameworkCore;

namespace ArrDash.Services;

public sealed record ServerWatchedMarkResult(int WouldMark, int Marked, int AlreadyLinked, int Unmatched, IReadOnlyList<string> Samples);

/// <summary>
/// Additive Trakt→Emby/Plex mark-watched for a Trakt account's user graph
/// (CanonicalUserName + MappedUsersJson). Never unmarks.
/// </summary>
public sealed class ServerWatchedMarkService(
    IDbContextFactory<ArrDashDbContext> dbFactory,
    EmbyPlaybackReportingClient emby,
    JellyfinPlaybackReportingClient jellyfin,
    PlexHistoryClient plex,
    ILogger<ServerWatchedMarkService> logger)
{
    /// <summary>
    /// Max items newly marked (sync) or would-mark candidates (preview) per run.
    /// Already-watched linking does not consume this budget so catch-up can proceed.
    /// </summary>
    private const int ItemsPerRun = 1000;

    public async Task<(ServerWatchedMarkResult Emby, ServerWatchedMarkResult Plex)> MarkForAccountAsync(
        TraktAccountEntity account,
        bool previewOnly,
        CancellationToken ct,
        Action<string>? reportProgress = null)
    {
        var embyResult = account.MarkEmbyWatched
            ? await MarkMediaServerAsync(account, WatchStatsSources.Emby, emby, previewOnly, ct, reportProgress)
            : new ServerWatchedMarkResult(0, 0, 0, 0, []);

        var plexResult = account.MarkPlexWatched
            ? await MarkPlexAsync(account, previewOnly, ct, reportProgress)
            : new ServerWatchedMarkResult(0, 0, 0, 0, []);

        // Jellyfin uses the same Emby-compatible API when configured and flagged.
        if (account.MarkJellyfinWatched && jellyfin.IsConfigured)
        {
            var jf = await MarkMediaServerAsync(account, WatchStatsSources.Jellyfin, jellyfin, previewOnly, ct, reportProgress);
            embyResult = new ServerWatchedMarkResult(
                embyResult.WouldMark + jf.WouldMark,
                embyResult.Marked + jf.Marked,
                embyResult.AlreadyLinked + jf.AlreadyLinked,
                embyResult.Unmatched + jf.Unmatched,
                embyResult.Samples.Concat(jf.Samples).Take(8).ToList());
        }

        return (embyResult, plexResult);
    }

    private async Task<ServerWatchedMarkResult> MarkMediaServerAsync(
        TraktAccountEntity account,
        string server,
        PlaybackReportingClient client,
        bool previewOnly,
        CancellationToken ct,
        Action<string>? reportProgress)
    {
        if (!client.IsConfigured)
            return new ServerWatchedMarkResult(0, 0, 0, 0, ["Media server not configured"]);

        var targetUsers = await ResolveMediaServerUsersAsync(account, server, client, ct);
        if (targetUsers.Count == 0)
            return new ServerWatchedMarkResult(0, 0, 0, 0, [$"No {server} users matched account maps"]);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var history = await LoadTraktHistoryAsync(db, account, ct);
        var existingLinks = await db.ServerWatchedLinks.AsNoTracking()
            .Where(l => l.AccountId == account.Id && l.Server == server)
            .Select(l => new { l.CanonicalMediaKey, l.ServerUserId, l.ServerItemId })
            .ToListAsync(ct);
        var linkedKeys = existingLinks
            .Select(l => l.CanonicalMediaKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // UNIQUE(AccountId, Server, ServerUserId, ServerItemId) — track item ids so rematches
        // with a different canonical key do not blow up SaveChanges mid-run.
        var linkedItemIds = existingLinks
            .Select(l => $"{l.ServerUserId}:{l.ServerItemId}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linkedKeyToItemId = existingLinks
            .GroupBy(l => l.CanonicalMediaKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().ServerItemId, StringComparer.OrdinalIgnoreCase);

        var wouldMark = 0;
        var marked = 0;
        var already = 0;
        var unmatched = 0;
        var samples = new List<string>();
        var scanned = 0;
        var pendingSinceSave = 0;

        foreach (var user in targetUsers)
        {
            foreach (var play in history)
            {
                ct.ThrowIfCancellationRequested();
                scanned++;
                if (scanned == 1 || scanned % 25 == 0)
                    reportProgress?.Invoke($"{(previewOnly ? "Previewing" : "Marking")} {server}… {scanned:N0}/{history.Count:N0} (marked {marked:N0})");

                var key = play.CanonicalMediaKey ?? "";
                if (string.IsNullOrWhiteSpace(key))
                    key = CanonicalMediaKeyBuilder.Build(
                        play.MediaType, play.ImdbId, play.TmdbId, play.TvdbId, play.TraktId,
                        play.MediaType == "episode" ? play.SeriesTitle ?? play.Title : play.Title,
                        play.Year, play.SeasonNumber, play.EpisodeNumber);

                var linkKey = $"{user.Id}:{key}";
                if (linkedKeys.Contains(key) || linkedKeys.Contains(linkKey))
                {
                    // Repair stale links: ArrDash row exists but Emby lost Played.
                    if (!previewOnly
                        && linkedKeyToItemId.TryGetValue(key, out var linkedItemId)
                        && !string.IsNullOrWhiteSpace(linkedItemId)
                        && marked < ItemsPerRun
                        && !await client.IsPlayedAsync(user.Id, linkedItemId, ct))
                    {
                        var repaired = await client.MarkPlayedAsync(
                            user.Id,
                            linkedItemId,
                            new DateTimeOffset(play.PlayedAtUtc, TimeSpan.Zero),
                            ct);
                        if (repaired)
                        {
                            wouldMark++;
                            marked++;
                            if (samples.Count < 8)
                                samples.Add($"{play.Title} → {server}/{user.Name} (repair)");
                        }
                        else
                        {
                            unmatched++;
                        }
                    }
                    else
                    {
                        already++;
                    }

                    continue;
                }

                // Cap only new marks / preview would-marks — not already-watched links.
                if ((previewOnly ? wouldMark : marked) >= ItemsPerRun)
                    break;

                var itemId = await client.FindItemIdByProviderAsync(
                    play.MediaType,
                    play.ImdbId,
                    play.TmdbId,
                    play.TvdbId,
                    play.TraktId,
                    play.SeasonNumber,
                    play.EpisodeNumber,
                    ct);

                if (string.IsNullOrWhiteSpace(itemId))
                {
                    unmatched++;
                    continue;
                }

                var itemLinkId = $"{user.Id}:{itemId}";
                if (linkedItemIds.Contains(itemLinkId))
                {
                    // Same repair path when keyed by item id under a different canonical key.
                    if (!previewOnly
                        && marked < ItemsPerRun
                        && !await client.IsPlayedAsync(user.Id, itemId, ct))
                    {
                        var repaired = await client.MarkPlayedAsync(
                            user.Id,
                            itemId,
                            new DateTimeOffset(play.PlayedAtUtc, TimeSpan.Zero),
                            ct);
                        if (repaired)
                        {
                            wouldMark++;
                            marked++;
                            linkedKeys.Add(key);
                            if (samples.Count < 8)
                                samples.Add($"{play.Title} → {server}/{user.Name} (repair)");
                        }
                        else
                        {
                            unmatched++;
                        }
                    }
                    else
                    {
                        linkedKeys.Add(key);
                        already++;
                    }

                    continue;
                }

                if (await client.IsPlayedAsync(user.Id, itemId, ct))
                {
                    if (!previewOnly && TryQueueLink(db, account.Id, server, user.Id, itemId, key, play.MediaType, linkedKeys, linkedItemIds))
                        pendingSinceSave++;

                    already++;
                    if (!previewOnly && pendingSinceSave >= 50)
                    {
                        await db.SaveChangesAsync(ct);
                        pendingSinceSave = 0;
                    }

                    continue;
                }

                wouldMark++;
                if (samples.Count < 8)
                    samples.Add($"{play.Title} → {server}/{user.Name}");

                if (previewOnly)
                    continue;

                var ok = await client.MarkPlayedAsync(
                    user.Id,
                    itemId,
                    new DateTimeOffset(play.PlayedAtUtc, TimeSpan.Zero),
                    ct);
                if (!ok)
                {
                    unmatched++;
                    wouldMark--;
                    continue;
                }

                if (TryQueueLink(db, account.Id, server, user.Id, itemId, key, play.MediaType, linkedKeys, linkedItemIds))
                    pendingSinceSave++;
                marked++;

                if (pendingSinceSave >= 50)
                {
                    await db.SaveChangesAsync(ct);
                    pendingSinceSave = 0;
                }
            }
        }

        if (!previewOnly && pendingSinceSave > 0)
            await db.SaveChangesAsync(ct);

        return new ServerWatchedMarkResult(wouldMark, marked, already, unmatched, samples);
    }

    private static bool TryQueueLink(
        ArrDashDbContext db,
        string accountId,
        string server,
        string userId,
        string itemId,
        string canonicalKey,
        string mediaType,
        HashSet<string> linkedKeys,
        HashSet<string> linkedItemIds)
    {
        var itemLinkId = $"{userId}:{itemId}";
        if (!linkedItemIds.Add(itemLinkId))
            return false;

        linkedKeys.Add(canonicalKey);
        db.ServerWatchedLinks.Add(new ServerWatchedLinkEntity
        {
            AccountId = accountId,
            Server = server,
            ServerUserId = userId,
            ServerItemId = itemId,
            CanonicalMediaKey = canonicalKey,
            MediaType = mediaType
        });
        return true;
    }

    private async Task<ServerWatchedMarkResult> MarkPlexAsync(
        TraktAccountEntity account,
        bool previewOnly,
        CancellationToken ct,
        Action<string>? reportProgress)
    {
        if (!plex.IsConfigured)
            return new ServerWatchedMarkResult(0, 0, 0, 0, ["Plex not configured"]);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var history = await LoadTraktHistoryAsync(db, account, ct);
        var existingLinks = await db.ServerWatchedLinks.AsNoTracking()
            .Where(l => l.AccountId == account.Id && l.Server == WatchStatsSources.Plex)
            .Select(l => new { l.CanonicalMediaKey, l.ServerItemId })
            .ToListAsync(ct);
        var linkedKeys = existingLinks
            .Select(l => l.CanonicalMediaKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linkedItemIds = existingLinks
            .Select(l => $"token-owner:{l.ServerItemId}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Plex server token marks as the token owner — use a stable synthetic user id.
        const string plexUserId = "token-owner";

        var wouldMark = 0;
        var marked = 0;
        var already = 0;
        var unmatched = 0;
        var samples = new List<string>();
        var scanned = 0;
        var pendingSinceSave = 0;

        foreach (var play in history)
        {
            ct.ThrowIfCancellationRequested();
            scanned++;
            if (scanned == 1 || scanned % 25 == 0)
                reportProgress?.Invoke($"{(previewOnly ? "Previewing" : "Marking")} Plex… {scanned:N0}/{history.Count:N0} (marked {marked:N0})");

            if ((previewOnly ? wouldMark : marked) >= ItemsPerRun)
                break;

            var key = play.CanonicalMediaKey ?? "";
            if (string.IsNullOrWhiteSpace(key))
                key = CanonicalMediaKeyBuilder.Build(
                    play.MediaType, play.ImdbId, play.TmdbId, play.TvdbId, play.TraktId,
                    play.MediaType == "episode" ? play.SeriesTitle ?? play.Title : play.Title,
                    play.Year, play.SeasonNumber, play.EpisodeNumber);

            if (linkedKeys.Contains(key))
            {
                already++;
                continue;
            }

            var ratingKey = await plex.FindRatingKeyByProviderAsync(play.ImdbId, play.TmdbId, play.TvdbId, ct);
            if (string.IsNullOrWhiteSpace(ratingKey))
            {
                unmatched++;
                continue;
            }

            var itemLinkId = $"{plexUserId}:{ratingKey}";
            if (linkedItemIds.Contains(itemLinkId))
            {
                linkedKeys.Add(key);
                already++;
                continue;
            }

            wouldMark++;
            if (samples.Count < 8)
                samples.Add($"{play.Title} → plex");

            if (previewOnly)
                continue;

            var ok = await plex.MarkWatchedAsync(ratingKey, ct);
            if (!ok)
            {
                unmatched++;
                wouldMark--;
                continue;
            }

            if (TryQueueLink(db, account.Id, WatchStatsSources.Plex, plexUserId, ratingKey, key, play.MediaType, linkedKeys, linkedItemIds))
                pendingSinceSave++;
            marked++;

            if (pendingSinceSave >= 50)
            {
                await db.SaveChangesAsync(ct);
                pendingSinceSave = 0;
            }
        }

        if (!previewOnly && pendingSinceSave > 0)
            await db.SaveChangesAsync(ct);

        return new ServerWatchedMarkResult(wouldMark, marked, already, unmatched, samples);
    }

    private static async Task<List<PlayEventEntity>> LoadTraktHistoryAsync(
        ArrDashDbContext db,
        TraktAccountEntity account,
        CancellationToken ct)
    {
        var names = LocalUserNames(account).ToList();
        // Prefer warehouse Trakt rows for this account's canonical name (imports use CanonicalUserName).
        var query = db.PlayEvents.AsNoTracking()
            .Where(e => e.Source == WatchStatsSources.Trakt
                        && e.WasCompleted
                        && (e.MediaType == "movie" || e.MediaType == "episode")
                        && (e.UserDisplayName == account.CanonicalUserName || names.Contains(e.UserDisplayName))
                        && (e.TmdbId != null || (e.ImdbId != null && e.ImdbId != "") || e.TvdbId != null || e.TraktId != null));

        if (!account.SyncMovies)
            query = query.Where(e => e.MediaType != "movie");
        if (!account.SyncEpisodes)
            query = query.Where(e => e.MediaType != "episode");

        // Full warehouse history for this account (not a recent window) so older films
        // like Office Space are not dropped before the movies-first catch-up order.
        var rows = await query
            .OrderByDescending(e => e.PlayedAtUtc)
            .ToListAsync(ct);

        return rows
            .GroupBy(e => e.CanonicalMediaKey ?? $"{e.MediaType}:{e.ImdbId}:{e.TmdbId}:{e.TvdbId}:{e.SeasonNumber}:{e.EpisodeNumber}:{e.Title}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => string.Equals(e.MediaType, "movie", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(e => e.PlayedAtUtc)
            .ToList();
    }

    private async Task<List<(string Id, string Name)>> ResolveMediaServerUsersAsync(
        TraktAccountEntity account,
        string server,
        PlaybackReportingClient client,
        CancellationToken ct)
    {
        var serverUsers = await client.ListUsersAsync(ct);
        if (serverUsers.Count == 0)
            return [];

        var mapped = ParseMappedUsers(account)
            .Where(u => string.IsNullOrWhiteSpace(u.Source)
                        || string.Equals(u.Source, server, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var targets = new List<(string Id, string Name)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var map in mapped)
        {
            if (!string.IsNullOrWhiteSpace(map.UserId))
            {
                var byId = serverUsers.FirstOrDefault(u => string.Equals(u.Id, map.UserId, StringComparison.OrdinalIgnoreCase));
                if (byId.Id is { Length: > 0 } && seen.Add(byId.Id))
                    targets.Add(byId);
                continue;
            }

            var byName = serverUsers.FirstOrDefault(u =>
                string.Equals(u.Name, map.UserName, StringComparison.OrdinalIgnoreCase));
            if (byName.Id is { Length: > 0 } && seen.Add(byName.Id))
                targets.Add(byName);
        }

        // Always try CanonicalUserName on the media server when no explicit map matched.
        if (targets.Count == 0)
        {
            var canonical = serverUsers.FirstOrDefault(u =>
                string.Equals(u.Name, account.CanonicalUserName, StringComparison.OrdinalIgnoreCase));
            if (canonical.Id is { Length: > 0 })
                targets.Add(canonical);
        }

        return targets;
    }

    private static HashSet<string> LocalUserNames(TraktAccountEntity account)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { account.CanonicalUserName };
        foreach (var u in ParseMappedUsers(account))
        {
            if (!string.IsNullOrWhiteSpace(u.UserName))
                set.Add(u.UserName);
        }

        return set;
    }

    private static List<TraktMappedUser> ParseMappedUsers(TraktAccountEntity account)
    {
        try
        {
            return JsonSerializer.Deserialize<List<TraktMappedUser>>(account.MappedUsersJson) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
