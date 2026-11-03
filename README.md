**Tech stack:** C#, ASP.NET Core 10, Blazor, Entity Framework Core, SQL Server 2022, Docker Compose, GitHub Actions

# StockFlow

An order and inventory workspace for small businesses. ASP.NET Core 10, C#, EF Core, SQL Server 2022, and interactive Blazor, served as one application.

## Run the complete demo

Requires Docker Desktop (with x86 emulation on Apple Silicon) or Docker Engine with Compose.

```sh
cp .env.example .env
# Edit .env: set three unique passwords, each at least 12 characters.
docker compose up --build -d
```

Open [StockFlow](http://localhost:5080). Sign in as `admin@stockflow.local` or `staff@stockflow.local` with the password you configured. The sample workspace includes eight products, three customers, and 24 historical orders. Data and authentication keys persist in Docker volumes. `docker compose down` stops services without deleting data.

## What you can do

- Search inventory, create/edit/archive products as an admin, and receive stock with a delivery reference.
- Create/edit customers and draft orders with multiple lines and captured prices.
- Confirm to reserve stock, ship to consume it, or cancel to release it.
- Filter and paginate orders; inspect order details and permitted next actions.
- Read shipped sales, running revenue, top-product rankings, and low-stock alerts.
- Inspect transactional audit events as an admin; sign in as staff to verify permissions.

The dashboard and forms use the authenticated REST API. Inventory, customer and order tables use server-side search and pagination. Order-creation selectors load the first 100 catalogue/customer records; this limit is labelled in the order form.

## Demonstrate the last-item race

1. Add a product; receive exactly one unit.
2. Create two draft orders for one unit each.
3. From two signed-in browser tabs, confirm both orders at nearly the same time.
4. One succeeds; the other gets a stock conflict. Available stock is zero and reserved stock one.
5. Cancel the winner. Availability returns to one, and the other order can now confirm.

The automated integration test releases two HTTP confirmation requests behind a shared start gate and asserts the winner, losing response, stock invariant, idempotent cancellation, and subsequent confirmation. This is tested against real SQL Server, not an EF in-memory substitute.

## Tests

With .NET 10 installed and the Compose database running:

```sh
export TEST_SQL_CONNECTION='Server=localhost,14333;User Id=sa;Password=YOUR_SQL_PASSWORD;TrustServerCertificate=True'
dotnet test tests/StockFlow.Tests -c Release
```

Each test factory creates a uniquely named disposable SQL database and deletes it afterward. The SQL login needs database creation permission. Tests also cover multi-line rollback, duplicate lines, invalid quantities, database constraints, shipment idempotency, terminal transitions, permissions, CSRF, and reporting. GitHub Actions runs the suite with SQL Server on every push/PR.

To use the SDK container on the Compose network:

```sh
docker run --rm --network stockflow_default -v "$PWD:/workspace" -w /workspace \
  -e 'TEST_SQL_CONNECTION=Server=db;User Id=sa;Password=YOUR_SQL_PASSWORD;TrustServerCertificate=True' \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet test tests/StockFlow.Tests -c Release
```

## Portfolio materials

- [Architecture, concurrency reasoning and ER diagram](docs/design.md)
- [API guide](docs/api.md) and authenticated [OpenAPI JSON](http://localhost:5080/openapi/v1.json)
- [Initial SQL schema](sql/schema.sql)
- [Index experiment](sql/benchmark.sql) and [measured results](docs/benchmark-results.md)
- [Deployment instructions](docs/deployment.md)

## Local development

Set `ConnectionStrings__Database`, `InitializeDatabase=true`, `Seed__AdminPassword`, `Seed__StaffPassword` and optionally `Seed__SampleData=true`, then run `dotnet run --project src/StockFlow --urls http://localhost:5080`. Only new users are seeded; changing seed passwords will not reset existing users. Database initialisation uses EnsureCreated for this initial version; see the design document before changing the schema.

Microsoft references: [integration testing and WebApplicationFactory](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0), [EF Core transaction semantics](https://learn.microsoft.com/en-us/ef/core/saving/transactions).
