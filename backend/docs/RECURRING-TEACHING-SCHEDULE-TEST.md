# Lịch dạy lặp: nghiệp vụ, setup và kiểm thử bằng Visual Studio/Swagger

## 1. Hiểu đúng hai loại dữ liệu

- **Mẫu lịch (`schedule_series`)** là quy tắc: lớp nào, giáo viên/phòng nào, thứ mấy, mấy giờ địa phương, từ ngày nào đến ngày nào. Mỗi lần tạo tối đa 180 ngày và buổi cuối phải kết thúc trong vòng một năm kể từ lúc tạo, để không có lịch vô hạn khó kiểm soát.
- **Buổi dạy (`teaching_sessions`)** là từng ngày thật được sinh từ mẫu. Mỗi buổi giữ `seriesId` và `occurrenceDate` (ngày gốc theo múi giờ của mẫu), ngay cả khi được dời sang giờ/ngày khác. Buổi tạo thủ công không có hai trường này.
- `excludedDates` chỉ bỏ qua các ngày cụ thể khi tạo. Hệ thống chưa tự biết lịch nghỉ lễ của trung tâm.
- Trạng thái buổi hiện tại là đang hoạt động hoặc đã hủy. Điểm danh có phiếu riêng theo từng buổi; xem [ATTENDANCE-FLOW-TEST.md](ATTENDANCE-FLOW-TEST.md). Buổi đã bắt đầu hoặc đã có phiếu không được dời/hủy theo mẫu.

Giờ trong request mẫu là **giờ địa phương** kèm `timeZoneId`. Mẫu dùng `Asia/Ho_Chi_Minh` thì 17:00 tương ứng 10:00 UTC. Buổi trả về `startUtc` và `endUtc` bằng UTC. Nếu múi giờ có DST, ngày/giờ không tồn tại hoặc bị lặp được báo lỗi 400; hệ thống không tự chọn một trong hai giờ mơ hồ.

Mỗi request tạo mẫu cần `requestId` là UUID do giao diện tạo **một lần cho một thao tác Lưu**. Nếu mạng chập chờn, gửi lại cùng UUID và cùng nội dung: HTTP 200, `replayed=true`, không sinh thêm buổi. UUID đã dùng với nội dung khác: 409. Swagger không tự sinh hộ: hãy tạo UUID mới bằng công cụ **Tools > Create GUID** trong Visual Studio (chọn định dạng không có dấu ngoặc nhọn), hoặc dùng một UUID của bạn. Khi thử replay, giữ nguyên UUID.

## 2. Setup trên IDE

1. Trong Visual Studio, dừng **TutorManagement.Api** và **TutorManagement.Worker** đang chạy bằng **Shift+F5** ở từng startup/debug session. Bước này tránh lỗi DLL bị khóa khi build.
2. Nhấp phải solution `TutorManagementSystem` → **Build Solution**. Nếu thành công mới chuyển tiếp.
3. Chọn **TutorManagement.DbMigrator** làm **Startup Project**. Mở **Tools > NuGet Package Manager > Package Manager Console**. Trong **Default project**, chọn `TutorManagement.Modules.Academics.Infrastructure` vì migration nằm tại đây. Gõ:

   ```powershell
   Update-Database -Context AcademicsDbContext -StartupProject TutorManagement.DbMigrator
   ```

   Lệnh này chỉ áp các migration Academics còn thiếu vào PostgreSQL cấu hình trong User Secrets. Migration lịch lặp là `20261020150228_AddRecurringTeachingSchedules`. Nếu báo `Done` và không có lỗi, mở pgAdmin → database `tutor_management` → Schemas → academics → Tables để thấy `schedule_series` và các cột `series_id`, `occurrence_date`, `rescheduled_at_utc` trong `teaching_sessions`. Ba ràng buộc `ex_sessions_class_time`, `ex_sessions_teacher_time`, `ex_sessions_room_time` chống trùng giờ ở DB thuộc migration Teaching Operations trước đó. Nếu migration báo có lịch cũ trùng, cần kiểm tra chính xác các buổi cũ và xử lý nghiệp vụ trước khi áp lại; không xóa hàng loạt.
4. Chuyển **TutorManagement.Api** thành startup và chạy **F5**. Mở Swagger theo URL Visual Studio in ra, thường là `http://localhost:5217/swagger/index.html`. Đăng nhập tài khoản quản lý trung tâm, chép `accessToken`, nhấn **Authorize** → dán `Bearer <accessToken>`.

## 3. Chuẩn bị dữ liệu

Trung tâm phải ở trạng thái **Approved** và tài khoản test có quyền **Management** tại trung tâm đó. Ghi lại:

- `centerId` từ `GET /api/v1/me/centers`;
- `classId` của lớp đang hoạt động từ `GET /api/v1/centers/{centerId}/classes`;
- `teacherId` của giáo viên đang hoạt động từ `GET /api/v1/centers/{centerId}/teachers`;
- `roomId` của phòng đang hoạt động từ `GET /api/v1/centers/{centerId}/rooms`.

Nếu thiếu, tạo môn học → lớp → giáo viên → phòng theo tài liệu [MANUAL-TEST-TEACHING-OPERATIONS.md](MANUAL-TEST-TEACHING-OPERATIONS.md). Giáo viên và phòng phải thuộc **cùng center**. Nếu đã có buổi dạy trong khung giờ định test, hãy chọn giờ/ngày khác để bước đầu không xung đột.

Các ngày trong ví dụ dưới đây phù hợp để thử ngay sau 20/10/2026. Nếu đã qua tháng 11/2026, thay toàn bộ ngày bằng **bốn ngày thứ Hai tương lai liên tiếp**. Mọi ngày trong request là `yyyy-MM-dd`; `localStartTime` là `HH:mm:ss`.

## 4. Tạo lịch lặp và kiểm tra kết quả

### Bước A: Xem trước, chưa ghi DB

Trong Swagger, mở `POST /api/v1/centers/{centerId}/schedule-series/preview`, bấm **Try it out**, điền `centerId` và body:

```json
{
  "requestId": "11111111-2222-4333-8444-555555555555",
  "classId": "<classId>",
  "teacherId": "<teacherId>",
  "roomId": "<roomId>",
  "timeZoneId": "Asia/Ho_Chi_Minh",
  "startDate": "2026-11-02",
  "endDate": "2026-11-23",
  "localStartTime": "17:00:00",
  "durationMinutes": 120,
  "weekdays": ["Monday"],
  "excludedDates": ["2026-11-09"]
}
```

Thay các giá trị `<...>` bằng GUID thật; không giữ dấu `< >`. Kết quả 200: `sessionCount=3`, các `occurrenceDate` là 02, 16, 23/11 và từng `conflicts=[]`. Ngày 09/11 không xuất hiện. `startUtc` mỗi buổi là `10:00:00+00:00`. Lúc này chưa có mẫu hay buổi mới trong DB.

`weekdays` dùng tên tiếng Anh đầy đủ, không lặp: `Sunday`, `Monday`, `Tuesday`, `Wednesday`, `Thursday`, `Friday`, `Saturday`. Có thể chọn nhiều ngày. `excludedDates` chỉ được chứa ngày thuộc những thứ đã chọn và nằm trong khoảng ngày.

### Bước B: Lưu thật

Gửi **đúng body ở bước A** vào `POST /api/v1/centers/{centerId}/schedule-series`. Kỳ vọng HTTP **201**, `data.series.id` là `seriesId`, `sessionsCreated=3`, `replayed=false`. Trong pgAdmin, bảng `academics.schedule_series` có 1 hàng mới và `academics.teaching_sessions` có 3 hàng với cùng `series_id`.

Gửi lại **đúng body**: HTTP **200**, vẫn là `seriesId` cũ, `replayed=true`, số hàng không đổi. Chỉ sửa `roomId` nhưng giữ `requestId`: HTTP **409**, mã `schedule_request_id_reused`, không tạo mẫu mới.

`GET /api/v1/centers/{centerId}/schedule-series/{seriesId}` xem mẫu; `GET /schedule-series?classId=<classId>&page=1&pageSize=25` xem danh sách có phân trang. `GET /api/v1/centers/{centerId}/sessions?fromUtc=2026-11-01T00:00:00Z&toUtc=2026-11-30T00:00:00Z` xem từng buổi; khoảng truy vấn tối đa 31 ngày. Các buổi trả `seriesId`, `occurrenceDate`, `isActive=true`, `isRescheduled=false`, `version=0`.

### Bước C: Thử xung đột

Với giờ 17:00–19:00 của buổi 02/11, gọi lại preview bằng `requestId` mới và lịch bắt đầu 18:00 trên cùng ngày; phải có `conflicts` nêu tài nguyên bận. Gọi Create tương ứng phải trả **409 `schedule_conflict`** và **không sinh một phần lịch**.

Kiểm thử lần lượt ba loại trùng giờ:

1. Giữ `classId`, đổi sang giáo viên và phòng khác: trùng **lớp**.
2. Đổi lớp/phòng, giữ `teacherId`: trùng **giáo viên**.
3. Đổi lớp/giáo viên, giữ `roomId`: trùng **phòng**.

Mỗi lần dùng `requestId` mới. Nếu cùng tài nguyên nhưng buổi sau bắt đầu **đúng lúc 19:00**, hai khoảng sát nhau là hợp lệ. Buổi qua nửa đêm cũng được tính xung đột theo UTC thật; ngày gốc vẫn là ngày bắt đầu địa phương.

### Bước D: Dời riêng một buổi

Lấy `sessionId` và `version=0` của ngày 02/11. Gọi `PUT /api/v1/centers/{centerId}/sessions/{sessionId}`:

```json
{
  "teacherId": "<teacherId>",
  "roomId": "<roomId>",
  "startUtc": "2026-11-02T11:00:00Z",
  "endUtc": "2026-11-02T13:00:00Z",
  "expectedVersion": 0
}
```

Kỳ vọng 200: buổi đó chuyển từ 17:00–19:00 thành 18:00–20:00 giờ Việt Nam; `isRescheduled=true`, `version=1`, `occurrenceDate` vẫn là 02/11. Các buổi 16 và 23/11 không thay đổi. Gửi tiếp với `expectedVersion=0`: 409 `teaching_concurrent_change`; phải đọc lại `version` mới trước khi sửa. Nếu giờ mới trùng lớp/giáo viên/phòng, nhận 409 `schedule_conflict`, bản cũ còn nguyên.

Muốn hủy **một** buổi, dùng `POST /sessions/{sessionId}/cancel` với `{"expectedVersion":1}`; các buổi khác còn hoạt động. Không hủy buổi đã bắt đầu.

### Bước E: Thay lịch từ một ngày trở đi

Giả sử muốn đổi giờ từ thứ Hai 16/11, dùng `POST /api/v1/centers/{centerId}/schedule-series/{seriesId}/replace-future`:

```json
{
  "fromDate": "2026-11-16",
  "expectedVersion": 0,
  "replacement": {
    "requestId": "66666666-7777-4888-8999-aaaaaaaaaaaa",
    "classId": "<classId>",
    "teacherId": "<teacherId>",
    "roomId": "<roomId>",
    "timeZoneId": "Asia/Ho_Chi_Minh",
    "startDate": "2026-11-16",
    "endDate": "2026-11-23",
    "localStartTime": "18:00:00",
    "durationMinutes": 120,
    "weekdays": ["Monday"],
    "excludedDates": []
  }
}
```

Kết quả 200: hai buổi cũ từ 16/11 trở đi bị hủy (vẫn xem được trong lịch sử), tạo hai buổi mới lúc 18:00–20:00, trả `replacement.series.id` mới. Buổi gốc 02/11 vẫn nguyên trạng, kể cả nếu đã dời riêng. Mẫu cũ có `stoppedFromDate=2026-11-16`. Lặp lại đúng request body: nhận `replayed=true`, không sinh thêm. Cùng `requestId` mới nhưng nội dung khác: 409.

Nếu lịch mới xung đột, API trả 409 và **cả mẫu cũ lẫn các buổi cũ vẫn hoạt động**; không có trạng thái thay dở. `replacement.classId` và `timeZoneId` phải khớp mẫu gốc. Buổi đã được hủy riêng trước đó cũng có thể được tạo lại theo quy tắc thay thế mới, vì quy tắc mới là lịch mới từ ngày này trở đi.

### Bước F: Dừng lịch từ một ngày

Lấy `seriesId` mới và `version=0` của mẫu thay thế. Gọi `POST /schedule-series/{seriesId}/stop`:

```json
{"fromDate":"2026-11-23","expectedVersion":0}
```

Kết quả 200: `sessionsCancelled=1`, mẫu có `stoppedFromDate=2026-11-23`, buổi 16/11 mới vẫn hoạt động; buổi 23/11 mới đã hủy. Dùng version cũ lần nữa nhận 409. Không thể dừng từ ngày đã qua hoặc đụng vào buổi đã bắt đầu.

## 5. Các trường hợp cần kiểm tra thêm

| Tình huống | Kết quả |
|---|---|
| Thiếu `requestId`, sai GUID hoặc `weekdays` trống/lặp | 400, lỗi validation theo field |
| Khoảng ngày >180 ngày, buổi cuối xa hơn một năm, thời lượng <15 hoặc >480 phút | 400; với buổi quá xa có mã `schedule_too_far` |
| Loại trừ toàn bộ các ngày đã chọn | 400 `no_schedule_occurrences` |
| Giờ địa phương nằm trong khoảng DST bị bỏ hoặc lặp | 400 `invalid_schedule_local_time`, nêu ngày xảy ra |
| Một buổi đã ở quá khứ hoặc bắt đầu trong lúc request chờ khóa | 400 `schedule_not_future`, không tạo buổi |
| Giáo viên/phòng/lớp của trung tâm khác hoặc đã ngừng hoạt động | 409 với mã lỗi tài nguyên tương ứng |
| Student hoặc thành viên không có quyền Management tạo/sửa lịch | 403 |
| Gọi bằng `centerId` không có quyền | 403; dùng token không hợp lệ → 401 |
| Hai nhân viên sửa cùng mẫu/buổi bằng cùng version | Chỉ một request thành công; request còn lại 409 |
| Hai request cùng đặt phòng/giáo viên/lớp trong giờ giao nhau | DB chỉ cho một request thành công; request còn lại 409 |

`preview` là ảnh chụp thời điểm kiểm tra. Người khác có thể xếp lịch sau khi bạn xem trước; thao tác Create/Replace **kiểm tra lại trong giao dịch** và DB vẫn chặn xung đột. Các request thay đổi đã bắt đầu không được phép để sau này điểm danh, học phí và lương dựa trên buổi cũ một cách ổn định.

## 6. Chạy test tự động ngay trong Visual Studio

Vào **Test > Configure Run Settings > Select Solution Wide runsettings File** và chọn `tests/IntegrationTests/AuthE2E.runsettings`. Trong **Test Explorer**, tìm `RealPostgresRecurringScheduleTests` → **Run**. Test tự tạo center/lớp/phòng/giáo viên riêng, kiểm tra preview, chống trùng tại API và PostgreSQL, replay, dời buổi, rollback khi thay lịch xung đột, stop và giờ DST mơ hồ; cuối cùng tự dọn dữ liệu test. Chỉ chạy với DB local. Sau test có thể bỏ chọn runsettings này.
