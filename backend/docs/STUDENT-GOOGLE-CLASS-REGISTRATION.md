# Luồng Student: Google login, đăng ký nhiều lớp và trung tâm duyệt từng lớp

## 1. Nghiệp vụ và dữ liệu

- `iam.users` là tài khoản chung của hệ thống. Vai trò `Student` là role mới của cùng một `User`, không phải bảng đăng nhập độc lập. Người đã có tài khoản mật khẩu có thể liên kết Google sau khi xác thực lại mật khẩu.
- `iam.external_logins` lưu `issuer + sub` của Google. Backend xác minh chữ ký, issuer, audience, hạn token bằng `Google.Apis.Auth`; **không dùng email hoặc họ tên làm khóa nhận diện**. Một email đã thuộc tài khoản cũ không tự động được gộp: đăng nhập tài khoản cũ rồi gọi API liên kết.
- `academics.students` là hồ sơ học sinh **theo trung tâm**. `user_id` liên kết tới `iam.users` và unique theo `(center_id, user_id)`. Hồ sơ Student online chỉ được tạo khi đơn lớp đầu tiên được duyệt. Hai tài khoản khác nhau dù cùng họ tên vẫn là hai học sinh khác nhau. Một tài khoản đăng ký nhiều lớp ở cùng trung tâm chỉ có một hồ sơ Student.
- Mỗi lớp được chọn tạo **một** `class_applications` với `status = Pending`; các lựa chọn cùng một lần gửi có chung `submission_id`. Trung tâm duyệt hoặc từ chối **từng lớp**; lưu người duyệt, thời điểm và lý do từ chối. `Approved` là quyết định lịch sử; danh sách lớp đang học lấy từ enrollment còn hiệu lực.
- Lớp mặc định **đóng đăng ký**. Quản lý trung tâm phải mở rõ ràng. Việc đóng sau khi nhận đơn không xóa đơn đang chờ; vẫn có thể duyệt nếu lớp còn hoạt động và còn chỗ. Lớp đã archive hoặc hết chỗ không thể duyệt; đơn vẫn Pending để quản lý xử lý.
- Student đã liên kết tài khoản không thể được thêm bằng API enrollment trực tiếp của nhân viên; phải qua đơn đăng ký/duyệt. API enrollment trực tiếp vẫn dùng được cho hồ sơ offline chưa có `user_id`.

Không tự động gộp một hồ sơ offline với tài khoản online chỉ vì trùng tên/số điện thoại. Nếu trung tâm đã nhập học sinh offline trước đây, cần đối chiếu giấy tờ hoặc xác nhận của người học rồi mới làm một use case liên kết/di chuyển dữ liệu riêng. Đây là giới hạn có chủ đích để tránh gộp nhầm hai người.

## 2. Thiết lập trong Visual Studio, PostgreSQL local

1. Dừng phiên Debug của `TutorManagement.Api` trước khi Build hoặc Update-Database, tránh DLL bị khóa.
2. Mở solution `backend/TutorManagementSystem.sln`. Đặt `TutorManagement.DbMigrator` làm **Startup Project** khi cập nhật DB.
3. Mở **Tools → NuGet Package Manager → Package Manager Console**. Trong ô **Default project**, chọn `TutorManagement.Modules.IdentityAccess.Infrastructure`, chạy:

   ```powershell
   Update-Database -Context IdentityDbContext -StartupProject TutorManagement.DbMigrator
   ```

4. Đổi **Default project** thành `TutorManagement.Modules.Academics.Infrastructure`, chạy:

   ```powershell
   Update-Database -Context AcademicsDbContext -StartupProject TutorManagement.DbMigrator
   ```

   Default project là nơi EF tìm các file migration. Startup project cung cấp cấu hình và DI để EF kết nối PostgreSQL. Thứ tự quan trọng: schema IAM có `users` trước, migration Academics mới tạo FK tới `iam.users`.

5. Trong pgAdmin, kiểm tra `iam.external_logins`, role `Student` trong `iam.roles`, `academics.class_applications`, cột `academics.students.user_id`, `academics.classes.registration_open` và hai bảng `__ef_migrations_history`. Hai migration này **đã được áp dụng trên PostgreSQL local hiện tại**; chạy lại Update-Database sẽ không áp dụng lần hai.
6. Đổi Startup Project về `TutorManagement.Api`, chọn launch profile `http`, chạy F5. Swagger: [http://localhost:5217/swagger/index.html](http://localhost:5217/swagger/index.html).

### Google Cloud và User Secrets

Tạo OAuth Client ID loại **Web application** trong Google Cloud Console. Thêm `http://localhost:5217` vào **Authorized JavaScript origins**; nếu app OAuth đang ở chế độ Testing, thêm tài khoản Google thử nghiệm vào danh sách test users. Quy trình Google Identity Services được mô tả trong [hướng dẫn Google](https://developers.google.com/identity/gsi/web/guides/get-google-api-clientid) và [xác minh ID token ở backend](https://developers.google.com/identity/gsi/web/guides/verify-google-id-token).

Trong Visual Studio: nhấp phải project `TutorManagement.Api` → **Manage User Secrets** → thêm khóa bên dưới vào JSON hiện có (đừng xóa connection string/JWT secret):

```json
"GoogleAuth:ClientId": "YOUR_WEB_CLIENT_ID.apps.googleusercontent.com"
```

Đây là **Web Client ID**, không phải Client Secret. API sẽ báo 503 ở trang thử nếu chưa cấu hình. Chạy lại API sau khi chỉnh User Secrets.

## 3. Test tay một luồng trọn vẹn bằng Swagger

### Chuẩn bị từ phía trung tâm

1. Dùng tài khoản Owner/Manager của **trung tâm đã được duyệt** đăng nhập bằng `POST /api/v1/auth/login`. Copy `data.accessToken`, nhấn **Authorize** trong Swagger và dán access token (không thêm `Bearer`).
2. Tạo ít nhất một môn học và hai lớp bằng API Subjects / Classes hiện có. Ghi lại `centerId`, `classId` của từng lớp. Mỗi lớp cần `maxStudents` còn chỗ.
3. Ở nhóm **11 · Class Registration**, gọi `PUT /api/v1/centers/{centerId}/classes/{classId}/registration` với `{"open":true}` cho từng lớp. `GET /classes/{classId}` sẽ hiển thị `registrationOpen: true`.

### Student đăng nhập Google

4. Mở [http://localhost:5217/dev/google-login](http://localhost:5217/dev/google-login) trong trình duyệt. Trang này **chỉ tồn tại ở Development**, dùng cùng origin của API. Chọn tài khoản Google; trang tự gọi `POST /api/v1/auth/google`, hiện ID token để dán vào Swagger và access token để Authorize. Refresh token chỉ nằm trong cookie `HttpOnly`, không có trong JSON hay trang test.
5. Trong Swagger, nếu muốn thử API Google riêng: `POST /api/v1/auth/google` với `{"idToken":"<ID token vừa nhận>"}`. Token Google ngắn hạn; lấy token mới nếu báo `invalid_google_token`. Nếu email đã thuộc tài khoản mật khẩu, API trả `409 google_account_link_required`: đăng nhập tài khoản cũ, gọi `POST /api/v1/auth/google/link` với **ID token + currentPassword**, rồi refresh token để nhận role Student. Link sai mật khẩu trả 401 và chịu rate limit/lockout.
6. Nhấn **Authorize** lần nữa bằng access token Student. Gọi `GET /api/v1/centers/{centerId}/open-classes?page=1&pageSize=25`; chỉ thấy lớp còn chỗ, đang mở và thuộc trung tâm Approved.

### Một form, nhiều lựa chọn lớp

7. Gọi `POST /api/v1/centers/{centerId}/class-applications`. Trong ô header `Idempotency-Key` dùng **một GUID mới cho lần gửi này** (ví dụ từ Visual Studio hoặc `New-Guid` trong PowerShell). Body:

   ```json
   {
     "fullName": "Nguyễn Minh Anh",
     "classIds": ["CLASS_ID_1", "CLASS_ID_2"]
   }
   ```

   Tối đa 10 `classIds` khác nhau. Kết quả 201 có hai đơn `Pending`, cùng `submissionId`, khác `id` và `classId`. `GET /my/class-applications` chỉ trả đơn của Student đang đăng nhập.

8. Gửi lại **đúng key + đúng nội dung**: 200, cùng các `id`, không tạo bản ghi mới. Cùng key nhưng đổi tên hoặc lớp: 409 `idempotency_key_reused`. Key mới nhưng chọn lớp đã Pending/Approved: 409 `class_already_applied`.

### Trung tâm duyệt riêng từng lớp

9. Đăng nhập lại tài khoản Owner/Manager, Authorize bằng access token của nhân viên. `GET /api/v1/centers/{centerId}/class-applications?status=Pending&page=1&pageSize=25` để thấy danh sách chờ. Bỏ `status` để xem cả lịch sử; dùng `Approved`/`Rejected` để lọc.
10. Gọi `POST .../class-applications/{applicationId_1}/approve` cho lớp thứ nhất. Kết quả `Approved`; DB tạo Student gắn `user_id`, enrollment, tăng `enrolled_count` trong **cùng transaction**. Gọi lại approve trả 200 cùng quyết định, không tăng sĩ số lần hai.
11. Gọi `POST .../class-applications/{applicationId_2}/reject` với `{"reason":"Lớp không phù hợp với khung giờ yêu cầu"}`. Kết quả `Rejected`, có `reviewedByUserId`, `reviewedAtUtc`, `reviewReason`. Gọi lại với cùng lý do trả 200; lý do khác trả 409. Student thấy cả hai kết quả trong `GET /my/class-applications`.
12. Student có thể đăng ký lại lớp bị từ chối bằng **key mới** nếu lớp vẫn mở. Khi được duyệt, hệ thống dùng lại Student của cùng `(centerId,userId)`, không tạo Student thứ hai. `GET /my/classes` hiển thị các enrollment đang hiệu lực; `GET /my/sessions?fromUtc=...&toUtc=...` chỉ hiển thị buổi học thuộc lớp của mình trong khoảng tối đa 93 ngày.

### Case cần thử thêm

- Hai Google account khác nhau điền **cùng họ tên**: hai `iam.users` khác nhau, hai Student khác nhau trong một trung tâm; không gộp theo tên.
- Một Student đăng ký các môn ở học kỳ sau bằng cùng Google account: cùng `sub → UserId`, rồi cùng Student của trung tâm; chỉ thêm enrollment/đơn của lớp mới.
- Hai request submit cùng key hoặc hai nhân viên bấm duyệt cùng đơn: khóa transaction theo user/lớp, kiểm tra lại DB sau khi có khóa. Chỉ tạo một bộ đơn và một enrollment.
- Lớp đầy lúc duyệt: 409 `class_full`; đơn vẫn `Pending`, không tăng sĩ số hay tạo enrollment. Quản lý có thể tăng `maxStudents` theo API Class hiện có rồi duyệt lại, hoặc từ chối rõ lý do.
- Đóng đăng ký: học sinh không nộp đơn mới; đơn Pending đã nhận vẫn có thể được duyệt nếu lớp còn hoạt động/chỗ. Archive lớp thì không duyệt được.
- Tài khoản Teacher không có role Student gọi `/my/classes` nhận 403; Student không có quyền Management duyệt đơn nhận 403. Tên học sinh của lớp khác không được đưa vào API Student self-service.

## 4. Kiến trúc và kiểm thử tự động

- `IdentityAccess.Domain/Application/Infrastructure/Api`: User + role, external login, xác minh Google và cấp JWT/session/refresh cookie. Google login không mở transaction DB trước khi xác minh ID token.
- `Academics.Domain`: trạng thái đơn, Student gắn User, lớp mở/đóng đăng ký.
- `Academics.Application`: use case submit/list/approve/reject và quy tắc idempotency/capacity. Không so khớp học sinh bằng tên.
- `Academics.Infrastructure`: EF/PostgreSQL, batch queries, phân trang, advisory transaction locks và unique/FK constraints.
- `Academics.Api`: route, authorization, input/response chuẩn. `class_applications` được index theo user/class/status; `students(center_id,user_id)` unique có điều kiện.

Visual Studio **Test Explorer**: chọn runsettings `backend/tests/IntegrationTests/AuthE2E.runsettings`, chạy `RealPostgresClassRegistrationTests`, `RealPostgresGoogleLoginTests` và toàn bộ suite. Google HTTP E2E dùng verifier giả **chỉ trong test** để không phụ thuộc mạng hoặc tài khoản Google thật; trang `/dev/google-login` là bước test với Google thật. Sau test, kiểm tra pgAdmin không còn tài khoản `e2e_google_*` / trung tâm thử nghiệm.

Lưu ý bảo mật: `HttpOnly` chỉ che **refresh cookie** khỏi JavaScript, không làm access token trong JSON miễn nhiễm XSS. Frontend vẫn cần chống XSS, không lưu access token trong `localStorage`, cấu hình CORS origin cụ thể và HTTPS khi triển khai thật. Google Client ID là định danh công khai; JWT signing key, DB password và các secret khác không đưa vào source code.
