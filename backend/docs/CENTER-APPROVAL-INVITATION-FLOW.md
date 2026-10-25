# Luồng duyệt trung tâm và mời chủ trung tâm

## 1. Kết quả đã triển khai

Luồng hiện tại không có `Register` công khai. Chỉ `PlatformAdmin` mới có thể:

1. Tạo hồ sơ trung tâm ở trạng thái `PendingApproval`.
2. Duyệt trung tâm thành `Approved`.
3. Mời một email chưa có tài khoản làm `CenterOwner`.
4. Worker lấy thư trong outbox và gửi qua SMTP.
5. Người nhận dùng invitation token để tạo tài khoản.
6. Hệ thống xác minh email ngay tại thời điểm accept invitation, tạo membership và tiêu thụ invitation trong cùng một transaction.
7. Chủ trung tâm đăng nhập bằng API login hiện có; refresh token tiếp tục nằm trong HttpOnly cookie.

Invitation có hạn **48 giờ**. Database chỉ lưu SHA-256 hash của token, không lưu token gốc.

## 2. Ranh giới module và database

- `organization.centers`: hồ sơ pháp lý và trạng thái duyệt của trung tâm.
- `iam.center_invitations`: lời mời, hash token, hạn dùng và trạng thái đã dùng/thu hồi.
- `iam.center_memberships`: quan hệ user - center và vai trò theo từng trung tâm.
- `iam.users`, `iam.refresh_tokens`, `iam.email_outbox_messages`: tài khoản, session và email outbox hiện có.

Hai foreign key từ schema `iam` sang `organization.centers` đã được tạo trong PostgreSQL. Vì vậy DB không cho phép invitation hoặc membership trỏ tới center không tồn tại. Việc tách schema là phân vùng module trong một modular monolith, không phải microservice và không làm mất ràng buộc DB.

Vai trò toàn hệ thống cũ `Owner` được đổi tên thành `PlatformAdmin` nhưng giữ nguyên ID. `CenterOwner` nằm trong membership, không phải global role; do đó chủ của center A không có quyền quản trị center B.

## 3. Cách chạy trong Visual Studio

### 3.1. Dừng phiên cũ

Nếu Build báo DLL đang được process sử dụng, bấm **Shift + F5** hoặc nút **Stop Debugging**. Không cần xóa `bin` hay `obj`.

### 3.2. Kiểm tra migration bằng Package Manager Console

Migration đã được tạo và áp dụng vào DB local. Để tự kiểm tra bằng IDE:

1. Mở **Tools → NuGet Package Manager → Package Manager Console**.
2. Trong Solution Explorer, nhấp phải `TutorManagement.DbMigrator` → **Set as Startup Project**.
3. Chọn **Default project** là `TutorManagement.Modules.Organizations.Infrastructure`.
4. Chạy `Get-Migration -Context OrganizationDbContext`. Migration cuối phải là `AddCenterApprovalWorkflow` và đã applied.
5. Đổi **Default project** sang `TutorManagement.Modules.IdentityAccess.Infrastructure`.
6. Chạy `Get-Migration -Context IdentityDbContext`. Migration cuối phải là `AddCenterInvitationsAndMemberships` và đã applied.

Nếu setup một database local mới, chạy `TutorManagement.DbMigrator` một lần. Migrator cố ý áp dụng theo thứ tự Organizations → IdentityAccess → Academics để foreign key xuyên schema luôn hợp lệ.

### 3.3. Chạy API và Worker

1. Nhấp phải Solution → **Configure Startup Projects**.
2. Chọn **Multiple startup projects**.
3. Đặt `TutorManagement.Api` = **Start**.
4. Đặt `TutorManagement.Worker` = **Start**.
5. Đặt `TutorManagement.DbMigrator` = **None** sau khi migration đã hoàn tất.
6. Bấm **F5**.

API hiển thị Swagger theo port trong `launchSettings.json`, thường là `http://localhost:5217/swagger/index.html`. Worker phải chạy để email trong outbox được gửi qua SMTP; API không chờ SMTP nên request tạo invitation vẫn nhanh.

## 4. Test trọn luồng trên Swagger

### Bước 1 — Login PlatformAdmin

Gọi `POST /api/v1/auth/login`:

```json
{
  "username": "admin",
  "password": "mật khẩu Seed:OwnerPassword trong User Secrets"
}
```

Copy `data.accessToken`, bấm **Authorize** ở đầu Swagger và dán token. Không dán chữ `Bearer` vì Swagger tự thêm.

Kết quả đúng:

- JSON có access token nhưng không có refresh token.
- Response có cookie `tm.refresh` với cờ HttpOnly.
- JWT mới có role `PlatformAdmin`.

### Bước 2 — Tạo hồ sơ trung tâm

Gọi `POST /api/v1/platform/centers`:

```json
{
  "legalName": "Công ty TNHH Giáo dục Ánh Sao",
  "displayName": "Trung tâm Ánh Sao",
  "taxCode": "0312345678",
  "businessRegistrationNumber": "0312345678",
  "address": "123 Nguyễn Trãi, Quận 1, TP.HCM",
  "contactEmail": "contact@example.com",
  "contactPhone": "0912345678"
}
```

Kết quả đúng: HTTP 201, `status = PendingApproval`. Copy `data.id`.

### Bước 3 — Duyệt trung tâm

Gọi `POST /api/v1/platform/centers/{centerId}/approve` với ID ở bước 2.

Kết quả đúng: HTTP 200, `status = Approved`. Gọi lại trên center đã approved là idempotent và vẫn thành công.

### Bước 4 — Mời chủ trung tâm

Gọi `POST /api/v1/platform/centers/{centerId}/invitations`:

```json
{
  "email": "owner.real.email@example.com"
}
```

Dùng một email thật chưa tồn tại trong `iam.users`. Kết quả đúng: HTTP 201, trả invitation ID, role `CenterOwner` và thời gian hết hạn; không trả invitation token trong JSON.

Kiểm tra trong pgAdmin bằng giao diện **View/Edit Data**:

- `iam.center_invitations`: có record mới, chỉ có `token_hash`.
- `iam.email_outbox_messages`: có thư chờ gửi rồi được Worker đánh dấu `processed_at_utc`.
- Hộp thư người nhận: có token gốc.

Không thể mời cho center chưa approved. Không thể tạo invitation active thứ hai cho cùng center và email. Không thể mời email đã là user.

Nếu API trả `401` với mã `invalid_inviter_session`, JWT trong Swagger vẫn còn hạn nhưng tài khoản PlatformAdmin tương ứng đã bị xóa, vô hiệu hóa hoặc mất role trong DB (thường gặp sau khi tạo lại database). Bấm **Authorize → Logout** trong Swagger, gọi `POST /api/v1/auth/login` bằng tài khoản admin hiện có rồi Authorize bằng `data.accessToken` mới. Swagger đang bật Persist Authorization nên đóng/mở tab không chắc đã xóa token cũ. Không sửa hoặc tắt foreign key `FK_center_invitations_users_invited_by_user_id` để vượt lỗi này.

### Bước 5 — Accept invitation

Gọi `POST /api/v1/auth/invitations/accept` (không cần Authorize):

```json
{
  "token": "token nhận từ email",
  "username": "center.owner",
  "password": "StrongPassword!123",
  "phoneNumber": "0912345678"
}
```

Kết quả đúng: HTTP 200. Trong DB:

- `iam.users`: có user mới và `email_verified_at_utc` có giá trị.
- `iam.center_memberships`: có membership `CenterOwner` cho đúng `center_id`.
- `iam.center_invitations`: có `accepted_at_utc` và `accepted_by_user_id`.

Gọi accept lần hai với cùng token phải nhận HTTP 422 `invalid_center_invitation`. Token quá 48 giờ, bị thu hồi hoặc center bị suspend cũng không được accept.

### Bước 6 — Login tài khoản CenterOwner

Gọi lại `POST /api/v1/auth/login` bằng username/password vừa tạo. Kết quả đúng:

- HTTP 200.
- Access token ở JSON.
- Refresh token chỉ ở HttpOnly cookie.
- Refresh/logout/logout-all tiếp tục dùng luồng hiện có.

Tài khoản `CenterOwner` gọi các route `/platform/...` phải nhận HTTP 403. Đây là hành vi đúng: platform permission và center membership là hai phạm vi quyền khác nhau.

## 5. Kiểm tra PostgreSQL bằng pgAdmin

Trong cây **Databases → tutor_management → Schemas**:

- `organization → Tables → centers`
- `iam → Tables → center_invitations`
- `iam → Tables → center_memberships`
- `iam → Tables → email_outbox_messages`
- `iam → Tables → users`

Nhấp phải bảng → **View/Edit Data → All Rows**. Ở phần Constraints của hai bảng invitation/membership sẽ thấy foreign key sang `organization.centers` và `iam.users`.

## 6. Mã phản hồi quan trọng

- `400`: body sai format hoặc không đạt validation.
- `401`: thiếu/sai access token.
- `403`: đã login nhưng không có role `PlatformAdmin`.
- `409`: username/email/phone đã tồn tại hoặc invitation active bị trùng.
- `422`: center chưa approved hoặc invitation không còn hợp lệ.

Các response vẫn theo envelope `success`, `message`, `data`, `errors`, `traceId` và hỗ trợ JSON/XML. Endpoint GET centers đã được smoke-test với `Accept: application/xml` và trả HTTP 200, không còn lỗi 406.
