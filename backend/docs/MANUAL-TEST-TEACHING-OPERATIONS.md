# Kiểm thử tay: Lớp, học sinh, ghi danh, giáo viên và lịch dạy

Tài liệu này dành cho backend hiện tại, dùng trực tiếp **Visual Studio + Package Manager Console + Swagger + pgAdmin**. Dữ liệu thử là dữ liệu của trung tâm đã được duyệt; không sửa bảng trực tiếp bằng SQL.

## 1. Chuẩn bị database trong Visual Studio

Trên database local dùng khi phát triển ngày 19/10/2026, hai migration mới đã được áp dụng và test thật trên PostgreSQL thành công. Nếu bạn dùng một database khác, cần kiểm tra/ap dụng theo thứ tự sau:

1. **Shift+F5** để dừng API/Worker trước khi Build; tránh lỗi DLL đang được sử dụng. Trong **Solution Explorer**, chọn `TutorManagement.DbMigrator` → **Set as Startup Project**.
2. Mở **Tools → NuGet Package Manager → Package Manager Console**.
3. Chọn **Default project = `TutorManagement.Modules.Organizations.Infrastructure`**, chạy `Update-Database -Context OrganizationDbContext -StartupProject TutorManagement.DbMigrator`. Migration `20261019053510_AddRoomCenterReferenceKey` bổ sung khóa `(center_id,id)` cho phòng, để lịch dạy không thể tham chiếu phòng của trung tâm khác.
4. Đổi **Default project = `TutorManagement.Modules.Academics.Infrastructure`**, chạy `Update-Database -Context AcademicsDbContext -StartupProject TutorManagement.DbMigrator`. Migration `20261019041801_AddTeachingOperations` tạo bảng lớp, học sinh, giáo viên, ghi danh và buổi dạy. Nếu database mới hoàn toàn, chạy các migration nền của Organizations trước Academics; thao tác `Update-Database` ở hai context theo đúng thứ tự trên sẽ chạy toàn bộ migration chưa áp dụng của mỗi context.
5. Trong **pgAdmin**, mở database ứng dụng → **Schemas → organization → Tables → rooms**, **Schemas → academics → Tables**. Xác nhận có `classes`, `students`, `teachers`, `enrollments`, `teaching_sessions`. Có thể xem `__ef_migrations_history` của từng schema để xác nhận hai migration trên.
6. Chọn lại `TutorManagement.Api` → **Set as Startup Project**, **Build → Build Solution**, **F5**. Mở [Swagger local](http://localhost:5217/swagger/index.html). Nếu URL không phản hồi, xem URL `Now listening on` trong Output: cổng có thể khác theo launch profile.

## 2. Đăng nhập và lấy ID

Đăng nhập bằng tài khoản `CenterOwner` hoặc `CenterAdmin` của một trung tâm **Approved** qua `POST /api/v1/auth/login`. Lấy `data.accessToken`, bấm **Authorize** trong Swagger và dán token (không gõ thêm `Bearer`). Gọi `GET /api/v1/me/centers`, chọn `centerId` có `canAccess=true`. Token của `PlatformAdmin` không mặc định có quyền vận hành một trung tâm nếu không là thành viên.

Trước khi tạo lớp, ở nhóm `05 · Subjects` tạo hoặc chọn môn active, ghi `subjectId`. Ở nhóm `06 · Rooms` tạo hoặc chọn phòng active, ghi `roomId`. Mọi URL dưới đây có tiền tố `/api/v1/centers/{centerId}`. Trong Swagger chọn `Accept: application/json` để đọc kết quả thuận tiện. Thành công có `success`, `message`, `data`, `traceId`; lỗi có `errors[].code`, `errors[].message`, `errors[].field`.

## 3. Luồng đầy đủ nên thử theo thứ tự

1. **Tạo lớp** — `POST /classes`, body `{"subjectId":"<subjectId>","name":"Lớp thử 01","maxStudents":2}`. Kỳ vọng `201`, `data.code` dạng `LH-000001` (server tự sinh), `data.enrolledCount=0`, `data.version=0`. Ghi `classId`.
2. **Tạo hai học sinh** — hai lần `POST /students`: `{"fullName":"Học sinh Một","parentName":"Phụ huynh Một","parentPhone":"0912345678"}` và `{"fullName":"Học sinh Hai","parentName":"Phụ huynh Hai","parentPhone":"0987654321"}`. Kỳ vọng `201`, `data.code` là `HS-000001`, `HS-000002`… theo trung tâm; ghi hai `studentId`. Đây là hồ sơ học sinh theo trung tâm, chưa phải tài khoản đăng nhập.
3. **Ghi danh** — `POST /classes/{classId}/enrollments` với `{"studentId":"<studentId-1>"}`, sau đó học sinh 2. Kỳ vọng mỗi lần `201`; `GET /classes/{classId}` thấy `enrolledCount=2`, `version=2`.
4. **Thử vượt sĩ số** — tạo học sinh 3, ghi danh vào lớp trên. Kỳ vọng `409`, `class_full`, số học sinh vẫn là 2. Ghi danh lại học sinh 1 cũng phải `409`, `student_already_enrolled`.
5. **Rút học** — `DELETE /classes/{classId}/enrollments/{studentId-1}`. Kỳ vọng `200`, `status=Withdrawn`, `withdrawnAtUtc` có giá trị, `enrolledCount=1`; sau đó có thể ghi danh học sinh 3. `GET /classes/{classId}/enrollments` mặc định chỉ trả người đang học; dùng `?status=withdrawn` hoặc `?status=all` để xem lịch sử. Response có `data.items` phân trang, mã và tên học sinh. Cùng một học sinh có thể ghi danh lại về sau bằng bản ghi mới. Xem `API-RESPONSE-CONTRACT.md` để biết đầy đủ contract mới.
6. **Tạo giáo viên** — `POST /teachers` với `{"fullName":"Giáo viên Một"}`. Kỳ vọng `201`, `data.code` dạng `GV-000001`, ghi `teacherId`. Đây là hồ sơ tài nguyên giảng dạy, **không tự tạo tài khoản IAM** cho giáo viên.
7. **Xếp buổi dạy** — `POST /sessions` với `{"classId":"<classId>","teacherId":"<teacherId>","roomId":"<roomId>","startUtc":"2026-11-02T10:00:00Z","endUtc":"2026-11-02T12:00:00Z"}`. Hãy thay ngày bằng ngày **tương lai** khi test. Kỳ vọng `201`, ghi `sessionId`, `version=0`. Dùng UTC có hậu tố `Z`; giờ Việt Nam = UTC+7. Thời lượng phải 15 phút–8 giờ.
8. **Xem lịch** — `GET /sessions?fromUtc=2026-11-02T00:00:00Z&toUtc=2026-11-03T00:00:00Z` (điều chỉnh theo ngày đã chọn). Kỳ vọng `200` và có buổi vừa tạo. Khoảng tìm tối đa 31 ngày; buổi chạm mép khoảng vẫn được hiển thị nếu thực sự giao nhau.
9. **Thử trùng lịch** — tạo buổi 11:00–13:00 cùng lớp, cùng giáo viên hoặc cùng phòng. Kỳ vọng `409`, `schedule_conflict`. Buổi 12:00–13:00 **được phép** vì sát mép nhưng không chồng giờ. Muốn thử từng tài nguyên riêng, tạo thêm lớp/giáo viên/phòng để chỉ trùng một loại ID mỗi lần.
10. **Hủy buổi tương lai** — `POST /sessions/{sessionId}/cancel` body `{"expectedVersion":0}` → `200`, `isActive=false`, `version=1`. Hủy lại với version mới không thành công. Buổi đã bắt đầu không được hủy theo API này; nghiệp vụ điểm danh/hoàn thành sẽ xử lý ở bước phát triển khác.

## 4. Quản lý dữ liệu và danh sách

`GET /classes`, `/students`, `/teachers` mặc định chỉ hiện bản active, `page=1`, `pageSize=25`. Có thể thử `pageSize=1`, `page=2`; `data.items` chỉ có tối đa một dòng, còn `data.totalCount` là tổng số phù hợp với bộ lọc. `pageSize` hợp lệ 1–100, `page` 1–1.000.000; sai trả `400 invalid_pagination`. `includeArchived=true` hiện cả bản đã lưu trữ. Dữ liệu được phân trang ở DB, không đọc cả bảng rồi mới cắt.

`GET /classes/{classId}`, `/students/{studentId}`, `/teachers/{teacherId}` dùng để lấy bản hiện hành và `version`. `PUT /classes/{classId}` nhận `{"name":"Lớp thử đã đổi tên","maxStudents":3,"expectedVersion":<version-hiện-tại>}`. Không cho giảm `maxStudents` thấp hơn `enrolledCount`. `PUT /students/{studentId}` và `PUT /teachers/{teacherId}` nhận `fullName` và `expectedVersion`. Mã định danh không đổi qua PUT, tránh làm hỏng tham chiếu vận hành.

`POST .../archive` và `POST .../reactivate` nhận `{"expectedVersion":<version-hiện-tại>}`. Không thể archive lớp còn học sinh hoặc còn buổi hiện tại/tương lai; không thể archive học sinh còn ghi danh; không thể archive giáo viên hoặc phòng còn buổi hiện tại/tương lai. Hủy buổi và rút học trước. Archive không xóa DB; `GET` chi tiết theo ID vẫn cho xem lịch sử. Reactivate lớp đòi hỏi môn học còn active.

Môn học có lớp active cũng không được archive (`409 subject_has_active_classes`). Tạo/reactivate lớp và archive môn dùng cùng khóa giao dịch để không tạo ra lớp active gắn với môn vừa ngừng hoạt động.

## 5. Case lỗi và tranh chấp cần kiểm tra

| Tình huống | Kỳ vọng |
|---|---|
| Không token / token hết hạn | `401`; đăng nhập lại và Authorize. |
| Member không thuộc trung tâm hoặc trung tâm chưa được duyệt | `403` trước khi vào nghiệp vụ. |
| Member `Teacher`/`Staff` gọi POST/PUT/archive | `403`; chỉ Owner/Admin được sửa. |
| Dùng ID lớp/học sinh/giáo viên ở center khác | Không truy cập được tài nguyên; không chuyển dữ liệu xuyên center. |
| Gửi kèm `code` khi tạo lớp/học sinh/giáo viên | Bị bỏ qua: mã luôn do server sinh (D9), không bao giờ trùng trong một center; center khác có dãy số riêng. |
| Body thiếu ID, tên trống, `maxStudents=0/501` | `400 validation_error`, có tên field. |
| Sửa với `expectedVersion` cũ | `409 teaching_concurrent_change`; GET lại rồi quyết định có sửa tiếp không. |
| Hai người cùng ghi danh vào ghế cuối | Chỉ một giao dịch thành công; người còn lại nhận lỗi hết chỗ/xung đột, không vượt `maxStudents`. |
| Hai người cùng xếp buổi trùng lớp/giáo viên/phòng | Chỉ một buổi được ghi; request còn lại `409 schedule_conflict`. |
| Xếp phòng/giáo viên/lớp đã archive | Không được; resource phải active trong cùng center. |
| Archive phòng đúng lúc người khác xếp lịch | Hệ thống khóa chung tài nguyên phòng; kết quả cuối cùng không thể có buổi active tương lai tham chiếu phòng đã archive. |
| `GET /sessions` thiếu/sai khoảng hoặc hơn 31 ngày | `400 invalid_teaching_details`. |

Khi test hai request đồng thời, có thể mở hai cửa sổ Swagger. Đừng phụ thuộc vào việc click bằng tay đủ nhanh: test tự động `RealPostgresTeachingFlowTests` kiểm tra tranh chấp bằng hai `DbContext` cùng đọc một phiên bản trên PostgreSQL thật.

## 6. Xem DB và chạy test trong IDE

Trong **pgAdmin → academics → Tables**, xem `classes.enrolled_count`, `classes.max_students`, `classes.version`; `enrollments.withdrawn_at_utc`; `teaching_sessions.start_utc/end_utc/cancelled_at_utc`. FK và unique index giữ ràng buộc theo center; lớp không được tham chiếu môn của center khác, buổi không được tham chiếu phòng/giáo viên/lớp của center khác. Không tự chỉnh `enrolled_count`: đó là bộ đếm do workflow ghi danh/rút học duy trì trong transaction.

Trong Visual Studio, mở **Test → Test Explorer → Run All Tests** để chạy test thông thường. Test PostgreSQL thật là opt-in: chọn `backend/tests/IntegrationTests/AuthE2E.runsettings` ở **Test → Configure Run Settings → Select Solution Wide runsettings File**, sau đó lọc `RealPostgresTeachingFlowTests` rồi Run. Test này tạo trung tâm thử riêng và xóa đúng các dòng của nó khi kết thúc. Chỉ bật trên DB local dành cho phát triển, không dùng DB production.

Buổi dạy có thể tạo đơn lẻ như trên hoặc sinh từ mẫu lịch lặp. Xem [RECURRING-TEACHING-SCHEDULE-TEST.md](RECURRING-TEACHING-SCHEDULE-TEST.md) để test lịch lặp và [ATTENDANCE-FLOW-TEST.md](ATTENDANCE-FLOW-TEST.md) để test điểm danh. Học phí và lương chưa được triển khai; hồ sơ giáo viên chưa gắn với tài khoản đăng nhập; hồ sơ học sinh chỉ giữ mã và tên để không thu thập dữ liệu cá nhân không cần thiết ở giai đoạn này.
