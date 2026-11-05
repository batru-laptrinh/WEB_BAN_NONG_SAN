using System.Globalization;
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
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.Services.Email;
using Xunit;

namespace WEB_BAN_NONG_SAN.Tests;

public sealed class CommerceJourneyTests
{
    [Fact]
    public async Task EmailFailureDoesNotExposeProviderSecretsOrChangeCredentialsAndKeepsGenericResponse()
    {
        await using var app = new JourneyApplication(failEmail: true);
        using var client = app.Client();
        await app.SignInAsync(client, "mail-failure@example.invalid", false);
        var before = await app.ReadAsync(db => db.Users.AsNoTracking().Select(user => new { user.PasswordHash, user.SecurityStamp }).SingleAsync());
        var known = await PostForm(client, "/Account/ForgotPassword", "/Account/ForgotPassword", ("Email", "mail-failure@example.invalid"));
        Redirect(known);
        var knownPage = Decode(await client.GetStringAsync("/Account/ForgotPassword"));
        Redirect(await PostForm(client, "/Account/ForgotPassword", "/Account/ForgotPassword", ("Email", "missing@example.invalid")));
        var unknownPage = Decode(await client.GetStringAsync("/Account/ForgotPassword"));
        const string generic = "Nếu email đã đăng ký, vui lòng kiểm tra hộp thư";
        Assert.Contains(generic, knownPage);
        Assert.Contains(generic, unknownPage);
        Assert.DoesNotContain("SMTP_TEST_SECRET", knownPage);
        Assert.DoesNotContain("RESET_TEST_TOKEN", string.Join('\n', app.Logs.Messages));
        Assert.DoesNotContain("SMTP_TEST_SECRET", string.Join('\n', app.Logs.Messages));
        Assert.Contains(app.Logs.Messages, message => message.Contains("Không thể gửi email khôi phục mật khẩu.", StringComparison.Ordinal));
        var after = await app.ReadAsync(db => db.Users.AsNoTracking().Select(user => new { user.PasswordHash, user.SecurityStamp }).SingleAsync());
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task BrowserSecurityHeadersAndProductionCookiesAreConfigured(string environment)
    {
        await using var app = new JourneyApplication(environment);
        using var client = app.Client();
        var home = await client.GetAsync("/");
        Assert.Equal("nosniff", Assert.Single(home.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(home.Headers.GetValues("X-Frame-Options")));
        Assert.Contains("frame-ancestors 'none'", Assert.Single(home.Headers.GetValues("Content-Security-Policy")));
        var login = await client.GetAsync("/Account/Login");
        var cookies = login.Headers.GetValues("Set-Cookie");
        Assert.All(cookies, cookie => Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase));
        if (environment == "Production")
            Assert.All(cookies, cookie => Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AdminCreatesCatalogAndCouponBuyerChecksOutAndAdminCompletesRealCodWorkflow()
    {
        await using var app = new JourneyApplication();
        using var admin = app.Client();
        using var buyer = app.Client();
        await app.SignInAsync(admin, "journey-admin@example.invalid", true);
        await app.SignInAsync(buyer, "journey-buyer@example.invalid", false);

        Redirect(await PostForm(admin, "/Admin/Categories/Create", "/Admin/Categories/Create",
            ("Name", "Danh mục hành trình"), ("IsActive", "true")));
        var categoryId = await app.ReadAsync(db => db.Categories.Select(x => x.Id).SingleAsync());
        Redirect(await PostForm(admin, "/Admin/Products/Create", "/Admin/Products/Create",
            ("Name", "Nông sản hành trình"), ("Description", "Mô tả lưu từ biểu mẫu Admin"),
            ("Unit", "hộp"), ("CategoryId", categoryId.ToString()), ("Price", "12500.50"),
            ("Stock", "5"), ("IsActive", "true"), ("IsFeatured", "true")));
        var productId = await app.ReadAsync(db => db.Products.Select(x => x.Id).SingleAsync());
        var localNow = DateTime.UtcNow.AddHours(7);
        Redirect(await PostForm(admin, "/Admin/Coupons/Create", "/Admin/Coupons/Create",
            ("Code", "JOURNEY10"), ("Kind", "Percentage"), ("Value", "10"),
            ("MinimumSubtotal", "0"), ("MaxUses", "10"), ("IsActive", "true"),
            ("StartsAtLocal", localNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)),
            ("EndsAtLocal", localNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture))));
        Assert.Contains("Nông sản hành trình", Decode(await buyer.GetStringAsync("/Catalog")));

        Redirect(await PostForm(buyer, "/Addresses/Create", "/Addresses/Create",
            ("RecipientName", "Người nhận hành trình"), ("PhoneNumber", "0901234567"),
            ("Province", "Thành phố hiện hành"), ("Ward", "Phường hiện hành"),
            ("AddressLine", "12 Đường kiểm thử"), ("DeliveryNote", "Giao trong giờ hành chính")));
        var addressId = await app.ReadAsync(db => db.ShippingAddresses.Select(x => x.Id).SingleAsync());
        Redirect(await PostForm(buyer, "/Cart", "/Cart/Add", ("ProductId", productId.ToString()), ("Quantity", "2")));
        var checkout = await buyer.GetStringAsync("/Checkout?couponCode=JOURNEY10");
        var fields = new[]
        {
            ("__RequestVerificationToken", Input(checkout, "__RequestVerificationToken")),
            ("Input.CheckoutToken", Input(checkout, "Input.CheckoutToken")),
            ("Input.CouponCode", "JOURNEY10"), ("Input.AddressId", addressId.ToString()),
            // Neither browser amounts nor posted ownership may override server values.
            ("Total", "1"), ("UserId", "another-account"), ("PaymentStatus", "Paid")
        };
        var placed = await buyer.PostAsync("/Checkout/PlaceOrder", Form(fields));
        Redirect(placed);
        var orderId = await app.ReadAsync(db => db.Orders.Select(x => x.Id).SingleAsync());
        Assert.EndsWith(orderId.ToString(), placed.Headers.Location!.OriginalString);
        Redirect(await buyer.PostAsync("/Checkout/PlaceOrder", Form(fields)));
        await app.ReadAsync(async db =>
        {
            var order = await db.Orders.Include(x => x.Items).SingleAsync();
            Assert.Equal(25001.00m, order.Subtotal);
            Assert.Equal(2500.10m, order.Discount);
            Assert.Equal(52500.90m, order.Total);
            Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
            Assert.Equal(2, Assert.Single(order.Items).Quantity);
            Assert.Equal(3, await db.Products.Select(x => x.Stock).SingleAsync());
            Assert.Empty(await db.CartItems.ToListAsync());
            Assert.Equal(1, await db.Coupons.Select(x => x.UsedCount).SingleAsync());
            Assert.Single(await db.CouponUsages.ToListAsync());
            return true;
        });

        // Modify the actual address/product later through their real forms.
        var editAddress = await buyer.GetStringAsync($"/Addresses/Edit/{addressId}");
        Redirect(await buyer.PostAsync($"/Addresses/Edit/{addressId}", Form(
            ("__RequestVerificationToken", Input(editAddress, "__RequestVerificationToken")),
            ("VersionToken", Input(editAddress, "VersionToken")),
            ("RecipientName", "Người nhận đã đổi"), ("PhoneNumber", "0901234567"),
            ("Province", "Thành phố đã đổi"), ("Ward", "Phường đã đổi"), ("AddressLine", "99 Đường mới"))));
        var editProduct = await admin.GetStringAsync($"/Admin/Products/Edit/{productId}");
        Redirect(await admin.PostAsync($"/Admin/Products/Edit/{productId}", Form(
            ("__RequestVerificationToken", Input(editProduct, "__RequestVerificationToken")),
            ("Version", Input(editProduct, "Version")), ("Name", "Tên sản phẩm đã đổi"),
            ("Description", "Mô tả mới"), ("Unit", "gói"), ("CategoryId", categoryId.ToString()),
            ("Price", "99000"), ("Stock", "3"), ("IsActive", "true"))));

        foreach (var target in new[] { OrderStatus.Confirmed, OrderStatus.Shipping, OrderStatus.Completed })
        {
            var details = await admin.GetStringAsync($"/Admin/Orders/Details/{orderId}");
            var actionForm = Regex.Matches(details, "<form\\b[\\s\\S]*?</form>")
                .Cast<Match>().Single(match => match.Value.Contains($"name=\"target\" value=\"{target}\"", StringComparison.Ordinal)).Value;
            Redirect(await admin.PostAsync($"/Admin/Orders/Change/{orderId}", Form(
                ("__RequestVerificationToken", Input(actionForm, "__RequestVerificationToken")),
                ("confirmation", Input(actionForm, "confirmation")), ("target", target.ToString()), ("confirmed", "true"))));
        }
        var finalDetails = Decode(await buyer.GetStringAsync($"/Orders/Details/{orderId}"));
        Assert.Contains("Người nhận hành trình", finalDetails);
        Assert.Contains("Nông sản hành trình", finalDetails);
        Assert.Contains("12 Đường kiểm thử", finalDetails);
        Assert.DoesNotContain("Tên sản phẩm đã đổi", finalDetails);
        Assert.DoesNotContain("99 Đường mới", finalDetails);
        await app.ReadAsync(async db =>
        {
            var order = await db.Orders.SingleAsync();
            Assert.Equal(OrderStatus.Completed, order.Status);
            Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
            Assert.NotNull(order.CompletedAtUtc);
            return true;
        });
        var reports = Decode(await admin.GetStringAsync("/Admin/Reports"));
        Assert.Contains("52.500,90", reports);
        Assert.Contains("Nông sản hành trình", reports);
        Assert.DoesNotContain("another-account", reports);
    }

    private static async Task<HttpResponseMessage> PostForm(HttpClient client, string get, string post,
        params (string, string)[] fields)
    {
        var html = await client.GetStringAsync(get);
        return await client.PostAsync(post, Form(fields.Append(("__RequestVerificationToken", Input(html, "__RequestVerificationToken"))).ToArray()));
    }

    private static string Decode(string html) => WebUtility.HtmlDecode(html);
    private static string Input(string html, string name)
    {
        var input = Regex.Matches(html, "<input\\b[^>]*>").Cast<Match>()
            .First(match => Regex.IsMatch(match.Value, $"name=\"{Regex.Escape(name)}\""));
        return Decode(Regex.Match(input.Value, "value=\"([^\"]*)\"").Groups[1].Value);
    }
    private static FormUrlEncodedContent Form(params (string, string)[] fields) =>
        new(fields.Select(field => new KeyValuePair<string, string>(field.Item1, field.Item2)));
    private static void Redirect(HttpResponseMessage response) => Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

    private sealed class JourneyApplication : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly string environment;
        private readonly bool failEmail;
        public readonly CapturedLogs Logs = new();
        public JourneyApplication(string environment = "Development", bool failEmail = false)
        {
            this.environment = environment;
            this.failEmail = failEmail;
            connection.Open();
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            db.Database.EnsureCreated();
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Isolated test placeholder replaced before resolution");
            builder.UseSetting("App:PublicBaseUrl", "https://localhost");
            builder.ConfigureLogging(logging => { logging.ClearProviders(); logging.AddProvider(Logs); });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
                if (failEmail)
                {
                    services.RemoveAll<IEmailService>();
                    services.AddSingleton<IEmailService, FailingEmail>();
                }
            });
        }
        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        public async Task<T> ReadAsync<T>(Func<ApplicationDbContext, Task<T>> action)
        {
            using var scope = Services.CreateScope();
            return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }
        public async Task SignInAsync(HttpClient client, string email, bool admin)
        {
            var password = Guid.NewGuid().ToString("N") + "!aA1";
            using (var scope = Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser { UserName = email, Email = email, FullName = "Isolated journey" };
                Assert.True((await users.CreateAsync(user, password)).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, admin ? "Admin" : "Customer")).Succeeded);
            }
            Redirect(await PostForm(client, "/Account/Login", "/Account/Login", ("Email", email), ("Password", password)));
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FailingEmail : IEmailService
    {
        public Task SendEmailAsync(string toEmail, string subject, string htmlMessage) =>
            throw new InvalidOperationException("SMTP_TEST_SECRET RESET_TEST_TOKEN");
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Messages = new();
        public ILogger CreateLogger(string categoryName) => new Capture(Messages);
        public void Dispose() { }
        private sealed class Capture(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception) + exception?.ToString());
        }
    }
}
