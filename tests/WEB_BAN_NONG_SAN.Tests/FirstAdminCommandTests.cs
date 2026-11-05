using WEB_BAN_NONG_SAN.Tools;
using Xunit;

namespace WEB_BAN_NONG_SAN.Tests;

public sealed class FirstAdminCommandTests
{
    [Fact]
    public void OrdinaryWebArgumentsDoNotRequestAdminCommand()
    {
        Assert.False(FirstAdminCommand.IsRequested([]));
        Assert.False(FirstAdminCommand.IsRequested(["--urls", "http://localhost:5083"]));
    }

    [Fact]
    public void ExplicitSwitchRequestsAdminCommand()
    {
        Assert.True(FirstAdminCommand.IsRequested(["--grant-first-admin", "--email", "operator@example.invalid"]));
    }

    [Fact]
    public void WellFormedCommandResolvesOnlyTheSpecifiedAccount()
    {
        Assert.True(FirstAdminCommand.TryGetEmail(
            ["--grant-first-admin", "--email", "operator@example.invalid"], out var email));
        Assert.Equal("operator@example.invalid", email);
    }

    public static TheoryData<string[]> InvalidArguments => new()
    {
        Array.Empty<string>(),
        new[] { "--grant-first-admin" },
        new[] { "--grant-first-admin", "--email" },
        new[] { "--grant-first-admin", "--email", "" },
        new[] { "--grant-first-admin", "--email", "   " },
        new[] { "--grant-first-admin", "--email", "invalid-address" },
        new[] { "--grant-first-admin", "--email", "operator@example.invalid\nCAP ADMIN other@example.invalid" },
        new[] { "--grant-first-admin", "--email", "operator@example.invalid", "--yes" },
        new[] { "--grant-first-admin", "--email", "operator@example.invalid", "--environment", "Development" },
        new[] { "--grant-first-admin", "--email", "operator@example.invalid", "--email", "other@example.invalid" },
        new[] { "--grant-first-admin", "--grant-first-admin", "--email", "operator@example.invalid" },
        new[] { "--email", "operator@example.invalid" },
        new[] { "--grant-first-admin=true", "--email", "operator@example.invalid" }
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void MissingAmbiguousOrUnsafeArgumentsAreRejected(string[] args)
    {
        Assert.False(FirstAdminCommand.TryGetEmail(args, out _));
    }
}
