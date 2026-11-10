# Hồ sơ cá nhân và đổi email đăng nhập

Các API tự phục vụ của tài khoản đang đăng nhập. Tất cả cần access token và nằm dưới `/api/v1/me`.

| Endpoint | Việc | Ghi chú |
|---|---|---|
| `GET /me` | Đọc tài khoản | Trả thêm `displayName`, `phoneNumber` (đều có thể `null`) |
| `PUT /me/profile` | Sửa `displayName`, `phoneNumber` | Trả về `CurrentUserDto` mới |
| `POST /me/email/change` | Bước 1 đổi email: `{ newEmail, currentPassword }` | Gửi mã 8 số tới **email mới**, trả `{ newEmail, expiresAtUtc, resendAvailableAtUtc }` |
| `POST /me/email/confirm` | Bước 2: `{ newEmail, otp }` | Đổi email, coi như đã xác minh, trả `CurrentUserDto` mới |

## Quy tắc

- **Họ tên hiển thị**: tuỳ chọn, tối đa 100 ký tự. Khoảng trắng thừa được gộp lại, để trống thì xoá. FE dùng nó cho chip/menu, nếu không có thì dùng tên đăng nhập hoặc email.
- **Số điện thoại**: số di động Việt Nam, lưu dạng `+84…`. Phải là duy nhất. Tài khoản nhân sự bắt buộc giữ số điện thoại (`phone_number_required`); tài khoản học sinh (Google) được để trống.
- **Đổi email**:
  - Chỉ tài khoản có mật khẩu mới đổi được. Tài khoản chỉ dùng Google nhận `email_change_requires_password`, vì email đi theo tài khoản Google.
  - Kiểm tra lại mật khẩu dùng chung bộ đếm khoá đăng nhập (`IPasswordReauthenticator`).
  - Mã gắn với cả địa chỉ mới: mã gửi tới địa chỉ A không xác nhận được địa chỉ B. Mã sống 10 phút, gửi lại sau 1 phút, tối đa 5 lần/24 giờ, sai 5 lần thì mã bị huỷ.
  - Khi đổi xong: email cũ nhận thư báo (địa chỉ mới được che, ví dụ `an***@gmail.com`). Mã đặt lại mật khẩu và mã xác minh email còn treo bị thu hồi. Phiên đăng nhập hiện có được giữ nguyên.
  - Đăng nhập Google đã liên kết vẫn hoạt động vì liên kết theo mã định danh Google, không theo email.

## Mã lỗi

| Mã | HTTP | Khi nào |
|---|---|---|
| `invalid_display_name` | 400 | Họ tên quá 100 ký tự hoặc có ký tự điều khiển |
| `invalid_phone_number` | 400 | Không phải số di động Việt Nam |
| `phone_number_required` | 400 | Tài khoản nhân sự xoá số điện thoại |
| `phone_number_already_exists` | 409 | Số đã thuộc tài khoản khác |
| `invalid_current_password` | 400 | Sai mật khẩu, hoặc đang bị khoá tạm |
| `email_change_requires_password` | 400 | Tài khoản chỉ dùng Google |
| `email_unchanged` | 400 | Email mới trùng email hiện tại |
| `email_already_exists` | 409 | Email đã thuộc tài khoản khác |
| `email_change_too_frequent` | 429 | Gửi lại trong vòng 1 phút hoặc quá 5 lần/24 giờ |
| `invalid_email_change_otp` | 400 | Mã sai, hết hạn, đã dùng, hoặc không khớp email mới |

## Thử tay

1. Áp migration `AddUserDisplayName` bằng DbMigrator.
2. Đăng nhập tài khoản có mật khẩu và gọi `PUT /me/profile` với `{"displayName":"Nguyễn Văn An","phoneNumber":"0987654321"}`. Kỳ vọng `200` và `phoneNumber` là `+84987654321`.
3. Gọi `POST /me/email/change` với email mới và mật khẩu hiện tại. Mở hộp thư email mới (khi chạy local thì xem thư mục email dạng file), lấy mã 8 số.
4. Gọi `POST /me/email/confirm`. Kỳ vọng `200`, `email` là địa chỉ mới, `emailVerified: true`; email cũ nhận thư báo.
