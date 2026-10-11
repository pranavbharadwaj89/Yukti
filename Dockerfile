# Multi-stage Dockerfile for Yukti Unified Automation Platform
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy solution and project definitions
COPY Directory.Build.props ./
COPY Yukti.sln ./
COPY src/Yukti.Domain/Yukti.Domain.csproj src/Yukti.Domain/
COPY src/Yukti.Contracts/Yukti.Contracts.csproj src/Yukti.Contracts/
COPY src/Yukti.Application/Yukti.Application.csproj src/Yukti.Application/
COPY src/Yukti.Orchestration/Yukti.Orchestration.csproj src/Yukti.Orchestration/
COPY src/Yukti.Infrastructure/Yukti.Infrastructure.csproj src/Yukti.Infrastructure/
COPY src/Yukti.Infrastructure.InMemory/Yukti.Infrastructure.InMemory.csproj src/Yukti.Infrastructure.InMemory/
COPY src/Yukti.Analyzers/Yukti.Analyzers.csproj src/Yukti.Analyzers/
COPY src/Yukti.Api/Yukti.Api.csproj src/Yukti.Api/
COPY src/Yukti.Worker/Yukti.Worker.csproj src/Yukti.Worker/
COPY src/Yukti.Host/Yukti.Host.csproj src/Yukti.Host/
COPY tests/Yukti.Analyzers.Tests/Yukti.Analyzers.Tests.csproj tests/Yukti.Analyzers.Tests/
COPY tests/Yukti.Infrastructure.Tests/Yukti.Infrastructure.Tests.csproj tests/Yukti.Infrastructure.Tests/
COPY tests/Yukti.Orchestration.Tests/Yukti.Orchestration.Tests.csproj tests/Yukti.Orchestration.Tests/
COPY tools/Yukti.LoadTest/Yukti.LoadTest.csproj tools/Yukti.LoadTest/

# Restore the whole solution, not individual projects one at a time.
# Yukti.Analyzers is consumed as a Roslyn analyzer reference (not a normal
# ProjectReference), so a per-project `dotnet restore <csproj>` doesn't
# reliably generate its own project.assets.json — and Yukti.Api's publish
# needs that file to exist regardless. Solution-level restore covers every
# project in one pass and removes this whole class of "forgot project X" bug.
RUN dotnet restore Yukti.sln

# Copy full source and publish
COPY src/ src/
RUN dotnet publish src/Yukti.Api/Yukti.Api.csproj -c Release -o /out/api --no-restore
RUN dotnet publish src/Yukti.Worker/Yukti.Worker.csproj -c Release -o /out/worker --no-restore

# Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /out/api ./api
COPY --from=build /out/worker ./worker

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV Deployment__SelfHosted=true

ENTRYPOINT ["dotnet", "./api/Yukti.Api.dll"]
