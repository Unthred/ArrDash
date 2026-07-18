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
}
