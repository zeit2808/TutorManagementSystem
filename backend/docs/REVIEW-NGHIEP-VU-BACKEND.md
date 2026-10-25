# Đánh giá nghiệp vụ back-end — Quản Lý Dạy Thêm

Phạm vi: toàn bộ `backend/src` (≈13.800 dòng C# không tính migration), 84 controller action, đối chiếu domain → application → infrastructure → API. Ngày đọc: 22/10/2026.

Ký hiệu ưu tiên: **P0** chặn vận hành thật · **P1** sai/thiếu nhất quán nghiệp vụ · **P2** API/read-model gây khó cho client · **P3** kỹ thuật, độ bền.

---

## 1. Những gì đang làm tốt (nên giữ)

- **Kiến trúc**: modular monolith rõ ranh giới (IdentityAccess / Organizations / Academics), có ArchitectureTests kiểm tra phụ thuộc; mỗi module tách Domain/Application/Infrastructure/Api/Contracts.
- **Cô lập tenant**: mọi truy vấn lọc theo cả `centerId` và `id`; policy `CenterMember / CenterManagement / CenterOwner` kiểm tra membership đang hoạt động trên từng request, trung tâm bị đình chỉ bị chặn ngay.
- **Chống ghi đè**: `expectedVersion` + `IsConcurrencyToken` cho subject/room/class/student/teacher/session/series/attendance.
- **Idempotency**: chuỗi lịch (`requestId` + hash), đơn đăng ký lớp (`Idempotency-Key` + hash nội dung) — replay trả kết quả cũ, đổi nội dung thì 409.
- **Khoá nghiệp vụ**: `pg_advisory_xact_lock` theo khoá `class/teacher/room/student` có sắp xếp → tránh deadlock, chống race khi ghi danh/xếp lịch.
- **Điểm danh đúng chuẩn kiểm toán**: roster lấy theo ghi danh *tại thời điểm buổi học bắt đầu*, nháp → nộp → hiệu chỉnh có lý do (≥10 ký tự), lưu snapshot before/after theo version.
- **Lịch lặp**: preview không ghi dữ liệu, phát hiện xung đột theo từng buổi, xử lý giờ không tồn tại/nhập nhằng khi đổi múi giờ, stop/replace không đụng buổi đã diễn ra hoặc đã điểm danh.
- **Bảo mật phiên**: access token ngắn hạn, refresh token xoay vòng + phát hiện tái sử dụng, cookie HttpOnly, OTP 8 số có giới hạn, rate limit theo nhóm endpoint, outbox email.

---

## 2. P0 — Lỗ hổng nghiệp vụ chặn vận hành thật

### 2.1 Không có cách tạo thành viên CenterAdmin / Teacher / Staff
`CenterMemberRole` có 4 vai trò nhưng `CenterMembership` chỉ có factory `CreateOwner`, `CenterInvitation` chỉ có `CreateOwner`, và API chỉ có `POST /platform/centers/{id}/invitations` (PlatformAdmin mời chủ). Hệ quả: chủ trung tâm **không thể mời quản lý, giáo viên, nhân viên**; ba vai trò còn lại chỉ tồn tại qua seed.

**Đề xuất**: thêm `POST/GET /centers/{centerId}/members/invitations` (Owner mời Admin/Teacher/Staff; Admin mời Teacher/Staff), `GET /centers/{id}/members`, `PUT .../members/{userId}/role`, `POST .../members/{userId}/deactivate`, chuyển quyền chủ (`transfer-ownership`) có xác nhận mật khẩu. Thu hồi membership phải thu hồi luôn phiên đang dùng trung tâm đó (hoặc ít nhất có hiệu lực ngay nhờ policy kiểm tra DB như hiện tại).

### 2.2 Lời mời chặn email đã có tài khoản
`InviteCenterOwnerCommandHandler` trả `email_already_exists` nếu email đã tồn tại; `AcceptCenterInvitation` luôn tạo user mới. Mâu thuẫn với nguyên tắc "một người thuộc nhiều trung tâm": chủ trung tâm A không thể được mời làm chủ trung tâm B.

**Đề xuất**: nếu email đã tồn tại → lời mời ở chế độ *liên kết*: người nhận đăng nhập rồi `POST /auth/invitations/{token}/accept-existing` để tạo membership mới, không tạo user.

### 2.3 Giáo viên (hồ sơ dạy) không gắn với tài khoản
`Teacher` là "teaching resource, not an IAM account". Không có liên kết `Teacher ↔ User`, nên:
- người có vai trò Teacher không có "lịch dạy của tôi", không xem được lớp mình dạy;
- điểm danh chỉ cho `CenterManagement` → giáo viên đứng lớp không tự điểm danh được — đây là luồng phổ biến nhất ở trung tâm dạy thêm.

**Đề xuất**: `Teacher.UserId?` (unique theo center), endpoint liên kết; policy mới `SessionTeacher` cho phép giáo viên được phân công (hoặc `actualTeacher`) lưu nháp/nộp điểm danh của *buổi mình dạy* trong một cửa sổ thời gian (ví dụ đến 23:59 cùng ngày); hiệu chỉnh sau khi nộp vẫn chỉ Management.

### 2.4 Học sinh không có cách tìm trung tâm
Học sinh (role toàn cục `Student`) không phải member nên `/me/centers` trả rỗng; mọi endpoint học sinh đều cần `centerId` trên URL, và không có endpoint nào trả danh sách trung tâm đang tuyển sinh hay thông tin công khai của trung tâm (kể cả tên — `/profile` yêu cầu membership). Front-end buộc phải dựa vào đường link do trung tâm gửi.

**Đề xuất**: `GET /student/centers` (các trung tâm học sinh đã nộp đơn/đang học), `GET /public/centers/{code}` (tên hiển thị + trạng thái nhận đăng ký), mã/slug trung tâm để chia sẻ link đăng ký.

### 2.5 Thiếu thông tin liên hệ cốt lõi của học sinh/giáo viên
`Student` chỉ có `Code`, `FullName`; `Teacher` tương tự. Trung tâm dạy thêm vận hành qua **phụ huynh** (SĐT, Zalo), ngày sinh, trường/lớp đang học. Đơn đăng ký lớp cũng chỉ có `FullName` — người duyệt không biết liên hệ ai, và `ClassApplicationDto` không trả email tài khoản.

**Đề xuất**: bổ sung `Student { DateOfBirth?, School?, Grade?, Phone?, GuardianName?, GuardianPhone?, Note? }`, `Teacher { Phone?, Email? }`; đơn đăng ký thu thêm SĐT phụ huynh; DTO duyệt đơn trả `applicantEmail` (đã có trong IAM) cho Management.

### 2.6 Buổi học "đã đến giờ nhưng không diễn ra" bị kẹt
`Cancel`/`Reschedule` chỉ cho buổi **chưa bắt đầu**. Giáo viên ốm báo muộn, mất điện… → buổi đã bắt đầu không thể huỷ, cũng không nên điểm danh; nó mãi ở trạng thái "hiệu lực, chưa điểm danh", sai báo cáo chuyên cần và (sau này) học phí/lương.

**Đề xuất**: hành động `mark-not-held` (sau giờ bắt đầu, chưa có phiếu điểm danh, bắt buộc lý do) và `cancel` có **lý do** cho mọi trường hợp; liên kết `MakeupForSessionId` cho buổi học bù.

---

## 3. P1 — Sai sót / thiếu nhất quán nghiệp vụ

| # | Vấn đề | Vị trí | Đề xuất |
|---|---|---|---|
| 1 | Lịch của học sinh hiển thị cả các buổi **trước ngày ghi danh**, và mất toàn bộ lịch sử sau khi rút | `ClassRegistrationStore.ListMySessionsAsync` lọc `WithdrawnAtUtc == null` mà không so `session.StartUtc` với `EnrolledAtUtc` | Lọc `session.StartUtc >= enrollment.EnrolledAtUtc && (WithdrawnAtUtc == null \|\| session.StartUtc < WithdrawnAtUtc)` |
| 2 | Không có ràng buộc chồng lịch ở DB; nhánh `ExclusionViolation` là code chết | Migrations không có `EXCLUDE`; `TeachingStore.SaveAsync` | Thêm `btree_gist` + `EXCLUDE USING gist (center_id WITH =, teacher_id WITH =, tstzrange(start_utc,end_utc) WITH &&) WHERE (cancelled_at_utc IS NULL)` cho class/teacher/room — lưới an toàn khi có worker/script khác ghi |
| 3 | Rút học không có lý do, không có ngày hiệu lực, không cần `expectedVersion` | `DELETE /classes/{id}/enrollments/{studentId}` | `POST .../withdraw { reason, effectiveDate?, expectedClassVersion }`; ngày hiệu lực ảnh hưởng trực tiếp roster điểm danh |
| 4 | Trung tâm bị **từ chối** không thể sửa/nộp lại; tạo mới thì vướng unique mã số thuế | `Center.Reject`, không có endpoint update | Cho PlatformAdmin sửa hồ sơ khi `PendingApproval/Rejected` và "nộp lại" về `PendingApproval` |
| 5 | Chủ trung tâm không huỷ được yêu cầu đổi hồ sơ đang chờ; CenterAdmin không xem được lịch sử | `CenterChangesController` chỉ Owner | `POST .../change-requests/{id}/withdraw`; GET cho Member |
| 6 | Hiệu chỉnh điểm danh không thêm được học sinh thiếu entry | `CorrectAsync` chỉ cho key có sẵn trong `entries` | Cho phép tạo entry nếu học sinh thuộc roster |
| 7 | Mỗi lần lưu nháp sinh 1 revision "DraftSaved" → lịch sử nhiễu, version tăng nhanh | `AttendanceWorkflow.SaveDraftAsync` | Chỉ ghi revision cho `Submitted/Corrected`, hoặc gộp nháp liên tiếp của cùng người trong N phút |
| 8 | Có thể chọn giáo viên **đã lưu trữ** làm giáo viên thực dạy | `FindTeacherAsync` không lọc active | Kiểm tra `IsActive` (hoặc cho phép có chủ đích nhưng ghi rõ) |
| 9 | Mở/đóng tuyển sinh không dùng `expectedVersion` nhưng vẫn tăng `Version` → form sửa lớp đang mở ở tab khác bị 409 khó hiểu | `SetRegistrationAsync` | Nhận `expectedVersion` như các mutation khác |
| 10 | Mã học sinh tự sinh `ST-<14 hex>` khi duyệt đơn, khác hẳn mã tạo tay | `ClassRegistrationWorkflow.ApproveAsync` | Bộ đếm mã theo trung tâm (`HS-000123`) dùng chung cho cả hai luồng |
| 11 | Duyệt trung tâm cho phép từ `Rejected` → `Approved` nhưng tài liệu ghi chỉ duyệt khi chờ | `Center.Approve` | Thống nhất quy tắc và ghi lịch sử chuyển trạng thái (hiện chỉ giữ review mới nhất) |
| 12 | Lịch sử duyệt trung tâm bị ghi đè (`ReviewNote`, `ReviewedAtUtc` chỉ một bản) | `Center` | Bảng `center_status_history` (from, to, actor, reason, at) |

---

## 4. P2 — API / read-model khiến client phải “ghép tay”

1. **DTO chỉ trả ID**: `SessionDto`, `ScheduleSeriesDto`, `ClassApplicationDto`, `StudentSessionDto`, `AttendanceSheetDto` chỉ có `classId/teacherId/roomId`. Client phải tải toàn bộ lớp, giáo viên, phòng (mỗi danh sách tối đa 100/trang) để hiển thị tên. Học sinh **không có quyền** đọc teachers/rooms nên lịch học của học sinh không thể hiện tên giáo viên/phòng.
   → Thêm phần `class { code, name }`, `teacher { fullName }`, `room { name }` vào các DTO đọc (projection, không ảnh hưởng domain).
2. **Người thao tác chỉ là GUID**: `actorUserId`, `requestedByUserId`, `reviewedByUserId` — không có endpoint tra tên. → trả kèm `displayName` hoặc `GET /centers/{id}/members/{userId}`.
3. **Thiếu tìm kiếm/lọc**: học sinh/giáo viên/lớp chỉ có phân trang + `includeArchived`; đơn đăng ký không lọc theo lớp; buổi học không lọc theo lớp/giáo viên/phòng. Ghi danh học sinh vào lớp cần ô tìm kiếm phía server (`?search=`).
4. **Không có thông tin `me`**: client phải tự giải mã JWT để biết `PlatformAdmin`/`Student`, username. → `GET /me` (username, email, roles, hasPassword, hasGoogle).
5. **Phân trang không đồng nhất**: subjects/rooms/platform centers trả mảng; classes/students/… trả `TeachingPage`. Nền tảng có nhiều trung tâm cần phân trang + lọc trạng thái + tìm theo MST.
6. **Không có hàng đợi tổng cho PlatformAdmin**: yêu cầu đổi hồ sơ chỉ xem theo từng trung tâm → `GET /platform/change-requests?status=Pending`. Lời mời không có list/resend/revoke dù domain đã có `Revoke`.
7. **Giới hạn khoảng thời gian khác nhau**: lịch trung tâm ≤31 ngày, lịch học sinh ≤93 ngày — nên thống nhất và công bố trong OpenAPI.
8. **Mã HTTP chưa nhất quán**: vi phạm quy tắc nghiệp vụ lúc là 409 (`class_full`, `subject_has_active_classes`), lúc 422 (`center_not_approved` khi mời), lúc 400. Đề xuất: 400 sai định dạng, 409 xung đột phiên bản/idempotency/trùng, 422 vi phạm quy tắc nghiệp vụ.
9. **Trung tâm chưa có múi giờ**: chuỗi lịch nhận `timeZoneId` tuỳ ý theo request. → `Center.TimeZoneId` mặc định `Asia/Ho_Chi_Minh`, series kế thừa.

---

## 5. P3 — Kỹ thuật, độ bền

- `ContractTests`, `EndToEndTests`, `docs/adr`, `docs/architecture` đang rỗng. Nên có contract test sinh từ OpenAPI để front-end bắt được thay đổi DTO.
- Chỉ mục: `sessions` chỉ có `(center_id, start_utc)`; truy vấn xung đột dùng OR theo class/teacher/room → thêm index `(center_id, teacher_id, start_utc)`, `(center_id, room_id, start_utc)`, `(center_id, class_id, start_utc)` (hoặc nhận luôn từ exclusion constraint ở mục 3.2).
- Thư mục `.artifacts/` và `bin/obj` nằm trong cây mã — kiểm tra `.gitignore`.
- `ClassRegistrationStore` giữ `transaction` trong field của service scoped — đúng với 1 request/1 scope nhưng dễ vỡ nếu tái sử dụng; nên trả guard như `TeachingStore`.
- Worker email: tài liệu ghi chưa đảm bảo multi-worker; đã có lease — cần test đồng thời 2 worker.
- Các module Finance / HumanResources / Notifications / Reporting / … đang trống: cần ghi rõ trên OpenAPI/README là *chưa có* để tránh client hiển thị nhầm.

---

## 6. Lộ trình nghiệp vụ gợi ý (theo giá trị cho trung tâm dạy thêm)

1. **Thành viên & phân quyền** (2.1, 2.2, 2.3) — mở khoá vận hành nhiều người.
2. **Hồ sơ học sinh/phụ huynh** (2.5) + tìm kiếm (4.3) + read-model có tên (4.1).
3. **Buổi học ngoại lệ**: huỷ có lý do, không diễn ra, học bù (2.6).
4. **Học phí** (module Finance): biểu phí theo lớp (theo buổi/theo tháng/khoá), sinh công nợ từ điểm danh đã nộp (đã có `Present/Late/ExcusedAbsent/UnexcusedAbsent` làm đầu vào), thu tiền, miễn giảm.
5. **Lương giáo viên** (HumanResources): tính từ `ActualTeacherId` của phiếu đã nộp — dữ liệu nền đã sẵn.
6. **Thông báo** (Notifications): nhắc lịch, báo vắng cho phụ huynh, kết quả duyệt đơn — tận dụng outbox hiện có.
7. **Báo cáo**: chuyên cần theo lớp/học sinh, tỉ lệ lấp đầy lớp, công nợ.

---

## 7. Ảnh hưởng lên front-end hiện tại

Front-end được dựng **chỉ trong phạm vi endpoint đang có**. Các điểm ở trên được xử lý tạm ở client như sau:

- Ghép tên lớp/giáo viên/phòng bằng cách tải danh mục một lần cho mỗi trang (4.1).
- Học sinh vào trung tâm qua liên kết `/student/centers/{centerId}` do trung tâm chia sẻ; trang nhập mã trung tâm thủ công (2.4).
- Vai trò toàn cục đọc từ claim `role` trong access token (4.4).
- Teacher/Staff thấy dữ liệu ở chế độ chỉ đọc; nút điểm danh/sửa ẩn hoặc vô hiệu kèm giải thích (2.3).
- Tìm kiếm học sinh khi ghi danh thực hiện trên các trang đã tải (giới hạn 100/trang) (4.3).
- Không hiển thị chức năng mời thành viên, học phí, báo cáo (2.1, mục 6).
