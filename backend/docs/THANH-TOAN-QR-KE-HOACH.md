# Thanh toán học phí bằng QR — kiến trúc & kế hoạch các đợt

Trạng thái: **đã chốt hướng, Đợt 1 phần backend đã làm** (31/10/2026). Liên quan: `HOC-PHI.md` (H10, H11, H12). Module: `Finance`.

## 1. Quyết định đã chốt

| # | Câu hỏi | Chốt |
|---|---|---|
| T1 | Nền tảng | **SePay** báo biến động số dư, qua lớp `IBankFeedProvider` để sau này đổi được |
| T2 | Chủ tài khoản SePay | **Mỗi trung tâm tự đăng ký**, tự trả phí, nhập cấu hình vào hệ thống |
| T3 | QR | **Điền sẵn số tiền và nội dung**, mã riêng cho từng lần đóng |
| T4 | Tiền thực nhận vẫn lệch QR | Tự ghi **đúng số thực nhận**: thiếu là nợ, thừa là số dư (H11) |
| T5 | Ghi tay chuyển khoản khi đã bật tự động | **Chặn**, chỉ được gán giao dịch ngân hàng có thật. Tiền mặt ghi tay như cũ |
| T6 | Anh chị em | **Mỗi con một QR**, một giao dịch ứng với một biên lai của một học sinh. Chuyển gộp thì vào hàng chờ, xử lý ngoài luồng ở bản đầu |

Mặc định, đổi được nếu bạn muốn:

| # | Nội dung | Mặc định |
|---|---|---|
| D1 | Giao dịch không khớp mã | Vào hàng chờ, nhân viên gán tay. Không đoán theo tên hay số tiền |
| D2 | Nội dung cũ `HS000123 HOC PHI` | Vẫn khớp trong giai đoạn chuyển tiếp, mã QR mới được ưu tiên |
| D3 | Học sinh tự tạo QR | Có. Số tiền = các khoản đã chọn, mặc định tất cả khoản còn nợ, không sửa tay |
| D4 | Hạn mã QR | 7 ngày. Quá hạn vẫn ghi nhận tiền |
| D5 | Hoàn tiền | Ngoài phạm vi đợt này |
| D6 | Báo cho phụ huynh | Email và trong app |
| D7 | Đổi tài khoản ngân hàng | Chỉ Chủ, nhập lại mật khẩu, email cảnh báo |

## 2. Những ràng buộc đã kiểm tra

**Trong code**

| Phát hiện | Ảnh hưởng tới thiết kế |
|---|---|
| `ux_receipts_request` là unique trên `(center_id, request_id)`, và request trùng trả lại biên lai cũ kể cả đã huỷ | **Không** dùng `requestId` sinh từ mã giao dịch: huỷ rồi gán lại sẽ trả về biên lai đã huỷ. Thay bằng cột riêng `bank_transaction_id` (mục 4) |
| `Receipt.Create` ném lỗi nếu `createdByUserId` rỗng hoặc `amount > 10 × MaxFee` (1 tỉ đồng) | Biên lai tự động cần **tài khoản hệ thống** cố định; giao dịch quá lớn phải vào hàng chờ trước khi gọi `Create` |
| `TuitionWorkflow` phụ thuộc danh bạ người dùng, Worker không đăng ký danh bạ này | Tách phần ghi sổ dùng chung, Worker không gọi trực tiếp `TuitionWorkflow` |
| `Worker.cs` chạy tuần tự trong một vòng lặp: email rồi tới học phí hằng giờ | Thanh toán chạy trong **`BackgroundService` riêng** để không chặn và không bị chặn |
| `CenterLedger.LoadAsync` nạp toàn bộ sổ của trung tâm và giữ khoá trung tâm | Xử lý **theo lô** để chỉ nạp một lần cho nhiều giao dịch; không gọi HTTP khi đang giữ khoá |
| Hồ sơ gộp (M3): `ledger.Resolve` trỏ về hồ sơ giữ lại | Khớp mã xong phải `Resolve`, nếu không tiền vào hồ sơ đã bị gộp |
| API đặt `ReturnHttpNotAcceptable = true` và bộ lọc `Produces` chung | Webhook phải trả body cố định, tránh 406 tuỳ header `Accept` của SePay |
| Chưa có mã hoá secret. API và Worker là hai process | Cần khoá dùng chung cho cả hai: AES-256-GCM với khoá từ biến môi trường, lưu kèm `key_id` để xoay khoá |

**Trong tài liệu SePay** (kiểm tra 31/10/2026, cần xác nhận lại trên sandbox)

| Sự thật | Ảnh hưởng |
|---|---|
| HMAC-SHA256 trên `{timestamp}.{raw_body}`, header `X-SePay-Signature`, `X-SePay-Timestamp`, lệch tối đa 300 giây | Xác minh trên **raw body**, trước khi parse; máy chủ phải đồng bộ NTP |
| Thành công = HTTP 200/201 **và** body `{"success": true}` trong 30 giây; 202 bị tính là lỗi | Webhook lưu xong trả đúng dạng này |
| 8 lần gửi trong khoảng 33 phút, sau đó **có thể mất**; thứ tự không đảm bảo; `id` giữ nguyên khi gửi lại | Bắt buộc có job kéo lại giao dịch, chống trùng theo `id`, không phụ thuộc thứ tự |
| Trường `code` chỉ có khi công ty cấu hình tiền tố mã thanh toán | **Tự đọc mã từ `content`**, không phụ thuộc cấu hình từng trung tâm |
| Payload thử dùng `id` giả như 0 | `id <= 0` không được ghi tiền |
| `GET /userapi/transactions/list` lọc được theo `account_number`, ngày, `since_id`; tối đa 5.000 bản ghi. Tài liệu không nêu rate limit | Dùng cho đối soát và kéo bù |
| Giá: FREE 50 giao dịch/tháng, STARTUP từ 120.000đ/tháng; gói SHOP **không có API**. API trực tiếp 12 ngân hàng: VCB, BIDV, VietinBank, MB, ACB, VPBank, TPBank, Sacombank, MSB, OCB, KienLongBank, Shinhan; còn lại qua SMS | Trung tâm phải dùng gói có API và ngân hàng kết nối API trực tiếp |

## 3. Kiến trúc

```
[Đường tiền ra QR]
Học sinh/NV chọn khoản → PaymentIntent (mã, số tiền, ảnh chụp thông tin TK) → chuỗi VietQR → ứng dụng ngân hàng

[Đường tiền về]
SePay → POST /payments/webhooks/sepay/{endpointKey}
         ├ tìm cấu hình → kiểm HMAC + timestamp
         └ INSERT bank_transactions (trùng thì bỏ qua) → 200 {"success": true}

PaymentWorker (BackgroundService riêng, poll 3 giây)
  1. Transaction ngắn: nhận tối đa 50 dòng bằng FOR UPDATE SKIP LOCKED, ghi lease, commit
  2. Ngoài transaction: gọi API SePay đối chiếu lô, gom theo trung tâm
  3. Mở khoá trung tâm → nạp sổ một lần
  4. Chỉ xử lý dòng còn đúng lease và còn Received:
     đọc mã → khớp → tạo biên lai + phân bổ + đổi trạng thái + xếp email outbox
  5. Commit

ReconciliationJob: kéo giao dịch theo chu kỳ → thêm phần bị lỡ vào inbox → so tổng → cảnh báo
```

**Nguyên tắc thiết kế**

1. **Ngân hàng là nguồn sự thật về tiền**, sổ cái là nguồn sự thật về công nợ. Không sửa cái này để khớp cái kia.
2. **Tiền thật không bao giờ bị bỏ.** Mọi giao dịch tiền vào hoặc thành biên lai, hoặc nằm trong hàng chờ, hoặc bị bỏ qua có lý do và có người chịu trách nhiệm.
3. **Tách nhận và xử lý.** Webhook chỉ xác thực và lưu.
4. **Chỉ một đường ghi tiền chuyển khoản**, đi từ `bank_transactions`.
5. **Dữ liệu tài chính chỉ thêm, không xoá.** Sửa bằng huỷ có lý do.
6. **Ràng buộc đặt ở DB**, code sai thì DB vẫn chặn.
7. **Không gọi mạng khi giữ khoá hay transaction.**
8. **Không phụ thuộc thứ tự** webhook.

Pháp lý: tiền về thẳng tài khoản trung tâm, nền tảng không giữ tiền hộ. Nên xác nhận với tư vấn pháp lý trước khi thương mại hoá.

### Chốt kỹ thuật

**Dùng**

| Kỹ thuật | Chốt | Áp dụng thế nào |
|---|---|---|
| **DB làm queue** | **Có**, Postgres là hàng đợi duy nhất | Bảng `bank_transactions`, `PaymentWorker` poll 3 giây |
| **Nhận việc bằng lease** | **Có**, theo đúng mẫu `EmailOutboxProcessor` | `FOR UPDATE SKIP LOCKED`, ghi `processing_id` + `processing_until_utc` (2 phút), commit rồi mới gọi SePay. Worker chết thì lease hết hạn, dòng được nhận lại |
| **Inbox** | **Có**, `bank_transactions` chính là Inbox, không tạo bảng riêng | Webhook và job kéo bù cùng ghi vào đây. Mọi xử lý đều bắt đầu từ bảng này |
| **Xử lý xong có xoá không** | **Không xoá**, kể cả `raw_payload` | Đây là chứng từ tiền. Xong thì đổi trạng thái |
| **Outbox** | **Có**, dùng lại `iam.email_outbox_messages` qua `QueueEmailsAsync`, không tạo bảng mới | Email xác nhận được xếp trong cùng transaction với biên lai |
| **Idempotency** | **Có**, ở từng tầng (bảng dưới) | Đảm bảo bằng ràng buộc DB, không chỉ bằng code |
| **Khoá ghi sổ** | **Có**, `pg_advisory_xact_lock` theo trung tâm qua `BeginCenterGuardAsync` | Mọi thao tác đổi sổ đi qua đúng một khoá này |
| **Optimistic concurrency** | **Có** cho thao tác của người | `Version` trên `bank_transactions` và cấu hình, chặn hai nhân viên cùng gán hoặc cùng sửa |
| **Transaction** | **Có**, Read Committed như hiện tại | Biên lai, phân bổ, trạng thái, email chung một transaction |
| **Retry** | **Có** | Lỗi xử lý: sau 1, 5, 15, 60 phút, tối đa 5 lần rồi vào `NeedsReview` (`ProcessingFailed`). SePay mất kết nối: chờ tiếp, không tính vào số lần thử, cảnh báo sau 1 giờ |
| **Dead letter** | **Có, gộp vào hàng chờ** | Không có bảng riêng, `NeedsReview` để con người xử lý |
| **Đối soát** | **Có** | Kéo bù 15 phút một lần; quét lại toàn bộ 3 ngày gần nhất mỗi ngày |

**Idempotency ở từng tầng**

| Tầng | Khoá chống trùng | Khi trùng |
|---|---|---|
| Tạo QR | Cùng học sinh + cùng khoản + cùng số tiền + còn hạn, kiểm tra trong khoá trung tâm | Trả lại intent cũ |
| Webhook | Unique `(config_id, provider_txn_id)`, `ON CONFLICT DO NOTHING` | Vẫn trả `{"success": true}` để SePay ngừng gửi |
| Job kéo bù | Cùng unique trên | Bỏ qua |
| Worker chạy lại | Trạng thái kiểm tra trong khoá + partial unique trên `receipts` | Bỏ qua |
| Gán tay | `expectedVersion` + partial unique trên `receipts` | Gán lặp cùng học sinh trả biên lai cũ; khác học sinh báo xung đột |
| Ghi tiền mặt | `requestId` sẵn có | Trả biên lai cũ |
| Email | Xếp trong transaction tạo biên lai | Không có biên lai mới thì không có email mới |

**Đảm bảo tổng thể:** SePay giao *ít nhất một lần*, hệ thống chống trùng ở mọi tầng, nên kết quả là **mỗi giao dịch được ghi đúng một lần**.

**Không dùng**

| Kỹ thuật | Lý do |
|---|---|
| RabbitMQ / Kafka | Lưu lượng thấp; broker vẫn cần Outbox và idempotency |
| Redis / khoá phân tán | Advisory lock của Postgres đã đủ |
| Bảng Outbox thanh toán riêng | Bên ngoài chỉ cần nhận email, chưa có hệ thống khác cần sự kiện |
| Saga / 2PC | Mọi thay đổi nằm trong một DB |
| Event sourcing, CQRS tách DB | Sổ biên lai + phân bổ đã đủ truy vết; biên lai không xoá |
| Xử lý theo thứ tự webhook | SePay không đảm bảo thứ tự, mỗi giao dịch xử lý độc lập |
| LISTEN/NOTIFY | Poll 3 giây đã đủ nhanh cho việc ghi biên lai |

### Trạng thái giao dịch

```
Received → Booked                       (khớp, có biên lai)
Received → NeedsReview → Booked         (nhân viên gán)
Received → NeedsReview → Ignored        (Chủ/QL bỏ qua, có lý do)
Booked   → NeedsReview                  (biên lai bị huỷ, giao dịch quay lại hàng chờ)
```

`review_reason`: `NoCode`, `UnknownCode`, `AmbiguousCode`, `WrongAccount`, `TooLarge`, `VoidedReceipt`, `ProcessingFailed`, `UnverifiedTransaction` (SePay không xác nhận giao dịch, **không ghi tiền**). Giao dịch tiền ra và `id <= 0` không ghi tiền.

### Quy tắc khớp

1. Chiều tiền vào, số tiền nguyên dương, `id > 0`.
2. Số TK là TK đang cấu hình hoặc TK đã chụp trong intent; nếu không → `WrongAccount`, **không vứt bỏ**, vì đó vẫn có thể là tiền thật.
3. Số tiền ≤ giới hạn của biên lai; nếu không → `TooLarge`.
4. Chuẩn hoá `content` (viết hoa, bỏ ký tự ngoài chữ số), tìm mã dạng `HP` + 8 ký tự + 1 ký tự kiểm tra. Hơn một mã hợp lệ → `AmbiguousCode`.
5. Có intent thuộc trung tâm này → `Resolve` về hồ sơ giữ lại, phân bổ ưu tiên các khoản của intent. Intent đã `Paid`, `Expired` hay `Cancelled` vẫn ghi tiền; khoản dư thành số dư.
6. Không có mã mới → thử mã học sinh theo đúng cách `TuitionViews.TransferContent` tạo ra, phải khớp duy nhất (D2).
7. Còn lại → `NoCode` / `UnknownCode`.

## 4. Dữ liệu (schema `finance`)

| Bảng | Cột chính | Ràng buộc |
|---|---|---|
| `payment_provider_configs` | id, center_id, provider, endpoint_key, webhook_secret (mã hoá), api_token (mã hoá), key_id, mode (`Off`/`Shadow`/`Auto`), last_pulled_at, version | unique `(center_id, provider)`; unique `endpoint_key` |
| `payment_intents` | id, center_id, student_id, code, amount, charge_ids, bank_bin, account_number, account_name, status (`Pending`/`Cancelled`, `Paid` thêm ở Đợt 3; hết hạn suy ra từ `expires_at_utc`), expires_at_utc, cancelled_at_utc, created_by_user_id | unique `code` toàn hệ thống; check `amount > 0` |
| `bank_transactions` | id, config_id, center_id, provider_txn_id, reference_code, account_number, amount, content, transacted_at, raw_payload (jsonb), status, review_reason, intent_id, student_id, attempt_count, next_attempt_at, processing_id, processing_until_utc, last_error, source (`Webhook`/`Pull`), version | **unique `(config_id, provider_txn_id)`**; check `amount > 0`; partial index theo trạng thái chờ |
| `receipts` (sửa) | thêm `bank_transaction_id`, `source` (`Manual`/`Auto`) | **partial unique `bank_transaction_id WHERE bank_transaction_id IS NOT NULL AND voided_at_utc IS NULL`** |
| `payment_audit_events` | center_id, actor, action, target, before, after, at | chỉ INSERT |

Partial unique trên `receipts` cho phép huỷ rồi gán lại, nhưng một giao dịch **chỉ có tối đa một biên lai còn hiệu lực** ở mức DB.

Một số TK chỉ được bật tự động ở một trung tâm, để cùng một khoản tiền không vào hai sổ.

## 5. API (prefix `/api/v1`)

| Method & route | Quyền | Ghi chú |
|---|---|---|
| `POST centers/{c}/students/{id}/payment-intents` | Chủ, QL, NV | `{chargeIds?}`, trả `{code, amount, transferContent, qrPayload, bank, chargeIds, expiresAtUtc}` |
| `POST me/tuition/students/{id}/payment-intents` | Học sinh | Như trên, chỉ hồ sơ của mình |
| `POST payments/webhooks/sepay/{endpointKey}` | Anonymous + HMAC | Rate limit riêng, giới hạn body, đọc raw body |
| `GET centers/{c}/bank-transactions?status=` | Chủ, QL, NV | Hàng chờ |
| `POST centers/{c}/bank-transactions/{id}/assign` | Chủ, QL, NV | `{studentId, chargeIds?}` |
| `POST centers/{c}/bank-transactions/{id}/ignore` | Chủ, QL | `{reason}` |
| `GET/PUT centers/{c}/tuition/payment-provider` | Xem: Chủ, QL · Sửa: Chủ | Không bao giờ trả secret về client |
| `GET centers/{c}/tuition/reconciliation?from&to` | Chủ, QL | Tổng tiền vào, tổng biên lai, danh sách lệch |

`endpointKey` chỉ để định tuyến, có thể xuất hiện trong log; **secret là khoá HMAC**.

## 6. Kỹ thuật áp dụng

### Chính xác

| # | Rủi ro | Kỹ thuật |
|---|---|---|
| K1 | Gõ sai tiền, sai nội dung | QR chuẩn EMVCo do server sinh: tag 54 số tiền, tag 62.08 nội dung, CRC16 |
| K2 | Mã bị gõ sai hoặc biến dạng | Base32 bỏ ký tự dễ nhầm, có ký tự kiểm tra, chuẩn hoá trước khi tìm, bộ test mẫu nội dung thật |
| K3 | Webhook giả | HMAC so sánh constant-time, kiểm timestamp, secret riêng từng trung tâm |
| K4 | Secret webhook bị lộ | Đối chiếu API SePay trước khi ghi tiền; API lỗi thì giao dịch chờ, không ghi |
| K5 | Webhook gửi lặp | Inbox + unique `(config_id, provider_txn_id)` + `ON CONFLICT DO NOTHING` |
| K6 | Webhook mất | Chu kỳ kéo giao dịch bù, `since_id` + khoảng thời gian chồng lấn |
| K7 | Ghi hai biên lai | Partial unique ở `receipts` |
| K8 | Ghi dở dang | Biên lai, phân bổ, trạng thái, email outbox trong **một transaction** |
| K9 | Hai luồng đè nhau | Khoá trung tâm sẵn có, tái dùng `BeginCenterGuardAsync` |
| K10 | Ghi tay trùng tự động | Chặn `BankTransfer` thủ công khi `mode = Auto`; không chặn ở `Shadow` |
| K11 | Ghi nhầm học sinh | Không đoán; mã học sinh phải khớp duy nhất; gán tay có audit |
| K12 | Lệch tiền | Luôn ghi số thực nhận, phân bổ theo `CenterLedger.Spread` |
| K13 | Hồ sơ đã gộp | `Resolve` trước khi ghi |
| K14 | Quét lại QR cũ | Intent đã trả vẫn ghi nhận, phần dư thành số dư và báo cho nhân viên |
| K15 | Đổi TK để chiếm tiền | D7 + audit; intent chụp sẵn TK; QR luôn hiện tên chủ TK |
| K16 | Huỷ biên lai làm "mất" tiền | Giao dịch về `NeedsReview` (`VoidedReceipt`), tổng đối soát vẫn khớp |
| K17 | Lệch âm thầm | Bất biến: Σ tiền vào = Σ biên lai Auto còn hiệu lực + Σ chờ xử lý + Σ bỏ qua |
| K18 | Phát hành sai logic khớp | **Shadow mode**: chạy khớp trên giao dịch thật, chỉ ghi kết quả dự kiến, chưa tạo biên lai |

### Performance

| # | Kỹ thuật | Tác dụng |
|---|---|---|
| P1 | Webhook chỉ INSERT | Trả lời trong vài mili giây, không dính khoá sổ |
| P2 | Xử lý theo lô từng trung tâm | Đầu kỳ nhiều giao dịch vẫn chỉ nạp sổ một lần |
| P3 | Giới hạn kích thước lô | Không giữ khoá lâu, không chặn nhân viên đang thao tác |
| P4 | Gọi SePay trước khi mở transaction, có timeout | SePay chậm không kéo cả trung tâm |
| P5 | Partial index trên trạng thái chờ | Quét nhanh, không phụ thuộc lịch sử |
| P6 | Backoff + giới hạn số lần | Lỗi không quay vòng tốn tài nguyên |
| P7 | Cô lập dòng lỗi | Lô lỗi thì rollback, chạy lại từng giao dịch để tìm dòng hỏng, lô còn lại đi tiếp |
| P8 | Worker riêng | Thanh toán, email, nhắc nợ không chặn nhau |

**Chưa cần:** RabbitMQ/Kafka, chia khoá theo học sinh, partition. Chỉ làm khi đo thấy chậm.

## 7. Các đợt

### Đợt 0 — Chuẩn bị (không code nghiệp vụ)
- Tạo tài khoản test SePay, kiểm tra thực tế: `id` webhook có trùng `id` API không, timestamp khi gửi lại, rate limit, phạm vi quyền của API token.
- Thu thập mẫu `content` thật từ các ngân hàng có khách.
- Có domain HTTPS công khai cho API.
- Xác định repo frontend.
- Sửa H10 trong `HOC-PHI.md`.

**Xong khi:** nhận được webhook thử và có danh sách mẫu nội dung.

### Đợt 1 — QR động (backend xong, còn quét thử và frontend)
- `payment_intents`, sinh mã, bộ dựng chuỗi VietQR, hai endpoint tạo QR.
- Tái dùng intent còn hiệu lực nếu cùng khoản và cùng số tiền; số nợ đổi thì tạo mới.
- Frontend hiện QR và tên chủ TK.

**Xong khi:** quét QR trên app của các ngân hàng phổ biến ra đúng số tiền, nội dung, tên TK. Vẫn ghi biên lai tay như cũ.

### Đợt 2 — Kết nối và nhận giao dịch
- `payment_provider_configs`, mã hoá secret, màn cấu hình.
- Webhook, xác thực, inbox.
- Bảo vệ đổi TK ngân hàng (D7), audit.

**Xong khi:** giao dịch thật nằm trong `bank_transactions`, gửi lặp không tạo dòng mới, chữ ký sai bị từ chối. **Chưa ghi tiền.**

### Đợt 3 — Khớp và ghi biên lai
- Parser, bộ khớp, tách phần ghi sổ dùng chung, tài khoản hệ thống, `PaymentWorker`, thay đổi ở `receipts`.
- Chạy `Shadow` trên giao dịch thật và so với biên lai nhân viên ghi tay.
- Chỉ chuyển trung tâm sang `Auto` khi kết quả dự kiến khớp hoàn toàn.

**Xong khi:** test song song và gửi lặp không tạo trùng, bất biến K17 đúng, Shadow không có khớp sai.

### Đợt 4 — Hàng chờ và siết luồng tay
- Màn hàng chờ, gán, bỏ qua, huỷ biên lai trả giao dịch về hàng chờ.
- Chặn ghi tay chuyển khoản khi `Auto`.
- Email xác nhận cho phụ huynh qua outbox.

**Xong khi:** mọi giao dịch đều có đường xử lý, không còn cách ghi trùng.

### Đợt 5 — Đối soát và vận hành
- Job kéo giao dịch, báo cáo đối soát, cảnh báo: lệch tiền, webhook im lặng bất thường, hàng chờ tồn lâu, lỗi xử lý.
- Tài liệu hướng dẫn trung tâm tự kết nối.
- Mở rộng dần từng trung tâm.

**Xong khi:** tắt webhook thử thì job vẫn bù đủ giao dịch và báo cáo tổng khớp.

### Kiểm thử xuyên suốt
- Unit: chuỗi VietQR và CRC, mã kiểm tra, parser, bộ khớp.
- Tích hợp Postgres thật theo mẫu `RealPostgresTuitionTests.cs`: gửi lặp, song song, huỷ rồi gán lại, hồ sơ gộp, lô có dòng hỏng.
- Bất biến sổ cái: Σ phân bổ + Σ số dư = Σ biên lai chưa huỷ.
- Contract test: cập nhật `tests/ContractTests/Snapshots/openapi.v1.json`.

## 8. Còn mở

| # | Nội dung | Ảnh hưởng |
|---|---|---|
| M1 | Frontend `D:\Project\QuanLyDayThemFE` (Next.js) đã dùng `payment-intents` ở cổng học sinh và màn trung tâm, QR vẽ tại chỗ bằng `qrcode-generator`. Màn Cài đặt vẫn dùng `img.vietqr.io` cho mã mẫu. QR chỉ tạo cho khoản còn nợ, nên đóng trước qua QR chưa có | Đã chạy thử hai màn với API thật ngày 01/11/2026. Còn thiếu quét trên app ngân hàng thật bằng một tài khoản có thật |
| M2 | Các điểm SePay ở Đợt 0 | Có thể đổi chi tiết K4, K6 |

Nguồn: [Tích hợp webhook](https://developer.sepay.vn/vi/sepay-webhooks/tich-hop-webhook) · [Xác thực](https://developer.sepay.vn/vi/sepay-webhooks/xac-thuc) · [Xử lý lỗi](https://developer.sepay.vn/vi/sepay-webhooks/xu-ly-loi) · [API giao dịch](https://docs.sepay.vn/api-giao-dich.html) · [Bảng giá](https://sepay.vn/bang-gia.html)
