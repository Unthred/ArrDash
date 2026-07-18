using System.Text.Json;
using ArrDash.Data;
using ArrDash.Data.Entities;
using ArrDash.Models;
using ArrDash.Services.Clients;
using Microsoft.EntityFrameworkCore;

namespace ArrDash.Services;

public sealed class TraktMappedUser
{
    public string Source { get; set; } = "";
    public string UserName { get; set; } = "";
    public string? UserId { get; set; }
}

public sealed record TraktSyncPreview(
    string AccountId,
    int HistoryMovies,
    int HistoryEpisodes,
    int WouldImport,
    int WouldLinkExisting,
    int WouldPush,
    int Unmatched,
    IReadOnlyList<string> SampleTitles,
    string? Note);

public sealed class TraktAccountService(
    IDbContextFactory<ArrDashDbContext> dbFactory,
    TraktClient trakt,
    TraktTokenProtector protector,
    ILogger<TraktAccountService> logger)
{
    private readonly object _deviceLock = new();
    private TraktDeviceCodeResponse? _pendingDevice;
    private DateTimeOffset _pendingExpiresAt;
    private CancellationTokenSource? _pollCts;

    public async Task<IReadOnlyList<TraktAccountEntity>> ListAccountsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TraktAccounts.AsNoTracking().OrderBy(a => a.TraktUsername).ToListAsync(ct);
    }

    public async Task<(bool Ok, string Message, TraktDeviceCodeResponse? Device)> StartConnectAsync(CancellationToken ct)
    {
        if (!trakt.IsAppConfigured)
            return (false, "Set Trakt Client ID and Client Secret on the API keys tab first.", null);

        try
        {
            var device = await trakt.RequestDeviceCodeAsync(ct);
            if (device is null || string.IsNullOrWhiteSpace(device.DeviceCode))
                return (false, "Trakt did not return a device code.", null);

            lock (_deviceLock)
            {
                _pendingDevice = device;
                _pendingExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(device.ExpiresIn, 60));
            }

            _pollCts?.Cancel();
            // Do NOT link to the request CancellationToken — Blazor/API request completion
            // would cancel polling before the user finishes trakt.tv/activate.
            _pollCts = new CancellationTokenSource();
            _ = PollUntilAuthorizedAsync(device, _pollCts.Token);

            return (true, $"Visit {device.VerificationUrl} and enter code {device.UserCode}", device);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trakt device connect failed");
            return (false, ex.Message, null);
        }
    }

    public async Task<(bool Ok, string Message)> DisconnectAsync(string accountId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var account = await db.TraktAccounts.FindAsync([accountId], ct);
        if (account is null)
            return (false, "Account not found");

        db.TraktHistoryLinks.RemoveRange(db.TraktHistoryLinks.Where(l => l.AccountId == accountId));
        db.TraktAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
        return (true, "Disconnected");
    }

    public async Task<(bool Ok, string Message)> UpdateAccountAsync(TraktAccountEntity updates, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var account = await db.TraktAccounts.FindAsync([updates.Id], ct);
        if (account is null)
            return (false, "Account not found");

        account.CanonicalUserName = updates.CanonicalUserName.Trim();
        account.SyncMovies = updates.SyncMovies;
        account.SyncEpisodes = updates.SyncEpisodes;
        account.ImportToWarehouse = updates.ImportToWarehouse;
        account.PushToTrakt = updates.PushToTrakt;
        account.MarkPlexWatched = updates.MarkPlexWatched;
        account.MarkEmbyWatched = updates.MarkEmbyWatched;
        account.MarkJellyfinWatched = updates.MarkJellyfinWatched;
        account.MappedUsersJson = updates.MappedUsersJson;
        account.HistoryStartUtc = updates.HistoryStartUtc;
        account.LastPreviewAtUtc = null;
        account.LastPreviewJson = null;
        await db.SaveChangesAsync(ct);
        return (true, "Saved");
    }

    public async Task<(string AccessToken, TraktAccountEntity Account)> GetValidAccessTokenAsync(
        string accountId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var account = await db.TraktAccounts.FindAsync([accountId], ct)
            ?? throw new InvalidOperationException("Trakt account not found");

        var access = protector.Unprotect(account.EncryptedAccessToken);
        if (account.TokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            return (access, account);

        var refresh = protector.Unprotect(account.EncryptedRefreshToken);
        var renewed = await trakt.RefreshTokenAsync(refresh, ct)
            ?? throw new InvalidOperationException("Trakt token refresh failed");

        account.EncryptedAccessToken = protector.Protect(renewed.AccessToken);
        account.EncryptedRefreshToken = protector.Protect(renewed.RefreshToken);
        account.TokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(renewed.ExpiresIn);
        await db.SaveChangesAsync(ct);
        return (renewed.AccessToken, account);
    }

    private async Task PollUntilAuthorizedAsync(TraktDeviceCodeResponse device, CancellationToken ct)
    {
        var interval = Math.Max(device.Interval, 5);
        try
        {
            while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < _pendingExpiresAt)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), ct);
                var token = await trakt.PollDeviceTokenAsync(device.DeviceCode, ct);
                if (token is null)
                    continue;

                if (string.IsNullOrWhiteSpace(token.AccessToken))
                {
                    logger.LogWarning("Trakt device token response missing access_token");
                    continue;
                }

                var settings = await trakt.GetSettingsAsync(token.AccessToken, ct);
                var username = settings?.User?.Username ?? settings?.User?.Name ?? "trakt-user";

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var existing = await db.TraktAccounts
                    .FirstOrDefaultAsync(a => a.TraktUsername == username, ct);

                if (existing is null)
                {
                    existing = new TraktAccountEntity
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        TraktUsername = username,
                        CanonicalUserName = username
                    };
                    db.TraktAccounts.Add(existing);
                }

                existing.EncryptedAccessToken = protector.Protect(token.AccessToken);
                existing.EncryptedRefreshToken = protector.Protect(token.RefreshToken ?? "");
                existing.TokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn, 3600));
                existing.LastError = null;
                await db.SaveChangesAsync(ct);

                lock (_deviceLock)
                    _pendingDevice = null;

                logger.LogInformation("Connected Trakt account {Username}", username);
                return;
            }

            logger.LogWarning("Trakt device code expired before authorization completed");
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trakt device polling failed");
        }
    }
}
