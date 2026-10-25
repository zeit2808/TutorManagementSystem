# Bước tiếp theo: chọn trung tâm và phân quyền theo membership

## Nghiệp vụ vừa hoàn thành

Một tài khoản có thể thuộc nhiều trung tâm. Frontend gọi `GET /api/v1/me/centers` để hiện danh sách, sau đó giữ `centerId` đang chọn trong route/UI và gửi nó trong URL của từng API theo trung tâm. Server **không** lưu một “trung tâm hiện hành” toàn cục trong session hoặc JWT. Mỗi request dùng `sub` từ access token và `centerId` trên route để tra membership còn hiệu lực trong PostgreSQL.

`GET /api/v1/centers/{centerId}/context` chỉ thành công khi user đang hoạt động, email đã xác minh, membership còn active và center ở trạng thái `Approved`. `PlatformAdmin` không mặc nhiên là thành viên của mọi trung tâm. Center bị suspend vẫn hiện trong `/me/centers` với `canAccess: false` để UI giải thích lý do; các API cần quyền center trả 403 ngay từ request kế tiếp. Membership bị tắt thì không còn trong danh sách và cũng không truy cập được.

Policy đã có để gắn vào những controller kế tiếp:

| Policy | Vai trò trong `iam.center_memberships` | Mục đích |
|---|---|---|
| `CenterMember` | Owner, Admin, Teacher, Staff | Xem dữ liệu của trung tâm |
| `CenterManagement` | Owner, Admin | Quản lý vận hành |
| `CenterOwner` | Owner | Thao tác chỉ chủ trung tâm |

Policy lấy `centerId` từ route, nên controller dùng policy phải có route parameter tên `centerId`. Không dùng global role claim để quyết định quyền trong trung tâm. Kết quả tra center được cache **chỉ trong scope của một HTTP request**, không cache xuyên request; vì vậy thay đổi quyền/trạng thái có hiệu lực ngay ở request tiếp theo.

PlatformAdmin có thêm các thao tác review: `POST /api/v1/platform/centers/{centerId}/reject`, `/suspend`, `/reinstate`. Body đều là `{"reason":"Lý do rõ ràng"}`; reason bắt buộc và tối đa 1.000 ký tự. Chuyển trạng thái hợp lệ: `PendingApproval → Rejected`, `Approved → Suspended`, `Suspended → Approved`. Duyệt ban đầu dùng `/approve`; `/approve` không tự mở khóa center suspended. Các thao tác này trả 400 khi reason không hợp lệ, 404 nếu ID không tồn tại, 409 nếu sai trạng thái.

## Cách chạy và test trong Visual Studio

1. Dừng phiên debug cũ bằng **Shift+F5** trước khi Build nếu IDE báo DLL bị khóa.
2. Trong **Solution Explorer**, chọn `TutorManagement.Api` làm **Startup Project**. Không cần chạy migration cho bước này vì chỉ dùng các bảng/cột đã có: `organization.centers`, `iam.center_memberships`, `iam.users`.
3. Bấm **Build → Build Solution**, rồi **Test → Run All Tests**. Bộ test mới kiểm tra 401/403/200, center suspended và quy tắc chuyển trạng thái; test không ghi dữ liệu vào DB local.
4. Bấm **F5** và mở Swagger theo URL trong `launchSettings.json` (thường là `http://localhost:5217/swagger/index.html`).
5. Login bằng tài khoản `CenterOwner` đã accept invitation trong hướng dẫn trước. Copy `data.accessToken`, bấm **Authorize** trong Swagger, dán token **không** thêm chữ `Bearer`.
6. Gọi `GET /api/v1/me/centers`. Phải thấy center đã duyệt với `role: "CenterOwner"`, `status: "Approved"`, `canAccess: true`. Copy `centerId`.
7. Gọi `GET /api/v1/centers/{centerId}/context`. Kết quả đúng là 200. Đổi `centerId` sang center khác hoặc GUID bất kỳ: 403. Bỏ authorization: 401.
8. Login tài khoản `PlatformAdmin`, Authorize bằng access token mới. Gọi `POST /api/v1/platform/centers/{centerId}/suspend` với `{"reason":"Tạm dừng để rà soát"}`. Kết quả là `status: "Suspended"`. Đăng nhập lại CenterOwner (hoặc dùng token còn hạn): `/me/centers` cho `canAccess: false`; `/context` trả 403.
9. Dùng PlatformAdmin gọi `/reinstate` với `{"reason":"Đã hoàn tất rà soát"}`. CenterOwner gọi lại `/context` và nhận 200. Thử suspend lần hai khi center đang suspended: 409, không âm thầm thay đổi trạng thái.

Trong **pgAdmin**, có thể nhấp phải `organization.centers` và `iam.center_memberships` → **View/Edit Data → All Rows** để quan sát `status`, `review_note`, `is_active`, `role` và `center_id`. Đừng sửa trực tiếp để thử quyền nếu đang dùng dữ liệu thật; dùng API review để domain rules được áp dụng.

## Phân tách kiến trúc

- `Organizations.Domain` giữ invariant của trạng thái center; `Organizations.Application` xử lý use case review; `Organizations.Infrastructure` lưu thay đổi; `Organizations.Api` chỉ nhận/validate request và ánh xạ HTTP.
- `IdentityAccess.Infrastructure` đọc membership active cùng trạng thái user; `IdentityAccess.Application` tổng hợp membership + center snapshot thành context; `IdentityAccess.Api` áp policy và expose hai GET endpoint.
- Hợp đồng đọc center nằm trong `Organizations.Contracts`, tránh IdentityAccess tham chiếu trực tiếp DbContext của Organizations. Không có migration mới trong bước này.

Lưu ý: hiện policy mới được áp vào endpoint `/context`. Khi triển khai các nghiệp vụ lớp học, học sinh, học phí... cần đặt `centerId` trong route và gắn policy phù hợp vào **từng endpoint**; không coi việc frontend đã chọn center là một kiểm tra bảo mật.
