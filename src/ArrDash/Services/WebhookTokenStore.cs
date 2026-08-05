using System.Security.Cryptography;

namespace ArrDash.Services;

/// <summary>
/// Shared secret for Emby/Plex → ArrDash webhooks. Stored at OpenBao
/// <c>secret/arrdash/webhook-token</c> (or appdata file when OpenBao is unset).
/// </summary>
public sealed class WebhookTokenStore(
    IWebHostEnvironment env,
    OpenBaoSecretsClient openBao,
    ILogger<WebhookTokenStore> logger)
{
    private readonly string _filePath = Path.Combine(
        Environment.GetEnvironmentVariable("ARRDASH_CONFIG_PATH") ?? Path.Combine(env.ContentRootPath, "config"),
        "webhook-token.txt");
    private readonly object _lock = new();
    private string? _token;

    public string? CurrentToken
    {
        get { lock (_lock) return _token; }
    }

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(CurrentToken))
            return;

        string? loaded = null;
        if (OpenBaoSecretsClient.IsConfigured)
        {
            try
            {
                loaded = await openBao.ReadWebhookTokenAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read webhook token from OpenBao");
            }
        }
        else if (File.Exists(_filePath))
        {
            loaded = (await File.ReadAllTextAsync(_filePath, ct)).Trim();
        }

        if (string.IsNullOrWhiteSpace(loaded))
            loaded = await RotateAsync(ct);
        else
            lock (_lock) _token = loaded;
    }

    public async Task<string> RotateAsync(CancellationToken ct = default)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        if (OpenBaoSecretsClient.IsConfigured)
            await openBao.WriteWebhookTokenAsync(token, ct);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await File.WriteAllTextAsync(_filePath, token, ct);
        }

        lock (_lock) _token = token;
        logger.LogInformation("Webhook token rotated / created");
        return token;
    }

    public bool IsValid(string? provided)
    {
        var current = CurrentToken;
        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(provided))
            return false;
        var a = System.Text.Encoding.UTF8.GetBytes(current);
        var b = System.Text.Encoding.UTF8.GetBytes(provided);
        if (a.Length != b.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
