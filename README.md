# MentorBooking

A full-stack mentor/student doubt-clearing platform with Angular 19, ASP.NET Core 8, EF Core, SQLite and JWT authentication.

## Project structure

- `backend/`: ASP.NET Core Web API, services and EF Core migrations
- `frontend/`: Angular 19 client
- `tests/MentorBooking.Tests/`: .NET xUnit regression tests

## Local start

From PowerShell, open the **backend** folder and run:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5055'
dotnet run --no-launch-profile
```

In a second PowerShell terminal:

```powershell
cd ..\frontend
npm ci
npm start
```

Open http://localhost:4200. The Angular dev server proxies `/api` to http://localhost:5055.

The backend uses the SQLite database configured by `ConnectionStrings:DefaultConnection`. Before first use, apply the existing migrations using `dotnet ef database update --project .\backend\MentorBooking.Api.csproj --startup-project .\backend\MentorBooking.Api.csproj` from the repository root (with the EF CLI tool installed).

The API requires a strong `Jwt:Key` (minimum 32 UTF-8 bytes). Keep it in User Secrets or deployment secrets rather than version control.

## Brevo SMTP: one-time configuration

**Do not paste an SMTP key or JWT key into GitHub, a chat, source files, PowerShell screenshots, or application logs.**

1. In [Brevo](https://app.brevo.com), activate transactional sending, configure a verified From address, and preferably authenticate a sender domain (DKIM/DMARC).
2. Go to **SMTP & API → SMTP**. Copy the SMTP **login** and generate an **SMTP key**. An API key will not work for SMTP authentication.
3. In PowerShell, change to the `backend` directory and save these settings once in .NET User Secrets:

```powershell
dotnet user-secrets set 'EmailDelivery:Enabled' 'true' --project .\MentorBooking.Api.csproj
dotnet user-secrets set 'EmailDelivery:SmtpHost' 'smtp-relay.brevo.com' --project .\MentorBooking.Api.csproj
dotnet user-secrets set 'EmailDelivery:SmtpPort' '587' --project .\MentorBooking.Api.csproj
dotnet user-secrets set 'EmailDelivery:RequireStartTls' 'true' --project .\MentorBooking.Api.csproj
dotnet user-secrets set 'EmailDelivery:FromName' 'MentorBooking' --project .\MentorBooking.Api.csproj
$sender = Read-Host 'Verified Brevo From email'
dotnet user-secrets set 'EmailDelivery:FromAddress' $sender --project .\MentorBooking.Api.csproj
$smtpLogin = Read-Host 'Brevo SMTP login (not From email)'
dotnet user-secrets set 'EmailDelivery:SmtpUsername' $smtpLogin --project .\MentorBooking.Api.csproj
$smtpKey = Read-Host 'Brevo SMTP key (input hidden)' -MaskInput
dotnet user-secrets set 'EmailDelivery:SmtpPassword' $smtpKey --project .\MentorBooking.Api.csproj
Remove-Variable sender,smtpLogin,smtpKey
```

User Secrets are used automatically when `ASPNETCORE_ENVIRONMENT=Development`. For hosted deployments, provide the same settings through secret environment variables such as `EmailDelivery__SmtpPassword`. Set `Frontend__BaseUrl` to the publicly accessible **HTTPS** Angular site on deployment. Never commit actual credentials.

After setup, **registration**, **resend verification**, **forgot password**, and queued session notifications use the SMTP provider automatically, even in local Development mode. If SMTP is disabled or incomplete, the system reports failure rather than presenting a fake sent email. A failed registration email does not remove the saved account; log in and use **Resend verification**.

The SMTP provider accepting a message is not proof of delivery to an inbox. Test both a student and a mentor using real recipient addresses. Inspect Brevo's transactional logs and the spam folder if delivery is missing.

Brevo documentation: [SMTP relay integration](https://developers.brevo.com/docs/smtp-integration), [sender verification](https://help.brevo.com/hc/en-us/articles/208836149-Create-a-new-sender-From-name-and-From-email).

## Run automated checks

From the repository root:

```powershell
dotnet test .\tests\MentorBooking.Tests\MentorBooking.Tests.csproj -c Release
cd .\frontend
npm ci
npm run build
```

Live email acceptance and inbox delivery are separate manual integration checks and require valid Brevo credentials. No schema changes are required for the SMTP transport switch.
