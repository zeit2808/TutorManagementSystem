# Setup backend local bằng Visual Studio

Đây là quy trình **IDE-first** cho backend tại
`D:\Project\QuanLyDayThem\backend`: bấm ở đâu, chọn project nào, mong đợi kết
quả gì và vì sao cần bước đó. Package Manager Console chỉ được dùng cho thao
tác EF Core migration.

## 1. Trạng thái đã kiểm tra ngày 16/10/2026

- .NET SDK `10.0.401` và Visual Studio Community 2026 đã có sẵn.
- PostgreSQL 18 chạy ở `localhost:5432`; database `tutor_management`, role
  `tutor_app`.
- Identity đã có 4 migration, mới nhất là
  `20261016143528_EnforceSingleEmailVerificationToken`.
- Organizations và Academics đã có migration khởi tạo.
- Cả ba EF model không có thay đổi chưa được migrate.
- Build toàn solution: 0 warning, 0 error.
- Test: 31/31 pass (3 architecture, 28 integration).
- API thật đã qua smoke test: health, Swagger, module status, JSON/XML/406,
  login, Owner authorization, refresh, logout, logout-all và CORS credentials.

`organization` và `academics` hiện chỉ có `__ef_migrations_history`. Đây không
phải lỗi setup: hai module này mới có API status và chưa có entity nghiệp vụ.

## 2. Mở solution và nhận biết project

1. Mở Visual Studio → **Open a project or solution**.
2. Chọn `D:\Project\QuanLyDayThem\backend\TutorManagementSystem.sln`.
3. Solution Explorer phải có `Hosts`, `BuildingBlocks`, `Modules`, `Tests`.

Ba executable project trong `Hosts`:

| Project | Vai trò | Khi nào chạy |
|---|---|---|
| `TutorManagement.Api` | HTTP API và Swagger | Mỗi lần phát triển/test API |
| `TutorManagement.Worker` | Xử lý email outbox qua File hoặc SMTP | Khi test invitation/email bảo mật |
| `TutorManagement.DbMigrator` | Apply migration và seed Owner | Setup lần đầu/có migration mới |

Các project `Domain`, `Application`, `Contracts`, `Infrastructure`, `Api` bên
trong module là class library, không đặt làm startup project.

## 3. Kiểm tra PostgreSQL bằng giao diện

### Windows service

1. Nhấn `Win + R`, nhập `services.msc`.
2. Tìm `postgresql-x64-18`.
3. **Status** phải là `Running`, **Startup Type** nên là `Automatic`.
4. Nếu đang dừng, nhấp phải → **Start**.

### pgAdmin

1. Mở `Servers → PostgreSQL 18 → Databases → tutor_management → Schemas`.
2. Phải thấy `iam`, `organization`, `academics`, `public`.
3. `iam → Tables` phải có `users`, `roles`, `user_roles`, `refresh_tokens`,
   `user_security_tokens`, `email_outbox_messages`, `__ef_migrations_history`.
4. `organization` và `academics` hiện chỉ cần `__ef_migrations_history`.

Không tạo bảng bằng tay trong pgAdmin. Bảng, FK và index phải đi qua migration
để database luôn đồng bộ với source code.

## 4. User Secrets trong Visual Studio

Không ghi password DB, JWT key hoặc mật khẩu Owner vào `appsettings.json`.

### TutorManagement.Api

Nhấp phải project → **Manage User Secrets**, dùng cấu trúc:

```json
{
  "ConnectionStrings:MainDatabase": "Host=localhost;Port=5432;Database=tutor_management;Username=tutor_app;Password=<DB_PASSWORD>;Include Error Detail=false",
  "Jwt:SigningKey": "<RANDOM_KEY_AT_LEAST_64_CHARACTERS>"
}
```

### TutorManagement.Worker

Nhấp phải project → **Manage User Secrets**:

```json
{
  "ConnectionStrings:MainDatabase": "Host=localhost;Port=5432;Database=tutor_management;Username=tutor_app;Password=<DB_PASSWORD>;Include Error Detail=false"
}
```

### TutorManagement.DbMigrator

Nhấp phải project → **Manage User Secrets**:

```json
{
  "ConnectionStrings:MainDatabase": "Host=localhost;Port=5432;Database=tutor_management;Username=tutor_app;Password=<DB_PASSWORD>;Include Error Detail=false",
  "Seed:OwnerUsername": "admin",
  "Seed:OwnerPassword": "<YOUR_LOCAL_OWNER_PASSWORD>"
}
```

Owner password dài 12–128 ký tự, có chữ hoa, chữ thường, chữ số, ký tự đặc biệt,
không có khoảng trắng. Đây là account dùng để test login. DbMigrator chỉ tạo
Owner chưa tồn tại; sửa secret không đổi mật khẩu user đã có.

## 5. Restore và build bằng IDE

1. Nhấp phải solution → **Restore NuGet Packages**.
2. Chọn configuration `Debug` để đặt breakpoint hoặc `Release` để kiểm tra.
3. Chọn **Build → Rebuild Solution**.
4. Mở **View → Error List**; kết quả đúng là build success, không error.

Trước khi Rebuild, bấm Stop nếu API đang chạy. Nếu không, Windows có thể khóa
DLL và báo `Exceeded retry count of 10`.

## 6. Migration trong Package Manager Console

EF cần phân biệt:

- **Default project** là Infrastructure chứa DbContext/migration/snapshot.
- **Startup project** là DbMigrator cung cấp DI, config và connection string.

### Identity

1. Nhấp phải `TutorManagement.DbMigrator` → **Set as Startup Project**.
2. Mở **Tools → NuGet Package Manager → Package Manager Console**.
3. Default project chọn
   `TutorManagement.Modules.IdentityAccess.Infrastructure`.
4. Kiểm tra rồi cập nhật:

```powershell
Get-Migration -Context IdentityDbContext -StartupProject TutorManagement.DbMigrator
Update-Database -Context IdentityDbContext -StartupProject TutorManagement.DbMigrator
```

Danh sách phải kết thúc ở `EnforceSingleEmailVerificationToken`.

### Organizations

Đổi Default project sang
`TutorManagement.Modules.Organizations.Infrastructure`:

```powershell
Get-Migration -Context OrganizationDbContext -StartupProject TutorManagement.DbMigrator
Update-Database -Context OrganizationDbContext -StartupProject TutorManagement.DbMigrator
```

### Academics

Đổi Default project sang `TutorManagement.Modules.Academics.Infrastructure`:

```powershell
Get-Migration -Context AcademicsDbContext -StartupProject TutorManagement.DbMigrator
Update-Database -Context AcademicsDbContext -StartupProject TutorManagement.DbMigrator
```

Phải chạy riêng ba context vì mỗi module sở hữu model và bảng migration riêng.
Cùng một PostgreSQL database nhưng khác schema vẫn có đầy đủ PK, unique index,
FK; PostgreSQL cũng hỗ trợ FK xuyên schema khi sau này thực sự cần.

`Update-Database` không chạy custom seed. Để seed Owner lần đầu:

1. Kiểm tra User Secrets của DbMigrator.
2. Đặt `TutorManagement.DbMigrator` làm startup project.
3. Nhấn `Ctrl + F5` và chờ host kết thúc không có exception.

## 7. Chạy API và Worker cùng lúc

1. Nhấp phải solution → **Configure Startup Projects**. Nếu không thấy, mở
   **Solution Properties → Common Properties → Startup Project**.
2. Chọn **Multiple startup projects**.
3. Đặt:

| Project | Action |
|---|---|
| `TutorManagement.Api` | Start |
| `TutorManagement.Worker` | Start |
| `TutorManagement.DbMigrator` | None |

4. Nhấn `F5` để debug hoặc `Ctrl + F5` để chạy không debug.
5. Output của API phải có `Now listening on: http://localhost:5217`.
6. Output của Worker phải có `Tutor Management worker started`.

API warm-up xác thực một lần khi startup, trên máy hiện tại mất gần 5 giây. Hãy
chờ `Now listening on` rồi mới test. Sau warm-up, GET đo được khoảng 0,21–0,23
giây. Login khoảng 2,4 giây vì phải verify password hash, không còn 8,4 giây.

## 8. Test Swagger theo thứ tự

Mở [Swagger local](http://localhost:5217/swagger/index.html).

### Nền tảng

- `http://localhost:5217/health/live`: HTTP 200.
- `http://localhost:5217/health/ready`: HTTP 200 khi DB sẵn sàng.
- `GET /api/v1/platform/centers` sau khi đăng nhập PlatformAdmin: HTTP 200.

Các GET `/api/v1/modules/...` trước đây chỉ trả chuỗi trạng thái giả và đã được
bỏ; dùng `/health/live` và `/health/ready` để kiểm tra hạ tầng.

### Login → Authorize → Refresh → Logout

1. `POST /api/v1/auth/login` → **Try it out**.
2. Nhập username/password từ User Secrets của DbMigrator → Execute.
3. Mong đợi 200 và `data.accessToken`; JSON không được có refresh token.
4. Browser tự nhận cookie `tm.refresh` có `HttpOnly` và `SameSite`.
5. Sao chép access token, nhấn **Authorize**, dán **chỉ token**, không thêm chữ
   `Bearer`.
6. `GET /api/v1/platform/centers` phải trả 200 cho PlatformAdmin.
7. `POST /api/v1/auth/refresh-token` không có body; phải trả access token mới.
8. `POST /api/v1/auth/logout` trả 204; refresh sau đó phải trả 401.

Test `logout-all`: login lại, Authorize bằng token mới, gọi
`POST /api/v1/auth/logout-all`. Kết quả 204; refresh sau đó phải là 401.

### Xác nhận public Register đã bị loại bỏ

Route `POST /api/v1/auth/register` không được xuất hiện trong Swagger. Nếu gửi
request trực tiếp tới route cũ, kết quả đúng là `404 Not Found`. Tài khoản trung
tâm sẽ chỉ được tạo qua invitation sau khi Platform Admin duyệt trung tâm.

Worker vẫn chạy để xử lý transactional outbox cho invitation và các email bảo
mật. File local được ghi vào
`src\Hosts\TutorManagement.Worker\.local\emails`. Không tạo outbox bằng cách
INSERT thủ công; test delivery bằng `FileEmailDeliveryClientTests` trong Test
Explorer cho tới khi API invitation được triển khai.

Để đổi sang SMTP thật và test end-to-end email verification, làm theo
`docs/SMTP-EMAIL-SETUP-AND-END-TO-END-TEST.md`. SMTP password phải ở Worker User
Secrets, không đặt trong `appsettings.json`.

## 9. Chạy test trong Test Explorer

1. Chọn **Test → Test Explorer**.
2. Build solution nếu danh sách chưa xuất hiện.
3. Chọn **Run All Tests**.
4. Kết quả hiện tại: 31 pass = 3 ArchitectureTests + 28 IntegrationTests.

Integration test đã kiểm tra validation, Owner/Teacher authorization, HttpOnly
cookie, refresh rotation, logout, logout-all, CORS, JSON/XML và 406. Không cần
tạo dữ liệu rác trong DB mỗi lần regression test.

## 10. Quy trình hằng ngày

1. Mở solution; kiểm tra PostgreSQL service đang Running.
2. Restore NuGet khi clone mới hoặc package thay đổi.
3. Build; sửa error trước khi chạy.
4. Nếu có migration mới, PMC chọn đúng Infrastructure + DbMigrator startup.
5. Chạy API + Worker bằng Multiple startup projects.
6. Kiểm tra `/health/ready`, sau đó test Swagger.
7. Trước khi kết thúc, **Run All Tests**.
8. Stop app trước khi Rebuild.

## 11. Khi thêm entity/migration mới

Ví dụ entity thuộc Academics:

1. Entity ở `Academics.Domain`.
2. `DbSet` và mapping ở `Academics.Infrastructure`.
3. Build solution.
4. Startup project = `TutorManagement.DbMigrator`.
5. PMC Default project = `Academics.Infrastructure`.
6. Tạo migration:

```powershell
Add-Migration AddClasses -Context AcademicsDbContext -StartupProject TutorManagement.DbMigrator
```

7. Mở migration mới, đọc `Up`/`Down`, kiểm tra schema, FK, unique index và delete
   behavior.
8. Chạy `Update-Database`, kiểm tra trong pgAdmin, chạy toàn bộ test.

## 12. Lỗi thường gặp

### Swagger không phản hồi

Chờ Output có `Now listening`, kiểm tra API là startup project, bấm Stop All để
dừng process cũ đang giữ cổng 5217 rồi chạy lại.

### DLL bị khóa khi build

Bấm Stop; nếu cần chọn **Debug → Terminate All**, sau đó Rebuild. Không xóa
`bin/obj` khi process còn chạy.

### Không tìm thấy DbContext

Startup project phải là DbMigrator; PMC Default project phải đúng
`*.Infrastructure`. Tên đúng là `IdentityDbContext`, `OrganizationDbContext`
(số ít) hoặc `AcademicsDbContext`.

### `/health/ready` lỗi

Kiểm tra PostgreSQL service, database, password User Secrets và quyền role
`tutor_app`. `/health/live` chỉ cho biết process sống; `/health/ready` mới kiểm
tra kết nối DB.

### Worker không tạo email local

Kiểm tra Worker đang chạy và refresh `.local/emails`. Trong pgAdmin, bảng
`iam.email_outbox_messages` cho biết message còn chờ hay đã xử lý. Public
Register không còn tạo email; outbox mới sẽ đến từ invitation hoặc luồng email
bảo mật được triển khai sau.
