using System.Text.Json;
using ArrDash.Configuration;
using ArrDash.Services.Clients;
using SocketIOClient;
using SocketIOClient.Transport;

namespace ArrDash.Services;

public sealed class AudiobookShelfActivityHostedService : BackgroundService
{
    private static readonly TimeSpan MinReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LibraryRefreshInterval = TimeSpan.FromMinutes(10);

    private readonly AudiobookShelfActivityTracker _tracker;
    private readonly MediaServiceOptionsAccessor _optionsAccessor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AudiobookShelfActivityHostedService> _logger;

    public AudiobookShelfActivityHostedService(
        AudiobookShelfActivityTracker tracker,
        MediaServiceOptionsAccessor optionsAccessor,
        IHttpClientFactory httpClientFactory,
        ILogger<AudiobookShelfActivityHostedService> logger)
    {
        _tracker = tracker;
        _optionsAccessor = optionsAccessor;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reconnectDelay = MinReconnectDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            var endpoint = _optionsAccessor.Options.AudiobookShelf;
            if (!endpoint.IsConfigured)
            {
                _tracker.SetConnected(false);
                _tracker.ClearScans();
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            try
            {
                await RefreshLibraryNamesAsync(endpoint, stoppingToken);
                await RunSocketSessionAsync(endpoint, stoppingToken);
                reconnectDelay = MinReconnectDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AudioBookShelf activity socket disconnected");
                _tracker.SetConnected(false);
            }

            await Task.Delay(reconnectDelay, stoppingToken);
            reconnectDelay = TimeSpan.FromMilliseconds(
                Math.Min(reconnectDelay.TotalMilliseconds * 2, MaxReconnectDelay.TotalMilliseconds));
        }
    }

    private async Task RunSocketSessionAsync(ServiceEndpoint endpoint, CancellationToken stoppingToken)
    {
        var uri = endpoint.Url.TrimEnd('/');
        using var client = new SocketIOClient.SocketIO(uri, new SocketIOOptions
        {
            Transport = TransportProtocol.WebSocket,
            Reconnection = false,
            ConnectionTimeout = TimeSpan.FromSeconds(15)
        });

        client.OnConnected += async (_, _) =>
        {
            try
            {
                await client.EmitAsync("auth", endpoint.ApiKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AudioBookShelf socket auth emit failed");
            }
        };

        client.On("init", response => HandleInit(response));
        client.On("scan_start", response => HandleScanStart(response));
        client.On("scan_complete", response => HandleScanComplete(response));
        client.On("invalid_token", _ =>
        {
            _logger.LogWarning("AudioBookShelf socket rejected API token");
            _tracker.SetConnected(false);
        });

        client.OnDisconnected += (_, reason) =>
        {
            _logger.LogDebug("AudioBookShelf socket disconnected: {Reason}", reason);
            _tracker.SetConnected(false);
        };

        await client.ConnectAsync();
        _tracker.SetConnected(true);
        _logger.LogInformation("AudioBookShelf activity socket connected");

        var lastLibraryRefresh = DateTimeOffset.UtcNow;
        try
        {
            while (!stoppingToken.IsCancellationRequested && client.Connected)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

                if (DateTimeOffset.UtcNow - lastLibraryRefresh >= LibraryRefreshInterval)
                {
                    await RefreshLibraryNamesAsync(endpoint, stoppingToken);
                    lastLibraryRefresh = DateTimeOffset.UtcNow;
                }
            }
        }
        finally
        {
            try
            {
                await client.DisconnectAsync();
            }
            catch
            {
                // ignore disconnect errors during shutdown
            }

            _tracker.SetConnected(false);
            _tracker.ClearScans();
        }
    }

    private void HandleInit(SocketIOResponse response)
    {
        try
        {
            var data = response.GetValue<JsonElement>();
            if (!data.TryGetProperty("librariesScanning", out var scanning) ||
                scanning.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var ids = scanning.EnumerateArray()
                .Select(el => el.GetString())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToList();

            _tracker.HandleInit(ids);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse AudioBookShelf init event");
        }
    }

    private void HandleScanStart(SocketIOResponse response)
    {
        try
        {
            var scan = response.GetValue<JsonElement>();
            _tracker.HandleScanStart(scan);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse AudioBookShelf scan_start event");
        }
    }

    private void HandleScanComplete(SocketIOResponse response)
    {
        try
        {
            var scan = response.GetValue<JsonElement>();
            _tracker.HandleScanComplete(scan);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse AudioBookShelf scan_complete event");
        }
    }

    private async Task RefreshLibraryNamesAsync(ServiceEndpoint endpoint, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(nameof(AudiobookShelfClient));
            var url = $"{endpoint.Url.TrimEnd('/')}/api/libraries";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("libraries", out var libraries) ||
                libraries.ValueKind != JsonValueKind.Array)
                return;

            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var library in libraries.EnumerateArray())
            {
                if (!library.TryGetProperty("id", out var idEl))
                    continue;

                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var name = library.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                names[id] = string.IsNullOrWhiteSpace(name) ? id : name;
            }

            _tracker.SetLibraryNames(names);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to refresh AudioBookShelf library names");
        }
    }
}
