// NnmAdvisory.Api â€” entry point
//
// Layered architecture:
//   Controllers/    â€” HTTP request handlers
//   Services/       â€” business logic interfaces and implementations
//   Repositories/   â€” EF Core data-access interfaces and implementations
//   Models/         â€” EF Core entities, DTOs, and settings classes
//   Infrastructure/ â€” cross-cutting concerns (DbContext, email, image storage, auth)
//
// Task 2.2: Settings validation (DatabaseSettings, JwtSettings, EmailSettings,
//           S3Settings, CorsSettings) registered with ValidateOnStart().
// Task 2.3: Middleware pipeline â€” HTTPS enforcement, CORS, Serilog structured
//           JSON request logging, global exception handler.

using System.Text;
using dotenv.net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NnmAdvisory.Api.Infrastructure;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Repositories;
using NnmAdvisory.Api.Services;
using Serilog;

// ---------------------------------------------------------------------------
// 1. Load .env file for local development (no-op when variables are already
//    present, so this is safe to leave in production container images too).
// ---------------------------------------------------------------------------
DotEnv.Load(options: new DotEnvOptions(
    envFilePaths: new[] { Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".env") },
    ignoreExceptions: true));   // silently skip if file absent (CI, production)

// ---------------------------------------------------------------------------
// 2. Bootstrap Serilog from appsettings.json *before* the host is built so
//    that any startup errors are captured in the structured log.
// ---------------------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
            optional: true)
        .AddEnvironmentVariables()
        .Build())
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting NNM Advisory API");

    var builder = WebApplication.CreateBuilder(args);

    // -----------------------------------------------------------------------
    // 3. Replace the default logging provider with Serilog, reading the full
    //    configuration from appsettings.json (and overrides).
    //    Requirement 10.6: log all unhandled exceptions with UTC timestamp,
    //    request path, and error message.
    // -----------------------------------------------------------------------
    builder.Host.UseSerilog((context, services, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

    // -----------------------------------------------------------------------
    // 4. Map environment variables to configuration sections so that the
    //    strongly-typed settings classes receive their values.
    //
    //    Environment variable â†’ Configuration key mapping:
    //      DB_CONNECTION_STRING  â†’ Database:ConnectionString
    //      JWT_SECRET            â†’ Jwt:Secret
    //      EMAIL_PROVIDER        â†’ Email:Provider
    //      SENDER_EMAIL          â†’ Email:SenderEmail
    //      ADMIN_EMAIL           â†’ Email:AdminEmail
    //      SMTP_HOST             â†’ Email:SmtpHost
    //      SMTP_PORT             â†’ Email:SmtpPort
    //      S3_IMAGES_BUCKET      â†’ S3:ImagesBucket
    //      IMAGE_STORAGE_PROVIDERâ†’ S3:ImageStorageProvider
    //      CORS_ALLOWED_ORIGIN   â†’ Cors:AllowedOrigin
    // -----------------------------------------------------------------------
    builder.Configuration
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
        .AddEnvironmentVariables()
        .AddInMemoryCollection(MapEnvironmentVariables());

    // -----------------------------------------------------------------------
    // 4a. AWS Systems Manager Parameter Store (Task 24.12, Requirement 13.6).
    //
    //     In AWS, all sensitive runtime configuration is stored as encrypted
    //     SecureString parameters under the /nnm/* namespace (see infra SSM
    //     parameters, task 24.9). AddSystemsManager loads that whole subtree so
    //     the parameters arrive in configuration as hierarchical keys, e.g.
    //       /nnm/db/connection-string  â†’ "db:connection-string"
    //       /nnm/jwt/secret            â†’ "jwt:secret"
    //     The provider also polls every 5 minutes (reloadAfter) so rotated
    //     secrets are picked up without a redeploy.
    //
    //     GUARD: this must NOT run during local development or `dotnet build`
    //     (there is no AWS region / credentials there, and the ECS task already
    //     injects the same values as flat env vars via its `secrets` block,
    //     which MapEnvironmentVariables() handles). We therefore only attach
    //     the provider when explicitly opted-in:
    //       * USE_SSM=true, OR
    //       * running in the Production environment.
    //     Local dev (EMAIL_PROVIDER=smtp / IMAGE_STORAGE_PROVIDER=local, no
    //     USE_SSM, non-Production) is completely unaffected.
    //
    //     Because the SSM parameter names (db/connection-string) do not match
    //     the app's nested settings keys (Database:ConnectionString), we add an
    //     in-memory layer AFTER the SSM provider that re-maps each loaded /nnm
    //     key onto the key the IOptions binder expects. Any value already
    //     supplied as a flat env var (DB_CONNECTION_STRING, ...) still wins via
    //     MapEnvironmentVariables() above, so SSM acts as the source of truth
    //     when env-var secrets are absent.
    // -----------------------------------------------------------------------
    var useSsm = string.Equals(
                     Environment.GetEnvironmentVariable("USE_SSM"),
                     "true",
                     StringComparison.OrdinalIgnoreCase)
                 || builder.Environment.IsProduction();

    if (useSsm)
    {
        Log.Information("Loading configuration from AWS Systems Manager Parameter Store path /nnm");

        builder.Configuration.AddSystemsManager(source =>
        {
            source.Path = "/nnm";
            // Fail fast at startup if the namespace cannot be read â€” in AWS the
            // parameters are required for the app to bind its settings.
            source.Optional = false;
            // Poll for rotated secrets so a parameter update is picked up
            // without forcing a new deployment.
            source.ReloadAfter = TimeSpan.FromMinutes(5);
        });

        // Re-map the loaded /nnm/* hierarchy onto the nested settings keys the
        // IOptions binder reads (Database/Jwt/Email/S3/Cors). Only entries that
        // are not already provided as flat env vars are applied.
        builder.Configuration.AddInMemoryCollection(MapSsmParameters(builder.Configuration));
    }

    // -----------------------------------------------------------------------
    // 5. Register and validate strongly-typed settings (Task 2.2).
    //    ValidateOnStart() causes the application to fail fast at startup
    //    with a descriptive error if any required variable is absent or
    //    invalid â€” satisfying Requirement 12.6.
    // -----------------------------------------------------------------------
    builder.Services
        .AddOptions<DatabaseSettings>()
        .Bind(builder.Configuration.GetSection(DatabaseSettings.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services
        .AddOptions<JwtSettings>()
        .Bind(builder.Configuration.GetSection(JwtSettings.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services
        .AddOptions<EmailSettings>()
        .Bind(builder.Configuration.GetSection(EmailSettings.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services
        .AddOptions<S3Settings>()
        .Bind(builder.Configuration.GetSection(S3Settings.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services
        .AddOptions<CorsSettings>()
        .Bind(builder.Configuration.GetSection(CorsSettings.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // -----------------------------------------------------------------------
    // 6. Configure CORS policy (Task 2.3, Requirement 10.5).
    //    A single named policy "NnmFrontend" is defined here and applied in
    //    the middleware pipeline below.  Only the configured frontend origin
    //    is allowed; any other origin receives HTTP 403.
    //
    //    NOTE: CorsSettings must be validated before the policy is built.
    //    We resolve the settings synchronously via the service provider after
    //    app.Build().  To allow builder-time registration we read the raw
    //    config value here, then re-validate via the IOptions pipeline.
    // -----------------------------------------------------------------------
    var allowedOrigin = builder.Configuration[$"{CorsSettings.SectionName}:AllowedOrigin"]
                        ?? string.Empty;

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("NnmFrontend", policy =>
        {
            if (!string.IsNullOrWhiteSpace(allowedOrigin))
            {
                policy
                    .WithOrigins(allowedOrigin)
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            }
            // If origin is not yet configured (will be caught by ValidateOnStart),
            // the policy denies all origins â€” a safe default.
        });
    });

    // -----------------------------------------------------------------------
    // 7. Register NnmDbContext with Npgsql (Task 3.1).
    //    The connection string is sourced from DatabaseSettings, which is
    //    validated at startup (step 5 above). We read it directly from the
    //    configuration section so the DbContext is available before the
    //    IOptions pipeline resolves, while ValidateOnStart() still guards
    //    against a missing value.
    //
    //    Requirements 11.1, 11.2, 11.3: entity persistence, referential
    //    integrity, and parameterised queries via EF Core ORM.
    // -----------------------------------------------------------------------
    var connectionString = builder.Configuration[$"{DatabaseSettings.SectionName}:ConnectionString"]
                           ?? string.Empty;

    builder.Services.AddDbContext<NnmDbContext>(options =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history")
                .EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null)));

    // -----------------------------------------------------------------------
    // 8. Register repositories and other framework services.
    //
    //    CompanyProfileRepository is registered as Scoped so each HTTP
    //    request gets its own instance (aligned with NnmDbContext lifetime).
    //    (Task 4.1 â€” Requirements 10.1, 11.3)
    // -----------------------------------------------------------------------
    builder.Services.AddScoped<ICompanyProfileRepository, CompanyProfileRepository>();

    // AdminUserRepository supports the admin authentication flow (lookup by
    // username, persist lockout / failed-attempt state). Scoped lifetime aligns
    // with the per-request NnmDbContext. (Task 9.1 â€” Requirements 8.7, 11.3)
    builder.Services.AddScoped<IAdminUserRepository, AdminUserRepository>();

    // AuthService validates credentials via bcrypt, enforces the 15-minute
    // lockout after 5 consecutive failures, and issues HS256 JWTs signed with
    // the secret from JwtSettings. (Task 9.2 â€” Requirements 8.1, 8.4, 8.5, 8.6, 8.7)
    builder.Services.AddScoped<IAuthService, AuthService>();

    // ContactRepository persists contact form submissions. Scoped lifetime
    // aligns with the per-request NnmDbContext. (Task 5.1 â€” Requirements 11.3, 11.5)
    builder.Services.AddScoped<IContactRepository, ContactRepository>();

    // ContactService validates contact form fields, persists valid submissions
    // via IContactRepository, then dispatches acknowledgement + admin emails
    // best-effort. Scoped lifetime aligns with the per-request repository and
    // email service. (Task 5.2 â€” Requirements 5.2, 5.3, 5.6, 5.7)
    builder.Services.AddScoped<IContactService, ContactService>();

    // SurveyRepository persists customer and employee survey responses.
    // Scoped lifetime aligns with the per-request NnmDbContext.
    // (Task 6.1 â€” Requirements 7.6, 11.1)
    builder.Services.AddScoped<ISurveyRepository, SurveyRepository>();

    // SurveyService validates customer and employee survey submissions and
    // persists valid responses via ISurveyRepository (customer scores in range,
    // employee required answers present, no PII persisted). Scoped lifetime
    // aligns with the scoped repository.
    // (Task 6.2 â€” Requirements 6.3, 6.4, 7.3, 7.4, 7.6)
    builder.Services.AddScoped<ISurveyService, SurveyService>();

    // CompanyProfileService caches profile/services/leadership reads in a
    // shared in-memory cache with a 5-second TTL (Task 4.2 â€” Requirement 3.8).
    // AddMemoryCache registers the process-wide IMemoryCache singleton the
    // service depends on; the service itself is Scoped so it can take the
    // scoped repository.
    builder.Services.AddMemoryCache();
    builder.Services.AddScoped<ICompanyProfileService, CompanyProfileService>();

    // -----------------------------------------------------------------------
    // Email delivery (Task 7). The active IEmailService implementation is
    // selected at startup from the EMAIL_PROVIDER value (Email:Provider):
    //   "ses"  â†’ AwsSesEmailService (production, AWS SES v2)
    //   "smtp" â†’ SmtpEmailService   (local development / Mailhog â€” Task 7.3)
    // EmailSettings.Provider is already validated via ValidateOnStart() above,
    // so only "smtp" or "ses" can reach this point.
    var emailProvider = builder.Configuration[$"{EmailSettings.SectionName}:Provider"]
                        ?? string.Empty;

    if (string.Equals(emailProvider, "ses", StringComparison.OrdinalIgnoreCase))
    {
        // The SES v2 client is registered as a singleton (it is thread-safe
        // and intended for reuse). Constructed with no arguments, it resolves
        // credentials via the AWS SDK default provider chain (ECS task role in
        // production) and the region from the standard environment/metadata â€”
        // no access keys are ever placed in configuration.
        builder.Services.AddSingleton<Amazon.SimpleEmailV2.IAmazonSimpleEmailServiceV2>(
            _ => new Amazon.SimpleEmailV2.AmazonSimpleEmailServiceV2Client());
        builder.Services.AddScoped<IEmailService, AwsSesEmailService>();
    }
    else
    {
        // Local development / Mailhog. SmtpEmailService uses the built-in
        // System.Net.Mail.SmtpClient to deliver mail to the relay configured
        // via SMTP_HOST / SMTP_PORT (Email:SmtpHost / Email:SmtpPort). Mailhog
        // captures every message in its web UI instead of relaying externally.
        // Email:Provider is validated to be "smtp" or "ses" at startup, so any
        // non-"ses" value reaching this branch is "smtp". (Task 7.3 â€” Req 12.2)
        builder.Services.AddScoped<IEmailService, SmtpEmailService>();
    }

    // -----------------------------------------------------------------------
    // Image storage (Task 8). The active IImageStorageService implementation
    // is selected at startup from the IMAGE_STORAGE_PROVIDER value
    // (S3:ImageStorageProvider):
    //   "s3"    â†’ S3ImageStorageService (production, AWS S3 â€” Task 8.2)
    //   "local" â†’ local filesystem provider (development â€” Task 8.3)
    // S3Settings.ImageStorageProvider is already validated via ValidateOnStart()
    // above, so only "s3" or "local" can reach this point.
    var imageStorageProvider = builder.Configuration[$"{S3Settings.SectionName}:ImageStorageProvider"]
                               ?? string.Empty;
    var useLocalImageStorage =
        !string.Equals(imageStorageProvider, "s3", StringComparison.OrdinalIgnoreCase);

    if (string.Equals(imageStorageProvider, "s3", StringComparison.OrdinalIgnoreCase))
    {
        // The S3 client is registered as a singleton (it is thread-safe and
        // intended for reuse). Constructed with no arguments, it resolves
        // credentials via the AWS SDK default provider chain (ECS task role in
        // production) and the region from the standard environment/metadata â€”
        // no access keys are ever placed in configuration.
        builder.Services.AddSingleton<Amazon.S3.IAmazonS3>(
            _ => new Amazon.S3.AmazonS3Client());
        builder.Services.AddScoped<IImageStorageService, S3ImageStorageService>();
    }
    else
    {
        // Local development. LocalFileImageStorageService writes validated
        // uploads to the directory named by S3:LocalStoragePath (defaults to
        // "uploads" under the working directory) and serves them via the
        // "/uploads" static path. S3:ImageStorageProvider is validated to be
        // "s3" or "local" at startup, so any non-"s3" value reaching this
        // branch is "local". (Task 8.3 â€” Req 12.2)
        builder.Services.AddScoped<IImageStorageService, LocalFileImageStorageService>();
    }

    // -----------------------------------------------------------------------
    // JWT bearer authentication (Task 9.4 â€” Requirements 8.1, 8.2).
    //
    // AuthService (Task 9.2) signs HS256 tokens using the symmetric secret
    // from JwtSettings.Secret and stamps them with the Issuer and Audience
    // from JwtSettings, plus a notBefore/expires lifetime. The validation
    // parameters below MUST mirror those exactly so that a token issued by
    // AuthService is accepted and anything else is rejected with HTTP 401.
    //
    // The secret is read directly from configuration here (the same source the
    // JwtSettings IOptions pipeline binds); ValidateOnStart() on JwtSettings
    // (step 5) still guards against a missing or too-short secret at startup.
    // Endpoints decorated with [Authorize] require a valid bearer token:
    // requests with a missing/invalid token receive HTTP 401, and a valid
    // token lacking the required policy/role receives HTTP 403.
    // -----------------------------------------------------------------------
    var jwtSecret = builder.Configuration[$"{JwtSettings.SectionName}:Secret"] ?? string.Empty;
    var jwtIssuer = builder.Configuration[$"{JwtSettings.SectionName}:Issuer"] ?? string.Empty;
    var jwtAudience = builder.Configuration[$"{JwtSettings.SectionName}:Audience"] ?? string.Empty;

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                // Signature â€” reject any token not signed with our HS256 secret.
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtSecret)),

                // Issuer / Audience â€” must match the values AuthService stamps.
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,
                ValidateAudience = true,
                ValidAudience = jwtAudience,

                // Lifetime â€” reject expired tokens; no leeway so the 60-minute
                // expiry is honoured precisely.
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });

    // Authorisation services back the [Authorize] attribute applied to admin
    // endpoints (e.g. AdminController in Task 10.3).
    builder.Services.AddAuthorization();

    builder.Services.AddControllers();

    // -----------------------------------------------------------------------
    // Swagger / OpenAPI (API documentation).
    //
    // Exposes an interactive Swagger UI and the generated OpenAPI document so
    // the API's endpoints can be explored and exercised. A JWT bearer security
    // definition is registered so the protected admin endpoints can be called
    // from the UI by pasting a token obtained from POST /api/auth/login.
    // -----------------------------------------------------------------------
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "NNM Advisory API",
            Version = "v1",
            Description =
                "Public and admin endpoints for the NNM Advisory website: company " +
                "profile content, service blocks, leadership profiles, contact and " +
                "survey submissions, and authenticated admin content management.",
        });

        // JWT bearer auth so [Authorize] admin endpoints are callable from the UI.
        var jwtScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Paste the JWT returned by POST /api/auth/login (no \"Bearer \" prefix).",
            Reference = new Microsoft.OpenApi.Models.OpenApiReference
            {
                Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                Id = "Bearer",
            },
        };
        options.AddSecurityDefinition("Bearer", jwtScheme);
        options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            [jwtScheme] = Array.Empty<string>(),
        });
    });

    // -----------------------------------------------------------------------
    // 9. Build the application and configure the middleware pipeline.
    // -----------------------------------------------------------------------
    var app = builder.Build();

    // -----------------------------------------------------------------------
    // 8b. Migrate-and-exit path (Task 24.12, Requirement 11.4).
    //
    //     The runtime container image does not ship the `dotnet ef` tooling, so
    //     EF Core migrations are applied by the app itself. When RUN_MIGRATIONS
    //     is set to "true", the application applies every pending migration in
    //     ascending order via Database.Migrate() against RDS, then exits WITHOUT
    //     starting Kestrel. The deployment pipeline runs a one-off ECS task with
    //     this flag set (see infra/terraform/ecs_migrations.tf) and waits for it
    //     to exit 0 BEFORE promoting the new API service revision.
    //
    //     GUARD: normal startup (RUN_MIGRATIONS unset/false) is unaffected â€” the
    //     app proceeds to configure the middleware pipeline and serve requests.
    //     Local `dotnet build` never sets this flag, so the build/startup path
    //     is unchanged.
    // -----------------------------------------------------------------------
    var runMigrations = string.Equals(
        Environment.GetEnvironmentVariable("RUN_MIGRATIONS"),
        "true",
        StringComparison.OrdinalIgnoreCase);

    if (runMigrations)
    {
        Log.Information("RUN_MIGRATIONS=true â€” applying EF Core migrations and exiting");

        using var migrationScope = app.Services.CreateScope();
        var dbContext = migrationScope.ServiceProvider.GetRequiredService<NnmDbContext>();

        var pending = (await dbContext.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            Log.Information("Database schema is already up to date â€” no pending migrations");
        }
        else
        {
            Log.Information(
                "Applying {PendingCount} pending migration(s): {Migrations}",
                pending.Count,
                string.Join(", ", pending));
            await dbContext.Database.MigrateAsync();
            Log.Information("Successfully applied all pending migrations");
        }

        // Exit 0 so the one-off ECS migration task is reported as succeeded and
        // the pipeline can proceed to promote the new API revision.
        return 0;
    }

    // 9a. Global exception handler â€” MUST be registered first so it wraps the
    //     entire pipeline. Catches any unhandled exception, logs it with UTC
    //     timestamp + request path, and returns HTTP 500 with a generic body.
    //     (Requirement 10.6)
    app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

    // 9b. HTTPS enforcement â€” redirects any plaintext HTTP request to HTTPS.
    //     In production the ALB also enforces HTTPS-only at the edge
    //     (Requirement 13.7), making this a secondary guard inside the container.
    //     Disabled in Development to allow plain http://localhost:5000 during
    //     local work (Docker Compose terminates TLS at the container boundary).
    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }

    // 9c. Serilog request logging â€” emits one structured log entry per HTTP
    //     request including method, path, status code, and elapsed time.
    //     Logs are written to stdout as JSON in production (â†’ CloudWatch)
    //     and as human-readable text in Development.
    //     (Requirement 10.6)
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

        // Enrich each request log entry with additional context.
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("RequestTimestamp", DateTime.UtcNow.ToString("O"));
        };
    });

    // 9c-swagger. Swagger / OpenAPI UI (Development only).
    //     Serves the generated OpenAPI document at /swagger/v1/swagger.json and
    //     the interactive UI at /swagger. Enabled only outside Production so the
    //     public-facing API surface is not exposed in a deployed environment.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "NNM Advisory API v1");
            options.DocumentTitle = "NNM Advisory API — Swagger";
            // Serve the UI at /swagger (the default).
            options.RoutePrefix = "swagger";
        });
    }

    // 9c-bis. Local image static serving (development only).
    //     When the local image storage provider is active, leadership profile
    //     photographs are stored on disk under S3:LocalStoragePath and exposed
    //     by LocalFileImageStorageService as "/uploads/{key}" URLs. Serve that
    //     directory as static files so the pre-signed URLs resolve in local
    //     development. In production (S3 provider) this is skipped â€” photos are
    //     served via pre-signed S3 URLs instead. This serves static assets only
    //     and does not affect application/API routing.
    if (useLocalImageStorage)
    {
        var localStoragePath =
            builder.Configuration[$"{S3Settings.SectionName}:LocalStoragePath"];
        if (string.IsNullOrWhiteSpace(localStoragePath))
        {
            localStoragePath = "uploads";
        }

        var uploadsRoot = Path.IsPathRooted(localStoragePath)
            ? localStoragePath
            : Path.Combine(Directory.GetCurrentDirectory(), localStoragePath);

        // Ensure the directory exists so the file provider can be created even
        // before the first upload.
        Directory.CreateDirectory(uploadsRoot);

        // Publish bundled seed assets (leadership profile photos) into the
        // uploads directory so the photos seeded by the SeedLeadershipProfiles
        // migration resolve on first run. Existing files are not overwritten,
        // so an admin re-upload via the Admin Page always takes precedence.
        var seedAssetsRoot =
            Path.Combine(AppContext.BaseDirectory, "SeedAssets");
        if (Directory.Exists(seedAssetsRoot))
        {
            foreach (var sourceFile in Directory.EnumerateFiles(
                         seedAssetsRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(seedAssetsRoot, sourceFile);
                var targetFile = Path.Combine(uploadsRoot, relative);
                var targetDir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                if (!File.Exists(targetFile))
                {
                    File.Copy(sourceFile, targetFile);
                }
            }
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider =
                new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsRoot),
            RequestPath = "/uploads",
        });
    }

    // 9d. CORS middleware â€” must appear before routing/auth/controllers so that
    //     preflight OPTIONS requests are handled before any auth checks.
    //     (Requirement 10.5)
    app.UseCors("NnmFrontend");

    // 9e. Authentication + Authorisation (Task 9.4 â€” Requirements 8.1, 8.2).
    //     UseAuthentication validates the JWT bearer token (per the parameters
    //     configured above) and populates HttpContext.User; UseAuthorization
    //     then enforces [Authorize] on protected admin endpoints. Both run
    //     after CORS (so preflight OPTIONS requests are not blocked) and before
    //     endpoint mapping, as required for the auth result to be available to
    //     the controllers.
    app.UseAuthentication();
    app.UseAuthorization();

    // 9f. Map controller endpoints.
    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // Capture fatal startup errors (e.g. missing required config) in the log
    // before the process exits so operators can diagnose the failure.
    Log.Fatal(ex, "Application startup failed: {ErrorMessage}", ex.Message);
    return 1;
}
finally
{
    // Flush and close all Serilog sinks on shutdown to avoid dropped log lines.
    await Log.CloseAndFlushAsync();
}

return 0;

// ---------------------------------------------------------------------------
// Helper: map flat environment variable names to nested config key paths so
// that the strongly-typed settings binder can find the right values.
// ---------------------------------------------------------------------------
static IEnumerable<KeyValuePair<string, string?>> MapEnvironmentVariables()
{
    var map = new Dictionary<string, string?>
    {
        ["Database:ConnectionString"] = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING"),
        ["Jwt:Secret"]                = Environment.GetEnvironmentVariable("JWT_SECRET"),
        ["Email:Provider"]            = Environment.GetEnvironmentVariable("EMAIL_PROVIDER"),
        ["Email:SenderEmail"]         = Environment.GetEnvironmentVariable("SENDER_EMAIL"),
        ["Email:AdminEmail"]          = Environment.GetEnvironmentVariable("ADMIN_EMAIL"),
        ["Email:SmtpHost"]            = Environment.GetEnvironmentVariable("SMTP_HOST"),
        ["Email:SmtpPort"]            = Environment.GetEnvironmentVariable("SMTP_PORT"),
        ["S3:ImageStorageProvider"]   = Environment.GetEnvironmentVariable("IMAGE_STORAGE_PROVIDER"),
        ["S3:ImagesBucket"]           = Environment.GetEnvironmentVariable("S3_IMAGES_BUCKET"),
        ["Cors:AllowedOrigin"]        = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGIN"),
    };

    // Only yield entries that have an actual value so we don't overwrite
    // appsettings.json defaults with null.
    return map.Where(kv => kv.Value is not null);
}

// ---------------------------------------------------------------------------
// Helper: map the keys loaded from the /nnm SSM subtree (via AddSystemsManager)
// onto the nested settings keys the IOptions binder expects.
//
//   SSM parameter name          loaded config key          target settings key
//   /nnm/db/connection-string â†’ db:connection-string     â†’ Database:ConnectionString
//   /nnm/jwt/secret           â†’ jwt:secret               â†’ Jwt:Secret
//   /nnm/ses/sender-email     â†’ ses:sender-email         â†’ Email:SenderEmail
//   /nnm/ses/admin-email      â†’ ses:admin-email          â†’ Email:AdminEmail
//   /nnm/s3/images-bucket     â†’ s3:images-bucket         â†’ S3:ImagesBucket
//   /nnm/cors/allowed-origin  â†’ cors:allowed-origin      â†’ Cors:AllowedOrigin
//
// A value already present at the target key (e.g. injected as the flat env var
// DB_CONNECTION_STRING by the ECS task definition) takes precedence and is not
// overwritten, so SSM only fills in settings that are otherwise unset.
// ---------------------------------------------------------------------------
static IEnumerable<KeyValuePair<string, string?>> MapSsmParameters(IConfiguration configuration)
{
    // sourceKey (as loaded from the /nnm subtree) â†’ target settings key.
    var mappings = new (string Source, string Target)[]
    {
        ("db:connection-string", "Database:ConnectionString"),
        ("jwt:secret",           "Jwt:Secret"),
        ("ses:sender-email",     "Email:SenderEmail"),
        ("ses:admin-email",      "Email:AdminEmail"),
        ("s3:images-bucket",     "S3:ImagesBucket"),
        ("cors:allowed-origin",  "Cors:AllowedOrigin"),
    };

    foreach (var (source, target) in mappings)
    {
        var value = configuration[source];

        // Skip when SSM did not supply the parameter, or when the target key is
        // already set (flat env var wins).
        if (string.IsNullOrEmpty(value) || !string.IsNullOrEmpty(configuration[target]))
        {
            continue;
        }

        yield return new KeyValuePair<string, string?>(target, value);
    }
}
