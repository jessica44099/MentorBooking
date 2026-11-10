# MentorBooking

A mentor–student doubt session booking website.

## Technology

- Frontend: Angular 19, standalone components, TypeScript and SCSS
- Backend: ASP.NET Core Web API with .NET 8
- Database: SQLite for local development
- Authentication: JWT bearer tokens and BCrypt password hashing
- Email: MailKit with configurable fake and SMTP providers

## Project folders

- backend: API, database access and application services
- frontend: Angular application

## Time handling

Store timestamps in UTC.
Display and schedule sessions in Asia/Kolkata by default.

## Development status

Phase 0, Step 1: project folders and Git initialised.

The backend and frontend applications will be created in subsequent steps.

## Secrets

Never commit passwords, JWT signing keys or SMTP credentials.
Use .NET user-secrets locally and environment variables in production.

## Database portability

Changing database engines requires the appropriate EF Core provider,
provider-specific migrations and a connection string.