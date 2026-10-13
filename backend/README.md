# Backend setup guide - Tutor Management System

**Kiến trúc:** Modular Monolith + Clean Architecture theo module + Vertical Slice  
**Runtime:** .NET 10 / ASP.NET Core  
**Database:** PostgreSQL + EF Core migrations  
**Trạng thái hiện tại:** Foundation backend đã được scaffold, restore, build và test thành công.

## Chạy dự án với PostgreSQL local

```powershell
Set-Location -LiteralPath 'D:\Project\QuanLyDayThem\backend'

dotnet tool restore
dotnet restore .\TutorManagementSystem.slnx
dotnet build .\TutorManagementSystem.slnx --no-restore --configuration Release -m:1
dotnet test .\TutorManagementSystem.slnx --no-build --configuration Release -m:1
```

Máy hiện có PostgreSQL 18 tại `C:\Program Files\PostgreSQL\18`, lắng nghe ở
`localhost:5432`, và có pgAdmin 4. Chạy script một lần để:

- kiểm tra PostgreSQL local;
- tạo/cập nhật role `tutor_app`;
- tạo database `tutor_management`;
- lưu connection string bằng .NET User Secrets cho cả ba host;
- chạy toàn bộ EF Core migrations.

```powershell
.\tools\setup-local-postgres.ps1
```

Script sẽ hỏi hai mật khẩu bằng prompt ẩn: mật khẩu admin `postgres` đã đặt khi
cài PostgreSQL và mật khẩu mới dành cho `tutor_app`. Mật khẩu không được ghi vào
repository hay `appsettings.json`.

Sau khi setup thành công, chạy API:

```powershell
dotnet run --project .\src\Hosts\TutorManagement.Api --urls http://localhost:8080
```

Ở terminal khác, có thể chạy Worker:

```powershell
dotnet run --project .\src\Hosts\TutorManagement.Worker
```

Các endpoint kiểm tra:

```text
GET http://localhost:8080/health/live
GET http://localhost:8080/health/ready
GET http://localhost:8080/api/v1/modules/identity-access
GET http://localhost:8080/api/v1/modules/organizations
GET http://localhost:8080/api/v1/modules/academics
GET http://localhost:8080/openapi/v1.json
```

> `.env.example` chỉ chứa credential phát triển cục bộ. Không dùng credential này
> cho staging/production và không commit file `.env`.

---

## 1. Trạng thái môi trường hiện tại

Đã kiểm tra ngày 13/10/2026:

| Thành phần | Trạng thái |
|---|---|
| .NET SDK | Đã cài `10.0.401` |
| ASP.NET Core runtime | Đã cài `10.0.12` |
| Git | Đã cài `2.51.0.windows.1` |
| PostgreSQL local | `18`, service đang chạy, `localhost:5432` accepting connections |
| `psql` | Có tại `C:\Program Files\PostgreSQL\18\bin\psql.exe` |
| pgAdmin 4 | Có tại `C:\Program Files\PostgreSQL\18\pgAdmin 4` |
| Docker | Không bắt buộc cho local development |
| `global.json` | Đã pin SDK `10.0.401` |
| Backend solution | `TutorManagementSystem.slnx` |
| Backend projects | 24 project |
| Build Release | Thành công, 0 warning, 0 error |
| Tests | 10/10 passed |
| NuGet audit | Không phát hiện package có lỗ hổng đã biết |
| EF migrations | Đã có migration nền cho 3 module đầu tiên |

Kiểm tra nhanh PostgreSQL local bất kỳ lúc nào:

```powershell
& 'C:\Program Files\PostgreSQL\18\bin\pg_isready.exe' -h localhost -p 5432
```

Kết quả mong đợi: `localhost:5432 - accepting connections`.

---

## 2. Cấu trúc đã tạo

```text
backend/
├─ src/
│  ├─ Hosts/
│  │  ├─ TutorManagement.Api/
│  │  ├─ TutorManagement.Worker/
│  │  └─ TutorManagement.DbMigrator/
│  ├─ BuildingBlocks/
│  │  ├─ TutorManagement.SharedKernel/
│  │  ├─ TutorManagement.Application.Abstractions/
│  │  ├─ TutorManagement.Infrastructure/
│  │  └─ TutorManagement.WebApi/
│  └─ Modules/
│     ├─ IdentityAccess/
│     ├─ Organizations/
│     ├─ Academics/
│     ├─ LearningOperations/
│     ├─ Finance/
│     ├─ HumanResources/
│     ├─ Compliance/
│     ├─ Notifications/
│     ├─ DocumentsExams/
│     └─ Reporting/
├─ tests/
├─ deploy/
├─ tools/
├─ artifacts/
└─ docs/
```

Mỗi module hiện có năm layer/project placeholder:

```text
TutorManagement.Modules.<Module>.Domain
TutorManagement.Modules.<Module>.Application
TutorManagement.Modules.<Module>.Infrastructure
TutorManagement.Modules.<Module>.Contracts
TutorManagement.Modules.<Module>.Api
```

Lưu ý: Git không theo dõi thư mục rỗng. Các folder sẽ xuất hiện trong commit sau khi scaffold `.csproj` hoặc có tài liệu/code. Không cần tạo hàng trăm `.gitkeep` nếu bước scaffold được thực hiện ngay sau khi chốt kiến trúc.

---

## 3. Trình tự setup tổng thể

Thực hiện theo thứ tự sau:

```text
1. Pin SDK và tạo solution
2. Tạo Hosts và Building Blocks
3. Tạo ba module đầu tiên
4. Thiết lập project references
5. Central Package Management
6. Compiler/analyzer conventions
7. PostgreSQL + EF Core
8. DbMigrator
9. API conventions
10. Authentication/Authorization
11. Logging/Observability
12. Tests
13. Docker Compose
14. CI pipeline
15. Tạo migration đầu tiên
```

Không scaffold cùng lúc cả 10 module. Bắt đầu bằng:

- `IdentityAccess`.
- `Organizations`.
- `Academics`.

Các module khác giữ folder placeholder và chỉ tạo project khi bắt đầu epic tương ứng.

---

## 4. Bước 1 - Chuyển đúng thư mục và pin .NET SDK

Mở PowerShell và luôn chuyển vào đúng backend trước khi chạy các lệnh tiếp theo:

```powershell
$backendRoot = 'D:\Project\QuanLyDayThem\backend'
Set-Location -LiteralPath $backendRoot
Get-Location
```

`Get-Location` phải trả về `D:\Project\QuanLyDayThem\backend`. Không chạy
`Get-ChildItem -Recurse` từ `C:\Users\ADMIN` vì lệnh sẽ quét cả cache như
`.gradle`, không liên quan đến dự án.

Sau đó pin SDK:

```powershell
dotnet new globaljson --sdk-version 10.0.401 --roll-forward latestPatch
```

Mục đích:

- Máy developer và CI dùng cùng SDK feature band.
- Patch được phép cập nhật an toàn trong cùng version policy.
- Tránh một máy build .NET 9, máy khác build .NET 10.

Kiểm tra:

```powershell
dotnet --version
```

Kết quả mong đợi hiện tại: `10.0.401`.

---

## 5. Bước 2 - Tạo solution và solution filters

Với .NET 10, `dotnet new sln` mặc định tạo solution theo định dạng mới `.slnx`.
Solution hiện đã tồn tại, kiểm tra bằng:

```powershell
Test-Path -LiteralPath .\TutorManagementSystem.slnx
```

Kết quả phải là `True`. Chỉ khi file chưa tồn tại mới chạy:

```powershell
dotnet new sln --name TutorManagementSystem
```

Các lệnh trong tài liệu này vì vậy sử dụng `TutorManagementSystem.slnx`, không
sử dụng `TutorManagementSystem.sln`.

Sau khi có nhiều project, tạo solution filter:

```text
TutorManagementSystem.Api.slnf
TutorManagementSystem.Tests.slnf
```

Mục đích:

- Solution chính chứa toàn bộ backend.
- API filter chỉ load host và module cần chạy.
- Test filter tối ưu thời gian load IDE/CI job.

Không tạo nhiều solution độc lập cho từng module; điều đó làm dependency và build toàn hệ thống khó kiểm soát.

---

## 6. Bước 3 - Tạo Hosts

### 6.1. API host

```powershell
dotnet new webapi `
  --name TutorManagement.Api `
  --output src/Hosts/TutorManagement.Api `
  --framework net10.0 `
  --use-controllers `
  --no-https false
```

API host chỉ chịu trách nhiệm:

- Composition root/Dependency Injection.
- Global middleware.
- Authentication/Authorization host configuration.
- OpenAPI.
- Health checks.
- Map các module API.

Không đặt entity, repository hoặc business use case tại host.

### 6.2. Worker host

```powershell
dotnet new worker `
  --name TutorManagement.Worker `
  --output src/Hosts/TutorManagement.Worker `
  --framework net10.0
```

Worker dùng cho:

- Outbox dispatch.
- Notification delivery.
- Report/export generation.
- File/document processing.
- Retry/dead-letter handling.

Không đưa các job dài vào HTTP request lifecycle.

### 6.3. Database migrator

```powershell
dotnet new console `
  --name TutorManagement.DbMigrator `
  --output src/Hosts/TutorManagement.DbMigrator `
  --framework net10.0
```

Migrator là run-to-completion process:

- Apply migrations của từng module.
- Chạy trước API deployment.
- Trả exit code khác 0 khi lỗi.
- API production không tự migrate database khi startup.

---

## 7. Bước 4 - Tạo Building Blocks

```powershell
dotnet new classlib --name TutorManagement.SharedKernel `
  --output src/BuildingBlocks/TutorManagement.SharedKernel --framework net10.0

dotnet new classlib --name TutorManagement.Application.Abstractions `
  --output src/BuildingBlocks/TutorManagement.Application.Abstractions --framework net10.0

dotnet new classlib --name TutorManagement.Infrastructure `
  --output src/BuildingBlocks/TutorManagement.Infrastructure --framework net10.0

dotnet new classlib --name TutorManagement.WebApi `
  --output src/BuildingBlocks/TutorManagement.WebApi --framework net10.0
```

### SharedKernel chỉ chứa

- `Entity`, `AggregateRoot`, `ValueObject`.
- Domain event abstractions.
- Strongly typed IDs.
- `Result`/`Error` primitives thật sự dùng chung.

Không đặt business entity như Student, Payment hoặc Teacher trong SharedKernel.

### Application.Abstractions chỉ chứa

- Command/query handler contracts.
- Unit of work/transaction contracts chung.
- Current user, clock và authorization abstractions.
- Cross-cutting pipeline behaviors contracts.

### Infrastructure building block chỉ chứa

- Outbox/inbox infrastructure chung.
- Base persistence helpers không mang nghiệp vụ.
- File storage/cache/telemetry adapters chung.

### WebApi building block chỉ chứa

- `ApiResponse<T>` và `PagedResponse<T>`.
- API error model.
- Versioning/model binding/OpenAPI conventions.
- Không chứa controller của module.

---

## 8. Bước 5 - Tạo module đầu tiên

Ví dụ `Academics`:

```powershell
dotnet new classlib --name TutorManagement.Modules.Academics.Domain `
  --output src/Modules/Academics/TutorManagement.Modules.Academics.Domain --framework net10.0

dotnet new classlib --name TutorManagement.Modules.Academics.Application `
  --output src/Modules/Academics/TutorManagement.Modules.Academics.Application --framework net10.0

dotnet new classlib --name TutorManagement.Modules.Academics.Infrastructure `
  --output src/Modules/Academics/TutorManagement.Modules.Academics.Infrastructure --framework net10.0

dotnet new classlib --name TutorManagement.Modules.Academics.Contracts `
  --output src/Modules/Academics/TutorManagement.Modules.Academics.Contracts --framework net10.0

dotnet new classlib --name TutorManagement.Modules.Academics.Api `
  --output src/Modules/Academics/TutorManagement.Modules.Academics.Api --framework net10.0
```

Lặp lại cho `IdentityAccess` và `Organizations`.

Không tạo tất cả module chỉ để solution có nhiều project; module rỗng làm tăng build time và maintenance noise.

---

## 9. Bước 6 - Thêm project vào solution

Chỉ thực hiện bước này sau khi đã scaffold các `.csproj` ở các bước tạo Hosts,
Building Blocks và các module đầu tiên. Giới hạn tìm kiếm trong `backend/src` và
`backend/tests`, không quét đệ quy từ thư mục người dùng:

```powershell
$backendRoot = 'D:\Project\QuanLyDayThem\backend'
$solutionPath = Join-Path $backendRoot 'TutorManagementSystem.slnx'

$projectPaths = @(
  Get-ChildItem `
    -LiteralPath (Join-Path $backendRoot 'src'), (Join-Path $backendRoot 'tests') `
    -Recurse `
    -File `
    -Filter '*.csproj' `
    -ErrorAction Stop |
  Select-Object -ExpandProperty FullName
)

if ($projectPaths.Count -eq 0) {
  Write-Warning 'Chưa có project .csproj. Bỏ qua bước add; hãy scaffold project trước.'
}
else {
  dotnet sln $solutionPath add $projectPaths
}
```

Kiểm tra:

```powershell
dotnet sln .\TutorManagementSystem.slnx list
```

Nhóm solution folder trong IDE:

- `Hosts`.
- `BuildingBlocks`.
- `Modules/IdentityAccess`.
- `Modules/Organizations`.
- `Modules/Academics`.
- `Tests`.

---

## 10. Bước 7 - Project references

### 10.1. Dependency direction

```text
Module.Domain
  -> SharedKernel

Module.Contracts
  -> không phụ thuộc module internals

Module.Application
  -> Module.Domain
  -> Module.Contracts
  -> Application.Abstractions

Module.Infrastructure
  -> Module.Domain
  -> Module.Application
  -> Infrastructure building block

Module.Api
  -> Module.Application
  -> Module.Contracts
  -> WebApi building block

API Host
  -> từng Module.Api
  -> từng Module.Infrastructure qua module registration
```

### 10.2. Ví dụ Academics

```powershell
dotnet add src/Modules/Academics/TutorManagement.Modules.Academics.Domain reference `
  src/BuildingBlocks/TutorManagement.SharedKernel

dotnet add src/Modules/Academics/TutorManagement.Modules.Academics.Application reference `
  src/Modules/Academics/TutorManagement.Modules.Academics.Domain `
  src/Modules/Academics/TutorManagement.Modules.Academics.Contracts `
  src/BuildingBlocks/TutorManagement.Application.Abstractions

dotnet add src/Modules/Academics/TutorManagement.Modules.Academics.Infrastructure reference `
  src/Modules/Academics/TutorManagement.Modules.Academics.Domain `
  src/Modules/Academics/TutorManagement.Modules.Academics.Application `
  src/BuildingBlocks/TutorManagement.Infrastructure

dotnet add src/Modules/Academics/TutorManagement.Modules.Academics.Api reference `
  src/Modules/Academics/TutorManagement.Modules.Academics.Application `
  src/Modules/Academics/TutorManagement.Modules.Academics.Contracts `
  src/BuildingBlocks/TutorManagement.WebApi
```

### 10.3. Dependency bị cấm

```text
Domain -> Application/Infrastructure/API
Application -> Infrastructure/API
Module A -> Module B Domain
Module A -> Module B Infrastructure
Controller -> DbContext
Controller -> Repository implementation
```

Architecture tests phải enforce các quy tắc này.

---

## 11. Bước 8 - Central Package Management

Tạo `Directory.Packages.props` ở `backend/` và bật:

```xml
<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
```

Lợi ích:

- Một nơi quản lý version.
- Tất cả module dùng cùng EF Core/Npgsql/validation/test versions.
- Dễ chạy vulnerability/license review.

### 11.1. Package groups cần thiết

**API/Web**

- `Asp.Versioning.Mvc`.
- `Asp.Versioning.Mvc.ApiExplorer`.
- `Swashbuckle.AspNetCore` hoặc OpenAPI UI được team chọn thống nhất.
- `Microsoft.AspNetCore.Authentication.JwtBearer`.
- `Microsoft.Extensions.Http.Resilience`.

**Application**

- `FluentValidation`.
- `FluentValidation.DependencyInjectionExtensions`.
- Không bắt buộc MediatR; có thể dùng dispatcher/handler abstractions nội bộ.

**Persistence**

- `Microsoft.EntityFrameworkCore`.
- `Microsoft.EntityFrameworkCore.Design` với `PrivateAssets=all`.
- `Npgsql.EntityFrameworkCore.PostgreSQL`.
- `EFCore.NamingConventions` nếu dùng snake_case convention package.
- `Dapper` chỉ khi có query/report cần tối ưu thực tế.

**Logging/Observability**

- `Serilog.AspNetCore` nếu chọn Serilog.
- OpenTelemetry hosting, ASP.NET, HTTP và Npgsql instrumentations.

**Testing**

- `Microsoft.NET.Test.Sdk`.
- `xunit` và runner.
- `NSubstitute` hoặc mocking framework thống nhất.
- `Microsoft.AspNetCore.Mvc.Testing`.
- `Testcontainers.PostgreSql`.
- `NetArchTest.Rules` hoặc `ArchUnitNET`.

Không cài toàn bộ package ngay từ đầu. Package chỉ được thêm khi project có use case thực sự.

---

## 12. Bước 9 - Compiler và code quality conventions

Tạo `Directory.Build.props`:

- Target framework mặc định `net10.0`.
- `Nullable=enable`.
- `ImplicitUsings=enable`.
- `TreatWarningsAsErrors=true` cho project code.
- `AnalysisLevel=latest-recommended` hoặc version được team pin.
- Deterministic build.
- Generate XML docs cho API contracts nếu cần OpenAPI.

Tạo `.editorconfig`:

- UTF-8.
- LF hoặc CRLF thống nhất; Git normalize rõ ràng.
- 4 spaces cho C#, 2 spaces cho JSON/YAML/XML nếu team chọn.
- Naming convention private field, interface, async suffix.
- Analyzer severity thống nhất.

Thêm `.gitattributes` để tránh file encoding/line-ending không nhất quán.

Các command CI đầu tiên:

```powershell
dotnet restore
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release
dotnet format --verify-no-changes
```

---

## 13. Bước 10 - Local tool manifest

Không yêu cầu mọi developer cài `dotnet-ef` global.

```powershell
dotnet new tool-manifest
dotnet tool install dotnet-ef
```

Sau khi clone:

```powershell
dotnet tool restore
```

Version tool được commit trong `dotnet-tools.json` ở thư mục `backend/` theo
định dạng local tool manifest của .NET 10.

---

## 14. Bước 11 - PostgreSQL local setup

### 14.1. Development database

Luồng local mặc định:

```text
Windows PostgreSQL service
  -> tutor_management database
      -> DbMigrator
          -> API / Worker
```

Setup tự động từ thư mục `backend`:

```powershell
.\tools\setup-local-postgres.ps1
```

Nếu chỉ muốn tạo role/database và User Secrets mà chưa apply migration:

```powershell
.\tools\setup-local-postgres.ps1 -SkipMigration
```

Không cần thêm PostgreSQL vào `PATH`; script tự dùng bản cài tại
`C:\Program Files\PostgreSQL\18`. Có thể truyền `-PostgresHome`, `-Port`,
`-AdminUser`, `-AppUser` hoặc `-Database` nếu môi trường khác mặc định.

Tài khoản migration có thể cần quyền schema cao hơn runtime account. Production nên tách:

- `tutor_migrator`: thay đổi schema.
- `tutor_app`: CRUD runtime trong schema được cấp.
- `tutor_readonly`: reporting/support khi cần.

### 14.2. Connection string

Tên thống nhất:

```text
ConnectionStrings__MainDatabase
```

Không commit password thật trong:

- `appsettings.json`.
- `docker-compose.yml`.
- launch settings.
- test source.

### 14.3. Development secrets

Script setup đã lưu cùng connection string cho API, Worker và DbMigrator. Dữ liệu
này nằm ngoài repository trong user profile của Windows và chỉ được nạp khi
environment là `Development`.

Lệnh `dotnet user-secrets list` sẽ hiển thị cả giá trị connection string và
password. Chỉ dùng trên máy cá nhân khi thật sự cần kiểm tra; không chụp màn hình,
chia sẻ terminal output hoặc commit output đó.

### 14.4. Xem database bằng pgAdmin 4

Mở pgAdmin:

```powershell
& 'C:\Program Files\PostgreSQL\18\pgAdmin 4\runtime\pgAdmin4.exe'
```

Đăng ký server bằng các giá trị:

```text
Host:                 localhost
Port:                 5432
Maintenance database: tutor_management
Username:             tutor_app
Password:             mật khẩu app đã nhập khi chạy script setup
```

Sau khi migration hoàn tất, database có ba schema ban đầu: `iam`, `organization`
và `academics`. Mỗi schema có bảng migration history riêng để các module không
ghi đè lịch sử migration của nhau.

---

## 15. Bước 12 - DbContext và migration strategy

Mỗi module sở hữu schema và DbContext:

```text
IdentityDbContext            -> iam
OrganizationDbContext        -> organization
AcademicsDbContext           -> academics
LearningOperationsDbContext  -> learning
FinanceDbContext             -> finance
HumanResourcesDbContext      -> hr
ComplianceDbContext          -> compliance
NotificationDbContext        -> notification
DocumentsDbContext           -> document
ReportingDbContext           -> reporting
```

Mỗi DbContext cấu hình migration history table trong schema riêng.

### Migration workflow

1. Thay đổi domain/configuration.
2. Tạo migration trong module Infrastructure.
3. Review migration `Up`, `Down`, SQL và lock risk.
4. CI tạo database từ số 0.
5. CI chạy migration từ previous version.
6. DbMigrator apply trước API rollout.
7. Không sửa migration đã deploy.

Ví dụ:

```powershell
dotnet ef migrations add InitialAcademics `
  --project src/Modules/Academics/TutorManagement.Modules.Academics.Infrastructure `
  --startup-project src/Hosts/TutorManagement.DbMigrator `
  --context AcademicsDbContext `
  --output-dir Persistence/Migrations
```

Production không dùng API startup `Database.Migrate()` vì nhiều replica có thể tranh migration.

---

## 16. Bước 13 - API nền tảng

Thiết lập trước khi viết feature controller:

### 16.1. Response contract

- `ApiResponse<T>`.
- `PagedResponse<T>`.
- `ApiError` có `code`, `field`, `message`.
- `traceId` trong response.
- Không trả EF entity.

### 16.2. Exception middleware

Map chuẩn:

| Exception | HTTP |
|---|---:|
| Request validation | 400 |
| Authentication | 401 |
| Forbidden | 403 |
| Not found | 404 |
| Conflict/concurrency | 409 |
| Business rule | 422 |
| Unknown | 500 |

Không trả stack trace/SQL/secret.

### 16.3. API versioning

- URL versioning `/api/v1/...`.
- API explorer group theo version.
- Không copy controller V1 sang V2 nếu contract không đổi.

### 16.4. Content negotiation

- JSON mặc định.
- XML formatter.
- `ReturnHttpNotAcceptable=true`.
- Unsupported Accept trả 406.

### 16.5. Query conventions

- `search`.
- `sort=field,-otherField`.
- `page`, `size`.
- `fields`.
- `expand`.
- Tất cả dùng whitelist và có giới hạn.

### 16.6. OpenAPI

- Một OpenAPI document theo version hoặc chiến lược được chốt.
- Bearer security scheme.
- Authorize button.
- Schema/status/example đầy đủ.

---

## 17. Bước 14 - Security nền tảng

### 17.1. Authentication

- Password hash; không mã hóa có thể giải ngược và không plaintext.
- JWT signing key từ secret store.
- Issuer/audience/expiry validation.
- Refresh token random, lưu hash.
- Token rotation và reuse detection.
- Revoke session khi đổi password/khóa user.

### 17.2. Authorization

- Role cho quyền cấp cao.
- Permission/policy cho tác vụ cụ thể.
- Scope theo tenant/campus/class.
- Object-level authorization trong application use case.

### 17.3. API security baseline

- HTTPS/HSTS production.
- CORS whitelist theo environment.
- Rate limit login/OTP/public endpoints.
- Request size limit.
- Secure headers.
- Không log Authorization header hoặc credential.

---

## 18. Bước 15 - Logging và observability

Tối thiểu:

- Structured logs.
- Correlation ID/trace ID.
- Request method, normalized path, status, duration.
- User/tenant ID khi phù hợp.
- Database call duration.
- Outbox backlog/job failures.

OpenTelemetry:

- ASP.NET Core traces.
- HttpClient traces.
- Npgsql traces.
- Metrics và exporters theo môi trường.

Health endpoints:

```text
/health/live
/health/ready
```

- Liveness không phụ thuộc external service.
- Readiness kiểm tra PostgreSQL và dependency bắt buộc.

---

## 19. Bước 16 - Test projects

Tạo ban đầu:

```powershell
dotnet new xunit --name TutorManagement.ArchitectureTests `
  --output tests/ArchitectureTests --framework net10.0

dotnet new xunit --name TutorManagement.IntegrationTests `
  --output tests/IntegrationTests --framework net10.0
```

Khi module có code, tạo test cạnh module:

```text
Modules/Academics/tests/
├─ Academics.Domain.Tests
├─ Academics.Application.Tests
├─ Academics.Infrastructure.IntegrationTests
└─ Academics.Api.IntegrationTests
```

Test levels:

- Domain unit tests.
- Application handler tests.
- Repository tests với PostgreSQL container thật.
- API integration tests với `WebApplicationFactory`.
- Architecture dependency tests.
- OpenAPI/contract breaking-change tests.

Không dùng EF InMemory để khẳng định query PostgreSQL hoạt động đúng.

---

## 20. Bước 17 - Docker Compose tùy chọn

Development compose nên có:

```text
postgres
db-migrator
api
worker
```

Thứ tự:

```text
postgres healthy
  -> db-migrator completed successfully
      -> api starts
      -> worker starts
```

Docker Compose được giữ lại cho CI, kiểm thử môi trường sạch hoặc khi cả team cần
một phiên bản PostgreSQL đồng nhất. Luồng phát triển chính hiện tại dùng
PostgreSQL cài trực tiếp trên Windows để dễ xem dữ liệu bằng pgAdmin/psql.

Không chạy đồng thời PostgreSQL Docker và PostgreSQL Windows trên cùng port 5432.
Khi chủ động chuyển sang Docker, dừng service local hoặc đổi port compose trước.

Luồng local:

```powershell
dotnet run --project src/Hosts/TutorManagement.DbMigrator
dotnet run --project src/Hosts/TutorManagement.Api
dotnet run --project src/Hosts/TutorManagement.Worker
```

---

## 21. Bước 18 - CI baseline

Pipeline pull request:

```text
checkout
  -> setup .NET theo global.json
  -> dotnet tool restore
  -> restore
  -> format verification
  -> build Release
  -> unit tests
  -> architecture tests
  -> integration tests with PostgreSQL
  -> migration validation
  -> OpenAPI generation/check
  -> dependency/security scan
  -> Docker image build
```

Không deploy nếu:

- Có migration model drift.
- Architecture rule fail.
- OpenAPI có breaking change chưa được duyệt.
- Critical/high dependency vulnerability chưa xử lý hoặc chưa có risk acceptance.

---

## 22. Cấu hình cần có theo environment

```text
ASPNETCORE_ENVIRONMENT
ConnectionStrings__MainDatabase
Auth__Jwt__Issuer
Auth__Jwt__Audience
Auth__Jwt__SigningKey
Auth__Jwt__AccessTokenMinutes
Auth__Jwt__RefreshTokenDays
Cors__AllowedOrigins__0
Observability__ServiceName
Observability__OtlpEndpoint
Storage__Provider
Storage__Bucket
```

Nguyên tắc:

- `appsettings.json`: default không nhạy cảm.
- `appsettings.Development.json`: config dev không chứa secret.
- User Secrets: secret local.
- Environment/secret manager: staging/production.
- Options classes phải validate khi startup.

---

## 23. Definition of Ready trước feature đầu tiên

- [ ] PostgreSQL Windows service chạy và port 5432 healthy.
- [ ] `global.json` pin SDK.
- [ ] Solution và ba hosts build được.
- [ ] Building blocks build được.
- [ ] Ba module đầu có project references đúng.
- [ ] Central package management hoạt động.
- [ ] Nullable/analyzers/format conventions bật.
- [ ] PostgreSQL local healthy.
- [ ] DbMigrator kết nối được.
- [ ] Response/error middleware hoạt động.
- [ ] API versioning + JSON/XML + Swagger hoạt động.
- [ ] JWT skeleton có secret validation.
- [ ] Architecture test pass.
- [ ] CI baseline pass.

Sau checklist này mới bắt đầu feature đầu tiên: `IdentityAccess/Login` hoặc `Academics/CreateStudent`.

---

## 24. Những việc chưa nên làm ngay

- Không tạo microservices.
- Không thêm message broker nếu outbox nội bộ/database queue đủ dùng.
- Không cài Redis trước khi có use case cache/rate limit/distributed lock rõ ràng.
- Không tạo generic repository/framework nội bộ lớn.
- Không scaffold cả 10 module thành project rỗng.
- Không viết base service/base controller chứa mọi logic.
- Không auto-generate hàng trăm mapping/profile class khi chưa có contract.
- Không tạo migration production từ máy cá nhân mà không review.
- Không thêm Kubernetes trước khi Docker Compose và deployment đơn giản ổn định.

---

## 25. Bước thực thi tiếp theo được khuyến nghị

Lần triển khai kế tiếp nên thực hiện đúng phạm vi:

1. Tạo `global.json`, solution và central props.
2. Scaffold ba Hosts và bốn Building Blocks.
3. Scaffold `IdentityAccess`, `Organizations`, `Academics`.
4. Add project references.
5. Cài package nền tảng.
6. Tạo architecture tests.
7. Build solution trống thành công.

Chưa cần tạo entity hoặc API nghiệp vụ ở bước này. Sau khi nền tảng build sạch mới tiếp tục PostgreSQL, migrations và module IdentityAccess.
