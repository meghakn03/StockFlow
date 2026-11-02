using Microsoft.EntityFrameworkCore;
namespace StockFlow.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AppUser> Users => Set<AppUser>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Product>().HasIndex(x => x.Sku).IsUnique();
        b.Entity<Product>().Property(x => x.Price).HasPrecision(18, 2);
        b.Entity<Product>().Ignore(x => x.Available);
        b.Entity<Product>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Product_Stock", "[OnHand] >= 0 AND [Reserved] >= 0 AND [Reserved] <= [OnHand]");
            t.HasCheckConstraint("CK_Product_Price", "[Price] >= 0 AND [ReorderLevel] >= 0");
        });
        b.Entity<Customer>().HasIndex(x => x.Email).IsUnique();
        b.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Order>().Ignore(x => x.Total);
        b.Entity<Order>().HasIndex(x => new { x.Status, x.CreatedAt });
        b.Entity<Order>().ToTable(t => t.HasCheckConstraint("CK_Order_Status", "[Status] IN ('Draft','Confirmed','Shipped','Cancelled')"));
        b.Entity<Order>().HasOne(x => x.Customer).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<OrderLine>().HasOne(x => x.Product).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<OrderLine>().Property(x => x.UnitPrice).HasPrecision(18, 2);
        b.Entity<OrderLine>().HasIndex(x => new { x.OrderId, x.ProductId }).IsUnique();
        b.Entity<OrderLine>().ToTable(t => t.HasCheckConstraint("CK_Line_Quantity", "[Quantity] > 0 AND [UnitPrice] >= 0"));
    }
}
