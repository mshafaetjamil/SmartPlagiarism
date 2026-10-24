using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace SmartPlagiarism.Web.Security;

/// <summary>Limits on failed sign-ins per client address, bound from "SignIn".</summary>
public sealed class SignInThrottleOptions
{
    public const string SectionName = "SignIn";

    /// <summary>Failed sign-ins one address may make within <see cref="FailureWindow"/>.</summary>
    [Range(1, 10_000)]
    public int MaxFailuresPerAddress { get; set; } = 20;

    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan FailureWindow { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Refuses sign-in from an address that has failed too often lately. Each
/// account already locks itself after a few wrong passwords; this stops one
/// address trying a common password against every account instead, a few
/// times each, which per-account lockout never notices.
///
/// Only failures count. A computer lab behind one address, where a whole class
/// signs in at once, is never held up by its classmates. Kept in memory: the
/// counts reset when the server restarts, which only gives an attacker a fresh
/// allowance as often as the server restarts.
/// </summary>
public sealed class SignInThrottle : IDisposable
{
    private readonly PartitionedRateLimiter<string> _failures;

    public SignInThrottle(IOptions<SignInThrottleOptions> options)
    {
        var settings = options.Value;

        _failures = PartitionedRateLimiter.Create<string, string>(address =>
            RateLimitPartition.GetSlidingWindowLimiter(address, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = settings.MaxFailuresPerAddress,
                Window = settings.FailureWindow,
                SegmentsPerWindow = 5,
                QueueLimit = 0,
            }));
    }

    /// <summary>True when <paramref name="address"/> has used up its failed attempts for now.</summary>
    public bool IsBlocked(IPAddress? address) =>
        _failures.GetStatistics(Key(address)) is { CurrentAvailablePermits: <= 0 };

    public void RecordFailure(IPAddress? address)
    {
        using var counted = _failures.AttemptAcquire(Key(address));
    }

    public void Dispose() => _failures.Dispose();

    /// <summary>
    /// IPv4 by address. IPv6 by /64 network, since one home or office connection
    /// is usually given a whole /64 and could otherwise use a fresh address for
    /// every attempt.
    /// </summary>
    private static string Key(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}

public static class SignInThrottleExtensions
{
    public static IServiceCollection AddSignInThrottle(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SignInThrottleOptions>()
            .Bind(configuration.GetSection(SignInThrottleOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services.AddSingleton<SignInThrottle>();
    }
}
