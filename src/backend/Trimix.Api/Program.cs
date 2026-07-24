using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Fido2NetLib;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Trimix.Api;
using Trimix.Api.Analytics;
using Trimix.Api.Auth;
using Trimix.Api.Billing;
using Trimix.Api.Data;
using Trimix.Api.Endpoints;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;

var builder = WebApplication.CreateBuilder(args);

// --- Data. PostgreSQL is the canonical runtime provider (docker-compose, Npgsql migrations
// checked in). Integration tests swap these options for SQLite in-memory (kept-open connection).
// The connection string is a deploy input (dev value in appsettings.Development.json; production via
// environment); fail closed if it is absent — mirroring the Jwt:Secret guard below — so a mis-provisioned
// host never silently binds a committed credential.
var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(postgresConnectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Postgres must be configured (dev value ships in appsettings.Development.json; provide via environment elsewhere).");
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(postgresConnectionString));

// --- Auth (architecture overview §8): PasswordHasher (PBKDF2) + HS256 JWT, 30-day.
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.Secret))
{
    throw new InvalidOperationException(
        "Jwt:Secret must be configured (dev value ships in appsettings.Development.json; provide via environment elsewhere).");
}

// HS256 needs a >=256-bit (32-byte) key (see TokenService); a short secret is offline-brute-forceable.
if (Encoding.UTF8.GetByteCount(jwtOptions.Secret) < JwtOptions.MinSecretBytes)
{
    throw new InvalidOperationException(
        $"Jwt:Secret must be at least {JwtOptions.MinSecretBytes} bytes for HS256; the configured secret is too short.");
}

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            NameClaimType = TrimixClaims.Subject,
            RoleClaimType = TrimixClaims.Role,
        };
    });

builder.Services.AddAuthorization(options =>
    options.AddPolicy(AuthPolicies.Moderator, policy => policy.RequireRole(UserRoles.Moderator)));

// --- Passkeys (WebAuthn/FIDO2 relying party). RP id + origins come from Passkey:* config; the
// defaults (localhost / https://localhost) let local dev and hermetic tests drive the flow. Real
// runtime validation is domain/cert-dependent (see PasskeyOptions / PasskeyEndpoints).
var passkeyOptions = builder.Configuration.GetSection(PasskeyOptions.SectionName).Get<PasskeyOptions>() ?? new PasskeyOptions();
builder.Services.AddSingleton(passkeyOptions);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IFido2>(_ => new Fido2(new Fido2Configuration
{
    ServerDomain = passkeyOptions.ServerDomain,
    ServerName = passkeyOptions.ServerName,
    Origins = passkeyOptions.Origins,
}));

// --- Receipt verification registry (architecture overview §3): the sandbox verifier mints the
// slice's live entitlements from an HMAC secret whose placeholder is committed to the repo — so it
// is registered ONLY in Development or behind an explicit server-side opt-in, never by default in a
// deployed environment.
var sandboxBillingEnabled = builder.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>(SandboxReceiptVerifier.EnabledConfigKey);
if (sandboxBillingEnabled)
{
    builder.Services.AddSingleton<IReceiptVerifier, SandboxReceiptVerifier>();
}

// google_play + app_store validate against the real vendor APIs, config-driven (Billing:GooglePlay:*
// / Billing:AppStore:*). Credentials are deploy inputs (empty by default): with none present each
// verifier returns NotConfigured (⇒ endpoint 501), so a dev host without store creds is unaffected.
// All vendor HTTP flows through a named IHttpClientFactory client so the path is unit-testable.
builder.Services.AddHttpClient(GooglePlayApiReceiptVerifier.HttpClientName);
builder.Services.AddSingleton<IReceiptVerifier, GooglePlayApiReceiptVerifier>();

builder.Services.AddHttpClient(AppleAppStoreReceiptVerifier.HttpClientName);
builder.Services.AddSingleton<IReceiptVerifier, AppleAppStoreReceiptVerifier>();

// --- Server-emitted funnel events (validated against the frozen contract).
builder.Services.AddScoped<ServerAnalytics>();

// --- Abuse controls: throttle the unauthenticated analytics ingest and rate-limit auth login
// (online password brute-force / credential stuffing). Fixed-window, partitioned by client IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimitPolicies.Login, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitPolicies.PartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = RateLimitPolicies.LoginPermitLimit,
                Window = RateLimitPolicies.Window,
                QueueLimit = 0,
            }));

    options.AddPolicy(RateLimitPolicies.AnalyticsIngest, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitPolicies.PartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = RateLimitPolicies.AnalyticsPermitLimit,
                Window = RateLimitPolicies.Window,
                QueueLimit = 0,
            }));
});

var app = builder.Build();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Schema management: Development auto-migrates the canonical Postgres database; SQLite (integration
// tests) uses EnsureCreated on the kept-open in-memory connection. Production migrates via pipeline.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsNpgsql())
    {
        if (app.Environment.IsDevelopment())
        {
            db.Database.Migrate();
        }
    }
    else
    {
        db.Database.EnsureCreated();
    }

    if (app.Environment.IsDevelopment())
    {
        // Dev-seeded moderator account (architecture overview §8). Development only.
        DevSeeder.SeedModerator(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>());
    }
}

var api = app.MapGroup(ApiRoutes.Root);

// Liveness + db check.
api.MapGet(ApiRoutes.Healthz, async (AppDbContext db, CancellationToken cancellationToken) =>
{
    var dbOk = await db.Database.CanConnectAsync(cancellationToken);
    var response = new HealthzResponse(dbOk ? "ok" : "degraded", dbOk ? "ok" : "unreachable");
    return dbOk ? Results.Ok(response) : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
});

// App-association documents live at the site root, not under /api/v1.
app.MapWellKnownEndpoints();

api.MapAuthEndpoints();
api.MapPasskeyEndpoints();
api.MapEntitlementsEndpoints();
api.MapPurchasesEndpoints();
api.MapDivesEndpoints();
api.MapSitesEndpoints();
api.MapModerationEndpoints();
api.MapAnalyticsEndpoints();
api.MapPrivacyEndpoints();

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests (Trimix.Api.Tests).</summary>
public partial class Program;
