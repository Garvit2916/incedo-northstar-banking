# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/ backend/
RUN dotnet publish backend/src/Northstar.Api/Northstar.Api.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Local Compose mounts SQLite and uploaded documents here. In AWS both are
# replaced by RDS and private S3, so this directory is not used by the task role.
# The official .NET 10 runtime image already provides the non-root "app" user.
RUN mkdir -p /data && chown -R app:app /data

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Northstar.Api.dll"]
