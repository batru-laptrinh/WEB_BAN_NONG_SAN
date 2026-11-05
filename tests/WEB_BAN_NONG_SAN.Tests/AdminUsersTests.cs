using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
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
using Microsoft.Extensions.Options;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;
using Xunit;

namespace WEB_BAN_NONG_SAN.Tests;

// Exercise MVC authorization, real Identity cookies, antiforgery and transactions.
// Each test owns a new SQLite :memory: database; application SQL settings are never used.
public sealed class AdminUsersTests
{
    [Theory]
    [InlineData("/Admin/Users")]
    [InlineData("/Admin/Users/Details/missing")]
    [InlineData("/Admin/Users/Confirm/missing?operation=lock")]
    public async Task AnonymousRequestsRequireLogin(string path)
    {
        await using var app = new UsersApplication();
        using var client = app.NewClient();
        AssertRedirect(await client.GetAsync(path), "/Account/Login");
    }

    [Theory]
    [InlineData("/Admin/Users")]
    [InlineData("/Admin/Users/Details/missing")]
    [InlineData("/Admin/Users/Confirm/missing?operation=lock")]
    public async Task CustomerCannotReachAdministration(string path)
    {
        await using var app = new UsersApplication();
        using var client = app.NewClient();
        await app.CreateUserAsync("customer@example.invalid");
        await app.LoginAsync(client, "customer@example.invalid");
        AssertRedirect(await client.GetAsync(path), "/Account/AccessDenied");
    }

    [Theory]
    [InlineData(false, "Lock")]
    [InlineData(false, "Unlock")]
    [InlineData(true, "Lock")]
    [InlineData(true, "Unlock")]
    public async Task NonAdminCannotPostEvenWithAValidAntiforgeryToken(bool authenticated, string operation)
    {
        await using var app = new UsersApplication();
        using var client = app.NewClient();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        if (authenticated) await app.LoginAsync(client, "buyer@example.invalid");
        var csrf = Hidden(await client.GetStringAsync(authenticated ? "/Profile" : "/Account/Login"),
            "__RequestVerificationToken");
        var before = await app.ReadAsync(target);

        var response = await client.PostAsync($"/Admin/Users/{operation}/{target}", Form(
            ("__RequestVerificationToken", csrf), ("confirmation", "invalid")));

        AssertRedirect(response, authenticated ? "/Account/AccessDenied" : "/Account/Login");
        Assert.Equal(before, await app.ReadAsync(target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminAndDualRoleAdminCanReadCustomerMembership(bool customer)
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync(customer);
        var buyer = await app.CreateUserAsync("buyer@example.invalid");
        var dual = await app.CreateUserAsync("dual@example.invalid", admin: true);
        await app.CreateUserAsync("not-customer@example.invalid", customer: false);

        var response = await client.GetAsync("/Admin/Users");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(buyer, RowIds(html));
        Assert.Contains(dual, RowIds(html));
        Assert.DoesNotContain("not-customer@example.invalid", html);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("NgUyEn", "Nguyen Thi Hoa", "hoa@example.invalid", "0901112223")]
    [InlineData("BUYER", "Name without match", "buyer@example.invalid", "0901112223")]
    [InlineData("111222", "Name without match", "phone@example.invalid", "0901112223")]
    public async Task SearchUsesActualNameEmailAndPhoneIgnoringAsciiCase(
        string search, string name, string email, string phone)
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var match = await app.CreateUserAsync(email, name: name, phone: phone);
        var other = await app.CreateUserAsync("other@example.invalid", name: "Other", phone: "0987654321");

        var html = await client.GetStringAsync("/Admin/Users?search=" + Uri.EscapeDataString(search));

        Assert.Equal(new[] { match }, RowIds(html));
        Assert.DoesNotContain(other, RowIds(html));
        Assert.Contains(WebUtility.HtmlEncode(search), html);
    }

    [Fact]
    public async Task SearchHasRealEmptyResultsAndTreatsMarkupAsText()
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        await app.CreateUserAsync("buyer@example.invalid");
        const string search = "<script>alert('x')</script>";

        var html = await client.GetStringAsync("/Admin/Users?search=" + Uri.EscapeDataString(search));

        Assert.Empty(RowIds(html));
        Assert.DoesNotContain(search, html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task SearchIsTrimmedAndLimitedOnTheServer()
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var supportedSearch = new string('a', 100);
        var target = await app.CreateUserAsync("buyer@example.invalid", name: supportedSearch);

        var html = await client.GetStringAsync("/Admin/Users?search=" +
            Uri.EscapeDataString("  " + supportedSearch + "a  "));

        Assert.Equal(new[] { target }, RowIds(html));
        Assert.Contains($"value=\"{supportedSearch}\"", html);
        Assert.DoesNotContain(supportedSearch + "a", html);
    }

    [Fact]
    public async Task PagingIsStableHasTenRowsAndKeepsTheFilter()
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var expected = new List<string>();
        for (var index = 0; index < 23; index++)
        {
            expected.Add(await app.CreateUserAsync($"buyer{index:00}@example.invalid"));
        }
        await app.CreateUserAsync("excluded@example.invalid");

        var first = await client.GetStringAsync("/Admin/Users?search=BUYER&page=1");
        var second = await client.GetStringAsync("/Admin/Users?search=BUYER&page=2");
        var last = await client.GetStringAsync("/Admin/Users?search=BUYER&page=999");
        var negative = await client.GetStringAsync("/Admin/Users?search=BUYER&page=-8");

        Assert.Equal(expected.Take(10), RowIds(first));
        Assert.Equal(expected.Skip(10).Take(10), RowIds(second));
        Assert.Equal(expected.Skip(20), RowIds(last));
        Assert.Equal(RowIds(first), RowIds(negative));
        Assert.Empty(RowIds(first).Intersect(RowIds(second)));
        Assert.Matches("href=\"[^\"]*search=BUYER[^\"]*page=2|href=\"[^\"]*page=2[^\"]*search=BUYER", first);
    }

    [Fact]
    public async Task DetailsShowOnlyCustomerInformationWithoutIdentitySecrets()
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid", name: "Buyer name", phone: "0901112223");
        var snapshot = await app.ReadAsync(target);

        var response = await client.GetAsync(Details(target));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("buyer@example.invalid", html);
        Assert.Contains("Buyer name", html);
        Assert.Contains("0901112223", html);
        Assert.Contains("Customer", html);
        AssertNoIdentitySecrets(snapshot, html);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task MissingAndNonCustomerDetailsReturnNotFound()
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var withoutRole = await app.CreateUserAsync("without-role@example.invalid", customer: false);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Details("missing"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Details(withoutRole))).StatusCode);
    }

    [Theory]
    [InlineData("Lock")]
    [InlineData("Unlock")]
    public async Task GetCannotChangeLockout(string operation)
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        var before = await app.ReadAsync(target);

        var response = await client.GetAsync($"/Admin/Users/{operation}/{target}");
        Assert.True(response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound,
            "GET must not select a state-changing action.");
        Assert.Equal(before, await app.ReadAsync(target));
    }

    [Theory]
    [InlineData("Lock")]
    [InlineData("Unlock")]
    public async Task PostRequiresAntiforgery(string operation)
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        var before = await app.ReadAsync(target);

        var response = await client.PostAsync($"/Admin/Users/{operation}/{target}", Form(("confirmation", "invalid")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await app.ReadAsync(target));
    }

    [Fact]
    public async Task LockPreservesIdentityAndProfileBlocksLoginAndMarksManualLock()
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid", name: "Preserved buyer", phone: "0901112223");
        await app.MutateAsync(target, async (users, user) =>
            Assert.True((await users.SetLockoutEnabledAsync(user, false)).Succeeded));
        var before = await app.ReadAsync(target);
        var form = await GetConfirmationAsync(client, target, "lock");

        var response = await SubmitAsync(client, target, "Lock", form);

        AssertRedirect(response, Details(target));
        var after = await app.ReadAsync(target);
        Assert.True(after.LockoutEnabled);
        Assert.Equal(DateTimeOffset.MaxValue, after.LockoutEnd);
        Assert.False(before.SecurityStamp == after.SecurityStamp, "Lock must replace the security stamp.");
        AssertPreservedIdentity(before, after);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(Details(target)));
        Assert.Contains("Khóa thủ công", html);
        using var buyer = app.NewClient();
        var login = await app.TryLoginAsync(buyer, "buyer@example.invalid");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AssertRedirect(await buyer.GetAsync("/Profile"), "/Account/Login");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnlockHandlesManualAndTemporaryLocksAndRestoresLogin(bool manual)
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        await app.MutateAsync(target, async (users, user) =>
        {
            Assert.True((await users.SetLockoutEndDateAsync(user,
                manual ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddMinutes(4))).Succeeded);
            Assert.True((await users.AccessFailedAsync(user)).Succeeded);
        });
        var before = await app.ReadAsync(target);
        var details = WebUtility.HtmlDecode(await client.GetStringAsync(Details(target)));
        Assert.Contains(manual ? "Khóa thủ công" : "Khóa tạm thời", details);
        var form = await GetConfirmationAsync(client, target, "unlock");

        AssertRedirect(await SubmitAsync(client, target, "Unlock", form), Details(target));

        var after = await app.ReadAsync(target);
        Assert.Null(after.LockoutEnd);
        Assert.Equal(0, after.AccessFailedCount);
        Assert.False(before.SecurityStamp == after.SecurityStamp, "Unlock must replace the security stamp.");
        AssertPreservedIdentity(before, after);
        using var buyer = app.NewClient();
        await app.LoginAsync(buyer, "buyer@example.invalid");
        Assert.Equal(HttpStatusCode.OK, (await buyer.GetAsync("/Profile")).StatusCode);
    }

    [Theory]
    [InlineData("lock", false, false)]
    [InlineData("lock", true, false)]
    [InlineData("unlock", true, false)]
    [InlineData("lock", true, true)]
    [InlineData("unlock", true, true)]
    public async Task AdminAndSelfTargetsAreProtectedInBothUiAndDirectPost(
        string operation, bool customer, bool self)
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync(customer: self);
        var target = self
            ? await app.IdForEmailAsync("admin@example.invalid")
            : await app.CreateUserAsync("other-admin@example.invalid", admin: true, customer: customer);
        var buyer = await app.CreateUserAsync("buyer@example.invalid");
        var ordinaryForm = await GetConfirmationAsync(client, buyer, "lock");
        var before = await app.ReadAsync(target);

        var confirmationResponse = await client.GetAsync($"/Admin/Users/Confirm/{target}?operation={operation}");
        if (customer || self)
            AssertRedirect(confirmationResponse, "/Account/AccessDenied");
        else
            Assert.Equal(HttpStatusCode.NotFound, confirmationResponse.StatusCode);
        var response = await SubmitAsync(client, target, operation, ordinaryForm);

        // An unrelated account-bound ticket is rejected before any account write.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await app.ReadAsync(target));
    }

    [Fact]
    public async Task ConfirmationIsBoundToAccountOperationAndActor()
    {
        await using var app = new UsersApplication();
        using var firstAdmin = await app.AdminClientAsync();
        using var secondAdmin = app.NewClient();
        await app.CreateUserAsync("second-admin@example.invalid", admin: true, customer: false);
        await app.LoginAsync(secondAdmin, "second-admin@example.invalid");
        var target = await app.CreateUserAsync("buyer@example.invalid");
        var other = await app.CreateUserAsync("other@example.invalid");
        var firstForm = await GetConfirmationAsync(firstAdmin, target, "lock");
        var secondForm = await GetConfirmationAsync(secondAdmin, target, "lock");
        var before = await app.ReadAsync(target);
        var otherBefore = await app.ReadAsync(other);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SubmitAsync(firstAdmin, other, "Lock", firstForm)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SubmitAsync(firstAdmin, target, "Unlock", firstForm)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SubmitAsync(secondAdmin, target, "Lock", secondForm with { Confirmation = firstForm.Confirmation })).StatusCode);
        Assert.Equal(before, await app.ReadAsync(target));
        Assert.Equal(otherBefore, await app.ReadAsync(other));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-protected-confirmation")]
    public async Task MissingOrTamperedConfirmationCannotMutateAccount(string confirmation)
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        var form = await GetConfirmationAsync(client, target, "lock");
        var before = await app.ReadAsync(target);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SubmitAsync(client, target, "Lock", form with { Confirmation = confirmation })).StatusCode);
        Assert.Equal(before, await app.ReadAsync(target));
    }

    [Fact]
    public async Task ModifiedAccountMakesOldConfirmationStale()
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        var form = await GetConfirmationAsync(client, target, "lock");
        await app.MutateAsync(target, async (users, user) =>
            Assert.True((await users.SetPhoneNumberAsync(user, "0981112223")).Succeeded));
        var before = await app.ReadAsync(target);

        AssertRedirect(await SubmitAsync(client, target, "Lock", form), Details(target));
        Assert.Equal(before, await app.ReadAsync(target));
    }

    [Theory]
    [InlineData("lock")]
    [InlineData("unlock")]
    public async Task CustomerPromotedAfterConfirmationCannotBeChanged(string operation)
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        if (operation == "unlock")
            await app.MutateAsync(target, async (users, user) =>
                Assert.True((await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue)).Succeeded));
        var form = await GetConfirmationAsync(client, target, operation);
        await app.MutateAsync(target, async (users, user) =>
            Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded));
        var before = await app.ReadAsync(target);

        AssertRedirect(await SubmitAsync(client, target, operation, form), "/Account/AccessDenied");
        Assert.Equal(before, await app.ReadAsync(target));
    }

    [Fact]
    public async Task DuplicatePostDoesNotRepeatSecurityStampChange()
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        var form = await GetConfirmationAsync(client, target, "lock");
        AssertRedirect(await SubmitAsync(client, target, "Lock", form), Details(target));
        var afterFirst = await app.ReadAsync(target);

        AssertRedirect(await SubmitAsync(client, target, "Lock", form), Details(target));
        Assert.Equal(afterFirst, await app.ReadAsync(target));
    }

    [Fact]
    public async Task DeletedCustomerAndMissingCustomerReturnNotFoundWithoutMutation()
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        var form = await GetConfirmationAsync(client, target, "lock");
        await app.MutateAsync(target, async (users, user) => Assert.True((await users.DeleteAsync(user)).Succeeded));

        Assert.Equal(HttpStatusCode.NotFound, (await SubmitAsync(client, target, "Lock", form)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            "/Admin/Users/Confirm/missing?operation=lock")).StatusCode);
    }

    [Fact]
    public async Task RemovedCustomerMembershipInvalidatesAnOutstandingConfirmation()
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        var form = await GetConfirmationAsync(client, target, "lock");
        await app.MutateAsync(target, async (users, user) =>
            Assert.True((await users.RemoveFromRoleAsync(user, "Customer")).Succeeded));
        var before = await app.ReadAsync(target);

        Assert.Equal(HttpStatusCode.NotFound, (await SubmitAsync(client, target, "Lock", form)).StatusCode);
        Assert.Equal(before, await app.ReadAsync(target));
    }

    [Fact]
    public async Task ActorMustStillHaveAdminMembershipWhenPostingAnOldForm()
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        var form = await GetConfirmationAsync(client, target, "lock");
        var actor = await app.IdForEmailAsync("admin@example.invalid");
        await app.MutateAsync(actor, async (users, user) =>
            Assert.True((await users.RemoveFromRoleAsync(user, "Admin")).Succeeded));
        var before = await app.ReadAsync(target);

        AssertRedirect(await SubmitAsync(client, target, "Lock", form), "/Account/AccessDenied");
        Assert.Equal(before, await app.ReadAsync(target));
    }

    [Theory]
    [InlineData("lock")]
    [InlineData("unlock")]
    public async Task FailureAfterSavingSecurityStampRollsBackEveryIdentityWrite(string operation)
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        await app.MutateAsync(target, async (users, user) =>
        {
            if (operation == "lock")
                Assert.True((await users.SetLockoutEnabledAsync(user, false)).Succeeded);
            else
            {
                Assert.True((await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue)).Succeeded);
                Assert.True((await users.AccessFailedAsync(user)).Succeeded);
            }
        });
        var form = await GetConfirmationAsync(client, target, operation);
        var before = await app.ReadAsync(target);
        app.Failures.FailAfterSecurityStampSave = true;

        AssertRedirect(await SubmitAsync(client, target, operation, form), Details(target));

        Assert.Equal(before, await app.ReadAsync(target));
        var html = await client.GetStringAsync(Details(target));
        Assert.DoesNotContain(FailureSwitch.PrivateFailureMarker, html);
        AssertNoIdentitySecrets(before, html);
        Assert.DoesNotContain("SqliteException", html);
    }

    [Fact]
    public async Task ConfirmationExpiresAfterTenMinutesAndAnExpiredTicketCannotBeSubmitted()
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        var started = DateTimeOffset.UtcNow;
        var form = await GetConfirmationAsync(client, target, "lock");
        var protector = app.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("WEB_BAN_NONG_SAN.AdminUsers.LockoutConfirmation.v1")
            .ToTimeLimitedDataProtector();
        var protectedPayload = protector.Unprotect(form.Confirmation, out var expiration);
        Assert.InRange(expiration - started, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(11));
        // Retain the real server-issued payload and protection purpose, changing only expiry.
        var expired = protector.Protect(protectedPayload, DateTimeOffset.UtcNow.AddMinutes(-1));
        var before = await app.ReadAsync(target);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SubmitAsync(client, target, "Lock", form with { Confirmation = expired })).StatusCode);
        Assert.Equal(before, await app.ReadAsync(target));
    }

    [Fact]
    public async Task ReviewingAndCancellingConfirmationDoesNotChangeAccount()
    {
        await using var app = new UsersApplication();
        using var client = await app.AdminClientAsync();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        var before = await app.ReadAsync(target);

        await GetConfirmationAsync(client, target, "lock");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Details(target))).StatusCode);

        Assert.Equal(before, await app.ReadAsync(target));
    }

    [Fact]
    public async Task ExistingCustomerCookieIsRejectedAtNextFiveMinuteStampValidation()
    {
        await using var app = new UsersApplication();
        using var admin = await app.AdminClientAsync();
        using var buyer = app.NewClient();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        await app.LoginAsync(buyer, "buyer@example.invalid");
        Assert.Equal(TimeSpan.FromMinutes(5), app.Services.GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value.ValidationInterval);
        var form = await GetConfirmationAsync(admin, target, "lock");
        AssertRedirect(await SubmitAsync(admin, target, "Lock", form), Details(target));

        // Cookie validation is periodic; the unchanged old cookie is still accepted immediately.
        Assert.Equal(HttpStatusCode.OK, (await buyer.GetAsync("/Profile")).StatusCode);
        app.Clock.Advance(TimeSpan.FromMinutes(6));

        AssertRedirect(await buyer.GetAsync("/Profile"), "/Account/Login");
    }

    [Fact]
    public async Task LockedCustomerCannotRefreshAnOldCookieByChangingPassword()
    {
        await using var app = new UsersApplication();
        using var admin = await app.AdminClientAsync();
        using var buyer = app.NewClient();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        await app.LoginAsync(buyer, "buyer@example.invalid");
        var form = await GetConfirmationAsync(admin, target, "lock");
        AssertRedirect(await SubmitAsync(admin, target, "Lock", form), Details(target));
        var locked = await app.ReadAsync(target);

        // The old cookie still passes periodic validation during this window.
        // A password change must not turn it into a fresh authenticated session.
        var passwordChange = await app.ChangePasswordAsync(buyer, $"{Guid.NewGuid():N}!aA1");
        app.Clock.Advance(TimeSpan.FromMinutes(6));

        AssertRedirect(await buyer.GetAsync("/Profile"), "/Account/Login");
        AssertRedirect(passwordChange, "/Account/Login");
        Assert.Equal(locked, await app.ReadAsync(target));
    }

    [Fact]
    public async Task ActiveCustomerCanStillChangePasswordAndKeepAValidSession()
    {
        await using var app = new UsersApplication();
        using var buyer = app.NewClient();
        var target = await app.CreateUserAsync("buyer@example.invalid");
        await app.LoginAsync(buyer, "buyer@example.invalid");
        var before = await app.ReadAsync(target);
        var newPassword = $"{Guid.NewGuid():N}!aA1";

        AssertRedirect(await app.ChangePasswordAsync(buyer, newPassword), "/Account/ChangePassword");
        var after = await app.ReadAsync(target);
        Assert.False(before.PasswordHash == after.PasswordHash, "A successful password change must update its hash.");
        Assert.Equal(before.Roles, after.Roles);
        Assert.Null(after.LockoutEnd);
        app.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(HttpStatusCode.OK, (await buyer.GetAsync("/Profile")).StatusCode);
        using var newSession = app.NewClient();
        AssertRedirect(await app.TryLoginAsync(newSession, "buyer@example.invalid", newPassword), "/");
        Assert.Equal(HttpStatusCode.OK, (await newSession.GetAsync("/Profile")).StatusCode);
    }

    private static string Details(string id) => $"/Admin/Users/Details/{id}";

    private static string[] RowIds(string html) => Regex.Matches(html, "data-user-id=\"([^\"]+)\"")
        .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value)).ToArray();

    private static void AssertPreservedIdentity(UserSnapshot before, UserSnapshot after)
    {
        Assert.True(before.PasswordHash == after.PasswordHash, "The password hash must be preserved.");
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.UserName, after.UserName);
        Assert.Equal(before.FullName, after.FullName);
        Assert.Equal(before.PhoneNumber, after.PhoneNumber);
        Assert.Equal(before.Roles, after.Roles);
        Assert.Contains("Customer", after.Roles);
        Assert.DoesNotContain("Admin", after.Roles);
    }

    private static void AssertNoIdentitySecrets(UserSnapshot snapshot, string html)
    {
        foreach (var secret in new[] { snapshot.PasswordHash, snapshot.SecurityStamp, snapshot.ConcurrencyStamp })
        {
            Assert.False(!string.IsNullOrEmpty(secret) && html.Contains(secret, StringComparison.Ordinal),
                "Sensitive Identity information must stay out of rendered HTML.");
        }
    }

    private static async Task<ConfirmationForm> GetConfirmationAsync(HttpClient client, string id, string operation)
    {
        var response = await client.GetAsync($"/Admin/Users/Confirm/{id}?operation={operation}&search=buyer&page=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        return new ConfirmationForm(Hidden(html, "__RequestVerificationToken"), Hidden(html, "confirmation"));
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, string id, string operation, ConfirmationForm form) =>
        client.PostAsync($"/Admin/Users/{operation}/{id}", Form(
            ("__RequestVerificationToken", form.Antiforgery), ("confirmation", form.Confirmation),
            ("search", "buyer"), ("page", "2")));

    private static string Hidden(string html, string name)
    {
        var match = Regex.Match(html, $"<input\\b(?=[^>]*\\bname=\"{Regex.Escape(name)}\")(?=[^>]*\\bvalue=\"([^\"]*)\")[^>]*>");
        Assert.True(match.Success, $"Expected rendered form field '{name}'.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(field => new KeyValuePair<string, string>(field.Key, field.Value)));

    private static void AssertRedirect(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        var actual = location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
        Assert.True(actual == path || actual.StartsWith(path + "?", StringComparison.Ordinal),
            $"Expected redirect to {path}.");
    }

    private sealed record ConfirmationForm(string Antiforgery, string Confirmation);

    private sealed record UserSnapshot(string? Email, string? UserName, string? FullName, string? PhoneNumber,
        string? PasswordHash, string? SecurityStamp, string? ConcurrencyStamp,
        bool LockoutEnabled, DateTimeOffset? LockoutEnd, int AccessFailedCount, string[] Roles)
    {
        // Array equality must compare membership, not freshly materialized array references.
        public bool Equals(UserSnapshot? other) => other is not null &&
            Email == other.Email && UserName == other.UserName && FullName == other.FullName &&
            PhoneNumber == other.PhoneNumber && PasswordHash == other.PasswordHash &&
            SecurityStamp == other.SecurityStamp && ConcurrencyStamp == other.ConcurrencyStamp &&
            LockoutEnabled == other.LockoutEnabled && LockoutEnd == other.LockoutEnd &&
            AccessFailedCount == other.AccessFailedCount && Roles.SequenceEqual(other.Roles);

        public override int GetHashCode() => HashCode.Combine(Email, SecurityStamp, ConcurrencyStamp);
        public override string ToString() => "Identity snapshot (sensitive fields omitted)";
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset current = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan amount) => current += amount;
    }

    private sealed class FailureSwitch
    {
        public const string PrivateFailureMarker = "private-test-only-identity-failure";
        public bool FailAfterSecurityStampSave { get; set; }
    }

    private sealed class FailingStampUserManager(
        IUserStore<ApplicationUser> store, IOptions<IdentityOptions> options,
        IPasswordHasher<ApplicationUser> hasher, IEnumerable<IUserValidator<ApplicationUser>> userValidators,
        IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators, ILookupNormalizer normalizer,
        IdentityErrorDescriber errors, IServiceProvider services, ILogger<UserManager<ApplicationUser>> logger,
        FailureSwitch failures)
        : UserManager<ApplicationUser>(store, options, hasher, userValidators, passwordValidators,
            normalizer, errors, services, logger)
    {
        public override async Task<IdentityResult> UpdateSecurityStampAsync(ApplicationUser user)
        {
            var result = await base.UpdateSecurityStampAsync(user);
            return result.Succeeded && failures.FailAfterSecurityStampSave
                ? IdentityResult.Failed(new IdentityError { Code = "InjectedFailure", Description = FailureSwitch.PrivateFailureMarker })
                : result;
        }
    }

    private sealed class UsersApplication : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private string Password { get; } = $"{Guid.NewGuid():N}!aA1";
        public MutableClock Clock { get; } = new();
        public FailureSwitch Failures { get; } = new();

        public UsersApplication()
        {
            connection.Open();
            using var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
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
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
                services.AddSingleton(Failures);
                services.RemoveAll<UserManager<ApplicationUser>>();
                services.AddScoped<UserManager<ApplicationUser>, FailingStampUserManager>();
                services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme,
                    options => options.TimeProvider = Clock);
                services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = Clock);
            });
        }

        public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost")
        });

        public async Task<HttpClient> AdminClientAsync(bool customer = false)
        {
            var client = NewClient();
            await CreateUserAsync("admin@example.invalid", admin: true, customer: customer);
            await LoginAsync(client, "admin@example.invalid");
            return client;
        }

        public async Task<string> CreateUserAsync(string email, bool admin = false, bool customer = true,
            string name = "Isolated customer test", string phone = "")
        {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, FullName = name, PhoneNumber = phone };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            if (customer) Assert.True((await users.AddToRoleAsync(user, "Customer")).Succeeded);
            if (admin) Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
            return user.Id;
        }

        public async Task<string> IdForEmailAsync(string email)
        {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            return (await users.FindByEmailAsync(email))!.Id;
        }

        public async Task MutateAsync(string id, Func<UserManager<ApplicationUser>, ApplicationUser, Task> action)
        {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await action(users, (await users.FindByIdAsync(id))!);
        }

        public async Task<UserSnapshot> ReadAsync(string id)
        {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(id))!;
            return new UserSnapshot(user.Email, user.UserName, user.FullName, user.PhoneNumber, user.PasswordHash,
                user.SecurityStamp, user.ConcurrencyStamp, user.LockoutEnabled, user.LockoutEnd,
                user.AccessFailedCount, (await users.GetRolesAsync(user)).Order().ToArray());
        }

        public async Task<HttpResponseMessage> TryLoginAsync(HttpClient client, string email, string? password = null)
        {
            var csrf = Hidden(await client.GetStringAsync("/Account/Login"), "__RequestVerificationToken");
            return await client.PostAsync("/Account/Login", Form(
                ("__RequestVerificationToken", csrf), ("Email", email), ("Password", password ?? Password)));
        }

        public async Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string newPassword)
        {
            var csrf = Hidden(await client.GetStringAsync("/Account/ChangePassword"), "__RequestVerificationToken");
            return await client.PostAsync("/Account/ChangePassword", Form(
                ("__RequestVerificationToken", csrf), ("CurrentPassword", Password),
                ("NewPassword", newPassword), ("ConfirmNewPassword", newPassword)));
        }

        public async Task LoginAsync(HttpClient client, string email) =>
            AssertRedirect(await TryLoginAsync(client, email), "/");

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
