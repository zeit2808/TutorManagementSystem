# Quy tắc xếp lịch: lịch cá nhân, kiểm trùng, thông báo đổi lịch

Hiện thực `QUY-TAC-DA-TRUNG-TAM-VA-LICH-CA-NHAN.md` (B4–B8, F1–F6). Migration mới:
`20261022095439_ScheduleChangesNoticesAndRequests` (Academics) — bảng `schedule_changes`,
`schedule_notices`, `student_requests`; dựng lại 3 exclusion constraint chống chồng lịch để bỏ qua cả buổi
**không diễn ra** (trước đây chỉ bỏ qua buổi đã huỷ).

```powershell
dotnet run --project src/Hosts/TutorManagement.DbMigrator
```

## 1. Lịch cá nhân (B4)

| Endpoint | Ai gọi | Ghi chú |
|---|---|---|
| `GET /api/v1/me/schedule?fromUtc&toUtc` | Mọi tài khoản đã đăng nhập | ≤ 42 ngày. Gộp **mọi trung tâm**: buổi của lớp mình học (chỉ trong khoảng `[ngày ghi danh, ngày rút)`, mọi trạng thái) và buổi được phân công dạy (`role = Teacher`). Mỗi buổi có `centerName`, tên lớp/giáo viên/phòng, `status` + `statusReason`, `previousStartUtc/EndUtc` (lịch trước lần dời gần nhất), `overlapsWith` (buổi khác của chính người đó chồng giờ), `absenceRequestStatus`. |
| `GET centers/{id}/my/sessions` | Học sinh | Nay trả cả buổi đã huỷ / không diễn ra, kèm `status`, `statusReason`, `isRescheduled`, `rescheduleReason`, `isMakeup`. |

Lịch không lưu bản sao theo người; mỗi lần đọc được tính từ ghi danh + phân công.

## 2. Kiểm trùng (B5)

| Thời điểm | Endpoint | Hành vi |
|---|---|---|
| Học sinh chọn lớp | `POST centers/{id}/class-applications/check {classIds}` | Liệt kê buổi trùng với lớp khác của chính em ở **mọi** trung tâm (kèm tên trung tâm) và giữa các lớp đang chọn. Không chặn. |
| Ghi danh tay | `POST centers/{id}/classes/{classId}/enrollments {studentId, acknowledgeConflicts?}` | Trùng lớp khác **cùng trung tâm** → `409 student_schedule_conflict`, `data` = `{sameCenterConflicts[], otherCenterConflictCount}`. Gửi lại với `acknowledgeConflicts: true` để tiếp tục. Trùng trung tâm khác chỉ trả **số buổi** (`otherCenterConflictCount`), không nêu tên. |
| Duyệt đơn | `POST centers/{id}/class-applications/{id}/approve {acknowledgeConflicts?}` | Như trên (body không bắt buộc). |
| Thêm / dời / huỷ buổi | `POST centers/{id}/sessions/impact {classId, startUtc, endUtc, sessionId?}` | Số học sinh bị ảnh hưởng, tên học sinh trùng lớp khác của trung tâm, số học sinh trùng nơi khác, số học sinh chưa có tài khoản, `isUrgent` (trong 24 giờ). Không chặn. |

Chỉ xét buổi trong 200 ngày tới; buổi đã huỷ / không diễn ra không tính là bận.

## 3. Đổi lịch: lý do, nhật ký, thông báo (B6, B7)

- Mỗi thao tác thêm buổi lẻ, xếp buổi bù, dời, huỷ, ghi nhận không diễn ra, dừng / thay lịch lặp ghi **một** dòng
  `schedule_changes` (lịch cũ, lịch mới, giáo viên/phòng cũ–mới, lý do, người đổi, thời điểm, `isUrgent`) và một
  `schedule_notices` cho mỗi học sinh của lớp, **trong cùng giao dịch**.
- Học sinh có tài khoản nhận thêm email (ghi vào `iam.email_outbox_messages` cùng giao dịch; Worker gửi như các
  email khác). Tiêu đề có `[Báo gấp]` khi khẩn. Link dùng `Frontend:BaseUrl` + `/student`.
- **Báo gấp:** một buổi bắt đầu (giờ cũ hoặc mới) trong 24 giờ tới. **Lịch lặp:** dừng / thay khi buổi đầu tiên bị
  ảnh hưởng còn < 7 ngày → `422 schedule_short_notice` trừ khi gửi `acknowledgeShortNotice: true`; khi đó thông báo
  được đánh dấu báo gấp.
- `POST schedule-series/{id}/stop` và `/replace-future` nay **bắt buộc** `reason` (400 nếu thiếu); lý do được ghi
  vào từng buổi bị huỷ.
- `GET centers/{id}/sessions/{sessionId}/changes` — lịch sử thay đổi của một buổi. `SessionDto` trên endpoint đọc có
  thêm `previousStartUtc/EndUtc`.
- `GET centers/{id}/schedule-changes?classId&page` (chủ/quản lý) — thay đổi gần đây + `responses`: đã báo, chưa có
  tài khoản, đã xem, đã xác nhận, yêu cầu theo loại, yêu cầu đang chờ.
- Học sinh: `GET /me/notices?unreadOnly&page` (kèm `unreadCount`), `POST /me/notices/{id}/acknowledge`
  ("Đã xem, vẫn đi được"), `POST /me/notices/seen`.

## 4. Yêu cầu của học sinh (B8)

| Endpoint | Ai | Ghi chú |
|---|---|---|
| `POST centers/{id}/my/requests {kind, classId, sessionId?, targetClassId?, noticeId?, reason}` | Học sinh | `kind`: `ExcusedAbsence` (buổi chưa bắt đầu, còn hiệu lực), `Makeup` (cần `sessionId`), `Transfer` (cần `targetClassId` cùng trung tâm), `Withdrawal`. Phải đang học lớp. Trùng yêu cầu đang chờ → 409 `student_request_exists`. Gắn `noticeId` để đánh dấu thông báo "Đã gửi yêu cầu". |
| `POST centers/{id}/my/requests/{id}/cancel` | Học sinh | Rút lại yêu cầu đang chờ. |
| `GET /me/requests` | Học sinh | Mọi trung tâm. |
| `GET centers/{id}/student-requests?status&kind&classId` | Chủ/quản lý | Chờ duyệt: cũ nhất trước. |
| `POST .../student-requests/{id}/approve {expectedVersion, note?, acknowledgeConflicts?}` | Chủ/quản lý | Xin vắng: phiếu điểm danh điền sẵn "Vắng có phép" (`excusedAbsenceApproved = true`) cho tới khi có người chấm khác; bị từ chối nếu buổi đã huỷ hoặc phiếu đã nộp. Rút lớp: rút ngay (lý do "Học sinh xin rút: …"). Chuyển lớp: rút lớp cũ + ghi danh lớp mới trong một giao dịch, kiểm sĩ số và trùng lịch (409 như mục 2). Học bù: ghi nhận, trung tâm tự xếp buổi bù. |
| `POST .../student-requests/{id}/reject {expectedVersion, reason}` | Chủ/quản lý | Lý do bắt buộc. |

## 5. Kiểm thử

- Tự động: `RealPostgresPersonalScheduleTests` (hai trung tâm, một tài khoản học sinh, toàn bộ luồng ở mục 1–4 với
  PostgreSQL thật) và `ScheduleRulesDomainTests`. Chạy với `TM_E2E_CONNECTION_STRING` hoặc `AuthE2E.runsettings`.
- Tay (Swagger): tạo 2 lớp cùng giờ thứ Năm → ghi danh một học sinh tạo tay vào cả hai → lần 2 nhận 409 kèm danh
  sách → gửi lại `acknowledgeConflicts: true`. Dời một buổi có lý do → `GET /me/notices` bằng tài khoản học sinh có
  thông báo, `iam.email_outbox_messages` có email. Dừng lịch lặp bắt đầu sau 3 ngày → 422 `schedule_short_notice`.
