using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Auditing;
using SmartPlagiarism.Core.DTOs.Admin;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Admin;

/// <inheritdoc cref="IApiKeyService"/>
/// <remarks>
/// A key is "spk_" + 8 random characters (the prefix) + "_" + 43 more (256 bits).
/// Only its SHA-256 is stored. A fast hash is right here, unlike for passwords:
/// the key is random, not chosen by a person, so there is nothing to guess and a
/// slow hash would only slow every API request down.
/// </remarks>
public class ApiKeyService : IApiKeyService
{
    private const string KeyStart = "spk_";

    /// <summary>
    /// Recording every use would mean a database write per request; once a
    /// minute is plenty for "last used".
    /// </summary>
    private static readonly TimeSpan s_lastUsedGranularity = TimeSpan.FromMinutes(1);

    private readonly AppDbContext _context;
    private readonly IAuditService _audit;

    public ApiKeyService(AppDbContext context, IAuditService audit)
    {
        _context = context;
        _audit = audit;
    }

    public async Task<IReadOnlyList<ApiKeyDto>> GetKeysAsync(CancellationToken cancellationToken = default) =>
        await _context.ApiKeys
            .AsNoTracking()
            .OrderByDescending(key => key.CreatedAtUtc)
            .Select(key => new ApiKeyDto(key.Id, key.Name, key.Prefix, key.CreatedAtUtc, key.LastUsedAtUtc, key.RevokedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<(AdminResult Result, CreatedApiKey? Key)> CreateAsync(
        string name,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return (AdminResult.Failure("Give the key a name, such as the system that will use it."), null);
        }

        if (trimmed.Length > ApiKey.MaxNameLength)
        {
            return (AdminResult.Failure(string.Create(
                CultureInfo.InvariantCulture,
                $"The name can be at most {ApiKey.MaxNameLength} characters.")), null);
        }

        var prefix = KeyStart + RandomText(bytes: 6)[..(ApiKey.PrefixLength - KeyStart.Length)];
        var key = $"{prefix}_{RandomText(bytes: 32)}";

        var apiKey = new ApiKey
        {
            Name = trimmed,
            Prefix = prefix,
            KeyHash = Hash(key),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = adminUserId,
        };

        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            new AuditEntry(AuditActions.ApiKeyCreated, AuditEntityTypes.ApiKey, Id(apiKey.Id), adminUserId, $"{trimmed} ({prefix})"),
            cancellationToken);

        return (AdminResult.Success, new CreatedApiKey(apiKey.Id, apiKey.Name, key));
    }

    public async Task<AdminResult> RevokeAsync(int id, string adminUserId, CancellationToken cancellationToken = default)
    {
        var apiKey = await _context.ApiKeys.FindAsync([id], cancellationToken);

        if (apiKey is null)
        {
            return AdminResult.Failure("That key no longer exists.");
        }

        apiKey.Revoke(DateTime.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            new AuditEntry(AuditActions.ApiKeyRevoked, AuditEntityTypes.ApiKey, Id(id), adminUserId, $"{apiKey.Name} ({apiKey.Prefix})"),
            cancellationToken);

        return AdminResult.Success;
    }

    public async Task<ApiClient?> AuthenticateAsync(string? presentedKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedKey)
            || !presentedKey.StartsWith(KeyStart, StringComparison.Ordinal)
            || presentedKey.Length <= ApiKey.PrefixLength)
        {
            return null;
        }

        var prefix = presentedKey[..ApiKey.PrefixLength];
        var apiKey = await _context.ApiKeys.FirstOrDefaultAsync(key => key.Prefix == prefix, cancellationToken);

        // Constant-time, so response timing reveals nothing about how much of a
        // guessed key was right.
        if (apiKey is null
            || apiKey.IsRevoked
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(apiKey.KeyHash),
                Encoding.ASCII.GetBytes(Hash(presentedKey))))
        {
            return null;
        }

        var now = DateTime.UtcNow;

        if (apiKey.LastUsedAtUtc is not { } lastUsed || now - lastUsed >= s_lastUsedGranularity)
        {
            apiKey.LastUsedAtUtc = now;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new ApiClient(apiKey.Id, apiKey.Name);
    }

    private static string Hash(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    /// <summary>URL-safe Base64 without padding, so keys survive headers and copy-paste.</summary>
    private static string RandomText(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);
}
