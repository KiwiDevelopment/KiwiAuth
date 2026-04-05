# Roadmap

KiwiAuth follows a deliberate release pace — ship what's useful, don't ship what isn't needed yet.

## v1.0 — MVP

- [x] Email/password registration and login
- [x] JWT access tokens (HMAC-SHA256)
- [x] Refresh token rotation and revocation (stored hashed)
- [x] Google OAuth backend flow
- [x] `GET /auth/me`
- [x] Role support
- [x] `AddKiwiAuth()` + `MapKiwiAuthEndpoints()` API
- [x] Sample API with SQLite + Swagger
- [x] Integration test foundation

## v1.1 — Email flows (current)

- [x] Email confirmation on registration (bring-your-own sender via `IEmailSender`)
- [x] Password reset (token generation + validation endpoint)
- [x] Account lockout after N failed login attempts
- [x] Custom password policy via options

## v1.2 — More providers + token improvements

- [ ] GitHub OAuth
- [ ] Microsoft / Azure AD OAuth
- [ ] Refresh token reuse detection (token family tracking)
- [ ] Optional: refresh token in Authorization header instead of cookie

## v1.3 — Production hardening

- [ ] Migration helpers and documentation
- [ ] Structured logging for auth events (login, logout, refresh, failures)
- [ ] Rate limiting integration point
- [ ] PKCE support for public clients

## Future / Pro ideas

These are candidates for a paid tier or a separate companion product:

| Feature | Notes |
|---|---|
| Admin dashboard (Blazor or React) | View users, sessions, roles |
| Multi-tenant support | Per-tenant user isolation |
| Audit log UI | Who logged in, from where, when |
| Organization / team management | Invite flows, member roles |
| Magic link authentication | No password required |
| WebAuthn / Passkeys | FIDO2 |
| Apple, LinkedIn, Twitter OAuth | Additional providers |
| Session management UI | Revoke active sessions |
| Hosted / SaaS version | Config UI, no code changes needed |

---

Have an idea? Open a [GitHub Discussion](https://github.com/yourusername/KiwiAuth/discussions) or a feature request issue.
