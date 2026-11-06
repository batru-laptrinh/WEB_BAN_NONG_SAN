using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.Services;
using WEB_BAN_NONG_SAN.ViewModels;
using Xunit;

namespace WEB_BAN_NONG_SAN.Tests;

public sealed class AddressesTests
{
    [Theory]
    [InlineData("/Addresses")]
    [InlineData("/Addresses/Create")]
    [InlineData("/Addresses/Edit/1")]
    [InlineData("/Addresses/Delete/1")]
    public async Task AnonymousAddressPagesRedirectToLogin(string path)
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        AssertLogin(await client.GetAsync(path));
    }

    [Theory]
    [InlineData("/Addresses/Create")]
    [InlineData("/Addresses/Edit/1")]
    [InlineData("/Addresses/Delete/1")]
    [InlineData("/Addresses/SetDefault/1")]
    public async Task AnonymousAddressMutationsRedirectToLogin(string path)
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        AssertLogin(await client.PostAsync(path, Form()));
    }

    [Fact]
    public async Task ValidCreateUsesSignedInOwnerIgnoresPostedUserIdAndSetsFirstDefault()
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        var owner = await app.CreateUserAsync("buyer@example.invalid");
        var other = await app.CreateUserAsync("other@example.invalid");
        await app.LoginAsync(client, "buyer@example.invalid");

        var result = await client.PostAsync("/Addresses/Create", Form(ValidFields(
            Token(await client.GetStringAsync("/Addresses/Create"))).Append(("UserId", other)).ToArray()));

        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        var saved = Assert.Single(await app.ReadAddressesAsync());
        Assert.Equal(owner, saved.UserId);
        Assert.Equal("Người nhận", saved.RecipientName);
        Assert.Null(saved.District);
        Assert.True(saved.IsDefault);
        var html = Html(await client.GetStringAsync("/Addresses"));
        Assert.Contains("Đã thêm địa chỉ giao hàng", html);
        Assert.Contains("Mặc định", html);
        Assert.DoesNotContain("name=\"UserId\"", html);
    }

    [Theory]
    [InlineData("RecipientName", "")]
    [InlineData("RecipientName", "   ")]
    [InlineData("PhoneNumber", "")]
    [InlineData("PhoneNumber", "not-a-phone")]
    [InlineData("PhoneNumber", "1-------1")]
    [InlineData("Province", "")]
    [InlineData("Ward", "")]
    [InlineData("AddressLine", "")]
    [InlineData("District", "LONG")]
    [InlineData("AddressLine", "LONG")]
    public async Task InvalidInputIsRejectedOnServerAndDoesNotCreateAnyAddress(string field, string value)
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        await app.CreateUserAsync("buyer@example.invalid");
        await app.LoginAsync(client, "buyer@example.invalid");
        var fields = ValidFields(Token(await client.GetStringAsync("/Addresses/Create"))).ToDictionary(x => x.Item1, x => x.Item2);
        fields[field] = value == "LONG" ? new string('x', 501) : value;

        var response = await client.PostAsync("/Addresses/Create", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
        Assert.Empty(await app.ReadAddressesAsync());
    }

    [Fact]
    public async Task AddressBookShowsOnlyOwnerDataAndHtmlEncodesFreeText()
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        var owner = await app.CreateUserAsync("buyer@example.invalid");
        var other = await app.CreateUserAsync("other@example.invalid");
        await app.AddAddressAsync(owner, "<script>alert(1)</script>");
        await app.AddAddressAsync(other, "OTHER_PRIVATE_RECIPIENT");
        await app.LoginAsync(client, "buyer@example.invalid");

        var response = await client.GetAsync("/Addresses");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.DoesNotContain("OTHER_PRIVATE_RECIPIENT", html);
        Assert.DoesNotContain("PasswordHash", html);
        Assert.DoesNotContain("SecurityStamp", html);
    }

    [Fact]
    public async Task CreatingBeyondAddressLimitIsRejectedWithoutChangingDefaults()
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        var owner = await app.CreateUserAsync("buyer@example.invalid");
        await app.ChangeAsync(async db =>
        {
            db.ShippingAddresses.AddRange(Enumerable.Range(0, ShippingAddressService.MaximumAddressesPerUser)
                .Select(i => NewAddress(owner, $"Address {i}", i == 0)));
            await db.SaveChangesAsync();
        });
        await app.LoginAsync(client, "buyer@example.invalid");

        var result = await client.PostAsync("/Addresses/Create", Form(ValidFields(
            Token(await client.GetStringAsync("/Addresses/Create"))).Append(("IsDefault", "true")).ToArray()));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("tối đa 50 địa chỉ", Html(await result.Content.ReadAsStringAsync()));
        var addresses = await app.ReadAddressesAsync();
        Assert.Equal(ShippingAddressService.MaximumAddressesPerUser, addresses.Count);
        Assert.Equal("Address 0", Assert.Single(addresses, a => a.IsDefault).RecipientName);
    }

    [Theory]
    [InlineData("Edit")]
    [InlineData("Delete")]
    public async Task ChangingGetIdCannotRevealAnotherOwnersAddress(string action)
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        await app.CreateUserAsync("buyer@example.invalid");
        var other = await app.CreateUserAsync("other@example.invalid");
        var address = await app.AddAddressAsync(other, "PRIVATE_RECIPIENT");
        await app.LoginAsync(client, "buyer@example.invalid");

        var response = await client.GetAsync($"/Addresses/{action}/{address}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("PRIVATE_RECIPIENT", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Edit")]
    [InlineData("Delete")]
    [InlineData("SetDefault")]
    public async Task ChangingPostedIdCannotMutateAnotherOwnersAddress(string action)
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        await app.CreateUserAsync("buyer@example.invalid");
        var other = await app.CreateUserAsync("other@example.invalid");
        var address = await app.AddAddressAsync(other, "PRIVATE_RECIPIENT", true);
        await app.LoginAsync(client, "buyer@example.invalid");
        var token = Token(await client.GetStringAsync("/Addresses/Create"));

        var response = await client.PostAsync($"/Addresses/{action}/{address}", Form(ValidFields(token).ToArray()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var saved = Assert.Single(await app.ReadAddressesAsync());
        Assert.Equal("PRIVATE_RECIPIENT", saved.RecipientName);
        Assert.True(saved.IsDefault);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Edit")]
    [InlineData("Delete")]
    [InlineData("SetDefault")]
    public async Task EveryAddressMutationRequiresAntiforgeryToken(string action)
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        var owner = await app.CreateUserAsync("buyer@example.invalid");
        var id = await app.AddAddressAsync(owner, "Original", true);
        await app.LoginAsync(client, "buyer@example.invalid");

        var response = await client.PostAsync($"/Addresses/{action}/{id}", Form());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Original", Assert.Single(await app.ReadAddressesAsync()).RecipientName);
    }

    [Fact]
    public async Task EditingOnlyUpdatesRouteTargetAndKeepsExistingDefault()
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        var owner = await app.CreateUserAsync("buyer@example.invalid");
        var first = await app.AddAddressAsync(owner, "Original", true);
        var second = await app.AddAddressAsync(owner, "Untouched");
        await app.LoginAsync(client, "buyer@example.invalid");
        var edit = await client.GetStringAsync($"/Addresses/Edit/{first}");
        var fields = ValidFields(Token(edit)).Concat([
            ("VersionToken", Field(edit, "VersionToken")), ("id", second.ToString()), ("IsDefault", "false")]);

        var result = await client.PostAsync($"/Addresses/Edit/{first}", Form(fields.ToArray()));

        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        var rows = await app.ReadAddressesAsync();
        Assert.Equal("Người nhận", rows.Single(x => x.Id == first).RecipientName);
        Assert.True(rows.Single(x => x.Id == first).IsDefault);
        Assert.Equal("Untouched", rows.Single(x => x.Id == second).RecipientName);
    }

    [Fact]
    public async Task StaleEditTokenCannotOverwriteNewerAddressData()
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        var owner = await app.CreateUserAsync("buyer@example.invalid");
        var id = await app.AddAddressAsync(owner, "Original", true);
        await app.LoginAsync(client, "buyer@example.invalid");
        var edit = await client.GetStringAsync($"/Addresses/Edit/{id}");
        await app.ChangeAsync(async db =>
        {
            var row = await db.ShippingAddresses.SingleAsync();
            row.RecipientName = "Updated elsewhere";
            row.Version = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
        });

        var result = await client.PostAsync($"/Addresses/Edit/{id}", Form(ValidFields(Token(edit))
            .Append(("VersionToken", Field(edit, "VersionToken"))).ToArray()));

        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Contains("đã thay đổi", Html(await result.Content.ReadAsStringAsync()));
        Assert.Equal("Updated elsewhere", Assert.Single(await app.ReadAddressesAsync()).RecipientName);
    }

    [Fact]
    public async Task SetDefaultSwitchesOnlyCurrentOwnersDefault()
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        var owner = await app.CreateUserAsync("buyer@example.invalid");
        var other = await app.CreateUserAsync("other@example.invalid");
        await app.AddAddressAsync(owner, "First", true);
        var second = await app.AddAddressAsync(owner, "Second");
        await app.AddAddressAsync(other, "Other", true);
        await app.LoginAsync(client, "buyer@example.invalid");
        var list = await client.GetStringAsync("/Addresses");
        var article = Regex.Match(list, $"data-address-id=\"{second}\"[\\s\\S]*?</article>").Value;

        var result = await client.PostAsync($"/Addresses/SetDefault/{second}", Form(
            ("__RequestVerificationToken", Token(article)), ("confirmation", Field(article, "confirmation"))));

        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        var rows = await app.ReadAddressesAsync();
        Assert.Equal(second, Assert.Single(rows, x => x.UserId == owner && x.IsDefault).Id);
        Assert.True(rows.Single(x => x.UserId == other).IsDefault);
    }

    [Fact]
    public async Task CreatingAnotherDefaultClearsOldDefaultBeforeUniqueIndexChecks()
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        var owner = await app.CreateUserAsync("buyer@example.invalid");
        var first = await app.AddAddressAsync(owner, "First", true);
        await app.LoginAsync(client, "buyer@example.invalid");

        var result = await client.PostAsync("/Addresses/Create", Form(ValidFields(
            Token(await client.GetStringAsync("/Addresses/Create"))).Append(("IsDefault", "true")).ToArray()));

        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        var rows = await app.ReadAddressesAsync();
        Assert.Equal(2, rows.Count);
        Assert.False(rows.Single(x => x.Id == first).IsDefault);
        Assert.Equal("Người nhận", Assert.Single(rows, x => x.IsDefault).RecipientName);
    }

    [Fact]
    public async Task DeleteGetNeverMutatesAndDeletePostPromotesEarliestRemainingAddress()
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        var owner = await app.CreateUserAsync("buyer@example.invalid");
        var first = await app.AddAddressAsync(owner, "First", true);
        var second = await app.AddAddressAsync(owner, "Second");
        await app.AddAddressAsync(owner, "Third");
        await app.LoginAsync(client, "buyer@example.invalid");
        var confirmation = await client.GetStringAsync($"/Addresses/Delete/{first}");
        Assert.Equal(3, (await app.ReadAddressesAsync()).Count);

        var result = await client.PostAsync($"/Addresses/Delete/{first}", Form(
            ("__RequestVerificationToken", Token(confirmation)), ("confirmation", Field(confirmation, "confirmation"))));

        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        var rows = await app.ReadAddressesAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(second, Assert.Single(rows, x => x.IsDefault).Id);
    }

    [Fact]
    public async Task DeletingOnlyAddressLeavesAnEmptyBookAndAccountIntact()
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        var owner = await app.CreateUserAsync("buyer@example.invalid");
        var id = await app.AddAddressAsync(owner, "First", true);
        await app.LoginAsync(client, "buyer@example.invalid");
        var confirmation = await client.GetStringAsync($"/Addresses/Delete/{id}");
        var result = await client.PostAsync($"/Addresses/Delete/{id}", Form(
            ("__RequestVerificationToken", Token(confirmation)), ("confirmation", Field(confirmation, "confirmation"))));

        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Empty(await app.ReadAddressesAsync());
        Assert.Contains("Bạn chưa lưu địa chỉ", Html(await client.GetStringAsync("/Addresses")));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Profile")).StatusCode);
    }

    [Fact]
    public async Task DeletingMissingAddressIsNotFoundAndDoesNotTouchExistingData()
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        var owner = await app.CreateUserAsync("buyer@example.invalid");
        await app.AddAddressAsync(owner, "Kept", true);
        await app.LoginAsync(client, "buyer@example.invalid");
        var response = await client.PostAsync("/Addresses/Delete/2147483647", Form(
            ("__RequestVerificationToken", Token(await client.GetStringAsync("/Addresses/Create")))));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Kept", Assert.Single(await app.ReadAddressesAsync()).RecipientName);
    }

    [Fact]
    public async Task EditTicketCannotBeReusedToDeleteAddress()
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        var owner = await app.CreateUserAsync("buyer@example.invalid");
        var id = await app.AddAddressAsync(owner, "Kept", true);
        await app.LoginAsync(client, "buyer@example.invalid");
        var edit = await client.GetStringAsync($"/Addresses/Edit/{id}");

        await client.PostAsync($"/Addresses/Delete/{id}", Form(
            ("__RequestVerificationToken", Token(edit)), ("confirmation", Field(edit, "VersionToken"))));

        Assert.Equal("Kept", Assert.Single(await app.ReadAddressesAsync()).RecipientName);
        Assert.Contains("xác nhận đã hết hạn", Html(await client.GetStringAsync("/Addresses")));
    }

    [Fact]
    public async Task FailedSecondWriteRollsBackClearingPreviousDefault()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var baseOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using (var setup = new ApplicationDbContext(baseOptions))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Users.Add(new ApplicationUser { Id = "owner", UserName = "owner@example.invalid" });
            setup.ShippingAddresses.Add(NewAddress("owner", "First", true));
            await setup.SaveChangesAsync();
        }
        var interceptor = new FailSecondSave();
        await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options))
        {
            var service = new ShippingAddressService(db, new EphemeralDataProtectionProvider(), NullLogger<ShippingAddressService>.Instance);
            var result = await service.CreateAsync(new AddressFormViewModel
            {
                RecipientName = "Second", PhoneNumber = "0912345678", Province = "Tỉnh", Ward = "Xã",
                AddressLine = "12 Đường", IsDefault = true
            }, "owner");
            Assert.Equal(AddressOperationStatus.Failure, result.Status);
        }
        await using var verification = new ApplicationDbContext(baseOptions);
        var address = Assert.Single(await verification.ShippingAddresses.ToListAsync());
        Assert.Equal("First", address.RecipientName);
        Assert.True(address.IsDefault);
    }

    [Fact]
    public async Task ConcurrentDefaultChangesThroughIndependentConnectionsKeepOneDefault()
    {
        var connectionString = $"Data Source=addresses-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=2";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString).Options;
        var protection = new EphemeralDataProtectionProvider();
        IReadOnlyList<AddressItemViewModel> targets;
        await using (var setup = new ApplicationDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Users.Add(new ApplicationUser { Id = "owner", UserName = "owner@example.invalid" });
            setup.ShippingAddresses.AddRange(NewAddress("owner", "First", true),
                NewAddress("owner", "Second", false), NewAddress("owner", "Third", false));
            await setup.SaveChangesAsync();
            var service = new ShippingAddressService(setup, protection, NullLogger<ShippingAddressService>.Instance);
            targets = (await service.GetListAsync("owner")).Items.Where(a => !a.IsDefault).ToArray();
        }
        using var start = new ManualResetEventSlim(false);
        var tasks = targets.Select(target => Task.Run(async () =>
        {
            await using var db = new ApplicationDbContext(options);
            var service = new ShippingAddressService(db, protection, NullLogger<ShippingAddressService>.Instance);
            start.Wait();
            return await service.SetDefaultAsync(target.Id, target.DefaultToken, "owner");
        })).ToArray();
        start.Set();
        var results = await Task.WhenAll(tasks);

        Assert.Contains(results, r => r.Status == AddressOperationStatus.Success);
        await using var verification = new ApplicationDbContext(options);
        var addresses = await verification.ShippingAddresses.AsNoTracking().ToListAsync();
        Assert.Equal(3, addresses.Count);
        var current = Assert.Single(addresses, a => a.IsDefault);
        Assert.Contains(current.Id, targets.Select(a => a.Id));
    }

    [Fact]
    public async Task AddressReadFailureDisplaysSafeUnavailableState()
    {
        await using var app = new AddressApplication();
        using var client = app.NewClient();
        await app.CreateUserAsync("buyer@example.invalid");
        await app.LoginAsync(client, "buyer@example.invalid");
        await app.ChangeAsync(db => db.Database.ExecuteSqlRawAsync("DROP TABLE \"ShippingAddresses\";"));

        var response = await client.GetAsync("/Addresses");
        var html = Html(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Chưa tải được địa chỉ", html);
        Assert.DoesNotContain("SqliteException", html);
        Assert.DoesNotContain("ShippingAddresses", html);
    }

    private static IEnumerable<(string, string)> ValidFields(string antiforgery) =>
    [
        ("__RequestVerificationToken", antiforgery), ("RecipientName", " Người nhận "),
        ("PhoneNumber", "0912345678"), ("Province", "Thành phố Hà Nội"),
        ("Ward", "Phường hiện hành"), ("AddressLine", "12 Đường Nông Sản"), ("District", "")
    ];
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(field => new KeyValuePair<string, string>(field.Key, field.Value)));
    private static string Html(string html) => WebUtility.HtmlDecode(html);
    private static string Token(string html) => Field(html, "__RequestVerificationToken");
    private static string Field(string html, string name)
    {
        var match = Regex.Match(html, "name=\"" + name + "\"[^>]*value=\"([^\"]*)\"");
        Assert.True(match.Success, $"Missing rendered form field {name}.");
        return Html(match.Groups[1].Value);
    }
    private static void AssertLogin(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith("/Account/Login", location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString);
    }
    private static ShippingAddress NewAddress(string ownerId, string recipient, bool isDefault) => new()
    {
        UserId = ownerId, RecipientName = recipient, PhoneNumber = "0912345678", Province = "Tỉnh hiện hành",
        Ward = "Xã hiện hành", AddressLine = "12 Đường Nông Sản", IsDefault = isDefault
    };

    private sealed class FailSecondSave : SaveChangesInterceptor
    {
        private int calls;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (++calls == 2) throw new InvalidOperationException("ISOLATED_TEST_FAILURE");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class AddressApplication : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly string password = $"{Guid.NewGuid():N}!aA1";
        public AddressApplication()
        {
            connection.Open();
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            db.Database.EnsureCreated();
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Isolated test replaces SQL Server with SQLite");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            });
        }
        public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        public async Task<string> CreateUserAsync(string email)
        {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, FullName = "Isolated address test" };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Customer")).Succeeded);
            return user.Id;
        }
        public async Task LoginAsync(HttpClient client, string email)
        {
            var response = await client.PostAsync("/Account/Login", Form(
                ("__RequestVerificationToken", Token(await client.GetStringAsync("/Account/Login"))),
                ("Email", email), ("Password", password)));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        public async Task<int> AddAddressAsync(string ownerId, string name, bool isDefault = false)
        {
            var address = NewAddress(ownerId, name, isDefault);
            await ChangeAsync(async db => { db.ShippingAddresses.Add(address); await db.SaveChangesAsync(); });
            return address.Id;
        }
        public async Task ChangeAsync(Func<ApplicationDbContext, Task> action)
        {
            using var scope = Services.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }
        public async Task<List<ShippingAddress>> ReadAddressesAsync()
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ShippingAddresses.AsNoTracking().ToListAsync();
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
