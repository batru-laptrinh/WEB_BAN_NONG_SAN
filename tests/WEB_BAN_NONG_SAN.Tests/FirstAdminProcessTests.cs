using System.Diagnostics;
using WEB_BAN_NONG_SAN.Tools;
using Xunit;

namespace WEB_BAN_NONG_SAN.Tests;

public sealed class FirstAdminProcessTests
{
    public static TheoryData<string, string, string[], int> GuardCases => new()
    {
        { "Production", "Production", ["--grant-first-admin", "--email", "operator@example.invalid"], 3 },
        { "Staging", "Staging", ["--grant-first-admin", "--email", "operator@example.invalid"], 3 },
        { "Production", "Development", ["--grant-first-admin", "--email", "operator@example.invalid"], 3 },
        { "Development", "Production", ["--grant-first-admin", "--email", "operator@example.invalid"], 3 },
        { "Development", "Development", ["--grant-first-admin"], 2 },
        { "Development", "Development", ["--grant-first-admin", "--email", "operator@example.invalid", "--yes"], 2 },
        { "Production", "Production", ["--grant-first-admin", "--email", "operator@example.invalid", "--environment", "Development"], 3 },
        { "Development", "Development", ["--grant-first-admin=true", "--email", "operator@example.invalid"], 2 }
    };

    [Theory]
    [MemberData(nameof(GuardCases))]
    public async Task ExecutableExitsBeforeSqlOrWebStartup(
        string aspnetEnvironment, string dotnetEnvironment, string[] args, int expectedExitCode)
    {
        var assemblyPath = typeof(FirstAdminCommand).Assembly.Location;
        const string marker = "INVALID_CONNECTION_GUARD_TEST_DO_NOT_ECHO";
        var start = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = Path.GetDirectoryName(assemblyPath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(assemblyPath);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        start.Environment["ASPNETCORE_ENVIRONMENT"] = aspnetEnvironment;
        start.Environment["DOTNET_ENVIRONMENT"] = dotnetEnvironment;
        // Even a regressed guard cannot reach a real SQL database: this is not a
        // connection string, and takes precedence over JSON/User Secrets bindings.
        start.Environment["ConnectionStrings__DefaultConnection"] = marker;

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        var output = await stdout + await stderr;
        Assert.Equal(expectedExitCode, process.ExitCode);
        Assert.DoesNotContain(marker, output);
        Assert.DoesNotContain("Now listening", output);
        Assert.DoesNotContain("SqlException", output);
    }
}
