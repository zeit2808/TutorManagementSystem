# Báo cáo bước 3: bỏ public Register và hoàn thiện nền Login

Ngày kiểm tra: 16/10/2026.

## 1. Kết quả

Public registration đã được loại bỏ hoàn toàn khỏi source code chạy thật:

- không còn action `POST /api/v1/auth/register`;
- không còn `RegisterRequest`, `RegistrationResponse`;
- không còn `RegisterRequestValidator`;
- không còn `RegisterOwnerCommand`, handler hoặc result;
- không còn DI registration của vertical slice Register;
- Swagger không công bố route Register;
- gọi trực tiếp route cũ trả `404 Not Found`.

Database không cần migration mới vì bước này chỉ xóa khả năng gọi public API.
Các bảng verification token và email outbox được giữ lại để dùng cho invitation,
forgot password và email bảo mật sau này.

Kết quả kiểm tra:

| Kiểm tra | Kết quả |
|---|---:|
| Release build | 0 warning, 0 error |
| Architecture tests | 3/3 pass |
| Integration tests | 22/22 pass |
| Tổng | 31/31 pass |

## 2. Quy tắc nghiệp vụ sau bước 3

Hệ thống không còn cho người ngoài tự tạo Owner. Hiện có hai nguồn account hợp lệ:

1. Account quản trị local do `TutorManagement.DbMigrator` seed.
2. Account trung tâm từ invitation do Platform Admin phát hành — sẽ được triển
   khai ở bước tiếp theo.

Cho đến khi invitation hoàn thành, không có API tạo account trung tâm mới. Đây là
trạng thái chủ ý nhằm đóng bề mặt tấn công trước khi mở onboarding có kiểm soát.

Các endpoint còn lại:

| Method | Route | Mục đích |
|---|---|---|
| `POST` | `/api/v1/auth/login` | Xác thực và đặt refresh cookie |
| `POST` | `/api/v1/auth/refresh-token` | Rotate refresh token |
| `POST` | `/api/v1/auth/logout` | Thu hồi session hiện tại |
| `POST` | `/api/v1/auth/logout-all` | Thu hồi mọi session của user |
| `GET` | `/api/v1/platform/centers` | Danh sách trung tâm, chỉ PlatformAdmin |
| `POST` | `/api/v1/auth/verify-email` | Khôi phục các account cũ chưa xác minh email |
| `POST` | `/api/v1/auth/resend-verification-email` | Gửi lại xác minh cho các account cũ chưa xác minh |

Invitation Owner hiện tạo account đã xác minh email. Hai route trên chỉ được giữ
để không khóa các account chưa xác minh còn sót từ luồng đăng ký cũ; chúng không
được dùng để xác minh `contactEmail` của trung tâm.

`Owner` hiện vẫn là role cũ của account seed. Việc tách `PlatformAdmin` và
`CenterOwner`, cùng tenant authorization theo `CenterId`, chưa thuộc bước 3.

## 3. Luồng Login hiện tại

### 3.1 Validate request

`LoginRequestValidator` kiểm tra username/password. Input sai trả thẳng response
400 theo `ApiResponse`; lỗi nhập liệu dự kiến không ném exception làm debugger
dừng.

### 3.2 Tìm user

Username được trim và normalize uppercase. Repository tìm bằng
`normalized_username` và load role trong cùng query.

Nếu username không tồn tại, hệ thống vẫn thực hiện dummy password verification.
Mục đích là giảm chênh lệch thời gian giữa “username không tồn tại” và “password
sai”, hạn chế username enumeration bằng timing.

### 3.3 Kiểm tra password và trạng thái

Password chỉ được so sánh với password hash; plaintext password không lưu DB.
User không tồn tại, password sai hoặc `IsActive=false` đều trả cùng lỗi
`invalid_credentials`.

User chưa xác minh email nhận `email_not_verified` và không được cấp token.

### 3.4 Phát hành token

Khi hợp lệ:

1. role của user được đưa vào access token;
2. JWT access token có thời gian sống ngắn;
3. refresh token ngẫu nhiên được tạo;
4. DB chỉ lưu hash của refresh token;
5. plaintext refresh token chỉ đặt trong cookie `tm.refresh`;
6. response JSON chỉ trả access token, không trả refresh token.

### 3.5 Cookie security

Cookie có:

- `HttpOnly`: JavaScript không đọc được token;
- `SameSite=Lax`: giảm CSRF ở request cross-site phổ biến;
- path `/api/v1/auth`: browser không gửi cookie tới route không cần thiết;
- expiration rõ ràng;
- Development HTTP dùng `Secure=false`;
- production HTTPS bắt buộc `Secure=true`.

React phải dùng `credentials: "include"`; Axios dùng `withCredentials: true`.
CORS chỉ cho origin cấu hình và bật credentials, không dùng wildcard origin.

### 3.6 Refresh rotation và reuse detection

Mỗi lần refresh:

1. đọc token từ HttpOnly cookie;
2. hash token để tìm DB;
3. revoke token cũ;
4. tạo token mới cùng family;
5. đặt cookie mới;
6. chỉ trả access token mới trong JSON.

Nếu token đã rotate bị dùng lại, toàn token family bị revoke. Đây là cơ chế phát
hiện khả năng refresh token đã bị sao chép.

### 3.7 Logout

- `logout`: revoke refresh token hiện tại và xóa cookie; gọi nhiều lần vẫn 204.
- `logout-all`: cần bearer access token, revoke mọi refresh token của user và xóa
  cookie hiện tại.

Access token đã phát hành không bị xóa khỏi browser khác ngay lập tức; nó hết hạn
theo lifetime JWT. Vì vậy access token phải ngắn hạn.

### 3.8 Vòng đời email verification token

- Token hết hạn sau 24 giờ và chỉ dùng một lần.
- Database chỉ lưu SHA-256 hash của token.
- Resend có cooldown một phút để hạn chế spam.
- Khi phát hành token mới, mọi token chưa dùng/chưa revoke trước đó được revoke
  với reason `superseded` trong cùng transaction.
- Khi verify thành công, token hiện tại được đánh dấu used và mọi token còn lại
  được revoke với reason `email_verified`.
- Partial unique index `ux_user_security_tokens_unconsumed` bảo đảm ở cấp
  PostgreSQL mỗi user/purpose chỉ có tối đa một token chưa dùng/chưa revoke,
  kể cả khi có hai request resend đồng thời.

## 4. Worker email sau refactor

### 4.1 Phân tách SOLID

Worker không còn tự vừa đọc DB, vừa dựng file email, vừa ghi file.

```text
Worker
  → EmailOutboxProcessor
      → IEmailDeliveryClient
          → FileEmailDeliveryClient (local)
          → SmtpEmailDeliveryClient (gửi thật)
```

- **SRP:** Worker điều khiển vòng lặp; processor điều phối outbox; delivery client
  chịu trách nhiệm giao email.
- **OCP:** SMTP được thêm bằng implementation mới của `IEmailDeliveryClient`,
  không sửa processor. Provider tiếp theo cũng theo cùng extension point.
- **LSP:** delivery client mới thay được file client nếu tuân cùng contract.
- **ISP:** interface chỉ có một nhiệm vụ `DeliverAsync`.
- **DIP:** processor phụ thuộc abstraction, không phụ thuộc file system cụ thể.

Email delivery chỉ được đăng ký trong Worker qua
`AddIdentityAccessEmailDelivery`; API và DbMigrator không kéo processor email vào
DI nếu không dùng.

### 4.2 Cách xử lý batch

Cấu hình mặc định:

```json
{
  "EmailDelivery": {
    "Provider": "File",
    "PickupDirectory": ".local/emails",
    "BatchSize": 50,
    "MaximumConcurrency": 4,
    "MaximumAttempts": 5,
    "PollingIntervalMilliseconds": 2000
  }
}
```

Luồng xử lý:

1. Query các message chưa xử lý và chưa vượt số lần retry.
2. Order theo `CreatedAtUtc`, lấy tối đa `BatchSize`.
3. Giao tối đa `MaximumConcurrency` email đồng thời.
4. Sau khi delivery hoàn tất, cập nhật domain state tuần tự.
5. Save DB một lần cho cả batch.
6. Nếu batch đầy và không lỗi, Worker chạy batch tiếp ngay, không chờ hai giây.
7. Nếu hàng đợi rỗng, batch chưa đầy hoặc có lỗi, Worker chờ polling interval để
   tránh busy loop.

Khi thành công, outbox body được xóa. Khi thất bại, attempt tăng và error được
giới hạn 1.000 ký tự.

SMTP thật dùng MailKit, xác thực password/App Password, STARTTLS hoặc
SSL-on-connect. Client giữ một pool kết nối có giới hạn để không lặp lại TLS
handshake và authentication cho từng email. Cấu hình bí mật chỉ nằm trong
Worker User Secrets. Xem hướng dẫn hoàn chỉnh tại
`docs/SMTP-EMAIL-SETUP-AND-END-TO-END-TEST.md`.

### 4.3 Giới hạn hiện tại

Worker hiện phù hợp local và một Worker instance. Trước production cần thêm:

- row lease hoặc PostgreSQL `SKIP LOCKED` nếu chạy nhiều Worker;
- exponential retry/backoff và dead-letter;
- provider message ID và webhook bounce/complaint;
- cleanup/retention outbox.

SMTP đã gửi email thật. Production quy mô lớn vẫn nên cân nhắc API của một dịch
vụ transactional email để có webhook bounce/complaint và observability tốt hơn.

Không tăng `MaximumConcurrency` tùy ý; giới hạn thực tế phải theo quota của email
provider và kết quả load test.

## 5. Tự test bằng Visual Studio

### 5.1 Dừng bản code cũ

1. Nhấn `Shift + F5` hoặc nút Stop.
2. Đảm bảo cả API và Worker đã dừng.
3. Việc này tránh DLL bị khóa và chắc chắn Swagger dùng assembly mới.

### 5.2 Build

1. Chọn configuration `Release`.
2. Chọn **Build → Rebuild Solution**.
3. Mở **View → Error List**.
4. Kết quả mong đợi: 0 warning, 0 error.

Không cần chạy `Update-Database`: bước 3 không có migration.

### 5.3 Automated tests

1. Chọn **Test → Test Explorer**.
2. Nhấn **Run All Tests**.
3. Mong đợi 31/31 pass.
4. Tìm và chạy riêng hai test quan trọng:
   - `Public_registration_endpoint_is_not_exposed`;
   - `Delivery_writes_the_expected_local_email_file`.

Test đầu xác nhận cả HTTP 404 và route không có trong Swagger. Test thứ hai ghi
email vào thư mục tạm, đọc nội dung rồi tự dọn file; không chạm DB local và không
cần INSERT dữ liệu thủ công.

### 5.4 Chạy host

Trong **Configure Startup Projects**:

| Project | Action |
|---|---|
| `TutorManagement.Api` | Start |
| `TutorManagement.Worker` | Start |
| `TutorManagement.DbMigrator` | None |

Nhấn `F5`, chờ:

```text
Now listening on: http://localhost:5217
Tutor Management worker started.
```

### 5.5 Kiểm tra Swagger

Mở `http://localhost:5217/swagger/index.html` và xác nhận:

- không có `POST /api/v1/auth/register`;
- có login, refresh-token, logout, logout-all và kiểm tra quyền qua API nghiệp vụ;
- có verify-email và resend-verification-email cho hạ tầng invitation tiếp theo.

Nếu Swagger vẫn hiện Register, browser hoặc process đang dùng build cũ:

1. Stop toàn bộ host.
2. Rebuild Solution.
3. Chạy lại.
4. Refresh browser bằng `Ctrl + F5`.

### 5.6 Test Login

1. Nhấp phải `TutorManagement.DbMigrator` → **Manage User Secrets**.
2. Xem `Seed:OwnerUsername` và `Seed:OwnerPassword`; không chụp/chia sẻ secret.
3. Swagger → `POST /api/v1/auth/login` → **Try it out**.
4. Nhập credential rồi Execute.

Mong đợi:

- status 200;
- `data.accessToken` có giá trị;
- response không có `refreshToken`;
- response header có `Set-Cookie: tm.refresh=...; HttpOnly; SameSite=Lax`.

Test payload rỗng phải trả 400 `validation_error`, không làm Visual Studio dừng
ở exception.

### 5.7 Test authorization

1. Copy riêng access token.
2. Nhấn **Authorize**.
3. Dán token, không thêm `Bearer`.
4. Gọi `GET /api/v1/platform/centers`.
5. Mong đợi 200 với account seed có role Owner.

Không token phải là 401. Token hợp lệ nhưng sai role phải là 403.

### 5.8 Test Refresh

1. Không logout sau Login.
2. Gọi `POST /api/v1/auth/refresh-token`, không nhập body.
3. Mong đợi 200 và access token mới.
4. JSON không chứa refresh token.
5. Browser tự gửi cookie cũ và nhận cookie đã rotate.

### 5.9 Test Logout

1. Gọi `POST /api/v1/auth/logout`.
2. Mong đợi 204.
3. Gọi refresh lại; mong đợi 401.

Test Logout All:

1. Login lại và Authorize token mới.
2. Gọi `POST /api/v1/auth/logout-all`.
3. Mong đợi 204.
4. Refresh bằng session cũ phải là 401.

## 6. Security checklist bước 3

- [x] Không còn public account creation.
- [x] Route cũ trả 404 và không nằm trong OpenAPI.
- [x] Validation input trả response chuẩn, không dùng exception cho lỗi dự kiến.
- [x] Password chỉ lưu hash.
- [x] Dummy verification giảm timing enumeration.
- [x] Account inactive hoặc email chưa verify không được token.
- [x] Refresh token không nằm trong JSON/local storage.
- [x] HttpOnly cookie có path giới hạn và SameSite.
- [x] Refresh rotation và reuse detection.
- [x] Logout và Logout All revoke DB token.
- [x] CORS credentials chỉ cho configured origin.
- [x] Worker delivery phụ thuộc abstraction và concurrency có giới hạn.
- [x] Regression/architecture tests pass.
- [ ] Đổi role Owner thành PlatformAdmin/CenterOwner.
- [ ] Invitation token và center membership.
- [ ] Tenant authorization theo CenterId.
- [ ] Rate limiting/lockout/audit log.
- [ ] HTTPS Secure cookie ở production.
- [ ] Multi-instance outbox leasing và email provider thật.

## 7. Bước tiếp theo

Bước 3 đã đóng public Register nhưng chưa tạo onboarding mới. Bước tiếp theo nên
là tạo `PlatformAdmin`, `Center`, `Invitation` và `AcceptInvitation`. Chỉ sau khi
invitation hợp lệ mới tạo `CenterOwner` và gắn user với đúng center.
