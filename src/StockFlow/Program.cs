using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StockFlow.Components;
using StockFlow.Data;
using StockFlow.Services;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Database") ?? throw new InvalidOperationException("ConnectionStrings__Database is required.")));
builder.Services.AddScoped<Operations>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.LoginPath = "/login"; o.Cookie.Name = "stockflow.session"; o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = builder.Configuration.GetValue<bool>("RequireSecureCookies") ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest; o.Cookie.SameSite = SameSiteMode.Strict; o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.Events.OnRedirectToLogin = c => { if (c.Request.Path.StartsWithSegments("/api")) c.Response.StatusCode = 401; else c.Response.Redirect(c.RedirectUri); return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.AddFixedWindowLimiter("login", x => { x.PermitLimit = 10; x.Window = TimeSpan.FromMinutes(1); x.QueueLimit = 0; }); });
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (RuleException ex) { await Results.Problem(ex.Message, statusCode: ex.Status).ExecuteAsync(ctx); }
    catch (DbUpdateException) { await Results.Problem("The record conflicts with existing data. Check unique fields and references.", statusCode: 409).ExecuteAsync(ctx); }
    catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205) { await Results.Problem("A concurrent operation interrupted this request. Retry the operation.", statusCode: 409).ExecuteAsync(ctx); }
});
app.UseStaticFiles(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization(); app.UseAntiforgery();
app.MapGet("/health", async (AppDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503));
app.MapGet("/login", () => Results.Content("""
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Sign in · StockFlow</title><link rel="stylesheet" href="/app.css"></head><body class="login"><main class="login-card"><div class="brand"><span class="brand-icon">▦</span> StockFlow</div><p class="eyebrow">YOUR OPERATIONS, IN SYNC</p><h1>Welcome back.</h1><p>Sign in to your inventory workspace.</p><form id="login"><label>Email<input name="email" type="email" required autocomplete="username"></label><label>Password<input name="password" type="password" required autocomplete="current-password"></label><button>Sign in →</button><p id="error" role="alert"></p></form><small>Use the admin or staff credentials configured by your administrator.</small></main><script>document.querySelector('form').onsubmit=async e=>{e.preventDefault();const f=new FormData(e.target);const r=await fetch('/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(Object.fromEntries(f))});if(r.ok)location.href='/';else document.querySelector('#error').textContent=r.status===429?'Too many attempts. Try again in a minute.':'Unable to sign in. Check your credentials.';};</script></body></html>
""", "text/html"));
app.MapPost("/api/auth/login", async (LoginInput input, AppDb db, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(input.Email) || string.IsNullOrEmpty(input.Password) || input.Password.Length > 256) return Results.Unauthorized();
    var user = await db.Users.SingleOrDefaultAsync(x => x.Email == input.Email.Trim().ToLower());
    if (user is null || new PasswordHasher<AppUser>().VerifyHashedPassword(user, user.PasswordHash, input.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
    await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name, user.Email), new(ClaimTypes.Role, user.Role)], CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(new { user.Email, user.Role });
}).RequireRateLimiting("login");
var api = app.MapGroup("/api").RequireAuthorization();
api.AddEndpointFilter(async (context, next) =>
{
    var http = context.HttpContext;
    if (http.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        try { await http.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>().ValidateRequestAsync(http); }
        catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return Results.Problem("Missing or invalid CSRF token.", statusCode: 400); }
    }
    return await next(context);
});
api.MapGet("/auth/csrf", (HttpContext c, Microsoft.AspNetCore.Antiforgery.IAntiforgery a) => Results.Ok(new { token = a.GetAndStoreTokens(c).RequestToken }));
api.MapGet("/auth/me", (ClaimsPrincipal u) => new { email = u.Identity!.Name, role = u.FindFirstValue(ClaimTypes.Role) });
api.MapPost("/auth/logout", async (HttpContext c) => { await c.SignOutAsync(); return Results.NoContent(); });
api.MapGet("/dashboard", async (AppDb db) => new
{
    activeProducts = await db.Products.CountAsync(x => x.Active),
    availableUnits = await db.Products.Where(x => x.Active).SumAsync(x => (long)x.OnHand - x.Reserved),
    totalOrders = await db.Orders.CountAsync()
});
api.MapGet("/products", async (AppDb db, string? search, bool? lowStock, int? page, int? pageSize) =>
{
    var p = Math.Clamp(page ?? 1, 1, 1000000); var size = Math.Clamp(pageSize ?? 20, 1, 100); var q = db.Products.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search) || x.Sku.Contains(search) || x.Category.Contains(search));
    if (lowStock == true) q = q.Where(x => x.OnHand - x.Reserved <= x.ReorderLevel && x.Active);
    return new PageResult<Product>(await q.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((p - 1) * size).Take(size).ToListAsync(), await q.CountAsync(), p, size);
});
api.MapGet("/products/{id:int}", async (int id, AppDb db) => await db.Products.FindAsync(id) is { } p ? Results.Ok(p) : Results.NotFound());
api.MapPost("/products", async (ProductInput input, AppDb db, Operations ops, ClaimsPrincipal u) =>
{
    ValidateProduct(input); var p = new Product { Sku = input.Sku.Trim(), Name = input.Name.Trim(), Category = input.Category.Trim(), Price = input.Price, ReorderLevel = input.ReorderLevel }; db.Products.Add(p); ops.Audit(u.Identity!.Name!, "Product created", p.Sku); await db.SaveChangesAsync(); return Results.Created($"/api/products/{p.Id}", p);
}).RequireAuthorization(p => p.RequireRole("Admin"));
api.MapPut("/products/{id:int}", async (int id, ProductInput input, AppDb db, Operations ops, ClaimsPrincipal u) =>
{
    ValidateProduct(input); var p = await db.Products.FindAsync(id) ?? throw new RuleException("Product not found.", 404); p.Sku = input.Sku.Trim(); p.Name = input.Name.Trim(); p.Category = input.Category.Trim(); p.Price = input.Price; p.ReorderLevel = input.ReorderLevel; ops.Audit(u.Identity!.Name!, "Product updated", $"Product #{id}"); await db.SaveChangesAsync(); return Results.Ok(p);
}).RequireAuthorization(p => p.RequireRole("Admin"));
api.MapDelete("/products/{id:int}", async (int id, AppDb db, Operations ops, ClaimsPrincipal u) => { var p = await db.Products.FindAsync(id) ?? throw new RuleException("Product not found.", 404); p.Active = false; ops.Audit(u.Identity!.Name!, "Product archived", $"Product #{id}"); await db.SaveChangesAsync(); return Results.NoContent(); }).RequireAuthorization(p => p.RequireRole("Admin"));
api.MapPost("/products/{id:int}/receive", async (int id, ReceiptInput input, Operations ops, ClaimsPrincipal u) => { await ops.Receive(id, input, u.Identity!.Name!); return Results.NoContent(); });
api.MapGet("/customers", async (AppDb db, string? search, int? page, int? pageSize) => { var p = Math.Clamp(page ?? 1, 1, 1000000); var size = Math.Clamp(pageSize ?? 20, 1, 100); var q = db.Customers.AsNoTracking().AsQueryable(); if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search) || x.Email.Contains(search)); return new PageResult<Customer>(await q.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((p - 1) * size).Take(size).ToListAsync(), await q.CountAsync(), p, size); });
api.MapGet("/customers/{id:int}", async (int id, AppDb db) => await db.Customers.FindAsync(id) is { } c ? Results.Ok(c) : Results.NotFound());
api.MapPost("/customers", async (CustomerInput input, AppDb db, Operations ops, ClaimsPrincipal u) => { ValidateCustomer(input); var c = new Customer { Name = input.Name.Trim(), Email = input.Email.Trim().ToLower(), Address = input.Address.Trim() }; db.Customers.Add(c); ops.Audit(u.Identity!.Name!, "Customer created", c.Name); await db.SaveChangesAsync(); return Results.Created($"/api/customers/{c.Id}", c); });
api.MapPut("/customers/{id:int}", async (int id, CustomerInput input, AppDb db, Operations ops, ClaimsPrincipal u) => { ValidateCustomer(input); var c = await db.Customers.FindAsync(id) ?? throw new RuleException("Customer not found.", 404); c.Name = input.Name.Trim(); c.Email = input.Email.Trim().ToLower(); c.Address = input.Address.Trim(); ops.Audit(u.Identity!.Name!, "Customer updated", $"Customer #{id}"); await db.SaveChangesAsync(); return Results.Ok(c); });
api.MapGet("/orders", async (AppDb db, string? status, string? search, int? page, int? pageSize) => { var p = Math.Clamp(page ?? 1, 1, 1000000); var size = Math.Clamp(pageSize ?? 20, 1, 100); var q = db.Orders.AsNoTracking().Include(x => x.Customer).Include(x => x.Lines).ThenInclude(x => x.Product).AsQueryable(); if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status); if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Customer.Name.Contains(search)); return new PageResult<Order>(await q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((p - 1) * size).Take(size).ToListAsync(), await q.CountAsync(), p, size); });
api.MapGet("/orders/{id:int}", async (int id, AppDb db) => await db.Orders.AsNoTracking().Include(x => x.Customer).Include(x => x.Lines).ThenInclude(x => x.Product).SingleOrDefaultAsync(x => x.Id == id) is { } o ? Results.Ok(o) : Results.NotFound());
api.MapPost("/orders", async (OrderInput input, Operations ops, ClaimsPrincipal u) => { var o = await ops.CreateOrder(input, u.Identity!.Name!); return Results.Created($"/api/orders/{o.Id}", new { o.Id, o.Status, o.Total }); });
api.MapPost("/orders/{id:int}/{action}", async (int id, string action, Operations ops, ClaimsPrincipal u) => { var target = action switch { "confirm" => "Confirmed", "ship" => "Shipped", "cancel" => "Cancelled", _ => throw new RuleException("Unknown order action.", 404) }; await ops.Transition(id, target, u.Identity!.Name!); return Results.NoContent(); });
api.MapGet("/reports/sales", (Operations ops) => ops.Sales());
api.MapGet("/reports/top-products", (Operations ops) => ops.TopProducts());
api.MapGet("/reports/low-stock", async (AppDb db) => await db.Products.AsNoTracking().Where(x => x.Active && x.OnHand - x.Reserved <= x.ReorderLevel).OrderBy(x => x.OnHand - x.Reserved).ToListAsync());
api.MapGet("/audit", async (AppDb db, int? page) => await db.AuditEvents.AsNoTracking().OrderByDescending(x => x.Id).Skip((Math.Clamp(page ?? 1, 1, 1000000) - 1) * 50).Take(50).ToListAsync()).RequireAuthorization(p => p.RequireRole("Admin"));
app.MapOpenApi().RequireAuthorization();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
if (builder.Configuration.GetValue<bool>("InitializeDatabase"))
{
    await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>(); await db.Database.EnsureCreatedAsync(); await Seed.Run(db, builder.Configuration);
}
app.Run();
static void ValidateProduct(ProductInput p)
{
    if (string.IsNullOrWhiteSpace(p.Sku) || p.Sku.Length > 40 || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 160 || p.Category is null || p.Category.Length > 80 || p.Price < 0 || p.Price > 1000000 || decimal.Round(p.Price, 2) != p.Price || p.ReorderLevel is < 0 or > 100000) throw new RuleException("Check SKU, name, category, price (two decimals) and reorder level.", 400);
}
static void ValidateCustomer(CustomerInput c)
{
    if (string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 160 || string.IsNullOrWhiteSpace(c.Email) || c.Email.Length > 254 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(c.Email) || c.Address is null || c.Address.Length > 300) throw new RuleException("Provide a name, valid email and address of up to 300 characters.", 400);
}
public partial class Program { }
