# Kế hoạch sửa back-end trước khi triển khai quy tắc xếp lịch

Trạng thái: **kế hoạch — đã chốt toàn bộ D1–D12 — sẵn sàng triển khai đợt 1 (mục 4)**. Ngày lập: 22/10/2026.
Nguồn: `REVIEW-NGHIEP-VU-BACKEND.md`. Việc sau kế hoạch này: `QUY-TAC-DA-TRUNG-TAM-VA-LICH-CA-NHAN.md`.

Nguyên tắc thực hiện:
- Mỗi hạng mục là một nhánh/PR riêng, kèm migration (nếu có), test domain + test tích hợp PostgreSQL, cập nhật OpenAPI và `docs/`.
- Thay đổi hợp đồng API được cập nhật đồng thời ở `QuanLyDayThemFE/lib/api` trong cùng đợt.
- Không xoá cột/endpoint cũ trong cùng đợt khi FE còn dùng; đánh dấu deprecated trước.

---

## 1. Giai đoạn A — Sửa lỗi & độ bền (không cần quyết định nghiệp vụ)

| # | Vấn đề (mục trong review) | Sửa | Migration | FE |
|---|---|---|---|---|
| A1 | Lịch học sinh hiện buổi trước ngày ghi danh, mất lịch sử sau khi rút (P1-1) | `ListMySessionsAsync` lọc theo khoảng ghi danh `[EnrolledAtUtc, WithdrawnAtUtc)`; không lọc `ArchivedAtUtc` với buổi đã qua | Không | Không |
| A2 | Không có ràng buộc chống chồng lịch ở DB (P1-2) | `btree_gist` + 3 exclusion constraint (class/teacher/room) trên `tstzrange(start_utc, end_utc)` với `cancelled_at_utc IS NULL`; giữ advisory lock | Có | Không |
| A3 | Mở/đóng đăng ký không có `expectedVersion` nhưng tăng version (P1-8) | Nhận `expectedVersion`, trả 409 khi lệch | Không | Có (gửi version) |
| A4 | Giáo viên đã lưu trữ vẫn được chọn làm người dạy thực tế (P1-7) | Kiểm `IsActive` khi lưu nháp/nộp/hiệu chỉnh | Không | Không |
| A5 | Thiếu index cho truy vấn xung đột (P3) | Index `(center_id, teacher_id, start_utc)`, `(center_id, room_id, start_utc)`, `(center_id, class_id, start_utc)`, `enrollments(student_id)` | Có | Không |
| A6 | `ClassRegistrationStore` giữ transaction trong field (P3) | Đổi sang guard trả về như `TeachingStore` | Không | Không |
| A7 | `ContractTests` rỗng (P3) | Snapshot OpenAPI + test so khớp DTO; chạy trong CI | Không | Không |
| A8 | `.artifacts/`, `bin/obj` lẫn trong cây mã (P3) | Bổ sung `.gitignore`, dọn khỏi git | Không | Không |

## 2. Giai đoạn B — Nghiệp vụ cần chốt quyết định

| # | Vấn đề | Hướng sửa | Phụ thuộc quyết định |
|---|---|---|---|
| B1 | Không mời được Admin/Teacher/Staff (P0-2.1) | Lời mời thành viên theo trung tâm; danh sách thành viên; đổi vai trò; ngừng quyền | D1 |
| B2 | Mời email đã có tài khoản bị chặn; chủ trung tâm nhiều trung tâm (P0-2.2) | Nhánh chấp nhận cho tài khoản đã có; unique index vai trò chủ | D2 |
| B3 | Giáo viên không gắn tài khoản, không tự điểm danh (P0-2.3) | `teachers.user_id`; policy điểm danh theo phân công | D3 |
| B4 | Học sinh không tìm được trung tâm (P0-2.4) | `centers.listed_in_directory` (mặc định tắt, chủ bật trong hồ sơ). API ẩn danh/học sinh: `GET /directory/centers?search&subjectId` → tên, các môn, số lớp đang mở; `GET /directory/centers/{id}/classes` → lớp đang mở đăng ký: tên, môn, giáo viên, lịch tuần (từ lịch lặp đang hiệu lực), ngày bắt đầu/kết thúc (buổi đầu/cuối), `maxStudents − enrolledCount`. Không lộ MST, địa chỉ, danh sách học sinh. FE: trang "Tìm trung tâm" trong cổng học sinh + nút bật hiển thị ở hồ sơ trung tâm | D4 |
| B5 | Thiếu thông tin học sinh/phụ huynh/giáo viên (P0-2.5) | Bổ sung trường hồ sơ; đơn đăng ký thu thêm thông tin liên hệ | D5 |
| B6 | Buổi đã bắt đầu không huỷ được; huỷ không có lý do (P0-2.6) | Lý do khi huỷ/dời; trạng thái "không diễn ra"; buổi học bù | D6 |
| B7 | Rút học không lý do, không ngày hiệu lực (P1-3) | `POST .../withdraw { reason }`, hiệu lực tại thời điểm rút | D7 |
| B8 | Trung tâm bị từ chối không nộp lại được; lịch sử duyệt bị ghi đè (P1-4, P1-10, P1-11) | Sửa hồ sơ + nộp lại; bảng `center_status_history` | D8 |
| B9 | Mã học sinh tự sinh `ST-<hex>` khác mã tạo tay (P1-9) | Bộ đếm mã theo trung tâm | D9 |
| B10 | Chủ không huỷ được yêu cầu đổi hồ sơ; Admin không xem được (P1-5) | `withdraw` cho yêu cầu chờ duyệt; GET cho thành viên | D10 |
| B11 | Lịch sử điểm danh nhiễu vì mỗi lần lưu nháp sinh 1 phiên bản (P1-6) | Chỉ ghi phiên bản cho Nộp/Hiệu chỉnh, hoặc gộp nháp | D11 |
| B12 | Trung tâm chưa có múi giờ (P2-9) | Một hằng cấu hình `Asia/Ho_Chi_Minh` dùng chung cho lịch lặp, "hết ngày" khi điểm danh và hiển thị; không thêm cột | D12 |

## 3. Giai đoạn C — Hợp đồng API & read-model (cập nhật FE cùng đợt)

| # | Vấn đề | Sửa |
|---|---|---|
| C1 | DTO chỉ trả ID (P2-1) | Thêm `class{code,name}`, `teacher{fullName}`, `room{name}` vào DTO đọc của buổi học, lịch lặp, đơn đăng ký, phiếu điểm danh, lịch học sinh |
| C2 | Người thao tác chỉ là GUID (P2-2) | Trả kèm `displayName` cho actor/requestedBy/reviewedBy |
| C3 | Thiếu tìm kiếm/lọc (P2-3) | `search` cho học sinh/giáo viên/lớp; lọc đơn theo lớp; lọc buổi theo lớp/giáo viên/phòng |
| C4 | Không có `/me` (P2-4) | `GET /me`: username, email, roles, hasPassword, hasGoogle |
| C5 | Phân trang không đồng nhất (P2-5) | Subjects/rooms/platform centers trả `page` như các danh sách khác; platform lọc trạng thái + tìm theo MST |
| C6 | Không có hàng đợi tổng cho quản trị (P2-6) | `GET /platform/change-requests?status=`; danh sách/gửi lại/thu hồi lời mời |
| C7 | Mã HTTP chưa nhất quán (P2-8) | 400 sai định dạng · 409 xung đột phiên bản/idempotency/trùng · 422 vi phạm quy tắc nghiệp vụ |

## 4. Quyết định nghiệp vụ cần chốt

| # | Câu hỏi | Đề xuất | Đã chốt |
|---|---|---|---|
| D1 | Ai được mời / đổi vai trò / ngừng quyền ai? | Chủ: mọi vai trò; Quản lý: chỉ Giáo viên & Nhân viên | ☑ **Chỉ chủ trung tâm** mời, đổi vai trò, ngừng quyền. Quản lý không có quyền này. |
| D2 | Một tài khoản làm chủ tối đa mấy trung tâm? Chủ trung tâm A có được làm giáo viên ở B? | 1 trung tâm; được dạy ở nơi khác | ☑ **1 trung tâm / chủ.** Chủ có thể tự đứng lớp ở chính trung tâm đó **bằng cùng tài khoản**: vai trò Chủ bao gồm quyền giáo viên; tài khoản chủ được gắn vào hồ sơ `Teacher` của trung tâm mình. Dạy ở trung tâm khác: chưa chốt (mặc định cho phép, như mọi giáo viên). |
| D3 | Giáo viên được điểm danh buổi nào, trong bao lâu? | Buổi được phân công, đến hết ngày diễn ra; hiệu chỉnh chỉ Chủ/Quản lý | ☑ Theo đề xuất: buổi mình được phân công hoặc dạy thay, đến hết ngày diễn ra; hiệu chỉnh sau nộp chỉ Chủ/Quản lý. |
| D4 | Học sinh vào trung tâm bằng cách nào? Thông tin gì được công khai? | Mã/đường dẫn trung tâm; công khai tên + trạng thái tuyển sinh | ☑ **Đổi (22/10): danh bạ công khai.** Học sinh tự tìm trung tâm theo tên, lọc theo môn, rồi chọn lớp và nộp đơn. Chỉ trung tâm đã duyệt, đang hoạt động **và tự bật "Hiện trong danh bạ"** mới hiện. Chỉ hiện lớp **đang mở đăng ký**, kèm: tên lớp, môn, lịch học trong tuần, tên giáo viên, số chỗ còn trống, ngày bắt đầu/kết thúc. Đường dẫn riêng của trung tâm vẫn giữ để chia sẻ. |
| D5 | Hồ sơ học sinh cần trường nào, trường nào bắt buộc? | Bắt buộc: họ tên, tên + SĐT phụ huynh; tuỳ chọn: ngày sinh, trường, khối, SĐT học sinh | ☑ Chỉ thêm **tên + SĐT phụ huynh (bắt buộc)**. Không thêm ngày sinh, trường/khối, SĐT/email học sinh ở đợt này. |
| D6 | Xử lý buổi không diễn ra / huỷ muộn? | Lý do bắt buộc; trạng thái "Không diễn ra" sau giờ bắt đầu; liên kết buổi học bù | ☑ Theo đề xuất: lý do bắt buộc; trạng thái "Không diễn ra"; buổi học bù liên kết với buổi gốc. |
| D7 | Rút học: có cho lùi ngày hiệu lực? lý do bắt buộc? | Lý do bắt buộc; lùi tối đa 7 ngày và không qua buổi đã nộp điểm danh | ☑ **Lý do bắt buộc, hiệu lực ngay, không cho lùi ngày.** |
| D8 | Trung tâm bị từ chối xử lý thế nào? Được duyệt thẳng từ "Từ chối"? | Sửa hồ sơ rồi nộp lại về "Chờ duyệt"; bỏ duyệt thẳng từ "Từ chối" | ☑ Không có ưu tiên → dùng đề xuất. |
| D9 | Định dạng mã học sinh? | Tự sinh `HS-000001` theo trung tâm cho mọi hồ sơ; vẫn cho sửa khi tạo tay | ☑ Theo đề xuất. |
| D10 | Ai xem/huỷ yêu cầu đổi hồ sơ? | Chủ gửi & huỷ; Quản lý xem | ☑ Theo đề xuất. |
| D11 | Lịch sử điểm danh ghi gì? | Chỉ Nộp và Hiệu chỉnh; nháp chỉ giữ bản mới nhất | ☑ Theo đề xuất. |
| D12 | Múi giờ trung tâm? | Mỗi trung tâm một múi giờ, mặc định Asia/Ho_Chi_Minh | ☑ **Cố định giờ Việt Nam** (`Asia/Ho_Chi_Minh`) cho mọi trung tâm, không có ô chọn. |

### Quyết định kèm theo cho quy tắc xếp lịch (`QUY-TAC-...`)

| Câu hỏi | Đã chốt |
|---|---|
| Báo trước khi đổi hẳn lịch lặp | 7 ngày, **chỉ cảnh báo**: dưới 7 ngày vẫn lưu được nhưng phải xác nhận và đánh dấu báo gấp |
| Ngưỡng "báo gấp" khi dời/huỷ một buổi | 24 giờ |
| Trùng lịch với trung tâm khác khi duyệt đơn | Trung tâm chỉ thấy **số buổi trùng**, không thấy tên trung tâm kia |
| Chủ trung tâm A dạy ở trung tâm B | Chưa chốt — mặc định cho phép |
| Tài khoản học sinh nhỏ tuổi / tài khoản phụ huynh | Mỗi học sinh một Google riêng; không có tài khoản phụ huynh |
| Học sinh xin vắng | Trước giờ học, có lý do; Chủ/Quản lý duyệt; duyệt xong phiếu điểm danh điền sẵn "Vắng có phép" |
| Học sinh nghỉ hẳn một lớp | Gửi yêu cầu có lý do; trung tâm xác nhận mới rút |
| Kênh thông báo đổi/huỷ lịch | Trong app + email |

## 5. Thứ tự thực hiện đề xuất

1. **Đợt 1 — A1–A8** (sửa lỗi, không đổi nghiệp vụ) + **C1, C4** (giảm ghép dữ liệu ở FE).
2. **Đợt 2 — B1, B2, B3** (thành viên & giáo viên) — cần D1–D3.
3. **Đợt 3 — B5, B7, B9, B11** (hồ sơ học sinh, rút học, mã, lịch sử điểm danh) + **C2, C3**.
4. **Đợt 4 — B4 (danh bạ), B6, B8, B10, B12** + **C5–C7**. B4 nên làm cùng C1 (tên giáo viên/môn trong DTO) và trước F3 kiểm trùng khi nộp đơn.
5. Sau đó mới triển khai `QUY-TAC-DA-TRUNG-TAM-VA-LICH-CA-NHAN.md` (lịch cá nhân, kiểm trùng, đổi lịch).

Mỗi đợt kết thúc bằng: build + toàn bộ test, cập nhật OpenAPI và tài liệu kiểm thử thủ công, FE build/lint/typecheck.

## 6. Tiến độ

### Đợt 1 — xong 22/10/2026

| # | Kết quả |
|---|---|
| A1 | `my/sessions` chỉ trả buổi trong khoảng `[ngày ghi danh, ngày rút)`; giữ buổi đã qua sau khi rút và sau khi hồ sơ bị lưu trữ |
| A2 | Migration `20261022090000_AddSessionOverlapConstraints`: `btree_gist` + 3 exclusion constraint `ex_sessions_{class,teacher,room}_overlap` (khoảng nửa mở, bỏ qua buổi đã huỷ). Migration dừng kèm thông báo rõ nếu dữ liệu cũ đang chồng lịch. Không đổi EF model |
| A3 | `PUT classes/{id}/registration` bắt buộc `expectedVersion` (400 nếu thiếu, 409 `class_registration_concurrent_change` nếu lệch); trả `ClassDto` có `version` mới |
| A4 | Người dạy thực tế phải là giáo viên đang hoạt động; giáo viên đã lưu trữ chỉ được giữ nếu là người được phân công hoặc đã ghi trên phiếu |
| A5 | Index `(center_id, teacher_id/room_id/class_id, start_utc)` và `enrollments(student_id)` (trong cùng migration A2) |
| A6 | `ClassRegistrationStore` trả guard giống `TeachingStore`, bỏ transaction lưu trong field |
| A7 | Dự án `tests/ContractTests`: snapshot OpenAPI `Snapshots/openapi.v1.json` + kiểm tra các trường hợp đồng chính. Cập nhật snapshot: `TM_UPDATE_CONTRACT_SNAPSHOT=1 dotnet test tests/ContractTests` |
| A8 | `.gitignore` đã đủ (`bin/`, `obj/`, `artifacts/`, `.artifacts/`, `.local/`); không có file build nào bị theo dõi |
| C1 | Đọc buổi học, lịch lặp, đơn đăng ký (quản lý + học sinh), phiếu điểm danh, lịch học sinh trả thêm `classCode`, `className`, `teacherName`, `roomName` / `actualTeacherName`. Phản hồi sau khi ghi chưa có các trường này |
| C4 | `GET /api/v1/me`: id, username, email, emailVerified, roles (đọc từ DB), hasPassword, hasGoogle |

Kiểm chứng: build 0 cảnh báo; 3 + 8 + 110 test đạt, gồm toàn bộ test PostgreSQL thật; chạy API thật + FE thật qua trình duyệt (mở/đóng đăng ký liên tiếp, lịch hiện tên).
FE cùng đợt: `setClassRegistration` gửi `expectedVersion`, `identityApi.me()`, màn lịch/lịch lặp/cổng học sinh ưu tiên tên do server trả; học sinh đã thấy giáo viên và phòng.

### Đợt 2 — xong 22/10/2026 (B1, B2, B3)

| # | Kết quả |
|---|---|
| B1 | Thành viên theo trung tâm (D1 — chỉ chủ quản lý): `GET centers/{id}/members`, `GET/POST centers/{id}/member-invitations`, `POST .../{invitationId}/revoke`, `PUT members/{membershipId}/role`, `POST members/{membershipId}/deactivate|reactivate`. Chủ và quản lý xem; chỉ chủ mời (vai trò Quản lý/Giáo viên/Nhân viên), đổi vai trò, ngừng/khôi phục quyền. Không đổi được vai trò hay ngừng quyền của chủ hoặc của chính mình |
| B2 | Một tài khoản dùng cho mọi trung tâm: mời email đã có tài khoản → người nhận đăng nhập rồi `POST me/invitations/accept` (email phải khớp; mở link coi như đã xác minh email). Email chưa có tài khoản → `auth/invitations/accept` như cũ, nay áp dụng mọi vai trò; nếu email đã có tài khoản thì trả `invitation_requires_sign_in`. `POST auth/invitations/preview` cho biết trung tâm, vai trò, email và đã có tài khoản chưa. Lời mời chủ trung tâm cũng nhận tài khoản có sẵn. Migration `EnforceSingleActiveCenterOwner`: unique index `ux_center_memberships_active_owner` (mỗi tài khoản tối đa 1 vai trò chủ đang hoạt động) → `already_center_owner` |
| B3 | Migration `LinkTeacherAccounts`: `teachers.user_id` + unique `(center_id, user_id)`. Chủ gắn/bỏ gắn hồ sơ giáo viên với tài khoản thành viên đang hoạt động: `PUT teachers/{id}/account {userId|null, expectedVersion}` — chủ có thể gắn chính mình để tự đứng lớp (D2). `GET my/teaching?fromUtc&toUtc` trả hồ sơ + buổi dạy (≤ 31 ngày, kèm tên). Điểm danh mở cho mọi thành viên: chủ/quản lý như cũ; người khác chỉ buổi mà hồ sơ của mình được phân công hoặc đang ghi là người dạy thực tế (403 `attendance_not_assigned`), lưu nháp/nộp đến hết ngày diễn ra theo giờ Việt Nam (409 `attendance_window_closed`). Hiệu chỉnh và lịch sử vẫn chỉ chủ/quản lý |

Kiểm chứng: build 0 cảnh báo; 3 + 15 + 121 test đạt (thêm test PostgreSQL thật cho toàn bộ luồng thành viên → gắn hồ sơ → giáo viên tự điểm danh, và test quy tắc vai trò/giờ Việt Nam); chạy API thật qua HTTP (mời → tạo tài khoản → giáo viên bị chặn 403 ở các thao tác của chủ/quản lý) và FE thật trong trình duyệt ở 1440px và 390px.
FE cùng đợt: trang **Thành viên** (mời, lời mời đang chờ, đổi vai trò, ngừng quyền), cột **Tài khoản đăng nhập** + hộp gắn tài khoản ở trang Giáo viên, trang nhận lời mời chung cho mọi vai trò (xem trước → đăng nhập rồi chấp nhận, hoặc tạo tài khoản), trang Điểm danh cho giáo viên chỉ hiện buổi mình dạy kèm hạn tự điểm danh.

Ghi chú: email mời ban đầu chỉ ghi đường dẫn API — đã xử lý ở đợt 3 bằng `Frontend:BaseUrl`.

### Đợt 3 — xong 22/10/2026 (B5, B7, B9, B11, C2, C3)

| # | Kết quả |
|---|---|
| B5 | Hồ sơ học sinh có `parentName`, `parentPhone` (chuẩn hoá `+84…`), bắt buộc khi tạo/sửa (D5). Đơn đăng ký trực tuyến bắt buộc gửi kèm tên + SĐT phụ huynh; khi duyệt, thông tin được chép vào hồ sơ (không ghi đè liên hệ trung tâm đã có). Hồ sơ cũ chưa có liên hệ vẫn đọc được và hiện nhãn "Chưa có liên hệ" |
| B7 | Rút học: `POST classes/{classId}/enrollments/{studentId}/withdraw {reason}` (bỏ `DELETE`), lý do bắt buộc, hiệu lực ngay (D7); lưu `withdrawal_reason`, `withdrawn_by_user_id`; danh sách lớp trả lý do và tên người rút |
| B9 | Mã học sinh tự sinh `HS-000001` theo từng trung tâm (D9) qua bảng đếm `academics.student_code_counters`; bỏ trống mã khi tạo tay để được cấp mã, mã nhập tay vẫn dùng được và bộ đếm tự bỏ qua mã đã có. Hồ sơ từ đơn trực tuyến cũng dùng mã này (thay cho `ST-<hex>`) |
| B11 | Lưu nháp không còn sinh dòng lịch sử; lịch sử chỉ có Nộp và Hiệu chỉnh (D11). Dòng `DraftSaved` cũ vẫn nằm trong DB nhưng không còn trả ra |
| C2 | Tên người thao tác: `actorName` (lịch sử điểm danh), `withdrawnByName`, `reviewedByName` (đơn đăng ký), `requestedByName`/`reviewedByName` (yêu cầu đổi hồ sơ) qua `IUserDirectory` của IdentityAccess |
| C3 | `search` cho học sinh (mã, tên, SĐT phụ huynh), giáo viên, lớp; `classId` cho đơn đăng ký; `classId`/`teacherId`/`roomId` cho danh sách buổi học |
| + | `Frontend:BaseUrl` (Development: `http://localhost:3000`): email mời chứa link `/accept-invitation?token=…`; để trống thì email giữ hướng dẫn API như cũ |

Migration: `StudentContactsWithdrawalReasonsAndCodes` (Academics). Kiểm chứng: build 0 cảnh báo; 3 + 19 + 127 test đạt (thêm test PostgreSQL thật cho toàn bộ quy tắc đợt 3 và test link email); thử trên trình duyệt: tạo học sinh để trống mã → `HS-000001`, tìm theo SĐT phụ huynh, rút học bắt buộc lý do và hiện lý do + người rút.
FE cùng đợt: form học sinh (mã tuỳ chọn, tên + SĐT phụ huynh), cột Phụ huynh, tìm kiếm phía server cho học sinh/giáo viên/lớp, hộp thoại rút học có lý do, đơn đăng ký hiện liên hệ phụ huynh + người duyệt + lọc theo lớp, form đăng ký của học sinh thu tên + SĐT phụ huynh, lịch sử điểm danh và yêu cầu đổi hồ sơ hiện tên người thao tác.


### Đợt 4 — xong 22/10/2026 (B4, B6, B8, B10, B12, C5, C6, C7)

| # | Kết quả |
|---|---|
| B4 | Danh bạ công khai (D4): chủ trung tâm bật/tắt `PUT centers/{id}/directory {listed}` (chỉ khi đã duyệt; đình chỉ tự ẩn). Người dùng đã đăng nhập: `GET directory/centers?search&subject&page`, `GET directory/subjects`, `GET directory/centers/{id}?subject`. Chỉ trả tên hiển thị trung tâm và lớp đang mở đăng ký còn chỗ: tên lớp, môn, lịch tuần (từ lịch lặp đang chạy), giáo viên, số chỗ còn, ngày bắt đầu/kết thúc. Không lộ MST, địa chỉ, liên hệ |
| B6 | Trạng thái buổi: `Scheduled` / `Cancelled` / `NotHeld` (D6). Hủy (`POST sessions/{id}/cancel {reason, expectedVersion}`) và dời lịch (`reason` trong `PUT`) bắt buộc lý do (D7). `POST sessions/{id}/not-held {reason, expectedVersion}` cho buổi đã bắt đầu, chưa có phiếu điểm danh. Buổi bù: `makeupForSessionId` khi tạo buổi — chỉ thay buổi đã hủy/không diễn ra của cùng lớp, mỗi buổi tối đa một buổi bù (unique index `ux_sessions_makeup_for`). `SessionDto` thêm `status`, `cancellationReason`, `notHeldReason`, `rescheduleReason`, `statusChangedAtUtc`, `makeupForSessionId`. Hủy hàng loạt khi dừng/thay lịch lặp ghi lý do "Lịch định kỳ thay đổi" |
| B8 | Hồ sơ bị từ chối: `POST platform/centers/{id}/resubmit` (sửa thông tin + ghi chú) → về Chờ duyệt; không duyệt thẳng từ Bị từ chối (D8). Lịch sử trạng thái đầy đủ (`center_status_history`): `GET platform/centers/{id}/history`, `GET centers/{id}/status-history` (chủ/quản lý), có tên người thao tác. Migration tự tạo lịch sử cho trung tâm sẵn có |
| B10 | Chủ trung tâm rút yêu cầu đổi hồ sơ đang chờ: `POST centers/{id}/change-requests/{requestId}/withdraw` (trạng thái `Withdrawn`); quản lý được xem danh sách yêu cầu (D10). Hàng đợi nền tảng: `GET platform/change-requests?status&page` (cũ nhất trước, kèm tên trung tâm) |
| B12 | Lịch lặp luôn dùng giờ Việt Nam: `timeZoneId` có thể bỏ trống, chỉ nhận `Asia/Ho_Chi_Minh` (D12). FE hiển thị và nhập giờ theo giờ Việt Nam bất kể múi giờ trình duyệt |
| C5 | Môn học và phòng học trả trang `{page, pageSize, totalCount, items}` kèm `search`; danh sách trung tâm của nền tảng có `status`, `search`, `page`; thêm `GET platform/centers/{id}` và `GET platform/center-counts` |
| C6 | Lời mời: chủ gửi lại lời mời thành viên `POST centers/{id}/member-invitations/{id}/resend` (link mới, hạn 48 giờ, link cũ hết hiệu lực); nền tảng xem lời mời đang mở (kể cả đã hết hạn) `GET platform/centers/{id}/invitations`, gửi lại/thu hồi lời mời chủ trung tâm |
| C7 | Mã lỗi HTTP: 409 chỉ cho trùng lặp và xung đột đồng thời (version, idempotency, trùng mã, trùng lịch, đã có buổi bù); vi phạm quy tắc nghiệp vụ trả 422 (Academics, phòng, môn, trung tâm) |

Migration: `CenterLifecycleAndDirectory` (Organizations), `SessionStatusReasonsAndMakeups` (Academics). Kiểm chứng: build 0 cảnh báo; 3 + 32 + 131 test đạt (thêm test PostgreSQL thật cho hủy/không diễn ra/buổi bù/danh bạ, test vòng đời trung tâm và rút yêu cầu); thử trên trình duyệt: bật danh bạ, ghi nhận không diễn ra có lý do, xếp buổi bù (trùng giờ bị chặn), hủy có lý do, học sinh tìm trung tâm và xem lớp, trang nền tảng phân trang + lịch sử + lời mời + hàng đợi.
FE cùng đợt: trang học sinh "Tìm trung tâm" (tìm theo tên, lọc môn, xem lớp), công tắc danh bạ + rút yêu cầu + lịch sử trạng thái ở Hồ sơ trung tâm, lịch dạy có Không diễn ra / Xếp buổi bù / lý do hủy-dời, nền tảng: sửa & gửi duyệt lại, lịch sử, lời mời, trang "Yêu cầu thay đổi", danh sách trung tâm phân trang phía server; gửi lại lời mời thành viên; môn/phòng phân trang; bỏ chọn múi giờ.
Lưu ý (đã xử lý 22/10 trong đợt lịch cá nhân): `my/sessions` nay trả trạng thái/lý do buổi.

### Rà soát trước quy tắc xếp lịch — 22/10/2026

| Lỗi | Sửa |
|---|---|
| Snapshot OpenAPI cũ (thiếu `platform/center-counts` của đợt 4) → ContractTests đỏ | Cập nhật snapshot; thêm kiểm tra cho DTO/endpoint mới |
| Buổi **không diễn ra** vẫn bị coi là chiếm lớp/giáo viên/phòng (kiểm trùng, "còn buổi tương lai" khi lưu trữ, exclusion constraint) | Bỏ qua như buổi đã huỷ; migration dựng lại 3 constraint |
| `my/sessions` không trả trạng thái/lý do, giấu buổi đã huỷ | Trả đủ trạng thái, lý do, đã dời, học bù |
| Dừng/thay lịch lặp huỷ buổi với lý do chung "Lịch định kỳ thay đổi", không lưu người đổi | Lý do bắt buộc do người dùng nhập, ghi vào từng buổi và nhật ký |
| FE: tuần/ngày tính theo múi giờ trình duyệt (`startOfWeek`, tổng quan 7 ngày, xem trước thay lịch lặp) — sai khi máy không ở giờ Việt Nam (D12) | Tính theo giờ Việt Nam |
| FE: tổng quan ghi "Đã hủy" cho buổi không diễn ra | Hiện đúng "Không diễn ra" |

Sau đó triển khai toàn bộ `QUY-TAC-DA-TRUNG-TAM-VA-LICH-CA-NHAN.md` (mục 9 của tài liệu đó, chi tiết API ở `SCHEDULE-RULES-TEST.md`).
