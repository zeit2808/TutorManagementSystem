# Hướng dẫn trung tâm tự kết nối thanh toán tự động (SePay)

Dành cho **chủ trung tâm**. Sau khi kết nối, phụ huynh chuyển khoản theo mã QR là hệ thống tự ghi biên lai và trừ nợ cho đúng học sinh, thường trong 2–5 giây. Thiết kế chi tiết: [THANH-TOAN-QR-KE-HOACH.md](THANH-TOAN-QR-KE-HOACH.md).

## Trước khi bắt đầu

- Tài khoản ngân hàng nhận học phí đã nhập ở **Học phí → Cài đặt**. Tài khoản SePay phải theo dõi **đúng tài khoản này**.
- Bạn đăng nhập bằng tài khoản **Chủ trung tâm** và nhớ mật khẩu: mọi thay đổi đều hỏi lại mật khẩu và được email cho tất cả chủ.
- Một tài khoản ngân hàng chỉ tự ghi biên lai cho **một** trung tâm.

## Bước 1 — Chuẩn bị trên SePay

1. Đăng ký tại [my.sepay.vn](https://my.sepay.vn) và liên kết tài khoản ngân hàng nhận học phí.
2. Tạo **API token**: *Cấu hình công ty → API Access → Thêm API*. Chép token (chỉ hiện một lần).
3. Tạo **webhook**: *Tích hợp WebHooks → Thêm webhook*:
   - Sự kiện: tiền vào.
   - Kiểu xác thực: **HMAC-SHA256**. Tự đặt một **Secret Key** dài (ít nhất 16 ký tự, nên 32+) và chép lại.
   - URL: để trống, chép ở bước 2.

Không gửi token hay Secret Key qua tin nhắn, email hay ảnh chụp màn hình.

## Bước 2 — Kết nối trong phần mềm (chế độ Chạy thử)

1. Vào **Học phí → Thanh toán tự động**.
2. Chọn chế độ **Chạy thử**, dán API token và Secret Key, nhập mật khẩu của bạn, bấm **Kết nối**.
3. Chép **Địa chỉ webhook** vừa hiện ra, dán vào ô URL của webhook trên SePay, lưu.
4. Bấm **Lấy giao dịch ngay**: "Lần lấy giao dịch gần nhất" phải có giờ, không có dòng lỗi.

Nếu thấy "Webhook bị từ chối vì sai chữ ký": Secret Key hai bên chưa giống nhau, dán lại ở cả hai nơi.

## Bước 3 — Chạy thử 1–2 tuần

Ở chế độ Chạy thử, hệ thống **chỉ ghi nháp** "khoản này của ai", không tạo biên lai. Nhân viên vẫn ghi biên lai chuyển khoản bằng tay như cũ.

Cuối mỗi tuần, mở **So sánh chạy thử** (cùng trang) và chọn khoảng ngày:

| Kết luận | Ý nghĩa | Cần làm |
|---|---|---|
| Khớp | Máy và nhân viên ghi cho cùng học sinh | Không |
| Cả hai không ghi | Không phải học phí, hoặc tài khoản khác | Không |
| Lệch học sinh | Máy và người ghi cho hai học sinh khác nhau | Xem ai đúng; báo kỹ thuật nếu máy sai |
| Máy ghi, người chưa ghi | Nhân viên quên ghi | Ghi bù biên lai |
| Người ghi, máy không ghi | Thường do phụ huynh không ghi mã | Bình thường; khi Tự động, khoản này sẽ vào hàng chờ |

Chỉ bật Tự động khi báo cáo hiện **"Máy và người ghi khớp nhau"** trong ít nhất một tuần có nhiều giao dịch.

## Bước 4 — Bật Tự động

1. **Thanh toán tự động → Chế độ: Tự động**, nhập mật khẩu, **Lưu**.
2. Từ lúc này, giao dịch **trước** thời điểm bật được bỏ qua (nhân viên đã ghi tay rồi), giao dịch **sau** được tự ghi.
3. Báo nhân viên: **không ghi tay biên lai chuyển khoản nữa** (hệ thống sẽ chặn). Tiền mặt vẫn ghi như cũ.

## Việc hằng ngày

- **Học phí → Chuyển khoản** (hàng chờ "Tiền chưa rõ của ai"): khoản không có mã hoặc sai mã. Nhân viên bấm **Gán** và chọn học sinh (có gợi ý). Chủ và quản lý có thể **Bỏ qua** nếu không phải học phí (bắt buộc ghi lý do).
- **Nghi trùng** và **Lỗi xử lý**: chỉ chủ xử lý (đối chiếu sao kê ngân hàng trước khi bấm).
- Huỷ một biên lai tự động thì khoản tiền **quay lại hàng chờ** để gán lại; tiền không mất.
- Phụ huynh có email trên tài khoản học sinh sẽ nhận thư "Đã nhận học phí".

## Đối soát và cảnh báo

- **Mỗi đêm 02:00** hệ thống đọc lại 3 ngày gần nhất từ SePay và kiểm tra tiền vào ↔ giao dịch ↔ biên lai ↔ trừ nợ. Xem ở **Học phí → Đối soát**; chủ có thể bấm **Đối soát ngay**.
- Khi có vấn đề, mọi chủ nhận **một email** ngay. Vấn đề còn mở được nhắc trong **email tổng hợp 07:00** mỗi sáng, cùng số khoản tồn quá 24 giờ ở hàng chờ.
- Bấm **Đã xem** để thôi nhắc. Vấn đề tự hết (ví dụ SePay hoạt động lại) thì cảnh báo tự đóng.

| Cảnh báo | Nên làm |
|---|---|
| Không lấy được giao dịch từ SePay quá 1 giờ | Tiền vẫn ở ngân hàng, sẽ được ghi khi SePay hoạt động lại. Nếu lỗi là token bị từ chối: tạo token mới và lưu lại |
| Nhiều webhook sai chữ ký | Vừa đổi Secret Key thì cập nhật cả hai nơi; nếu không, có thể có người thử gửi webhook giả (không gây mất tiền) |
| Giao dịch lỗi xử lý 5 lần | Xem lỗi, sửa nguyên nhân, bấm **Thử lại** ở hàng chờ |
| Tiền vào nhưng chưa được ghi biên lai | Worker thanh toán có thể đang dừng: báo kỹ thuật |
| Webhook có giao dịch nhưng API SePay không có | Đối chiếu sao kê; nếu tiền đã vào, ghi tay biên lai và báo SePay |
| SePay đổi dữ liệu giao dịch đã ghi | Hệ thống giữ bản đã ghi; đối chiếu sao kê ngân hàng |
| Chặn được một lần ghi biên lai trùng, hoặc sổ lệch | Không tự sửa; báo kỹ thuật |

## Đổi tài khoản, đổi token, tạm dừng

- **Đổi API token / Secret Key**: nhập giá trị mới ở **Thanh toán tự động**, để trống ô nào không đổi.
- **Đổi tài khoản ngân hàng** ở *Cài đặt*: chỉ chủ, có hỏi mật khẩu và email cho mọi chủ. Kết nối SePay đi theo tài khoản mới; bỏ tài khoản thì kết nối về **Tắt**.
- **Tạm dừng**: chọn chế độ **Tắt**. Muốn bật lại Tự động phải chạy thử trước.
- Nhận được email báo thay đổi mà **không phải bạn làm**: đổi mật khẩu ngay, kiểm tra tài khoản nhận ở *Cài đặt* và liên hệ kỹ thuật.
