using SmartPlagiarism.Core.DTOs;
using SmartPlagiarism.Core.DTOs.Admin;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>A key as the admin list shows it; the key itself is never available again.</summary>
public sealed record ApiKeyDto(
    int Id,
    string Name,
    string Prefix,
    DateTime CreatedAtUtc,
    DateTime? LastUsedAtUtc,
    DateTime? RevokedAtUtc);

/// <param name="Key">The full key - shown to the admin this once, and stored nowhere.</param>
public sealed record CreatedApiKey(int Id, string Name, string Key);

/// <summary>The integration a valid key belongs to.</summary>
public sealed record ApiClient(int ApiKeyId, string Name);

/// <summary>Issues, lists, revokes and checks REST API keys.</summary>
public interface IApiKeyService
{
    /// <summary>One page of the keys, newest first.</summary>
    Task<PagedResult<ApiKeyDto>> GetKeysAsync(int page, CancellationToken cancellationToken = default);

    /// <summary>Null name or too long a name is refused with a reason.</summary>
    Task<(AdminResult Result, CreatedApiKey? Key)> CreateAsync(
        string name,
        string adminUserId,
        CancellationToken cancellationToken = default);

    Task<AdminResult> RevokeAsync(int id, string adminUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The client a presented key belongs to, or null when the key is unknown,
    /// malformed or revoked. Compares hashes in constant time.
    /// </summary>
    Task<ApiClient?> AuthenticateAsync(string? presentedKey, CancellationToken cancellationToken = default);
}
