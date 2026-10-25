# Cấu hình SMTP và test luồng email end-to-end

## 0. Cách nhanh (cập nhật 24/10/2026)

1. Tạo App Password Gmail (mục 3.1).
2. Trong `backend`: `.\tools\setup-smtp.ps1 -Sender ten.ban@gmail.com`, dán App Password khi được hỏi.
   Script ghi vào User Secrets của Worker (không đụng `appsettings.json`). Quay về ghi file: `.\tools\setup-smtp.ps1 -UseFile`.
3. Khởi động lại Worker; log phải có `worker started with Smtp email delivery`.
4. Đăng nhập `admin` → **Quản trị nền tảng → Email hệ thống** → **Gửi email thử**. Trang này hiện số thư chờ gửi,
   đang thử lại, thất bại và 20 thư gần nhất (không hiện nội dung).

Email hệ thống gửi (tất cả bằng tiếng Việt, người gửi mặc định "Quản Lý Dạy Thêm"):

| Luồng | Người nhận | Link trong thư |
|---|---|---|
| Mời chủ trung tâm / mời thành viên | Email được mời | `/accept-invitation?token=…` + mã dự phòng |
| Xác minh email (tài khoản cũ) | Chủ tài khoản | `/verify-email?token=…` + mã |
| Mã đặt lại mật khẩu, báo đã đổi mật khẩu | Chủ tài khoản | — |
| Đổi lịch / thêm vào lớp | Học sinh có tài khoản | `/student` |
| Duyệt / từ chối đơn đăng ký lớp | Học sinh gửi đơn | `/student` |
| Nhắc học phí quá hạn (Worker, mỗi giờ) | Học sinh có tài khoản | `/student/tuition` |

Link lấy từ `Frontend:BaseUrl` (API và Worker; môi trường Development = `http://localhost:3000`).
Production phải đặt địa chỉ thật cho **cả hai** host.

Độ tin cậy:

- Lỗi tạm (mất mạng, SMTP sập, sai mật khẩu) → thử lại sau 1, 5, 15 phút rồi mỗi giờ, tối đa
  `EmailDelivery:MaximumAttempts` (5) lần. Không còn đốt hết số lần thử trong vài giây.
- Máy chủ từ chối người nhận (5xx khi RCPT TO, địa chỉ sai định dạng) → dừng ngay, trạng thái "Gửi thất bại".
- Kết nối SMTP giữ sẵn bị nhà cung cấp cắt khi rảnh → tự mở kết nối mới và gửi lại ngay, không tính là lỗi.
- Mã OTP / mã xác minh đã hết hạn hoặc có mã mới → không gửi, ghi "Bỏ qua" kèm lý do.
- Gửi xong thì xoá nội dung thư khỏi hàng đợi.

## 1. Kết quả hiện tại

Worker hỗ trợ hai phương thức giao email:

| Provider | Mục đích | Kết quả |
|---|---|---|
| `File` | Phát triển offline | Tạo file `.txt` trong `.local/emails` |
| `Smtp` | Gửi email thật | Gửi qua SMTP bằng MailKit và TLS |

`File` vẫn là mặc định. Vì vậy pull source hoặc chạy project lần đầu không tự ý
gửi email ra Internet. Chỉ khi Worker User Secrets có
`EmailDelivery:Provider = Smtp` thì Worker mới dùng SMTP.

Không cần tạo migration mới vì thay đổi này chỉ thuộc hạ tầng giao email, không
thay đổi cấu trúc database.

## 2. Luồng chạy

```text
POST /api/v1/auth/resend-verification-email
                    |
                    v
API tạo token, chỉ lưu token hash và email Outbox trong cùng transaction
                    |
                    v
API trả 204 ngay, không chờ SMTP
                    |
                    v
Worker lấy email Pending từ iam.email_outbox_messages
                    |
                    v
SMTP + STARTTLS -> hộp thư người nhận
                    |
                    v
Worker đánh dấu Processed và xóa Body nhạy cảm khỏi Outbox
```

Nếu SMTP tạm lỗi, Outbox vẫn còn trong PostgreSQL để Worker thử lại. Tối đa mặc
định là 5 lần. API không bị chậm theo thời gian phản hồi của nhà cung cấp email.

## 3. Thiết lập Gmail SMTP bằng Visual Studio

Phần này phù hợp để test local. Với production nên dùng một account gửi mail
riêng hoặc nhà cung cấp transactional email, không dùng hộp thư cá nhân.

### 3.1. Tạo App Password của Google

1. Đăng nhập Google Account dùng để gửi email.
2. Mở **Security** và bật **2-Step Verification**.
3. Mở **App passwords**.
4. Tạo App Password mới, có thể đặt tên `TutorManagement Local`.
5. Sao chép mật khẩu 16 ký tự. Đây không phải mật khẩu Gmail thông thường.
6. Không commit, không chụp màn hình và không gửi mật khẩu này cho người khác.

Nếu account công ty/trường học không hiện App passwords, chính sách tổ chức có
thể đã khóa chức năng này. Khi đó dùng SMTP credential của Mailtrap, Brevo,
Postmark, Amazon SES hoặc nhà cung cấp khác; code không cần thay đổi.

### 3.2. Mở Worker User Secrets

Trong **Solution Explorer**:

1. Mở `Hosts`.
2. Nhấp phải project `TutorManagement.Worker`.
3. Chọn **Manage User Secrets**.
4. Giữ nguyên `ConnectionStrings:MainDatabase` đang có.
5. Bổ sung phần `EmailDelivery` dưới đây vào cùng object JSON.

Ví dụ cấu hình Gmail:

```json
{
  "ConnectionStrings:MainDatabase": "GIỮ NGUYÊN GIÁ TRỊ HIỆN TẠI",
  "EmailDelivery": {
    "Provider": "Smtp",
    "MaximumConcurrency": 2,
    "Smtp": {
      "Host": "smtp.gmail.com",
      "Port": 587,
      "Security": "StartTls",
      "AuthenticationRequired": true,
      "Username": "your-sender@gmail.com",
      "Password": "APP_PASSWORD_16_KY_TU",
      "FromAddress": "your-sender@gmail.com",
      "FromName": "Tutor Management",
      "TimeoutMilliseconds": 30000
    }
  }
}
```

Lưu ý:

- `FromAddress` nên giống Gmail trong `Username`.
- Có thể bỏ khoảng trắng trong App Password trước khi dán.
- Cổng `587` phải đi với `StartTls`.
- Nếu dùng cổng `465`, đổi `Security` thành `SslOnConnect`.
- Không đặt password vào `appsettings.json`.
- Chỉ sửa User Secrets của **Worker**; API không trực tiếp gửi email.

## 4. Kiểm tra cấu hình trước khi test nghiệp vụ

### 4.1. Build trong Visual Studio

1. Chọn menu **Build**.
2. Chọn **Rebuild Solution**.
3. Mở **View > Error List**.
4. Kết quả mong đợi: 0 error.

Sau đó mở **Test > Test Explorer**, chọn **Run All Tests**. Kết quả hiện tại là
31/31 pass. `SmtpEmailDeliveryClientTests` dựng một SMTP server cục bộ trong
test process và xác nhận client thực sự gửi đúng SMTP/MIME message, không chỉ
kiểm tra mock.

### 4.2. Cấu hình chạy đồng thời API và Worker

1. Nhấp phải solution `TutorManagementSystem`.
2. Chọn **Configure Startup Projects**.
3. Chọn **Multiple startup projects**.
4. Đặt `TutorManagement.Api` thành **Start**.
5. Đặt `TutorManagement.Worker` thành **Start**.
6. Các project còn lại để **None**.
7. Nhấn **Apply**, sau đó **OK**.
8. Nhấn `F5` hoặc `Ctrl+F5`.

Worker phải log dòng tương tự:

```text
Tutor Management worker started with Smtp email delivery.
```

Nếu log hiển thị `File`, User Secrets chưa được nạp đúng project hoặc
`Provider` vẫn là `File`.

## 5. Chuẩn bị account phát triển chưa xác minh

Public Register đã bị xóa theo quy tắc invitation-only. Trong khi chức năng
invitation chưa được xây dựng, ta dùng account seed để test riêng email
verification trong môi trường local.

Thao tác bằng pgAdmin:

1. Mở database `tutor_management`.
2. Mở **Schemas > iam > Tables > users**.
3. Nhấp phải `users` và chọn **View/Edit Data > All Rows**.
4. Tìm dòng có `username = admin` hoặc username seed của bạn.
5. Đặt `email` thành địa chỉ email mà bạn có thể mở hộp thư.
6. Đặt `normalized_email` thành chính email đó nhưng viết hoa toàn bộ.
7. Đặt `email_verified_at_utc` thành `NULL`.
8. Nhấn nút **Save Data Changes**.

Đây chỉ là chuẩn bị dữ liệu test local. Production sẽ tạo account chưa xác minh
từ lời mời trung tâm hợp lệ, không chỉnh bảng bằng tay.

## 6. Test một luồng hoàn chỉnh trong Swagger

Mở:

`http://localhost:5217/swagger/index.html`

### Bước A — kiểm tra chưa xác minh thì không đăng nhập được

1. Mở `POST /api/v1/auth/login`.
2. Nhập username seed và password hiện tại.
3. Chọn **Execute**.

Kết quả mong đợi:

- HTTP `401`;
- error code là `email_not_verified`;
- chưa có access token và refresh cookie hợp lệ mới.

### Bước B — yêu cầu gửi email xác minh

1. Mở `POST /api/v1/auth/resend-verification-email`.
2. Nhập đúng email vừa đặt trong pgAdmin:

```json
{
  "email": "your-recipient@example.com"
}
```

3. Chọn **Execute**.

Kết quả mong đợi là HTTP `204`. API cố ý luôn trả `204` cả khi email không tồn
tại để người ngoài không dò được account nào đã đăng ký.

### Bước C — quan sát Worker và Outbox

Trong cửa sổ console của Worker, kết quả thành công có dạng:

```text
Email outbox batch completed. Processed: 1; Failed: 0.
```

Trong pgAdmin:

1. Mở **Schemas > iam > Tables > email_outbox_messages**.
2. Chọn **View/Edit Data > All Rows**.
3. Xem dòng mới nhất.

Kết quả mong đợi:

- `recipient` đúng;
- `processed_at_utc` có giá trị;
- `attempt_count = 1`;
- `last_error` là `NULL`;
- `body` đã được xóa sau khi gửi thành công để giảm thời gian token thô nằm
  trong database.

### Bước D — nhận email và xác minh

1. Mở Inbox và cả Spam/Junk của địa chỉ nhận.
2. Mở email có subject `Verify your Tutor Management email`.
3. Sao chép token trong email.
4. Trong Swagger mở `POST /api/v1/auth/verify-email`.
5. Gửi:

```json
{
  "token": "TOKEN_VUA_NHAN"
}
```

Kết quả mong đợi là HTTP `204`.

Gọi lại cùng token phải nhận HTTP `400`, vì token one-time đã được dùng. Trong
database chỉ lưu SHA-256 hash của token, không lưu token thô.

Nếu gọi resend lần nữa sau cooldown một phút, token trong email trước bị revoke
ngay. Chỉ token mới nhất còn hợp lệ. PostgreSQL cũng có unique partial index để
không thể tồn tại hai token chưa dùng/chưa revoke cho cùng user và purpose.

### Bước E — login, access token và refresh cookie

1. Gọi lại `POST /api/v1/auth/login` với username/password seed.
2. Kết quả mong đợi là HTTP `200`.
3. JSON có `accessToken`, `expiresIn`, thời gian hết hạn access/refresh.
4. JSON **không có refresh token**.
5. Response header có cookie `tm.refresh` với `HttpOnly`.
6. Nhấn nút **Authorize** của Swagger và nhập:

```text
Bearer ACCESS_TOKEN
```

7. Gọi `GET /api/v1/platform/centers`, mong đợi HTTP `200`.
8. Gọi `POST /api/v1/auth/refresh-token` không truyền body, mong đợi HTTP `200`
   và một access token mới. Swagger cùng origin sẽ tự gửi cookie.
9. Gọi `POST /api/v1/auth/logout`, mong đợi HTTP `204`.
10. Gọi lại `refresh-token`, mong đợi HTTP `401`.

Như vậy một lần test đã đi qua:

```text
Email chưa xác minh -> Outbox -> Worker -> SMTP -> Inbox -> Verify
-> Login -> JWT access token + HttpOnly refresh cookie -> Refresh -> Logout
```

## 7. Xử lý lỗi thường gặp

### Worker không khởi động

Đọc exception cấu hình. Validator sẽ chỉ rõ trường thiếu, ví dụ `Host`,
`Username`, `Password`, `FromAddress` hoặc port không hợp lệ.

### Gmail báo authentication failed

- Không dùng mật khẩu Gmail thường.
- Kiểm tra 2-Step Verification đã bật.
- Tạo App Password mới và cập nhật Worker User Secrets.
- Nếu vừa đổi mật khẩu Google, App Password cũ có thể đã bị thu hồi.

### TLS hoặc certificate lỗi

- Gmail: dùng `587 + StartTls` hoặc `465 + SslOnConnect`.
- Không sửa code để bỏ kiểm tra certificate.
- Kiểm tra ngày giờ Windows và antivirus/proxy có chặn TLS hay không.

### Worker log `Failed: 1`

Trong `iam.email_outbox_messages`, xem `last_error`. Sau khi sửa SMTP:

- nếu chưa đủ `MaximumAttempts`, Worker sẽ thử lại;
- nếu đã đủ 5 lần, chờ hết cooldown một phút rồi gọi lại endpoint resend để
  tạo một Outbox message mới.

### API trả 204 nhưng không có dòng Outbox

Endpoint resend cố ý không tiết lộ account. Kiểm tra:

- email trong request có khớp `users.normalized_email` không;
- account đã verify hay chưa;
- đã có token được tạo trong vòng một phút hay chưa.

## 8. Ranh giới hiện tại

SMTP thật đã hoàn chỉnh cho hạ tầng gửi email. Luồng invitation-only của trung
tâm vẫn là bước nghiệp vụ kế tiếp và chưa được giả lập bằng public Register.
Khi có `CenterInvitation`, handler chỉ cần lưu invitation + Outbox message trong
cùng transaction; Worker và SMTP hiện tại được tái sử dụng nguyên vẹn.

