using ArrDash.Services;
using ArrDash.Services.Clients;
using System.Text.Json;

namespace ArrDash.Tests.Services;

public sealed class LibraryWatchedToTraktServiceTests
{
    [Fact]
    public void BuildIds_prefers_trakt_then_imdb_then_tmdb()
    {
        var withTrakt = new LibraryWatchedItem("emby", "u", "1", "movie", "A", null, "tt1", 2, 3, 99, 2020, null, null, null, null);
        var traktJson = JsonSerializer.Serialize(LibraryWatchedToTraktService.BuildIds(withTrakt));
        Assert.Contains("\"trakt\":99", traktJson);

        var withImdb = new LibraryWatchedItem("emby", "u", "1", "movie", "A", null, "tt1", 2, 3, null, 2020, null, null, null, null);
        var imdbJson = JsonSerializer.Serialize(LibraryWatchedToTraktService.BuildIds(withImdb));
        Assert.Contains("\"imdb\":\"tt1\"", imdbJson);

        var withTmdb = new LibraryWatchedItem("emby", "u", "1", "movie", "A", null, null, 42, null, null, 2020, null, null, null, null);
        var tmdbJson = JsonSerializer.Serialize(LibraryWatchedToTraktService.BuildIds(withTmdb));
        Assert.Contains("\"tmdb\":42", tmdbJson);

        var none = new LibraryWatchedItem("emby", "u", "1", "movie", "A", null, null, null, null, null, 2020, null, null, null, null);
        Assert.Null(LibraryWatchedToTraktService.BuildIds(none));
    }
}
