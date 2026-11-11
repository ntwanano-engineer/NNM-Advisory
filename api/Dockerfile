# NNM Advisory API — multi-stage Docker build (Task 24.8 / Requirement 13.4)
#
# Stage 1 (sdk)    : restore + publish NnmAdvisory.Api.csproj in Release.
# Stage 2 (runtime): copy the published output onto the lean ASP.NET runtime
#                    image and run as a non-root user.
#
# The test project (NnmAdvisory.Tests) is excluded from the web build by the
# csproj's <Compile Remove> globbing and by .dockerignore, so publishing the
# single project file never drags the tests into the image.

# ---------------------------------------------------------------------------
# Stage 1: build
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file first and restore so dependency layers cache
# independently of source changes (faster incremental builds).
COPY NnmAdvisory.Api.csproj ./
RUN dotnet restore NnmAdvisory.Api.csproj

# Copy the remaining source and publish a Release build. Publishing the
# specific project (not the directory) keeps the test project out of the
# output even if .dockerignore is bypassed.
COPY . ./
RUN dotnet publish NnmAdvisory.Api.csproj -c Release -o /app/publish --no-restore

# ---------------------------------------------------------------------------
# Stage 2: runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Run as a non-root system user rather than root.
RUN addgroup --system nnm && adduser --system --ingroup nnm nnm

COPY --from=build /app/publish ./

# Serilog's File sink (local fallback per Requirement 10.8) writes to ./logs,
# relative to the working directory. Pre-create it and hand ownership to the
# non-root user so the sink can roll files; the primary sink is stdout →
# CloudWatch via the awslogs driver.
RUN mkdir -p /app/logs && chown -R nnm:nnm /app

USER nnm

# Kestrel binds to 8080 inside the container; the ALB target group and the ECS
# task definition both forward to this port. docker-compose maps host:5000 →
# container:8080 for local development.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "NnmAdvisory.Api.dll"]
