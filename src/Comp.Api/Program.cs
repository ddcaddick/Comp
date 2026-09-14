using System.Text;
using System.Threading.RateLimiting;
using Comp.Api.Security;
using Comp.Api.Validation;
using Comp.Application.Abstractions;
using Comp.Application.Validation;
using Comp.Contracts.Auth;
using Comp.Infrastructure;
using Comp.Infrastructure.Identity;
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
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

// Access tokens are short-lived (15 minutes); the mobile app stays signed in via a
// rotating refresh token instead.
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
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

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    at = DateTimeOffset.UtcNow
}));

var auth = app.MapGroup("/auth");

auth.MapPost("/login", async (LoginRequest request, IAuthService authService, CancellationToken ct) =>
        (await authService.LoginAsync(request, ct)) switch
        {
            AuthResult.Success success => Results.Ok(success.Tokens),
            AuthResult.Failure failure => Results.Problem(detail: failure.Reason, statusCode: StatusCodes.Status401Unauthorized),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
        })
    .AddEndpointFilter<ValidationFilter<LoginRequest>>()
    .RequireRateLimiting("auth")
    .AllowAnonymous();

auth.MapPost("/refresh", async (RefreshRequest request, IAuthService authService, CancellationToken ct) =>
        (await authService.RefreshAsync(request, ct)) switch
        {
            AuthResult.Success success => Results.Ok(success.Tokens),
            AuthResult.Failure failure => Results.Problem(detail: failure.Reason, statusCode: StatusCodes.Status401Unauthorized),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
        })
    .AddEndpointFilter<ValidationFilter<RefreshRequest>>()
    .RequireRateLimiting("auth")
    .AllowAnonymous();

app.Run();

// Exposed so WebApplicationFactory<Program> can host this app from integration tests.
public partial class Program;
