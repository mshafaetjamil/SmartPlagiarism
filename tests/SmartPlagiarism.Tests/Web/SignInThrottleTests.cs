using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SmartPlagiarism.Web.Security;

namespace SmartPlagiarism.Tests.Web;

/// <summary>Counting failed sign-ins per client address, and which addresses count as one.</summary>
public sealed class SignInThrottleTests : IDisposable
{
    private readonly SignInThrottle _throttle = new(Options.Create(new SignInThrottleOptions { MaxFailuresPerAddress = 3 }));

    [Fact]
    public void AnAddress_IsBlockedOnceItReachesTheLimit_AndOthersAreNot()
    {
        var address = IPAddress.Parse("203.0.113.7");

        RecordFailures(address, 2);
        _throttle.IsBlocked(address).Should().BeFalse();

        RecordFailures(address, 1);
        _throttle.IsBlocked(address).Should().BeTrue();
        _throttle.IsBlocked(IPAddress.Parse("203.0.113.8")).Should().BeFalse();
    }

    [Fact]
    public void IPv6Addresses_InOneSlash64_CountTogether()
    {
        RecordFailures(IPAddress.Parse("2001:db8:1:2::1"), 1);
        RecordFailures(IPAddress.Parse("2001:db8:1:2::2"), 1);
        RecordFailures(IPAddress.Parse("2001:db8:1:2:ffff::3"), 1);

        _throttle.IsBlocked(IPAddress.Parse("2001:db8:1:2::99")).Should().BeTrue();
        _throttle.IsBlocked(IPAddress.Parse("2001:db8:1:3::1")).Should().BeFalse();
    }

    [Fact]
    public void AnIPv4AddressMappedToIPv6_CountsAsTheIPv4Address()
    {
        RecordFailures(IPAddress.Parse("::ffff:198.51.100.4"), 3);

        _throttle.IsBlocked(IPAddress.Parse("198.51.100.4")).Should().BeTrue();
    }

    public void Dispose() => _throttle.Dispose();

    private void RecordFailures(IPAddress address, int count)
    {
        for (var failure = 0; failure < count; failure++)
        {
            _throttle.RecordFailure(address);
        }
    }
}
