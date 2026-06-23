using ArrDash.Components;
using ArrDash.Configuration;
using ArrDash.Hubs;
using ArrDash.Services;
using ArrDash.Services.Clients;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MediaServiceOptions>(builder.Configuration.GetSection(MediaServiceOptions.SectionName));
BindEnvironmentOverrides(builder.Configuration);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddSignalR();

builder.Services.AddHttpClient<SonarrClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<RadarrClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<LidarrClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<ChaptarrClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<AudiobookShelfClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<PlexClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<EmbyClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient(nameof(PosterProxyService), c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient(nameof(ServiceConnectionTester), c => c.Timeout = TimeSpan.FromSeconds(15));

builder.Services.AddSingleton<ServiceSecretsStore>();
builder.Services.AddSingleton<MediaServiceOptionsAccessor>();
builder.Services.AddSingleton<PosterProxyService>();
builder.Services.AddSingleton<HostSystemMetricsService>();
builder.Services.AddSingleton<LibraryStatsService>();
builder.Services.AddSingleton<DashboardState>();
builder.Services.AddSingleton<DashboardCollector>();
builder.Services.AddSingleton<DashboardRefreshService>();
builder.Services.AddSingleton<ServiceCredentialsPreviewService>();
builder.Services.AddSingleton<ServiceConnectionTester>();
builder.Services.AddSingleton<LayoutPreferencesService>();
builder.Services.AddSingleton<ThemeService>();
builder.Services.AddScoped<ExternalLinkService>();
builder.Services.AddHostedService<MediaAggregatorService>();

var app = builder.Build();

var prefs = app.Services.GetRequiredService<LayoutPreferencesService>();
await prefs.LoadAsync();

var secrets = app.Services.GetRequiredService<ServiceSecretsStore>();
await secrets.LoadAsync();
app.Services.GetRequiredService<MediaServiceOptionsAccessor>().Reload();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<DashboardHub>("/hubs/dashboard");
app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "arrdash" }));
app.MapGet("/api/dashboard", (DashboardState state) => Results.Json(state.Current));
app.MapGet("/api/poster/sonarr/{seriesId:int}", (int seriesId, PosterProxyService proxy, CancellationToken ct) =>
    proxy.FetchAsync("sonarr", seriesId, null, ct));
app.MapGet("/api/poster/radarr/{movieId:int}", (int movieId, PosterProxyService proxy, CancellationToken ct) =>
    proxy.FetchAsync("radarr", movieId, null, ct));
app.MapGet("/api/poster/lidarr/{artistId:int}", (int artistId, PosterProxyService proxy, CancellationToken ct) =>
    proxy.FetchAsync("lidarr", artistId, null, ct));
app.MapGet("/api/poster/chaptarr/book/{bookId:int}", (int bookId, PosterProxyService proxy, CancellationToken ct) =>
    proxy.FetchAsync("chaptarr", bookId, "book", ct));
app.MapGet("/api/poster/chaptarr/author/{authorId:int}", (int authorId, PosterProxyService proxy, CancellationToken ct) =>
    proxy.FetchAsync("chaptarr", authorId, "author", ct));
app.MapGet("/api/poster/audiobookshelf/{itemId}", (string itemId, PosterProxyService proxy, CancellationToken ct) =>
    proxy.FetchAudiobookShelfCoverAsync(itemId, ct));
app.MapGet("/api/thumbnail/plex", (string path, PosterProxyService proxy, CancellationToken ct) =>
    proxy.FetchStreamingThumbnailAsync("plex", path, ct));
app.MapGet("/api/thumbnail/emby/{itemId}", (string itemId, PosterProxyService proxy, CancellationToken ct) =>
    proxy.FetchStreamingThumbnailAsync("emby", itemId, ct));

WarnPrivateServiceUrls(app);

app.Run();

static void WarnPrivateServiceUrls(WebApplication app)
{
    var options = app.Services.GetRequiredService<MediaServiceOptionsAccessor>().Options;
    var urls = new (string Name, string? Url)[]
    {
        ("Sonarr", options.Sonarr.Url),
        ("Radarr", options.Radarr.Url),
        ("Lidarr", options.Lidarr.Url),
        ("Chaptarr", options.Chaptarr.Url),
        ("AudioBookShelf", options.AudiobookShelf.Url),
        ("slskd", options.Slskd.Url),
        ("Plex", options.Plex.Url),
        ("Emby", options.Emby.Url),
        ("Tautulli", options.Tautulli.Url),
    };

    foreach (var (name, url) in urls)
    {
        if (ServiceUrlRules.IsPrivateOrLoopbackUrl(url))
            app.Logger.LogWarning("{Service} URL {Url} uses a private address — use https://*.yeradonkey.com (split DNS)", name, url);
    }
}

static void BindEnvironmentOverrides(IConfigurationManager config)
{
    void Set(string envKey, string configPath, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            config[configPath] = value;
    }

    Set("SONARR_URL", "MediaServices:Sonarr:Url", Environment.GetEnvironmentVariable("SONARR_URL"));
    Set("SONARR_API_KEY", "MediaServices:Sonarr:ApiKey", Environment.GetEnvironmentVariable("SONARR_API_KEY"));
    Set("RADARR_URL", "MediaServices:Radarr:Url", Environment.GetEnvironmentVariable("RADARR_URL"));
    Set("RADARR_API_KEY", "MediaServices:Radarr:ApiKey", Environment.GetEnvironmentVariable("RADARR_API_KEY"));
    Set("LIDARR_URL", "MediaServices:Lidarr:Url", Environment.GetEnvironmentVariable("LIDARR_URL"));
    Set("LIDARR_API_KEY", "MediaServices:Lidarr:ApiKey", Environment.GetEnvironmentVariable("LIDARR_API_KEY"));
    Set("CHAPTARR_URL", "MediaServices:Chaptarr:Url", Environment.GetEnvironmentVariable("CHAPTARR_URL"));
    Set("CHAPTARR_API_KEY", "MediaServices:Chaptarr:ApiKey", Environment.GetEnvironmentVariable("CHAPTARR_API_KEY"));
    Set("AUDIOBOOKSHELF_URL", "MediaServices:AudiobookShelf:Url", Environment.GetEnvironmentVariable("AUDIOBOOKSHELF_URL"));
    Set("AUDIOBOOKSHELF_API_KEY", "MediaServices:AudiobookShelf:ApiKey", Environment.GetEnvironmentVariable("AUDIOBOOKSHELF_API_KEY"));
    Set("SLSKD_URL", "MediaServices:Slskd:Url", Environment.GetEnvironmentVariable("SLSKD_URL"));
    Set("SLSKD_API_KEY", "MediaServices:Slskd:ApiKey", Environment.GetEnvironmentVariable("SLSKD_API_KEY"));
    Set("PLEX_URL", "MediaServices:Plex:Url", Environment.GetEnvironmentVariable("PLEX_URL"));
    Set("PLEX_TOKEN", "MediaServices:Plex:Token", Environment.GetEnvironmentVariable("PLEX_TOKEN"));
    Set("EMBY_URL", "MediaServices:Emby:Url", Environment.GetEnvironmentVariable("EMBY_URL"));
    Set("EMBY_API_KEY", "MediaServices:Emby:ApiKey", Environment.GetEnvironmentVariable("EMBY_API_KEY"));
    Set("TAUTULLI_URL", "MediaServices:Tautulli:Url", Environment.GetEnvironmentVariable("TAUTULLI_URL"));
    Set("TAUTULLI_API_KEY", "MediaServices:Tautulli:ApiKey", Environment.GetEnvironmentVariable("TAUTULLI_API_KEY"));

    if (int.TryParse(Environment.GetEnvironmentVariable("POLL_INTERVAL_SECONDS"), out var poll))
        config["MediaServices:PollIntervalSeconds"] = poll.ToString();

    if (int.TryParse(Environment.GetEnvironmentVariable("RECENT_LIMIT"), out var limit))
        config["MediaServices:RecentLimit"] = limit.ToString();
}
