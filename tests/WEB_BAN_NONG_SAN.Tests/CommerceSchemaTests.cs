using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Data.Migrations;
using WEB_BAN_NONG_SAN.Models;
using Xunit;

namespace WEB_BAN_NONG_SAN.Tests;

// SQL Server metadata/script tests never open a database connection.
public sealed class CommerceSchemaTests
{
    private static ApplicationDbContext MetadataContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=MetadataOnly;Integrated Security=true;Connect Timeout=1")
            .Options);

    [Fact]
    public void AdditiveMigrationCreatesEightTablesWithoutAlteringIdentityOrSeeding()
    {
        var operations = new AddCommerce().UpOperations;
        Assert.All(operations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation));
        var tables = operations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "CartItems", "Categories", "CouponUsages", "Coupons", "OrderItems", "Orders", "Products", "ShippingAddresses" },
            tables.Select(x => x.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(tables, table => table.Name.StartsWith("AspNet", StringComparison.Ordinal));
        Assert.All(tables.SelectMany(table => table.ForeignKeys), fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        Assert.All(tables.SelectMany(table => table.Columns).Where(column => column.ClrType == typeof(decimal)),
            column => { Assert.Equal(18, column.Precision); Assert.Equal(2, column.Scale); });
    }

    [Fact]
    public void DefaultAndIdempotencyHaveDatabaseUniquenessAndVersionsProtectWrites()
    {
        using var db = MetadataContext();
        var address = db.Model.FindEntityType(typeof(ShippingAddress))!;
        Assert.Contains(address.GetIndexes(), index => index.IsUnique && index.GetFilter() == "[IsDefault] = 1"
            && index.Properties.Select(property => property.Name).SequenceEqual(new[] { "UserId" }));
        var order = db.Model.FindEntityType(typeof(Order))!;
        Assert.Contains(order.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(new[] { "UserId", "CheckoutKey" }));
        foreach (var type in new[] { typeof(ShippingAddress), typeof(Category), typeof(Product), typeof(CartItem), typeof(Order), typeof(Coupon) })
            Assert.True(db.Model.FindEntityType(type)!.FindProperty("Version")!.IsConcurrencyToken);
    }

    [Fact]
    public void SnapshotMatchesCurrentSqlServerModelWithoutConnecting()
    {
        using var db = MetadataContext();
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void GeneratedSqlServerScriptOnlyAddsCommerceTablesAndConstraints()
    {
        using var db = MetadataContext();
        var script = db.GetService<IMigrator>().GenerateScript("20261105050811_InitialIdentity", "20261105155910_AddCommerce");
        Assert.Contains("CREATE TABLE [ShippingAddresses]", script);
        Assert.Contains("CREATE UNIQUE INDEX [UX_ShippingAddresses_DefaultPerUser]", script);
        Assert.Contains("WHERE [IsDefault] = 1", script);
        Assert.Contains("decimal(18,2)", script);
        Assert.Contains("REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION", script);
        Assert.DoesNotContain("ALTER TABLE [AspNet", script);
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.DoesNotContain("UPDATE [AspNet", script);
        Assert.DoesNotContain("INSERT INTO [AspNet", script);
        Assert.DoesNotContain("PasswordHash", script);
    }
}
