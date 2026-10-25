# Đổi mật khẩu, quên mật khẩu và kiểm thử phiên đăng nhập

Tài liệu này dành cho Visual Studio + PostgreSQL local. API dùng JWT access token và refresh token trong cookie HttpOnly. Email được ghi vào outbox trong cùng giao dịch với dữ liệu tài khoản; Worker giao email sau đó. Không cần chạy SQL thủ công.

## 1. Nghiệp vụ đã triển khai

| Hành động | Điều kiện | Sau khi thành công |
|---|---|---|
| `POST /api/v1/auth/password/change` | JWT, refresh cookie cùng phiên và mật khẩu hiện tại | Đổi hash; xoay refresh cookie; trả access JWT mới; **giữ phiên hiện tại**, thu hồi tất cả phiên thiết bị khác; email thông báo. |
| `POST /api/v1/auth/password/forgot` | Email đúng cú pháp | Luôn trả `202` giống nhau cho email tồn tại/không tồn tại. Chỉ tài khoản active + đã xác minh email nhận OTP. |
| `POST /api/v1/auth/password/reset` | Email + OTP còn hạn + mật khẩu mới hợp lệ | Dùng OTP đúng một lần, đổi hash, thu hồi **mọi phiên**; không tự đăng nhập; email thông báo. |

OTP gồm 8 chữ số, sống 10 phút. Mã cũ bị vô hiệu khi phát hành mã mới. Tài khoản được phát tối đa 5 mã trong cửa sổ 24 giờ trượt, mỗi lần cách nhau ít nhất 1 phút. Mỗi mã chịu tối đa 5 lần thử sai. Giới hạn theo IP bổ sung: forgot 10 lần/giờ, reset 20 lần/10 phút, change 10 lần/15 phút theo tài khoản. OTP trong bảng token là HMAC-SHA256 gắn với user và token ID; khóa HMAC được tách theo mục đích từ JWT signing key trong User Secrets. Bản rõ chỉ nằm tạm trong outbox cho đến khi Worker gửi hoặc bỏ email quá hạn.

Mật khẩu mới theo cùng một policy với invitation: 12–128 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt, tránh một số mẫu quá phổ biến; không được trùng mật khẩu hiện tại. `confirmPassword` phải khớp. Validation chạy cả ở API lẫn use-case để không bị bỏ qua khi gọi handler từ kênh khác. Không đưa access/refresh token trong URL hoặc email.

## 2. Chuẩn bị trong Visual Studio

1. Mở `backend/TutorManagementSystem.sln` bằng Visual Studio. Bấm **Stop Debugging** nếu API đang chạy để tránh lỗi DLL bị khóa khi Build/Update-Database.
2. Trong **Services** của Windows, kiểm tra PostgreSQL local đang `Running` (ví dụ `postgresql-x64-18`). Phiên kiểm tra của Codex không kết nối được `127.0.0.1:5432`; nếu bạn cũng không kết nối được, mở service trước.
3. Trong **Solution Explorer**, chuột phải các project `TutorManagement.Api`, `TutorManagement.Worker`, `TutorManagement.DbMigrator` → **Manage User Secrets**. Xác nhận mỗi host có `ConnectionStrings:MainDatabase` trỏ đến cùng database local và có mật khẩu. Chỉ API cần `Jwt:SigningKey`; không chụp/chia sẻ giá trị bí mật. Worker giữ `EmailDelivery:Provider=File` để test local hoặc làm theo [hướng dẫn SMTP](SMTP-EMAIL-SETUP-AND-END-TO-END-TEST.md).
4. **Build → Build Solution**. Nếu Visual Studio vẫn báo DLL bị khóa, đóng phiên API đang debug rồi Build lại. Không xóa `bin/obj` khi tiến trình đang chạy.

### Áp migration bằng Package Manager Console

Mở **Tools → NuGet Package Manager → Package Manager Console**:

1. Dropdown **Default project**: chọn `TutorManagement.Modules.IdentityAccess.Infrastructure`. Đây là project chứa `IdentityDbContext` và các file migration trong `Persistence/Migrations`.
2. Chuột phải `TutorManagement.DbMigrator` → **Set as Startup Project**. Đây là host cung cấp cấu hình/connection string khi EF chạy ở design time.
3. Trong PMC chạy đúng một lệnh:

```powershell
Update-Database -Context IdentityDbContext -StartupProject TutorManagement.DbMigrator
```

Lệnh áp toàn bộ migration **còn thiếu** của schema `iam`, gồm `AddPasswordRecoverySessions` và `ConstrainPasswordResetOutbox`; nó không tự sửa hai schema `organization`, `academics`. Không cần `Add-Migration` nữa vì hai migration đã nằm trong source. Nếu PMC báo không kết nối được DB, kiểm tra service PostgreSQL và User Secrets của DbMigrator trước khi thử lại. Không tiếp tục test API mật khẩu khi migration chưa thành công.

Trong pgAdmin, chọn database `tutor_management` → Schemas → `iam` → Tables. Bạn sẽ thấy `auth_sessions`; `user_security_tokens` có `failed_attempt_count`, `version`; `email_outbox_messages` có `security_token_id` và FK. Có thể xem migration history bằng **View/Edit Data** trên `iam.__ef_migrations_history` để thấy hai migration mới, không cần thực thi SQL thủ công.

## 3. Chạy API và Worker trong IDE

Với cấu hình local, chuột phải `TutorManagement.Api` → **Set as Startup Project** → chọn profile `http` và Start. Mở [Swagger local](http://localhost:5217/swagger/index.html). Chạy `TutorManagement.Worker` ở một phiên Visual Studio thứ hai, hoặc đặt cả API và Worker trong **Solution Properties → Startup Project → Multiple startup projects** với Action = `Start`.

Nếu Worker dùng `File`, kiểm tra `EmailDelivery:PickupDirectory` trong `TutorManagement.Worker/appsettings.json` (mặc định `.local/emails`). Đây là đường dẫn tương đối với working directory của tiến trình Worker. File email chứa OTP/token nhạy cảm: chỉ dùng local, không commit/chia sẻ và dọn theo chính sách môi trường của bạn.

Migration chuyển các refresh-token family cũ thành phiên. **Access JWT phát hành trước bản cập nhật không có claim `sid` nên sẽ nhận 401**; bấm `POST /api/v1/auth/refresh-token` để lấy JWT mới từ cookie cũ, hoặc đăng nhập lại. Đây là chuyển đổi một lần.

## 4. Test trong Swagger theo thứ tự

Swagger cùng origin với API nên browser tự giữ cookie `tm.refresh`. Ở môi trường HTTP local, `Secure=false` theo `appsettings.Development.json`; môi trường HTTPS/production phải để `Secure=true`. Không nhập refresh token vào JSON hay nút Authorize.

### A. Đăng nhập và đổi mật khẩu

1. Gọi `POST /api/v1/auth/login` bằng tài khoản đã xác minh email. Xem `200`, copy `data.accessToken`, xác nhận JSON **không** có `refreshToken`, response header có `Set-Cookie: tm.refresh=...; HttpOnly`.
2. Bấm **Authorize** ở Swagger và dán **chỉ access token**. Mở `POST /api/v1/auth/password/change`:

```json
{
  "currentPassword": "MAT_KHAU_HIEN_TAI",
  "newPassword": "NewStrongPass!456",
  "confirmPassword": "NewStrongPass!456"
}
```

Kỳ vọng `200`; JSON trả access token mới nhưng không trả refresh token, cookie được xoay. Dán access token mới vào Authorize. Gọi một GET có `[Authorize]` như `GET /api/v1/me/centers`: phiên hiện tại vẫn dùng được. Worker gửi email thông báo; email này không chứa mật khẩu.

3. Test lỗi: sai `currentPassword` → `400` + `invalid_current_password`; mật khẩu mới trùng cũ → `400` + `password_unchanged`; thiếu/khác phiên refresh cookie → `401` + `invalid_refresh_token`; payload sai quy tắc → `400` + `validation_error`. Mọi body lỗi theo `ApiResponse` có `success:false`, `errors[]`, `traceId`.
4. Để kiểm tra thiết bị khác: đăng nhập cùng tài khoản ở **browser profile/Incognito khác** trước bước đổi mật khẩu. Sau khi đổi ở browser A, dùng access JWT cũ ở browser B gọi GET có authorization → `401` ngay, không phải chờ 15 phút; refresh ở B cũng → `401`. Browser A tiếp tục bằng JWT mới và cookie mới. Hai tab cùng browser profile thường dùng chung cookie, không phải hai thiết bị độc lập.

### B. Quên mật khẩu và reset OTP

1. Gọi `POST /api/v1/auth/password/forgot`:

```json
{ "email": "email-da-xac-minh@example.com" }
```

Kỳ vọng `202` với thông điệp chung. Một email không tồn tại nhưng đúng cú pháp cũng nhận cùng response; chỉ tài khoản hợp lệ mới có email trong outbox. Worker phải đang chạy để có file/email. Không dùng `contactEmail` của trung tâm trừ khi chính nó cũng là email tài khoản đã xác minh.

2. Mở email mới nhất, lấy **8 chữ số** rồi gọi `POST /api/v1/auth/password/reset`:

```json
{
  "email": "email-da-xac-minh@example.com",
  "otp": "12345678",
  "newPassword": "AnotherStrong!789",
  "confirmPassword": "AnotherStrong!789"
}
```

Thay OTP ví dụ bằng mã thực. Kỳ vọng `204`, không có access/refresh token trong body. Cả JWT/cookie cũ của mọi thiết bị đều không còn dùng được; đăng nhập lại bằng mật khẩu mới nhận phiên mới.

3. Test biên: mã sai → `400` + `invalid_password_reset_otp`; sai 5 lần thì ngay cả mã đúng cũng bị từ chối; mã hết 10 phút hoặc dùng lại → cùng lỗi; gửi mã mới sau 1 phút khiến mã trước không còn hiệu lực. Khi đã phát đủ 5 mã trong 24 giờ trượt, endpoint forgot vẫn `202` nhưng không thêm email. Gọi dồn dập từ một IP vượt limiter → `429` + `rate_limit_exceeded`.

## 5. Giải thích trạng thái dữ liệu và bảo mật

- `iam.auth_sessions.id` bằng `refresh_tokens.family_id`. JWT có `sid`; mỗi request `[Authorize]` tra phiên còn active và user chưa bị khóa. Logout/logout-all/reuse detection/change/reset thu hồi phiên, vì vậy JWT cũ bị 401 ngay dù chữ ký và `exp` còn hợp lệ.
- Change cần **cả** JWT, cookie cùng `sid`, mật khẩu cũ; phiên hiện tại được xoay cookie và cấp JWT mới, các phiên còn lại bị revoke trong cùng `SaveChanges` với hash mới.
- Forgot không trả lời email có tồn tại hay không. Giới hạn theo account nằm trong DB, theo IP nằm ở API. Việc thu hồi OTP cũ và phát OTP mới được đặt trong transaction `Serializable`; unique index bảo đảm một token chưa dùng cho mỗi user/purpose.
- Reset tăng bộ đếm khi nhập sai; token có concurrency version nên hai request không thể tiêu thụ cùng một OTP thành công. Đổi hash, đánh dấu OTP đã dùng, thu hồi tất cả phiên và tạo email thông báo trong cùng giao dịch DB.
- Worker bỏ email OTP nếu token tương ứng đã bị thay thế, dùng hoặc hết hạn. Email outbox giữ mã ở dạng bản rõ **cho đến khi xử lý**, nên quyền đọc DB/outbox và thư mục pickup phải được hạn chế. Worker hiện nên chạy **một instance**; cơ chế claim/lease cho nhiều replica là một hạng mục triển khai riêng trước khi scale ngang email delivery.

## 6. Giới hạn kiểm chứng của lần thay đổi này

Code đã build Release và chạy test tự động, nhưng phiên Codex không kết nối được PostgreSQL local ở `127.0.0.1:5432`, nên **chưa có xác nhận end-to-end trên database của bạn** và chưa áp migration. Sau khi bạn làm mục 2–4, nếu gặp lỗi hãy gửi `status`, `errors` và đoạn log không chứa mật khẩu, OTP, JWT, cookie hay connection string để đối chiếu.
