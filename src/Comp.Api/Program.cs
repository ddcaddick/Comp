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

var builder = WebApplication.CreateBuilder(args);

// The Vite dev server runs on 5173. The Expo app talks to the API over the LAN
// and does not send an Origin header, so it needs no entry here.
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();

// The interceptor is scoped (it needs the current request's user), so it must be resolved
// from the provider passed into this callback rather than captured once at startup.
builder.Services.AddDbContext<CompDbContext>((sp, options) =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("Default"))
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
