
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WEB_BAN_NONG_SAN.Models;

namespace WEB_BAN_NONG_SAN.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options
        ) : base(options)
        {
        }

        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<ShippingAddress> ShippingAddresses => Set<ShippingAddress>();
        public DbSet<CartItem> CartItems => Set<CartItem>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();
        public DbSet<Coupon> Coupons => Set<Coupon>();
        public DbSet<CouponUsage> CouponUsages => Set<CouponUsage>();

        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model);

            model.Entity<Category>().Property(x => x.Version).IsConcurrencyToken();
            model.Entity<Category>().HasIndex(x => x.Name);
            model.Entity<Product>(entity =>
            {
                entity.Property(x => x.Version).IsConcurrencyToken();
                entity.Property(x => x.Price).HasPrecision(18, 2);
                entity.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.IsActive, x.CategoryId, x.Id });
                entity.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_Products_Price", "CAST([Price] AS decimal(18,2)) >= 0 AND CAST([Price] AS decimal(18,2)) <= 1000000000");
                    t.HasCheckConstraint("CK_Products_Stock", "[Stock] >= 0 AND [Stock] <= 1000000");
                });
            });
            model.Entity<ShippingAddress>(entity =>
            {
                entity.Property(x => x.Version).IsConcurrencyToken();
                entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.UserId, x.Id });
                entity.HasIndex(x => x.UserId).IsUnique().HasFilter("[IsDefault] = 1").HasDatabaseName("UX_ShippingAddresses_DefaultPerUser");
            });
            model.Entity<CartItem>(entity =>
            {
                entity.Property(x => x.Version).IsConcurrencyToken();
                entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.UserId, x.ProductId }).IsUnique();
                entity.ToTable(t => t.HasCheckConstraint("CK_CartItems_Quantity", "[Quantity] >= 1 AND [Quantity] <= 1000"));
            });
            model.Entity<Order>(entity =>
            {
                entity.Property(x => x.Version).IsConcurrencyToken();
                entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<Coupon>().WithMany().HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.UserId, x.CheckoutKey }).IsUnique();
                entity.HasIndex(x => new { x.UserId, x.CreatedAtUtc, x.Id });
                entity.HasIndex(x => new { x.Status, x.CreatedAtUtc });
                entity.Property(x => x.Subtotal).HasPrecision(18, 2);
                entity.Property(x => x.ShippingFee).HasPrecision(18, 2);
                entity.Property(x => x.Discount).HasPrecision(18, 2);
                entity.Property(x => x.Total).HasPrecision(18, 2);
                entity.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_Orders_Amounts", "CAST([Subtotal] AS decimal(18,2)) >= 0 AND CAST([ShippingFee] AS decimal(18,2)) >= 0 AND CAST([Discount] AS decimal(18,2)) >= 0 AND CAST([Discount] AS decimal(18,2)) <= CAST([Subtotal] AS decimal(18,2)) AND CAST([Total] AS decimal(18,2)) = ROUND(CAST([Subtotal] AS decimal(18,2)) + CAST([ShippingFee] AS decimal(18,2)) - CAST([Discount] AS decimal(18,2)), 2)");
                    t.HasCheckConstraint("CK_Orders_Status", "[Status] >= 0 AND [Status] <= 4 AND [PaymentStatus] >= 0 AND [PaymentStatus] <= 1");
                });
            });
            model.Entity<OrderItem>(entity =>
            {
                entity.HasOne(x => x.Order).WithMany(x => x.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
                entity.Property(x => x.LineTotal).HasPrecision(18, 2);
                entity.HasIndex(x => new { x.OrderId, x.ProductId }).IsUnique();
                entity.ToTable(t => t.HasCheckConstraint("CK_OrderItems_Amounts", "[Quantity] >= 1 AND [Quantity] <= 1000 AND CAST([UnitPrice] AS decimal(18,2)) >= 0 AND CAST([LineTotal] AS decimal(18,2)) = ROUND(CAST([UnitPrice] AS decimal(18,2)) * [Quantity], 2)"));
            });
            model.Entity<Coupon>(entity =>
            {
                entity.Property(x => x.Version).IsConcurrencyToken();
                entity.Property(x => x.Value).HasPrecision(18, 2);
                entity.Property(x => x.MinimumSubtotal).HasPrecision(18, 2);
                entity.HasIndex(x => x.Code).IsUnique();
                entity.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_Coupons_Uses", "[MaxUses] >= 1 AND [UsedCount] >= 0 AND [UsedCount] <= [MaxUses]");
                    t.HasCheckConstraint("CK_Coupons_Value", "CAST([Value] AS decimal(18,2)) > 0 AND CAST([Value] AS decimal(18,2)) <= 1000000000 AND CAST([MinimumSubtotal] AS decimal(18,2)) >= 0 AND [Kind] IN (0, 1) AND ([Kind] = 0 OR CAST([Value] AS decimal(18,2)) <= 100)");
                    t.HasCheckConstraint("CK_Coupons_Dates", "[EndsAtUtc] > [StartsAtUtc]");
                });
            });
            model.Entity<CouponUsage>(entity =>
            {
                entity.HasOne<Coupon>().WithMany().HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.OrderId).IsUnique();
                entity.HasIndex(x => new { x.CouponId, x.UserId });
            });
        }
    }
}
