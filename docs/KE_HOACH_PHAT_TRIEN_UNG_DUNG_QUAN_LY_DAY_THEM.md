# Kế hoạch phát triển ứng dụng web quản lý dạy thêm

**Phiên bản:** 1.0  
**Ngày lập:** 12/10/2026  
**Thị trường mục tiêu:** Việt Nam  
**Tech stack bắt buộc:** .NET/C# và React/TypeScript  
**Loại tài liệu:** Product scope + compliance blueprint + solution architecture + delivery plan

> Tài liệu này là bản thiết kế sản phẩm và kỹ thuật, không phải ý kiến tư vấn pháp lý hoặc tư vấn thuế. Trước khi phát hành chức năng kê khai, hóa đơn, tính lương hoặc ký hợp đồng điện tử, cần luật sư/kế toán thuế xác nhận rule theo loại hình pháp nhân và địa phương triển khai.

---

## 1. Kết luận điều hành

Sản phẩm nên được xây dựng như một SaaS đa tenant phục vụ hai nhóm:

1. Giáo viên/hộ kinh doanh dạy tại nhà: ít lớp, tự dạy, tự thu, cần thao tác cực nhanh trên điện thoại.
2. Trung tâm dạy thêm: nhiều cơ sở, giáo viên, phòng học, cần phân quyền, đối soát, lương, hóa đơn, báo cáo và kiểm soát tuân thủ.

MVP không nên chỉ sao chép các màn hình tham khảo. Điểm khác biệt cần ưu tiên là:

- Sổ tài chính có truy vết, không cho sửa/xóa tùy tiện các giao dịch đã chốt.
- Học phí theo buổi hoặc tháng, cho phép thu nhiều lần, phân bổ tiền rõ ràng.
- Lịch học, phòng học, giáo viên và điểm danh nằm trong một luồng nghiệp vụ thống nhất.
- Bảo vệ dữ liệu trẻ em theo hướng privacy-by-design.
- Bộ rule pháp lý/thuế có phiên bản hiệu lực, không hard-code con số vào giao diện.
- Trang công khai thông tin dạy thêm, hồ sơ giáo viên và checklist tuân thủ theo Thông tư 29.
- Parent portal an toàn; không dùng một link công khai cho phép xem danh sách hoặc tình trạng của cả lớp.

Kiến trúc khuyến nghị là **modular monolith** trên .NET 10 LTS, React 19 + TypeScript, PostgreSQL, Redis và object storage. Chỉ tách dịch vụ xử lý file đề thi hoặc thông báo khi tải thực tế yêu cầu.

Thời gian hợp lý cho bản production đầu tiên với đội 6–7 người là **24 tuần**, sau đó pilot 4–6 tuần. Nếu chỉ có 2–3 người, cần 9–12 tháng hoặc cắt phạm vi P1/P2.

---

## 2. Phạm vi nghiên cứu từ sản phẩm tham khảo

### 2.1. Chức năng quan sát trực tiếp từ ảnh

Thanh điều hướng thể hiện các phân hệ:

- Tổng quan.
- Học sinh.
- Lớp học.
- Môn học.
- Lịch dạy.
- Trộn đề thi.
- Học phí.
- Chi phí.
- Báo cáo.
- Khai thuế.
- Giáo viên.
- Phòng học.
- Lương giáo viên.

Các màn hình chi tiết thể hiện:

- Điểm danh theo buổi, chọn giáo viên dạy buổi đó, lưu trạng thái từng học sinh, ghi nhận vắng có/không phép.
- Học phí theo buổi hoặc theo tháng; tổng phải thu, đã thu, còn phải thu, số phiếu chưa đủ; thu một phần/nhiều lần.
- Lịch tuần/tháng/danh sách, phân màu theo lớp.
- Báo cáo doanh thu, chi phí, lợi nhuận, phiếu thu và doanh thu theo lớp/giáo viên.
- Màn hình thuế theo quý/năm, so sánh các cách kê khai, tải tờ khai dự thảo.
- Lương giáo viên theo số tiết, tiền tiết, phụ cấp, trạng thái thanh toán.

### 2.2. Chức năng bổ sung xác nhận từ trang giới thiệu công khai

- Tạo hàng loạt buổi học từ thời khóa biểu.
- Buổi bù, tăng cường, đổi giáo viên dạy thay.
- Giáo viên có tài khoản riêng và chỉ thấy lớp được phân công.
- Tách quyền điểm danh, thu tiền, xem số điện thoại phụ huynh.
- Phụ huynh xem điểm danh qua liên kết.
- Phòng học và cảnh báo trùng phòng/trùng giờ.
- Lương theo tiết, thưởng, khấu trừ và phiếu lương.
- Trộn đề từ DOCX, nhiều mã đề, đáp án Excel, giữ hình ảnh/công thức.
- Dữ liệu mẫu để trải nghiệm.
- Giao diện responsive/PWA cho điện thoại.
- Gói thuê bao, dùng thử và chương trình giới thiệu.

### 2.3. Nguyên tắc không sao chép

- Chỉ tham khảo bài toán và luồng nghiệp vụ, không sao chép mã nguồn, nhãn hiệu, nội dung, hình ảnh hoặc pixel-perfect UI.
- Thiết kế hệ thống riêng, có design system và nhận diện độc lập.
- Chức năng thuế trong ảnh chỉ là gợi ý UX; công thức phải được xác nhận lại theo văn bản hiệu lực tại ngày tính.

---

## 3. Đối tượng sử dụng và vai trò

### 3.1. Vai trò nghiệp vụ

| Vai trò | Phạm vi mặc định |
|---|---|
| Chủ hộ/Chủ trung tâm | Toàn bộ tenant, tài chính, thuế, cấu hình, phân quyền |
| Quản lý cơ sở | Dữ liệu một hoặc nhiều cơ sở được giao; không mặc định xem thuế toàn hệ thống |
| Kế toán/Thu ngân | Học phí, phiếu thu, chi phí, hóa đơn, đối soát, báo cáo tài chính được giao |
| Điều phối viên | Học sinh, lớp, lịch, phòng, phân công giáo viên |
| Giáo viên | Lớp/buổi được phân công, điểm danh, ghi chú học tập; quyền nhạy cảm được bật riêng |
| Trợ giảng | Điểm danh/ghi chú theo phạm vi được giao; không xem tài chính |
| Phụ huynh/Người giám hộ | Chỉ dữ liệu của học sinh được liên kết |
| Học sinh | Lịch, điểm danh, học liệu, công nợ theo chính sách và độ tuổi |
| Auditor/Thanh tra nội bộ | Chỉ đọc báo cáo, audit log và hồ sơ tuân thủ |
| System operator | Vận hành hạ tầng; không mặc định đọc nội dung tenant |

### 3.2. Mô hình phân quyền

Không chỉ dùng role cố định. Áp dụng RBAC + scope:

- Permission: `student.read`, `guardian.phone.read`, `attendance.write`, `payment.collect`, `finance.report.read`, `tax.manage`, `payroll.approve`...
- Scope: tenant, campus, class, assigned-session, own-student.
- Quyền nhạy cảm cần explicit grant: số điện thoại, địa chỉ, ghi chú sức khỏe, xuất Excel, tài chính, thuế.
- Thao tác rủi ro cao dùng step-up authentication: hoàn tiền, hủy phiếu, xuất toàn bộ học sinh, đổi tài khoản ngân hàng, chốt lương.
- Mỗi lần thay quyền phải ghi audit log và thông báo chủ tenant.

---

## 4. Product principles

1. **Mobile-first cho tác vụ tại lớp:** điểm danh và thu tiền hoàn tất trong tối đa 3 thao tác chính.
2. **Một nguồn dữ liệu:** lịch đã diễn ra tạo căn cứ điểm danh, học phí theo buổi và lương giáo viên.
3. **Không âm thầm sửa lịch sử:** giao dịch tài chính đã phát hành chỉ được void/adjust/refund bằng chứng từ đối ứng.
4. **Legal-by-configuration:** rule thuế, biểu mẫu, hạn nộp và yêu cầu địa phương có phiên bản hiệu lực.
5. **Privacy-by-default:** giáo viên không thấy dữ liệu không cần cho việc dạy; phụ huynh chỉ thấy con mình.
6. **Explainable calculation:** mọi số tiền có màn hình giải thích công thức và dữ liệu nguồn.
7. **Progressive complexity:** giáo viên cá nhân không bị làm phiền bởi module trung tâm; module được bật theo capability/gói.
8. **Vietnam-first:** múi giờ Asia/Ho_Chi_Minh, tiền VND dạng số nguyên, tên có dấu, số điện thoại +84, ngày dd/MM/yyyy.

---

## 5. Phạm vi chức năng cực chi tiết

### 5.1. Onboarding, tenant và cấu hình ban đầu

**P0**

- Đăng ký bằng số điện thoại/email; OTP; thiết lập mật khẩu và MFA tùy chọn.
- Tạo tenant theo loại: giáo viên cá nhân, hộ kinh doanh, doanh nghiệp/trung tâm.
- Chọn tỉnh/thành, múi giờ, năm học, cơ sở chính, tên hiển thị.
- Khai báo thông tin pháp lý: tên đăng ký, mã số thuế, mã hộ kinh doanh/doanh nghiệp, địa chỉ, người đại diện.
- Wizard thiết lập: môn học → giáo viên → phòng → lớp → học sinh → biểu phí → thời khóa biểu.
- Import từ mẫu Excel/CSV với dry-run, mapping cột, kiểm tra trùng và báo lỗi theo dòng.
- Tạo dữ liệu mẫu trong tenant sandbox; xóa dữ liệu mẫu độc lập với dữ liệu thật.
- Checklist tiến độ onboarding và hướng dẫn theo ngữ cảnh.

**P1**

- Nhiều cơ sở/campus.
- Sao chép cấu hình năm học cũ sang năm học mới.
- Branding: logo, màu, mẫu phiếu thu.
- Subscription, trial, quota, gia hạn và hóa đơn SaaS.

**Acceptance criteria**

- Người dùng mới có thể tạo lớp đầu tiên trong 10 phút.
- Import 2.000 học sinh không tạo bản ghi trùng nếu chạy lại cùng file/idempotency key.
- Tenant A không thể đọc bất kỳ record nào của tenant B kể cả khi đoán ID.

### 5.2. Tổng quan

**P0**

- KPI hôm nay: số buổi, học sinh dự kiến, đã/chưa điểm danh, giáo viên/phòng đang hoạt động.
- KPI tài chính theo kỳ: phải thu, đã thu, nợ, chi phí, số phiếu quá hạn.
- Việc cần làm: điểm danh chưa lưu, phiếu sắp đến hạn, lớp chưa phân giáo viên, xung đột lịch.
- Biểu đồ doanh thu thực thu, công nợ, chuyên cần.
- Bộ lọc cơ sở, khoảng thời gian, lớp, giáo viên.
- Drill-down từ KPI đến danh sách nguồn.

**P1**

- Forecast dòng tiền và sĩ số.
- Cảnh báo biến động: chuyên cần giảm, công nợ tăng, lớp sắp vượt sức chứa.
- Dashboard tùy biến theo vai trò.

### 5.3. Học sinh và người giám hộ

**P0**

- Hồ sơ học sinh: mã nội bộ, họ tên, ngày sinh, giới tính tùy chọn, trường, khối, lớp chính khóa, trạng thái.
- Tách hồ sơ người giám hộ: họ tên, quan hệ, số điện thoại, email, địa chỉ liên lạc.
- Một người giám hộ có nhiều học sinh; một học sinh có nhiều người giám hộ.
- Chỉ định người nhận thông báo, người thanh toán chính, người có quyền đón trẻ.
- Liên kết học sinh với lớp bằng enrollment có ngày bắt đầu/kết thúc.
- Lịch sử chuyển lớp, bảo lưu, nghỉ học, quay lại.
- Tìm kiếm không dấu, theo số điện thoại, mã học sinh, trường/lớp.
- Timeline: ghi danh, điểm danh, học phí, biên nhận, ghi chú có phân quyền.
- Cảnh báo bản ghi trùng theo tên + ngày sinh + người giám hộ.
- Export theo quyền; watermark và audit khi xuất dữ liệu.

**Dữ liệu nhạy cảm**

- Thông tin sức khỏe, dị ứng, nhu cầu đặc biệt để vùng dữ liệu riêng, mã hóa cột và giới hạn quyền.
- Không thu CCCD, ảnh giấy tờ, sinh trắc học nếu không có mục đích hợp pháp rõ ràng.
- Ảnh học sinh là tùy chọn; consent riêng cho mục đích vận hành và mục đích truyền thông.

**P1**

- Tag/nhóm, lead chờ xếp lớp, waitlist.
- Hồ sơ học tập: nhận xét, mục tiêu, tiến bộ.
- Phụ huynh tự cập nhật thông tin qua form có xác minh.

### 5.4. Môn học, chương trình và lớp học

**P0**

- Danh mục môn học, khối lớp, màu hiển thị.
- Lớp có mã, tên, môn, khối, cơ sở, phòng mặc định, giáo viên phụ trách, trợ giảng.
- Ngày hoạt động, sĩ số tối đa, trạng thái nháp/đang tuyển/đang học/kết thúc.
- Hình thức: trực tiếp, trực tuyến, hybrid.
- Biểu phí mặc định: theo buổi/tháng/gói; cho phép override theo học sinh có lý do.
- Danh sách học sinh hiện tại và lịch sử enrollment.
- Trang lớp tổng hợp: lịch, sĩ số, điểm danh, học phí, giáo viên, tài liệu.
- Kiểm tra điều kiện dạy thêm theo độ tuổi/cấp học và cảnh báo compliance.

**P1**

- Khóa học/chương trình gồm nhiều lớp hoặc cấp độ.
- Xếp lớp dựa trên kiểm tra đầu vào.
- Clone lớp và kết chuyển năm học.

### 5.5. Lịch dạy, thời khóa biểu và phòng học

**P0**

- Rule thời khóa biểu lặp theo thứ, giờ bắt đầu/kết thúc, ngày hiệu lực.
- Sinh session hàng loạt theo khoảng ngày; bỏ ngày nghỉ/lịch nghỉ tùy chọn.
- Chế độ xem tuần, tháng, agenda/danh sách và lịch cá nhân.
- Buổi học có trạng thái: Scheduled → Confirmed → InProgress → Completed/Cancelled.
- Buổi bù liên kết buổi bị hủy; buổi tăng cường có loại riêng.
- Đổi giờ/phòng/giáo viên một buổi hoặc từ một ngày trở đi.
- Phân công giáo viên dạy thay và lưu người phụ trách gốc.
- Cảnh báo trùng giáo viên, trùng phòng, ngoài giờ cơ sở, vượt sức chứa.
- Khóa sửa session đã chốt điểm danh/lương; thay đổi phải qua adjustment workflow.
- Đồng bộ thông báo thay đổi đến giáo viên/phụ huynh.

**P1**

- Drag & drop có kiểm tra xung đột phía server.
- Calendar feed iCal; đồng bộ Google/Outlook sau khi có consent.
- Quản lý phòng: cơ sở, sức chứa, thiết bị, trạng thái bảo trì.
- Đặt phòng cho hoạt động ngoài lớp.

**Cạnh biên bắt buộc**

- Qua nửa đêm, đổi múi giờ, daylight-saving của calendar ngoài Việt Nam.
- Một giáo viên dạy hai lớp online cùng lúc phải vẫn cảnh báo, cho phép override có lý do.
- Xóa rule thời khóa biểu không được xóa session đã diễn ra.

### 5.6. Điểm danh

**P0**

- Mở từ lịch hôm nay hoặc trang lớp.
- Trạng thái: có mặt, vắng có phép, vắng không phép, đi muộn, học bù, chưa xác định.
- “Chọn tất cả có mặt” rồi sửa ngoại lệ.
- Ghi chú từng học sinh và ghi chú cả buổi.
- Ghi nhận giáo viên thực dạy; là nguồn cho payroll.
- Auto-save nháp, explicit submit và timestamp người lưu.
- Sau submit có thời gian cho phép chỉnh; quá thời gian cần quyền và lý do.
- Audit trước/sau cho mọi thay đổi.
- Nếu lớp thu theo buổi, attendance là nguồn tạo charge theo policy đã cấu hình.
- Nếu vắng vẫn tính phí/không tính phí/học bù: policy theo lớp hoặc học sinh, không suy diễn.

**P1**

- Offline-first/PWA cho mạng yếu, đồng bộ có conflict resolution.
- QR check-in chỉ là tùy chọn; không dùng nhận diện khuôn mặt ở MVP.
- Thông báo vắng/đi muộn cho người giám hộ.

**Acceptance criteria**

- Điểm danh lớp 30 em trên điện thoại dưới 60 giây.
- Submit hai lần không tạo hai charge.
- Nếu hai người sửa cùng lúc, server phát hiện version conflict thay vì ghi đè im lặng.

### 5.7. Học phí, công nợ, phiếu thu và thanh toán

Đây là bounded context quan trọng nhất; cần thiết kế như ledger, không phải CRUD đơn giản.

**Khái niệm**

- Fee plan: quy tắc giá.
- Charge: nghĩa vụ phải thu.
- Invoice/payment request: chứng từ/yêu cầu thanh toán.
- Receipt: xác nhận đã nhận tiền.
- Payment: dòng tiền thực nhận.
- Allocation: phân bổ payment vào charge.
- Credit note/refund: điều chỉnh/hoàn tiền.

**P0**

- Học phí theo buổi: số buổi tính phí × đơn giá; policy vắng/học bù rõ ràng.
- Học phí theo tháng: fixed amount hoặc theo số buổi dự kiến/thực tế.
- Học phí theo gói: số buổi/credit có ngày hết hạn.
- Phí khác: giáo trình, kiểm tra, cơ sở vật chất; cấu hình chịu thuế/không chịu thuế riêng.
- Giảm giá/học bổng/miễn giảm có lý do, người duyệt và khoảng hiệu lực.
- Tạo phiếu hàng loạt theo lớp/tháng; preview trước khi commit.
- Thu đủ, thu một phần, thu nhiều lần.
- Phương thức: tiền mặt, chuyển khoản, QR, cổng thanh toán, khác.
- Mã giao dịch và bằng chứng chuyển khoản; chống trùng.
- Phân bổ một payment cho nhiều charge hoặc nhiều payment cho một charge.
- Trạng thái: Draft, Issued, PartiallyPaid, Paid, Overdue, Voided, Refunded.
- In/tải phiếu thu PDF có số chứng từ duy nhất.
- Không xóa phiếu đã phát hành; void có lý do và chứng từ thay thế.
- Khóa kỳ tài chính; mở khóa cần phê duyệt.
- Nhắc nợ theo template và kênh được đồng ý.
- Import sao kê ngân hàng và màn hình đối soát thủ công bán tự động.

**P1**

- VietQR động; webhook cổng thanh toán có signature validation và idempotency.
- Tách tiền thu hộ/chi hộ.
- Hóa đơn điện tử qua nhà cung cấp được cấp phép; không tự phát hành chuẩn thuế nếu chưa tích hợp hợp lệ.
- Kế hoạch trả góp và auto-allocation.
- Hoàn tiền một phần/toàn phần.

**Quy tắc kế toán kỹ thuật**

- Tiền VND lưu bằng `long/bigint`, không dùng float/double.
- Mọi post tài chính trong transaction database.
- `Idempotency-Key` bắt buộc cho collect/refund/webhook.
- Receipt number được cấp phía server và unique trong tenant/kỳ.
- Tách `occurred_at`, `recorded_at`, `value_date`.
- Báo cáo “doanh thu thực thu” không đồng nhất với “doanh thu phát sinh”; phải ghi nhãn rõ.

### 5.8. Chi phí

**P0**

- Danh mục chi phí: thuê nhà, điện nước, lương, giáo trình, marketing, thiết bị, khác.
- Chi phí định kỳ hoặc một lần.
- Phân bổ theo cơ sở/lớp/giáo viên/cost center.
- Nhà cung cấp, số hóa đơn/chứng từ, ngày chứng từ, ngày thanh toán.
- Upload file; hash và audit.
- Trạng thái nháp/chờ duyệt/đã duyệt/đã thanh toán/hủy.
- Quy trình maker-checker tùy ngưỡng tiền.
- Chi phí được trừ/không được trừ chỉ là phân loại hỗ trợ, cần kế toán xác nhận.

**P1**

- OCR chứng từ với bước xác nhận người dùng.
- Ngân sách tháng và cảnh báo vượt.
- Khấu hao tài sản nhỏ nếu phạm vi yêu cầu.

### 5.9. Báo cáo và phân tích

**P0**

- Doanh thu thực thu theo ngày/tháng/quý/năm.
- Doanh thu phát sinh, đã thu và công nợ tách riêng.
- Chi phí và lợi nhuận quản trị.
- Doanh thu theo lớp, môn, giáo viên, cơ sở.
- Phiếu thu theo trạng thái/phương thức.
- Chuyên cần theo học sinh/lớp/giáo viên.
- Số buổi/tiết đã dạy làm căn cứ lương.
- Export CSV/XLSX/PDF theo quyền.
- Mọi con số có drill-down và mô tả cách tính.
- Timezone và cutoff nhất quán.

**P1**

- Cohort giữ chân học sinh.
- Công suất phòng, tỷ lệ lấp đầy lớp.
- AR aging: 0–7, 8–30, 31–60, >60 ngày.
- So sánh budget/actual, forecast.
- Snapshot báo cáo theo kỳ đã khóa.

### 5.10. Giáo viên và nhân sự

**P0**

- Hồ sơ giáo viên: thông tin liên hệ, môn có thể dạy, loại cộng tác/hợp đồng, cơ sở.
- Hồ sơ năng lực/chứng chỉ; ngày hết hạn; tệp đính kèm được bảo vệ.
- Thông tin đang công tác tại trường nào để chạy rule compliance.
- Tài khoản riêng; invitation có hạn; bắt đổi mật khẩu/MFA.
- Phân công lớp và lịch khả dụng.
- Ghi nhận báo cáo với hiệu trưởng/người đứng đầu đơn vị theo Mẫu 03 hoặc biểu mẫu cập nhật.
- Trạng thái active/on-leave/terminated; revoke session ngay khi nghỉ.

**P1**

- Hợp đồng, phụ lục, chữ ký điện tử qua nhà cung cấp.
- Đánh giá, đào tạo, hồ sơ sự cố.
- Timesheet ngoài giờ dạy.

### 5.11. Lương giáo viên

**P0**

- Cấu hình pay scheme: theo tiết, theo buổi, theo giờ, fixed monthly, tỷ lệ doanh thu, kết hợp.
- Rate có ngày hiệu lực; không sửa hồi tố im lặng.
- Session completed + giáo viên thực dạy là nguồn tính.
- Phụ cấp, thưởng, khấu trừ với lý do và chứng từ.
- Payroll period: Draft → Calculated → Reviewed → Approved → Paid → Locked.
- Màn hình giải thích từng dòng: session nào, rate nào, số tiền nào.
- Phiếu lương PDF và xác nhận đã nhận.
- Không cho cùng một session trả hai lần.

**P1**

- Tính/khấu trừ PIT, BHXH theo loại hợp đồng sau khi tư vấn pháp lý.
- Bank transfer batch file.
- Điều chỉnh kỳ sau thay vì sửa kỳ đã khóa.

### 5.12. Thuế và tuân thủ

Không biến module này thành “máy tư vấn pháp luật”. Sản phẩm cung cấp dữ liệu, rule có nguồn và tờ khai **dự thảo**, người nộp thuế chịu trách nhiệm rà soát/nộp.

**P0**

- Tax profile: hộ kinh doanh/cá nhân/doanh nghiệp; mã số thuế; ngành nghề; phương pháp tính; kỳ khai.
- Tax rule version theo `effective_from/effective_to`, loại đối tượng, ngưỡng doanh thu, thuế suất, nguồn văn bản.
- Tổng hợp doanh thu theo kỳ và phân loại nguồn thu.
- Phân biệt hoạt động dạy học thuộc đối tượng không chịu VAT với hàng hóa/dịch vụ phụ trợ cần đánh giá riêng.
- Mô phỏng phương án PIT được pháp luật cho phép; hiển thị đầu vào và giả định.
- Checklist hồ sơ, hạn nộp, trạng thái nháp/đã rà soát/đã nộp.
- Xuất workbook/PDF/Word dự thảo theo template version.
- Đính kèm bằng chứng nộp; không tự ghi “đã nộp” nếu chưa có xác nhận.
- Cảnh báo khi rule pháp lý hết hiệu lực hoặc chưa được cập nhật.
- Banner “cần tư vấn” khi loại hình/nguồn thu nằm ngoài rule đã kiểm chứng.

**P1**

- Tích hợp hóa đơn điện tử và trạng thái cơ quan thuế qua nhà cung cấp hợp lệ.
- Reconciliation giữa doanh thu app, hóa đơn, sao kê và số kê khai.
- Lịch tuân thủ theo địa phương và loại hình.

**Các con số phải xác nhận tại thời điểm triển khai**

- Từ 01/07/2026, Luật Thuế TNCN 109/2025/QH15 có hiệu lực.
- Theo hướng dẫn 2026 hiện có, cá nhân cư trú kinh doanh doanh thu năm không quá 500 triệu đồng không phải nộp PIT; trên 500 triệu đến 3 tỷ đồng có các phương án tính theo luật. Dịch vụ thường có tỷ lệ 2% trên phần doanh thu tính thuế nếu chọn phương pháp theo doanh thu; phương án theo thu nhập dùng tỷ lệ khác. Không dùng cố định “1,5%” cho dạy thêm.
- Hoạt động dạy học/dạy nghề theo pháp luật giáo dục thuộc đối tượng không chịu VAT; dịch vụ/hàng hóa phụ trợ không được mặc nhiên gộp cùng.
- Thuế và biểu mẫu thay đổi nhanh: production phải có legal owner, changelog, four-eyes approval và ngày hiệu lực.

### 5.13. Hồ sơ tuân thủ dạy thêm

**P0**

- Compliance profile cho từng cơ sở.
- Checklist đăng ký kinh doanh, mã số thuế, địa điểm, giáo viên, công khai mức thu.
- Public disclosure page theo dữ liệu bắt buộc: môn, khối, thời lượng, địa điểm, hình thức, thời gian, người dạy, mức thu.
- Lịch sử phiên bản công khai; PDF snapshot; ngày bắt đầu/kết thúc.
- Xuất Mẫu 02 và Mẫu 03 theo template version.
- Rule/cảnh báo:
  - Không tổ chức dạy thêm có thu tiền đối với học sinh tiểu học, trừ nghệ thuật, thể thao, kỹ năng sống theo quy định.
  - Giáo viên đang dạy tại trường không được dạy thêm có thu tiền ngoài trường cho chính học sinh mình đang được phân công dạy.
  - Giáo viên trường công có thể bị hạn chế vai trò quản lý/điều hành hoạt động dạy thêm ngoài trường theo quy định.
  - Giáo viên tham gia dạy ngoài trường cần báo cáo người đứng đầu trường theo nội dung yêu cầu.
- Rule là cảnh báo/block có override được kiểm soát; tránh khẳng định quan hệ “học sinh chính khóa của giáo viên” nếu dữ liệu chưa đủ.
- Upload biên nhận đăng ký, báo cáo, công khai; nhắc ngày hết hạn nếu có.

**P1**

- Rule pack riêng theo tỉnh/thành khi địa phương có quy định bổ sung.
- Trang audit export dành cho kiểm tra nội bộ.

### 5.14. Parent/student portal và thông báo

**P0**

- Đăng nhập/OTP qua số điện thoại đã xác minh.
- Phụ huynh xem lịch, điểm danh, học phí, biên nhận của đúng học sinh được liên kết.
- Một phụ huynh chuyển nhanh giữa các con.
- Xác nhận thông báo, gửi phản hồi/yêu cầu sửa dữ liệu.
- Template SMS/email/Zalo/app có biến; log trạng thái gửi.
- Consent và unsubscribe theo kênh/mục đích, trừ thông báo vận hành bắt buộc được pháp luật cho phép.

**Thiết kế link chia sẻ an toàn**

- Không hiển thị danh sách cả lớp từ một URL công khai.
- Magic link phải là token ngẫu nhiên >=128 bit, hash trong DB, hết hạn, thu hồi được, giới hạn một học sinh/mục đích.
- Với dữ liệu tài chính hoặc lịch sử dài, yêu cầu OTP/đăng nhập.
- Chặn index/search engine, rate limit và theo dõi truy cập bất thường.

**P1**

- Push notification/PWA.
- Xin nghỉ, đăng ký học bù, xác nhận công nợ.

### 5.15. Trộn đề thi

Nên tách process worker vì xử lý DOCX có rủi ro bảo mật và tài nguyên.

**P1**

- Upload `.docx`, giới hạn dung lượng và loại MIME thực tế.
- Antivirus scan, zip-bomb protection, sandbox conversion.
- Preview câu hỏi nhận diện được; người dùng xác nhận cấu trúc.
- Trộn thứ tự câu/đáp án theo rule, sinh nhiều mã đề.
- Giữ công thức, hình ảnh và style trong phạm vi hỗ trợ; báo rõ phần không hỗ trợ.
- Tạo file đề, đáp án và bảng mapping.
- Job async, progress, retry có giới hạn.
- File lưu riêng tenant, URL tải có hạn, tự xóa sau retention mặc định 24 giờ.
- Điều khoản xác nhận quyền sử dụng nội dung; không dùng nội dung để huấn luyện mô hình.

**Không đưa vào MVP nếu nguồn lực hạn chế**, vì đây là sản phẩm con có complexity lớn và không nằm trên critical path quản lý lớp/tài chính.

### 5.16. SaaS subscription, giới thiệu và system admin

**P1**

- Plan/capability/quota thay vì rải điều kiện plan trong code.
- Trial, gia hạn, grace period, downgrade có preview ảnh hưởng.
- Referral code, attribution, commission ledger, anti-fraud, payout approval.
- Support impersonation chỉ khi tenant chấp thuận, có thời hạn, banner rõ và audit.
- System admin: tenant health, job status, feature flags, legal rule deployment.
- Không hiển thị nội dung nhạy cảm của tenant cho operator nếu không có break-glass workflow.

---

## 6. Các state machine bắt buộc

### 6.1. Buổi học

`Draft → Scheduled → Confirmed → InProgress → Completed`

Nhánh: `Scheduled/Confirmed → Cancelled → Rescheduled`.

- Completed mới được dùng chốt lương.
- Attendance chưa submit thì không được Completed, trừ override có lý do.
- Cancelled không tạo lương; charge xử lý theo fee policy.

### 6.2. Phiếu học phí

`Draft → Issued → PartiallyPaid → Paid → Closed`

Nhánh: `Issued/PartiallyPaid → Voided`; `Paid → PartiallyRefunded/Refunded`.

- Không chuyển ngược bằng update trực tiếp.
- Mỗi transition có command, permission và audit riêng.

### 6.3. Kỳ lương

`Draft → Calculated → Reviewed → Approved → Paid → Locked`.

- Recalculate chỉ trước Approved.
- Sau Locked, điều chỉnh ghi vào kỳ sau.

### 6.4. Tờ khai dự thảo

`Draft → DataValidated → Reviewed → Exported → SubmittedByUser → Accepted/Rejected`.

- App không tự gán Submitted/Accepted nếu không có bằng chứng hoặc callback tin cậy.

---

## 7. Kiến trúc hệ thống

### 7.1. Quyết định chính

**Chọn modular monolith**, một deployable API nhưng code chia module và schema rõ. Lý do:

- Giao dịch lịch–điểm danh–học phí–lương cần consistency.
- Đội nhỏ dễ phát triển, test, deploy và quan sát.
- Tách microservice sớm làm tăng chi phí distributed transaction, observability và vận hành.
- Dùng outbox/event từ đầu để có đường tách module sau này.

### 7.2. Tech stack đề xuất

**Backend**

- .NET 10 LTS, C# hiện hành.
- ASP.NET Core Web API, controllers hoặc endpoint groups; OpenAPI.
- EF Core cho transaction; Dapper chỉ cho query báo cáo đã đo lường.
- FluentValidation hoặc validation layer tương đương.
- ASP.NET Core Identity hoặc IdP OIDC tiêu chuẩn; JWT access ngắn + rotating refresh token.
- Hangfire/Quartz cho job; transactional outbox.
- ClosedXML/Open XML SDK cho Excel/DOCX; QuestPDF hoặc engine có license phù hợp cho PDF.

**Frontend**

- React 19 + TypeScript strict.
- Vite.
- React Router.
- TanStack Query cho server state.
- React Hook Form + Zod cho form/schema client.
- Design system dựa trên accessible primitives; không sao chép UI tham khảo.
- i18n từ đầu, mặc định vi-VN.
- PWA cho teacher workflow ở P1.

**Data/infrastructure**

- PostgreSQL, schema migration có review.
- Redis cho cache, distributed lock có giới hạn và rate limit.
- S3-compatible object storage; encryption, signed URL.
- OpenTelemetry + centralized logs/metrics/traces.
- Docker; dev orchestration bằng Aspire nếu đội quen hệ sinh thái .NET.
- Managed cloud services ưu tiên hơn tự vận hành.

### 7.3. Module backend

```text
Identity & Access
Tenant & Subscription
Organization & Campus
Students & Guardians
Academics (Subjects, Classes, Enrollments)
Scheduling & Rooms
Attendance
Billing & Payments
Expenses
Teachers & Contracts
Payroll
Reporting
Tax & Compliance
Notifications
Documents & Exam Processing
Audit & Data Rights
```

Mỗi module có:

- Domain model và invariants.
- Application commands/queries.
- API contract.
- Persistence mapping.
- Authorization policy.
- Events/outbox handlers.
- Unit/integration tests.

Không cho module khác truy cập trực tiếp DbSet nội bộ; dùng application interface hoặc event.

### 7.4. Luồng sự kiện tiêu biểu

```text
SessionCompleted
  ├─> Attendance validates final roster
  ├─> Billing creates per-session charges idempotently
  ├─> Payroll creates teacher earning item idempotently
  ├─> Reporting updates read model
  └─> Notifications sends guardian summary if enabled
```

Outbox record được ghi cùng transaction với business change. Worker publish/handle với inbox deduplication.

### 7.5. Multi-tenancy

- Shared database, shared schema cho giai đoạn đầu; mọi aggregate có `TenantId`.
- Global query filter chỉ là lớp bảo vệ đầu, không phải lớp duy nhất.
- Repository yêu cầu tenant context; integration test cố tình truy cập chéo tenant.
- Unique index luôn gồm `TenantId` nếu uniqueness thuộc tenant.
- File path không dùng tên người dùng; dùng tenant-id opaque/object-id.
- Có lộ trình PostgreSQL Row-Level Security sau khi benchmark.
- Tenant lớn có thể chuyển database riêng trong tương lai qua abstraction.

### 7.6. API conventions

- Base `/api/v1`.
- Problem Details RFC 9457 cho lỗi.
- Cursor pagination cho timeline/giao dịch; page pagination cho danh mục nhỏ.
- ETag/row version cho optimistic concurrency.
- `Idempotency-Key` cho mutation tài chính/import/webhook.
- Correlation ID xuyên suốt.
- UTC trong storage; convert Asia/Ho_Chi_Minh ở boundary.
- OpenAPI là contract; generated client types hoặc schema validation.

### 7.7. API groups dự kiến

```text
/auth, /me, /tenants, /campuses
/students, /guardians, /enrollments
/subjects, /classes, /fee-plans
/timetables, /sessions, /rooms
/sessions/{id}/attendance
/charges, /payment-requests, /payments, /receipts, /refunds
/expenses, /vendors
/teachers, /assignments, /teacher-documents
/payroll-periods, /payslips
/reports/*
/tax-profiles, /tax-rules, /tax-periods, /tax-drafts
/compliance/disclosures, /compliance/checklists
/notifications, /templates, /consents
/portal/*
/exam-jobs
/audit-events, /data-subject-requests
```

---

## 8. Mô hình dữ liệu mức logic

### 8.1. Identity và tenant

- `Tenant`, `Subscription`, `Plan`, `Capability`, `TenantCapability`.
- `User`, `UserIdentity`, `Session`, `MfaMethod`.
- `Membership`, `Role`, `Permission`, `RolePermission`, `MembershipScope`.
- `Campus`, `OrganizationLegalProfile`, `BankAccount`.

### 8.2. Academic

- `Student`, `Guardian`, `StudentGuardian`.
- `Subject`, `GradeLevel`, `Class`, `Enrollment`.
- `Teacher`, `TeacherQualification`, `TeacherSchoolEmployment`.
- `Room`, `TimetableRule`, `TeachingSession`, `TeacherAssignment`.
- `AttendanceRecord`, `AttendanceRevision`.

### 8.3. Billing

- `FeePlan`, `FeePlanRule`, `StudentFeeOverride`.
- `Charge`, `ChargeLine`, `Discount`, `Scholarship`.
- `PaymentRequest`, `Payment`, `PaymentAllocation`.
- `Receipt`, `CreditNote`, `Refund`.
- `FinancialPeriod`, `BankStatement`, `ReconciliationMatch`.

### 8.4. Expense/payroll

- `Expense`, `ExpenseCategory`, `Vendor`, `ExpenseApproval`.
- `PayScheme`, `PayRateVersion`, `PayrollPeriod`, `PayrollEntry`, `PayrollLine`, `Payslip`, `PayrollPayment`.

### 8.5. Tax/compliance

- `TaxProfile`, `TaxRuleSet`, `TaxRuleVersion`, `TaxPeriod`, `TaxCalculation`, `TaxDraft`, `TaxSubmissionEvidence`.
- `ComplianceRequirement`, `ComplianceCase`, `ComplianceDocument`.
- `PublicDisclosure`, `DisclosureVersion`, `TeacherExternalTeachingReport`.

### 8.6. Cross-cutting

- `Consent`, `PrivacyNoticeVersion`, `ConsentEvidence`.
- `Notification`, `NotificationDelivery`, `TemplateVersion`.
- `Document`, `DocumentRetentionPolicy`, `ExamJob`, `ExamOutput`.
- `AuditEvent`, `SecurityEvent`, `OutboxMessage`, `InboxMessage`.
- `DataSubjectRequest`, `DeletionJob`, `LegalHold`.

### 8.7. Trường dữ liệu chuẩn

Mọi aggregate chính:

```text
Id (UUID/ULID)
TenantId
CreatedAtUtc, CreatedBy
UpdatedAtUtc, UpdatedBy
RowVersion
Status
```

Record tài chính bổ sung:

```text
OccurredAtUtc
RecordedAtUtc
Currency = VND
AmountMinor (bigint)
SourceType, SourceId
PostedAtUtc, VoidedAtUtc
```

---

## 9. Yêu cầu pháp lý và cách chuyển thành yêu cầu sản phẩm

### 9.1. Dạy thêm, học thêm

**Căn cứ chính:** Thông tư 29/2024/TT-BGDĐT, hiệu lực 14/02/2025.

| Nghĩa vụ/rủi ro | Yêu cầu sản phẩm |
|---|---|
| Cơ sở ngoài nhà trường có thu tiền phải đăng ký kinh doanh | Bắt buộc khai legal profile; checklist và lưu bằng chứng |
| Công khai thông tin trước tuyển sinh | Public disclosure page, versioning, PDF snapshot, Mẫu 02 |
| Người dạy có đạo đức và chuyên môn phù hợp | Hồ sơ năng lực, môn được phép phân công, ngày hết hạn chứng chỉ |
| Giáo viên đang công tác tại trường phải báo cáo | Sinh Mẫu 03, tracking đã gửi/xác nhận |
| Hạn chế với học sinh tiểu học | Rule theo grade/subject/category; block hoặc warning có kiểm soát |
| Không thu tiền chính học sinh đang dạy chính khóa | Lưu school/class relationship và conflict check |
| Giáo viên trường công không được quản lý/điều hành cơ sở ngoài trường theo quy định | Cảnh báo role conflict trong legal owner/manager setup |

Không áp dụng cứng quy định cũ đã hết hiệu lực. Rule địa phương phải có nguồn, ngày hiệu lực và review owner.

### 9.2. Đăng ký kinh doanh

**Căn cứ tham khảo:** Nghị định 168/2025/NĐ-CP, hiệu lực 01/07/2025.

- Onboarding hỏi loại hình nhưng không tự kết luận người dùng đã đủ điều kiện.
- Lưu mã đăng ký, ngành nghề, địa chỉ và file bằng chứng.
- Không dùng app như bằng chứng thay thế giấy tờ pháp lý.
- Khi nhiều cơ sở/địa điểm, sinh checklist thông báo/đăng ký cần xác nhận với chuyên gia địa phương.

### 9.3. Thuế 2026

**Căn cứ cần theo dõi:** Luật 109/2025/QH15, Nghị định 68/2026/NĐ-CP, Luật VAT 48/2024/QH15 và sửa đổi, quy định quản lý thuế/hóa đơn hiện hành.

- Engine phải hỗ trợ nhiều rule version, không hard-code UI.
- Khóa calculation cùng `rule_version_id` và input snapshot để tái hiện kết quả.
- Tách doanh thu dạy học, bán giáo trình, cho thuê, vận chuyển và nguồn thu khác.
- Hóa đơn điện tử chỉ qua kết nối được pháp luật cho phép.
- Mốc doanh thu, cách tính PIT, hạn nộp và mẫu biểu là content có quản trị, review và rollback.
- Mỗi màn hình thuế hiển thị nguồn văn bản, ngày cập nhật, giả định và cảnh báo “dự thảo”.

### 9.4. Dữ liệu cá nhân và trẻ em

**Căn cứ chính:** Luật Bảo vệ dữ liệu cá nhân 91/2025/QH15, hiệu lực 01/01/2026; Luật Dữ liệu và quy định liên quan.

Yêu cầu tối thiểu:

- Data inventory và mục đích xử lý cho từng trường dữ liệu.
- Privacy notice theo phiên bản; bằng chứng đã thông báo/đồng ý khi cần.
- Xác minh người giám hộ và quan hệ với học sinh.
- Cơ chế truy cập, sửa, xóa/hạn chế và rút consent theo luật.
- Retention schedule; không giữ vô hạn dữ liệu học sinh đã nghỉ.
- Đánh giá tác động xử lý/chuyển dữ liệu khi pháp luật yêu cầu.
- Danh sách processor/subprocessor; DPA với vendor.
- Chuyển dữ liệu ra nước ngoài chỉ sau legal review và hồ sơ cần thiết.
- Incident response và thông báo theo thời hạn luật hiện hành.
- Mã hóa, least privilege, audit, backup và secure deletion.
- Không quảng cáo nhắm mục tiêu trẻ em dựa trên hồ sơ học tập/hành vi.

### 9.5. Người tiêu dùng, hợp đồng và hoàn phí

**Căn cứ chính:** Luật Bảo vệ quyền lợi người tiêu dùng 19/2023/QH15, hiệu lực 01/07/2024.

- Công khai giá, cách tính, điều kiện hoàn/bảo lưu và phí phát sinh trước khi ghi danh.
- Lưu version điều khoản mà phụ huynh đã chấp nhận.
- Cho xem toàn bộ hợp đồng trước khi xác nhận điện tử.
- Kênh tiếp nhận khiếu nại và SLA xử lý.
- Không dùng điều khoản loại trừ trách nhiệm/quyền khiếu nại trái luật.
- Biên nhận, lịch sử điều chỉnh và hoàn tiền minh bạch.

### 9.6. Lao động, cộng tác viên và lương

- Phân loại đúng hợp đồng lao động/hợp đồng dịch vụ cần luật sư xác nhận theo thực tế, không chỉ theo tên hợp đồng.
- Hỗ trợ PIT khấu trừ, BHXH, giờ làm và chứng từ khi áp dụng.
- Tách rõ rate, phụ cấp, thưởng, khấu trừ; không cho khấu trừ không có căn cứ/phê duyệt.
- Dữ liệu nhân sự sau chấm dứt hợp đồng được giữ/xóa theo nghĩa vụ pháp lý và retention policy.

### 9.7. Thương mại điện tử, thanh toán và quảng cáo

- Đánh giá nghĩa vụ thông báo/đăng ký website/app với cơ quan quản lý tùy mô hình cung cấp dịch vụ.
- Cổng thanh toán phải là đối tác hợp pháp; app không lưu dữ liệu thẻ thanh toán thô.
- Xác minh quy định khuyến mại, referral/hoa hồng và thuế liên quan trước khi bật.
- Marketing SMS/Zalo/email phải có cơ sở hợp pháp, consent/opt-out và frequency cap.

### 9.8. Sở hữu trí tuệ và đề thi

- Người upload cam kết có quyền sử dụng tài liệu.
- Không cung cấp kho đề vi phạm bản quyền.
- File tạm tự xóa; không tái sử dụng để huấn luyện.
- Thư viện xử lý DOCX/PDF/font phải được rà license.

---

## 10. Security architecture

### 10.1. Authentication/session

- Password hash Argon2id hoặc PBKDF2 cấu hình hiện đại theo framework.
- MFA TOTP/passkey cho owner, kế toán, system operator.
- Access token ngắn; refresh token rotation và reuse detection.
- Revoke toàn bộ session khi đổi mật khẩu, khóa user, mất thiết bị.
- Login throttling, breached-password screening nếu có dịch vụ phù hợp.

### 10.2. Authorization

- Deny-by-default.
- Policy check ở server cho mọi endpoint; ẩn nút frontend không phải security control.
- Object-level authorization chống IDOR.
- Step-up auth cho export, refund, bank-account change, payroll approval.

### 10.3. Data protection

- TLS, encryption at rest, field encryption cho dữ liệu nhạy cảm.
- Secrets trong managed secret store, không trong repository/config production.
- Signed URL ngắn hạn; malware scan; file content-disposition an toàn.
- Backup mã hóa, restore test định kỳ.
- Log không chứa access token, mật khẩu, nội dung health note hoặc full phone.

### 10.4. Application security

- CSRF strategy phù hợp cơ chế auth; strict CORS.
- CSP, HSTS, secure cookies, clickjacking protection.
- Validation server-side, parameterized query, output encoding.
- Rate limit theo IP/user/tenant và riêng cho OTP/magic link.
- Dependency/SBOM scan, secret scan, SAST, DAST.
- Pen test trước GA và sau thay đổi auth/payment lớn.

### 10.5. Audit

- Append-only audit cho login, permission, student export, attendance edit, financial mutation, payroll/tax export.
- Audit gồm actor, tenant, action, target, before/after redacted, IP/device, correlation ID, timestamp.
- Không cho tenant admin xóa audit log.
- Retention theo phân loại dữ liệu và legal policy.

---

## 11. Non-functional requirements và SLO

### 11.1. Hiệu năng

- P95 API read < 400 ms, write < 700 ms trong tải mục tiêu, loại trừ job file/report nặng.
- First meaningful render < 2,5 giây trên mạng 4G phổ biến cho luồng giáo viên.
- Điểm danh 50 học sinh submit dưới 2 giây ở P95.
- Report phổ biến < 5 giây; report lớn chạy async.

### 11.2. Quy mô thiết kế năm đầu

- 5.000 tenant.
- 50.000 user active/ngày.
- 2 triệu học sinh records.
- 500 requests/giây peak cho API thường.
- 5.000 job thông báo/phút qua queue/worker.

Đây là giả định để benchmark, phải cập nhật sau discovery.

### 11.3. Availability và phục hồi

- SLO 99,9% theo tháng cho core app.
- RPO <= 15 phút, RTO <= 4 giờ ở MVP; nâng theo gói enterprise nếu có.
- Backup PITR database, object versioning, restore drill mỗi quý.
- Graceful degradation: lỗi SMS không chặn điểm danh; lỗi report không chặn thu tiền.

### 11.4. Accessibility và tương thích

- Mục tiêu WCAG 2.2 AA cho luồng chính.
- Keyboard, focus state, contrast, screen reader labels.
- Chrome/Edge/Safari/Firefox hai phiên bản gần nhất; Android/iOS web hiện hành.
- Không truyền đạt trạng thái chỉ bằng màu.

---

## 12. Cấu trúc repository đề xuất

```text
/src
  /backend
    /App.Api
    /App.Host
    /BuildingBlocks
    /Modules
      /Identity
      /Tenancy
      /Students
      /Academics
      /Scheduling
      /Attendance
      /Billing
      /Expenses
      /Teachers
      /Payroll
      /Reporting
      /TaxCompliance
      /Notifications
      /Documents
  /frontend
    /src
      /app
      /features
      /components
      /design-system
      /api
      /i18n
/tests
  /unit
  /integration
  /architecture
  /e2e
  /performance
/docs
  /adr
  /api
  /legal-register
  /runbooks
/infra
  /docker
  /iac
  /monitoring
```

Quy ước:

- ADR cho quyết định quan trọng.
- Feature folder frontend theo domain, không theo loại file toàn cục.
- Test đặt theo module và luồng nghiệp vụ.
- Không dùng shared/common như “ngăn kéo” chứa logic domain.

---

## 13. Delivery plan 24 tuần

### 13.1. Team mẫu

- 1 Product Manager/Business Analyst.
- 1 Tech Lead/Backend.
- 2 Backend engineers.
- 2 Frontend engineers.
- 1 QA automation.
- 0,5 Product Designer.
- 0,5 DevOps/SRE.
- Legal/tax advisor theo checkpoint.

### 13.2. Sprint 0–1, tuần 1–4: Discovery và nền tảng

**Product/legal**

- Phỏng vấn 5 giáo viên cá nhân, 3 trung tâm, 2 kế toán.
- Chốt personas, glossary, state machines, payment/fee policies.
- Lập legal register và data inventory.
- Chốt P0/P1/P2; prototype 5 luồng rủi ro cao.

**Engineering**

- Monorepo, CI, environments, branching/trunk strategy.
- Skeleton .NET/React, OpenAPI, error format.
- Tenant context, auth, membership, permission framework.
- PostgreSQL migrations, outbox, audit base.
- Observability, secret management, backup baseline.

**Exit criteria**

- Threat model v1, architecture ADR được duyệt.
- Login + tenant isolation integration test chạy CI.
- Prototype được người dùng mục tiêu test.

### 13.3. Sprint 2, tuần 5–6: Master data

- Campus, subjects, rooms.
- Students, guardians, teacher profiles.
- Class, enrollment, import Excel dry-run.
- Search không dấu và duplicate check.
- Privacy notice/consent v1.

**Demo:** import học sinh, tạo lớp, ghi danh và phân giáo viên.

### 13.4. Sprint 3–4, tuần 7–10: Lịch và điểm danh

- Timetable recurrence và session generation.
- Calendar tuần/tháng/list.
- Conflict engine giáo viên/phòng.
- Attendance mobile flow, audit, concurrency.
- Attendance policy hooks cho billing/payroll.
- Notification lịch thay đổi cơ bản.

**Exit:** E2E từ tạo lớp → lịch → buổi → điểm danh.

### 13.5. Sprint 5–6, tuần 11–14: Học phí và thanh toán

- Fee plans theo buổi/tháng.
- Charge generation idempotent.
- Bulk monthly payment request.
- Partial/multiple payment, allocation, receipt PDF.
- Debt filters/reminders.
- Void/refund foundation, financial close.
- Reconciliation import MVP.

**Exit:** kiểm toán nội bộ có thể lần từ báo cáo về payment/receipt/attendance nguồn.

### 13.6. Sprint 7, tuần 15–16: Chi phí và báo cáo

- Expense workflow và attachments.
- Revenue/cash/AR/expense/profit reports.
- Attendance and session reports.
- Export permission/audit.
- Dashboard v1.

### 13.7. Sprint 8, tuần 17–18: Trung tâm và payroll

- Fine-grained teacher permissions.
- Teacher substitution.
- Pay schemes/rate versions.
- Payroll calculation/review/approve/pay/lock.
- Payslip và adjustment.

### 13.8. Sprint 9, tuần 19–20: Compliance và tax draft

- Legal profile/checklist.
- Public disclosure, Mẫu 02/Mẫu 03.
- Tax profile, rule version framework.
- Revenue aggregation và tax draft export.
- Legal/tax advisor UAT.

Không release module này nếu chưa có sign-off nghiệp vụ bằng văn bản.

### 13.9. Sprint 10, tuần 21–22: Parent portal và hardening

- Guardian OTP portal.
- Lịch, attendance, debt, receipt.
- Consent/preferences.
- Security review, accessibility fixes, load test.
- Restore drill, incident runbook.

### 13.10. Sprint 11, tuần 23–24: Pilot và release candidate

- Migrate dữ liệu 3–5 pilot tenants.
- Parallel run 2 kỳ thu học phí và 1 kỳ lương.
- Fix severity 1/2; support playbook.
- Training/help center.
- Go/no-go checklist và rollback plan.

### 13.11. Sau GA

**P1 quý tiếp theo**

- E-invoice provider, bank/VietQR integration.
- PWA/offline attendance.
- Exam shuffler worker.
- Subscription/referral.
- Local rule packs.

**P2**

- Online learning/học liệu sâu.
- Forecast nâng cao.
- Mobile native chỉ khi dữ liệu sử dụng chứng minh cần thiết.
- Microservice extraction chỉ khi có bottleneck rõ.

---

## 14. Backlog dạng epic và độ ưu tiên

| Epic | Priority | Dependency | Rủi ro |
|---|---|---|---|
| Identity/tenant/RBAC | P0 | None | Critical |
| Student/guardian privacy | P0 | Identity | Critical |
| Class/enrollment | P0 | Master data | High |
| Scheduling/rooms | P0 | Class/teacher | High |
| Attendance | P0 | Sessions | High |
| Billing ledger | P0 | Enrollment/attendance | Critical |
| Payment/receipt/refund | P0 | Billing | Critical |
| Expense/reporting | P0 | Finance base | High |
| Teacher/payroll | P0 center | Sessions | Critical |
| Compliance disclosure | P0 | Legal profile/classes | Critical |
| Tax draft | P0/P1 | Finance + legal rules | Critical |
| Parent portal | P0 | Student/guardian | Critical privacy |
| Notifications | P0 basic | Identity/consent | High |
| E-invoice/payment integration | P1 | Finance stable | Critical external |
| Exam shuffler | P1 | Object storage/worker | High security |
| Subscription/referral | P1 | Tenant/billing SaaS | Medium |

---

## 15. Testing strategy

### 15.1. Unit tests

Ưu tiên pure domain rules:

- Fee calculation cho mọi attendance status.
- Partial allocation/refund.
- Schedule recurrence và conflict.
- Payroll rate effective date.
- Tax rule selection theo ngày hiệu lực.
- Permission/scope evaluator.

### 15.2. Integration tests

- PostgreSQL thật qua container.
- Tenant isolation.
- Transaction/outbox/idempotency.
- Concurrency attendance/payment.
- Authorization trên từng API mutation.
- File upload validation.

### 15.3. Contract tests

- Payment/e-invoice/SMS provider webhooks.
- Signature, retry, duplicate, out-of-order events.
- Không gọi sandbox provider bằng dữ liệu production.

### 15.4. E2E critical journeys

1. Onboard → import → class → timetable → attendance.
2. Attendance → charge → partial payment → final payment → receipt.
3. Cancel/refund → report/tax data reflects adjustment.
4. Teacher substitutes → payroll pays correct person.
5. Guardian sees only linked child.
6. Permission revoked → active session loses access.
7. Legal disclosure version exported and traceable.

### 15.5. Performance tests

- Morning/evening login spikes.
- Bulk monthly billing 10.000 students.
- Attendance concurrent across 1.000 classes.
- Report on 12–24 months of data.
- Notification burst with provider degradation.

### 15.6. Security/privacy tests

- OWASP ASVS-aligned checklist.
- IDOR/cross-tenant fuzzing.
- Magic-link leakage/expiry/replay.
- CSV formula injection in exports.
- Malicious DOCX/zip bomb.
- Backup restore and secure deletion verification.

---

## 16. CI/CD và môi trường

### 16.1. Pipeline mỗi pull request

- Format/lint/typecheck.
- Unit + architecture tests.
- Build backend/frontend.
- Integration tests với PostgreSQL/Redis.
- SAST, dependency vulnerability, secret scan, license scan.
- Migration validation và backward-compatibility check.
- Preview environment cho frontend/API nếu chi phí cho phép.

### 16.2. Deploy

- Dev → staging → production.
- Artifact immutable; promote cùng image.
- Expand-and-contract database migration.
- Feature flag cho tax rules/integration/risky UI.
- Canary hoặc blue-green khi đạt quy mô.
- Rollback ứng dụng; migration có forward-fix plan.

### 16.3. Observability

- RED metrics API; queue depth; job failures; provider latency.
- Business metrics: duplicate webhook, unallocated payment, failed charge generation, attendance not submitted.
- Alert theo SLO, tránh alert theo từng lỗi vô hại.
- Dashboard tách operator health và tenant business data.

---

## 17. Data migration và rollout

### 17.1. Import

- Template versioned.
- Upload → scan → parse → validate → preview → commit.
- Error report theo dòng/cột.
- Idempotency key/import batch ID.
- Rollback batch chỉ khi chưa phát sinh downstream transactions.

### 17.2. Pilot

- Chọn 2 giáo viên cá nhân và 3 trung tâm khác quy mô.
- Chạy song song Excel/sổ cũ ít nhất 2 kỳ thu và 1 kỳ lương.
- So sánh opening balance, charge, payment, outstanding, payroll.
- Chấp nhận chênh lệch chỉ khi có biên bản nguyên nhân.

### 17.3. Go-live

- Freeze data cũ trong cửa sổ công bố.
- Import opening balances có signed-off report.
- Smoke test: login, session, attendance, collect payment, receipt, backup.
- Hypercare 2 tuần; war room giờ cao điểm.

---

## 18. Definition of Done

Một story chỉ Done khi:

- Acceptance criteria đạt.
- Authorization và tenant isolation được test.
- Audit áp dụng nếu là thao tác nhạy cảm.
- Error/empty/loading/concurrency states hoàn thiện.
- Unit/integration/E2E tương xứng rủi ro.
- Logging/metrics không lộ PII.
- Accessibility và responsive kiểm tra.
- API/docs/help text cập nhật.
- Legal owner duyệt nếu liên quan thuế, dữ liệu trẻ em, công khai, hợp đồng hoặc hóa đơn.
- Migration/rollback/feature flag được xem xét.

---

## 19. Release gates

### 19.1. Gate trước pilot

- Không có Sev-1/Sev-2 mở.
- Cross-tenant test pass.
- Restore backup thành công.
- Privacy notice/terms/retention approved.
- Billing totals reconcile với bộ test vàng.

### 19.2. Gate trước GA

- Pen test critical/high đã xử lý hoặc có risk acceptance chính thức.
- UAT của kế toán cho học phí/chi phí/lương.
- UAT legal/tax cho compliance/tax draft.
- Load test đạt SLO.
- Incident, breach, support, rollback runbook diễn tập.
- Data processor agreements và vendor register đầy đủ.

---

## 20. Rủi ro chính và biện pháp

| Rủi ro | Tác động | Biện pháp |
|---|---|---|
| Hard-code thuế theo ảnh tham khảo | Tính sai/nguy cơ pháp lý | Rule version, nguồn văn bản, sign-off, effective date |
| Một link xem điểm danh cả lớp | Lộ dữ liệu trẻ em | Portal xác minh, token per-student, expiry, OTP |
| Sửa/xóa phiếu thu trực tiếp | Không đối soát được | Immutable ledger, void/refund/adjustment |
| Attendance tạo phí/lương trùng | Thiệt hại tài chính | Idempotency + unique business keys |
| Phân quyền chỉ ở UI | Rò dữ liệu | Server policy + object scope + tests |
| Microservice quá sớm | Chậm delivery | Modular monolith + outbox |
| Trộn DOCX trong API chính | RCE/DoS | Sandbox worker, scan, limits, retention |
| Rule địa phương khác nhau | Sai compliance | Rule pack theo jurisdiction, legal register |
| Báo cáo dùng cash/accrual lẫn lộn | Quyết định sai | Tách nhãn, data dictionary, drill-down |
| Vendor SMS/payment lỗi | Chặn vận hành | Queue/retry/circuit breaker; core flow độc lập |

---

## 21. KPI sau phát hành

### Activation

- % tenant tạo lớp đầu tiên trong 24 giờ.
- Time-to-first-schedule.
- % import thành công không cần support.

### Usage

- % session được điểm danh trong ngày.
- Thời gian trung vị điểm danh một lớp.
- % học phí được ghi nhận qua app.

### Finance quality

- Số unallocated payments.
- Tỷ lệ duplicate/void/refund.
- Chênh lệch đối soát theo tenant.

### Reliability/security

- SLO, error budget, job failure rate.
- Cross-tenant/security incidents = 0.
- Thời gian revoke compromised session.

### Compliance

- % cơ sở có disclosure còn hiệu lực.
- % giáo viên cần Mẫu 03 đã hoàn tất.
- % tax draft có rule version và source đầy đủ.

---

## 22. Các quyết định cần chốt trong discovery

1. Sản phẩm là SaaS bán cho hộ/trung tâm hay phần mềm nội bộ một trung tâm?
2. Tenant có nhiều cơ sở ngay từ MVP không?
3. Giáo viên là nhân viên, cộng tác viên hay hỗn hợp?
4. Học phí tháng tính cố định, theo buổi dự kiến hay buổi thực tế?
5. Chính sách vắng/học bù/hoàn phí cụ thể?
6. Có thu hộ các khoản không phải học phí không?
7. Có phát hành hóa đơn điện tử ngay MVP không, dùng nhà cung cấp nào?
8. Parent portal dùng OTP hay tài khoản mật khẩu?
9. Kênh thông báo ưu tiên: Zalo ZNS, SMS, email hay push?
10. Địa phương pilot để lập rule pack?
11. Mức tải/tenant/student thực tế năm 1 và năm 3?
12. Nhu cầu trộn đề có đủ lớn để vào P1 hay tách sản phẩm?
13. Nơi lưu dữ liệu và yêu cầu chuyển dữ liệu xuyên biên giới?
14. RPO/RTO và ngân sách hạ tầng?

Không nên bắt đầu sprint billing/tax trước khi trả lời câu 3–7.

---

## 23. Nguồn tham khảo chính thức

- [Thông tư 29/2024/TT-BGDĐT về dạy thêm, học thêm](https://vanban.chinhphu.vn/?classid=1&docid=212362&orggroupid=4&pageid=27160)
- [Nghị định 168/2025/NĐ-CP về đăng ký doanh nghiệp](https://vanban.chinhphu.vn/?docid=214334&pageid=27160)
- [Luật Thuế thu nhập cá nhân 109/2025/QH15](https://vanban.chinhphu.vn/?docid=216495&pageid=27160)
- [Nghị định 68/2026/NĐ-CP về thuế và quản lý thuế đối với hộ/cá nhân kinh doanh](https://vanban.chinhphu.vn/?docid=217111&pageid=27160&typegroupid=4)
- [Hướng dẫn 2026 về thuế đối với hộ kinh doanh dạy thêm](https://xaydungchinhsach.chinhphu.vn/huong-dan-tinh-thue-voi-ho-kinh-doanh-mam-non-tu-thuc-day-them-hoc-them-119260406144846386.htm)
- [Nghị định 70/2025/NĐ-CP về hóa đơn, chứng từ](https://vanban.chinhphu.vn/?docid=213179&lang=vi&pageid=27160)
- [Luật Bảo vệ dữ liệu cá nhân 91/2025/QH15](https://vanban.chinhphu.vn/?docid=214590&pageid=27160)
- [Luật Bảo vệ quyền lợi người tiêu dùng 19/2023/QH15](https://vanban.chinhphu.vn/?classid=1&docid=208363&orggroupid=1&pageid=27160)
- [Chính sách hỗ trợ .NET chính thức](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [Tài liệu React 19](https://react.dev/versions)

Các nguồn phải được rà soát lại ngay trước khi code tax rule hoặc phát hành production; link hướng dẫn không thay thế việc đọc toàn văn văn bản và văn bản sửa đổi/hợp nhất.

---

## 24. Bước tiếp theo đề xuất

Trong 5 ngày làm việc đầu tiên:

1. Workshop 2 giờ chốt mô hình kinh doanh và địa phương pilot.
2. Workshop 3 giờ chốt fee/attendance/payroll state machine bằng ví dụ thật.
3. Legal/tax review danh sách rule trong mục 9.
4. Product designer dựng prototype: onboarding, calendar, attendance, collect payment, guardian portal.
5. Tech lead tạo 6 ADR đầu: tenancy, auth, finance ledger, modular boundaries, file processing, tax rule versioning.
6. QA tạo “golden dataset” 20 tình huống học phí/lương/thuế để dùng xuyên suốt dự án.

Đầu ra cuối tuần phải là: scope P0 khóa, prototype test được, data glossary, legal register, architecture skeleton và release plan có owner cho từng epic.
