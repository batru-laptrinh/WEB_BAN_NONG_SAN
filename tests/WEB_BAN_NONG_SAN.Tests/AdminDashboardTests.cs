using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;
using Xunit;

namespace WEB_BAN_NONG_SAN.Tests;

// Full MVC, antiforgery, cookies and Identity stores; only the database is replaced.
// Every factory uses its own SQLite :memory: connection, never application SQL settings.
public sealed class AdminDashboardTests
{
    [Theory]
    [InlineData("/Admin/Dashboard")]
    [InlineData("/Admin/Dashboard/Index")]
    public async Task AnonymousRequestsAreChallengedByTheRealCookieHandler(string path)
    {
        await using var factory = new DashboardApplication();
        using var client = factory.NewClient();

        var response = await client.GetAsync(path);

        AssertRedirect(response, "/Account/Login");
        Assert.Contains("ReturnUrl=", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task CustomerCannotReachAdminEvenByTypingItsUrl()
    {
        await using var factory = new DashboardApplication();
        using var client = factory.NewClient();
        await factory.CreateUserAsync("customer@example.invalid");
        await factory.LoginAsync(client, "customer@example.invalid");

        var response = await client.GetAsync("/Admin/Dashboard");

        AssertRedirect(response, "/Account/AccessDenied");
        Assert.DoesNotContain("Tổng quan quản trị", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task DashboardCountsActualCustomerMembershipIncludingDualRoleAdmin()
    {
        await using var factory = new DashboardApplication();
        using var client = factory.NewClient();
        await factory.CreateUserAsync("admin@example.invalid", admin: true);
        await factory.CreateUserAsync("buyer@example.invalid");
        await factory.CreateUserAsync("admin-only@example.invalid", admin: true, customer: false);
        await factory.CreateUserAsync("without-role@example.invalid", customer: false);
        await factory.LoginAsync(client, "admin@example.invalid");

        var response = await client.GetAsync("/Admin/Dashboard");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Tổng quan quản trị", html);
        Assert.Contains("admin@example.invalid", html);
        Assert.Matches("data-testid=\"customer-count\"[^>]*>\\s*2\\s*<", html);
        Assert.Contains("/css/admin", html);
        Assert.Contains("/Account/Logout", html);
        Assert.Contains("href=\"/Admin/Products\"", html);
        Assert.Contains("href=\"/Admin/Reports\"", html);
        Assert.DoesNotContain("href=\"#\"", html);
        Assert.True(response.Headers.CacheControl?.NoStore);

        await factory.CreateUserAsync("later-buyer@example.invalid");
        var refreshedHtml = await client.GetStringAsync("/Admin/Dashboard");
        Assert.Matches("data-testid=\"customer-count\"[^>]*>\\s*3\\s*<", refreshedHtml);
    }

    [Fact]
    public async Task EmptyCustomerMembershipIsARealZeroInsteadOfAnError()
    {
        await using var factory = new DashboardApplication();
        using var client = factory.NewClient();
        await factory.CreateUserAsync("admin-only@example.invalid", admin: true, customer: false);
        await factory.LoginAsync(client, "admin-only@example.invalid");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Admin/Dashboard"));

        Assert.Matches("data-testid=\"customer-count\"[^>]*>\\s*0\\s*<", html);
        Assert.DoesNotContain("Chưa tải được", html);
    }

    [Fact]
    public async Task OnlyAdminGetsManagementLinkOnCustomerWebsite()
    {
        await using var factory = new DashboardApplication();
        using var anonymous = factory.NewClient();
        using var customer = factory.NewClient();
        using var admin = factory.NewClient();
        await factory.CreateUserAsync("buyer@example.invalid");
        await factory.CreateUserAsync("admin@example.invalid", admin: true);
        await factory.LoginAsync(customer, "buyer@example.invalid");
        await factory.LoginAsync(admin, "admin@example.invalid");

        var guestHtml = WebUtility.HtmlDecode(await anonymous.GetStringAsync("/"));
        var customerHtml = WebUtility.HtmlDecode(await customer.GetStringAsync("/"));
        var adminHtml = WebUtility.HtmlDecode(await admin.GetStringAsync("/"));

        Assert.DoesNotContain("Trang quản trị", guestHtml);
        Assert.Contains("Đăng nhập", guestHtml);
        Assert.Contains("Đăng ký", guestHtml);
        Assert.DoesNotContain("Trang quản trị", customerHtml);
        Assert.Contains("Trang quản trị", adminHtml);
        Assert.Contains("href=\"/Admin/Dashboard\"", adminHtml);
    }

    [Fact]
    public async Task AdminLogoutUsesExistingPostEndpointAndRemovesAccess()
    {
        await using var factory = new DashboardApplication();
        using var client = factory.NewClient();
        await factory.CreateUserAsync("admin@example.invalid", admin: true);
        await factory.LoginAsync(client, "admin@example.invalid");
        var dashboard = await client.GetStringAsync("/Admin/Dashboard");
        var token = AntiforgeryToken(dashboard);

        var logout = await client.PostAsync("/Account/Logout", Form(
            ("__RequestVerificationToken", token)));

        AssertRedirect(logout, "/");
        AssertRedirect(await client.GetAsync("/Admin/Dashboard"), "/Account/Login");
        Assert.DoesNotContain("Trang quản trị", WebUtility.HtmlDecode(await client.GetStringAsync("/")));
    }

    [Fact]
    public async Task LogoutWithoutAntiforgeryIsRejectedAndKeepsTheSession()
    {
        await using var factory = new DashboardApplication();
        using var client = factory.NewClient();
        await factory.CreateUserAsync("admin@example.invalid", admin: true);
        await factory.LoginAsync(client, "admin@example.invalid");

        var response = await client.PostAsync("/Account/Logout", Form());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Dashboard")).StatusCode);
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    [InlineData("/Account/ForgotPassword")]
    [InlineData("/Home/Privacy")]
    public async Task ExistingPublicRoutesStillRender(string path)
    {
        await using var factory = new DashboardApplication();
        using var client = factory.NewClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task RegistrationStillSignsInWithCustomerOnly()
    {
        await using var factory = new DashboardApplication();
        using var client = factory.NewClient();
        var token = AntiforgeryToken(await client.GetStringAsync("/Account/Register"));

        var response = await client.PostAsync("/Account/Register", Form(
            ("__RequestVerificationToken", token),
            ("FullName", "Isolated registration test"),
            ("Email", "new-buyer@example.invalid"),
            ("Password", factory.Password),
            ("ConfirmPassword", factory.Password)));

        AssertRedirect(response, "/");
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync("new-buyer@example.invalid");
        Assert.NotNull(user);
        Assert.True(await users.IsInRoleAsync(user, "Customer"));
        Assert.False(await users.IsInRoleAsync(user, "Admin"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Profile")).StatusCode);
        AssertRedirect(await client.GetAsync("/Admin/Dashboard"), "/Account/AccessDenied");
    }

    [Fact]
    public async Task AdminLoginKeepsExistingHomeRedirectAndCanReadOwnProfile()
    {
        await using var factory = new DashboardApplication();
        using var client = factory.NewClient();
        await factory.CreateUserAsync("admin@example.invalid", admin: true);

        var login = await factory.LoginAsync(client, "admin@example.invalid");

        AssertRedirect(login, "/");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/ChangePassword")).StatusCode);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task FailedDatabaseReadShowsUnavailableInsteadOfAFalseZeroOrException(string environment)
    {
        await using var factory = new DashboardApplication(environment);
        using var client = factory.NewClient();
        Assert.Equal(environment, factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        await factory.CreateUserAsync("admin@example.invalid", admin: true);
        await factory.LoginAsync(client, "admin@example.invalid");
        using (var scope = factory.Services.CreateScope())
        {
            // Only this test's in-memory database is affected; force a real query error.
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"AspNetUserRoles\";");
        }

        var response = await client.GetAsync("/Admin/Dashboard");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Chưa tải được", html);
        Assert.DoesNotContain("SQLite", html);
        Assert.DoesNotContain("AspNetUserRoles", html);
        Assert.DoesNotContain("Exception", html);
        Assert.DoesNotMatch("data-testid=\"customer-count\"[^>]*>\\s*0\\s*<", html);
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(field => new KeyValuePair<string, string>(field.Key, field.Value)));

    private static string AntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Expected an antiforgery input in the actual rendered form.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static void AssertRedirect(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var location = response.Headers.Location.IsAbsoluteUri
            ? response.Headers.Location.PathAndQuery
            : response.Headers.Location.OriginalString;
        Assert.True(location == path || location.StartsWith(path + "?", StringComparison.Ordinal),
            $"Expected redirect to {path}, received {location}.");
    }

    private sealed class DashboardApplication : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection;
        private readonly string environment;

        public string Password { get; } = $"{Guid.NewGuid():N}!aA1";

        public DashboardApplication(string environment = "Development")
        {
            this.environment = environment;
            connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            using var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            db.Database.EnsureCreated();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            // Satisfy initial configuration validation; this string is NEVER used by SQL.
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Test-only connection replaced by SQLite");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            });
        }

        public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

        public async Task CreateUserAsync(string email, bool admin = false, bool customer = true)
        {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, FullName = "Isolated dashboard test" };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            if (customer)
            {
                Assert.True((await users.AddToRoleAsync(user, "Customer")).Succeeded);
            }
            if (admin)
            {
                Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
            }
        }

        public async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email)
        {
            var token = AntiforgeryToken(await client.GetStringAsync("/Account/Login"));
            var response = await client.PostAsync("/Account/Login", Form(
                ("__RequestVerificationToken", token), ("Email", email), ("Password", Password)));
            AssertRedirect(response, "/");
            return response;
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
