using KiwiAuth.Data;
using KiwiAuth.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─── CORS ────────────────────────────────────────────────────────────────────
// AllowCredentials() is required for the refresh token HttpOnly cookie to work
// across origins. Pair this with SameSite=None; Secure on the cookie for cross-origin SPAs.

var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// ─── Database ────────────────────────────────────────────────────────────────

builder.Services.AddDbContext<KiwiDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("Default") ?? "Data Source=kiwiauth.db"));

// ─── KiwiAuth ────────────────────────────────────────────────────────────────

builder.Services.AddKiwiAuth(options =>
{
    options.Jwt.Issuer    = builder.Configuration["KiwiAuth:Jwt:Issuer"] ?? "KiwiAuth.Sample";
    options.Jwt.Audience  = builder.Configuration["KiwiAuth:Jwt:Audience"] ?? "KiwiAuth.Client";
    options.Jwt.SigningKey = builder.Configuration["KiwiAuth:Jwt:SigningKey"]
        ?? throw new InvalidOperationException(
            "KiwiAuth:Jwt:SigningKey is required. " +
            "Run: dotnet user-secrets set \"KiwiAuth:Jwt:SigningKey\" \"<your-key>\"");
    options.Jwt.AccessTokenMinutes = 15;

    options.RefreshToken.DaysToLive = 7;

    options.Google.ClientId     = builder.Configuration["KiwiAuth:Google:ClientId"] ?? "";
    options.Google.ClientSecret = builder.Configuration["KiwiAuth:Google:ClientSecret"] ?? "";

    options.Frontend.GoogleSuccessRedirectUrl =
        builder.Configuration["KiwiAuth:Frontend:GoogleSuccessRedirectUrl"]
        ?? "http://localhost:5173/auth/callback";

    options.Frontend.GoogleErrorRedirectUrl =
        builder.Configuration["KiwiAuth:Frontend:GoogleErrorRedirectUrl"]
        ?? "http://localhost:5173/auth/error";
});

// ─── Swagger ─────────────────────────────────────────────────────────────────

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "KiwiAuth Sample API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new()
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Paste your access token here.",
    });

    c.AddSecurityRequirement(new()
    {
        {
            new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
            []
        }
    });
});

// ─── App ─────────────────────────────────────────────────────────────────────

var app = builder.Build();

// Initialize database and seed roles on startup.
// For production, replace EnsureCreated with proper migrations:
//   dotnet ef migrations add Initial --project src/KiwiAuth --startup-project samples/KiwiAuth.SampleApi
//   dotnet ef database update --startup-project samples/KiwiAuth.SampleApi
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KiwiDbContext>();
    await db.Database.EnsureCreatedAsync();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "User", "Admin" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    if (app.Environment.IsDevelopment())
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        const string adminEmail = "admin@example.com";

        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "Admin User",
                EmailConfirmed = true,
            };

            await userManager.CreateAsync(admin, "Admin1234!");
            await userManager.AddToRolesAsync(admin, ["User", "Admin"]);
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapKiwiAuthEndpoints();

// ─── Example protected endpoints ─────────────────────────────────────────────

app.MapGet("/user/ping", () => Results.Ok(new { message = "pong", requiredRole = "User" }))
   .RequireAuthorization()
   .WithTags("Protected");

app.MapGet("/admin/ping", () => Results.Ok(new { message = "pong", requiredRole = "Admin" }))
   .RequireAuthorization(policy => policy.RequireRole("Admin"))
   .WithTags("Protected");

app.Run();

public partial class Program { }
