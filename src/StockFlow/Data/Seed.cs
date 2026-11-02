using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace StockFlow.Data;

public static class Seed
{
    public static async Task Run(AppDb db, IConfiguration config)
    {
        foreach (var role in new[] { "Admin", "Staff" })
        {
            var password = config[$"Seed:{role}Password"];
            var email = $"{role.ToLower()}@stockflow.local";
            if (!await db.Users.AnyAsync(x => x.Email == email) && !string.IsNullOrEmpty(password))
            {
                if (password.Length < 12) throw new InvalidOperationException("Seed passwords must contain at least 12 characters.");
                var u = new AppUser { Email = email, Role = role }; u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, password); db.Users.Add(u);
            }
        }
        if (!await db.Products.AnyAsync() && config.GetValue<bool>("Seed:SampleData"))
        {
            var names = new[] { ("SF-001", "Everyday canvas tote", "Accessories", 18m, 64), ("SF-002", "Stoneware coffee mug", "Home & living", 24m, 8), ("SF-003", "Hardcover notebook", "Stationery", 16m, 42), ("SF-004", "Brass desk lamp", "Home & living", 89m, 3), ("SF-005", "Cotton crew socks", "Apparel", 12m, 96), ("SF-006", "Cedar & sage candle", "Home & living", 28m, 5), ("SF-007", "Weekly desk planner", "Stationery", 22m, 31), ("SF-008", "Insulated water bottle", "Accessories", 32m, 18) };
            foreach (var x in names) db.Products.Add(new() { Sku = x.Item1, Name = x.Item2, Category = x.Item3, Price = x.Item4, OnHand = x.Item5, ReorderLevel = 10 });
            db.Customers.AddRange(new Customer { Name = "Olivia Martin", Email = "olivia@example.com", Address = "14 King Street, Bristol" }, new Customer { Name = "James Wilson", Email = "james@example.com", Address = "28 Park Road, London" }, new Customer { Name = "Amelia Brown", Email = "amelia@example.com", Address = "6 Queen Street, Bath" });
            await db.SaveChangesAsync();
            var products = await db.Products.OrderBy(x => x.Id).ToListAsync(); var customers = await db.Customers.ToListAsync();
            for (var i = 0; i < 24; i++) db.Orders.Add(new() { CustomerId = customers[i % 3].Id, Status = i < 18 ? "Shipped" : "Draft", CreatedAt = DateTime.UtcNow.AddDays(-i * 3), Lines = [new() { ProductId = products[i % 8].Id, Quantity = 1 + i % 3, UnitPrice = products[i % 8].Price }] });
            db.AuditEvents.Add(new() { Actor = "System", Action = "Workspace initialized", Detail = "Sample catalogue, customers and historical orders loaded. Opening stock is current stock after historical shipments." });
        }
        await db.SaveChangesAsync();
    }
}
