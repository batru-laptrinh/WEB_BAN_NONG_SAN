using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.Services;
using Xunit;

namespace WEB_BAN_NONG_SAN.Tests;

public sealed class CatalogAdminTests
{
    [Theory]
    [InlineData("/Admin/Categories")]
    [InlineData("/Admin/Categories/Create")]
    [InlineData("/Admin/Categories/Edit/1")]
    [InlineData("/Admin/Products")]
    [InlineData("/Admin/Products/Create")]
    [InlineData("/Admin/Products/Edit/1")]
    public async Task AnonymousRequiresLogin(string path)
    {
        await using var app = new CatalogApplication();
        using var client = app.Client();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location!.AbsolutePath);
    }

    [Theory]
    [InlineData("/Admin/Categories")]
    [InlineData("/Admin/Products")]
    public async Task CustomerCannotUseCatalogAdmin(string path)
    {
        await using var app = new CatalogApplication();
        using var client = await app.SignedInAsync(admin: false);
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location!.AbsolutePath);
    }

    [Theory]
    [InlineData("/Admin/Categories/Create")]
    [InlineData("/Admin/Products/Create")]
    public async Task CustomerCannotPostWithAValidCsrfToken(string path)
    {
        await using var app = new CatalogApplication();
        using var client = await app.SignedInAsync(admin: false);
        var token = Hidden(await client.GetStringAsync("/Profile"), "__RequestVerificationToken");
        var response = await client.PostAsync(path, Form(("Name", "Forbidden"), ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location!.AbsolutePath);
        Assert.Equal(0, await app.DbAsync(db => db.Categories.CountAsync()));
        Assert.Equal(0, await app.DbAsync(db => db.Products.CountAsync()));
    }

    [Theory]
    [InlineData("/Admin/Categories/Create")]
    [InlineData("/Admin/Categories/Edit/1")]
    [InlineData("/Admin/Products/Create")]
    [InlineData("/Admin/Products/Edit/1")]
    public async Task EveryMutationRejectsMissingCsrf(string path)
    {
        await using var app = new CatalogApplication();
        using var client = await app.SignedInAsync();
        var response = await client.PostAsync(path, new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await app.DbAsync(db => db.Products.CountAsync()));
    }

    [Fact]
    public async Task CategoryCanBeAddedEditedAndDeactivatedWithoutDeletingProducts()
    {
        await using var app = new CatalogApplication();
        using var client = await app.SignedInAsync();
        var csrf = Hidden(await client.GetStringAsync("/Admin/Categories/Create"), "__RequestVerificationToken");
        var created = await client.PostAsync("/Admin/Categories/Create", Form(("Name", "  Rau sạch  "),
            ("Description", "Rau theo mùa"), ("IsActive", "true"), ("__RequestVerificationToken", csrf)));
        Assert.True(created.StatusCode == HttpStatusCode.Redirect, await created.Content.ReadAsStringAsync());
        var category = await app.DbAsync(db => db.Categories.AsNoTracking().SingleAsync());
        Assert.Equal("Rau sạch", category.Name);
        var productId = await app.AddProductAsync(category.Id);
        var html = await client.GetStringAsync($"/Admin/Categories/Edit/{category.Id}");
        var edited = await client.PostAsync($"/Admin/Categories/Edit/{category.Id}", Form(("Name", "Rau hữu cơ"),
            ("IsActive", "false"), ("Version", Hidden(html, "Version")),
            ("__RequestVerificationToken", Hidden(html, "__RequestVerificationToken"))));
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        var saved = await app.DbAsync(db => db.Categories.AsNoTracking().SingleAsync());
        Assert.False(saved.IsActive);
        Assert.Equal("Rau hữu cơ", saved.Name);
        Assert.True(await app.DbAsync(db => db.Products.AnyAsync(x => x.Id == productId)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task InvalidCategoryCannotBeSaved(string name)
    {
        await using var app = new CatalogApplication();
        using var client = await app.SignedInAsync();
        var csrf = Hidden(await client.GetStringAsync("/Admin/Categories/Create"), "__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/Admin/Categories/Create",
            Form(("Name", name), ("__RequestVerificationToken", csrf)))).StatusCode);
        Assert.Equal(0, await app.DbAsync(db => db.Categories.CountAsync()));
    }

    [Fact]
    public async Task StaleCategoryFormCannotOverwriteNewerChange()
    {
        await using var app = new CatalogApplication();
        using var client = await app.SignedInAsync();
        var categoryId = await app.AddCategoryAsync();
        var html = await client.GetStringAsync($"/Admin/Categories/Edit/{categoryId}");
        await app.DbAsync(async db =>
        {
            var item = await db.Categories.SingleAsync(); item.Name = "Newer name";
            item.Version = Guid.NewGuid().ToString("N"); return await db.SaveChangesAsync();
        });
        await client.PostAsync($"/Admin/Categories/Edit/{categoryId}", Form(("Name", "Stale name"),
            ("Version", Hidden(html, "Version")), ("IsActive", "false"),
            ("__RequestVerificationToken", Hidden(html, "__RequestVerificationToken"))));
        Assert.Equal("Newer name", await app.DbAsync(db => db.Categories.Select(x => x.Name).SingleAsync()));
    }

    [Fact]
    public async Task ProductCreateEditAndStopSellingUseRealDataAndIgnoreSensitiveOverposting()
    {
        await using var app = new CatalogApplication();
        using var client = await app.SignedInAsync();
        var categoryId = await app.AddCategoryAsync();
        var created = await app.PostProductAsync(client, categoryId, overrides: [("ImagePath", "https://evil.invalid/image.svg"),
            ("Id", "12345"), ("Version", "fixed"), ("UserId", "other")]);
        Assert.True(created.StatusCode == HttpStatusCode.Redirect, await created.Content.ReadAsStringAsync());
        var product = await app.DbAsync(db => db.Products.AsNoTracking().SingleAsync());
        Assert.Equal("Bắp ngọt", product.Name);
        Assert.Equal(15000.50m, product.Price);
        Assert.Equal(4, product.Stock);
        Assert.Null(product.ImagePath);
        Assert.Equal(32, product.Version.Length);
        Assert.NotEqual(12345, product.Id);
        var html = await client.GetStringAsync($"/Admin/Products/Edit/{product.Id}");
        await client.PostAsync($"/Admin/Products/Edit/{product.Id}", Form(
            ("Name", "Bắp mới"), ("Description", "Mô tả cập nhật"), ("Unit", "gói"),
            ("CategoryId", categoryId.ToString()), ("Price", "20000"), ("Stock", "0"), ("IsActive", "false"),
            ("Version", Hidden(html, "Version")), ("__RequestVerificationToken", Hidden(html, "__RequestVerificationToken"))));
        var updated = await app.DbAsync(db => db.Products.AsNoTracking().SingleAsync());
        Assert.False(updated.IsActive);
        Assert.Equal(0, updated.Stock);
        Assert.Equal("Bắp mới", updated.Name);
        Assert.NotEqual(product.Version, updated.Version);
    }

    [Theory]
    [InlineData("Price", "-1")]
    [InlineData("Price", "1000000001")]
    [InlineData("Price", "1.123")]
    [InlineData("Stock", "-1")]
    [InlineData("Stock", "1000001")]
    [InlineData("Name", "")]
    [InlineData("Unit", "")]
    [InlineData("Description", "")]
    public async Task ProductRejectsInvalidFields(string key, string value)
    {
        await using var app = new CatalogApplication();
        using var client = await app.SignedInAsync();
        var categoryId = await app.AddCategoryAsync();
        Assert.Equal(HttpStatusCode.OK, (await app.PostProductAsync(client, categoryId, [(key, value)])).StatusCode);
        Assert.Equal(0, await app.DbAsync(db => db.Products.CountAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductRejectsMissingAndInactiveCategory(bool inactive)
    {
        await using var app = new CatalogApplication();
        using var client = await app.SignedInAsync();
        var categoryId = inactive ? await app.AddCategoryAsync(active: false) : 99999;
        Assert.Equal(HttpStatusCode.OK, (await app.PostProductAsync(client, categoryId)).StatusCode);
        Assert.Equal(0, await app.DbAsync(db => db.Products.CountAsync()));
    }

    [Fact]
    public async Task StockChangeAfterFormWasOpenedCannotBeOverwritten()
    {
        await using var app = new CatalogApplication();
        using var client = await app.SignedInAsync();
        var categoryId = await app.AddCategoryAsync();
        var id = await app.AddProductAsync(categoryId);
        var html = await client.GetStringAsync($"/Admin/Products/Edit/{id}");
        await app.DbAsync(async db =>
        {
            var item = await db.Products.SingleAsync(); item.Stock = 1;
            item.Version = Guid.NewGuid().ToString("N"); return await db.SaveChangesAsync();
        });
        await client.PostAsync($"/Admin/Products/Edit/{id}", Form(("Name", "Stale edit"),
            ("Description", "Old data"), ("Unit", "kg"), ("Price", "2"), ("Stock", "99"),
            ("IsActive", "true"), ("CategoryId", categoryId.ToString()), ("Version", Hidden(html, "Version")),
            ("__RequestVerificationToken", Hidden(html, "__RequestVerificationToken"))));
        Assert.Equal(1, await app.DbAsync(db => db.Products.Select(x => x.Stock).SingleAsync()));
    }

    [Fact]
    public async Task ListsSearchAndPageRealRowsAndHtmlEncodeNames()
    {
        await using var app = new CatalogApplication();
        using var client = await app.SignedInAsync();
        var categoryId = await app.AddCategoryAsync();
        for (var i = 0; i < 14; i++) await app.AddProductAsync(categoryId, $"Fruit {i:00}");
        await app.AddProductAsync(categoryId, "<script>bad()</script>");
        var first = await client.GetStringAsync("/Admin/Products?search=Fruit");
        var second = await client.GetStringAsync("/Admin/Products?search=Fruit&page=2");
        Assert.Equal(12, Regex.Matches(first, "data-product-id=").Count);
        Assert.Equal(2, Regex.Matches(second, "data-product-id=").Count);
        var all = await client.GetStringAsync("/Admin/Products?search=script");
        Assert.DoesNotContain("<script>bad()</script>", all);
        Assert.Contains("&lt;script&gt;", all);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Admin/Products/Edit/99999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Admin/Categories/Edit/99999")).StatusCode);
    }

    [Fact]
    public async Task CategoryListSearchAndPaginationUseStoredRows()
    {
        await using var app = new CatalogApplication();
        using var client = await app.SignedInAsync();
        await app.DbAsync(async db =>
        {
            for (var i = 0; i < 14; i++) db.Categories.Add(new Category { Name = $"Nhóm {i:00}" });
            db.Categories.Add(new Category { Name = "Khác" });
            return await db.SaveChangesAsync();
        });
        var first = await client.GetStringAsync("/Admin/Categories?search=" + Uri.EscapeDataString("Nhóm"));
        var last = await client.GetStringAsync("/Admin/Categories?page=999&search=" + Uri.EscapeDataString("Nhóm"));
        Assert.Equal(12, Regex.Matches(first, "data-category-id=").Count);
        Assert.Equal(2, Regex.Matches(last, "data-category-id=").Count);
        Assert.DoesNotContain("Khác", WebUtility.HtmlDecode(first));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Categories?page=-1")).StatusCode);
    }

    [Theory]
    [InlineData("/Admin/Categories")]
    [InlineData("/Admin/Products")]
    public async Task DatabaseFailuresShowOnlySafeUnavailableMessage(string path)
    {
        await using var app = new CatalogApplication();
        using var client = await app.SignedInAsync();
        await app.CloseDatabaseAsync();
        var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Chưa tải", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("SqliteException", html);
        Assert.DoesNotContain("no such table", html);
        Assert.DoesNotContain("Data Source", html);
    }

    [Fact]
    public async Task RealPngUploadIsRewrittenAndServedWithFixedSafeHeaders()
    {
        await using var app = new CatalogApplication();
        using var admin = await app.SignedInAsync();
        var categoryId = await app.AddCategoryAsync();
        var image = Png(metadata: "<script>private metadata</script>");
        var result = await app.PostProductAsync(admin, categoryId, image: image, fileName: "../../payload.php");
        Assert.True(result.StatusCode == HttpStatusCode.Redirect, await result.Content.ReadAsStringAsync());
        var product = await app.DbAsync(db => db.Products.AsNoTracking().SingleAsync());
        Assert.Matches("^/ProductImages/[0-9a-f]{32}\\.png$", product.ImagePath!);
        using var visitor = app.Client();
        var response = await visitor.GetAsync(product.ImagePath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.Private);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.DoesNotContain("private metadata", Encoding.ASCII.GetString(bytes));
        Assert.Single(Directory.GetFiles(app.ImageDirectory));
        await app.DbAsync(async db => { var item = await db.Products.SingleAsync(); item.IsActive = false; return await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(product.ImagePath)).StatusCode);
        var hiddenImage = await admin.GetAsync(product.ImagePath);
        Assert.Equal(HttpStatusCode.OK, hiddenImage.StatusCode);
        Assert.True(hiddenImage.Headers.CacheControl?.NoStore);
        Assert.True(hiddenImage.Headers.CacheControl?.Private);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(product.ImagePath)).StatusCode);
    }

    [Theory]
    [InlineData("fake")]
    [InlineData("trailing")]
    [InlineData("crc")]
    [InlineData("dimension")]
    [InlineData("filter")]
    [InlineData("short")]
    [InlineData("bomb")]
    [InlineData("oversize")]
    public async Task UnsafeUploadsDoNotCreateAProductOrFile(string kind)
    {
        await using var app = new CatalogApplication();
        using var client = await app.SignedInAsync();
        var categoryId = await app.AddCategoryAsync();
        var bytes = kind switch
        {
            "fake" => Encoding.UTF8.GetBytes("<svg onload='alert(1)' />"),
            "trailing" => Png().Concat(Encoding.UTF8.GetBytes("<?php bad(); ?>")).ToArray(),
            "crc" => BadCrc(),
            "dimension" => Png(width: 2049),
            "filter" => Png(scanlines: [9, 1, 2, 3]),
            "short" => Png(scanlines: [0, 1]),
            "bomb" => Png(scanlines: new byte[65536]),
            "oversize" => new byte[ProductImageService.MaximumBytes + 1],
            _ => throw new InvalidOperationException()
        };
        var response = await app.PostProductAsync(client, categoryId, image: bytes);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await app.DbAsync(db => db.Products.CountAsync()));
        Assert.False(Directory.Exists(app.ImageDirectory));
    }

    [Fact]
    public async Task ImageEndpointRejectsUnknownAndTraversalNames()
    {
        await using var app = new CatalogApplication();
        using var visitor = app.Client();
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/ProductImages/not-a-file.png")).StatusCode);
        var service = app.Services.GetRequiredService<ProductImageService>();
        Assert.Null(service.GetExistingPath("../appsettings.json"));
        Assert.Null(service.GetExistingPath(new string('a', 32) + ".php"));
    }

    [Fact]
    public async Task LostDatabaseAcknowledgementDoesNotDeleteAnImageReferencedByACommittedProduct()
    {
        await using var app = new CatalogApplication();
        using var admin = await app.SignedInAsync();
        var categoryId = await app.AddCategoryAsync();
        app.FailNextProductAcknowledgement();

        var response = await app.PostProductAsync(admin, categoryId, image: Png());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-test-lost-acknowledgement", html);
        Assert.Contains("kiểm tra danh sách trước khi thử lại", WebUtility.HtmlDecode(html));
        var committed = await app.DbAsync(db => db.Products.AsNoTracking().SingleAsync());
        Assert.NotNull(committed.ImagePath);
        Assert.Single(Directory.GetFiles(app.ImageDirectory));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(committed.ImagePath)).StatusCode);
    }

    private static byte[] BadCrc()
    {
        var bytes = Png(); bytes[29] ^= 0xff; return bytes;
    }

    private static byte[] Png(int width = 1, byte[]? scanlines = null, string? metadata = null)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), 1);
        header[8] = 8; header[9] = 2;
        Chunk(output, "IHDR", header);
        if (metadata != null) Chunk(output, "tEXt", Encoding.UTF8.GetBytes("Comment\0" + metadata));
        using var data = new MemoryStream();
        using (var compression = new ZLibStream(data, CompressionLevel.Optimal, leaveOpen: true))
            compression.Write(scanlines ?? [0, 1, 2, 3]);
        Chunk(output, "IDAT", data.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var word = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(word, data.Length); stream.Write(word);
        var content = Encoding.ASCII.GetBytes(type).Concat(data).ToArray(); stream.Write(content);
        var crc = uint.MaxValue;
        foreach (var item in content)
        {
            crc ^= item;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc); stream.Write(word);
    }

    private static string Hidden(string html, string name)
    {
        var input = Regex.Matches(html, "<input[^>]+>").Select(x => x.Value)
            .First(x => Regex.IsMatch(x, $"name=\"{Regex.Escape(name)}\""));
        return WebUtility.HtmlDecode(Regex.Match(input, "value=\"([^\"]*)\"").Groups[1].Value);
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] pairs) =>
        new(pairs.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));

    private sealed class CatalogApplication : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly string password = Guid.NewGuid().ToString("N") + "!aA1";
        private readonly string directory = Path.Combine(Path.GetTempPath(), "catalog-tests-" + Guid.NewGuid().ToString("N"));
        private readonly LostAcknowledgementInterceptor acknowledgement = new();
        public string ImageDirectory => Path.Combine(directory, "App_Data", "product-images");

        public CatalogApplication()
        {
            connection.Open();
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            db.Database.EnsureCreated();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Test-only connection replaced by SQLite");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(x => x.UseSqlite(connection).AddInterceptors(acknowledgement));
                services.RemoveAll<ProductImageService>();
                services.AddSingleton(new ProductImageService(new ImageEnvironment { ContentRootPath = directory }));
            });
        }

        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });

        public async Task<HttpClient> SignedInAsync(bool admin = true)
        {
            var client = Client();
            const string email = "catalog@example.invalid";
            using (var scope = Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser { UserName = email, Email = email };
                Assert.True((await users.CreateAsync(user, password)).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, admin ? "Admin" : "Customer")).Succeeded);
            }
            var html = await client.GetStringAsync("/Account/Login");
            var response = await client.PostAsync("/Account/Login", Form(("Email", email), ("Password", password),
                ("__RequestVerificationToken", Hidden(html, "__RequestVerificationToken"))));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            return client;
        }

        public async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
        {
            using var scope = Services.CreateScope();
            return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }

        public Task<int> AddCategoryAsync(bool active = true) => DbAsync(async db =>
        {
            var category = new Category { Name = "Rau củ", IsActive = active };
            db.Categories.Add(category); await db.SaveChangesAsync(); return category.Id;
        });

        public Task<int> AddProductAsync(int categoryId, string name = "Sản phẩm thử") => DbAsync(async db =>
        {
            var product = new Product { CategoryId = categoryId, Name = name, Description = "Mô tả", Unit = "kg", Price = 10000m, Stock = 10 };
            db.Products.Add(product); await db.SaveChangesAsync(); return product.Id;
        });

        public Task CloseDatabaseAsync() => connection.CloseAsync();
        public void FailNextProductAcknowledgement() => acknowledgement.FailNextProduct = true;

        public async Task<HttpResponseMessage> PostProductAsync(HttpClient client, int categoryId,
            (string Key, string Value)[]? overrides = null, byte[]? image = null, string fileName = "photo.png")
        {
            var html = await client.GetStringAsync("/Admin/Products/Create");
            var fields = new Dictionary<string, string>
            {
                ["Name"] = "Bắp ngọt", ["Description"] = "Bắp thu hoạch trong ngày", ["Unit"] = "kg",
                ["Price"] = "15000.50", ["Stock"] = "4", ["CategoryId"] = categoryId.ToString(),
                ["IsActive"] = "true", ["IsFeatured"] = "true", ["__RequestVerificationToken"] = Hidden(html, "__RequestVerificationToken")
            };
            foreach (var item in overrides ?? []) fields[item.Key] = item.Value;
            if (image == null) return await client.PostAsync("/Admin/Products/Create", new FormUrlEncodedContent(fields));
            var form = new MultipartFormDataContent();
            foreach (var item in fields) form.Add(new StringContent(item.Value), item.Key);
            var content = new ByteArrayContent(image);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(content, "Image", fileName);
            return await client.PostAsync("/Admin/Products/Create", form);
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync(); await connection.DisposeAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ImageEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ImageTests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class LostAcknowledgementInterceptor : SaveChangesInterceptor
    {
        public bool FailNextProduct { get; set; }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (FailNextProduct && eventData.Context!.ChangeTracker.Entries<Product>().Any())
            {
                FailNextProduct = false;
                // SavedChanges runs after EF's database transaction has committed.
                // This simulates a lost acknowledgement without touching real SQL.
                throw new IOException("private-test-lost-acknowledgement");
            }
            return ValueTask.FromResult(result);
        }
    }
}
