# Hướng dẫn test tay: Môn học và Phòng học theo trung tâm

Tài liệu này dành cho **Môn học** (`05 · Subjects`) và **Phòng học** (`06 · Rooms`). Đây là hai danh mục nền cho lớp học/lịch dạy. Các chức năng lớp, ghi danh và xếp lịch đã có ở [hướng dẫn kiểm thử nghiệp vụ dạy học](MANUAL-TEST-TEACHING-OPERATIONS.md).

## 1. Trạng thái database và chuẩn bị trong Visual Studio

Tại thời điểm kiểm tra **19/10/2026** trên PostgreSQL local của dự án:

| Migration | Trạng thái |
|---|---|
| `organization.20261018174147_AddCenterRooms` | Đã áp dụng; `organization.rooms` đã có |
| `academics.20261018172351_AddCenterSubjects` | Đã áp dụng trên DB local đã kiểm thử; nếu dùng DB khác, thực hiện các bước bên dưới |

Để áp dụng migration Môn học bằng giao diện Visual Studio:

1. Bấm **Shift+F5** để dừng API đang debug. Việc này tránh lỗi DLL bị khóa khi build.
2. Trong **Solution Explorer**, chọn `TutorManagement.DbMigrator` làm **Startup Project** (nhấp phải project → **Set as Startup Project**).
3. Mở **Tools → NuGet Package Manager → Package Manager Console**.
4. Ở ô **Default project**, chọn **`TutorManagement.Modules.Academics.Infrastructure`**. Đây là project chứa `AcademicsDbContext` và migration Môn học.
5. Gõ một lệnh trong Package Manager Console:

   ```powershell
   Update-Database -Context AcademicsDbContext -StartupProject TutorManagement.DbMigrator
   ```

6. Chờ dòng kết thúc `Done.`. Trong **pgAdmin → Databases → database của ứng dụng → Schemas → academics → Tables**, bấm **Refresh** và xác nhận có bảng `subjects`. Nếu muốn kiểm tra migration, mở `academics.__ef_migrations_history` bằng **View/Edit Data → All Rows**; phải thấy `20261018172351_AddCenterSubjects`.
7. Chọn lại `TutorManagement.Api` làm **Startup Project**. Bấm **Build → Build Solution**, rồi **F5**. Mở [Swagger local](http://localhost:5217/swagger/index.html). Nếu vẫn không thấy nhóm `05 · Subjects` và `06 · Rooms`, hãy xác nhận đã dừng tiến trình API cũ rồi F5 lại.

**Không cần chạy lại migration Phòng học** trên DB local hiện tại. Nếu bạn tạo một database hoàn toàn mới, cần áp dụng migration của các DbContext theo thứ tự phụ thuộc (đặc biệt bảng `organization.centers` phải có trước FK của `academics.subjects`); dùng quy trình setup/migrator chung của dự án thay vì chỉ chạy riêng lệnh trên.

## 2. Có tài khoản và `centerId` để test

Hai danh mục chỉ hoạt động trong **trung tâm đã được duyệt** (`Approved`). Tài khoản dùng để tạo/sửa phải là `CenterOwner` hoặc `CenterAdmin` đang có membership active của chính trung tâm đó. `PlatformAdmin` **không tự động** có quyền quản lý danh mục của mọi trung tâm.

1. Trong Swagger, gọi `POST /api/v1/auth/login` bằng `username` và `password` của tài khoản **chủ/quản lý trung tâm** đã xác minh email. Ví dụ cấu trúc body:

   ```json
   {"username":"<username-cua-ban>","password":"<password-cua-ban>"}
   ```

2. Kết quả thành công là `200`, lấy `data.accessToken`. Refresh token nằm trong **HttpOnly cookie**, không nằm trong JSON; bước test danh mục này chỉ cần access token.
3. Bấm **Authorize** ở đầu Swagger, dán **giá trị access token**, không thêm chữ `Bearer` (Swagger tự ghép). Đặt **Accept = application/json** khi thử endpoint.
4. Gọi `GET /api/v1/me/centers`. Chọn dòng có `status: "Approved"`, `canAccess: true` và `role: "CenterOwner"` hoặc `"CenterAdmin"`; sao chép `centerId`. Có thể gọi tiếp `GET /api/v1/centers/{centerId}/context` để xác nhận quyền, kỳ vọng `200`.

Nếu `/me/centers` rỗng, `canAccess` là `false`, hoặc role chỉ là `Teacher`/`Staff`, bạn cần hoàn tất luồng mời chủ trung tâm/duyệt trung tâm hoặc dùng tài khoản quản lý đúng. Đừng lấy ngẫu nhiên ID từ bảng `centers`: route còn kiểm tra membership của người đang đăng nhập.

Trong mọi response thành công, đọc dữ liệu ở `data`; response có dạng `success`, `message`, `data`, `errors`, `traceId`. Khi lỗi, xem `errors[0].code` và `errors[0].field`, đừng chỉ xem HTTP status.

## 3. Luồng thành công: Môn học

Thực hiện trong nhóm **`05 · Subjects`**. Thay `{centerId}` bằng ID ở bước 2. Nên dùng tên mới để không đụng dữ liệu test cũ, ví dụ thêm hậu tố `01` hoặc `02`.

| Bước trong Swagger | Giá trị nhập | Kết quả phải thấy |
|---|---|---|
| `POST /api/v1/centers/{centerId}/subjects` | `{"name":"Toán thử 01","description":"Môn dùng để test"}` | `201`; `data.code` do server sinh dạng `MH-000001`, `data.isActive=true`, `data.version=0`. Ghi lại `data.id` làm `{subjectId}`. |
| `GET /api/v1/centers/{centerId}/subjects` | `includeArchived=false` hoặc bỏ trống | `200`; danh sách có môn vừa tạo. |
| `GET /api/v1/centers/{centerId}/subjects/{subjectId}` | ID vừa tạo | `200`; đúng ID, mã, tên, mô tả. `centerId` lấy từ URL nên không lặp trong response. |
| `PUT /api/v1/centers/{centerId}/subjects/{subjectId}` | `{"name":"Toán thử nâng cao 01","description":"Đã sửa","expectedVersion":0}` | `200`; thông tin đổi, `data.code` giữ nguyên, `version=1`. |
| `POST /api/v1/centers/{centerId}/subjects/{subjectId}/archive` | `{"expectedVersion":1}` | `200`; `isActive=false`, `version=2`. Audit archive vẫn lưu trong DB, không trả trong DTO thường ngày. |
| Gọi lại `GET` danh sách | `includeArchived=false`, sau đó `true` | Môn **không có** trong danh sách mặc định nhưng **có** khi `includeArchived=true`. `GET` chi tiết theo ID vẫn đọc được. |
| `POST /api/v1/centers/{centerId}/subjects/{subjectId}/reactivate` | `{"expectedVersion":2}` | `200`; `isActive=true`, `version=3`; môn lại xuất hiện trong danh sách mặc định. |

Mã môn (`MH-…`) và mã phòng (`PH-…`) do server sinh theo từng center (D9), không nhận từ request và không đổi qua `PUT`. Tên được trim và gộp khoảng trắng; so trùng tên **không phân biệt chữ hoa/chữ thường**. `description` tùy chọn, tối đa 500 ký tự. Không có `DELETE`: archive giữ lịch sử và giúp các luồng lớp/lịch sau này không bị mất tham chiếu.

## 4. Luồng thành công: Phòng học

Thực hiện trong nhóm **`06 · Rooms`**, cũng dùng `{centerId}` đã chọn. Phòng thuộc schema `organization` vì đó là tài nguyên vật lý của trung tâm; môn học thuộc schema `academics`.

| Bước trong Swagger | Giá trị nhập | Kết quả phải thấy |
|---|---|---|
| `POST /api/v1/centers/{centerId}/rooms` | `{"name":"Phòng thử 01","locationNote":"Tầng 2"}` | `201`; `data.code` dạng `PH-000001`, `data.isActive=true`, `data.version=0`. Ghi lại `data.id` làm `{roomId}`. |
| `GET /api/v1/centers/{centerId}/rooms` và `GET .../rooms/{roomId}` | `includeArchived=false` | `200`; thấy đúng phòng và vị trí, không còn trường `capacity`. |
| `PUT /api/v1/centers/{centerId}/rooms/{roomId}` | `{"name":"Phòng thử 01","locationNote":"Tầng 3","expectedVersion":0}` | `200`; `locationNote="Tầng 3"`, `version=1`. |
| `POST /api/v1/centers/{centerId}/rooms/{roomId}/archive` | `{"expectedVersion":1}` | `200`; `isActive=false`, `version=2`. |
| Gọi `GET` danh sách | `includeArchived=false`, sau đó `true` | Phòng bị ẩn trong danh sách mặc định nhưng vẫn có khi yêu cầu cả phòng lưu trữ. |
| `POST /api/v1/centers/{centerId}/rooms/{roomId}/reactivate` | `{"expectedVersion":2}` | `200`; `isActive=true`, `version=3`. |

`locationNote` tùy chọn, tối đa 200 ký tự và không chứa ký tự điều khiển. Giới hạn học sinh thuộc lớp học (`maxStudents`), không thuộc phòng; hiện lịch dạy đã kiểm tra trùng phòng, lớp và giáo viên.

## 5. Các case lỗi và phân quyền phải test

Làm với **cả Subjects và Rooms** trừ những dòng ghi riêng. Để không ảnh hưởng dữ liệu đã tạo, dùng tên mới hoặc ID từ luồng thành công. Nếu một case đã làm thay đổi `version`, hãy đọc lại `GET` chi tiết trước case tiếp theo.

| Case | Cách thử | Kết quả mong đợi / giải thích |
|---|---|---|
| Không đăng nhập | Bấm **Authorize → Logout** rồi gọi `GET` danh sách | `401`: API yêu cầu access token. |
| Sai quyền thao tác | Đăng nhập `Teacher`/`Staff` có membership của center rồi gọi `POST`, `PUT`, `archive` hoặc `reactivate` | `403`: thành viên được xem nhưng chỉ Owner/Admin được quản lý. |
| PlatformAdmin không là thành viên | Login PlatformAdmin không có membership rồi gọi API danh mục của center | `403`: quyền toàn hệ thống không tự biến thành quyền vận hành của từng center. |
| Trung tâm khác hoặc bị tạm dừng | Dùng token của Owner ở center A gọi route center B không thuộc mình; hoặc dùng center `Suspended` | `403`: authorization chặn trước khi vào nghiệp vụ. |
| ID bản ghi thuộc center khác | Nếu cùng tài khoản có quyền ở cả A và B, tạo mục ở A rồi `GET/PUT/archive` bằng URL center B và ID mục ở A | `404` (`subject_not_found`/`room_not_found`): repository luôn lọc theo **cả** `centerId` và ID bản ghi. |
| Gửi kèm `code` | Thêm `"code":"ABC"` vào body tạo/sửa | Bị bỏ qua: mã luôn do server sinh và không đổi. |
| Tên trùng | Tên giống mục cũ, chỉ đổi hoa/thường hoặc khoảng trắng | `409`, `subject_name_already_exists` hoặc `room_name_already_exists`. |
| Tạo tại center khác | Nếu bạn có quyền quản lý một center B đã duyệt, dùng lại chính tên ở B | `201`: unique áp dụng **theo từng center**; mã ở B có dãy số riêng bắt đầu từ `-000001`. |
| Dữ liệu không hợp lệ | Tên toàn khoảng trắng; mô tả môn dài 501 ký tự; vị trí phòng dài 201 ký tự | `400`, lỗi `validation_error`, có `field` tương ứng. Tên dài 2–100 ký tự. |
| Thiếu `expectedVersion` | Gọi `PUT`, `archive` hoặc `reactivate` nhưng bỏ trường này | `400`: không cho cập nhật mù khi không biết bản hiện hành. |
| `expectedVersion` cũ | Sau khi sửa thành version 1, tiếp tục gửi version 0 | `409`, `subject_concurrent_change`/`room_concurrent_change`. Đọc lại bản mới rồi quyết định có áp dụng thay đổi không. |
| Sửa mục đã archive | Archive rồi `PUT` với đúng version hiện tại | `409`, `subject_archived_cannot_edit`/`room_archived_cannot_edit`; phải reactivate trước. |
| Archive hoặc reactivate lặp lại | Gọi lần hai với **version hiện tại** | `409`, `*_already_archived` hoặc `*_already_active`; khác với stale version. |
| Archive môn đang có lớp active | Tạo lớp dùng môn đó rồi archive môn | `409 subject_has_active_classes`; archive lớp trước. |
| Dùng lại tên đã archive | Archive một mục rồi tạo mục khác cùng tên | `409`: soft archive **không giải phóng** tên. Muốn dùng lại, reactivate và sửa mục cũ. |
| ID không tồn tại | Dùng một GUID mới cho `{subjectId}` hoặc `{roomId}` trong center mà mình có quyền | `404`. |

### Case hai người sửa cùng lúc

Mở hai cửa sổ Swagger hoặc hai người cùng đăng nhập với quyền quản lý center. Cả hai `GET` cùng bản ghi và đều thấy `version=0`. Người A `PUT` với version 0 → `200`, version mới là 1. Người B vẫn `PUT` với version 0 → `409`. Đó là **optimistic concurrency**: hệ thống không khóa giao diện của người B, nhưng không cho bản cũ ghi đè bản mới. Nếu hai request thực sự đến sát nhau, EF Core còn kiểm tra version khi `UPDATE` ở DB; unique index PostgreSQL bảo vệ trường hợp hai request tạo cùng mã/tên đồng thời.

## 6. Kiểm tra dữ liệu bằng pgAdmin (chỉ xem, không sửa tay)

Trong **pgAdmin**, mở database mà API đang kết nối:

- **Schemas → academics → Tables → subjects → View/Edit Data → All Rows**: xem `center_id`, `code`, `normalized_code`, `name`, `normalized_name`, `archived_at_utc`, `version`.
- **Schemas → organization → Tables → rooms → View/Edit Data → All Rows**: xem các trường trên cùng `location_note`; cột `capacity` đã được bỏ.
- Kiểm tra `center_id` của mỗi dòng phải là trung tâm bạn đã chọn. Sau archive, dòng **vẫn còn**, chỉ có `archived_at_utc`/`archived_by_user_id` và version thay đổi; sau reactivate hai trường archive trở lại `NULL`.
- Dưới **Indexes** của từng bảng, có unique index theo `(center_id, normalized_code)` và `(center_id, normalized_name)`. `subjects.center_id` có FK xuyên schema về `organization.centers.id`; `rooms.center_id` cũng có FK về `organization.centers.id`.

Không đổi trạng thái hoặc version trực tiếp trong pgAdmin để “test nhanh”: làm thế sẽ bỏ qua kiểm tra phân quyền, domain rules và audit fields của API.

## 7. Chạy test tự động bằng Visual Studio

Mở **Test → Test Explorer → Run All Tests**, hoặc lọc theo `SubjectCatalogTests`, `RoomCatalogTests`, `CenterAccessApiTests`. Các test này kiểm tra quy tắc nghiệp vụ, normalize/unique, center isolation, archive/reactivate, version và authorization HTTP mà **không ghi dữ liệu vào DB local**. Lần chạy gần nhất: **3 architecture tests đạt, 84 integration tests đạt; 3 real-Postgres auth tests dạng opt-in được skip**. Test tự động không thay thế các bước Swagger + pgAdmin ở trên, đặc biệt khi kiểm tra migration trên DB của bạn.

## 8. Khi test không đúng kỳ vọng

- **Swagger không mở / không có nhóm mới:** dừng debug bằng Shift+F5, chọn lại `TutorManagement.Api`, Build Solution rồi F5. URL mặc định là [http://localhost:5217/swagger/index.html](http://localhost:5217/swagger/index.html).
- **Môn học lỗi DB “relation academics.subjects does not exist”:** `AddCenterSubjects` chưa áp dụng; quay lại mục 1. Đừng tạo bảng bằng tay.
- **Phòng học lỗi DB tương tự:** kiểm tra bạn đang kết nối đúng database và có migration `AddCenterRooms` trong `organization.__ef_migrations_history`.
- **401:** đăng nhập lại, copy `data.accessToken` mới, Authorize lại. Refresh cookie không thay thế access token cho các API này.
- **403:** kiểm tra `GET /me/centers`: membership active, đúng `centerId`, role và center `Approved`. Đổi token PlatformAdmin thành token Owner/Admin của center nếu đang test thao tác quản lý.
- **406:** API hỗ trợ `Accept: application/json` và `application/xml`, kể cả danh sách có dữ liệu. `text/csv` hoặc media type không hỗ trợ phải trả `406`. Nếu XML trả `406`/`500`, đó là lỗi cần báo lại, không phải tình huống bình thường.
- **409:** đọc `errors[0].code`. Mã/tên trùng thì dùng dữ liệu khác; version cũ thì GET lại; đã archive thì reactivate trước khi sửa.

Sau khi hoàn tất, bạn có thể để lại các bản ghi test để kiểm tra soft archive, hoặc archive chúng qua API. Nếu môn/phòng đang được lớp hay buổi dạy tham chiếu, hãy xử lý các phụ thuộc nghiệp vụ trước; không xóa trực tiếp trong DB.
