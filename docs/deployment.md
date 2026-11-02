# Deployment

The Dockerfile builds a self-contained application image (framework-dependent .NET 10 runtime) with a non-root runtime user. Compose provisions SQL Server, health-gates startup and persists database and Data Protection keys. Local URL: http://localhost:5080.

For a public deployment, deploy the same image on a container host capable of running ASP.NET Core (Azure Container Apps, App Service for Containers, or a VM), and use Azure SQL or a managed SQL Server. Set `ConnectionStrings__Database` from a secret manager with encrypted SQL connectivity (`Encrypt=True;TrustServerCertificate=False`). Initialise the empty schema once using `sql/schema.sql` or the single-instance bootstrap path, then set `InitializeDatabase=false` on replicas. Provision user hashes during that controlled initialisation with seed passwords. Remove seed secrets afterward. Set `ASPNETCORE_URLS=http://+:8080` and configure a health probe at `/health`.

Terminate HTTPS at the host ingress, set `RequireSecureCookies=true`, and persist/share Data Protection keys across replicas. Grant the application login CRUD rights only; use a separate schema owner for deployments. Back up SQL regularly and test restores. Do not deploy the local SA credentials or expose port 1433. Seed demo data only in a separate demonstration database. The application has no public account registration or password-reset flow; use controlled provisioning until those capabilities are implemented.

Sites hosting uses Cloudflare Workers and cannot host this .NET process or SQL Server. No public cloud deployment is claimed by this repository; a compatible hosting account and SQL service must be provisioned for a public URL.
