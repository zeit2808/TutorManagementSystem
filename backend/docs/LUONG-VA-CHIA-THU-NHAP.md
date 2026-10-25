# Lương giáo viên & chia thu nhập — quyết định nghiệp vụ & thiết kế

Trạng thái: **đã chốt 24/10/2026**. Module: `Finance` (schema `finance`), cạnh học phí (`HOC-PHI.md`).
Test tay: phần 8 của tài liệu kiểm thử toàn hệ thống.

Hai phương thức, chọn **theo từng trung tâm**, chỉ **admin nền tảng** bật/tắt và đổi:

| Phương thức | Dùng khi | Tiền của mỗi giáo viên |
|---|---|---|
| **M1 – Chia thu nhập** (`RevenueShare`) | Hộ gia đình: chủ tự dạy, không ai trả lương cho ai | Học phí **thực thu** trong tháng (trừ chi phí chung nếu có) chia cho người dạy **theo lớp**, trong lớp theo **giờ dạy** |
| **M2 – Trả lương theo buổi** (`PerSession`) | Trung tâm thuê giáo viên | Số buổi dạy × **đơn giá / buổi** (theo giáo viên, ghi đè theo lớp) |

## 1. Quyết định chung (C)

| # | Chốt |
|---|---|
| C1 | Trang admin có mục "Lương & chia thu nhập" cho từng trung tâm, mặc định **tắt**. Tắt thì menu ẩn, dữ liệu giữ nguyên |
| C2 | Phương thức mới có hiệu lực **từ đầu tháng hiện tại hoặc tháng sau**. Không đổi được tháng đã chốt |
| C3 | Bảng `payroll_config_changes`: mỗi lần bật/tắt/đổi ghi trước → sau, tháng hiệu lực, **lý do bắt buộc**, ai, lúc nào |
| C4 | Kỳ = **tháng dương lịch**, giờ Việt Nam |
| C5 | Chỉ buổi có **phiếu điểm danh đã nộp**, tính cho **giáo viên dạy thực tế**, thời lượng = giờ bắt đầu → kết thúc buổi. Quên điểm danh: chủ / quản lý nộp muộn được bất cứ lúc nào (giáo viên chỉ đến hết ngày); buổi tính vào **tháng của ngày diễn ra**. Tháng đó đã chốt thì phần chênh sang tháng sau (C9). Buổi đã qua mà chưa có phiếu: cảnh báo "chưa tính" |
| C6 | Buổi huỷ / không diễn ra: không tính. Buổi **bù** là buổi riêng: diễn ra và nộp phiếu thì tính cho người dạy buổi bù, vào tháng của buổi bù (xếp trước hay sau buổi gốc đều vậy) — không bao giờ tính hai lần |
| C7 | Buổi mọi học sinh vắng vẫn tính (phiếu đã nộp) |
| C8 | **Nháp** (tự cập nhật) → **Đã chốt** (khoá số liệu) → chi tiền bằng phiếu chi. Chốt theo thứ tự tháng. Chỉ **chủ** chốt / mở lại; mở lại cần lý do, chỉ mở được tháng chốt gần nhất |
| C9 | Tháng đã chốt không đổi. Dữ liệu đổi sau khi chốt (phiếu nộp muộn, hiệu chỉnh, huỷ / thêm biên lai, đổi đơn giá lùi ngày) → phần chênh tự vào tháng nháp kế tiếp thành dòng **"Điều chỉnh tháng trước"** |
| C10 | Khoản cộng / trừ tay (thưởng, phạt, tạm ứng, phụ cấp, chia tay phần chưa phân bổ): số tiền ± bội 1.000đ, **lý do bắt buộc**, gắn tháng nháp + giáo viên; huỷ có lý do |
| C11 | **Phiếu chi** `PC-000001` (cả M1 và M2): tiền mặt / chuyển khoản, chi nhiều lần, chi được cả trong tháng nháp (tạm ứng — L7); huỷ có lý do, không xoá |
| C12 | Chủ: xem tất cả, chốt, mở lại, chi. **Quản lý**: xem tất cả, lập nháp (đơn giá, chi phí, khoản cộng/trừ, xử lý báo sai), không chốt, không chi. Nhân viên: không thấy. Giáo viên: chỉ phần của mình |
| C13 | Gia đình: một người **Chủ**, người kia **Quản lý**; cả hai có **hồ sơ giáo viên gắn với tài khoản mình** (quản lý vừa quản lý vừa dạy) |
| C14 | Giáo viên xem bản nháp của mình và **Báo sai**; chủ / quản lý xử lý. Còn báo sai chưa xử lý thì **không chốt được** |
| C15 | Mỗi trung tâm chỉ thấy phần tại trung tâm mình. API **"Thu nhập của tôi"**: giáo viên xem tổng theo tháng và theo năm, gộp mọi trung tâm, tách từng trung tâm / lớp; chỉ chính người đó |
| C16 | Thuế, bảo hiểm: **chưa làm, bàn sau**. Tạm dùng khoản trừ tay |

## 2. M1 – Chia thu nhập (G)

| # | Chốt |
|---|---|
| G1 | Tiền đem chia = **biên lai chưa huỷ có ngày thu trong tháng** |
| G2 | Phần biên lai chưa trừ vào kỳ nào (đóng trước) **chưa chia**; khi được trừ vào một kỳ thì phần đó thuộc tháng của biên lai (tháng đã chốt → C9) |
| G3 | **Theo lớp**: mỗi khoản tiền đã trừ vào kỳ học phí của lớp X được chia cho người dạy lớp X trong **kỳ học phí đó**, theo phút dạy: buổi đã nộp phiếu → giáo viên dạy thực tế; buổi chưa tới → giáo viên được phân công (dự kiến, đổi theo thực tế khi dạy xong); buổi đã qua chưa nộp phiếu → chưa tính |
| G4 | **Chi phí chung** tuỳ chọn: tháng có nhập thì trừ trước khi chia, theo đúng tỉ lệ G3; không nhập thì chia toàn bộ |
| G5 | Tiền không gán được cho ai (kỳ không có buổi nào tính được) → **"Chưa phân bổ"**; chủ chia tay bằng khoản cộng, hoặc tự vào khi có phiếu (C9) |
| G6 | Huỷ biên lai: tháng nháp tự trừ; tháng đã chốt → C9 |
| G7 | Mỗi phần làm tròn 1.000đ, phần lẻ dồn vào người nhận nhiều nhất; tổng chia = đúng tổng còn lại |
| G8 | Lớp chưa đặt học phí vẫn hiện giờ dạy, tiền 0 |
| G9 | Màn tháng: tổng thu → đóng trước chưa chia → chưa phân bổ → chi phí → còn lại; mỗi người: buổi, giờ, từng lớp, tiền chia, cộng/trừ, điều chỉnh tháng trước, thực nhận, đã chi, còn lại |

## 3. M2 – Trả lương theo buổi (L)

| # | Chốt |
|---|---|
| L1 | **Theo buổi** (đơn giá × số buổi đã nộp phiếu) |
| L2 | Mỗi giáo viên một đơn giá mặc định; **ghi đè theo cặp giáo viên – lớp** |
| L3 | Đơn giá có ngày hiệu lực, lưu lịch sử; buổi dùng giá có hiệu lực vào **ngày diễn ra** |
| L4 | Buổi huỷ sát giờ do trung tâm: không tự tính; dùng khoản cộng tay |
| L5 | Sĩ số không ảnh hưởng |
| L6 | Dạy thay: người dạy thực tế nhận, theo **đơn giá của chính người đó** |
| L7 | Ứng lương: phiếu chi trong tháng nháp; thực nhận cuối tháng tự trừ phần đã chi |
| L8 | QR chuyển khoản tới giáo viên: để sau |
| L9 | Thực nhận âm, buổi chưa có đơn giá, hoặc đã chi nhiều hơn thực nhận → **không cho chốt** cho đến khi chủ sửa |

## 4. Admin (A)

| # | Chốt |
|---|---|
| A1 | Admin chỉ cấu hình, **không xem số tiền** của trung tâm |
| A2 | Admin chỉ có **bật/tắt + chọn phương thức + tháng hiệu lực + lý do**; mọi tuỳ chọn chi tiết do trung tâm nhập |

## 5. Thiết kế

- Bảng: `payroll_configs`, `payroll_config_changes`, `pay_rates`, `payroll_adjustments`, `shared_expenses`,
  `payroll_months` + `payroll_month_lines` (ảnh chụp lúc chốt), `payroll_carries` (chênh lệch đã chuyển: từ tháng → vào tháng),
  `payroll_month_events` (chốt / mở lại), `payroll_payouts` + `payout_counters`, `payroll_disputes`.
- Tính toán là hàm thuần (`PayrollCalculator`) từ: buổi học + phiếu điểm danh (SQL chỉ đọc sang `academics`), biên lai +
  phân bổ + khoản thu (M1, sau khi đồng bộ học phí), đơn giá (M2), khoản cộng/trừ, chi phí, phiếu chi, tháng đã chốt.
- Tháng nháp luôn tính lại khi xem. "Điều chỉnh tháng trước" = (tính lại tháng K) − (ảnh chụp K) − (đã chuyển từ K), cho mọi
  tháng K đã chốt; lúc chốt tháng N, phần này được ghi vào `payroll_carries` (K → N).
- Phương thức của một tháng = lần đổi cấu hình mới nhất có hiệu lực ≤ tháng đó; tháng đã chốt giữ phương thức lúc chốt.
- Ghi dữ liệu dưới khoá `finance:{centerId}` (chung với học phí).

## 6. Endpoint (prefix `/api/v1`, tag "17 · Payroll")

| Method & route | Quyền | Ghi chú |
|---|---|---|
| `GET/PUT platform/centers/{c}/payroll-config` | Admin nền tảng | `{enabled, mode, effectiveMonth "yyyy-MM", reason, expectedVersion}`; trả kèm lịch sử |
| `GET platform/payroll-configs` | Admin nền tảng | Mọi trung tâm đang hoạt động / đình chỉ: tháng này bật gì, thay đổi đã hẹn, tháng chốt gần nhất, đổi lần cuối (trang **Quản trị nền tảng → Lương & thu nhập**) |
| `GET centers/{c}/payroll/status` | Mọi thành viên | Bật chưa, phương thức tháng này, hồ sơ giáo viên của tôi |
| `GET centers/{c}/payroll/months` | Chủ, quản lý | Các tháng từ khi bật: trạng thái, tổng |
| `GET centers/{c}/payroll/months/{yyyy-MM}` | Chủ, quản lý; giáo viên (chỉ phần mình) | Chi tiết tháng + cảnh báo |
| `POST …/months/{m}/close` · `POST …/months/{m}/reopen` | Chủ | Mở lại cần `{reason}` |
| `POST …/months/{m}/adjustments` · `POST centers/{c}/payroll/adjustments/{id}/void` | Chủ, quản lý | `{teacherId, amount, reason}` |
| `POST …/months/{m}/expenses` · `POST centers/{c}/payroll/expenses/{id}/void` | Chủ, quản lý (M1) | `{amount, spentOn, category, note}` |
| `GET/POST centers/{c}/payroll/rates` · `POST …/rates/{id}/void` | Chủ, quản lý (M2) | `{teacherId, classId?, amount, effectiveFrom}` |
| `POST …/months/{m}/payouts` · `POST centers/{c}/payroll/payouts/{id}/void` | Chủ | `{teacherId, amount, method, paidOn, note?, requestId?}` |
| `POST …/months/{m}/disputes` · `POST centers/{c}/payroll/disputes/{id}/resolve` | Giáo viên (của mình) · Chủ, quản lý | `{message}` · `{note}` |
| `GET me/earnings?year=` | Tài khoản nhân sự | Gộp mọi trung tâm đã bật |
