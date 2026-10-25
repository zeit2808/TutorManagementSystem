# Response API sau khi thu gọn

Áp dụng cho JSON của các API v1 hiện tại. Các thay đổi chỉ liên quan contract response và truy vấn danh sách enrollment; không thay đổi bảng, dữ liệu gốc hay migration.

## 1. Phần bao chung

Giữ `success`, `message`, `traceId` theo quy ước hiện có. `data` và `errors` chỉ xuất hiện khi có giá trị.

Thành công:

```json
{
  "success": true,
  "message": "Request processed successfully",
  "data": [],
  "traceId": "..."
}
```

Lỗi:

```json
{
  "success": false,
  "message": "Request validation failed.",
  "errors": [
    {
      "code": "validation_error",
      "message": "Provide a valid UUID in the Idempotency-Key header.",
      "field": "Idempotency-Key"
    }
  ],
  "traceId": "..."
}
```

`field` chỉ xuất hiện với lỗi gắn với một trường/header. `null` được bỏ trên cả response MVC và middleware (authentication, rate limit, exception). Không bỏ `false`, `0`, chuỗi rỗng hay mảng `[]`: đây có thể là dữ liệu có ý nghĩa. Khi một trường tùy chọn không có giá trị, frontend coi trường vắng mặt tương đương `null`.

API `204 No Content` vẫn không có body. API forgot-password `202` giữ thông báo chung, không có `data`/`errors` vì không trả dữ liệu và cần tránh tiết lộ tài khoản tồn tại.

XML vẫn được hỗ trợ theo content negotiation hiện có; quy tắc bỏ null này áp dụng cho JSON.

## 2. Những trường đã thu gọn

| Nhóm | Response hiện tại |
| --- | --- |
| Login, Google login, refresh, change-password | `tokenType`, `accessToken`, `expiresIn` (giây). Bỏ hai mốc hết hạn lặp lại; refresh token và hạn cookie do trình duyệt quản lý. |
| Môn học | `id`, `code`, `name`, `description` nếu có, `isActive`, `version`. |
| Phòng học | `id`, `code`, `name`, `locationNote` nếu có, `isActive`, `version`. |
| Lớp học | Giữ dữ liệu lớp, môn, sĩ số, trạng thái và `version`; bỏ `centerId` đã có trong URL. |
| Học sinh | `id`, `code`, `fullName`, `hasAccount`, `isActive`, `version`. `hasAccount` giúp phân biệt hồ sơ đăng nhập và hồ sơ tạo tay, thay việc công khai `userId`. |
| Giáo viên | `id`, `code`, `fullName`, `isActive`, `version`; bỏ `centerId` lặp. |
| Đơn đăng ký lớp | `id`, `submissionId`, `classId`, `fullName`, `status`, `submittedAtUtc`, thời điểm/lý do duyệt nếu có. Bỏ `centerId`, `userId`, `reviewedByUserId` trong response; vẫn lưu nguyên trong DB. |
| Ghi danh/rút học | `id`, `studentId`, `status`, `enrolledAtUtc`, `withdrawnAtUtc` nếu đã rút. Bỏ `classId` đã có trong URL. |
| Yêu cầu đổi thông tin trung tâm | Bỏ `centerId` đã có trên URL. Giữ bản gốc, đề xuất, bằng chứng, người yêu cầu/người duyệt và lịch sử vì cần đối chiếu nghiệp vụ pháp lý. |

Thông tin audit của môn/phòng vẫn được ghi ở domain và DB; chỉ không đưa vào response thao tác thường ngày. Các DTO được khai báo rõ, không serialize trực tiếp EF entities.

Các nhóm đã kiểm tra và giữ thông tin nghiệp vụ: hồ sơ trung tâm, duyệt trung tâm, gửi/nhận invitation, `/me/centers`, center context, lịch dạy, lớp mở đăng ký và lớp của bản thân. `centerId` trong `/me/centers` và kết quả nhận invitation vẫn cần để frontend biết chuyển vào trung tâm nào. Thông tin pháp lý của trung tâm không bị lược bỏ khỏi API dành cho người được phép xem.

`version` được giữ để frontend gửi `expectedVersion` khi cập nhật, tránh ghi đè dữ liệu người khác. `submissionId` được giữ để nhóm các đơn cùng một lần đăng ký. `traceId` được giữ để tra log khi lỗi.

## 3. Xem học sinh trong lớp

Trong Swagger, nhóm **07 · Classes**, chọn:

```http
GET /api/v1/centers/{centerId}/classes/{classId}/enrollments
```

Query:

- `status=active` (mặc định): chỉ đang học.
- `status=withdrawn`: lịch sử rút học.
- `status=all`: cả hai; một học sinh có thể có nhiều dòng lịch sử ghi danh.
- `page=1`, `pageSize=25` (tối đa 100).

Response ví dụ:

```json
{
  "success": true,
  "message": "Request processed successfully",
  "data": {
    "page": 1,
    "pageSize": 25,
    "totalCount": 1,
    "items": [
      {
        "enrollmentId": "e9920dfb-7288-4d55-92e6-9b81b0e9ca98",
        "studentId": "cddd791a-d27f-496c-9224-7ab6b2ae9f57",
        "studentCode": "ST-A12BC34",
        "fullName": "Ho Ngoc Bao Duy",
        "status": "Active",
        "enrolledAtUtc": "2026-10-19T14:51:02.108535+00:00"
      }
    ]
  },
  "traceId": "..."
}
```

Backend join học sinh ngay trong truy vấn có giới hạn center và class, rồi phân trang trên PostgreSQL; frontend không cần gọi thêm một API cho từng tên học sinh. `totalCount` tính trên tập dữ liệu sau lọc status, trước phân trang. Các trang có thứ tự ổn định theo họ tên và enrollment ID.

## 4. Tự kiểm tra trong Visual Studio và Swagger

1. Dừng phiên API cũ bằng **Stop Debugging**. **Build → Build Solution**, chạy lại project `TutorManagement.Api` với profile HTTP.
2. Mở Swagger và tải lại trang để cập nhật schema. Không cần `Update-Database` cho thay đổi này.
3. Login: `data` chỉ có ba trường token nói trên. Kiểm tra refresh cookie vẫn tồn tại trong **DevTools → Application → Cookies**, có `HttpOnly`.
4. GET môn/phòng: không còn center/audit IDs. `version=0` và `isActive=false` vẫn xuất hiện nếu có; trường mô tả trống/null không xuất hiện.
5. GET students: có `hasAccount=true` cho học sinh liên kết tài khoản và `false` cho hồ sơ tạo tay. Không trả `userId`.
6. GET enrollments bằng tài khoản có quyền thành viên trung tâm: mặc định thấy học sinh đang học, có mã và tên. Thử `pageSize=1`, trang 2 và một trang vượt dữ liệu; khi rỗng `items=[]`, `totalCount` vẫn đúng.
7. Rút một học sinh theo luồng được phép. GET `status=active` không có người đó; `status=withdrawn` có người đó với thời điểm rút. `status=abc` hoặc `pageSize=101` trả `400` theo envelope chung.
8. Gửi đăng ký lớp thiếu key: `400`, `errors[0].field=Idempotency-Key`. Đổi tên/lớp không hợp lệ thì lỗi chỉ đúng trường đó. Retry cùng key/body vẫn trả lại đơn cũ.
9. Gọi API cần đăng nhập khi chưa Authorize: `401`, `success=false`, có `errors`/`traceId`, không còn `data:null` hoặc `field:null`.
10. Gọi forgot-password: vẫn `202` với thông báo chung, không lộ email/tài khoản và không có trường null.
11. Trong **Test → Test Explorer**, chạy `ApiSmokeTests`, `TeachingResponseSerializationTests`, `SubjectCatalogTests`, `RoomCatalogTests`. Để kiểm tra truy vấn DB thật, chọn **Test → Configure Run Settings → Select Solution Wide runsettings File → tests/IntegrationTests/AuthE2E.runsettings**, chạy `RealPostgresTeachingFlowTests` và `RealPostgresClassRegistrationTests` trên DB local phát triển.

## 5. Cập nhật frontend/client

Đây là thay đổi contract của API v1 đang phát triển: thay `response.data` dạng mảng enrollment bằng `response.data.items`; đọc thông tin token bằng `expiresIn`; dùng `hasAccount` thay kiểm tra `userId`. Các client dùng trường đã bỏ phải cập nhật. Frontend xử lý lỗi qua `errors ?? []` và các thuộc tính tùy chọn bằng `?? null` khi cần. Tên route và request nghiệp vụ không thay đổi; query lọc/phân trang enrollment là tùy chọn mới.
