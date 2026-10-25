# Quy tắc đa trung tâm & lịch cá nhân (bản ghi nhớ thiết kế)

Trạng thái: **đã triển khai 22/10/2026** (B1–B8, F1–F6; xem mục 9 và `SCHEDULE-RULES-TEST.md`). Ngày ghi: 22/10/2026.
Liên quan: `REVIEW-NGHIEP-VU-BACKEND.md` (mục 2.1, 2.2, 2.3, 2.6, 3.1).

---

## 1. Mô hình: tách con người khỏi vai trò ở từng trung tâm

| Lớp dữ liệu | Ý nghĩa | Bội số |
|---|---|---|
| `User` | Một con người, một lần đăng nhập | 1 người = 1 tài khoản |
| `CenterMembership` | Vai trò của người đó ở một trung tâm | Nhiều dòng; tối đa 1 dòng / (trung tâm, tài khoản) — index `(CenterId, UserId)` đã có |
| `Teacher` (hồ sơ dạy) | Hồ sơ riêng của từng trung tâm: mã GV, tên, sau này lương | Mỗi trung tâm một hồ sơ; **thêm `UserId?`**, unique `(center_id, user_id)` |
| `Student` (hồ sơ học) | Hồ sơ riêng của từng trung tâm | Mỗi trung tâm một hồ sơ; `UserId?` + unique `(center_id, user_id)` **đã có** |

Mỗi trung tâm chỉ thấy hồ sơ, lớp và lịch của mình — cách ly dữ liệu giữ nguyên.

## 2. Quy tắc theo vai trò

| Vai trò | Quy tắc |
|---|---|
| Chủ trung tâm | **Mỗi tài khoản tối đa 1 vai trò CenterOwner đang hoạt động.** Chốt bằng unique index `(user_id) WHERE role = 'CenterOwner' AND is_active`. Lời mời chủ báo đúng lý do này thay cho "email đã tồn tại". |
| Quản lý / Giáo viên / Nhân viên | Được thuộc nhiều trung tâm. |
| Học sinh | Được học nhiều trung tâm qua một tài khoản Google; mỗi trung tâm một hồ sơ học sinh gắn với tài khoản. |
| Học sinh tạo tay (chưa có tài khoản) | Không liên kết chéo trung tâm; không tự gộp theo tên/SĐT. Chỉ kiểm trùng trong một trung tâm. |

**Cần chốt:** chủ trung tâm A có được dạy ở trung tâm B không? Đề xuất **cho phép** (quy tắc ở trên chỉ giới hạn vai trò chủ).

## 3. Mời thành viên (back-end chưa có)

1. Chủ/quản lý mời bằng email, chọn vai trò (Admin/Teacher/Staff), có thể chọn sẵn hồ sơ `Teacher` để gắn.
2. Email **chưa có tài khoản** → tạo tài khoản như lời mời chủ hiện nay.
3. Email **đã có tài khoản** → người nhận đăng nhập rồi chấp nhận; hệ thống chỉ tạo thêm membership và gắn `Teacher.UserId`, **không tạo tài khoản mới**.
4. Quản lý thành viên: danh sách, đổi vai trò, ngừng quyền (ngừng có hiệu lực ngay nhờ policy đọc DB mỗi request).

Quyền điểm danh mới cho giáo viên: lưu nháp/nộp phiếu cho buổi mà hồ sơ `Teacher` của mình được phân công hoặc là người dạy thực tế, trong cùng ngày. Hiệu chỉnh sau khi nộp vẫn chỉ Owner/Admin.

## 4. Lịch cá nhân = kết quả tính, không lưu bản riêng

- Trung tâm xếp lịch cho **lớp**; học sinh thuộc lớp qua **ghi danh** (có ngày vào, ngày rút); giáo viên thuộc buổi qua **phân công**.
- Lịch học sinh = buổi của các lớp em học, **chỉ trong khoảng ghi danh**. Lịch giáo viên = buổi được phân công ở mọi trung tâm.
- Không lưu bản sao lịch cho từng người (mỗi lần dời buổi sẽ phải sửa hàng chục bản sao).
- Lịch gộp nhiều trung tâm **chỉ chính người đó xem được**.

Sửa lỗi hiện tại: `ClassRegistrationStore.ListMySessionsAsync` phải lọc `session.StartUtc >= enrollment.EnrolledAtUtc` và giữ buổi trước `WithdrawnAtUtc` (hiện hiện cả buổi trước khi ghi danh và mất lịch sử sau khi rút).

## 5. Kiểm trùng: tại thời điểm người được gắn vào lớp/buổi

Không chặn khi trung tâm xếp lịch lớp, vì: lớp có giờ trước khi có học sinh; trung tâm không được biết lịch ở trung tâm khác; trung tâm có quyền dời buổi bất cứ lúc nào.

| Thời điểm | Trong cùng trung tâm | Giữa các trung tâm |
|---|---|---|
| Học sinh nộp đơn | Cảnh báo | Cảnh báo **cho học sinh** ("trùng T3 18:00 với lớp ở trung tâm khác"); em tự quyết |
| Ghi danh / duyệt đơn | **Chặn** (409 `student_schedule_conflict` kèm danh sách buổi), trừ khi gửi `acknowledgeConflicts: true` | Chỉ báo số lượng "học sinh có lịch khác trùng N buổi", không nêu trung tâm |
| Phân công giáo viên | Đã chặn (logic hiện có) | Cảnh báo **cho giáo viên** trong "Lịch của tôi" |
| Thêm/dời buổi | Báo trước khi lưu: "3 học sinh đang có buổi khác giờ này" | Không báo trung tâm; báo **cho học sinh/giáo viên** (mục 6) |

## 6. Khi trung tâm đổi lịch

Trung tâm vẫn có quyền đổi; hệ thống bảo đảm thay đổi **được báo trước, có lý do, đến tay người học, và có cách phản hồi**. Không chặn trung tâm, không tự xếp lại thay học sinh.

1. **Xem hậu quả trước khi lưu:** số học sinh bị ảnh hưởng; số sẽ bị trùng (cùng trung tâm: tên + lớp; trung tâm khác: chỉ số lượng).
2. **Lý do bắt buộc** cho dời, huỷ, thay lịch lặp (hiện back-end chưa lưu). Buổi diễn ra trong 24–48 giờ tới → đánh dấu **báo gấp**.
3. **Lịch sử thay đổi** mỗi buổi: lịch cũ, lịch mới, lý do, người đổi, thời điểm. Học sinh thấy "Đã dời từ T3 18:00 → T4 19:00 · lý do".
4. **Thông báo** qua email outbox có sẵn + trong app; nêu rõ nếu gây trùng với lớp khác của em.
5. **Học sinh phản hồi:**

| Lựa chọn | Hệ thống làm gì |
|---|---|
| Đã xem, vẫn đi được | Ghi nhận đã xem |
| Xin vắng có phép buổi đó | Phiếu điểm danh buổi đó điền sẵn "Vắng có phép" + lý do |
| Xin học bù / học ghép lớp cùng môn | Yêu cầu vào hàng chờ duyệt của trung tâm |
| Xin chuyển lớp (lịch lặp đổi hẳn) | Khi duyệt: rút lớp cũ + ghi danh lớp mới trong một giao dịch |
| Rút khỏi lớp | Rút có ngày hiệu lực và lý do, giữ lịch sử |

6. **Trung tâm theo dõi:** tổng quan có mục "Phản hồi đổi lịch" (vd. "Toán 9A: 15 đã xem · 2 xin vắng · 1 xin học bù").
7. **Hai mức:** đổi một buổi (đi học / xin vắng / học bù) và đổi hẳn lịch lặp (ưu tiên chuyển lớp / rút; đề xuất báo trước tối thiểu **7 ngày**).
8. Về sau, module học phí dựa vào đây để không tính hoặc hoàn phí cho buổi bị huỷ do trung tâm.

## 7. Hạng mục triển khai

### Back-end
| # | Hạng mục |
|---|---|
| B1 | Migration: `teachers.user_id` + unique; unique index chủ trung tâm; index `enrollments(student_id)`, `sessions(class_id, start_utc)` |
| B2 | Mời thành viên (mới + tài khoản đã có), danh sách/đổi vai trò/ngừng quyền |
| B3 | Policy giáo viên điểm danh buổi được phân công |
| B4 | `GET /me/schedule?from&to` (học sinh) và `GET /me/teaching-sessions` (giáo viên): gộp mọi trung tâm, kèm tên trung tâm/lớp/giáo viên/phòng; sửa lỗi lọc theo ngày ghi danh |
| B5 | Kiểm trùng: `POST /centers/{id}/class-applications/check`; chặn có xác nhận khi ghi danh/duyệt; cảnh báo khi thêm/dời buổi |
| B6 | Lý do cho dời/huỷ/thay lịch; bảng lịch sử thay đổi buổi |
| B7 | Thông báo đổi lịch theo học sinh (đã gửi/đã xem/đã phản hồi) qua outbox |
| B8 | Yêu cầu của học sinh: xin vắng, học bù, chuyển lớp — luồng duyệt như đơn đăng ký; nối "xin vắng" vào phiếu điểm danh |

### Front-end
| # | Hạng mục |
|---|---|
| F1 | Cổng học sinh: trang chính "Lịch học của tôi" — lịch tuần gộp, màu theo trung tâm, nhãn "Trùng giờ" / "Đã đổi" / "Đã huỷ" |
| F2 | Giáo viên nhiều trung tâm: trang "Lịch của tôi", bấm buổi mở đúng phiếu điểm danh của trung tâm đó |
| F3 | Màn nộp đơn: cảnh báo trùng trên từng lớp trước khi nộp |
| F4 | Trung tâm: hộp xác nhận trùng khi ghi danh/duyệt; xem hậu quả + lý do khi dời/huỷ/thay lịch |
| F5 | Mục "Thành viên" trong trung tâm; cột "Tài khoản đã gắn" ở hồ sơ giáo viên |
| F6 | Hàng chờ phản hồi đổi lịch & yêu cầu học sinh ở phía trung tâm |

Đã làm ở front-end: tài khoản chỉ có 1 trung tâm (không phải quản trị/học sinh) vào thẳng trung tâm; nút đổi trung tâm chỉ hiện khi có nơi khác để chuyển.

### Thứ tự đề xuất
1. **Lịch cá nhân** (B1, B4, F1, F2)
2. **Kiểm trùng khi vào lớp** (B5, F3, F4)
3. **Thông báo & phản hồi khi đổi lịch** (B6, B7, B8, F6)
4. **Thành viên & quyền giáo viên** (B2, B3, F5) — có thể làm song song từ đầu

## 8. Câu hỏi cần chốt

- Chủ trung tâm A có được làm giáo viên ở trung tâm B?
- ~~Báo trước khi đổi hẳn lịch lặp / ngưỡng báo gấp~~ → **Đã chốt:** 7 ngày, chỉ cảnh báo (phải xác nhận); báo gấp khi buổi diễn ra trong 24 giờ.
- ~~Trùng giữa các trung tâm khi duyệt~~ → **Đã chốt:** trung tâm chỉ thấy số buổi trùng.
- Liên quan: múi giờ cố định Asia/Ho_Chi_Minh; chủ trung tâm dạy được ở chính trung tâm mình bằng cùng tài khoản (xem `KE-HOACH-SUA-BACKEND.md`, D2).
- Tài khoản học sinh nhỏ tuổi: dùng Google của phụ huynh hay của học sinh; phụ huynh có cần tài khoản xem lịch riêng?

## 9. Tiến độ triển khai — xong 22/10/2026

| Đợt | Hạng mục | Kết quả |
|---|---|---|
| 1 — Lịch cá nhân | B4, F1, F2 | `GET /me/schedule` (≤ 42 ngày) gộp mọi trung tâm, vai trò học/dạy, trạng thái + lý do, lịch trước khi dời, buổi chồng giờ, trạng thái xin vắng. `my/sessions` trả cả buổi huỷ/không diễn ra kèm lý do. FE: `/student` thành trang **Lịch học của tôi** (thông báo, lịch 2 tuần màu theo trung tâm, yêu cầu); đăng ký học chuyển sang `/student/find`; mục **Lịch của tôi** trong trung tâm cho giáo viên nhiều trung tâm (bấm buổi mở phiếu điểm danh đúng trung tâm). |
| 2 — Kiểm trùng | B5, F3, F4 | `class-applications/check` (học sinh thấy cả tên trung tâm khác); ghi danh / duyệt đơn / duyệt chuyển lớp trả `409 student_schedule_conflict` kèm danh sách, gửi lại `acknowledgeConflicts`; trung tâm khác chỉ là số buổi. `sessions/impact` cho thêm/dời/huỷ buổi. FE: cảnh báo trên từng lớp khi chọn, hộp xác nhận trùng ở Đơn đăng ký / Ghi danh / Chuyển lớp, khung "N học sinh sẽ được báo" trong hộp dời/thêm/huỷ buổi. |
| 3 — Đổi lịch & phản hồi | B6, B7, B8, F6 | Bảng `schedule_changes` + `schedule_notices` + `student_requests`. Lý do bắt buộc cho dừng/thay lịch lặp; < 7 ngày phải xác nhận (`schedule_short_notice`); báo gấp trong 24 giờ. Thông báo trong app + email outbox cùng giao dịch. Học sinh: đã xem / xin vắng / học bù / chuyển lớp / rút. Trung tâm: trang **Phản hồi học sinh** (hàng chờ duyệt + "Đổi lịch gần đây" với số đã xem, xác nhận, yêu cầu theo loại); duyệt xin vắng điền sẵn "Vắng có phép" vào phiếu điểm danh; lịch dạy hiện "Đã dời từ …" và lịch sử thay đổi từng buổi. |

Kiểm chứng: build 0 cảnh báo; 3 + 49 + 137 test đạt (gồm `RealPostgresPersonalScheduleTests` với PostgreSQL thật); chạy API + FE thật trong trình duyệt (1440px và 390px, cả khi trình duyệt ở múi giờ khác Việt Nam).

Còn mở: thông báo khi trung tâm **duyệt/từ chối** yêu cầu mới chỉ hiện trong app (chưa gửi email); học sinh tạo tay (không tài khoản) chỉ được đếm "báo qua phụ huynh".

## 10. Hai loại tài khoản: học sinh và nhân sự — chốt 23/10/2026

Quyết định: **tách hẳn** tài khoản học sinh khỏi tài khoản nhân sự; học sinh **chỉ đăng nhập Google** (không có trang đăng ký mật khẩu). Thay cho dòng "1 người = 1 tài khoản" ở mục 1: quy tắc đó vẫn đúng **trong từng loại** (một học sinh một tài khoản cho mọi trung tâm; một nhân sự một tài khoản cho mọi trung tâm), nhưng một người vừa dạy vừa học dùng hai tài khoản với hai email khác nhau.

| | Tài khoản học sinh | Tài khoản nhân sự |
|---|---|---|
| Tạo bằng | Đăng nhập Google lần đầu | Lời mời (admin → chủ trung tâm; chủ → quản lý / giáo viên / nhân viên) |
| Vai trò | Chỉ `Student` (vai trò hệ thống) | Vai trò theo từng trung tâm (+ `PlatformAdmin` cho admin) |
| Đăng nhập | Google | Mật khẩu; Google nếu tự liên kết (phải nhập lại mật khẩu) |
| Trang chủ | `/student` | `/select-center` hoặc thẳng trung tâm |

Lý do: học sinh phần lớn là trẻ vị thành niên, nhân sự có quyền xem dữ liệu học sinh và phụ huynh; trộn hai loại gây nhầm (chủ trung tâm hiện trong danh sách học sinh, menu vừa "Trung tâm" vừa "Cổng học sinh"). Giống Google Workspace for Education (tài khoản giáo viên và học sinh tách riêng), khác Canvas/Moodle (vai trò theo từng khoá, dành cho người lớn).

Triển khai:
- `User.IsStudentAccount` (có vai trò `Student`). Liên kết Google **không còn cấp** vai trò `Student` — chỉ thêm cách đăng nhập.
- Mời thành viên, gửi lại lời mời, xem trước lời mời, nhận lời mời khi đã đăng nhập, mời chủ trung tâm: email thuộc tài khoản học sinh → `student_account_not_allowed`.
- Đăng nhập Google bằng email của tài khoản nhân sự chưa liên kết → `google_account_link_required` (không tự gộp theo email).
- Migration `SeparateStudentAndStaffAccounts` (IdentityAccess): gỡ `Student` khỏi mọi tài khoản đang có membership (kể cả đã ngừng) hoặc `PlatformAdmin`. Hồ sơ học sinh trong trung tâm giữ nguyên.
- FE: học sinh luôn vào `/student` (cả khi gõ `/select-center`); nhân sự vào `/student` thấy "Cổng chỉ dành cho tài khoản học sinh"; trang Tài khoản & bảo mật theo loại (học sinh: không form mật khẩu nếu không có, mục "Nơi học"; nhân sự: liên kết Google không đổi vai trò, không còn lối vào Cổng học sinh).
- Test: `RealPostgresGoogleLoginTests` (liên kết không cấp `Student`, token sau liên kết bị 403 ở API học sinh), `RealPostgresCenterMembersTests` (mời tài khoản học sinh bị từ chối). 3 + 49 + 137 test đạt.

## 11. M3 — Hồ sơ tạo tay, link kích hoạt và link mời vào lớp — chốt 23/10/2026

Mục tiêu: mỗi học sinh chỉ có **một** hồ sơ ở mỗi trung tâm, dù em được trung tâm thêm tay hay tự vào bằng Google.
Nguyên tắc: hồ sơ tạo tay **không cần email**; việc gắn tài khoản Google vào hồ sơ chỉ xảy ra khi em mở một link
**riêng cho hồ sơ đó** và tự xác nhận "Đúng là tôi". Hệ thống không đoán theo email hay số điện thoại.

### 11.1 Hai đường vào

| Đường | Ai dùng | Kết quả |
|---|---|---|
| **Link kích hoạt** (riêng từng hồ sơ) | Học sinh đã có hồ sơ do trung tâm tạo tay | Gắn tài khoản Google đang đăng nhập vào đúng hồ sơ đó. Không tạo hồ sơ mới. |
| **Link mời vào lớp + mã 6 ký tự** | Học sinh MỚI với trung tâm (hoặc đã có hồ sơ gắn tài khoản) | Vào lớp ngay, không cần duyệt. Chưa có hồ sơ → tạo hồ sơ mới gắn tài khoản. Đã có hồ sơ gắn tài khoản → dùng lại hồ sơ đó. |
| Đơn đăng ký từ danh bạ khoá học (B) | Học sinh tự tìm trung tâm | Giữ nguyên: trung tâm duyệt tay. |

### 11.2 Link kích hoạt
- Tạo ở trang **Học sinh** (nút "Link kích hoạt" trên hồ sơ chưa có tài khoản) hoặc ở trang lớp ("Link kích hoạt cho cả lớp": một link cho mỗi em trong lớp chưa có tài khoản).
- Hạn **14 ngày**, dùng **một lần**. Chỉ lưu SHA-256 của token. Tạo link mới thì link cũ của hồ sơ đó tự thu hồi.
- Em mở link (phải đăng nhập Google tài khoản học sinh) → thấy tên trung tâm, mã + họ tên hồ sơ, các lớp đang học → chọn:
  - **"Đúng là tôi"** → gắn tài khoản; link chuyển "Đã dùng".
  - **"Không phải tôi"** → link ngừng hoạt động; trung tâm thấy cờ "Người mở link báo ‘không phải tôi’".
- **Biến thể: tài khoản đã gắn một hồ sơ khác ở cùng trung tâm** (ví dụ em đã tự vào lớp B bằng link mời, sau đó mới nhận link kích hoạt của hồ sơ tạo tay ở lớp A): hệ thống **không** gắn, báo lỗi `activation_account_has_profile`, đồng thời đánh cờ **SameAccount** ("Cùng một học sinh với …") trên **cả hai** hồ sơ. Link vẫn còn hiệu lực. Trung tâm gộp hai hồ sơ (11.4), sau đó em thấy đủ cả hai lớp.

### 11.3 Link mời vào lớp
- Tạo ở trang lớp: hiệu lực **1 / 7 / 30 ngày** (mặc định 7), số lượt tối đa 1–500 hoặc không giới hạn (vẫn không vượt sĩ số). Mỗi link có **một mã 6 ký tự** (bỏ các ký tự dễ nhầm 0/O/1/I). Có thể **Đổi mã** (link giữ nguyên, mã cũ hết tác dụng) hoặc **Thu hồi**.
- Em mở link → xem lớp, môn, giáo viên, lịch tuần, số chỗ còn → trả lời **"Bạn đã từng học ở trung tâm này chưa?"**:
  - **"Rồi"** và tài khoản chưa có hồ sơ ở đây → bị chặn (`class_invite_use_activation_link`), hướng dẫn xin link kích hoạt riêng. Đây là chốt chặn chính để không sinh hồ sơ trùng.
  - **"Chưa"** → nhập mã + họ tên + tên và SĐT phụ huynh → vào lớp, hồ sơ mới được tạo.
  - Tài khoản đã có hồ sơ ở đây → không hỏi lại thông tin, chỉ nhập mã.
- Sai mã **5 lần** → khoá tài khoản đó trên link đó **15 phút** (`class_invite_locked`). Đúng mã thì bộ đếm về 0.
- Trùng giờ với lớp khác **cùng trung tâm** → cảnh báo, em phải xác nhận mới vào (409 kèm danh sách). Trùng với trung tâm khác chỉ hiện thông tin.
- Lưới an toàn: nếu em vẫn chọn "Chưa" dù đã có hồ sơ tạo tay cùng họ tên (không phân biệt hoa thường), hồ sơ mới mang cờ **SameName** ("Có thể trùng …") để trung tâm xem và gộp hoặc bỏ qua.
- Mỗi lần vào lớp bằng link được ghi lại ("Vào lớp bằng link gần đây": hồ sơ mới?, có trùng lịch?).

### 11.4 Gộp hồ sơ và gỡ tài khoản
- **Gộp** (trang Học sinh → "Gộp / bỏ qua" trên hồ sơ có cờ): chọn hồ sơ giữ lại. Ghi danh, điểm danh, yêu cầu, thông báo đổi lịch và lịch sử vào lớp chuyển sang hồ sơ giữ lại; tài khoản (nếu chỉ hồ sơ bị gộp có) cũng chuyển sang; hồ sơ bị gộp được lưu trữ kèm con trỏ "Đã gộp vào hồ sơ khác" và không kích hoạt lại được. Tất cả trong một giao dịch.
- Gộp bị chặn khi: hai hồ sơ gắn **hai tài khoản khác nhau** (`student_merge_two_accounts`); cùng đang học **một lớp** (`student_merge_shared_class`); cùng được điểm danh trong **một buổi** (`student_merge_shared_attendance` — dấu hiệu là hai em khác nhau).
- **Bỏ qua**: xoá cờ trùng khi chắc chắn là hai em khác nhau.
- **Gỡ tài khoản**: tách tài khoản khỏi hồ sơ (dùng khi gắn nhầm); hồ sơ và lịch sử giữ nguyên, có thể phát link kích hoạt mới.

### 11.5 Ghi danh tay học sinh đã có tài khoản
- Trước đây hồ sơ có tài khoản phải nộp đơn; nay trung tâm **ghi danh trực tiếp** được. Em nhận thông báo "Được thêm vào lớp" trong cổng học sinh (và email nếu đã cấu hình). Thông báo này không xuất hiện trong danh sách "Thay đổi lịch" của trung tâm.

### 11.6 Endpoint (prefix `/api/v1`)
| Phía | Method & route | Ghi chú |
|---|---|---|
| Trung tâm | `POST centers/{c}/students/{id}/activation-link` | Trả token thô (chỉ lần này) |
| | `POST centers/{c}/students/{id}/activation-link/revoke` | |
| | `POST centers/{c}/classes/{id}/activation-links` | Cho mọi em trong lớp chưa có tài khoản |
| | `POST centers/{c}/students/{id}/unlink-account` · `dismiss-duplicate` · `merge` | `expectedVersion`; merge nhận `sourceStudentId` |
| | `GET/POST centers/{c}/classes/{id}/invite-links` | `validDays`, `maxUses` |
| | `POST centers/{c}/invite-links/{id}/revoke` · `recode` | |
| | `GET centers/{c}/class-invite-joins?classId=` | |
| Học sinh | `GET student-activation/{token}` · `POST …/accept` · `POST …/decline` | Role Student |
| | `GET class-invites/{token}` · `POST …/join` | 409 khi trùng lịch, 429 khi bị khoá |

Bảng mới (schema `academics`): `student_activation_links`, `class_invite_links`, `class_invite_attempts`, `class_invite_joins`; cột mới ở `students`: `merged_into_student_id`, `duplicate_candidate_id`, `duplicate_reason`. Migration: `20261023054606_StudentActivationAndClassInvites` — chạy DbMigrator trước khi start API.
