FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/StockFlow/StockFlow.csproj src/StockFlow/
RUN dotnet restore src/StockFlow/StockFlow.csproj
COPY src/ src/
RUN dotnet publish src/StockFlow/StockFlow.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "StockFlow.dll"]
