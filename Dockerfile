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
COPY src/Yukti.Cli/Yukti.Cli.csproj src/Yukti.Cli/
COPY demo/fixtures/FixtureApp/FixtureApp.csproj demo/fixtures/FixtureApp/
COPY tests/Yukti.Analyzers.Tests/Yukti.Analyzers.Tests.csproj tests/Yukti.Analyzers.Tests/
COPY tests/Yukti.Infrastructure.Tests/Yukti.Infrastructure.Tests.csproj tests/Yukti.Infrastructure.Tests/
COPY tests/Yukti.Orchestration.Tests/Yukti.Orchestration.Tests.csproj tests/Yukti.Orchestration.Tests/
COPY tests/Yukti.Cli.Tests/Yukti.Cli.Tests.csproj tests/Yukti.Cli.Tests/
COPY tools/Yukti.LoadTest/Yukti.LoadTest.csproj tools/Yukti.LoadTest/
# D6-T8: added to Yukti.sln the same session this Dockerfile's own comment below warns about —
# "forgot project X" is exactly what a solution-level restore needs every project's .csproj
# copied in first to avoid; this line is why that comment is correct to worry about it.
COPY benchmarks/DispatchBenchmark/DispatchBenchmark.csproj benchmarks/DispatchBenchmark/

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
# D6-T6: curl backs docker-compose.yml's api healthcheck (/health) — the base aspnet image doesn't
# include it. worker's own image shares this stage too, which is harmless (worker never runs curl).
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/api ./api
COPY --from=build /out/worker ./worker
COPY docker-entrypoint.sh ./docker-entrypoint.sh
RUN chmod +x ./docker-entrypoint.sh

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV Deployment__SelfHosted=true

ENTRYPOINT ["./docker-entrypoint.sh"]
CMD ["./api/Yukti.Api.dll"]
