# Học phí — quyết định nghiệp vụ & thiết kế

Trạng thái: **đã chốt và triển khai 23/10/2026**. Test tay: Phần 13 của tài liệu kiểm thử. Module: `Finance` (schema `finance`).
Liên quan: `REVIEW-NGHIEP-VU-BACKEND.md` mục 6.4; lương giáo viên làm đợt sau.

## 1. Quyết định đã chốt

| # | Câu hỏi | Chốt |
|---|---|---|
| H1 | Hình thức thu | **Theo tháng**, mỗi lớp một mức học phí cố định / kỳ |
| H2 | Giá theo lớp hay học sinh | **Theo lớp**; khác biệt từng em dùng miễn giảm (H8) |
| H3 | Đổi giá giữa chừng | Áp dụng **từ kỳ sau**; lưu lịch sử giá; kỳ đã tạo không đổi |
| H4 | Kỳ thu | 1 kỳ = 1 tháng, **tính từ buổi học đầu tiên của lớp** (mọi em trong lớp chung mốc kỳ). Ngày 29–31 không có trong tháng → ngày cuối tháng |
| H5 | Vắng có phép / không phép | **Đều tính** — điểm danh không làm đổi học phí |
| H6 | Buổi trung tâm huỷ / không diễn ra | Không trừ tiền (H9). Kỳ **không có buổi học nào** (lớp đã kết thúc hoặc nghỉ cả kỳ) thì không thu |
| H7 | Vào / rời giữa chừng | **Vào giữa kỳ: tính theo % số buổi** — kỳ đầu = học phí × (số buổi từ buổi đầu em học đến hết kỳ ÷ số buổi của cả kỳ), làm tròn 1.000đ; từ kỳ sau đóng đủ. Số buổi chỉ đếm buổi không bị huỷ / không diễn ra, chốt lúc lập khoản thu. **Rời giữa kỳ:** **tròn tiền** — tháng học trọn phải đóng; kỳ dở dang đã đóng bao nhiêu giữ bấy nhiêu, **không hoàn**, chưa đóng thì **không thu**, chỉ ghi chú "(kỳ n: đã học x/y buổi ≈ z đ — không thu)" |
| H8 | Miễn giảm | Giảm **%** hoặc **số tiền cố định** cho một em ở một lớp, có ngày hiệu lực, **bắt buộc lý do**; chỉ chủ/quản lý |
| H9 | Tính tiền khi nào | Mỗi kỳ một khoản cố định, lập ngay khi kỳ bắt đầu. Không cộng trừ sang kỳ sau vì buổi nghỉ. Tiền chưa đóng vẫn là nợ của kỳ đó. Không có khoá sổ |
| H10 | Hình thức thu | Tiền mặt, chuyển khoản; hiện **mã VietQR** theo tài khoản trung tâm. Không đối soát ngân hàng tự động |
| H11 | Đóng một phần / đóng trước / gộp | Cho phép cả ba. Tiền thừa thành **số dư**, tự trừ vào kỳ sau. Một biên lai trả nhiều kỳ, nhiều lớp |
| H12 | Biên lai | Số liên tục theo trung tâm (`PT-000001`), in / lưu PDF; **huỷ có lý do, không xoá** |
| H13 | Quyền | Chủ, Quản lý, Nhân viên: xem và thu tiền. Chỉ Chủ, Quản lý: đặt học phí, miễn giảm, huỷ biên lai, cài đặt. Giáo viên không thấy học phí |
| H14 | Học sinh | Xem khoản phải đóng, biên lai, số dư, mã QR trong cổng học sinh (chỉ xem) |
| H15 | Nhắc nợ | Hạn đóng mặc định **7 ngày** từ đầu kỳ (trung tâm chỉnh được); quá hạn → nhắc trong app + một email. Không chặn gì |
| H16 | Tiền tệ | VND, làm tròn 1.000đ. Hoá đơn điện tử / thuế: ngoài phạm vi |
| H17 | Lương giáo viên | Đợt sau |
| H18 | Buổi nghỉ (chốt 23/10) | Hệ thống **không tự trừ** tiền. Mỗi khoản thu hiện **các buổi nghỉ** của kỳ: em vắng có phép, vắng không phép (theo phiếu điểm danh đã nộp), buổi trung tâm huỷ / không diễn ra (ghi rõ đã có buổi bù hay chưa). Chủ / quản lý **tự quyết** có giảm không: "Giảm học phí kỳ này" với số buổi được giảm (gợi ý = số buổi × học phí kỳ ÷ số buổi của kỳ, sửa được) và **lý do bắt buộc**. Sửa hoặc bỏ được về 0. Nếu kỳ đã đóng đủ, phần giảm thành **số dư** trừ vào kỳ sau. Không áp dụng cho kỳ đã kết thúc sớm (em đã nghỉ học) |

## 2. Ví dụ quy tắc H7 (800.000đ/tháng, em nghỉ giữa tháng thứ 2)

| Đã đóng | Tính | Ghi chú |
|---|---|---|
| 2 tháng | 2 tháng, không hoàn | — |
| 1 tháng | 1 tháng, hết nợ | (kỳ 2: đã học 4/8 buổi ≈ 400.000đ — không thu) |
| 0 | 1 tháng → nợ 800.000đ | (kỳ 2: đã học 4/8 buổi ≈ 400.000đ — không thu) |
| Kỳ 2 đóng dở 300.000đ | Giữ 300.000đ, không đòi thêm | (kỳ 2: … — đã thu 300.000đ) |

Số dư (đóng trước) còn lại khi em nghỉ vẫn nằm trên hồ sơ; hệ thống không tự hoàn.

**Vào giữa kỳ** (lớp bắt đầu 15/10, kỳ 15/10–14/11 có 8 buổi, em vào ngày 1/11 còn 3 buổi): kỳ đầu = 800.000 × 3/8 = 300.000đ, ghi chú "(vào giữa kỳ: 3/8 buổi)". Từ kỳ 15/11–14/12 đóng đủ 800.000đ. Nếu em vừa vào giữa kỳ vừa nghỉ ngay trong kỳ đó thì áp dụng tiếp quy tắc rời giữa kỳ cho khoản 300.000đ.

## 3. Thiết kế

- **Học phí lớp** (`class_fees`): lịch sử giá theo `effective_from`. Kỳ dùng giá có `effective_from` ≤ ngày đầu kỳ. Lần đặt giá **đầu tiên** cũng áp dụng cho kỳ đang chạy (kỳ chứa ngày đặt giá) để không bỏ sót tháng hiện tại; các kỳ trước đó không bị truy thu.
- **Khoản thu** (`tuition_charges`): một dòng / (ghi danh, số kỳ), unique. Số tiền = giá − miễn giảm (làm tròn 1.000đ, không âm). Hạn = đầu kỳ + số ngày hạn. Rời giữa kỳ → `amount` hạ bằng số đã đóng, lưu `sessions_attended/sessions_in_period/partial_value`.
- **Biên lai** (`receipts` + `receipt_allocations`): tổng tiền phân bổ vào các khoản (cũ nhất trước, hoặc theo khoản chọn); phần chưa phân bổ là **số dư** và tự phân bổ vào khoản mới. Huỷ biên lai bỏ các phân bổ của nó, rồi phân bổ lại số dư còn lại.
- **Đồng bộ** (`TuitionSync`): tính kỳ từ dữ liệu Academics (ghi danh, buổi học, hồ sơ) mỗi khi xem học phí và mỗi ngày ở Worker; idempotent nhờ unique + advisory lock theo trung tâm. Hồ sơ đã gộp (M3): khoản thu theo ghi danh nên tự đi theo hồ sơ giữ lại; biên lai của hồ sơ bị gộp được chuyển sang.
- Đọc dữ liệu Academics bằng SQL chỉ đọc (như Academics đọc `iam`/`organization`).
- Khoản thu lập từ ngày học sinh được ghi danh (hoặc ngày lớp bắt đầu), kể cả khi buổi đầu của em còn ở phía trước. Kỳ đầu của em vào giữa kỳ = học phí × số buổi từ lúc ghi danh đến hết kỳ ÷ số buổi của kỳ; hạn tính từ ngày ghi danh.
- Nhắc nợ: Worker chạy mỗi giờ (đồng bộ mọi trung tâm có học phí, rồi gửi một email cho mỗi học sinh có khoản mới quá hạn; mỗi khoản chỉ nhắc một lần).

## 4. Endpoint (prefix `/api/v1`, tag "16 · Tuition")

| Method & route | Quyền | Ghi chú |
|---|---|---|
| `GET centers/{c}/tuition/summary` | Chủ, quản lý, nhân viên | Còn phải thu, quá hạn, đã thu tháng này, số dư |
| `GET centers/{c}/tuition/classes` · `PUT centers/{c}/classes/{id}/fee` | Xem: 3 vai trò · Đặt: chủ, quản lý | `{monthlyFee}` |
| `GET centers/{c}/tuition/charges?status=All\|Open\|Overdue\|Paid\|Missed&classId&studentId&search&page` | 3 vai trò | Kèm buổi nghỉ của từng kỳ |
| `PUT centers/{c}/tuition/charges/{id}/adjustment` | Chủ, quản lý | H18 `{amount, sessions, reason}`; amount 0 = bỏ giảm |
| `GET centers/{c}/students/{id}/tuition` | 3 vai trò | Các kỳ, biên lai, miễn giảm, số dư, VietQR |
| `POST centers/{c}/students/{id}/receipts` | 3 vai trò | `{amount, method, receivedOn?, payerName?, note?, chargeIds?, requestId?}` |
| `GET centers/{c}/receipts` · `GET …/{id}` · `POST …/{id}/void` | Xem: 3 vai trò · Huỷ: chủ, quản lý | Huỷ cần `{reason}` |
| `POST centers/{c}/students/{id}/discounts` · `POST centers/{c}/discounts/{id}/revoke` | Chủ, quản lý | `{classId, kind, value, fromDate, toDate?, reason}` |
| `GET/PUT centers/{c}/tuition/settings` | Xem: 3 vai trò · Sửa: chủ, quản lý | Ngân hàng, hạn đóng, email nhắc, `expectedVersion` |
| `GET me/tuition` | Học sinh | Mọi trung tâm, chỉ xem |

Migration: `Finance/20261023112910_InitialTuition` (schema `finance`: `class_fees`, `class_billing_anchors`, `tuition_charges`, `tuition_discounts`, `receipts`, `receipt_allocations`, `receipt_counters`, `finance_settings`).
