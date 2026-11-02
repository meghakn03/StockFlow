/* Run in a dedicated benchmark database. Produces REAL actual XML plans and IO/time messages.
   200,000 orders; index targets the report's status/date predicate. No timings are invented. */
SET NOCOUNT ON;
DROP TABLE IF EXISTS dbo.BenchmarkOrders;
CREATE TABLE dbo.BenchmarkOrders(Id int IDENTITY PRIMARY KEY, Status varchar(20) NOT NULL, CreatedAt datetime2 NOT NULL, Payload char(200) NOT NULL);
WITH N AS (SELECT TOP (200000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT dbo.BenchmarkOrders(Status,CreatedAt,Payload)
SELECT CASE WHEN n%20=0 THEN 'Shipped' ELSE 'Draft' END, DATEADD(minute,-CAST(n AS int),SYSUTCDATETIME()),'benchmark' FROM N;
UPDATE STATISTICS dbo.BenchmarkOrders WITH FULLSCAN;
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
SET STATISTICS XML ON;
SELECT COUNT_BIG(*) AS Orders FROM dbo.BenchmarkOrders WHERE Status='Shipped' AND CreatedAt >= DATEADD(day,-7,SYSUTCDATETIME());
SET STATISTICS XML OFF;
CREATE INDEX IX_BenchmarkOrders_Status_CreatedAt ON dbo.BenchmarkOrders(Status,CreatedAt);
SET STATISTICS XML ON;
SELECT COUNT_BIG(*) AS Orders FROM dbo.BenchmarkOrders WHERE Status='Shipped' AND CreatedAt >= DATEADD(day,-7,SYSUTCDATETIME());
SET STATISTICS XML OFF;
SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;
