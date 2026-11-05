using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WEB_BAN_NONG_SAN.Areas.Admin.Controllers;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.ViewModels;
using Xunit;

namespace WEB_BAN_NONG_SAN.Tests;

public sealed class PromotionsReportsTests
{
    [Theory]
    [InlineData("/Admin/Coupons")]
    [InlineData("/Admin/Coupons/Create")]
    [InlineData("/Admin/Reports")]
    public async Task AnonymousAndCustomerCannotReadBusinessAdministration(string path)
    {
        await using var app = new CommerceApplication();
        using var guest = app.Client();
        AssertRedirect(await guest.GetAsync(path), "/Account/Login");
        using var customer = await app.SignedInAsync(admin: false);
        AssertRedirect(await customer.GetAsync(path), "/Account/AccessDenied");
    }

    [Fact]
    public async Task CreateCanonicalizesCodeAndNeverTrustsPostedUsageCounters()
    {
        await using var app = new CommerceApplication();
        using var admin = await app.SignedInAsync(admin: true);
        var fields = CouponFields();
        fields["Code"] = " harvest_10 ";
        fields["UsedCount"] = "999";
        var response = await PostAsync(admin, "/Admin/Coupons/Create", fields);
        AssertRedirect(response, "/Admin/Coupons");
        using var scope = app.Services.CreateScope();
        var coupon = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Coupons.SingleAsync();
        Assert.Equal("HARVEST_10", coupon.Code);
        Assert.Equal(0, coupon.UsedCount);
        Assert.Equal(10m, coupon.Value);
        Assert.Equal(32, coupon.Version.Length);
    }

    [Theory]
    [InlineData("Value", "101")]
    [InlineData("Value", "0.001")]
    [InlineData("Value", "-1")]
    [InlineData("MaxUses", "0")]
    [InlineData("EndsAtLocal", "2020-01-01T00:00")]
    [InlineData("Code", "ưuđãi")]
    public async Task InvalidCouponConditionsAreRejectedByServer(string name, string value)
    {
        await using var app = new CommerceApplication();
        using var admin = await app.SignedInAsync(admin: true);
        var fields = CouponFields();
        fields[name] = value;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(admin, "/Admin/Coupons/Create", fields)).StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Coupons.ToListAsync());
    }

    [Fact]
    public async Task UsedCouponConditionsAreImmutableButAllowanceAndActiveFlagCanChange()
    {
        await using var app = new CommerceApplication();
        using var admin = await app.SignedInAsync(admin: true);
        var coupon = await app.SeedCouponAsync(used: 1);
        var fields = CouponFields(coupon);
        fields["Value"] = "15";
        var rejection = await PostAsync(admin, $"/Admin/Coupons/Edit/{coupon.Id}", fields);
        Assert.Equal(HttpStatusCode.OK, rejection.StatusCode);
        Assert.Contains("chỉ được thay đổi", WebUtility.HtmlDecode(await rejection.Content.ReadAsStringAsync()));
        using (var scope = app.Services.CreateScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Coupons.SingleAsync();
            Assert.Equal(coupon.Value, saved.Value);
            Assert.Equal(coupon.Version, saved.Version);
        }

        fields = CouponFields(coupon);
        fields["MaxUses"] = "20";
        fields["IsActive"] = "false";
        AssertRedirect(await PostAsync(admin, $"/Admin/Coupons/Edit/{coupon.Id}", fields), "/Admin/Coupons");
        using (var scope = app.Services.CreateScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Coupons.SingleAsync();
            Assert.Equal(20, saved.MaxUses);
            Assert.False(saved.IsActive);
            Assert.Equal(1, saved.UsedCount);
            Assert.Equal(coupon.Value, saved.Value);
            Assert.NotEqual(coupon.Version, saved.Version);
        }
    }

    [Fact]
    public async Task StaleDeactivateDoesNotOverwriteNewerCouponAndValidDeactivateDoesNotDeleteHistory()
    {
        await using var app = new CommerceApplication();
        using var admin = await app.SignedInAsync(admin: true);
        var coupon = await app.SeedCouponAsync(used: 1);
        var tokenPage = $"/Admin/Coupons/Edit/{coupon.Id}";
        var token = Token(await admin.GetStringAsync(tokenPage));
        var stale = await admin.PostAsync($"/Admin/Coupons/Deactivate/{coupon.Id}", Form(new()
        {
            ["__RequestVerificationToken"] = token, ["version"] = Guid.NewGuid().ToString("N")
        }));
        AssertRedirect(stale, "/Admin/Coupons");
        using (var scope = app.Services.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Coupons.SingleAsync()).IsActive);
        var valid = await admin.PostAsync($"/Admin/Coupons/Deactivate/{coupon.Id}", Form(new()
        {
            ["__RequestVerificationToken"] = token, ["version"] = coupon.Version
        }));
        AssertRedirect(valid, "/Admin/Coupons");
        using (var scope = app.Services.CreateScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Coupons.SingleAsync();
            Assert.False(saved.IsActive);
            Assert.Equal(1, saved.UsedCount);
        }
    }

    [Fact]
    public async Task CouponMutationWithoutAntiforgeryIsRejected()
    {
        await using var app = new CommerceApplication();
        using var admin = await app.SignedInAsync(admin: true);
        var coupon = await app.SeedCouponAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsync("/Admin/Coupons/Create", Form(CouponFields()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsync($"/Admin/Coupons/Deactivate/{coupon.Id}", Form(new() { ["version"] = coupon.Version }))).StatusCode);
    }

    [Fact]
    public async Task ReportsUseExactPaidCompletedTotalsAndHistoricalProductSnapshots()
    {
        await using var app = new CommerceApplication();
        using var admin = await app.SignedInAsync(admin: true);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync();
        var category = new Category { Name = "Isolated report category" };
        var product = new Product { Category = category, Name = "Current catalog name", Description = "Test", Unit = "kg", Price = 100.05m, Stock = 10 };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        db.Orders.AddRange(
            TestOrder(user.Id, product.Id, OrderStatus.Completed, PaymentStatus.Paid, 200.10m, 20m, 10m, now, "Historical avocado", 2),
            TestOrder(user.Id, product.Id, OrderStatus.Completed, PaymentStatus.Unpaid, 500m, 0m, 0m, now, "Unpaid snapshot"),
            TestOrder(user.Id, product.Id, OrderStatus.Cancelled, PaymentStatus.Paid, 700m, 0m, 0m, now, "Cancelled snapshot"),
            TestOrder(user.Id, product.Id, OrderStatus.Pending, PaymentStatus.Unpaid, 30m, 0m, 0m, now, "Pending snapshot"),
            TestOrder(user.Id, product.Id, OrderStatus.Completed, PaymentStatus.Paid, 9.91m, 0m, 0m, now.AddMonths(-13), "Archived snapshot"));
        await db.SaveChangesAsync();
        var controller = new ReportsController(db, new UpperInvariantLookupNormalizer(), NullLogger<ReportsController>.Instance);

        var view = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));
        var report = Assert.IsType<ReportsViewModel>(view.Model);

        Assert.True(report.IsDataAvailable);
        Assert.Equal(1, report.ProductCount);
        Assert.Equal(5, report.OrderCount);
        Assert.Equal(1, report.PendingOrderCount);
        Assert.Equal(3, report.CompletedOrderCount);
        Assert.Equal(200.01m, report.Revenue);
        Assert.Equal(190.10m, report.DailyRevenue.Sum(x => x.Revenue));
        Assert.Equal(190.10m, report.MonthlyRevenue.Sum(x => x.Revenue));
        Assert.Equal(30, report.DailyRevenue.Count);
        Assert.Equal(12, report.MonthlyRevenue.Count);
        Assert.Contains(report.TopProducts, x => x.ProductName == "Historical avocado" && x.Quantity == 2 && x.GrossValue == 200.10m);
        Assert.DoesNotContain(report.TopProducts, x => x.ProductName.Contains("Current") || x.ProductName.Contains("Unpaid") || x.ProductName.Contains("Cancelled"));
        var response = await admin.GetAsync("/Admin/Reports");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task EmptyReportsShowRealEmptyStateAndDatabaseFailureDoesNotShowFalseNumbers()
    {
        await using var app = new CommerceApplication();
        using var admin = await app.SignedInAsync(admin: true);
        var empty = WebUtility.HtmlDecode(await admin.GetStringAsync("/Admin/Reports"));
        Assert.Contains("Chưa có dữ liệu bán hàng", empty);
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE \"Orders\";");
        var failed = WebUtility.HtmlDecode(await admin.GetStringAsync("/Admin/Reports"));
        Assert.Contains("Chưa tải được báo cáo", failed);
        Assert.DoesNotContain("SQLite", failed);
        Assert.DoesNotContain("report-revenue", failed);
    }

    private static Order TestOrder(string userId, int productId, OrderStatus status, PaymentStatus payment,
        decimal subtotal, decimal discount, decimal shipping, DateTime completed, string name, int quantity = 1) => new()
    {
        UserId = userId, CheckoutKey = Guid.NewGuid(), Status = status, PaymentStatus = payment,
        CreatedAtUtc = completed.AddHours(-1), CompletedAtUtc = status == OrderStatus.Completed ? completed : null,
        Subtotal = subtotal, Discount = discount, ShippingFee = shipping, Total = subtotal - discount + shipping,
        RecipientName = "Isolated recipient", PhoneNumber = "0901234567", Province = "Hồ Chí Minh", Ward = "Phường test", AddressLine = "Test address",
        Items = [new OrderItem { ProductId = productId, ProductName = name, Unit = "kg", Quantity = quantity, UnitPrice = subtotal / quantity, LineTotal = subtotal }]
    };

    private static Dictionary<string, string> CouponFields(Coupon? coupon = null) => new()
    {
        ["Code"] = coupon?.Code ?? "HARVEST10", ["Kind"] = ((int)(coupon?.Kind ?? CouponKind.Percentage)).ToString(),
        ["Value"] = (coupon?.Value ?? 10m).ToString(CultureInfo.InvariantCulture),
        ["MinimumSubtotal"] = (coupon?.MinimumSubtotal ?? 1000m).ToString(CultureInfo.InvariantCulture),
        ["StartsAtLocal"] = (coupon?.StartsAtUtc.AddHours(7) ?? DateTime.UtcNow.Date.AddHours(7)).ToString("O", CultureInfo.InvariantCulture),
        ["EndsAtLocal"] = (coupon?.EndsAtUtc.AddHours(7) ?? DateTime.UtcNow.Date.AddDays(7).AddHours(7)).ToString("O", CultureInfo.InvariantCulture),
        ["MaxUses"] = (coupon?.MaxUses ?? 10).ToString(), ["IsActive"] = (coupon?.IsActive ?? true).ToString(), ["Version"] = coupon?.Version ?? ""
    };

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Token(await client.GetStringAsync(path));
        return await client.PostAsync(path, Form(fields));
    }
    private static FormUrlEncodedContent Form(Dictionary<string, string> fields) => new(fields);
    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static void AssertRedirect(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var location = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location.AbsolutePath : response.Headers.Location.OriginalString.Split('?')[0];
        Assert.Equal(path, location);
    }

    private sealed class CommerceApplication : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly string password = $"{Guid.NewGuid():N}!aA1";
        public CommerceApplication()
        {
            connection.Open();
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            db.Database.EnsureCreated();
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Test-only replaced by SQLite");
            builder.ConfigureLogging(x => x.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(x => x.UseSqlite(connection));
            });
        }
        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        public async Task<HttpClient> SignedInAsync(bool admin)
        {
            var client = Client();
            var email = $"{Guid.NewGuid():N}@example.invalid";
            using (var scope = Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser { UserName = email, Email = email };
                Assert.True((await users.CreateAsync(user, password)).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, "Customer")).Succeeded);
                if (admin) Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
            }
            var token = Token(await client.GetStringAsync("/Account/Login"));
            AssertRedirect(await client.PostAsync("/Account/Login", Form(new()
            {
                ["__RequestVerificationToken"] = token, ["Email"] = email, ["Password"] = password
            })), "/");
            return client;
        }
        public async Task<Coupon> SeedCouponAsync(int used = 0)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var coupon = new Coupon { Code = "HARVEST10", Kind = CouponKind.Percentage, Value = 10m, MinimumSubtotal = 1000m,
                StartsAtUtc = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc), EndsAtUtc = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(7), DateTimeKind.Utc), MaxUses = 10, UsedCount = used };
            db.Coupons.Add(coupon);
            await db.SaveChangesAsync();
            return coupon;
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
