using System.ComponentModel.DataAnnotations;
namespace StockFlow.Data;

public class Product
{
    public int Id { get; set; }
    [MaxLength(40)] public string Sku { get; set; } = "";
    [MaxLength(160)] public string Name { get; set; } = "";
    [MaxLength(80)] public string Category { get; set; } = "";
    public decimal Price { get; set; }
    public int OnHand { get; set; }
    public int Reserved { get; set; }
    public int ReorderLevel { get; set; } = 5;
    public bool Active { get; set; } = true;
    public int Available => OnHand - Reserved;
}
public class Customer
{
    public int Id { get; set; }
    [MaxLength(160)] public string Name { get; set; } = "";
    [MaxLength(254)] public string Email { get; set; } = "";
    [MaxLength(300)] public string Address { get; set; } = "";
}
public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    [MaxLength(20)] public string Status { get; set; } = "Draft";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<OrderLine> Lines { get; set; } = [];
    public decimal Total => Lines.Sum(x => x.Quantity * x.UnitPrice);
}
public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
public class AuditEvent
{
    public long Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    [MaxLength(160)] public string Actor { get; set; } = "";
    [MaxLength(80)] public string Action { get; set; } = "";
    [MaxLength(1000)] public string Detail { get; set; } = "";
}
public class AppUser
{
    public int Id { get; set; }
    [MaxLength(160)] public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    [MaxLength(20)] public string Role { get; set; } = "Staff";
}
public record ProductInput(string Sku, string Name, string Category, decimal Price, int ReorderLevel);
public record CustomerInput(string Name, string Email, string Address);
public record LineInput(int ProductId, int Quantity);
public record OrderInput(int CustomerId, List<LineInput> Lines);
public record ReceiptInput(int Quantity, string Reference);
public record LoginInput(string Email, string Password);
public record PageResult<T>(List<T> Items, int Total, int Page, int PageSize);
public record SalesRow(string Month, decimal Revenue, int Orders, decimal RunningRevenue);
public record TopProductRow(string Name, int Units, decimal Revenue, long Rank);
