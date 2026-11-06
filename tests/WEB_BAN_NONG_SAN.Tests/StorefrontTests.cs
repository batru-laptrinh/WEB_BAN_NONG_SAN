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
using Xunit;

namespace WEB_BAN_NONG_SAN.Tests;

public sealed class StorefrontTests
{
    [Fact]
    public async Task EmptyShopUsesTruthfulEmptyStateAndRealNavigation()
    {
        await using var app = new StoreApplication();
        using var client = app.NewClient();
        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        var catalog = WebUtility.HtmlDecode(await client.GetStringAsync("/Catalog"));
        Assert.Contains("Cửa hàng đang chuẩn bị sản phẩm", home);
        Assert.Contains("0</strong> sản phẩm phù hợp", catalog);
        Assert.DoesNotContain("data-product-id", home);
        Assert.DoesNotContain("href=\"#\"", home);
        Assert.Contains("/Cart", home);
        Assert.Contains("/Addresses", home);
        Assert.Contains("/Orders", home);
        Assert.Contains("lang=\"vi\"", home);
    }

    [Fact]
    public async Task CatalogAndHomeExcludeInactiveProductsAndInactiveCategories()
    {
        await using var app = new StoreApplication();
        var seed = await app.SeedAsync();
        using var client = app.NewClient();
        foreach (var path in new[] { "/", "/Catalog" })
        {
            var html = await client.GetStringAsync(path);
            Assert.Contains($"data-product-id=\"{seed.Carrot}\"", html);
            Assert.Contains($"data-product-id=\"{seed.Cabbage}\"", html);
            Assert.DoesNotContain($"data-product-id=\"{seed.Inactive}\"", html);
            Assert.DoesNotContain($"data-product-id=\"{seed.HiddenCategory}\"", html);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Catalog/Details/{seed.Inactive}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Catalog/Details/{seed.HiddenCategory}")).StatusCode);
    }

    [Fact]
    public async Task NameCategoryAndPriceFiltersAreAppliedTogetherOnServer()
    {
        await using var app = new StoreApplication();
        var seed = await app.SeedAsync();
        using var client = app.NewClient();
        var html = await client.GetStringAsync($"/Catalog?q=CARROT&categoryId={seed.Vegetables}&minPrice=15000&maxPrice=25000");
        Assert.Equal(new[] { seed.Carrot }, Cards(html));
        Assert.Contains("value=\"CARROT\"", html);
        Assert.Empty(Cards(await client.GetStringAsync($"/Catalog?categoryId={seed.Vegetables}&maxPrice=1000")));
    }

    [Theory]
    [InlineData("price-asc", false)]
    [InlineData("price-desc", true)]
    public async Task SortingByPriceUsesActualDatabasePrices(string sort, bool descending)
    {
        await using var app = new StoreApplication();
        var seed = await app.SeedAsync();
        using var client = app.NewClient();
        var ids = Cards(await client.GetStringAsync($"/Catalog?sort={sort}"));
        Assert.Equal(descending ? new[] { seed.Apple, seed.Carrot, seed.Cabbage } : new[] { seed.Cabbage, seed.Carrot, seed.Apple }, ids);
    }

    [Fact]
    public async Task CatalogPaginatesAndPreservesSearchAndSort()
    {
        await using var app = new StoreApplication();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var category = new Category { Name = "Harvest" };
            db.Categories.Add(category);
            for (var i = 0; i < 15; i++) db.Products.Add(new Product { Category = category, Name = $"Harvest {i:D2}", Description = "Test product", Price = i + 100, Stock = 2 });
            await db.SaveChangesAsync();
        }
        using var client = app.NewClient();
        var first = await client.GetStringAsync("/Catalog?q=Harvest&sort=name");
        var second = await client.GetStringAsync("/Catalog?q=Harvest&sort=name&page=2");
        Assert.Equal(12, Cards(first).Length);
        Assert.Equal(3, Cards(second).Length);
        Assert.Empty(Cards(first).Intersect(Cards(second)));
        Assert.Contains("q=Harvest", first);
        Assert.Contains("sort=name", first);
        Assert.Contains("page=2", first);
        Assert.Contains("15</strong>", first);
    }

    [Fact]
    public async Task MalformedPriceRangeIsRejectedInsteadOfIgnored()
    {
        await using var app = new StoreApplication();
        await app.SeedAsync();
        using var client = app.NewClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Catalog?minPrice=90000&maxPrice=100"));
        Assert.Contains("Giá thấp nhất không được lớn hơn giá cao nhất.", html);
        Assert.Empty(Cards(html));
    }

    [Fact]
    public async Task OverlongSearchIsValidatedAndEscaped()
    {
        await using var app = new StoreApplication();
        using var client = app.NewClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Catalog?q=" + new string('x', 101)));
        Assert.Contains("Từ khóa tối đa 100 ký tự", html);
        Assert.Empty(Cards(html));
        var attack = await client.GetStringAsync("/Catalog?q=%3Cscript%3Ealert(1)%3C%2Fscript%3E");
        Assert.DoesNotContain("<script>alert(1)</script>", attack);
        Assert.Contains("&lt;script&gt;", attack);
    }

    [Fact]
    public async Task AvailableDetailsContainAntiforgeryPurchaseAndEscapedDescription()
    {
        await using var app = new StoreApplication();
        var seed = await app.SeedAsync();
        using var client = app.NewClient();
        await app.SignInAsync(client);
        var html = await client.GetStringAsync($"/Catalog/Details/{seed.Carrot}");
        Assert.Contains("/Cart/Add", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Contains("name=\"productId\"", html);
        Assert.Contains("name=\"quantity\"", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>unsafe()</script>", html);
        Assert.Contains("/images/agriculture-harvest.webp", html);
        Assert.Contains("Ảnh minh họa", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task FractionalPriceIsDisplayedWithoutRoundingToTheNextDong()
    {
        await using var app = new StoreApplication();
        var seed = await app.SeedAsync();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Products.SingleAsync(p => p.Id == seed.Carrot)).Price = 20000.50m;
            await db.SaveChangesAsync();
        }
        using var client = app.NewClient();
        var html = await client.GetStringAsync($"/Catalog/Details/{seed.Carrot}");
        Assert.Contains("20.000,5", html);
        Assert.DoesNotContain("20.001", html);
    }

    [Fact]
    public async Task OutOfStockDetailsRemainVisibleWithoutPurchaseForm()
    {
        await using var app = new StoreApplication();
        var seed = await app.SeedAsync();
        using var client = app.NewClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Catalog/Details/{seed.Cabbage}"));
        Assert.Contains("Sản phẩm tạm hết hàng", html);
        // Related products may be available and have their own quick-add forms.
        // The product being viewed must still offer no purchase controls.
        var mainProduct = Regex.Match(html, "<article class=\"store-product-detail\">[\\s\\S]*?</article>");
        Assert.True(mainProduct.Success, "Expected the main product details region.");
        Assert.DoesNotContain("/Cart/Add", mainProduct.Value);
        Assert.DoesNotContain("name=\"quantity\"", mainProduct.Value);
    }

    [Theory]
    [InlineData("/Catalog/Details/0")]
    [InlineData("/Catalog/Details/-10")]
    [InlineData("/Catalog/Details/999999")]
    public async Task MissingProductDoesNotExposeData(string path)
    {
        await using var app = new StoreApplication();
        using var client = app.NewClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task DatabaseFailureIsReportedWithoutSqlDetailsOrFalseProductCounts()
    {
        await using var app = new StoreApplication();
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE Products");
        using var client = app.NewClient();
        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        var catalog = WebUtility.HtmlDecode(await client.GetStringAsync("/Catalog"));
        Assert.Contains("Chưa thể tải cửa hàng", home);
        Assert.Contains("Chưa tải được sản phẩm", catalog);
        Assert.DoesNotContain("0</strong> sản phẩm phù hợp", catalog);
        Assert.DoesNotContain("SQLite", home + catalog);
        Assert.DoesNotContain("Exception", home + catalog);
        Assert.DoesNotContain("DROP TABLE", home + catalog);
    }

    private static int[] Cards(string html) => Regex.Matches(html, "data-product-id=\"([0-9]+)\"")
        .Select(m => int.Parse(m.Groups[1].Value)).ToArray();

    private sealed class StoreApplication : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public StoreApplication()
        {
            connection.Open();
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            db.Database.EnsureCreated();
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Test-only database replaced by SQLite");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            });
        }
        public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        public async Task SignInAsync(HttpClient client)
        {
            var password = Guid.NewGuid().ToString("N") + "!aA1";
            const string email = "storefront-buyer@example.invalid";
            using (var scope = Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser { UserName = email, Email = email, FullName = "Khách kiểm thử giao diện" };
                Assert.True((await users.CreateAsync(user, password)).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, "Customer")).Succeeded);
            }
            var login = await client.GetStringAsync("/Account/Login");
            var token = WebUtility.HtmlDecode(Regex.Match(login, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = token
            }));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        public async Task<(int Vegetables, int Carrot, int Cabbage, int Apple, int Inactive, int HiddenCategory)> SeedAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var vegetables = new Category { Name = "Vegetables" };
            var fruit = new Category { Name = "Fruit" };
            var hidden = new Category { Name = "Hidden category", IsActive = false };
            var carrot = new Product { Category = vegetables, Name = "Carrot", Description = "<script>unsafe()</script>", Price = 20000, Stock = 10, IsFeatured = true };
            var cabbage = new Product { Category = vegetables, Name = "Cabbage", Description = "Test", Price = 10000, Stock = 0 };
            var apple = new Product { Category = fruit, Name = "Apple", Description = "Test", Price = 50000, Stock = 3 };
            var inactive = new Product { Category = vegetables, Name = "Hidden product", Description = "Test", Price = 30000, Stock = 1, IsActive = false };
            var hiddenProduct = new Product { Category = hidden, Name = "Hidden by category", Description = "Test", Price = 40000, Stock = 1 };
            db.AddRange(carrot, cabbage, apple, inactive, hiddenProduct);
            await db.SaveChangesAsync();
            return (vegetables.Id, carrot.Id, cabbage.Id, apple.Id, inactive.Id, hiddenProduct.Id);
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
