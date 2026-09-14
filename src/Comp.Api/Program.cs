
var builder = WebApplication.CreateBuilder(args);

// The Vite dev server runs on 5173. The Expo app talks to the API over the LAN
// and does not send an Origin header, so it needs no entry here.
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    at = DateTimeOffset.UtcNow
}));

app.Run();