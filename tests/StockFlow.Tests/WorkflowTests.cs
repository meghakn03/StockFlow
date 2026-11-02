using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Data;
using Xunit;
namespace StockFlow.Tests;

public sealed class AppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string Connection { get; } = (Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? "Server=localhost,14333;User Id=sa;Password=ChangeThis_SQL_2026!;TrustServerCertificate=True") + $";Database=StockFlowTest_{Guid.NewGuid():N}";
    protected override void ConfigureWebHost(IWebHostBuilder b) => b.UseEnvironment("Testing").ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Database"] = Connection,
        ["InitializeDatabase"] = "true",
        ["Seed:AdminPassword"] = "Test_Admin_2026!",
        ["Seed:StaffPassword"] = "Test_Staff_2026!",
        ["Seed:SampleData"] = "false"
    }));
    public Task InitializeAsync() { _ = Services; return Task.CompletedTask; }
    async Task IAsyncLifetime.DisposeAsync() { await using var s = Services.CreateAsyncScope(); await s.ServiceProvider.GetRequiredService<AppDb>().Database.EnsureDeletedAsync(); await DisposeAsync(); }
    public async Task<HttpClient> Login(string role = "Admin")
    {
        var client = CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginInput($"{role.ToLower()}@stockflow.local", $"Test_{role}_2026!")); response.EnsureSuccessStatusCode();
        var token = await client.GetFromJsonAsync<Token>("/api/auth/csrf"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!.Value); return client;
    }
    record Token([property: System.Text.Json.Serialization.JsonPropertyName("token")] string Value);
}
public class WorkflowTests(AppFactory factory) : IClassFixture<AppFactory>
{
    async Task<(HttpClient Client, int Product, int Customer)> Setup(int stock = 1)
    {
        var client = await factory.Login(); var key = Guid.NewGuid().ToString("N")[..10];
        var p = await (await client.PostAsJsonAsync("/api/products", new ProductInput(key, "Test item", "Tests", 12.50m, 2))).Content.ReadFromJsonAsync<Product>();
        var c = await (await client.PostAsJsonAsync("/api/customers", new CustomerInput("Test customer", $"{key}@example.com", "Test address"))).Content.ReadFromJsonAsync<Customer>();
        if (stock > 0) (await client.PostAsJsonAsync($"/api/products/{p!.Id}/receive", new ReceiptInput(stock, "test receipt"))).EnsureSuccessStatusCode();
        return (client, p!.Id, c!.Id);
    }
    static async Task<int> Draft(HttpClient c, int customer, params LineInput[] lines) { var r = await c.PostAsJsonAsync("/api/orders", new OrderInput(customer, lines.ToList())); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<Order>())!.Id; }
    [Fact]
    public async Task Last_item_has_one_winner_and_cancellation_restores_it()
    {
        var (c, p, customer) = await Setup(); var a = await Draft(c, customer, new LineInput(p, 1)); var b = await Draft(c, customer, new LineInput(p, 1));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Confirm(int id) { await gate.Task; return await c.PostAsync($"/api/orders/{id}/confirm", null); }
        var t1 = Confirm(a); var t2 = Confirm(b); gate.SetResult(); var results = await Task.WhenAll(t1, t2);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.NoContent); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var product = await c.GetFromJsonAsync<Product>($"/api/products/{p}"); Assert.Equal(1, product!.OnHand); Assert.Equal(1, product.Reserved); Assert.Equal(0, product.Available);
        var winner = results[0].IsSuccessStatusCode ? a : b; var loser = winner == a ? b : a;
        (await c.PostAsync($"/api/orders/{winner}/cancel", null)).EnsureSuccessStatusCode();
        (await c.PostAsync($"/api/orders/{winner}/cancel", null)).EnsureSuccessStatusCode();
        product = await c.GetFromJsonAsync<Product>($"/api/products/{p}"); Assert.Equal(1, product!.Available); Assert.Equal(0, product.Reserved);
        (await c.PostAsync($"/api/orders/{loser}/confirm", null)).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Multi_line_failure_rolls_back_every_reservation()
    {
        var (c, p, customer) = await Setup(3); var key = Guid.NewGuid().ToString("N")[..10]; var second = await (await c.PostAsJsonAsync("/api/products", new ProductInput(key, "Empty", "Tests", 1, 1))).Content.ReadFromJsonAsync<Product>();
        var id = await Draft(c, customer, new LineInput(p, 2), new LineInput(second!.Id, 1)); Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/orders/{id}/confirm", null)).StatusCode);
        Assert.Equal(0, (await c.GetFromJsonAsync<Product>($"/api/products/{p}"))!.Reserved);
        Assert.Equal("Draft", (await c.GetFromJsonAsync<Order>($"/api/orders/{id}"))!.Status);
    }
    [Fact]
    public async Task Shipping_consumes_stock_once_and_cannot_be_cancelled()
    {
        var (c, p, customer) = await Setup(2); var id = await Draft(c, customer, new LineInput(p, 2));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/orders/{id}/ship", null)).StatusCode);
        (await c.PostAsync($"/api/orders/{id}/confirm", null)).EnsureSuccessStatusCode();
        await Task.WhenAll(c.PostAsync($"/api/orders/{id}/ship", null), c.PostAsync($"/api/orders/{id}/ship", null));
        var product = await c.GetFromJsonAsync<Product>($"/api/products/{p}"); Assert.Equal(0, product!.OnHand); Assert.Equal(0, product.Reserved);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/orders/{id}/cancel", null)).StatusCode);
        var sales = await c.GetFromJsonAsync<List<SalesRow>>("/api/reports/sales"); Assert.True(sales!.Sum(x => x.Revenue) >= 25m);
    }
    [Fact]
    public async Task Permissions_and_csrf_are_enforced()
    {
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false }); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/products")).StatusCode);
        using var staff = await factory.Login("Staff"); Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/products", new ProductInput("x", "x", "x", 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/audit")).StatusCode);
        using var admin = await factory.Login(); admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/customers", new CustomerInput("x", "x@example.com", ""))).StatusCode);
    }
    [Fact]
    public async Task Duplicate_lines_and_negative_receipts_are_rejected()
    {
        var (c, p, customer) = await Setup(); Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/orders", new OrderInput(customer, [new LineInput(p, 1), new LineInput(p, 1)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/products/{p}/receive", new ReceiptInput(-1, "bad"))).StatusCode);
    }
    [Fact]
    public async Task Simultaneous_ship_and_cancel_cannot_both_consume_the_reservation()
    {
        var (c, p, customer) = await Setup(); var id = await Draft(c, customer, new LineInput(p, 1));
        (await c.PostAsync($"/api/orders/{id}/confirm", null)).EnsureSuccessStatusCode();
        var results = await Task.WhenAll(c.PostAsync($"/api/orders/{id}/ship", null), c.PostAsync($"/api/orders/{id}/cancel", null));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.NoContent); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var product = await c.GetFromJsonAsync<Product>($"/api/products/{p}"); var order = await c.GetFromJsonAsync<Order>($"/api/orders/{id}");
        Assert.Equal(0, product!.Reserved); Assert.Equal(order!.Status == "Shipped" ? 0 : 1, product.OnHand);
    }
    [Fact]
    public async Task Database_constraint_rejects_negative_stock()
    {
        var (_, p, _) = await Setup(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Products SET OnHand=-1 WHERE Id={p}"));
    }
}
