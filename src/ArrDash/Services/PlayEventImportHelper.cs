using System.Text.Json;
using ArrDash.Data.Entities;
using ArrDash.Services.Clients;

namespace ArrDash.Services;

public static class PlayEventImportHelper
{
    public static PlayEventEntity MapEntity(ImportedPlayEvent ev)
    {
        var origin = ev.Origin ?? ev.Source;
        var key = CanonicalMediaKeyBuilder.Build(
            ev.MediaType,
            ev.ImdbId,
            ev.TmdbId,
            ev.TvdbId,
            ev.TraktId,
            ev.MediaType == "episode" ? ev.SeriesTitle ?? ev.Title : ev.Title,
            ev.Year,
            ev.SeasonNumber,
            ev.EpisodeNumber);

        return new PlayEventEntity
        {
            Source = ev.Source,
            ExternalPlayId = ev.ExternalPlayId,
            UserDisplayName = ev.UserDisplayName,
            UserExternalId = ev.UserExternalId,
            Title = ev.Title,
            SeriesTitle = ev.SeriesTitle,
            MediaType = ev.MediaType,
            GenresJson = JsonSerializer.Serialize(ev.Genres),
            Client = ev.Client,
            Platform = ev.Platform,
            PlayedAtUtc = ev.PlayedAtUtc.UtcDateTime,
            DurationSeconds = ev.DurationSeconds,
            ExternalItemId = ev.ExternalItemId,
            ItemTitle = ev.ItemTitle,
            GrandparentExternalId = ev.GrandparentExternalId,
            ThumbPath = ev.ThumbPath,
            TranscodeDecision = ev.TranscodeDecision,
            LibraryName = ev.LibraryName,
            LibraryExternalId = ev.LibraryExternalId,
            ProgressPercent = ev.ProgressPercent,
            Origin = origin,
            Year = ev.Year,
            SeasonNumber = ev.SeasonNumber,
            EpisodeNumber = ev.EpisodeNumber,
            ImdbId = ev.ImdbId,
            TmdbId = ev.TmdbId,
            TvdbId = ev.TvdbId,
            TraktId = ev.TraktId,
            WasCompleted = ev.WasCompleted,
            DurationIsEstimated = ev.DurationIsEstimated,
            CanonicalMediaKey = key
        };
    }

    /// <summary>
    /// Fills missing episode indexes / titles on an already-imported row when a later
    /// Tracearr (or other) fetch has them. Returns true when any field changed.
    /// </summary>
    public static bool TryBackfillEpisodeMetadata(PlayEventEntity existing, ImportedPlayEvent incoming)
    {
        var changed = false;

        if (string.IsNullOrWhiteSpace(existing.ItemTitle) && !string.IsNullOrWhiteSpace(incoming.ItemTitle))
        {
            existing.ItemTitle = incoming.ItemTitle;
            changed = true;
        }

        if (existing.SeasonNumber is null && incoming.SeasonNumber is int sn)
        {
            existing.SeasonNumber = sn;
            changed = true;
        }

        if (existing.EpisodeNumber is null && incoming.EpisodeNumber is int en)
        {
            existing.EpisodeNumber = en;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(existing.SeriesTitle) && !string.IsNullOrWhiteSpace(incoming.SeriesTitle))
        {
            existing.SeriesTitle = incoming.SeriesTitle;
            changed = true;
        }

        if (!changed)
            return false;

        existing.CanonicalMediaKey = CanonicalMediaKeyBuilder.Build(
            existing.MediaType,
            existing.ImdbId,
            existing.TmdbId,
            existing.TvdbId,
            existing.TraktId,
            existing.MediaType == "episode" ? existing.SeriesTitle ?? existing.Title : existing.Title,
            existing.Year,
            existing.SeasonNumber,
            existing.EpisodeNumber);
        return true;
    }
}
