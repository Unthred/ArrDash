using System.Text.Json;
using ArrDash.Data.Entities;
using ArrDash.Services.Clients;

namespace ArrDash.Services;

public static class PlayEventImportHelper
{
    public static PlayEventEntity MapEntity(ImportedPlayEvent ev) => new()
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
        GrandparentExternalId = ev.GrandparentExternalId,
        ThumbPath = ev.ThumbPath,
        TranscodeDecision = ev.TranscodeDecision,
        LibraryName = ev.LibraryName,
        LibraryExternalId = ev.LibraryExternalId,
        ProgressPercent = ev.ProgressPercent
    };
}
