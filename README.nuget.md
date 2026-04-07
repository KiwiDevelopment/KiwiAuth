# KiwiAuth

Lightweight authentication library for ASP.NET Core — JWT access tokens, refresh token rotation, TOTP MFA, Google OAuth, and ASP.NET Identity without the ceremony.

Built for solo developers, indie hackers, and startups that need solid auth without rolling their own or paying per user.

---

## Install

```bash
dotnet add package KiwiAuth
```

## Quick Start

```csharp
// Program.cs
builder.Services.AddDbContext<KiwiDbContext>(o =>
    o.UseSqlite("Data Source=app.db"));

builder.Services.AddKiwiAuth(options =>
{
    options.Jwt.Issuer    = "MyApp";
    options.Jwt.Audience  = "MyApp.Client";
    options.Jwt.SigningKey = builder.Configuration["KiwiAuth:Jwt:SigningKey"]!;
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapKiwiAuthEndpoints();
```

That's it. You get:

- `POST /auth/register` — email + password registration
- `POST /auth/login` — login with JWT + refresh token cookie
- `POST /auth/refresh` — rotate refresh token
- `POST /auth/logout` — revoke token
- `GET  /auth/me` — current user info (Bearer)
- `GET  /auth/google/login` — Google OAuth
- `GET  /auth/confirm-email` — email confirmation
- `POST /auth/forgot-password` / `POST /auth/reset-password`
- `GET/POST /auth/mfa/*` — TOTP setup, enable, verify, recovery codes

## Set your signing key

```bash
dotnet user-secrets set "KiwiAuth:Jwt:SigningKey" "your-secret-key-minimum-32-chars"
```

## Compatibility

Targets **net7.0**, **net8.0**, **net9.0**, and **net10.0**.

---

Full documentation, configuration reference, and endpoint details:
**[github.com/KiwiAuth/KiwiAuth](https://github.com/KiwiAuth/KiwiAuth)**
