# KiwiAuth

> Simple auth for ASP.NET that just works.

KiwiAuth is a lightweight authentication library for ASP.NET Core backends and SPA apps. JWT access tokens, refresh token rotation, TOTP-based MFA, Google OAuth, and ASP.NET Identity — without the ceremony.

Built for solo developers, indie hackers, startups, and internal tools that need solid auth without rolling their own or fighting an enterprise framework.

---

## Features

- Email/password registration and login
- JWT access tokens (HMAC-SHA256, short-lived)
- Refresh token rotation with revocation (stored hashed, never plaintext)
- **TOTP-based MFA** (Google Authenticator, Authy, 1Password, etc.)
- Recovery codes (8 single-use codes generated on MFA enable)
- Logout with token invalidation
- `/auth/me` — current user info and roles
- Google OAuth (backend-initiated, redirects to frontend)
- Role support (`User`, `Admin`, or custom)
- **Email confirmation** on registration (bring-your-own sender via `IEmailSender`)
- **Password reset** (token generation + validation endpoint)
- **Account lockout** after N failed login attempts
- **Custom password policy** via options
- ASP.NET Core Identity for password hashing (PBKDF2)
- EF Core + any provider (SQLite in sample)
- Consistent `{ success, data }` / `{ success, error }` response shape
- Two-line integration: `AddKiwiAuth()` + `MapKiwiAuthEndpoints()`
- Swagger/OpenAPI ready

---

## Compatibility

| .NET version | Supported |
|---|---|
| .NET 7 | Yes |
| .NET 8 (LTS) | Yes |
| .NET 9 | Yes |
| .NET 10 (LTS) | Yes |
| .NET 11 | Planned after release (Nov 2026) |

---

## Why KiwiAuth?

Auth is one of those things you can't afford to get wrong — but you also can't afford to overpay for it or spend weeks building it from scratch.

Managed auth services add recurring costs that grow with your user base. Rolling your own is risky and easy to get wrong. Enterprise frameworks are powerful but bring far more complexity than most apps need.

KiwiAuth is the pragmatic middle ground — a pre-built, production-minded auth layer built on boring, well-understood .NET primitives. No subscriptions, no lock-in, no guesswork.

---

## Quick Start

### 1. Install

```bash
dotnet add package KiwiAuth
```

### 2. Set your signing key

Never put secrets in `appsettings.json`. Use user-secrets for local development:

```bash
cd your-project
dotnet user-secrets init
dotnet user-secrets set "KiwiAuth:Jwt:SigningKey" "$(openssl rand -base64 32)"
```

In production, use an environment variable:

```bash
export KiwiAuth__Jwt__SigningKey="your-secret-key-minimum-32-chars"
```

### 3. Register KiwiAuth

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<KiwiDbContext>(options =>
    options.UseSqlite("Data Source=app.db"));

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("https://your-frontend.com")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials())); // Required for refresh token cookie

builder.Services.AddKiwiAuth(options =>
{
    options.Jwt.Issuer    = "MyApp";
    options.Jwt.Audience  = "MyApp.Client";
    options.Jwt.SigningKey = builder.Configuration["KiwiAuth:Jwt:SigningKey"]!;

    // Optional: Google OAuth
    options.Google.ClientId     = builder.Configuration["KiwiAuth:Google:ClientId"] ?? "";
    options.Google.ClientSecret = builder.Configuration["KiwiAuth:Google:ClientSecret"] ?? "";
    options.Frontend.GoogleSuccessRedirectUrl = "https://your-frontend.com/auth/callback";
    options.Frontend.GoogleErrorRedirectUrl   = "https://your-frontend.com/auth/error";
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapKiwiAuthEndpoints();

app.Run();
```

### 4. Initialize schema

```bash
# Development — auto-created on first run via EnsureCreated
# Production — use migrations:
dotnet ef migrations add Initial
dotnet ef database update
```

---

## Configuration Reference

| Option | Default | Description |
|---|---|---|
| `Jwt.Issuer` | `"KiwiAuth"` | JWT issuer claim |
| `Jwt.Audience` | `"KiwiAuth"` | JWT audience claim |
| `Jwt.SigningKey` | *(required, 32+ chars)* | HMAC-SHA256 signing key |
| `Jwt.AccessTokenMinutes` | `15` | Access token lifetime |
| `RefreshToken.DaysToLive` | `7` | Refresh token lifetime |
| `Mfa.SessionTokenMinutes` | `5` | MFA session token lifetime |
| `Mfa.RecoveryCodeCount` | `8` | Recovery codes generated on MFA enable |
| `Google.ClientId` | `""` | Google OAuth client ID |
| `Google.ClientSecret` | `""` | Google OAuth client secret |
| `Frontend.GoogleSuccessRedirectUrl` | `"/"` | Redirect after Google login success |
| `Frontend.GoogleErrorRedirectUrl` | `"/"` | Redirect after Google login failure |
| `Frontend.EmailConfirmationUrl` | `"/auth/confirm-email"` | Link destination in confirmation email |
| `Frontend.PasswordResetUrl` | `"/auth/reset-password"` | Link destination in password reset email |
| `Password.RequiredLength` | `8` | Minimum password length |
| `Password.RequireDigit` | `true` | Require at least one digit |
| `Password.RequireUppercase` | `false` | Require at least one uppercase letter |
| `Password.RequireNonAlphanumeric` | `false` | Require at least one special character |
| `Lockout.Enabled` | `true` | Enable account lockout |
| `Lockout.MaxFailedAttempts` | `5` | Failed attempts before lockout |
| `Lockout.LockoutMinutes` | `15` | Lockout duration in minutes |
| `Email.RequireConfirmedEmail` | `false` | Block login until email is confirmed |

Google OAuth is disabled when `ClientId` or `ClientSecret` is empty.

---

## Endpoints

### Auth

| Method | Route | Auth | Description |
|---|---|---|---|
| `POST` | `/auth/register` | — | Register with email/password |
| `POST` | `/auth/login` | — | Login (returns MFA session token if MFA enabled) |
| `POST` | `/auth/refresh` | Cookie | Rotate refresh token |
| `POST` | `/auth/logout` | Cookie | Revoke refresh token |
| `GET` | `/auth/me` | Bearer JWT | Current user info |
| `GET` | `/auth/google/login` | — | Initiate Google OAuth |
| `GET` | `/auth/google/callback` | — | Google OAuth callback |
| `GET` | `/auth/confirm-email` | — | Confirm email address (`?userId=...&token=...`) |
| `POST` | `/auth/forgot-password` | — | Request password reset email |
| `POST` | `/auth/reset-password` | — | Reset password using token from email |

### MFA

| Method | Route | Auth | Description |
|---|---|---|---|
| `GET` | `/auth/mfa/setup` | Bearer JWT | Get TOTP secret + QR URI |
| `POST` | `/auth/mfa/enable` | Bearer JWT | Confirm first TOTP code, get recovery codes |
| `POST` | `/auth/mfa/disable` | Bearer JWT | Disable MFA (requires current TOTP) |
| `POST` | `/auth/mfa/verify` | MFA session token | Complete login after password step |
| `POST` | `/auth/mfa/recovery-codes` | Bearer JWT | Regenerate recovery codes |

---

## Response Shape

**Success:**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiJ9...",
    "user": {
      "id": "abc123",
      "email": "user@example.com",
      "fullName": "Jane Doe",
      "roles": ["User"]
    }
  }
}
```

**Error:**
```json
{
  "success": false,
  "error": {
    "code": "invalid_credentials",
    "message": "Invalid email or password."
  }
}
```

---

## Refresh Token Flow

```
POST /auth/login  →  access token in body + refresh token in HttpOnly cookie

[15 min later — access token expires]

POST /auth/refresh  →  new access token in body, new cookie set, old token revoked

POST /auth/logout  →  token revoked in DB, cookie cleared
```

Refresh tokens are stored as SHA-256 hashes. The raw token never touches the database.

---

## MFA Flow

```
GET  /auth/mfa/setup    →  { secret, authenticatorUri }
                           Show QR code to user (use any QR library on the frontend)

POST /auth/mfa/enable   →  { code: "123456" }
                        ←  { recoveryCodes: [...] }  ← show once, user must save these

--- login with MFA enabled ---

POST /auth/login        →  { requiresMfa: true, mfaSessionToken: "..." }

POST /auth/mfa/verify   →  { mfaSessionToken, code: "123456" }
                        ←  { accessToken, user }  +  refresh token cookie
```

---

## Google OAuth Setup

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create an OAuth 2.0 credential (Web Application)
3. Add `https://your-api.com/auth/google/callback-oidc` to **Authorized redirect URIs**
4. Set `ClientId` and `ClientSecret` via user-secrets or environment variables

After successful login, the user is redirected to:
```
{Frontend.GoogleSuccessRedirectUrl}?token=<access_token>
```

The frontend should read the token, store it in memory, and immediately remove it from the URL (`history.replaceState`).

---

## Local Development

```bash
git clone <repo-url>
cd KiwiAuth

# Set the signing key (required)
dotnet user-secrets set "KiwiAuth:Jwt:SigningKey" "local-dev-key-change-in-production-!!" \
  --project samples/KiwiAuth.SampleApi

dotnet run --project samples/KiwiAuth.SampleApi
```

Swagger: `https://localhost:5001/swagger`

Seeded credentials (Development only):
- Email: `admin@example.com`
- Password: `Admin1234!`

---

## Running Tests

```bash
dotnet test
```

28 integration tests, in-memory SQLite, no external services required.

---

## Security Notes

- Passwords: PBKDF2 via ASP.NET Identity
- Refresh tokens: SHA-256 hashed, never stored plaintext
- JWT: HMAC-SHA256, `ClockSkew = TimeSpan.Zero`
- Refresh token cookie: `HttpOnly`, `Secure`, `SameSite=Strict`, scoped to `/auth`
- MFA session token: separate short-lived JWT (`mfa_pending` claim), not a valid access token
- Signing key validated at startup (minimum 32 characters)
- Account lockout after N failed attempts (configurable)
- `POST /auth/forgot-password` always returns 200 — never reveals whether an email is registered

### Production Checklist

- [ ] Signing key via environment variable or secrets manager — never in `appsettings.json`
- [ ] HTTPS only (`app.UseHttpsRedirection()` is included in the sample)
- [ ] CORS configured for your specific frontend origin (not `*`)
- [ ] `AllowCredentials()` on CORS if using refresh token cookie cross-origin
- [ ] Review `SameSite=Strict` — cross-origin SPAs may need `SameSite=None; Secure`
- [ ] Use EF migrations in production (`dotnet ef migrations add Initial`)
- [ ] Remove or gate the dev admin seed before deploying
- [ ] Register a real `IEmailSender` implementation if using email confirmation or password reset

---

## Limitations

- Access tokens cannot be revoked mid-lifetime (they expire naturally)
- Google token passed as query param after OAuth (see note in Google OAuth section)
- No multi-tenancy
- No admin UI

See [ROADMAP.md](ROADMAP.md) for what's planned next.

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

---

## License

[MIT](LICENSE)
