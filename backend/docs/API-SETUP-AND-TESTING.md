# Hướng dẫn setup và kiểm thử toàn bộ API hiện tại

Tài liệu này áp dụng cho backend tại:

```text
D:\Project\QuanLyDayThem\backend
```

URL local mặc định:

```text
API:      http://localhost:5217
Swagger:  http://localhost:5217/swagger/index.html
OpenAPI:  http://localhost:5217/openapi/v1.json
```

## 1. Thành phần cần chạy

Backend local gồm ba host:

| Host | Nhiệm vụ | Có cần chạy thường xuyên? |
|---|---|---|
| `TutorManagement.DbMigrator` | Áp dụng EF Core migration và seed Owner phát triển | Chạy khi setup hoặc có migration mới |
| `TutorManagement.Api` | Phục vụ HTTP API và Swagger | Có |
| `TutorManagement.Worker` | Xử lý email outbox qua File hoặc SMTP | Có khi test email |

Tất cả module hiện dùng chung một PostgreSQL database nhưng được phân chia bằng
schema `iam`, `organization` và `academics`.

## 2. Yêu cầu môi trường

- Windows PowerShell 7 hoặc PowerShell 5.1.
- .NET SDK `10.0.401` theo `global.json`.
- PostgreSQL local đang lắng nghe ở `localhost:5432`.
- PostgreSQL command line tools, đặc biệt `psql.exe` và `pg_isready.exe`.

Kiểm tra nhanh:

```powershell
Set-Location -LiteralPath 'D:\Project\QuanLyDayThem\backend'

dotnet --version
dotnet tool restore

& 'C:\Program Files\PostgreSQL\18\bin\pg_isready.exe' `
  -h localhost `
  -p 5432
```

Kết quả PostgreSQL mong đợi:

```text
localhost:5432 - accepting connections
```

## 3. Setup PostgreSQL lần đầu

Chạy tại thư mục `backend`:

```powershell
.\tools\setup-local-postgres.ps1
```

Script sẽ yêu cầu:

1. Mật khẩu tài khoản quản trị PostgreSQL `postgres`.
2. Mật khẩu mới cho application role `tutor_app`.
3. Nhập lại mật khẩu của `tutor_app`.

Sau đó script sẽ:

- tạo hoặc cập nhật role `tutor_app`;
- tạo database `tutor_management`;
- lưu connection string vào .NET User Secrets của API, Worker và DbMigrator;
- áp dụng migration nếu không truyền `-SkipMigration`.

Nếu PostgreSQL nằm ở thư mục khác:

```powershell
.\tools\setup-local-postgres.ps1 `
  -PostgresHome 'D:\PostgreSQL\18'
```

Không ghi mật khẩu thật vào `appsettings.json`, `.env` hoặc source code.

## 4. Setup JWT và tài khoản admin phát triển

```powershell
.\tools\setup-identity-secrets.ps1 -OwnerUsername 'admin'
```

Nhập mật khẩu Owner tối thiểu 12 ký tự. Nên dùng mật khẩu có chữ hoa, chữ thường,
chữ số và ký tự đặc biệt, ví dụ theo cấu trúc:

```text
MyLocalAdmin!2026
```

Không sử dụng mật khẩu ví dụ này ở môi trường thật.

Script sẽ:

- tạo JWT signing key ngẫu nhiên 512-bit;
- lưu signing key vào User Secrets của API;
- lưu username/password seed vào User Secrets của DbMigrator;
- chạy DbMigrator và tạo Owner nếu chưa tồn tại.

Mật khẩu seed không ghi đè tài khoản đã tồn tại. Nếu muốn đổi mật khẩu admin cũ,
không chạy lại script để kỳ vọng nó cập nhật; chức năng Change Password sẽ được
triển khai ở bước authentication sau.

## 5. Restore, build, test và migration

```powershell
dotnet restore .\TutorManagementSystem.sln

dotnet build `
  .\TutorManagementSystem.sln `
  --configuration Release `
  --no-restore `
  -m:1

$env:VSTEST_CONNECTION_TIMEOUT = '180'
dotnet test `
  .\TutorManagementSystem.sln `
  --configuration Release `
  --no-build `
  --no-restore `
  -m:1
```

`-m:1` giúp build tuần tự, tránh hai project cùng copy một DLL và gây lỗi file
đang bị khóa trên Windows.

Áp dụng migration thủ công khi có migration mới:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project .\src\Hosts\TutorManagement.DbMigrator
```

Kết quả cuối mong đợi:

```text
All database migrations were applied successfully.
```

### 5.1 Dùng Visual Studio Package Manager Console thay cho DbMigrator

Trong Visual Studio, hai lựa chọn có vai trò khác nhau:

- **Default project**: project chứa `DbContext`, migration và model snapshot.
- **Startup project**: project chạy được, cung cấp dependency injection,
  configuration và connection string.

Đối với migration authentication hiện tại:

1. Trong **Solution Explorer**, nhấp phải `TutorManagement.DbMigrator`, chọn
   **Set as Startup Project**.
2. Mở **Tools → NuGet Package Manager → Package Manager Console**.
3. Ở dropdown **Default project**, chọn:

```text
TutorManagement.Modules.IdentityAccess.Infrastructure
```

4. Chạy:

```powershell
Update-Database `
  -Context IdentityDbContext `
  -StartupProject TutorManagement.DbMigrator
```

Có thể ghi đầy đủ `-Project` để lệnh không phụ thuộc dropdown đang chọn:

```powershell
Update-Database `
  -Project TutorManagement.Modules.IdentityAccess.Infrastructure `
  -StartupProject TutorManagement.DbMigrator `
  -Context IdentityDbContext
```

Để áp dụng cả ba `DbContext`, Package Manager Console phải chạy ba lần vì
`Update-Database` chỉ xử lý một context trong mỗi lệnh:

```powershell
Update-Database `
  -Project TutorManagement.Modules.IdentityAccess.Infrastructure `
  -StartupProject TutorManagement.DbMigrator `
  -Context IdentityDbContext

Update-Database `
  -Project TutorManagement.Modules.Organizations.Infrastructure `
  -StartupProject TutorManagement.DbMigrator `
  -Context OrganizationDbContext

Update-Database `
  -Project TutorManagement.Modules.Academics.Infrastructure `
  -StartupProject TutorManagement.DbMigrator `
  -Context AcademicsDbContext
```

Không chọn `TutorManagement.Api`, `Application`, `Domain` hoặc `Contracts` làm
Default project vì các project đó không chứa migration của module.

`Update-Database` chỉ áp dụng EF migration; nó không chạy đoạn custom seed Owner
trong `TutorManagement.DbMigrator`. Nếu database chưa có admin, vẫn cần chạy
`setup-identity-secrets.ps1`/DbMigrator một lần hoặc đăng ký tài khoản qua API.

Nếu Visual Studio chưa nhận lệnh sau khi thêm package Tools, chạy **Restore NuGet
Packages**, đóng/mở lại Package Manager Console rồi thử lại.

## 6. Chạy API và Worker

Mở terminal thứ nhất:

```powershell
Set-Location -LiteralPath 'D:\Project\QuanLyDayThem\backend'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project .\src\Hosts\TutorManagement.Api
```

API sẽ chạy theo `launchSettings.json` tại:

```text
http://localhost:5217
```

Mở terminal thứ hai:

```powershell
Set-Location -LiteralPath 'D:\Project\QuanLyDayThem\backend'
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project .\src\Hosts\TutorManagement.Worker
```

Worker local ghi email vào:

```text
D:\Project\QuanLyDayThem\backend\src\Hosts\TutorManagement.Worker\.local\emails
```

Không cần chạy Worker khi chỉ test health check hoặc đăng nhập admin đã được seed.
Worker bắt buộc khi test resend/verify email. Để gửi email thật qua SMTP, xem
`docs/SMTP-EMAIL-SETUP-AND-END-TO-END-TEST.md`.

## 7. Danh sách endpoint hiện tại

> **Lưu ý kiến trúc:** public registration đã bị loại bỏ. Tài khoản trung tâm sẽ
> chỉ được tạo qua invitation sau khi Platform Admin duyệt trung tâm.

| Method | Route | Authentication | Kết quả chính |
|---|---|---|---|
| `GET` | `/health/live` | Không | API process đang hoạt động |
| `GET` | `/health/ready` | Không | API và PostgreSQL sẵn sàng |
| `POST` | `/api/v1/auth/verify-email` | Không | Xác minh email bằng token một lần |
| `POST` | `/api/v1/auth/resend-verification-email` | Không | Gửi lại email, luôn trả `204` |
| `POST` | `/api/v1/auth/login` | Không | Trả access token và đặt refresh cookie |
| `POST` | `/api/v1/auth/refresh-token` | HttpOnly cookie | Rotate refresh token và trả access token mới |
| `POST` | `/api/v1/auth/logout` | HttpOnly cookie, có thể không có | Revoke session hiện tại |
| `POST` | `/api/v1/auth/logout-all` | Bearer access token | Revoke tất cả session của user |
| `GET` | `/api/v1/platform/centers` | Bearer token, role `PlatformAdmin` | Danh sách trung tâm và kiểm tra quyền quản trị |

Ngoài ra:

| Route | Nội dung |
|---|---|
| `/swagger/index.html` | Swagger UI |
| `/swagger/v1/swagger.json` | Swagger document |
| `/openapi/v1.json` | OpenAPI document của ASP.NET Core |

## 8. Quy ước response

API nghiệp vụ trả envelope thống nhất:

```json
{
  "success": true,
  "message": "Operation completed.",
  "data": {},
  "traceId": "0H..."
}
```

Response lỗi:

```json
{
  "success": false,
  "message": "Request validation failed",
  "errors": [
    {
      "code": "validation_error",
      "message": "Email is invalid.",
      "field": "Email"
    }
  ],
  "traceId": "0H..."
}
```

Giữ lại `traceId` khi báo lỗi để đối chiếu log backend.

Các trường có giá trị `null` được bỏ khỏi JSON; `false`, `0` và `[]` vẫn được giữ. Danh sách học sinh của lớp trả `data.items` có phân trang, mã/tên học sinh và mặc định chỉ người đang học. Login/refresh/change-password trả `tokenType`, `accessToken`, `expiresIn`. Xem [API-RESPONSE-CONTRACT.md](API-RESPONSE-CONTRACT.md) để biết các trường đã lược bỏ và hướng dẫn test mới.

Các endpoint trả `204 No Content` sẽ không có JSON body.

## 9. Test nhanh health check

```powershell
$baseUrl = 'http://localhost:5217'

$routes = @(
  '/health/live',
  '/health/ready'
)

foreach ($route in $routes) {
  $response = Invoke-WebRequest -Uri "$baseUrl$route" -Method Get
  [PSCustomObject]@{
    Route = $route
    Status = $response.StatusCode
    ContentType = $response.Headers.'Content-Type'
    Body = $response.Content
  }
}
```

Kết quả mong đợi: tất cả trả `200`.

`/health/ready` lỗi trong khi `/health/live` thành công thường có nghĩa API chạy
được nhưng connection string hoặc PostgreSQL đang có vấn đề.

## 10. Test bằng Swagger

Mở:

```text
http://localhost:5217/swagger/index.html
```

### 10.1 Login bằng admin seed

Mở `POST /api/v1/auth/login`, chọn **Try it out** và nhập:

```json
{
  "username": "admin",
  "password": "MAT_KHAU_BAN_DA_NHAP_KHI_SETUP"
}
```

Kết quả mong đợi:

- status `200`;
- JSON có `data.accessToken`;
- JSON không có refresh token;
- response header có `Set-Cookie: tm.refresh=...; HttpOnly`.

Sao chép giá trị `accessToken`, bấm nút **Authorize** trên Swagger và dán token.
Chỉ dán JWT, không thêm `Bearer` nếu Swagger đã tự thêm prefix.

Sau đó gọi `GET /api/v1/platform/centers`. Kết quả mong đợi là `200`.

### 10.2 Refresh token trên Swagger

Sau khi login, trình duyệt giữ cookie `tm.refresh`. Gọi:

```text
POST /api/v1/auth/refresh-token
```

Endpoint này không có request body. Kết quả mong đợi:

- status `200`;
- access token mới trong JSON;
- refresh cookie mới trong `Set-Cookie`;
- refresh token cũ bị revoke.

### 10.3 Logout

Gọi `POST /api/v1/auth/logout`. Kết quả mong đợi `204`; cookie bị hết hạn.

Đăng nhập lại, Authorize lại access token, sau đó gọi
`POST /api/v1/auth/logout-all`. Kết quả mong đợi `204` và mọi refresh token của
tài khoản bị revoke.

## 11. Test đầy đủ Login → Refresh → Logout

Public Register không còn tồn tại. Trước khi test, seed account local bằng
`TutorManagement.DbMigrator`, sau đó lấy username/password từ User Secrets của
DbMigrator trong Visual Studio.

Trước hết kiểm tra trong Swagger rằng `/api/v1/auth/register` không xuất hiện.
Request trực tiếp tới route cũ phải trả `404`.

### 11.1 Login và giữ HttpOnly cookie

```powershell
$baseUrl = 'http://localhost:5217'
$loginBody = @{
  username = 'USERNAME_TRONG_USER_SECRETS'
  password = 'PASSWORD_TRONG_USER_SECRETS'
} | ConvertTo-Json

$session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()

$loginResponse = Invoke-WebRequest `
  -Uri "$baseUrl/api/v1/auth/login" `
  -Method Post `
  -ContentType 'application/json' `
  -Body $loginBody `
  -WebSession $session

$login = $loginResponse.Content | ConvertFrom-Json
$accessToken = $login.data.accessToken

$loginResponse.StatusCode
$login.data
$loginResponse.Headers.'Set-Cookie'
```

Kết quả mong đợi:

- `200 OK`;
- access token trong JSON;
- không có refresh token trong JSON;
- refresh token nằm trong cookie `tm.refresh` với `HttpOnly` và `SameSite=Lax`.

Development đang dùng HTTP nên `Secure=false`. Production bắt buộc HTTPS và
`Secure=true`.

### 11.2 Test authorization Owner

```powershell
$ownerResponse = Invoke-WebRequest `
  -Uri "$baseUrl/api/v1/platform/centers" `
  -Method Get `
  -Headers @{ Authorization = "Bearer $accessToken" }

$ownerResponse.StatusCode
$ownerResponse.Content
```

Kết quả mong đợi: `200`.

### 11.3 Refresh bằng cookie, không gửi token trong body

```powershell
$refreshResponse = Invoke-WebRequest `
  -Uri "$baseUrl/api/v1/auth/refresh-token" `
  -Method Post `
  -WebSession $session

$refresh = $refreshResponse.Content | ConvertFrom-Json
$accessToken = $refresh.data.accessToken

$refreshResponse.StatusCode
$refresh.data
$refreshResponse.Headers.'Set-Cookie'
```

Kết quả mong đợi `200`. Cookie mới tự được cập nhật trong `$session`.

### 11.4 Logout All

```powershell
$logoutAllResponse = Invoke-WebRequest `
  -Uri "$baseUrl/api/v1/auth/logout-all" `
  -Method Post `
  -Headers @{ Authorization = "Bearer $accessToken" } `
  -WebSession $session

$logoutAllResponse.StatusCode
```

Kết quả mong đợi `204`. Gọi refresh bằng session cũ sau đó phải trả `401`.

## 12. Test resend verification email

Endpoint luôn trả `204`, bất kể email tồn tại hay không, để tránh dò tài khoản.

```powershell
Invoke-WebRequest `
  -Uri "$baseUrl/api/v1/auth/resend-verification-email" `
  -Method Post `
  -ContentType 'application/json' `
  -Body (@{ email = 'unknown@example.com' } | ConvertTo-Json)
```

Quy tắc hiện tại:

- tài khoản đã verify: trả `204`, không tạo email;
- email không tồn tại: trả `204`, không tạo email;
- tài khoản chưa verify: tạo token/email mới;
- gửi lại trong vòng 60 giây: trả `204` nhưng không tạo thêm email.

Rate limiting tổng quát theo IP/user sẽ được bổ sung ở bước security số 9.

## 13. Test Logout session hiện tại

Sau khi login và có `$session`:

```powershell
$logoutResponse = Invoke-WebRequest `
  -Uri "$baseUrl/api/v1/auth/logout" `
  -Method Post `
  -WebSession $session

$logoutResponse.StatusCode
$logoutResponse.Headers.'Set-Cookie'
```

Kết quả mong đợi:

- status `204`;
- cookie `tm.refresh` được đặt ngày hết hạn trong quá khứ;
- gọi lại logout vẫn trả `204` vì endpoint idempotent;
- refresh sau logout trả `401`.

## 14. Test CORS credentials

Frontend được phép hiện tại:

```text
http://localhost:5173
```

Test preflight:

```powershell
$corsResponse = Invoke-WebRequest `
  -Uri "$baseUrl/api/v1/auth/refresh-token" `
  -Method Options `
  -Headers @{
    Origin = 'http://localhost:5173'
    'Access-Control-Request-Method' = 'POST'
    'Access-Control-Request-Headers' = 'content-type'
  }

$corsResponse.StatusCode
$corsResponse.Headers
```

Kết quả mong đợi:

```text
Status: 204
Access-Control-Allow-Origin: http://localhost:5173
Access-Control-Allow-Credentials: true
```

React Fetch phải dùng:

```javascript
fetch(url, {
  method: "POST",
  credentials: "include"
});
```

Axios phải dùng:

```javascript
axios.post(url, body, {
  withCredentials: true
});
```

Không cấu hình `AllowAnyOrigin()` cùng `AllowCredentials()`.

## 15. Test content negotiation và lỗi 406

JSON:

```powershell
Invoke-WebRequest `
  -Uri "$baseUrl/api/v1/platform/centers" `
  -Headers @{ Accept = 'application/json'; Authorization = "Bearer $accessToken" }
```

XML:

```powershell
Invoke-WebRequest `
  -Uri "$baseUrl/api/v1/platform/centers" `
  -Headers @{ Accept = 'application/xml'; Authorization = "Bearer $accessToken" }
```

Media type không hỗ trợ:

```powershell
try {
  Invoke-WebRequest `
    -Uri "$baseUrl/api/v1/platform/centers" `
    -Headers @{ Accept = 'text/csv'; Authorization = "Bearer $accessToken" } `
    -ErrorAction Stop
}
catch {
  $_.Exception.Response.StatusCode
}
```

Kết quả cuối phải là `406 Not Acceptable`. Nếu client không cần XML, luôn đặt:

```http
Accept: application/json
```

## 16. Kiểm tra database bằng pgAdmin

Kết nối:

```text
Host: localhost
Port: 5432
Database: tutor_management
Username: tutor_app
Password: mật khẩu đã nhập trong setup-local-postgres.ps1
```

Các bảng authentication hiện tại nằm trong schema `iam`:

```text
iam.users
iam.roles
iam.user_roles
iam.refresh_tokens
iam.user_security_tokens
iam.email_outbox_messages
iam.__ef_migrations_history
```

Các truy vấn kiểm tra an toàn:

```sql
SELECT id,
       username,
       email,
       phone_number,
       is_active,
       email_verified_at_utc,
       created_at_utc
FROM iam.users
ORDER BY created_at_utc DESC;

SELECT user_id,
       family_id,
       expires_at_utc,
       revoked_at_utc,
       revocation_reason
FROM iam.refresh_tokens
ORDER BY created_at_utc DESC;

SELECT user_id,
       purpose,
       expires_at_utc,
       used_at_utc
FROM iam.user_security_tokens
ORDER BY created_at_utc DESC;

SELECT recipient,
       created_at_utc,
       processed_at_utc,
       attempt_count,
       last_error
FROM iam.email_outbox_messages
ORDER BY created_at_utc DESC;

SELECT "MigrationId", "ProductVersion"
FROM iam.__ef_migrations_history
ORDER BY "MigrationId";
```

Không ghi hoặc hiển thị `password_hash`, `token_hash`, JWT signing key hay
connection string trong log/screenshot chia sẻ ra ngoài.

## 17. Status code cần kiểm tra

| Tình huống | Status/code mong đợi |
|---|---|
| Gọi route Register cũ | `404` |
| Verify token hợp lệ | `204` |
| Verify token sai/hết hạn/đã dùng | `400 / invalid_email_verification_token` |
| Login đúng nhưng chưa verify | `401 / email_not_verified` |
| Login sai | `401 / invalid_credentials` |
| Login thành công | `200` và `Set-Cookie` |
| Refresh hợp lệ | `200` và cookie được rotate |
| Refresh thiếu/sai/reuse token | `401 / invalid_refresh_token` |
| Logout | `204` |
| Logout All thiếu bearer token | `401` |
| Owner endpoint với role khác | `403` |
| Accept header không hỗ trợ | `406` |

## 18. Xử lý lỗi thường gặp

### Swagger không phản hồi

Kiểm tra API có thực sự đang lắng nghe:

```powershell
Get-NetTCPConnection -LocalPort 5217 -State Listen -ErrorAction SilentlyContinue
Invoke-WebRequest http://localhost:5217/health/live
```

Nếu không có listener, chạy lại API.

### DLL bị khóa khi build

Đóng API/Worker đang chạy bằng `Ctrl+C`. Nếu process cũ vẫn còn:

```powershell
Get-Process TutorManagement.Api,TutorManagement.Worker -ErrorAction SilentlyContinue
```

Chỉ dừng đúng process đã xác định:

```powershell
Stop-Process -Name TutorManagement.Api -ErrorAction SilentlyContinue
Stop-Process -Name TutorManagement.Worker -ErrorAction SilentlyContinue
```

Sau đó build tuần tự với `-m:1`.

### Database connection failed

```powershell
& 'C:\Program Files\PostgreSQL\18\bin\pg_isready.exe' `
  -h localhost `
  -p 5432

dotnet user-secrets list `
  --project .\src\Hosts\TutorManagement.Api\TutorManagement.Api.csproj
```

Không chụp hoặc chia sẻ output User Secrets vì có thể chứa mật khẩu database và
JWT signing key.

### Worker không tạo email local

1. Đảm bảo Worker đang chạy.
2. Kiểm tra `iam.email_outbox_messages`.
3. Kiểm tra thư mục `.local\emails` của Worker.
4. Xem `attempt_count` và `last_error`.
5. Public Register không còn tạo outbox; email mới phải đến từ invitation hoặc
   một luồng email bảo mật hợp lệ.

### Refresh luôn trả 401

- Đăng nhập lại để lấy cookie mới.
- Không gửi refresh token trong JSON body.
- PowerShell phải tái sử dụng cùng `WebRequestSession`.
- React/Axios phải bật credentials.
- Cookie path hiện là `/api/v1/auth` nên cookie không được gửi tới route ngoài
  auth; đây là hành vi chủ ý.

## 19. Checklist hoàn tất

- [ ] PostgreSQL trả `accepting connections`.
- [ ] `setup-local-postgres.ps1` thành công.
- [ ] `setup-identity-secrets.ps1` thành công.
- [ ] DbMigrator báo migration thành công.
- [ ] Solution build không warning/error.
- [ ] 31/31 automated tests pass.
- [ ] `/health/live` trả `200`.
- [ ] `/health/ready` trả `200`.
- [ ] Ba module status API trả `200`.
- [ ] Admin login trả `200`.
- [ ] Owner authorization trả `200`.
- [ ] Route Register không có trong Swagger và trả `404` khi gọi trực tiếp.
- [ ] `FileEmailDeliveryClientTests` pass.
- [ ] Login account seed trả `200`.
- [ ] Refresh rotate cookie thành công.
- [ ] Logout và Logout All trả `204`.
- [ ] Refresh sau logout trả `401`.
- [ ] CORS preflight trả allow-origin và allow-credentials đúng.
- [ ] XML trả `200`, media type không hỗ trợ trả `406`.

