# Điểm danh từng buổi học — nghiệp vụ và kiểm thử trong Visual Studio

## 1. Luồng đã triển khai

Mỗi `teaching_session` có tối đa **một phiếu điểm danh**. Lần đầu mở GET chỉ hiển thị danh sách dự kiến, không ghi DB. Danh sách được xác định theo học sinh đã ghi danh **tại thời điểm buổi bắt đầu**; học sinh rút sau đó vẫn nằm trong lịch sử, học sinh vào lớp sau đó không bị thêm ngược vào buổi cũ. Phải dùng `studentId`, không dựa vào tên để tránh nhầm hai học sinh trùng tên.

Sau khi buổi bắt đầu, người quản lý trung tâm lưu **Draft**. `defaultMark` áp cho toàn bộ danh sách, `overrides` chỉ chứa các trường hợp khác; ví dụ mặc định `Present`, riêng một em `ExcusedAbsent`. Trạng thái hợp lệ: `Unknown`, `Present`, `Late`, `ExcusedAbsent`, `UnexcusedAbsent`. `Unknown` chỉ dùng khi còn ở bản nháp. Học bù là nghiệp vụ của một buổi khác, không phải mark của buổi này. Muốn **Submit**, phải chọn `actualTeacherId` và đã đánh dấu mọi học sinh. Phiếu đã Submit không được ghi đè bằng Draft; sửa qua **Correction** với lý do 10–500 ký tự. Mỗi lần lưu/chốt/sửa tăng `version` và ghi lịch sử `before`/`after`, người thao tác, thời gian, lý do.

Phiếu này **không tạo công nợ, học phí hay lương**. Việc ghi danh/điểm danh không tự động tạo khoản thanh toán. `actualTeacherId` là hồ sơ giáo viên thực tế dạy, còn `actorUserId` trong lịch sử là tài khoản quản lý thao tác. Hiện hồ sơ giáo viên chưa liên kết chắc chắn với tài khoản IAM, nên chỉ `CenterOwner`/`CenterAdmin` được đọc và sửa điểm danh; không cấp quyền cho mọi tài khoản mang role `Teacher` để tránh người không phụ trách lớp sửa phiếu. Trung tâm chưa được duyệt hoặc tài khoản không thuộc trung tâm sẽ bị chặn bởi authorization.

## 2. Setup trong IDE

1. Dừng API/Worker trong Visual Studio bằng **Shift+F5** rồi **Build → Build Solution**; build phải không lỗi.
2. Chọn `TutorManagement.DbMigrator` → **Set as Startup Project**. Vào **Tools → NuGet Package Manager → Package Manager Console**. Chọn **Default project = `TutorManagement.Modules.Academics.Infrastructure`**, chạy:

   ```powershell
   Update-Database -Context AcademicsDbContext -StartupProject TutorManagement.DbMigrator
   ```

   Migration `20261020155251_AddSessionAttendance` tạo `academics.attendance_sheets`, `attendance_entries`, `attendance_revisions` và khóa ngoại ràng buộc đúng trung tâm/buổi/lớp/học sinh. Trên DB local của workspace này migration đã được áp dụng thành công. Nếu dùng DB khác, kiểm tra ba bảng và `academics.__ef_migrations_history` trong pgAdmin. **Không sửa bảng bằng SQL để thử nghiệp vụ**.
3. Chọn lại `TutorManagement.Api` làm Startup Project, nhấn **F5**. Mở Swagger theo dòng `Now listening on` (thường là [Swagger local](http://localhost:5217/swagger/index.html)). Đăng nhập bằng tài khoản quản lý của trung tâm Approved và bấm **Authorize** với `accessToken` như các tài liệu test API trước.
4. Ghi `centerId` từ `GET /api/v1/me/centers`; chọn lớp có ít nhất hai học sinh đã ghi danh, một giáo viên và một phòng **cùng trung tâm**. Nếu chưa có, tạo môn → lớp → học sinh → ghi danh → giáo viên → phòng theo [MANUAL-TEST-TEACHING-OPERATIONS.md](MANUAL-TEST-TEACHING-OPERATIONS.md).

## 3. Test tay theo thứ tự

### A. Tạo buổi và lấy danh sách

Trong nhóm `10 · Teaching Sessions`, `POST /api/v1/centers/{centerId}/sessions`. Đặt `startUtc` khoảng **2–3 phút sau hiện tại**, `endUtc` ít nhất 15 phút sau `startUtc` (ví dụ kéo dài 30 phút). Swagger nhận UTC, dạng `2026-10-20T15:00:00Z`; giờ Việt Nam = UTC+7. Ghi `data.id` làm `sessionId`. Nếu báo `schedule_conflict`, chọn khung giờ/lớp/phòng/giáo viên khác.

Trong nhóm `12 · Attendance`, gọi `GET /api/v1/centers/{centerId}/sessions/{sessionId}/attendance`. Kỳ vọng `200`, `data.status="NotStarted"`, `data.version=0`, `data.students[]` gồm `studentId`, `studentCode`, `fullName`, `mark="Unknown"`. Chép hai `studentId`. Trước giờ bắt đầu, thử lưu Draft phải nhận `409 attendance_session_not_started`; chờ qua giờ bắt đầu rồi thử tiếp.

### B. Lưu nháp và chốt

Gọi `PUT .../attendance/draft` với:

```json
{
  "expectedVersion": 0,
  "actualTeacherId": "<teacherId>",
  "note": "Buổi đầu tiên",
  "defaultMark": "Unknown",
  "overrides": [
    { "studentId": "<studentId-1>", "mark": "Present", "note": null }
  ]
}
```

Kỳ vọng `200`, `Draft`, `version=1`; học sinh còn lại `Unknown`. `POST .../attendance/submit` body `{"expectedVersion":1}` lúc này phải `409 attendance_incomplete` và không tăng version.

Lưu Draft lại với `expectedVersion=1`, `defaultMark="Present"`, `overrides` chứa học sinh vắng nếu có:

```json
{
  "expectedVersion": 1,
  "actualTeacherId": "<teacherId>",
  "note": "Đã kiểm tra sĩ số",
  "defaultMark": "Present",
  "overrides": [
    { "studentId": "<studentId-2>", "mark": "ExcusedAbsent", "note": "Phụ huynh báo vắng" }
  ]
}
```

Kỳ vọng `version=2`, mọi học sinh đã có mark cụ thể. Lưu lại với version cũ `1` phải `409 teaching_concurrent_change` — cần GET phiếu mới nhất rồi quyết định thay đổi, không tự động ghi đè. Gọi `POST .../attendance/submit` với `{"expectedVersion":2}` → `200`, `Submitted`, `version=3`, có `submittedAtUtc`. Lưu Draft sau Submit → `409 attendance_submitted`.

### C. Sửa có lý do và xem lịch sử

Gọi `PATCH .../attendance/correction`:

```json
{
  "expectedVersion": 3,
  "actualTeacherId": "<teacherId>",
  "note": "Đã đối chiếu lại",
  "reason": "Phụ huynh cung cấp bằng chứng học sinh đến muộn",
  "changes": [
    { "studentId": "<studentId-2>", "mark": "Late", "note": "Đến muộn" }
  ]
}
```

Kỳ vọng `200`, vẫn `Submitted`, `version=4`, học sinh thứ hai `Late`. `GET .../attendance/history` phải có bốn phiên bản theo thứ tự: `DraftSaved`, `DraftSaved`, `Submitted`, `Corrected`; mục cuối có `reason` và `before`/`after` dạng đối tượng JSON. Sửa với `expectedVersion=3` phải `409`; lý do quá ngắn hoặc `Unknown` trong correction không hợp lệ. Trường `note` của phiếu trong correction là giá trị đầy đủ mới; để `null` nghĩa là xóa ghi chú phiếu cũ. Có thể sửa chỉ giáo viên/ghi chú với `changes: []` nếu thông tin thực sự thay đổi; correction không đổi gì bị từ chối `attendance_unchanged`. Gửi lại Draft y hệt với version hiện hành trả phiếu cũ, không sinh thêm bản lịch sử.

## 4. Case nghiệp vụ và bảo mật đáng kiểm tra

| Tình huống | Kết quả mong đợi |
|---|---|
| Không token | `401` trong envelope chuẩn. |
| Không phải Owner/Admin của trung tâm | `403`; không thấy danh sách học sinh. |
| `sessionId` của trung tâm khác | `404 teaching_resource_not_found`. |
| Buổi đã hủy hoặc chưa bắt đầu | Không thể lưu điểm danh (`409`). |
| `studentId` không thuộc danh sách ở thời điểm buổi bắt đầu, lặp ID, mark sai | `400`; không có phiếu sai được lưu. |
| Submit còn `Unknown` hoặc chưa có giáo viên thực dạy | `409 attendance_incomplete`. |
| Hai quản lý cùng lưu từ `version` cũ | Chỉ request đầu thành công; request còn lại `409 teaching_concurrent_change`. PostgreSQL khóa lớp trong transaction ngắn và kiểm tra lại version sau khi có khóa. |
| Học sinh rút sau buổi / ghi danh sau buổi | Phiếu buổi cũ giữ danh sách đúng tại thời điểm bắt đầu; không viết lại lịch sử. |
| Dời/hủy buổi đã bắt đầu | Bị từ chối; không xóa dấu vết điểm danh. |

Response thành công dùng `success`, `message`, `data`, `traceId`; lỗi dùng `success=false`, `errors[]` và `traceId`, không trả entity EF trực tiếp. Ghi chú chỉ nên mô tả tình huống học tập; tránh nhập dữ liệu nhạy cảm không cần thiết.

## 5. Test tự động trong Visual Studio

Vào **Test → Configure Run Settings → Select Solution Wide runsettings File**, chọn `backend/tests/IntegrationTests/AuthE2E.runsettings`. Trong **Test Explorer**, lọc `RealPostgresAttendanceTests` rồi **Run**. Test tạo trung tâm riêng trên PostgreSQL local, kiểm thử cả lịch sử và tự dọn đúng dữ liệu của trung tâm đó. Không chạy test DB này trên production. Bộ test của workspace sau thay đổi: **108 integration + 3 architecture pass**, build solution **0 warning, 0 error**.
