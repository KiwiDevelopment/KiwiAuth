# Contributing to KiwiAuth

Thanks for your interest in contributing. KiwiAuth aims to stay small, focused, and understandable.

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/yourusername/KiwiAuth`
3. Create a branch: `git checkout -b feature/my-improvement`
4. Make your changes
5. Run tests: `dotnet test`
6. Push and open a pull request against `master`

## Guidelines

**Keep it focused.** One feature or fix per PR. KiwiAuth is intentionally minimal — resist the urge to add abstractions or generalize for hypothetical cases.

**Write tests.** Any new behavior should have at least one integration test.

**Match the style.** Small classes, clear naming, minimal dependencies, no unnecessary patterns. If you're adding an interface, ask yourself if a concrete class would do.

**Don't add dependencies** without opening a discussion first. One of KiwiAuth's goals is to stay lean.

**Update docs** if you change behavior, add options, or rename endpoints.

## What's in scope

- Bug fixes
- Security improvements
- OAuth provider additions (with discussion)
- Options that don't add complexity
- Test coverage improvements

## What's out of scope (for this library)

- Admin dashboards or UIs
- Multi-tenancy
- Event buses or messaging
- CQRS / MediatR / DDD patterns
- Email sending (implement `IEmailSender` and register it via DI — the integration point is already in place)

## Reporting Issues

Use GitHub Issues. Please include:
- .NET version (`dotnet --version`)
- KiwiAuth version
- Minimal reproduction steps
- Expected vs. actual behavior
- Stack trace if applicable

## Questions

Open a GitHub Discussion or tag your issue with `question`.
