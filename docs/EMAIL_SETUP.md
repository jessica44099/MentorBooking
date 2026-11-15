# MentorBooking email delivery and Brevo setup

This setup is **one-time per development machine**. After it is configured,
normal VS Code runs send real verification and password-reset links.
No SMTP key belongs in Git, `appsettings.json`, a frontend file, or a URL.

## What was wrong

The application previously selected the development fake-email sender whenever
`ASPNETCORE_ENVIRONMENT=Development`, even when real SMTP credentials were
present. Separately, `EmailDelivery:Enabled` was false by default.

The fix selects SMTP whenever `EmailDelivery:Enabled=true`, including local
Development. When it is false, Development still previews email locally.
Production/staging will refuse to start with real email disabled.

## One-time local configuration

Use the .NET **User Secrets** attached to
`backend/MentorBooking.Api.csproj` (its `UserSecretsId` is already defined).
An IDE assistant with access to VS Code can configure these on your machine
after you supply the credentials privately; it must not print or commit them.

| Key | Value |
| --- | --- |
| `EmailDelivery:Enabled` | `true` |
| `EmailDelivery:SmtpHost` | `smtp-relay.brevo.com` |
| `EmailDelivery:SmtpPort` | `587` |
| `EmailDelivery:RequireStartTls` | `true` |
| `EmailDelivery:SmtpUsername` | **SMTP login** shown under Brevo SMTP settings (not always your Brevo account email) |
| `EmailDelivery:SmtpPassword` | **SMTP key**, not Brevo API key or account password |
| `EmailDelivery:FromAddress` | Verified transactional sender address |
| `EmailDelivery:FromName` | `MentorBooking` |
| `Frontend:BaseUrl` | `http://localhost:4200` locally; public HTTPS URL when deployed |

If the backend already has `Jwt:Key` in User Secrets, **preserve it**.
Never reset, delete, print, or rotate existing secrets without approval.

The SMTP configuration is checked at startup when delivery is enabled.
It reports which setting name is missing, never the secret value.

## Brevo requirements outside the code

1. Enable transactional SMTP on the Brevo account.
2. Verify the sender and authenticate its domain (SPF/DKIM/DMARC as appropriate).
3. Confirm SMTP **login** and SMTP **key** are from **SMTP & API > SMTP**.
4. Check Brevo **Transactional > Logs** after attempting a registration or
   reset. SMTP acceptance is not proof of inbox delivery. Check for
   blocked contacts, bounces, rejections and provider restrictions.
5. If port 587 is blocked, use 2525 or 465 after checking firewall/provider.

## Verify end to end

1. Start the API using the HTTP launch profile. It now listens on
   `http://localhost:5055`, which matches Angular's proxy.
2. Start Angular from `frontend` using `npm start`.
3. Register with a **new address you control**.
4. Confirm that the real inbox receives a link to
   `http://localhost:4200/verify-email?token=...`.
   Open it and click **Verify email**.
5. Request **Forgot password**. Confirm receipt of
   `http://localhost:4200/reset-password?token=...`.
   Reset the password and log in using the new one.
6. Try **Resend verification** on both mentor and student dashboards.
7. Check Brevo delivery logs independently. No raw token should be copied
   from the terminal in this real-SMTP flow.

**Security:** user enumeration is deliberately prevented on Forgot password,
so a generic response does not prove that email was sent. If SMTP rejects the
mail, check the server's protected logs. Registration and resend endpoints
report delivery failures to the UI.

## Automated verification

A GitHub Actions workflow runs the .NET test suite on isolated in-memory
SQLite databases and builds the Angular app. Do not use production credentials
in CI. Real inbox delivery requires an authorized manual smoke test or an
approved sandbox SMTP account.

## Remaining hardening

- Add request-rate limiting to registration, resend and password-reset actions.
- Add sanitized delivery observability and a safe retry workflow for account email.
- Run a complete authenticated session-booking UI smoke test.
- Update `Frontend:BaseUrl` to an HTTPS deployment URL before public release.
