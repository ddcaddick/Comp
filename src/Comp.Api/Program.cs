using System.Text;
using System.Threading.RateLimiting;
using Comp.Api.Endpoints;
using Comp.Api.Security;
using Comp.Application.Abstractions;
using Comp.Application.Validation;
using Comp.Infrastructure;
using Comp.Infrastructure.Competitions;
using Comp.Infrastructure.Events;
using Comp.Infrastructure.Identity;
using Comp.Infrastructure.Leagues;
using Comp.Infrastructure.Shooters;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Locally this is already the Host=...;Port=...; form Npgsql expects. Render's managed
// Postgres (and most other hosts) hand out a postgres:// URI instead, which UseNpgsql
// cannot parse directly -- converted here rather than asking every environment to agree
// on one format.
static string NormalizeConnectionString(string raw)
{
    if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return raw;
    }

    var uri = new Uri(raw);
    var userInfo = uri.UserInfo.Split(':', 2);
    var connectionStringBuilder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = uri.AbsolutePath.TrimStart('/'),
        Username = Uri.UnescapeDataString(userInfo[0]),
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        SslMode = SslMode.Require
    };
    return connectionStringBuilder.ConnectionString;
}

// A comma-separated list so a staging deployment can allow its own web origin alongside
// (or instead of) the local Vite dev server -- the Expo app talks to the API directly and
// sends no Origin header, so it needs no entry here regardless of environment.
var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();

// The interceptor is scoped (it needs the current request's user), so it must be resolved
// from the provider passed into this callback rather than captured once at startup.
builder.Services.AddDbContext<CompDbContext>((sp, options) =>
    options
        .UseNpgsql(NormalizeConnectionString(builder.Configuration.GetConnectionString("Default")!))
        .UseSnakeCaseNamingConvention()
        .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));

builder.Services
    .AddIdentityCore<AppUser>(options => options.User.RequireUniqueEmail = true)
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<CompDbContext>()
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<JwtAccessTokenGenerator>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IShooterService, ShooterService>();
builder.Services.AddScoped<ICompetitionService, CompetitionService>();
builder.Services.AddScoped<ILeagueService, LeagueService>();
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<IEventParticipantService, EventParticipantService>();
builder.Services.AddScoped<ISquadService, SquadService>();
builder.Services.AddScoped<IRunnerService, RunnerService>();
builder.Services.AddScoped<IRunService, RunService>();
builder.Services.AddScoped<IEntrySessionService, EntrySessionService>();
builder.Services.AddScoped<IResultsService, ResultsService>();
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

// Access tokens are short-lived (15 minutes); the mobile app stays signed in via a
// rotating refresh token instead.
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim types exactly as JwtAccessTokenGenerator issued them (e.g. "sub"
        // rather than a remapped ClaimTypes.NameIdentifier) so there is one unambiguous
        // name to read them back by, regardless of handler defaults.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SigningKey"]!)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization(options =>
    options.AddPolicy("CanAmendPublished", policy =>
        policy.RequireClaim(AppUserClaimsPrincipalFactory.AmendPublishedClaimType, "true")));

// The document this produces is also written to disk at build time (see the
// OpenApiDocumentsDirectory property in Comp.Api.csproj) — that copy is what
// tools/generate-api-types reads to produce clients/mobile/api/api-types.ts.
builder.Services.AddOpenApi();

// Partitioned by client IP so one caller hammering /auth/login can't lock everyone else
// out of it too.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

// Off by default -- locally and in tests, migrations are applied explicitly (`dotnet ef
// database update`, or the test's own MigrateAsync). A deployed environment with no shell
// access to run that command opts in with RunMigrationsOnStartup=true instead. Safe to
// run on every boot: EF Core's migrator is a no-op once the database is already current.
if (builder.Configuration.GetValue<bool>("RunMigrationsOnStartup"))
{
    using var migrationScope = app.Services.CreateScope();
    await migrationScope.ServiceProvider.GetRequiredService<CompDbContext>().Database.MigrateAsync();
}

// There is no registration endpoint and no seed data by design (user creation is otherwise
// always an authenticated Super Admin/Admin/Official action), so a fresh database has no
// account anyone could sign in with at all. This creates exactly one bootstrap Super Admin,
// and only: locally in Development (the fixed dev-admin@comp.local / Comp1234! convenience
// this project has always used), or wherever SeedInitialAdmin=true is set explicitly and an
// InitialAdmin:Email/:Password have been configured -- e.g. a staging deployment reachable
// from the internet, where the well-known local dev credentials must never apply. Runs once
// (a no-op the moment any user exists) and is skipped whenever migrations haven't been
// applied yet: Comp.Api.Tests' own WebApplicationFactory sets Development too, and starts
// the host (running this) against a brand-new Testcontainers database *before* its own
// MigrateAsync call — querying Users here first would hit "relation asp_net_users does not
// exist" and crash every test. GetAppliedMigrationsAsync tolerates a missing history table
// (returns empty) rather than throwing, unlike a direct query against a table that isn't
// there yet.
var seedInitialAdmin = app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("SeedInitialAdmin");
if (seedInitialAdmin)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CompDbContext>();
    var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();
    if (appliedMigrations.Any() && !await dbContext.Users.AnyAsync())
    {
        var email = builder.Configuration["InitialAdmin:Email"] ?? "dev-admin@comp.local";
        var password = builder.Configuration["InitialAdmin:Password"] ?? "Comp1234!";

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var initialAdmin = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Admin" };
        dbContext.PendingActorOverride = initialAdmin.Id;
        await userManager.CreateAsync(initialAdmin, password);
        dbContext.PendingActorOverride = initialAdmin.Id;
        await userManager.AddToRoleAsync(initialAdmin, Roles.SuperAdmin);
    }
}

app.UseCors();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    at = DateTimeOffset.UtcNow
}));

app.MapAuthEndpoints();
app.MapShooterEndpoints();
app.MapCompetitionEndpoints();
app.MapLeagueEndpoints();
app.MapEventEndpoints();
app.MapLiveEntryEndpoints();

app.Run();

// Exposed so WebApplicationFactory<Program> can host this app from integration tests.
public partial class Program;
