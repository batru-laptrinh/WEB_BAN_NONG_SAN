using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.Tools;
using Xunit;

namespace WEB_BAN_NONG_SAN.Tests;

// These tests use Identity's real EF stores on an isolated in-memory SQLite database.
// They never read application configuration or connect to SQL Server/Gmail.
public sealed class FirstAdminGrantServiceTests
{
    private const string TargetEmail = "operator@example.invalid";

    [Fact]
    public async Task ConfirmedGrantPreservesCustomerPasswordAndOtherAccountData()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateCustomerAsync(TargetEmail);
        var originalHash = user.PasswordHash;
        var originalStamp = user.SecurityStamp;
        var originalName = user.FullName;
        var output = new StringWriter();

        var result = await fixture.Command.GrantAsync(TargetEmail,
            new StringReader($"CAP ADMIN {TargetEmail}\n"), output);

        Assert.Equal(0, result);
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Users.FindByIdAsync(user.Id);
        Assert.NotNull(persisted);
        Assert.True(await fixture.Users.IsInRoleAsync(persisted, "Admin"));
        Assert.True(await fixture.Users.IsInRoleAsync(persisted, "Customer"));
        Assert.Equal(originalHash, persisted.PasswordHash);
        Assert.Equal(originalStamp, persisted.SecurityStamp);
        Assert.Equal(originalName, persisted.FullName);
        Assert.True(await fixture.Users.CheckPasswordAsync(persisted, fixture.Password));
        Assert.Contains(TargetEmail, output.ToString());
        Assert.DoesNotContain(fixture.Password, output.ToString());
        Assert.DoesNotContain(originalHash!, output.ToString());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task NonDevelopmentIsRefusedBeforeAnyDatabaseQuery(string environment)
    {
        await using var fixture = await IdentityFixture.CreateAsync(environment: environment, createSchema: false);

        var result = await fixture.Command.GrantAsync(TargetEmail,
            new RejectInputReader(), new StringWriter());

        Assert.Equal(3, result);
    }

    [Fact]
    public async Task MissingAdminRoleIsNotCreatedAutomatically()
    {
        await using var fixture = await IdentityFixture.CreateAsync(includeAdminRole: false);
        var user = await fixture.CreateCustomerAsync(TargetEmail);

        var result = await fixture.Command.GrantAsync(TargetEmail,
            new RejectInputReader(), new StringWriter());

        Assert.Equal(4, result);
        Assert.False(await fixture.Roles.RoleExistsAsync("Admin"));
        Assert.Equal(new[] { "Customer" }, await fixture.Users.GetRolesAsync(user));
    }

    [Fact]
    public async Task MissingAccountIsNotCreatedAutomatically()
    {
        await using var fixture = await IdentityFixture.CreateAsync();

        var result = await fixture.Command.GrantAsync(TargetEmail,
            new RejectInputReader(), new StringWriter());

        Assert.Equal(4, result);
        Assert.Empty(await fixture.Db.Users.ToListAsync());
    }

    [Fact]
    public async Task ExistingAdminTargetIsIdempotentAndDoesNotAskForConfirmation()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateCustomerAsync(TargetEmail);
        Assert.True((await fixture.Users.AddToRoleAsync(user, "Admin")).Succeeded);
        var existingStamp = user.ConcurrencyStamp;

        var result = await fixture.Command.GrantAsync(TargetEmail,
            new RejectInputReader(), new StringWriter());

        Assert.Equal(0, result);
        Assert.Equal(2, await fixture.Db.UserRoles.CountAsync(x => x.UserId == user.Id));
        Assert.Equal(existingStamp, user.ConcurrencyStamp);
    }

    [Fact]
    public async Task AnotherExistingAdminPreventsGrantToADifferentCustomer()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var target = await fixture.CreateCustomerAsync(TargetEmail);
        var existingAdmin = await fixture.CreateCustomerAsync("existing-admin@example.invalid");
        Assert.True((await fixture.Users.AddToRoleAsync(existingAdmin, "Admin")).Succeeded);

        var result = await fixture.Command.GrantAsync(TargetEmail,
            new RejectInputReader(), new StringWriter());

        Assert.Equal(4, result);
        Assert.False(await fixture.Users.IsInRoleAsync(target, "Admin"));
        Assert.True(await fixture.Users.IsInRoleAsync(target, "Customer"));
        Assert.Single(await fixture.Users.GetUsersInRoleAsync("Admin"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NO\n")]
    [InlineData("yes\n")]
    [InlineData("CAP ADMIN other@example.invalid\n")]
    [InlineData("cap admin operator@example.invalid\n")]
    public async Task EofDeclineOrWrongAccountConfirmationDoesNotChangeRoles(string input)
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateCustomerAsync(TargetEmail);
        var originalStamp = user.ConcurrencyStamp;

        var result = await fixture.Command.GrantAsync(TargetEmail,
            new StringReader(input), new StringWriter());

        Assert.Equal(5, result);
        Assert.Equal(new[] { "Customer" }, await fixture.Users.GetRolesAsync(user));
        Assert.Equal(originalStamp, user.ConcurrencyStamp);
    }

    [Fact]
    public async Task IdentityFailureAfterStoreSaveRollsBackTheGrant()
    {
        await using var fixture = await IdentityFixture.CreateAsync(failAdminAssignmentAfterSave: true);
        var user = await fixture.CreateCustomerAsync(TargetEmail);
        var originalHash = user.PasswordHash;
        var output = new StringWriter();

        var result = await fixture.Command.GrantAsync(TargetEmail,
            new StringReader($"CAP ADMIN {TargetEmail}\n"), output);

        Assert.Equal(1, result);
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Users.FindByIdAsync(user.Id);
        Assert.NotNull(persisted);
        Assert.False(await fixture.Users.IsInRoleAsync(persisted, "Admin"));
        Assert.True(await fixture.Users.IsInRoleAsync(persisted, "Customer"));
        Assert.Equal(originalHash, persisted.PasswordHash);
        Assert.DoesNotContain("private-test-token", output.ToString());
    }

    [Fact]
    public async Task AdminCreatedDuringConfirmationIsDetectedBeforeTheWrite()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var target = await fixture.CreateCustomerAsync(TargetEmail);
        var reader = new CallbackReader(async () =>
        {
            var other = await fixture.CreateCustomerAsync("concurrent-admin@example.invalid");
            Assert.True((await fixture.Users.AddToRoleAsync(other, "Admin")).Succeeded);
        }, $"CAP ADMIN {TargetEmail}");

        var result = await fixture.Command.GrantAsync(TargetEmail, reader, new StringWriter());

        Assert.Equal(4, result);
        Assert.False(await fixture.Users.IsInRoleAsync(target, "Admin"));
        Assert.Single(await fixture.Users.GetUsersInRoleAsync("Admin"));
    }

    [Fact]
    public async Task ChangedTargetEmailDuringConfirmationRequiresANewConfirmation()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var target = await fixture.CreateCustomerAsync(TargetEmail);
        var reader = new CallbackReader(async () =>
        {
            Assert.True((await fixture.Users.SetEmailAsync(target, "changed@example.invalid")).Succeeded);
        }, $"CAP ADMIN {TargetEmail}");

        var result = await fixture.Command.GrantAsync(TargetEmail, reader, new StringWriter());

        Assert.Equal(4, result);
        Assert.False(await fixture.Users.IsInRoleAsync(target, "Admin"));
        Assert.True(await fixture.Users.IsInRoleAsync(target, "Customer"));
    }

    [Fact]
    public async Task LockedTargetIsRejectedWithoutReadingConfirmation()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var target = await fixture.CreateCustomerAsync(TargetEmail);
        Assert.True((await fixture.Users.SetLockoutEnabledAsync(target, true)).Succeeded);
        Assert.True((await fixture.Users.SetLockoutEndDateAsync(target, DateTimeOffset.UtcNow.AddMinutes(10))).Succeeded);

        var result = await fixture.Command.GrantAsync(TargetEmail,
            new RejectInputReader(), new StringWriter());

        Assert.Equal(4, result);
        Assert.False(await fixture.Users.IsInRoleAsync(target, "Admin"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-address")]
    [InlineData("operator@example.invalid\nunsafe-output")]
    public async Task InvalidEmailIsRefusedBeforeAnyDatabaseQuery(string email)
    {
        await using var fixture = await IdentityFixture.CreateAsync(createSchema: false);

        var result = await fixture.Command.GrantAsync(email,
            new RejectInputReader(), new StringWriter());

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task DatabaseFailureDoesNotPrintExceptionOrProviderDetails()
    {
        await using var fixture = await IdentityFixture.CreateAsync(createSchema: false);
        var output = new StringWriter();

        var result = await fixture.Command.GrantAsync(TargetEmail,
            new RejectInputReader(), output);

        Assert.Equal(1, result);
        Assert.DoesNotContain("SQLite", output.ToString());
        Assert.DoesNotContain("AspNetRoles", output.ToString());
        Assert.DoesNotContain("Exception", output.ToString());
        Assert.DoesNotContain("Data Source", output.ToString());
    }

    private sealed class RejectInputReader : TextReader
    {
        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("This guard must not prompt for confirmation.");
    }

    private sealed class CallbackReader(Func<Task> callback, string response) : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            await callback();
            return response;
        }
    }

    private sealed class IdentityFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider provider;
        private readonly AsyncServiceScope scope;

        private IdentityFixture(SqliteConnection connection, ServiceProvider provider, AsyncServiceScope scope)
        {
            this.connection = connection;
            this.provider = provider;
            this.scope = scope;
        }

        public string Password { get; } = $"{Guid.NewGuid():N}!aA1";
        public ApplicationDbContext Db => scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        public UserManager<ApplicationUser> Users => scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        public RoleManager<IdentityRole> Roles => scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        public FirstAdminGrantService Command => scope.ServiceProvider.GetRequiredService<FirstAdminGrantService>();

        public static async Task<IdentityFixture> CreateAsync(string environment = "Development",
            bool includeAdminRole = true, bool createSchema = true, bool failAdminAssignmentAfterSave = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHostEnvironment>(new TestEnvironment { EnvironmentName = environment });
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();
            if (failAdminAssignmentAfterSave)
            {
                services.AddScoped<UserManager<ApplicationUser>, FailAdminAfterSaveUserManager>();
            }
            services.AddScoped<FirstAdminGrantService>();
            var provider = services.BuildServiceProvider();
            var fixture = new IdentityFixture(connection, provider, provider.CreateAsyncScope());
            if (createSchema)
            {
                await fixture.Db.Database.EnsureCreatedAsync();
                Assert.True((await fixture.Roles.CreateAsync(new IdentityRole("Customer"))).Succeeded);
                if (includeAdminRole)
                {
                    Assert.True((await fixture.Roles.CreateAsync(new IdentityRole("Admin"))).Succeeded);
                }
            }
            return fixture;
        }

        public async Task<ApplicationUser> CreateCustomerAsync(string email)
        {
            var user = new ApplicationUser { Email = email, UserName = email, FullName = "Isolated identity test" };
            Assert.True((await Users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await Users.AddToRoleAsync(user, "Customer")).Succeeded);
            return user;
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "IsolatedFirstAdminTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FailAdminAfterSaveUserManager(
        IUserStore<ApplicationUser> store,
        IOptions<IdentityOptions> options,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IEnumerable<IUserValidator<ApplicationUser>> userValidators,
        IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
        ILookupNormalizer normalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<ApplicationUser>> logger)
        : UserManager<ApplicationUser>(store, options, passwordHasher, userValidators,
            passwordValidators, normalizer, errors, services, logger)
    {
        public override async Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role)
        {
            var result = await base.AddToRoleAsync(user, role);
            return role == "Admin" && result.Succeeded
                ? IdentityResult.Failed(new IdentityError
                {
                    Code = "SimulatedFailureAfterSave",
                    Description = "private-test-token must not be printed"
                })
                : result;
        }
    }
}
