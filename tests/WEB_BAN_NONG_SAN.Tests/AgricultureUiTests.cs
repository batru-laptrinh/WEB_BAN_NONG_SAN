using System.Net;
using System.Buffers.Binary;
using System.Security.Cryptography;
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
using Xunit;

namespace WEB_BAN_NONG_SAN.Tests;

// Exercise the redesigned shopping controls through the real MVC/cookie/CSRF
// pipeline. Only the database is substituted; no Windows configuration is read.
public sealed class AgricultureUiTests
{
    [Fact]
    public async Task OfficialLogoAndFaviconsRenderAndServeTheVerifiedOriginalAsset()
    {
        await using var app = new UiApplication();
        using var client = app.Client();
        var html = await client.GetStringAsync("/");
        var logo = Regex.Matches(html, "<img\\b[^>]*>").Cast<Match>()
            .First(match => match.Value.Contains("class=\"brand-logo\"", StringComparison.Ordinal)).Value;
        var logoUrl = Attribute(logo, "src");
        Assert.StartsWith("/images/logo-nong-san.png", logoUrl);
        using var logoResponse = await client.GetAsync(logoUrl);
        Assert.Equal(HttpStatusCode.OK, logoResponse.StatusCode);
        Assert.Equal("image/png", logoResponse.Content.Headers.ContentType?.MediaType);
        var original = await logoResponse.Content.ReadAsByteArrayAsync();
        // Fingerprint of NONG_SAN.png extracted directly from the user's ZIP.
        Assert.Equal("99065c6479ac8e76e23c650b7627d50ea446b90aadff1f4c660824359fda2384",
            Convert.ToHexStringLower(SHA256.HashData(original)));

        var icons = Regex.Matches(html, "<link\\b[^>]*>").Cast<Match>()
            .Where(match => Attribute(match.Value, "rel") == "icon").Select(match => match.Value).ToArray();
        var pngIcon = Assert.Single(icons, icon => Attribute(icon, "type") == "image/png");
        var png = await client.GetByteArrayAsync(Attribute(pngIcon, "href"));
        Assert.Equal(32u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(32u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4)));
        var icoIcon = Assert.Single(icons, icon => Attribute(icon, "type") == "image/x-icon");
        var ico = await client.GetByteArrayAsync(Attribute(icoIcon, "href"));
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, ico[..4]);
        Assert.Equal(6, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4, 2)));
    }

    [Fact]
    public async Task AdminSidebarUsesTheSameOfficialLogoAndKeepsItsManagementNavigation()
    {
        await using var app = new UiApplication();
        using var client = app.Client();
        await app.SignInAsync(client, admin: true);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Admin/Dashboard"));
        Assert.Contains("brand-identity-admin", html);
        Assert.Contains("/images/logo-nong-san.png", html);
        Assert.Contains("KHU QUẢN TRỊ", html);
        Assert.Contains("href=\"/Admin/Users\"", html);
        Assert.Contains("href=\"/Admin/Products\"", html);
        Assert.Contains("/images/favicon-nong-san-32.png", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
    }

    [Fact]
    public async Task HomeQuickAddSubmitsItsRenderedFieldsAndChangesOnlyTheSignedInBuyersCart()
    {
        await using var app = new UiApplication();
        var seed = await app.SeedAsync();
        using var client = app.Client();
        var userId = await app.SignInAsync(client);
        var form = QuickAddForm(await client.GetStringAsync("/"), seed.Available);

        var response = await client.PostAsync(Action(form), new FormUrlEncodedContent(Inputs(form)));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/Cart", response.Headers.Location!.OriginalString);
        await app.ReadAsync(async db =>
        {
            var line = Assert.Single(await db.CartItems.ToListAsync());
            Assert.Equal(userId, line.UserId);
            Assert.Equal(seed.Available, line.ProductId);
            Assert.Equal(1, line.Quantity);
            Assert.Equal(7, await db.Products.Where(product => product.Id == seed.Available).Select(product => product.Stock).SingleAsync());
            Assert.Empty(await db.Orders.ToListAsync());
        });
    }

    [Fact]
    public async Task HomeQuickAddWithoutCsrfIsRejectedWithoutAddingAnItem()
    {
        await using var app = new UiApplication();
        var seed = await app.SeedAsync();
        using var client = app.Client();
        await app.SignInAsync(client);
        var form = QuickAddForm(await client.GetStringAsync("/"), seed.Available);
        var fields = Inputs(form).Where(field => field.Key != "__RequestVerificationToken");

        var response = await client.PostAsync(Action(form), new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await app.ReadAsync(async db => Assert.Empty(await db.CartItems.ToListAsync()));
    }

    [Fact]
    public async Task GuestShoppingLinkReturnsFromLoginToAReadableProductBeforePurchase()
    {
        await using var app = new UiApplication();
        var seed = await app.SeedAsync();
        using var client = app.Client();
        var home = await client.GetStringAsync("/");
        var card = Regex.Matches(home, "<article\\b[^>]*data-product-id=\"" + seed.Available + "\"[\\s\\S]*?</article>")
            .Cast<Match>().First().Value;
        var loginLink = Regex.Matches(card, "\\bhref=\"([^\"]*)\"").Cast<Match>()
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
            .First(link => link.StartsWith("/Account/Login", StringComparison.Ordinal));
        var loginHtml = await client.GetStringAsync(loginLink);
        var loginForm = Forms(loginHtml).First(form => Action(form).StartsWith("/Account/Login", StringComparison.Ordinal));
        var identity = await app.CreateUserAsync();
        var fields = Inputs(loginForm);
        fields["Email"] = identity.Email;
        fields["Password"] = identity.Password;
        fields["RememberMe"] = "false";

        var response = await client.PostAsync(Action(loginForm), new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Catalog/Details/{seed.Available}", response.Headers.Location!.OriginalString);
        var details = await client.GetStringAsync(response.Headers.Location);
        var purchase = QuickAddForm(details, seed.Available);
        Assert.True(Inputs(purchase).ContainsKey("__RequestVerificationToken"));
        Assert.Equal("1", Inputs(purchase)["quantity"]);
        await app.ReadAsync(async db => Assert.Empty(await db.CartItems.ToListAsync()));
    }

    [Fact]
    public async Task HeaderSearchSubmitsToTheRealCatalogAndFiltersActiveDatabaseProducts()
    {
        await using var app = new UiApplication();
        var seed = await app.SeedAsync();
        using var client = app.Client();
        var home = await client.GetStringAsync("/");
        var form = Forms(home).First(candidate => Regex.IsMatch(candidate, "method=\"get\"", RegexOptions.IgnoreCase)
            && Action(candidate).StartsWith("/Catalog", StringComparison.Ordinal)
            && Regex.IsMatch(candidate, "<input\\b[^>]*name=\"q\""));
        var action = Action(form);

        var result = await client.GetStringAsync(action + "?q=" + Uri.EscapeDataString("Rau bina"));

        Assert.Contains($"data-product-id=\"{seed.Available}\"", result);
        Assert.DoesNotContain($"data-product-id=\"{seed.Other}\"", result);
        Assert.DoesNotContain($"data-product-id=\"{seed.Hidden}\"", result);
        Assert.DoesNotContain("href=\"#\"", home);
    }

    private static IEnumerable<string> Forms(string html) => Regex.Matches(html, "<form\\b[\\s\\S]*?</form>")
        .Cast<Match>().Select(match => match.Value);

    private static string QuickAddForm(string html, int productId) => Forms(html).First(form =>
        Action(form) == "/Cart/Add" && Inputs(form).Any(input => input.Key.Equals("productId", StringComparison.OrdinalIgnoreCase)
            && input.Value == productId.ToString()));

    private static string Action(string form) => WebUtility.HtmlDecode(Regex.Match(form, "\\baction=\"([^\"]*)\"").Groups[1].Value);

    private static string Attribute(string element, string name) =>
        WebUtility.HtmlDecode(Regex.Match(element, "\\b" + Regex.Escape(name) + "=\"([^\"]*)\"").Groups[1].Value);

    private static Dictionary<string, string> Inputs(string form) => Regex.Matches(form, "<input\\b[^>]*>")
        .Cast<Match>().Where(match => Regex.IsMatch(match.Value, "\\bname=\""))
        .GroupBy(match => WebUtility.HtmlDecode(Regex.Match(match.Value, "\\bname=\"([^\"]*)\"").Groups[1].Value))
        .ToDictionary(group => group.Key,
            group => WebUtility.HtmlDecode(Regex.Match(group.First().Value, "\\bvalue=\"([^\"]*)\"").Groups[1].Value));

    private sealed class UiApplication : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");

        public UiApplication()
        {
            connection.Open();
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            db.Database.EnsureCreated();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Isolated UI test placeholder replaced by SQLite");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            });
        }

        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });

        public async Task ReadAsync(Func<ApplicationDbContext, Task> action)
        {
            using var scope = Services.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }

        public async Task<(int Available, int Other, int Hidden)> SeedAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var category = new Category { Name = "Rau củ thử nghiệm cô lập" };
            var product = new Product { Category = category, Name = "Rau bina", Description = "Chỉ dùng để kiểm thử giao diện.", Price = 18000.50m, Stock = 7, IsFeatured = true };
            var other = new Product { Category = category, Name = "Bí đỏ", Description = "Chỉ dùng để kiểm thử giao diện.", Price = 25000, Stock = 4 };
            var hidden = new Product { Category = category, Name = "Rau bina đang ẩn", Description = "Không hiển thị công khai.", Price = 25000, Stock = 4, IsActive = false };
            db.AddRange(product, other, hidden);
            await db.SaveChangesAsync();
            return (product.Id, other.Id, hidden.Id);
        }

        public async Task<(string Id, string Email, string Password)> CreateUserAsync(bool admin = false)
        {
            var password = Guid.NewGuid().ToString("N") + "!aA1";
            const string email = "ui-buyer@example.invalid";
            string id;
            using (var scope = Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser { UserName = email, Email = email, FullName = "Khách kiểm thử cô lập" };
                Assert.True((await users.CreateAsync(user, password)).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, "Customer")).Succeeded);
                if (admin) Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
                id = user.Id;
            }
            return (id, email, password);
        }

        public async Task<string> SignInAsync(HttpClient client, bool admin = false)
        {
            var identity = await CreateUserAsync(admin);
            var login = await client.GetStringAsync("/Account/Login");
            var token = Inputs(Forms(login).First(form => Action(form).StartsWith("/Account/Login", StringComparison.Ordinal)))["__RequestVerificationToken"];
            var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["Email"] = identity.Email, ["Password"] = identity.Password, ["__RequestVerificationToken"] = token }));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            return identity.Id;
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
