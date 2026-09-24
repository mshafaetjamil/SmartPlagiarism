namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// A credential for a system integrating over the REST API - the university
/// portal. Only a hash of the key is stored; the key itself is shown once, when
/// an admin creates it.
/// </summary>
public class ApiKey
{
    public const int MaxNameLength = 100;

    /// <summary>Length of <see cref="Prefix"/>: "spk_" and eight random characters.</summary>
    public const int PrefixLength = 12;

    public int Id { get; set; }

    /// <summary>Who the key is for, such as "Student portal (production)".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The key's first characters, stored in the clear so the key can be found
    /// without scanning every hash, and so an admin can tell keys apart.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>SHA-256 of the whole key, as lower-case hex.</summary>
    public string KeyHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>The admin who created it. Not a foreign key, as in the audit log.</summary>
    public string? CreatedByUserId { get; set; }

    public DateTime? LastUsedAtUtc { get; set; }

    /// <summary>Set once revoked; a revoked key never authenticates again.</summary>
    public DateTime? RevokedAtUtc { get; private set; }

    public bool IsRevoked => RevokedAtUtc is not null;

    public void Revoke(DateTime nowUtc)
    {
        RevokedAtUtc ??= nowUtc;
    }
}
