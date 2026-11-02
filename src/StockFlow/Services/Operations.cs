using Microsoft.EntityFrameworkCore;
using StockFlow.Data;
namespace StockFlow.Services;

public class RuleException(string message, int status = 409) : Exception(message) { public int Status { get; } = status; }
public class Operations(AppDb db)
{
    public void Audit(string actor, string action, string detail) => db.AuditEvents.Add(new() { Actor = actor, Action = action, Detail = detail });
    public async Task<Order> CreateOrder(OrderInput input, string actor)
    {
        if (input.Lines is null || input.Lines.Count is < 1 or > 100 || input.Lines.Any(x => x.Quantity is < 1 or > 100000) || input.Lines.Select(x => x.ProductId).Distinct().Count() != input.Lines.Count)
            throw new RuleException("Provide 1–100 unique products with positive quantities.", 400);
        if (!await db.Customers.AnyAsync(x => x.Id == input.CustomerId)) throw new RuleException("Customer not found.", 404);
        var ids = input.Lines.Select(x => x.ProductId).ToArray();
        var products = await db.Products.Where(x => ids.Contains(x.Id) && x.Active).ToDictionaryAsync(x => x.Id);
        if (products.Count != ids.Length) throw new RuleException("An item is missing or inactive.", 400);
        var order = new Order { CustomerId = input.CustomerId, Lines = input.Lines.Select(x => new OrderLine { ProductId = x.ProductId, Quantity = x.Quantity, UnitPrice = products[x.ProductId].Price }).ToList() };
        db.Orders.Add(order);
        Audit(actor, "Order drafted", $"Customer {input.CustomerId}; {input.Lines.Count} lines");
        await db.SaveChangesAsync();
        return order;
    }
    public async Task Transition(int id, string target, string actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        // Lock order first, then products in ID order. Same-order commands serialize; product updates are guarded atomically.
        var order = await db.Orders.FromSqlInterpolated($"SELECT * FROM Orders WITH (UPDLOCK, ROWLOCK) WHERE Id = {id}").SingleOrDefaultAsync()
         ?? throw new RuleException("Order not found.", 404);
        if (order.Status == target) { await tx.CommitAsync(); return; }
        var allowed = (order.Status, target) is ("Draft", "Confirmed") or ("Draft", "Cancelled") or ("Confirmed", "Shipped") or ("Confirmed", "Cancelled");
        if (!allowed) throw new RuleException($"Cannot move {order.Status} to {target}.");
        var lines = await db.OrderLines.Where(x => x.OrderId == id).OrderBy(x => x.ProductId).ToListAsync();
        foreach (var line in lines)
        {
            int affected = 1;
            if (target == "Confirmed")
                affected = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Products SET Reserved = Reserved + {line.Quantity} WHERE Id = {line.ProductId} AND Active = 1 AND OnHand - Reserved >= {line.Quantity}");
            else if (order.Status == "Confirmed" && target == "Cancelled")
                affected = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Products SET Reserved = Reserved - {line.Quantity} WHERE Id = {line.ProductId} AND Reserved >= {line.Quantity}");
            else if (target == "Shipped")
                affected = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Products SET OnHand = OnHand - {line.Quantity}, Reserved = Reserved - {line.Quantity} WHERE Id = {line.ProductId} AND Reserved >= {line.Quantity}");
            if (affected != 1) throw new RuleException($"Insufficient available stock for product {line.ProductId}. No stock was reserved.");
        }
        var previous = order.Status;
        order.Status = target;
        Audit(actor, $"Order {target.ToLowerInvariant()}", $"Order #{id}: {previous} → {target}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
    public async Task Receive(int id, ReceiptInput input, string actor)
    {
        if (input.Quantity is < 1 or > 100000 || string.IsNullOrWhiteSpace(input.Reference) || input.Reference.Length > 120) throw new RuleException("Provide a quantity between 1 and 100,000 and a reference (120 characters maximum).", 400);
        await using var tx = await db.Database.BeginTransactionAsync();
        var count = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Products SET OnHand = OnHand + {input.Quantity} WHERE Id = {id} AND Active = 1 AND OnHand <= 2000000000 - {input.Quantity}");
        if (count != 1) throw new RuleException("Active product not found or stock limit exceeded.", 404);
        Audit(actor, "Stock received", $"Product #{id}: +{input.Quantity}. Reference: {input.Reference}");
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public Task<List<SalesRow>> Sales() => db.Database.SqlQueryRaw<SalesRow>("""
 WITH Monthly AS (
 SELECT CONVERT(varchar(7), o.CreatedAt, 126) AS Month,
 CAST(SUM(l.Quantity*l.UnitPrice) AS decimal(18,2)) AS Revenue, COUNT(DISTINCT o.Id) AS Orders
 FROM Orders o JOIN OrderLines l ON l.OrderId=o.Id WHERE o.Status='Shipped'
 GROUP BY CONVERT(varchar(7), o.CreatedAt, 126))
 SELECT Month, Revenue, Orders, CAST(SUM(Revenue) OVER (ORDER BY Month) AS decimal(18,2)) AS RunningRevenue FROM Monthly
 """).ToListAsync();
    public Task<List<TopProductRow>> TopProducts() => db.Database.SqlQueryRaw<TopProductRow>("""
 WITH Sales AS (SELECT p.Name, SUM(l.Quantity) AS Units, CAST(SUM(l.Quantity*l.UnitPrice) AS decimal(18,2)) AS Revenue
 FROM Products p JOIN OrderLines l ON p.Id=l.ProductId JOIN Orders o ON o.Id=l.OrderId
 WHERE o.Status='Shipped' GROUP BY p.Id,p.Name)
 SELECT Name, Units, Revenue, DENSE_RANK() OVER (ORDER BY Revenue DESC) AS Rank FROM Sales
 """).ToListAsync();
}
