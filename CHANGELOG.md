# Changelog

All notable changes to this project will be documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
This project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.1.2] - 2026-04-07

### Changed
- Multi-target support: library now ships for **net7.0**, **net8.0**, **net9.0**, and **net10.0**
- All ASP.NET Core package references now resolve per target framework (no longer pinned to `8.0.*`)

## [1.1.0] - 2026-04-05

### Added
- `IEmailSender` interface — bring-your-own email sender via DI
- Email confirmation on registration (`GET /auth/confirm-email`)
- Password reset flow (`POST /auth/forgot-password`, `POST /auth/reset-password`)
- Account lockout after N failed login attempts (`LockoutOptions`)
- Custom password policy via `PasswordPolicyOptions`
- `EmailOptions` (`RequireConfirmedEmail`)
- `Frontend.EmailConfirmationUrl` and `Frontend.PasswordResetUrl` options
- 10 new integration tests covering email flows and lockout

### Fixed
- `MfaOptions` was not being propagated through `Configure<KiwiAuthOptions>` (used defaults only)
- Duplicate refresh token cookie logic in `MfaEndpoints` unified into shared `AuthEndpoints.SetRefreshTokenCookie`

## [1.0.0] - 2024-04-03

### Added
- TOTP-based MFA (compatible with Google Authenticator, Authy, 1Password, etc.)
- `GET /auth/mfa/setup` — generate authenticator secret + `otpauth://` URI for QR display
- `POST /auth/mfa/enable` — confirm first TOTP code and activate MFA, returns recovery codes
- `POST /auth/mfa/disable` — disable MFA (requires current TOTP code)
- `POST /auth/mfa/verify` — complete login when MFA is required (TOTP or recovery code)
- `POST /auth/mfa/recovery-codes` — regenerate recovery codes (invalidates previous set)
- MFA session token (short-lived JWT with `mfa_pending` claim) issued after password verification when MFA is enabled
- `MfaOptions` in `KiwiAuthOptions` (`SessionTokenMinutes`, `RecoveryCodeCount`)
- `Otp.NET` in test project for generating real TOTP codes in integration tests
- 9 new MFA integration tests covering full setup → enable → login → verify → disable flow

### Changed
- `LoginAsync` now returns `LoginResult` instead of `AuthResult`
- Login response includes `{ requiresMfa: true, mfaSessionToken }` when MFA is enabled

### Version
- Bumped to 1.0.0

## [0.1.0] - 2024-01-01

### Added
- Email/password registration and login
- JWT access token issuance (HMAC-SHA256)
- Refresh token rotation with revocation (stored hashed)
- Logout with cookie clearing and token invalidation
- `GET /auth/me` for current user info and roles
- Google OAuth backend flow with frontend redirect
- `AddKiwiAuth()` extension method for service registration
- `MapKiwiAuthEndpoints()` extension method for endpoint mapping
- Consistent `{ success, data }` / `{ success, error }` API response shape
- `KiwiDbContext` with `ApplicationUser` and `RefreshToken` entities
- EF Core configuration with index on `TokenHash`
- Roles support via ASP.NET Core Identity
- SQLite sample project with Swagger UI
- Development seed: `User` and `Admin` roles, optional admin user
- Basic xUnit integration tests with in-memory SQLite
- GitHub Actions workflow for build and test
- MIT license
