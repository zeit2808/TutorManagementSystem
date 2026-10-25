# Login security: setup and manual verification

## What changed

- `POST /api/v1/auth/login` is limited to 30 requests/minute per observed client IP. Rejected requests return the existing `ApiResponse` error shape with HTTP 429 and `Retry-After`.
- Known accounts have a 15-minute failed-attempt window. Attempts 1-4 are not blocked; attempts 5-9 impose delays of 30, 60, 90, 120 and 150 seconds. Attempt 10 imposes a 15-minute temporary block. The response stays the generic `401 invalid_credentials` so anonymous clients cannot discover account state.
- Successful password authentication clears the counter. Successful password reset and authenticated password change also clear it. An administrative deactivation (`IsActive`) remains a separate concept.
- Security events are stored in `iam.security_audit_events`. Passwords, OTPs and token values are never stored there. The email worker deletes expired audit rows in bounded batches; its default retention is 90 days, configurable through `SecurityAudit:RetentionDays`.
- `RateLimit:TrustedProxies` is empty by default. If deploying behind a reverse proxy, configure only its actual IP addresses. The API then trusts a single forwarded hop; never trust arbitrary `X-Forwarded-For` from clients.

## Visual Studio setup

1. Stop the running `TutorManagement.Api` Debug session before rebuilding Debug. Windows locks loaded DLLs. Alternatively build the solution in Release while Debug remains running.
2. In Solution Explorer, set `TutorManagement.DbMigrator` as the startup project **only for migration commands**. In Package Manager Console choose `TutorManagement.Modules.IdentityAccess.Infrastructure` as **Default project** and run `Update-Database -Context IdentityDbContext -StartupProject TutorManagement.DbMigrator`. The latest migration is `AddLoginSecurityAndAudit`. Running it again when already applied is safe. On the original local database it was applied on 2026-10-18.
3. Switch the startup project back to `TutorManagement.Api` and start the `http` launch profile. Open `http://localhost:5217/swagger/index.html`.
4. Start `TutorManagement.Worker` as a second startup project or a separate Visual Studio instance when testing email delivery and audit retention. The login API itself does not require the worker to be running.
5. In pgAdmin, open the local database, then `Schemas > iam > Tables`. Confirm `login_security_states` and `security_audit_events` exist. The first table is populated lazily when a known account attempts login.

## Manual test with a dedicated verified test account

Do not use the production owner account for repeated wrong-password tests.

1. Log in correctly once. Expect HTTP 200, an access token in JSON and a refresh token only in an HttpOnly cookie.
2. Log out. Send four wrong passwords for the same username within 15 minutes. Each response should be HTTP 401 with `invalid_credentials`.
3. Send a fifth wrong password. It still returns the same generic 401; it starts a 30-second block.
4. Immediately send the correct password. It must still return 401 and issue no new refresh cookie or session.
5. Wait slightly more than 30 seconds and log in correctly. Expect 200. A later wrong password should start a new counter at 1.
6. For IP limiting, make 31 login requests within one minute from the same client. The 31st returns 429 with `rate_limit_exceeded` and `Retry-After`. Wait for the window to expire before normal use.
7. In pgAdmin, inspect the known account's row in `iam.login_security_states` and its events in `iam.security_audit_events`. Confirm the audit records include event type and UTC time but no password, OTP, access token or refresh token.
8. If you perform a successful password reset while temporarily blocked, the next correct login should work immediately and the failed-attempt count should be zero.

Local single-instance API rate limiting is in process and resets on restart. Per-account state is in PostgreSQL and survives restarts. A multi-instance deployment needs a shared edge/distributed IP limiter in addition to this policy.
