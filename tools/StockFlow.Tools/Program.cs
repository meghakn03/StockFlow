using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StockFlow.Data;
using System.Xml.Linq;
var mode=args.FirstOrDefault()??"schema";
if(mode=="schema") {
 using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlServer("Server=unused;Database=StockFlow").Options);
 Directory.CreateDirectory("sql");await File.WriteAllTextAsync("sql/schema.sql",db.Database.GenerateCreateScript());Console.WriteLine("Wrote sql/schema.sql");return;
}
if(mode!="benchmark")throw new ArgumentException("Use schema or benchmark");
var cs=Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION")??throw new InvalidOperationException("Set TEST_SQL_CONNECTION");
var cb=new SqlConnectionStringBuilder(cs){InitialCatalog="StockFlowBenchmark"};
await using(var master=new SqlConnection(new SqlConnectionStringBuilder(cs){InitialCatalog="master"}.ConnectionString)) {
 await master.OpenAsync();await using var create=master.CreateCommand();create.CommandText="IF DB_ID('StockFlowBenchmark') IS NULL CREATE DATABASE StockFlowBenchmark";await create.ExecuteNonQueryAsync();
}
await using var connection=new SqlConnection(cb.ConnectionString);await connection.OpenAsync();
Directory.CreateDirectory("docs/plans");var messages=new List<string>();connection.InfoMessage+=(_,e)=>messages.Add(e.Message);
var sql=await File.ReadAllTextAsync("sql/benchmark.sql");await using var command=connection.CreateCommand();command.CommandTimeout=180;command.CommandText=sql;
await using var reader=await command.ExecuteReaderAsync();var plans=new List<string>();
do {while(await reader.ReadAsync()) {if(reader.FieldCount==1 && reader.GetValue(0) is string text && text.Contains("ShowPlanXML"))plans.Add(text);}}while(await reader.NextResultAsync());
await reader.CloseAsync();
for(var i=0;i<plans.Count;i++)await File.WriteAllTextAsync($"docs/plans/{(i==0?"before":"after")}.sqlplan",plans[i]);
await File.WriteAllLinesAsync("docs/plans/statistics.txt",messages);
var ns=(XNamespace)"http://schemas.microsoft.com/sqlserver/2004/07/showplan";
var summary=new List<string>{"# Measured indexing experiment","","Measured on local SQL Server 2022 in Docker (linux/amd64 emulation on Apple Silicon). Synthetic table: 200,000 rows, 200-byte payload, 5% Shipped. Query counts shipped orders created in the last seven days. Measurements are one execution per plan on warm data; CPU and elapsed time are noisy under emulation. Logical reads and operator changes provide the more useful comparison.","","| Plan | Physical operators | Estimated subtree cost |","|---|---|---|"};
for(var i=0;i<plans.Count;i++){var xml=XDocument.Parse(plans[i]);var ops=xml.Descendants(ns+"RelOp").Select(x=>(string?)x.Attribute("PhysicalOp")).Distinct();var cost=xml.Descendants(ns+"StmtSimple").FirstOrDefault()?.Attribute("StatementSubTreeCost")?.Value;summary.Add($"| {(i==0?"Before":"After")} | {string.Join(", ",ops)} | {cost} |");}
summary.AddRange(["","## Actual SQL Server statistics","","```text",..messages.Where(x=>x.Contains("BenchmarkOrders")||x.Contains("CPU time")),"```","","The index is `(Status, CreatedAt)`, matching the application's order-list index. It covers this count query and avoids reading the wide payload. This demonstrates an indexing improvement for a representative predicate, not a benchmark of the whole sales-report endpoint. Estimated cost is the optimizer's estimate, not measured duration.","","Reproduce with `dotnet run --project tools/StockFlow.Tools -- benchmark` after setting `TEST_SQL_CONNECTION`. The benchmark recreates only `dbo.BenchmarkOrders` in the dedicated `StockFlowBenchmark` database. [Before plan](plans/before.sqlplan), [after plan](plans/after.sqlplan), [full statistics](plans/statistics.txt). Open `.sqlplan` files in SQL Server Management Studio to inspect the graphical plan."]);
await File.WriteAllLinesAsync("docs/benchmark-results.md",summary);Console.WriteLine("Captured two actual plans, SQL statistics and docs/benchmark-results.md.");
