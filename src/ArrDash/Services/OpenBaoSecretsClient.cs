using System.Text.Json;
using VaultSharp;
using VaultSharp.V1.AuthMethods.AppRole;
using VaultSharp.V1.Commons;

namespace ArrDash.Services;

/// <summary>
/// Thin OpenBao (Vault-compatible) client for ArrDash media-service secrets via AppRole.
/// </summary>
public sealed class OpenBaoSecretsClient(ILogger<OpenBaoSecretsClient> logger)
{
    public const string SecretPath = "arrdash/media-services";
    public const string MountPoint = "secret";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENBAO_ADDR"))
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENBAO_ROLE_ID"))
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENBAO_SECRET_ID"));

    public async Task<ServiceSecretsFile?> ReadMediaServicesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var client = CreateClient();
        Secret<SecretData> secret;
        try
        {
            secret = await client.V1.Secrets.KeyValue.V2.ReadSecretAsync(
                path: SecretPath,
                mountPoint: MountPoint);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            logger.LogWarning("OpenBao secret {Path} not found", $"{MountPoint}/{SecretPath}");
            return null;
        }

        if (secret.Data?.Data is null || secret.Data.Data.Count == 0)
            return new ServiceSecretsFile();

        var flat = secret.Data.Data.ToDictionary(
            kv => kv.Key,
            kv => kv.Value?.ToString());
        var json = JsonSerializer.Serialize(flat, JsonOptions);
        return JsonSerializer.Deserialize<ServiceSecretsFile>(json, JsonOptions) ?? new ServiceSecretsFile();
    }

    public async Task WriteMediaServicesAsync(ServiceSecretsFile secrets, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(secrets, JsonOptions);
        var flat = JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions)
                   ?? new Dictionary<string, string?>();

        // KV v2 replaces the entire secret on write — omit null/empty so we don't store blanks.
        var data = flat
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => (object)kv.Value!);

        var client = CreateClient();
        await client.V1.Secrets.KeyValue.V2.WriteSecretAsync(
            path: SecretPath,
            data: data,
            mountPoint: MountPoint);
        logger.LogInformation("Wrote {Count} fields to OpenBao {Path}", data.Count, $"{MountPoint}/{SecretPath}");
    }

    public const string WebhookTokenPath = "arrdash/webhook-token";

    public async Task<string?> ReadWebhookTokenAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var client = CreateClient();
        try
        {
            var secret = await client.V1.Secrets.KeyValue.V2.ReadSecretAsync(
                path: WebhookTokenPath,
                mountPoint: MountPoint);
            if (secret.Data?.Data is null)
                return null;
            if (secret.Data.Data.TryGetValue("token", out var tokenObj))
                return tokenObj?.ToString()?.Trim();
            return null;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    public async Task WriteWebhookTokenAsync(string token, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Webhook token is required.", nameof(token));

        var client = CreateClient();
        await client.V1.Secrets.KeyValue.V2.WriteSecretAsync(
            path: WebhookTokenPath,
            data: new Dictionary<string, object> { ["token"] = token.Trim() },
            mountPoint: MountPoint);
        logger.LogInformation("Wrote webhook token to OpenBao {Path}", $"{MountPoint}/{WebhookTokenPath}");
    }

    private static IVaultClient CreateClient()
    {
        var addr = Environment.GetEnvironmentVariable("OPENBAO_ADDR")?.Trim()
                   ?? throw new InvalidOperationException("OPENBAO_ADDR is not set");
        var roleId = Environment.GetEnvironmentVariable("OPENBAO_ROLE_ID")?.Trim()
                     ?? throw new InvalidOperationException("OPENBAO_ROLE_ID is not set");
        var secretId = Environment.GetEnvironmentVariable("OPENBAO_SECRET_ID")?.Trim()
                       ?? throw new InvalidOperationException("OPENBAO_SECRET_ID is not set");

        var auth = new AppRoleAuthMethodInfo(roleId, secretId);
        var settings = new VaultClientSettings(addr.TrimEnd('/'), auth);
        return new VaultClient(settings);
    }

    private static bool IsNotFound(Exception ex)
    {
        var msg = ex.Message ?? "";
        return msg.Contains("404", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }
}
