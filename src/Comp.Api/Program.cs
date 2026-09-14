using System.Text;
using Comp.Api.Security;
using Comp.Application.Abstractions;
using Comp.Infrastructure;
using Comp.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
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

// Access tokens are short-lived (15 minutes); the mobile app stays signed in via a
// rotating refresh token instead. No endpoint issues a token yet — this only wires the
// validation side of the pipeline so authorisation policies below have something to run.
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

var app = builder.Build();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    at = DateTimeOffset.UtcNow
}));

app.Run();