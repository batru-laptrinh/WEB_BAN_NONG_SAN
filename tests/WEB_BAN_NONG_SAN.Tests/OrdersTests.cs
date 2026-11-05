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

public sealed class OrdersTests
{
    [Theory]
    [InlineData("/Orders")]
    [InlineData("/Orders/Details/35b57fd8-b6e0-49aa-9ba7-7fe37b116c76")]
    [InlineData("/Admin/Orders")]
    public async Task AnonymousOrderPagesRequireLogin(string path)
    {
        await using var app = new OrdersApplication();
        using var client = app.NewClient();
        AssertRedirect(await client.GetAsync(path), "/Account/Login");
    }

    [Fact]
    public async Task CustomerCannotReadOrMutateAdminOrders()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        using var client = app.NewClient();
        await app.LoginAsync(client, "buyer@example.invalid");
        AssertRedirect(await client.GetAsync("/Admin/Orders"), "/Account/AccessDenied");
        var csrf = Hidden(await client.GetStringAsync("/Profile"), "__RequestVerificationToken");
        AssertRedirect(await client.PostAsync($"/Admin/Orders/Change/{seed.Order}", Form(
            ("__RequestVerificationToken", csrf), ("target", "Confirmed"), ("confirmed", "true"))), "/Account/AccessDenied");
        Assert.Equal(OrderStatus.Pending, (await app.SnapshotAsync(seed.Order)).Status);
    }

    [Fact]
    public async Task CustomerListAndDetailsContainOnlyOwnSnapshots()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        var otherOrder = await app.AddOrderAsync(seed.Other, seed.Product, "Private other recipient");
        using var client = app.NewClient();
        await app.LoginAsync(client, "buyer@example.invalid");
        var list = await client.GetStringAsync("/Orders");
        Assert.Contains(seed.Order.ToString("N").ToUpperInvariant(), list);
        Assert.DoesNotContain(otherOrder.ToString("N").ToUpperInvariant(), list);
        var details = await client.GetAsync($"/Orders/Details/{seed.Order}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.True(details.Headers.CacheControl?.NoStore);
        var html = await details.Content.ReadAsStringAsync();
        Assert.Contains("Historical product", html);
        Assert.DoesNotContain("Current product", html);
        Assert.DoesNotContain("PasswordHash", html);
        Assert.DoesNotContain("SecurityStamp", html);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Orders/Details/{otherOrder}")).StatusCode);
    }

    [Fact]
    public async Task CustomerCannotCancelAnotherOwnersOrderEvenWithOwnToken()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        var other = await app.AddOrderAsync(seed.Other, seed.Product, "Other recipient");
        var own = await app.CustomerDetailsAsync(seed.Order, seed.Buyer);
        var result = await app.WithServiceAsync(service => service.CancelCustomerAsync(other, seed.Buyer, own.Actions.Single().Confirmation));
        Assert.Equal(OrderOperationStatus.NotFound, result.Status);
        Assert.Equal(OrderStatus.Pending, (await app.SnapshotAsync(other)).Status);
        Assert.Equal(5, await app.StockAsync(seed.Product));
    }

    [Fact]
    public async Task AllOrderPostsRequireAntiforgery()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        using var customer = app.NewClient();
        await app.LoginAsync(customer, "buyer@example.invalid");
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsync($"/Orders/Cancel/{seed.Order}",
            Form(("confirmation", "anything"), ("confirmed", "true")))).StatusCode);
        using var admin = app.NewClient();
        await app.LoginAsync(admin, "admin@example.invalid");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/Admin/Orders/Change/{seed.Order}",
            Form(("target", "Confirmed"), ("confirmed", "true")))).StatusCode);
        Assert.Equal(OrderStatus.Pending, (await app.SnapshotAsync(seed.Order)).Status);
    }

    [Fact]
    public async Task CustomerCancellationRestocksExactlyOnceAndKeepsCouponUsage()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync(coupon: true);
        var details = await app.CustomerDetailsAsync(seed.Order, seed.Buyer);
        var token = details.Actions.Single().Confirmation;
        var first = await app.WithServiceAsync(service => service.CancelCustomerAsync(seed.Order, seed.Buyer, token));
        var repeat = await app.WithServiceAsync(service => service.CancelCustomerAsync(seed.Order, seed.Buyer, token));
        Assert.Equal(OrderOperationStatus.Success, first.Status);
        Assert.Equal(OrderOperationStatus.Success, repeat.Status);
        Assert.Equal(7, await app.StockAsync(seed.Product));
        var snapshot = await app.SnapshotAsync(seed.Order);
        Assert.Equal(OrderStatus.Cancelled, snapshot.Status);
        Assert.Equal(PaymentStatus.Unpaid, snapshot.Payment);
        Assert.Equal(1, snapshot.CouponCount);
        Assert.Equal(1, snapshot.UsageCount);
        var finalDetails = await app.CustomerDetailsAsync(seed.Order, seed.Buyer);
        Assert.Empty(finalDetails.Actions);
        Assert.Equal(details.Items, finalDetails.Items);
        Assert.Equal(details.Total, finalDetails.Total);
    }

    [Fact]
    public async Task AdminWorkflowIsMonotonicAndCompletedCodIsPaid()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        foreach (var target in new[] { OrderStatus.Confirmed, OrderStatus.Shipping, OrderStatus.Completed })
        {
            var details = await app.AdminDetailsAsync(seed.Order, seed.Admin);
            var action = details.Actions.Single(action => action.TargetStatus == target);
            var result = await app.WithServiceAsync(service => service.ChangeAdminAsync(seed.Order, seed.Admin, target, action.Confirmation));
            Assert.Equal(OrderOperationStatus.Success, result.Status);
            Assert.Equal(target, (await app.SnapshotAsync(seed.Order)).Status);
        }
        var completed = await app.AdminDetailsAsync(seed.Order, seed.Admin);
        Assert.Empty(completed.Actions);
        Assert.NotNull(completed.CompletedAtUtc);
        Assert.Equal(PaymentStatus.Paid, completed.PaymentStatus);
        Assert.Equal(5, await app.StockAsync(seed.Product));
    }

    [Fact]
    public async Task CustomerCancellationIsRejectedAfterAdminConfirmation()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        var customerToken = (await app.CustomerDetailsAsync(seed.Order, seed.Buyer)).Actions.Single().Confirmation;
        var adminDetails = await app.AdminDetailsAsync(seed.Order, seed.Admin);
        var confirm = adminDetails.Actions.Single(action => action.TargetStatus == OrderStatus.Confirmed);
        Assert.Equal(OrderOperationStatus.Success, (await app.WithServiceAsync(service =>
            service.ChangeAdminAsync(seed.Order, seed.Admin, OrderStatus.Confirmed, confirm.Confirmation))).Status);
        Assert.Equal(OrderOperationStatus.Conflict, (await app.WithServiceAsync(service =>
            service.CancelCustomerAsync(seed.Order, seed.Buyer, customerToken))).Status);
        Assert.Empty((await app.CustomerDetailsAsync(seed.Order, seed.Buyer)).Actions);
        Assert.Equal(5, await app.StockAsync(seed.Product));
    }

    [Fact]
    public async Task AdminCanCancelConfirmedButCannotCancelShipping()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        var staleCancel = (await app.AdminDetailsAsync(seed.Order, seed.Admin)).Actions.Single(action => action.TargetStatus == OrderStatus.Cancelled);
        await app.AdminTransitionAsync(seed.Order, seed.Admin, OrderStatus.Confirmed);
        var confirmed = await app.AdminDetailsAsync(seed.Order, seed.Admin);
        Assert.Contains(confirmed.Actions, action => action.TargetStatus == OrderStatus.Cancelled);
        await app.AdminTransitionAsync(seed.Order, seed.Admin, OrderStatus.Shipping);
        var shipping = await app.AdminDetailsAsync(seed.Order, seed.Admin);
        Assert.DoesNotContain(shipping.Actions, action => action.TargetStatus == OrderStatus.Cancelled);
        Assert.Equal(OrderOperationStatus.Conflict, (await app.WithServiceAsync(service =>
            service.ChangeAdminAsync(seed.Order, seed.Admin, OrderStatus.Cancelled, staleCancel.Confirmation))).Status);
        Assert.Equal(OrderStatus.Shipping, (await app.SnapshotAsync(seed.Order)).Status);
        Assert.Equal(5, await app.StockAsync(seed.Product));
    }

    [Fact]
    public async Task AdminConfirmedCancellationRestoresStock()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        await app.AdminTransitionAsync(seed.Order, seed.Admin, OrderStatus.Confirmed);
        await app.AdminTransitionAsync(seed.Order, seed.Admin, OrderStatus.Cancelled);
        Assert.Equal(7, await app.StockAsync(seed.Product));
        Assert.Equal(OrderStatus.Cancelled, (await app.SnapshotAsync(seed.Order)).Status);
    }

    [Fact]
    public async Task MutatingTargetOrActorInvalidatesConfirmation()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        var details = await app.AdminDetailsAsync(seed.Order, seed.Admin);
        var confirm = details.Actions.Single(action => action.TargetStatus == OrderStatus.Confirmed);
        var changedTarget = await app.WithServiceAsync(service => service.ChangeAdminAsync(seed.Order, seed.Admin, OrderStatus.Completed, confirm.Confirmation));
        Assert.Equal(OrderOperationStatus.InvalidConfirmation, changedTarget.Status);
        var changedActor = await app.WithServiceAsync(service => service.ChangeAdminAsync(seed.Order, seed.Buyer, OrderStatus.Confirmed, confirm.Confirmation));
        Assert.Equal(OrderOperationStatus.InvalidConfirmation, changedActor.Status);
        Assert.Equal(OrderStatus.Pending, (await app.SnapshotAsync(seed.Order)).Status);
    }

    [Fact]
    public async Task StaleAdminCookieCannotMutateAfterRoleRemoval()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        var confirm = (await app.AdminDetailsAsync(seed.Order, seed.Admin)).Actions.Single(action => action.TargetStatus == OrderStatus.Confirmed);
        await app.MutateAsync(async db =>
        {
            var role = await db.Roles.SingleAsync(role => role.NormalizedName == "ADMIN");
            db.UserRoles.Remove(await db.UserRoles.SingleAsync(link => link.UserId == seed.Admin && link.RoleId == role.Id));
        });
        var result = await app.WithServiceAsync(service => service.ChangeAdminAsync(seed.Order, seed.Admin, OrderStatus.Confirmed, confirm.Confirmation));
        Assert.Equal(OrderOperationStatus.Forbidden, result.Status);
        Assert.Equal(OrderStatus.Pending, (await app.SnapshotAsync(seed.Order)).Status);
    }

    [Fact]
    public async Task LockedCustomerCannotCancelWithPreviouslyIssuedToken()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        var token = (await app.CustomerDetailsAsync(seed.Order, seed.Buyer)).Actions.Single().Confirmation;
        await app.MutateAsync(async db =>
        {
            var buyer = await db.Users.SingleAsync(user => user.Id == seed.Buyer);
            buyer.LockoutEnabled = true;
            buyer.LockoutEnd = DateTimeOffset.UtcNow.AddDays(1);
        });
        Assert.Equal(OrderOperationStatus.Forbidden, (await app.WithServiceAsync(service =>
            service.CancelCustomerAsync(seed.Order, seed.Buyer, token))).Status);
        Assert.Equal(5, await app.StockAsync(seed.Product));
    }

    [Fact]
    public async Task FailureAfterDatabaseSaveRollsBackOrderAndRestock()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        var token = (await app.CustomerDetailsAsync(seed.Order, seed.Buyer)).Actions.Single().Confirmation;
        app.Failures.FailSave = true;
        var result = await app.WithServiceAsync(service => service.CancelCustomerAsync(seed.Order, seed.Buyer, token));
        app.Failures.FailSave = false;
        Assert.Equal(OrderOperationStatus.Failure, result.Status);
        Assert.DoesNotContain(FailureSwitch.PrivateFailure, result.Message);
        Assert.Equal(OrderStatus.Pending, (await app.SnapshotAsync(seed.Order)).Status);
        Assert.Equal(5, await app.StockAsync(seed.Product));
    }

    [Fact]
    public async Task RestockOverflowRefusesEntireCancellation()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        await app.MutateAsync(async db => (await db.Products.SingleAsync(product => product.Id == seed.Product)).Stock = 1_000_000);
        var token = (await app.CustomerDetailsAsync(seed.Order, seed.Buyer)).Actions.Single().Confirmation;
        Assert.Equal(OrderOperationStatus.Conflict, (await app.WithServiceAsync(service =>
            service.CancelCustomerAsync(seed.Order, seed.Buyer, token))).Status);
        Assert.Equal(OrderStatus.Pending, (await app.SnapshotAsync(seed.Order)).Status);
        Assert.Equal(1_000_000, await app.StockAsync(seed.Product));
    }

    [Fact]
    public async Task AdminFilteringAndPagingUseStoredData()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        for (var index = 0; index < 23; index++)
            await app.AddOrderAsync(seed.Buyer, seed.Product, "Filter Buyer " + index);
        var page = await app.WithServiceAsync(service => service.GetAdminListAsync(seed.Admin, "Filter", OrderStatus.Pending, 2));
        Assert.Equal(23, page.TotalCount);
        Assert.Equal(3, page.Items.Count);
        Assert.Equal(2, page.Page);
        var byId = await app.WithServiceAsync(service => service.GetAdminListAsync(seed.Admin, seed.Order.ToString("N"), null));
        Assert.Equal(seed.Order, Assert.Single(byId.Items).Id);
        var absent = await app.WithServiceAsync(service => service.GetAdminListAsync(seed.Admin, null, OrderStatus.Completed));
        Assert.Empty(absent.Items);
    }

    [Fact]
    public async Task HttpCancellationNeedsExplicitConfirmationAndUsesRouteId()
    {
        await using var app = new OrdersApplication();
        var seed = await app.SeedAsync();
        using var client = app.NewClient();
        await app.LoginAsync(client, "buyer@example.invalid");
        var html = await client.GetStringAsync($"/Orders/Details/{seed.Order}");
        var csrf = Hidden(html, "__RequestVerificationToken");
        var token = Hidden(html, "confirmation");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/Orders/Cancel/{seed.Order}", Form(
            ("__RequestVerificationToken", csrf), ("confirmation", token)))).StatusCode);
        AssertRedirect(await client.PostAsync($"/Orders/Cancel/{seed.Order}", Form(
            ("__RequestVerificationToken", csrf), ("confirmation", token), ("confirmed", "true"),
            ("id", Guid.NewGuid().ToString()), ("UserId", seed.Other))), $"/Orders/Details/{seed.Order}");
        Assert.Equal(OrderStatus.Cancelled, (await app.SnapshotAsync(seed.Order)).Status);
        Assert.Equal(7, await app.StockAsync(seed.Product));
    }

    [Fact]
    public async Task ConcurrentCancellationRequestsNeverRestoreStockTwice()
    {
        var connectionString = $"Data Source=orders-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=1";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString,
            sqlite => sqlite.CommandTimeout(1)).Options;
        var owner = Guid.NewGuid().ToString();
        int productId;
        Guid orderId;
        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new ApplicationUser { Id = owner, UserName = "isolated-concurrent", Email = "race@example.invalid" });
            var product = new Product { Name = "Concurrent product", Description = "Isolated", Unit = "kg", Price = 10,
                Stock = 5, Category = new Category { Name = "Concurrent category" } };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            var order = new Order { UserId = owner, CheckoutKey = Guid.NewGuid(), RecipientName = "Concurrent test",
                PhoneNumber = "0901234567", Province = "Province", Ward = "Ward", AddressLine = "Street",
                Subtotal = 20, Total = 20, Items = [new OrderItem { ProductId = product.Id, ProductName = "Snapshot",
                    Unit = "kg", UnitPrice = 10, Quantity = 2, LineTotal = 20 }] };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            productId = product.Id;
            orderId = order.Id;
        }
        var protection = new EphemeralDataProtectionProvider();
        string token;
        await using (var db = new ApplicationDbContext(options))
            token = (await new OrderService(db, protection, NullLogger<OrderService>.Instance)
                .GetCustomerDetailsAsync(orderId, owner))!.Actions.Single().Confirmation;

        async Task<OrderOperationResult> CancelAsync()
        {
            await using var db = new ApplicationDbContext(options);
            return await new OrderService(db, protection, NullLogger<OrderService>.Instance)
                .CancelCustomerAsync(orderId, owner, token);
        }
        var responses = await Task.WhenAll(Task.Run(CancelAsync), Task.Run(CancelAsync));
        Assert.All(responses, response => Assert.Contains(response.Status,
            new[] { OrderOperationStatus.Success, OrderOperationStatus.Conflict, OrderOperationStatus.Failure }));
        // SQLite can reject both contenders on lock contention; a safe retry
        // should complete cancellation without ever repeating the restoration.
        Assert.Equal(OrderOperationStatus.Success, (await CancelAsync()).Status);
        Assert.Equal(OrderOperationStatus.Success, (await CancelAsync()).Status);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(OrderStatus.Cancelled, (await verify.Orders.SingleAsync(order => order.Id == orderId)).Status);
        Assert.Equal(7, (await verify.Products.SingleAsync(product => product.Id == productId)).Stock);
    }

    private static string Hidden(string html, string name)
    {
        var match = Regex.Match(html, $"<input\\b(?=[^>]*\\bname=\"{Regex.Escape(name)}\")(?=[^>]*\\bvalue=\"([^\"]*)\")[^>]*>");
        Assert.True(match.Success, $"Expected form input {name}.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(field => new KeyValuePair<string, string>(field.Key, field.Value)));

    private static void AssertRedirect(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        var value = location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
        Assert.True(value == path || value.StartsWith(path + "?", StringComparison.Ordinal), "Expected redirect to " + path);
    }

    private sealed class FailureSwitch : SaveChangesInterceptor
    {
        public const string PrivateFailure = "isolated-order-failure-must-not-leak";
        public bool FailSave { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default) => FailSave
                ? throw new InvalidOperationException(PrivateFailure) : ValueTask.FromResult(result);
    }

    private sealed record Seed(string Buyer, string Other, string Admin, int Product, Guid Order);
    private sealed record Snapshot(OrderStatus Status, PaymentStatus Payment, int CouponCount, int UsageCount);

    private sealed class OrdersApplication : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private string Password { get; } = $"{Guid.NewGuid():N}!aA1";
        public FailureSwitch Failures { get; } = new();

        public OrdersApplication()
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
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection).AddInterceptors(Failures));
            });
        }

        public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost")
        });

        public async Task<Seed> SeedAsync(bool coupon = false)
        {
            var buyer = await UserAsync("buyer@example.invalid");
            var other = await UserAsync("other@example.invalid");
            var admin = await UserAsync("admin@example.invalid", true);
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var category = new Category { Name = "Fresh category" };
            var product = new Product { Category = category, Name = "Current product", Description = "Changed catalog description", Unit = "kg", Price = 10000m, Stock = 5 };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            var order = NewOrder(buyer, product.Id, "Own recipient");
            db.Orders.Add(order);
            if (coupon)
            {
                var promotion = new Coupon { Code = "ISOLATED", Kind = CouponKind.FixedAmount, Value = 1000m,
                    StartsAtUtc = DateTime.UtcNow.AddDays(-1), EndsAtUtc = DateTime.UtcNow.AddDays(1), MaxUses = 5, UsedCount = 1 };
                db.Coupons.Add(promotion);
                await db.SaveChangesAsync();
                order.CouponId = promotion.Id;
                order.CouponCode = promotion.Code;
                order.Discount = 1000m;
                order.Total -= 1000m;
                db.CouponUsages.Add(new CouponUsage { CouponId = promotion.Id, OrderId = order.Id, UserId = buyer });
            }
            await db.SaveChangesAsync();
            return new(buyer, other, admin, product.Id, order.Id);
        }

        private async Task<string> UserAsync(string email, bool admin = false)
        {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { Email = email, UserName = email, FullName = "Order test" };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, admin ? "Admin" : "Customer")).Succeeded);
            return user.Id;
        }

        public async Task<Guid> AddOrderAsync(string owner, int product, string recipient)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = NewOrder(owner, product, recipient);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            return order.Id;
        }

        private static Order NewOrder(string owner, int product, string recipient) => new()
        {
            UserId = owner, CheckoutKey = Guid.NewGuid(), RecipientName = recipient, PhoneNumber = "0901234567",
            Province = "Stored province", Ward = "Stored ward", AddressLine = "Stored address", Subtotal = 20000m,
            ShippingFee = 30000m, Total = 50000m, Items = [new OrderItem { ProductId = product,
                ProductName = "Historical product", Unit = "stored kg", UnitPrice = 10000m, Quantity = 2, LineTotal = 20000m }]
        };

        public async Task<T> WithServiceAsync<T>(Func<OrderService, Task<T>> action)
        {
            using var scope = Services.CreateScope();
            return await action(scope.ServiceProvider.GetRequiredService<OrderService>());
        }

        public async Task<OrderDetailsViewModel> CustomerDetailsAsync(Guid id, string owner) =>
            Assert.IsType<OrderDetailsViewModel>(await WithServiceAsync(service => service.GetCustomerDetailsAsync(id, owner)));
        public async Task<OrderDetailsViewModel> AdminDetailsAsync(Guid id, string actor) =>
            Assert.IsType<OrderDetailsViewModel>(await WithServiceAsync(service => service.GetAdminDetailsAsync(id, actor)));

        public async Task AdminTransitionAsync(Guid id, string actor, OrderStatus target)
        {
            var details = await AdminDetailsAsync(id, actor);
            var action = details.Actions.Single(action => action.TargetStatus == target);
            Assert.Equal(OrderOperationStatus.Success, (await WithServiceAsync(service =>
                service.ChangeAdminAsync(id, actor, target, action.Confirmation))).Status);
        }

        public async Task MutateAsync(Func<ApplicationDbContext, Task> mutation)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await mutation(db);
            await db.SaveChangesAsync();
        }

        public async Task<int> StockAsync(int id)
        {
            using var scope = Services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Products.SingleAsync(product => product.Id == id)).Stock;
        }

        public async Task<Snapshot> SnapshotAsync(Guid id)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.Orders.SingleAsync(order => order.Id == id);
            return new(order.Status, order.PaymentStatus, await db.Coupons.SumAsync(coupon => coupon.UsedCount),
                await db.CouponUsages.CountAsync());
        }

        public async Task LoginAsync(HttpClient client, string email)
        {
            var csrf = Hidden(await client.GetStringAsync("/Account/Login"), "__RequestVerificationToken");
            AssertRedirect(await client.PostAsync("/Account/Login", Form(("__RequestVerificationToken", csrf),
                ("Email", email), ("Password", Password))), "/");
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
