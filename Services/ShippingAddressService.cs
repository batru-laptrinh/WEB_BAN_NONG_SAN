using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Services;

public enum AddressOperationStatus { Success, NotFound, Invalid, Conflict, Failure }
public sealed record AddressOperationResult(AddressOperationStatus Status, string Message);

public sealed class ShippingAddressService(
    ApplicationDbContext db, IDataProtectionProvider protection, ILogger<ShippingAddressService> logger)
{
    public const int MaximumAddressesPerUser = 50;
    private readonly ITimeLimitedDataProtector tickets = protection
        .CreateProtector("WEB_BAN_NONG_SAN.ShippingAddresses.v1").ToTimeLimitedDataProtector();

    public async Task<AddressIndexViewModel> GetListAsync(string ownerId, CancellationToken ct = default)
    {
        try
        {
            var rows = await db.ShippingAddresses.AsNoTracking().Where(a => a.UserId == ownerId)
                .OrderByDescending(a => a.IsDefault).ThenBy(a => a.Id).ToListAsync(ct);
            return new() { Items = rows.Select(a => Item(a, ownerId)).ToArray() };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Shipping addresses could not be loaded.");
            return new() { IsDataAvailable = false };
        }
    }

    public async Task<AddressFormViewModel?> GetFormAsync(int id, string ownerId, CancellationToken ct = default)
    {
        var a = await Owned(id, ownerId).AsNoTracking().SingleOrDefaultAsync(ct);
        return a is null ? null : new()
        {
            RecipientName = a.RecipientName, PhoneNumber = a.PhoneNumber,
            Province = a.Province, Ward = a.Ward, District = a.District,
            AddressLine = a.AddressLine, DeliveryNote = a.DeliveryNote, Label = a.Label,
            IsDefault = a.IsDefault, IsCurrentDefault = a.IsDefault,
            VersionToken = Ticket(a, ownerId, "edit")
        };
    }

    public async Task<AddressDeleteViewModel?> GetDeleteAsync(int id, string ownerId, CancellationToken ct = default)
    {
        var a = await Owned(id, ownerId).AsNoTracking().SingleOrDefaultAsync(ct);
        return a is null ? null : new() { Address = Item(a, ownerId), Confirmation = Ticket(a, ownerId, "delete") };
    }

    public Task<AddressOperationResult> CreateAsync(AddressFormViewModel model, string ownerId, CancellationToken ct = default) =>
        ChangeAsync(null, "create", null, model, ownerId, ct);

    public Task<AddressOperationResult> EditAsync(int id, AddressFormViewModel model, string ownerId, CancellationToken ct = default) =>
        ChangeAsync(id, "edit", model.VersionToken, model, ownerId, ct);

    public Task<AddressOperationResult> DeleteAsync(int id, string? confirmation, string ownerId, CancellationToken ct = default) =>
        ChangeAsync(id, "delete", confirmation, null, ownerId, ct);

    public Task<AddressOperationResult> SetDefaultAsync(int id, string? confirmation, string ownerId, CancellationToken ct = default) =>
        ChangeAsync(id, "default", confirmation, null, ownerId, ct);

    private async Task<AddressOperationResult> ChangeAsync(int? id, string action, string? token,
        AddressFormViewModel? form, string ownerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) return NotFound();
        if (form is not null && !Validator.TryValidateObject(form, new ValidationContext(form), [], true))
            return new(AddressOperationStatus.Invalid, "Vui lòng kiểm tra thông tin địa chỉ.");

        try
        {
            // Serializable protects the owner range, including an empty address
            // book. A filtered unique index provides the final default invariant.
            // A competing request may be the deadlock victim and must retry with
            // fresh form data; it must never partially change default addresses.
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var addresses = await db.ShippingAddresses.Where(a => a.UserId == ownerId)
                .OrderBy(a => a.Id).ToListAsync(ct);
            if (action == "create" && addresses.Count >= MaximumAddressesPerUser)
                return new(AddressOperationStatus.Invalid,
                    "Bạn có thể lưu tối đa 50 địa chỉ. Hãy chỉnh sửa hoặc xóa một địa chỉ trước khi thêm mới.");
            var target = id.HasValue ? addresses.SingleOrDefault(a => a.Id == id.Value) : null;
            if (id.HasValue)
            {
                if (target is null) return NotFound();
                if (!ValidTicket(token, target, ownerId, action)) return Conflict();
            }

            if (action == "delete")
            {
                var wasDefault = target!.IsDefault;
                db.ShippingAddresses.Remove(target);
                await db.SaveChangesAsync(ct);
                if (wasDefault)
                {
                    var replacement = addresses.FirstOrDefault(a => a.Id != target.Id);
                    if (replacement is not null)
                    {
                        replacement.IsDefault = true;
                        replacement.Version = NewVersion();
                        await db.SaveChangesAsync(ct);
                    }
                }
            }
            else if (action == "default")
            {
                if (!target!.IsDefault)
                {
                    await ClearDefaultAsync(addresses, ct);
                    target.IsDefault = true;
                    target.Version = NewVersion();
                    await db.SaveChangesAsync(ct);
                }
            }
            else
            {
                var makeDefault = addresses.Count == 0 || form!.IsDefault || target?.IsDefault == true;
                if (makeDefault && target?.IsDefault != true)
                    await ClearDefaultAsync(addresses, ct);
                if (target is null)
                {
                    target = new ShippingAddress { UserId = ownerId };
                    db.ShippingAddresses.Add(target);
                }
                Copy(form!, target);
                target.IsDefault = makeDefault;
                target.Version = NewVersion();
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
            return new(AddressOperationStatus.Success, action switch
            {
                "create" => "Đã thêm địa chỉ giao hàng.",
                "edit" => "Đã cập nhật địa chỉ giao hàng.",
                "delete" => "Đã xóa địa chỉ. Nếu cần, địa chỉ còn lại đầu tiên đã được chọn mặc định.",
                _ => "Đã thay đổi địa chỉ mặc định."
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DetachAddresses();
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            DetachAddresses();
            return Conflict();
        }
        catch (Exception)
        {
            DetachAddresses();
            // Deliberately omit addresses, user IDs, token values, SQL and raw
            // exception text; a failed commit can leave the outcome uncertain.
            logger.LogWarning("Shipping address operation could not confirm its outcome.");
            return new(AddressOperationStatus.Failure,
                "Chưa xác nhận được kết quả. Hãy tải lại danh sách địa chỉ trước khi thử lại.");
        }
    }

    private async Task ClearDefaultAsync(IEnumerable<ShippingAddress> addresses, CancellationToken ct)
    {
        foreach (var address in addresses.Where(a => a.IsDefault))
        {
            address.IsDefault = false;
            address.Version = NewVersion();
        }
        // Save removal of the old default before INSERT/UPDATE of the new one;
        // SQL Server's filtered unique index is checked per statement.
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<ShippingAddress> Owned(int id, string ownerId) =>
        db.ShippingAddresses.Where(a => a.Id == id && a.UserId == ownerId);

    private AddressItemViewModel Item(ShippingAddress a, string ownerId) => new()
    {
        Id = a.Id, RecipientName = a.RecipientName, PhoneNumber = a.PhoneNumber,
        Province = a.Province, Ward = a.Ward, District = a.District,
        AddressLine = a.AddressLine, Label = a.Label, DeliveryNote = a.DeliveryNote,
        IsDefault = a.IsDefault, DefaultToken = Ticket(a, ownerId, "default")
    };

    private string Ticket(ShippingAddress a, string ownerId, string action) => tickets.Protect(
        JsonSerializer.Serialize(new AddressTicket(a.Id, ownerId, action, a.Version)), TimeSpan.FromMinutes(30));

    private bool ValidTicket(string? token, ShippingAddress a, string ownerId, string action)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192) return false;
        try
        {
            var ticket = JsonSerializer.Deserialize<AddressTicket>(tickets.Unprotect(token));
            return ticket is not null && ticket.Id == a.Id && ticket.OwnerId == ownerId
                && ticket.Action == action && ticket.Version == a.Version;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        { return false; }
    }

    private void DetachAddresses()
    {
        foreach (var entry in db.ChangeTracker.Entries<ShippingAddress>().ToArray())
            entry.State = EntityState.Detached;
    }

    private static void Copy(AddressFormViewModel form, ShippingAddress a)
    {
        a.RecipientName = form.RecipientName.Trim(); a.PhoneNumber = form.PhoneNumber.Trim();
        a.Province = form.Province.Trim(); a.Ward = form.Ward.Trim();
        a.District = Optional(form.District); a.AddressLine = form.AddressLine.Trim();
        a.DeliveryNote = Optional(form.DeliveryNote); a.Label = Optional(form.Label);
    }
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string NewVersion() => Guid.NewGuid().ToString("N");
    private static AddressOperationResult NotFound() => new(AddressOperationStatus.NotFound, "Không tìm thấy địa chỉ.");
    private static AddressOperationResult Conflict() => new(AddressOperationStatus.Conflict,
        "Địa chỉ đã thay đổi hoặc xác nhận đã hết hạn. Hãy tải lại trang trước khi thử lại.");
    private sealed record AddressTicket(int Id, string OwnerId, string Action, string Version);
}
