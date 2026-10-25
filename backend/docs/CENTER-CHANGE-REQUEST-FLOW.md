# Thử luồng yêu cầu thay đổi thông tin trung tâm

Hướng dẫn cho Visual Studio, PostgreSQL local và Swagger. Đây là **quy trình duyệt nội bộ**:
ứng dụng không kết nối hay tự xác minh dữ liệu trên hệ thống nhà nước. Admin phải đối chiếu
nguồn chính thức theo quy trình vận hành trước khi bấm duyệt.

## Quy tắc nghiệp vụ

Chủ trung tâm (CenterOwner) đọc hồ sơ, gửi **một đề xuất đầy đủ** gồm tên pháp lý,
tên hiển thị, mã số thuế, số đăng ký kinh doanh, địa chỉ đăng ký, email và số điện thoại
liên hệ. Các giá trị không đổi cũng phải gửi đúng như hồ sơ hiện tại. Hệ thống ghi bản
gốc và bản đề xuất; trung tâm **không thay đổi** khi đơn `Pending`. Mỗi trung tâm chỉ
có một đơn `Pending` tại một thời điểm. Admin chỉ có thể `Approved` hoặc `Rejected`;
phải ghi lý do/ghi chú. Khi duyệt, mọi trường được cập nhật cùng lúc với trạng thái
đơn trong một transaction. Đơn đã xử lý không được xử lý lần nữa. Nếu hồ sơ gốc đã
thay đổi trong lúc chờ, API trả xung đột để admin xem lại.

`contactEmail` là email **liên hệ của trung tâm**, tách biệt với email đăng nhập đã
xác minh của CenterOwner (`iam.users`). Duyệt thay đổi contactEmail không đổi email
đăng nhập và cũng không tự chứng minh người gửi có quyền kiểm soát hộp thư liên hệ.
`evidenceReference` chỉ là mã/mô tả tài liệu để admin tra cứu: chưa có upload tệp,
chữ ký số hay kết nối dữ liệu nhà nước.

## Chuẩn bị migration trong Visual Studio

1. Dừng API/Worker đang Debug bằng **Stop** để tránh khóa DLL; mở
   `TutorManagementSystem.sln`, chọn **Build → Build Solution**.
2. Đảm bảo PostgreSQL local đang chạy và User Secrets của `TutorManagement.DbMigrator`
   trỏ đúng DB `tutor_management` như hướng dẫn [setup local](VISUAL-STUDIO-LOCAL-SETUP.md).
3. Trong Solution Explorer, chuột phải `TutorManagement.DbMigrator` → **Set as Startup Project**.
   Vào **Tools → NuGet Package Manager → Package Manager Console**. Ở ô **Default
   project** chọn `TutorManagement.Modules.Organizations.Infrastructure` (project chứa
   `OrganizationDbContext` và các migration). Chạy lần lượt trong console:

   ```powershell
   Get-Migration -Context OrganizationDbContext -StartupProject TutorManagement.DbMigrator
   Update-Database -Context OrganizationDbContext -StartupProject TutorManagement.DbMigrator
   ```

   Migration mới nhất phải là `UnifyCenterChangeRequests`. `Get-Migration` cho biết
   migration nào đã áp dụng; `Update-Database` áp phần còn thiếu. Không chọn
   `TutorManagement.Api` làm **Default project**. Sao lưu DB trước khi cập nhật dữ
   liệu thật. Migration **đổi tên và mở rộng** bảng đơn pháp lý cũ để giữ lịch sử;
   hai bảng email liên hệ/outbox cũ đổi tên có tiền tố `legacy_`, không còn được Worker
   đọc hoặc gửi email.
4. Trong pgAdmin → database → Schemas → `organization` → Tables, xác nhận có
   `centers`, `center_change_requests`, `legacy_center_contact_email_changes`,
   `legacy_email_outbox_messages`. Refresh danh sách nếu chưa hiện. Không tự chạy SQL
   tạo bảng hoặc sửa trạng thái đơn. Nếu các migration Identity/Academics chưa áp,
   xem hướng dẫn setup local trước khi khởi chạy toàn hệ thống.

## Chuẩn bị tài khoản và mở Swagger

Đặt `TutorManagement.Api` làm Startup Project và chạy `F5` (hoặc `Ctrl+F5`). Mở
[Swagger local](http://localhost:5217/swagger/index.html); kiểm tra đúng cổng theo
`Properties/launchSettings.json` của máy bạn. Worker **không cần chạy** riêng cho
luồng duyệt thay đổi này; chỉ chạy nếu bạn cần email invitation của luồng tạo Owner.

Nếu chưa có trung tâm `Approved` và tài khoản CenterOwner, theo
[luồng duyệt trung tâm/invitation](CENTER-APPROVAL-INVITATION-FLOW.md) để tạo qua
Swagger. Ghi lại `centerId`. Đăng nhập bằng `POST /api/v1/auth/login`, lấy
`data.accessToken`; bấm **Authorize** và dán JWT. Khi đổi giữa Admin và Owner,
đăng nhập lại và thay JWT, không dùng một token cho cả hai vai trò.

## Kiểm thử trong Swagger

1. Bằng JWT Owner, gọi `GET /api/v1/centers/{centerId}/profile`. Ghi lại **tất cả**
   bảy trường hiện tại. Owner không còn route `PUT profile` hoặc route xác minh
   contactEmail; Swagger không được hiện chúng.
2. Gọi `POST /api/v1/centers/{centerId}/change-requests`. Ví dụ, thay những giá trị
   dưới đây cho khớp dữ liệu local của bạn, đặc biệt taxCode chưa được trung tâm khác
   sử dụng:

   ```json
   {
     "legalName": "Trung tâm Ánh Sao - pháp nhân mới",
     "displayName": "Ánh Sao",
     "taxCode": "0312345679",
     "businessRegistrationNumber": "REG-2026-02",
     "registeredAddress": "12 Đường Mới, TP.HCM",
     "contactEmail": "lienhe@example.com",
     "contactPhone": "0912345678",
     "reason": "Thông tin đăng ký đã được cơ quan có thẩm quyền cập nhật",
     "evidenceReference": "Mã hồ sơ hoặc đường dẫn hồ sơ để admin đối chiếu"
   }
   ```

   Kỳ vọng `201`, `data.status = "Pending"`; lưu `data.id` thành `requestId`.
   `data.original` và `data.proposed` cho thấy bản trước/sau. Gọi lại `GET profile`:
   **chưa có trường nào đổi**. Gọi `GET /api/v1/centers/{centerId}/change-requests`:
   Owner thấy đơn Pending. Gửi thêm một đơn khi còn Pending phải nhận `409`.
3. Đăng nhập PlatformAdmin, thay JWT trong **Authorize**. Gọi
   `GET /api/v1/platform/centers/{centerId}/change-requests`; đối chiếu hồ sơ,
   `evidenceReference`, tất cả giá trị gốc/đề xuất với nguồn đáng tin cậy. Chọn một
   nhánh:

   - Hợp lệ: `POST /api/v1/platform/centers/{centerId}/change-requests/{requestId}/approve`
     với `{ "reviewNote": "Đã đối chiếu hồ sơ chính thức" }` → `200`, `Approved`.
   - Không hợp lệ: cùng đường dẫn kết thúc bằng `/reject`, body
     `{ "reviewNote": "Chưa có thông tin cập nhật trên nguồn chính thức" }` →
     `200`, `Rejected`.

4. Đăng nhập lại Owner và gọi `GET profile` cùng `GET change-requests`. Sau
   `Approved`, bảy trường trong profile khớp bản đề xuất; sau `Rejected`, profile
   giữ nguyên. Cả hai bên thấy trạng thái, reviewer, thời gian và ghi chú xử lý.
   Gọi lại approve/reject cho đơn đã xử lý phải nhận `409`. Sau khi từ chối, Owner
   có thể gửi đơn mới.

## Kiểm tra thêm

- Không có JWT → `401`; người không phải Owner của trung tâm → `403`; Owner gọi
  route `/platform/...` → `403`. Trung tâm chưa Approved hoặc Suspended không thể
  gửi/duyệt đơn.
- Body thiếu trường, sai định dạng email/điện thoại hoặc note rỗng → lỗi validation;
  đề xuất không khác hồ sơ gốc → lỗi nghiệp vụ; mã số thuế đã dùng → xung đột.
- Trong pgAdmin, mở **View/Edit Data** của `organization.center_change_requests`.
  Một hàng mới có bản gốc, bản đề xuất, `status`, `requested_by_user_id`,
  `reviewed_by_user_id`, `reviewed_at_utc`, `review_note`. Xem bảng `centers` để
  xác nhận chỉ nhánh Approved mới thay đổi. Không chỉnh trực tiếp các hàng để mô
  phỏng API vì như thế bỏ qua phân quyền và kiểm tra xung đột.
- Các bảng `legacy_` giữ dữ liệu cũ cho đối chiếu/di trú. Email liên hệ đã xếp
  hàng trước khi nâng cấp sẽ **không được gửi tiếp**; muốn thay email liên hệ,
  Owner gửi đơn mới theo luồng duyệt. Đừng coi email Owner đã xác minh là email
  liên hệ của trung tâm đã được xác minh.
