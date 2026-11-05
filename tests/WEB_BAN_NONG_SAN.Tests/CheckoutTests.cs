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
using Microsoft.Extensions.Configuration;
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

public sealed class CheckoutTests
{
    [Fact]
    public async Task CartStoresPerOwnerAndUsesCurrentServerPrices()
    {
        await using var test = new CheckoutDatabase();
        Assert.True((await test.Service().AddAsync(test.Owner, test.ProductId, 2)).Succeeded);
        Assert.True((await test.Service().AddAsync(test.Other, test.ProductId, 1)).Succeeded);
        var cart = await test.Service().GetCartAsync(test.Owner);
        Assert.Single(cart.Items);
        Assert.Equal(2, cart.Items[0].Quantity);
        Assert.Equal(50000m, cart.Subtotal);
        var other = await test.Service().GetCartAsync(test.Other);
        Assert.Equal(1, other.Items[0].Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    [InlineData(11)]
    public async Task CartRejectsInvalidQuantityOrStock(int quantity)
    {
        await using var test = new CheckoutDatabase();
        Assert.False((await test.Service().AddAsync(test.Owner, test.ProductId, quantity)).Succeeded);
        Assert.Empty(await test.Read().CartItems.ToListAsync());
    }

    [Fact]
    public async Task CartMergesQuantitiesButCannotExceedStock()
    {
        await using var test = new CheckoutDatabase();
        Assert.True((await test.Service().AddAsync(test.Owner, test.ProductId, 6)).Succeeded);
        Assert.False((await test.Service().AddAsync(test.Owner, test.ProductId, 5)).Succeeded);
        Assert.Equal(6, (await test.Read().CartItems.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task StaleCartFormCannotOverwriteNewerQuantity()
    {
        await using var test = new CheckoutDatabase();
        await test.Service().AddAsync(test.Owner, test.ProductId, 1);
        var line = (await test.Service().GetCartAsync(test.Owner)).Items.Single();
        Assert.True((await test.Service().UpdateAsync(test.Owner, line.Id, 2, line.MutationToken)).Succeeded);
        Assert.False((await test.Service().UpdateAsync(test.Owner, line.Id, 3, line.MutationToken)).Succeeded);
        Assert.False((await test.Service().RemoveAsync(test.Owner, line.Id, line.MutationToken)).Succeeded);
        Assert.Equal(2, (await test.Read().CartItems.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task CartIdAndTokenCannotMutateAnotherOwner()
    {
        await using var test = new CheckoutDatabase();
        await test.Service().AddAsync(test.Owner, test.ProductId, 1);
        var line = (await test.Service().GetCartAsync(test.Owner)).Items.Single();
        Assert.True((await test.Service().UpdateAsync(test.Other, line.Id, 2, line.MutationToken)).NotFound);
        Assert.True((await test.Service().RemoveAsync(test.Other, line.Id, line.MutationToken)).NotFound);
        Assert.Equal(1, (await test.Read().CartItems.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task PlaceOrderStoresSnapshotsReducesStockClearsOnlyOwnersCart()
    {
        await using var test = new CheckoutDatabase();
        await test.Service().AddAsync(test.Owner, test.ProductId, 2);
        await test.Service().AddAsync(test.Other, test.ProductId, 1);
        var input = (await test.Service().GetCheckoutAsync(test.Owner)).Input;
        var result = await test.Service().PlaceOrderAsync(test.Owner, input);
        Assert.True(result.Succeeded, result.Message);
        var order = await test.Read().Orders.Include(x => x.Items).SingleAsync();
        Assert.Equal(result.OrderId, order.Id);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
        Assert.Equal(50000m, order.Subtotal);
        Assert.Equal(30000m, order.ShippingFee);
        Assert.Equal(80000m, order.Total);
        Assert.Equal("Người nhận", order.RecipientName);
        Assert.Equal("Rau an toàn", order.Items.Single().ProductName);
        Assert.Equal(25000m, order.Items.Single().UnitPrice);
        Assert.Equal(8, (await test.Read().Products.SingleAsync()).Stock);
        Assert.Equal(test.Other, (await test.Read().CartItems.SingleAsync()).UserId);

        using var edits = test.Read();
        var product = await edits.Products.SingleAsync();
        var address = await edits.ShippingAddresses.SingleAsync(x => x.UserId == test.Owner);
        product.Name = "Tên mới";
        product.Price = 99000m;
        address.RecipientName = "Người nhận mới";
        await edits.SaveChangesAsync();
        var unchanged = await test.Read().Orders.Include(x => x.Items).SingleAsync();
        Assert.Equal("Người nhận", unchanged.RecipientName);
        Assert.Equal("Rau an toàn", unchanged.Items.Single().ProductName);
        Assert.Equal(80000m, unchanged.Total);
    }

    [Fact]
    public async Task RepeatedCheckoutTokenReturnsSameOrderWithoutSecondStockOrCouponConsumption()
    {
        await using var test = new CheckoutDatabase();
        await test.AddCouponAsync("ONCE", CouponKind.FixedAmount, 10000m, maxUses: 1);
        await test.Service().AddAsync(test.Owner, test.ProductId, 2);
        var input = (await test.Service().GetCheckoutAsync(test.Owner, "ONCE")).Input;
        var first = await test.Service().PlaceOrderAsync(test.Owner, input);
        var replay = await test.Service().PlaceOrderAsync(test.Owner, input);
        Assert.True(first.Succeeded, first.Message);
        Assert.True(replay.Succeeded, replay.Message);
        Assert.Equal(first.OrderId, replay.OrderId);
        Assert.Equal(1, await test.Read().Orders.CountAsync());
        Assert.Equal(8, (await test.Read().Products.SingleAsync()).Stock);
        Assert.Equal(1, (await test.Read().Coupons.SingleAsync()).UsedCount);
        Assert.Equal(1, await test.Read().CouponUsages.CountAsync());
    }

    [Fact]
    public async Task FractionalPricesAndPercentageDiscountRoundOnlyToTwoDecimalPlaces()
    {
        await using var test = new CheckoutDatabase();
        using (var update = test.Read())
        {
            (await update.Products.SingleAsync()).Price = 19.99m;
            await update.SaveChangesAsync();
        }
        await test.AddCouponAsync("DECIMAL", CouponKind.Percentage, 10m);
        await test.Service().AddAsync(test.Owner, test.ProductId, 3);
        var input = (await test.Service().GetCheckoutAsync(test.Owner, "DECIMAL")).Input;
        var result = await test.Service().PlaceOrderAsync(test.Owner, input);
        Assert.True(result.Succeeded, result.Message);
        var order = await test.Read().Orders.Include(x => x.Items).SingleAsync();
        Assert.Equal(59.97m, order.Subtotal);
        Assert.Equal(59.97m, order.Items.Single().LineTotal);
        Assert.Equal(6m, order.Discount);
        Assert.Equal(30053.97m, order.Total);
    }

    [Fact]
    public async Task TokenAndAddressAreBoundToCurrentAccount()
    {
        await using var test = new CheckoutDatabase();
        await test.Service().AddAsync(test.Owner, test.ProductId, 1);
        var input = (await test.Service().GetCheckoutAsync(test.Owner)).Input;
        Assert.False((await test.Service().PlaceOrderAsync(test.Other, input)).Succeeded);
        input.AddressId = await test.Read().ShippingAddresses.Where(x => x.UserId == test.Other).Select(x => x.Id).SingleAsync();
        Assert.False((await test.Service().PlaceOrderAsync(test.Owner, input)).Succeeded);
        Assert.Empty(await test.Read().Orders.ToListAsync());
        Assert.Equal(10, (await test.Read().Products.SingleAsync()).Stock);
    }

    [Fact]
    public async Task TamperedCheckoutTokenIsRejectedWithoutAnyWrite()
    {
        await using var test = new CheckoutDatabase();
        await test.Service().AddAsync(test.Owner, test.ProductId, 1);
        var input = (await test.Service().GetCheckoutAsync(test.Owner)).Input;
        input.CheckoutToken = "tampered";
        Assert.False((await test.Service().PlaceOrderAsync(test.Owner, input)).Succeeded);
        Assert.Empty(await test.Read().Orders.ToListAsync());
    }

    [Theory]
    [InlineData("price")]
    [InlineData("cart")]
    [InlineData("address")]
    [InlineData("inactive")]
    public async Task CheckoutRejectsChangesSinceTheReviewedPage(string change)
    {
        await using var test = new CheckoutDatabase();
        await test.Service().AddAsync(test.Owner, test.ProductId, 1);
        var input = (await test.Service().GetCheckoutAsync(test.Owner)).Input;
        using (var update = test.Read())
        {
            if (change == "address")
            {
                var address = await update.ShippingAddresses.SingleAsync(x => x.UserId == test.Owner);
                address.AddressLine = "Địa chỉ mới";
                address.Version = Guid.NewGuid().ToString("N");
            }
            else if (change == "cart")
            {
                var cart = await update.CartItems.SingleAsync();
                cart.Quantity = 2;
                cart.Version = Guid.NewGuid().ToString("N");
            }
            else
            {
                var product = await update.Products.SingleAsync();
                if (change == "price") product.Price = 100m;
                else product.IsActive = false;
                product.Version = Guid.NewGuid().ToString("N");
            }
            await update.SaveChangesAsync();
        }
        Assert.False((await test.Service().PlaceOrderAsync(test.Owner, input)).Succeeded);
        Assert.Empty(await test.Read().Orders.ToListAsync());
        Assert.Equal(10, (await test.Read().Products.SingleAsync()).Stock);
    }

    [Fact]
    public async Task TwoShoppersCannotBothBuyTheLastUnit()
    {
        await using var test = new CheckoutDatabase();
        using (var seed = test.Read())
        {
            (await seed.Products.SingleAsync()).Stock = 1;
            await seed.SaveChangesAsync();
        }
        await test.Service().AddAsync(test.Owner, test.ProductId, 1);
        await test.Service().AddAsync(test.Other, test.ProductId, 1);
        var first = (await test.Service().GetCheckoutAsync(test.Owner)).Input;
        var second = (await test.Service().GetCheckoutAsync(test.Other)).Input;
        Assert.True((await test.Service().PlaceOrderAsync(test.Owner, first)).Succeeded);
        Assert.False((await test.Service().PlaceOrderAsync(test.Other, second)).Succeeded);
        Assert.Equal(0, (await test.Read().Products.SingleAsync()).Stock);
        Assert.Equal(1, await test.Read().Orders.CountAsync());
        Assert.Equal(test.Other, (await test.Read().CartItems.SingleAsync()).UserId);
    }

    [Fact]
    public async Task OverlappingCheckoutTransactionsKeepTheLastStockAndCouponAllowanceConsistent()
    {
        await using var test = new CheckoutDatabase();
        using (var seed = test.Read())
        {
            (await seed.Products.SingleAsync()).Stock = 1;
            await seed.SaveChangesAsync();
        }
        await test.AddCouponAsync("RACE", CouponKind.FixedAmount, 1000m, maxUses: 1);
        await test.Service().AddAsync(test.Owner, test.ProductId, 1);
        await test.Service().AddAsync(test.Other, test.ProductId, 1);
        var firstInput = (await test.Service().GetCheckoutAsync(test.Owner, "RACE")).Input;
        var secondInput = (await test.Service().GetCheckoutAsync(test.Other, "RACE")).Input;
        var pause = new PauseOrderWrite();
        var firstTask = test.Service(pause).PlaceOrderAsync(test.Owner, firstInput);
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        CommerceResult second;
        try
        {
            // Independent connections to the same isolated database. The first
            // transaction is held open here while the second genuinely overlaps it.
            second = await test.Service().PlaceOrderAsync(test.Other, secondInput).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { pause.Release.TrySetResult(); }
        var first = await firstTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(first.Succeeded, first.Message);
        Assert.False(second.Succeeded);
        Assert.Equal(0, (await test.Read().Products.SingleAsync()).Stock);
        Assert.Equal(1, (await test.Read().Coupons.SingleAsync()).UsedCount);
        Assert.Equal(1, await test.Read().Orders.CountAsync());
        Assert.Equal(1, await test.Read().CouponUsages.CountAsync());
        Assert.Equal(test.Other, (await test.Read().CartItems.SingleAsync()).UserId);
    }

    [Theory]
    [InlineData(CouponKind.Percentage, 10, 5000, 75000)]
    [InlineData(CouponKind.FixedAmount, 10000, 10000, 70000)]
    [InlineData(CouponKind.FixedAmount, 999999, 50000, 30000)]
    public async Task CouponsUseDecimalServerCalculationAndCannotDiscountShippingOrExceedSubtotal(CouponKind kind, int value, int expectedDiscount, int expectedTotal)
    {
        await using var test = new CheckoutDatabase();
        await test.AddCouponAsync("SAVE", kind, value);
        await test.Service().AddAsync(test.Owner, test.ProductId, 2);
        var input = (await test.Service().GetCheckoutAsync(test.Owner, " save ")).Input;
        var result = await test.Service().PlaceOrderAsync(test.Owner, input);
        Assert.True(result.Succeeded, result.Message);
        var order = await test.Read().Orders.SingleAsync();
        Assert.Equal(expectedDiscount, order.Discount);
        Assert.Equal(expectedTotal, order.Total);
        Assert.Equal("SAVE", order.CouponCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("exhausted")]
    [InlineData("minimum")]
    public async Task UnusableCouponRejectsOrderWithoutChangingAnything(string scenario)
    {
        await using var test = new CheckoutDatabase();
        if (scenario != "missing") await test.AddCouponAsync("BAD", CouponKind.FixedAmount, 1000m);
        using (var update = test.Read())
        {
            var coupon = await update.Coupons.SingleOrDefaultAsync();
            if (coupon is not null)
            {
                if (scenario == "inactive") coupon.IsActive = false;
                if (scenario == "expired") { coupon.StartsAtUtc = DateTime.UtcNow.AddDays(-2); coupon.EndsAtUtc = DateTime.UtcNow.AddDays(-1); }
                if (scenario == "future") { coupon.StartsAtUtc = DateTime.UtcNow.AddDays(1); coupon.EndsAtUtc = DateTime.UtcNow.AddDays(2); }
                if (scenario == "exhausted") coupon.UsedCount = coupon.MaxUses;
                if (scenario == "minimum") coupon.MinimumSubtotal = 1000000m;
                await update.SaveChangesAsync();
            }
        }
        await test.Service().AddAsync(test.Owner, test.ProductId, 1);
        var input = (await test.Service().GetCheckoutAsync(test.Owner, "BAD")).Input;
        Assert.False((await test.Service().PlaceOrderAsync(test.Owner, input)).Succeeded);
        Assert.Empty(await test.Read().Orders.ToListAsync());
        Assert.Equal(10, (await test.Read().Products.SingleAsync()).Stock);
        Assert.Single(await test.Read().CartItems.ToListAsync());
    }

    [Fact]
    public async Task CouponLastAllowanceCannotBeUsedByTwoOrders()
    {
        await using var test = new CheckoutDatabase();
        await test.AddCouponAsync("LAST", CouponKind.FixedAmount, 1000m, maxUses: 1);
        await test.Service().AddAsync(test.Owner, test.ProductId, 1);
        await test.Service().AddAsync(test.Other, test.ProductId, 1);
        var first = (await test.Service().GetCheckoutAsync(test.Owner, "LAST")).Input;
        var second = (await test.Service().GetCheckoutAsync(test.Other, "LAST")).Input;
        Assert.True((await test.Service().PlaceOrderAsync(test.Owner, first)).Succeeded);
        Assert.False((await test.Service().PlaceOrderAsync(test.Other, second)).Succeeded);
        Assert.Equal(1, (await test.Read().Coupons.SingleAsync()).UsedCount);
        Assert.Equal(1, await test.Read().CouponUsages.CountAsync());
        Assert.Equal(9, (await test.Read().Products.SingleAsync()).Stock);
    }

    [Theory]
    [InlineData("coupon-edit")]
    [InlineData("coupon-tamper")]
    [InlineData("shipping")]
    public async Task CheckoutCannotPlaceAnOrderAtAnUnreviewedDiscountOrShippingFee(string change)
    {
        await using var test = new CheckoutDatabase();
        await test.AddCouponAsync("QUOTE", CouponKind.FixedAmount, 10000m);
        await test.Service().AddAsync(test.Owner, test.ProductId, 1);
        var input = (await test.Service().GetCheckoutAsync(test.Owner, "QUOTE")).Input;
        if (change == "coupon-edit")
        {
            var update = test.Read();
            var coupon = await update.Coupons.SingleAsync();
            coupon.Value = 1000m;
            coupon.Version = Guid.NewGuid().ToString("N");
            await update.SaveChangesAsync();
        }
        else if (change == "coupon-tamper") input.CouponCode = null;
        var service = change == "shipping" ? test.ServiceWithShipping(99000m) : test.Service();
        Assert.False((await service.PlaceOrderAsync(test.Owner, input)).Succeeded);
        Assert.Empty(await test.Read().Orders.ToListAsync());
        Assert.Equal(10, (await test.Read().Products.SingleAsync()).Stock);
        Assert.Equal(0, (await test.Read().Coupons.SingleAsync()).UsedCount);
    }

    [Fact]
    public async Task DatabaseFailureRollsBackStockCouponCartAndOrderTogether()
    {
        await using var test = new CheckoutDatabase();
        await test.AddCouponAsync("ROLLBACK", CouponKind.FixedAmount, 1000m);
        await test.Service().AddAsync(test.Owner, test.ProductId, 1);
        var input = (await test.Service().GetCheckoutAsync(test.Owner, "ROLLBACK")).Input;
        var result = await test.Service(new RejectOrderWrite()).PlaceOrderAsync(test.Owner, input);
        Assert.False(result.Succeeded);
        Assert.DoesNotContain("sensitive", result.Message);
        Assert.Equal(10, (await test.Read().Products.SingleAsync()).Stock);
        Assert.Equal(0, (await test.Read().Coupons.SingleAsync()).UsedCount);
        Assert.Empty(await test.Read().Orders.ToListAsync());
        Assert.Empty(await test.Read().CouponUsages.ToListAsync());
        Assert.Single(await test.Read().CartItems.ToListAsync());
    }

    [Theory]
    [InlineData("/Cart")]
    [InlineData("/Checkout")]
    public async Task AnonymousCustomerCommercePagesChallenge(string path)
    {
        await using var app = new CheckoutApplication();
        using var client = app.NewClient();
        AssertRedirect(await client.GetAsync(path), "/Account/Login");
    }

    [Theory]
    [InlineData("/Cart/Add")]
    [InlineData("/Cart/Update/1")]
    [InlineData("/Cart/Remove/1")]
    [InlineData("/Checkout/PlaceOrder")]
    public async Task CommercePostsRequireAntiforgery(string path)
    {
        await using var app = new CheckoutApplication();
        using var client = app.NewClient();
        await app.LoginAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path, Form())).StatusCode);
    }

    [Fact]
    public async Task HttpCheckoutIgnoresForgedBrowserPricesAndCreatesOneCodOrder()
    {
        await using var app = new CheckoutApplication();
        using var client = app.NewClient();
        var owner = await app.LoginAsync(client);
        int productId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var category = new Category { Name = "Đang bán" };
            var product = new Product { Name = "Thực phẩm", Description = "Mô tả", Category = category, Price = 25000m, Stock = 5 };
            db.Products.Add(product);
            db.ShippingAddresses.Add(new ShippingAddress { UserId = owner, RecipientName = "Người mua", PhoneNumber = "0901234567", Province = "Thành phố", Ward = "Phường", AddressLine = "Số nhà 1", IsDefault = true });
            await db.SaveChangesAsync();
            productId = product.Id;
        }
        var cartHtml = await client.GetStringAsync("/Catalog");
        var csrf = Token(cartHtml, "__RequestVerificationToken");
        AssertRedirect(await client.PostAsync("/Cart/Add", Form(("__RequestVerificationToken", csrf), ("ProductId", productId.ToString()), ("Quantity", "2"), ("UnitPrice", "1"))), "/Cart");
        var checkoutHtml = await client.GetStringAsync("/Checkout");
        var checkoutToken = Token(checkoutHtml, "Input.CheckoutToken");
        using var readScope = app.Services.CreateScope();
        var read = readScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var addressId = (await read.ShippingAddresses.SingleAsync()).Id;
        var fields = new[] { ("__RequestVerificationToken", Token(checkoutHtml, "__RequestVerificationToken")), ("Input.CheckoutToken", checkoutToken), ("Input.AddressId", addressId.ToString()), ("Input.Total", "1"), ("Input.Subtotal", "1"), ("Input.ShippingFee", "0"), ("Input.Discount", "999999") };
        var response = await client.PostAsync("/Checkout/PlaceOrder", Form(fields));
        AssertRedirect(response, "/Orders/Details/");
        var replay = await client.PostAsync("/Checkout/PlaceOrder", Form(fields));
        Assert.Equal(response.Headers.Location, replay.Headers.Location);
        var order = await read.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(80000m, order.Total);
        Assert.Equal(0, order.Discount);
        Assert.Equal(1, await read.Orders.CountAsync());
    }

    private sealed class RejectOrderWrite : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Order>().Any(x => x.State == EntityState.Added))
                throw new DbUpdateException("test-sensitive-database-error");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PauseOrderWrite : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Order>().Any(x => x.State == EntityState.Added))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return result;
        }
    }

    private sealed class CheckoutDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new($"Data Source=checkout-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=1");
        private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
        private readonly List<ApplicationDbContext> contexts = [];
        public string Owner { get; } = Guid.NewGuid().ToString();
        public string Other { get; } = Guid.NewGuid().ToString();
        public int ProductId { get; }

        public CheckoutDatabase()
        {
            connection.Open();
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            db.Database.EnsureCreated();
            db.Users.AddRange(new ApplicationUser { Id = Owner }, new ApplicationUser { Id = Other });
            var product = new Product { Name = "Rau an toàn", Description = "Mô tả", Unit = "kg", Price = 25000m, Stock = 10, Category = new Category { Name = "Rau" } };
            db.Products.Add(product);
            foreach (var owner in new[] { Owner, Other })
                db.ShippingAddresses.Add(new ShippingAddress { UserId = owner, RecipientName = "Người nhận", PhoneNumber = "0901234567", Province = "Thành phố", Ward = "Phường", AddressLine = "Số nhà 1", IsDefault = true });
            db.SaveChanges();
            ProductId = product.Id;
        }

        public ApplicationDbContext Read(params IInterceptor[] interceptors)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection.ConnectionString);
            if (interceptors.Length > 0) options.AddInterceptors(interceptors);
            var db = new ApplicationDbContext(options.Options);
            contexts.Add(db);
            return db;
        }

        public CheckoutService Service(params IInterceptor[] interceptors) => new(Read(interceptors), protection,
            new ConfigurationBuilder().AddInMemoryCollection().Build(), NullLogger<CheckoutService>.Instance);

        public CheckoutService ServiceWithShipping(decimal shipping) => new(Read(), protection,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Commerce:ShippingFee"] = shipping.ToString(System.Globalization.CultureInfo.InvariantCulture) }).Build(), NullLogger<CheckoutService>.Instance);

        public async Task AddCouponAsync(string code, CouponKind kind, decimal value, int maxUses = 5)
        {
            var db = Read();
            db.Coupons.Add(new Coupon { Code = code, Kind = kind, Value = value, StartsAtUtc = DateTime.UtcNow.AddDays(-1), EndsAtUtc = DateTime.UtcNow.AddDays(1), MaxUses = maxUses });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var context in contexts) await context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class CheckoutApplication : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly string password = Guid.NewGuid().ToString("N") + "!aA1";
        public CheckoutApplication()
        {
            connection.Open();
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            db.Database.EnsureCreated();
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Test-only; replaced by isolated SQLite");
            builder.ConfigureLogging(x => x.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection));
            });
        }
        public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        public async Task<string> LoginAsync(HttpClient client)
        {
            var email = Guid.NewGuid().ToString("N") + "@example.invalid";
            string owner;
            using (var scope = Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser { UserName = email, Email = email, FullName = "Khách kiểm thử" };
                Assert.True((await users.CreateAsync(user, password)).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, "Customer")).Succeeded);
                owner = user.Id;
            }
            var html = await client.GetStringAsync("/Account/Login");
            AssertRedirect(await client.PostAsync("/Account/Login", Form(("__RequestVerificationToken", Token(html, "__RequestVerificationToken")), ("Email", email), ("Password", password))), "/");
            return owner;
        }
        public override async ValueTask DisposeAsync() { await base.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private static string Token(string html, string name)
    {
        var match = Regex.Match(html, "name=\"" + Regex.Escape(name) + "\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, $"Expected actual rendered token {name}.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) => new(fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
    private static void AssertRedirect(HttpResponseMessage response, string prefix)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.IsAbsoluteUri ? response.Headers.Location.PathAndQuery : response.Headers.Location.OriginalString;
        Assert.True(location == prefix || location.StartsWith(prefix + "?", StringComparison.Ordinal) || (prefix.EndsWith('/') && location.StartsWith(prefix, StringComparison.Ordinal)), $"Expected {prefix}, received {location}.");
    }
}
