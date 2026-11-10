# Thanh toán học phí bằng QR — kiến trúc & kế hoạch các đợt

Trạng thái: **Đợt 0 và Đợt 1 xong; thiết kế nhận tiền đã chốt ngày 06/11/2026**: API SePay là nguồn sự thật duy nhất, webhook chỉ là chuông báo, chống trùng hai lớp (mục 3). Liên quan: `HOC-PHI.md` (H10, H11, H12). Module: `Finance`.

## 1. Quyết định đã chốt

| # | Câu hỏi | Chốt |
|---|---|---|
| T1 | Nền tảng | **SePay** báo biến động số dư, qua lớp `IBankFeedProvider` để sau này đổi được |
| T2 | Chủ tài khoản SePay | **Mỗi trung tâm tự đăng ký**, tự trả phí, nhập cấu hình vào hệ thống |
| T3 | QR | **Điền sẵn số tiền và nội dung**, mã riêng cho từng lần đóng |
| T4 | Tiền thực nhận vẫn lệch QR | Tự ghi **đúng số thực nhận**: thiếu là nợ, thừa là số dư (H11) |
| T5 | Ghi tay chuyển khoản khi đã bật tự động | **Chặn**, chỉ được gán giao dịch ngân hàng có thật. Tiền mặt ghi tay như cũ |
| T6 | Anh chị em | **Mỗi con một QR**, một giao dịch ứng với một biên lai của một học sinh. Chuyển gộp thì vào hàng chờ ở bản đầu (xem M3) |
| T7 | Nguồn ghi tiền | **Chỉ dữ liệu lấy từ API SePay** mới được ghi tiền. Webhook chỉ báo "có giao dịch mới", nội dung webhook không dùng để ghi |
| T8 | Khoá chống trùng | **Hai lớp.** Khoá chính: `(provider, id API v2)`. Khoá ngữ nghĩa: `(ngân hàng, số TK, referenceCode)` khi `referenceCode` khác rỗng. Trùng khoá ngữ nghĩa mà khác `id` → `Held`, không ghi tiền, email Chủ. Không ghép `id` webhook với `id` API |
| T12 | API SePay sập lâu | **Chờ**, không bao giờ ghi tiền từ nội dung webhook; email Chủ sau 1 giờ |
| T13 | Lưu webhook thô | **Giữ mãi**, làm bằng chứng khi đối soát hay tranh chấp |
| T9 | Giao dịch chỉ thấy qua đối soát | Tự ghi như mọi giao dịch khác, vì cùng một đường xử lý |
| T10 | Mã hoá secret | AES-256-GCM, nonce ngẫu nhiên, mục đích (trung tâm + trường) gắn vào associated data. Khoá từ cấu hình/biến môi trường: `Payments:EncryptionKeys:{id}` (Base64, 32 byte, giữ khoá cũ để giải mã), `Payments:ActiveKeyId`, `Payments:FingerprintKey` (Base64, ≥ 32 byte, không xoay). Mỗi cấu hình lưu `key_id` |
| T11 | Hàng chờ của nhân viên | Chỉ chứa câu hỏi nghiệp vụ "**tiền này của học sinh nào?**". Lỗi kỹ thuật không vào hàng chờ, hệ thống tự thử lại và email cho Chủ |

Mặc định, đổi được nếu bạn muốn:

| # | Nội dung | Mặc định |
|---|---|---|
| D1 | Giao dịch không khớp mã | Vào hàng chờ, hệ thống **gợi ý** học sinh (mã QR đang mở trùng số tiền, mã học sinh trong nội dung), nhân viên bấm chọn. Không tự gán theo gợi ý |
| D2 | Nội dung cũ `HS000123 HOC PHI` | Vẫn khớp trong giai đoạn chuyển tiếp, mã QR mới được ưu tiên |
| D3 | Học sinh tự tạo QR | Có. Số tiền = các khoản đã chọn, mặc định tất cả khoản còn nợ, không sửa tay |
| D4 | Hạn mã QR | 7 ngày. Quá hạn vẫn ghi nhận tiền |
| D5 | Hoàn tiền | Ngoài phạm vi đợt này |
| D6 | Báo cho phụ huynh | Email và trong app |
| D7 | Đổi tài khoản ngân hàng | Chỉ Chủ, nhập lại mật khẩu, email cảnh báo |
| D8 | Quyền hàng chờ | Gán: Chủ, QL, NV. Bỏ qua ("không phải học phí", bắt buộc lý do): Chủ, QL |
| D9 | Kéo định kỳ | 5 phút một lần mỗi trung tâm, kể cả không có chuông; quét lại 3 ngày gần nhất mỗi đêm |
| D10 | Cảnh báo cho Chủ | Email ngay: lỗi xử lý quá 5 lần, API SePay lỗi quá 1 giờ, dữ liệu SePay đổi sau khi đã ghi, tỉ lệ chuông giả bất thường. Email tổng hợp: hàng chờ có việc tồn quá 24 giờ |

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
| Chưa có mã hoá secret. API và Worker là hai process | Cần khoá dùng chung cho cả hai (T10) |
| `ICenterMemberDirectory` chỉ có `FindActiveAsync(center, user)`, không liệt kê được Chủ của một trung tâm; Finance chưa biết ai là Chủ | Thêm `ListActiveUserIdsAsync(centerId, roles)` vào contract của IdentityAccess, đăng ký bản đọc này trong Worker, rồi lấy email qua `ReadEmailsAsync` sẵn có. Trung tâm không có Chủ nào có email → ghi log và đếm vào chỉ số, không chặn xử lý |

**SePay, đã thử trên sandbox (Đợt 0, tháng 11/2026)**

| Sự thật | Ảnh hưởng |
|---|---|
| `id` webhook là số nguyên (vd. `44412`), `id` API v2 là UUID; hai `id` **khác nhau** cho cùng một giao dịch. Trường chung duy nhất là `referenceCode` / `reference_number`, có ngân hàng để rỗng | Lý do đổi sang T7, T8: không ghép hai `id` |
| API v2 có sandbox `https://userapi-sandbox.sepay.vn/v2`, `GET /v2/transactions` với `per_page`, `since_id`; giới hạn **3 request/giây**. API v1 không có sandbox và đang bị thay thế | Dùng API v2. Có bộ giới hạn tốc độ theo token |
| HMAC-SHA256 trên `{timestamp}.{raw_body}`, header `X-SePay-Signature` (`sha256=...`), `X-SePay-Timestamp`, lệch tối đa 300 giây | Xác minh trên **raw body**; máy chủ đồng bộ NTP. Chữ ký giờ chỉ chống spam chuông, không còn là lớp bảo vệ tiền |
| Thành công = HTTP 200/201 **và** body `{"success": true}`; tài liệu ghi timeout 30 giây ở một trang và 8 giây ở trang khác | Trả lời trong vài mili giây nên cả hai đều thoả |
| Gửi lại tối đa 7 lần trong khoảng 33 phút, quá 5 giờ thì không gửi lại; thứ tự không đảm bảo | Không phụ thuộc webhook: kéo định kỳ D9 bắt phần bị lỡ |
| `code` là `""` khi chưa cấu hình tiền tố, không phải `null`; `content` giữ nguyên văn; `transactionDate` dạng `yyyy-MM-dd HH:mm:ss` giờ Việt Nam | **Tự đọc mã từ `content`**; đổi giờ VN sang UTC khi lưu |
| Payload thử dùng `id` giả như 0 | Chuông thử ký đúng vẫn được ghi nhật ký và kích hoạt một lần kéo (vô hại, vì chỉ API ghi tiền); dùng được để thử kết nối |
| Giá: FREE 50 giao dịch/tháng, STARTUP từ 120.000đ/tháng; gói SHOP **không có API** | Trung tâm phải dùng gói có API |

**Còn phải đo ở Đợt 2, trước khi viết code (bước 2a):**

1. `id` API v2 giữ nguyên qua nhiều lần kéo (K20). **Không đạt thì dừng và xem lại thiết kế.**
2. `since_id` với `id` dạng UUID có trả đúng các giao dịch mới theo thứ tự ghi nhận không.
3. API v2 có lọc theo ngày không (cần cho quét lại 3 ngày mỗi đêm). Không có thì quét lùi theo trang đến khi gặp giao dịch cũ hơn 3 ngày.
4. Webhook gửi lại có mang `X-SePay-Timestamp` mới không. Nếu giữ timestamp cũ thì cửa sổ 300 giây sẽ chặn mọi lần gửi lại (xem "Cửa sổ timestamp" ở mục 3).
5. Độ trễ từ lúc webhook đến tới lúc API v2 thấy giao dịch.
6. Giới hạn 3 request/giây tính theo token hay theo tài khoản.
7. Tên trường mã ngân hàng trong API v2 (để tạo `bank_code`) và `referenceCode` trên tài khoản ngân hàng thật.

**Kết quả đo 2a trên sandbox (10/11/2026, 3 giao dịch thử, chỉ dùng GET)**

| # | Kết quả | Ảnh hưởng |
|---|---|---|
| 1 | **Đạt.** Cùng 3 `id`, cùng thứ tự, cùng dữ liệu nghiệp vụ qua 3 lần gọi danh sách; `GET /v2/transactions/{id}` trả đúng `id` và dữ liệu đó. `id` dạng UUID v1 (theo thời gian), vd. `254cd3a9-d7b0-11f1-...` | Thiết kế đứng được. Mẫu còn nhỏ và trong vài giây: **kiểm lại** sau vài ngày trên cùng giao dịch, và trên tài khoản thật trước khi bật `Auto` |
| 2 | **Đạt.** `since_id=X` trả đúng các giao dịch mới hơn X, không gồm X. Danh sách luôn mới nhất trước | Khi kéo phải đi hết các trang rồi mới dời con trỏ về `id` **mới nhất** đã ghi |
| 3 | **Có.** `transaction_date_from` / `transaction_date_to` (`yyyy-MM-dd HH:mm:ss`, giờ VN) lọc đúng | Quét 3 ngày mỗi đêm dùng lọc ngày, không cần quét lùi |
| 4 | **Ký lại với timestamp mới** (đo 10/11/2026: tắt API, giao dịch lúc 11:17:11, SePay tự gửi lại và được nhận lúc 11:31:59 với cửa sổ 300 giây; SePay hiển thị lần thử theo dạng "2/7", khoảng cách giãn dần: lần 1 → lần 2 khoảng 1 phút. Bấm "Gửi lại" bằng tay cũng ký với timestamp mới: giao dịch 47182 lúc 11:37:28 được nhận lúc 11:44:19) | Giữ cửa sổ 300 giây cho cả hai cách gửi lại |
| 5 | Chưa đo. Một lần gọi danh sách mất khoảng 0,5 giây | Đo ở bước 2e |
| 6 | Header trả `X-RateLimit-Limit: 3`, `X-RateLimit-Reset: 1` (giây) | Giới hạn 3 request/giây; đọc `X-RateLimit-Remaining` và `Retry-After` khi gặp 429 |
| 7 | Trường ngân hàng là `bank_brand_name` (vd. `Vietcombank`); `reference_number` có giá trị (`SB...`) trên sandbox | `bank_code` = `bank_brand_name` chuẩn hoá. `reference_number` trên TK thật vẫn phải kiểm |

**Thử webhook thật (bước 2d, 10/11/2026):** chữ ký đúng HMAC-SHA256 trên `{timestamp}.{raw body}`, header `X-SePay-Signature: sha256=<hex>`, `X-SePay-Timestamp` là Unix giây. Cả 3 lần 401 đầu tiên là do Secret Key dán trên SePay khác với Secret đã lưu; log `SePay webhook refused: ...` cho biết ngay lý do (thiếu header, lệch giờ bao nhiêu giây, sai định dạng hay không khớp secret) mà không ghi secret.

**Kéo thật (bước 2e, 10/11/2026):** một lượt `PaymentFetcher` trên sandbox cho trung tâm thử lấy về 6 giao dịch tiền vào, khớp 4/4 `referenceCode` trong nhật ký chuông, và cả **2 giao dịch có webhook bị 401** (Secret Key lệch lúc đầu): webhook hỏng không làm mất tiền. Lượt đầu lọc theo ngày kết nối, con trỏ dời tới `id` mới nhất, lịch kéo kế tiếp +5 phút.

- Trên sandbox, tiền vào tài khoản ảo: `account_number = 0000000001`, `va = SBSEPAY...`. Tài khoản học phí của trung tâm thử là số khác, nên `bank_bin` để trống. **Trước Đợt 3:** tài khoản nhận trong cài đặt học phí phải là đúng số tài khoản (hoặc VA) mà SePay báo, nếu không mọi giao dịch sẽ là `OtherAccount`.
- Độ trễ webhook → API (điểm đo 5) chưa đo riêng: lượt kéo chạy sau khi đã có đủ webhook. Đo khi chạy Worker liên tục ở Đợt 3.
- DB local còn nhiều cấu hình thanh toán do test để lại (khoá thử ngẫu nhiên). Worker gặp chúng chỉ ghi `last_pull_error` ("cannot be decrypted") và không gọi SePay; nên cho các test tự dọn hoặc dọn DB local trước khi chạy Worker lâu dài.

**Phát hiện thêm:**

- **Cloudflare chặn client không có User-Agent rõ ràng** (lỗi 1010 với User-Agent mặc định). SePay client phải gửi User-Agent cố định, vd. `TutorManagement/<version>`.
- Mỗi giao dịch có `bank_account_id` (UUID tài khoản ngân hàng trong SePay), `va` và `va_id` (tài khoản ảo), `transfer_type` (`in`/`out`), `webhook_success`. Lưu `bank_account_id` và `va` vào `bank_transactions`; quy tắc khớp TK phải xét cả số TK chính lẫn VA đã cấu hình. Chiều tiền đọc từ `transfer_type`.
- Số tiền là số nguyên (`amount_in: 100000`), không phải chuỗi như API v1.

## 3. Kiến trúc

```
[Đường tiền ra QR]
Học sinh/NV chọn khoản → PaymentIntent (mã, số tiền, ảnh chụp thông tin TK) → chuỗi VietQR → ứng dụng ngân hàng

[Chuông]
SePay → POST /payments/webhooks/sepay/{endpointKey}   (body tối đa 64 KB)
         ├ endpointKey không tồn tại → 404, body rỗng, không ghi gì
         ├ chữ ký hoặc timestamp sai → 401, chỉ tăng bộ đếm trên cấu hình, KHÔNG ghi dòng nào
         ├ chữ ký đúng → một transaction:
         │    ghi nhật ký chuông (trùng id webhook thì tăng delivery_count)
         │    nếu mode <> Off: đánh dấu cần kéo (pull_requested_at = now, next_pull_at = now)
         └ ghi được → 200 {"success": true}; ghi lỗi → 500 để SePay gửi lại

[Kéo — nguồn sự thật]  PaymentFetcher, chạy trong PaymentWorker
  1. Nhận cấu hình có next_pull_at <= now và last_pulled_at <= now - 1 giây,
     bằng lease + FOR UPDATE SKIP LOCKED. Mọi nhịp kéo đều lưu trong DB, Worker khởi động lại không mất:
       - có chuông: next_pull_at = now
       - không có gì: next_pull_at = last_pulled_at + 5 phút
       - có chuông mà API chưa thấy giao dịch mới: pull_retry_count + 1,
         next_pull_at = now + 1, 3, 10, 30 giây; hết 4 lần thì về nhịp 5 phút
  2. Ngoài transaction: GET /v2/transactions?since_id=<con trỏ>, qua bộ giới hạn 3 req/giây
  3. Một transaction: INSERT bank_transactions ON CONFLICT (provider, provider_txn_id) DO NOTHING
     (trùng thì tăng seen_count, so checksum), dời con trỏ, commit.
     Con trỏ chỉ dời khi đã ghi xong, nên lỗi giữa chừng không làm mất giao dịch
  4. Chiều tiền hoặc trường lạ chưa biết → lưu với trạng thái Failed, email Chủ, không ghi tiền
  5. Kéo được giao dịch mới thì pull_retry_count = 0

[Ghi sổ]  PaymentBooker, chạy trong PaymentWorker
  1. Transaction ngắn: nhận tối đa 50 dòng Received bằng FOR UPDATE SKIP LOCKED, ghi lease, commit
  2. Mở khoá trung tâm → nạp sổ một lần
  3. Chỉ xử lý dòng còn đúng lease và còn Received:
     đọc mã → khớp → tạo biên lai + phân bổ + đổi trạng thái + xếp email outbox
  4. Commit

[Đối soát]  Mỗi đêm quét lại 3 ngày gần nhất bằng đúng hàm Kéo (lọc theo ngày, hoặc quét lùi
            theo trang nếu API không lọc được), rồi so tổng (K17)
```

Webhook, kéo định kỳ và đối soát **đi chung một đường** (bước Kéo), nên chỉ có một khoá chống trùng và một bộ test. Mẫu này gọi là *thin webhook / fetch-on-notify*.

**Cửa sổ timestamp.** Mặc định ±300 giây theo tài liệu SePay, đổi bằng `Payments:WebhookTimestampToleranceSeconds` (60–21600). Chuông không ghi được tiền, nên một webhook thật bị phát lại chỉ gây thêm một lần kéo; cửa sổ này chỉ để chống spam. Nếu bước đo 4 cho thấy SePay gửi lại với timestamp cũ, nới cửa sổ lên 6 giờ (lớn hơn mốc 5 giờ SePay ngừng gửi lại). Dù chuông bị chặn, kéo định kỳ 5 phút vẫn lấy được giao dịch.

**Giới hạn tốc độ khi chạy nhiều Worker.** Mỗi cấu hình chỉ một Worker kéo tại một thời điểm (lease), và khoảng cách tối thiểu 1 giây giữa hai lần kéo lưu trong DB (`last_pulled_at`), nên đúng với bất kỳ số Worker nào. Token bucket trong bộ nhớ chỉ dàn đều các trang trong **một** lần kéo, mà một lần kéo luôn nằm trong một process. Giả định: mỗi token SePay chỉ thuộc một cấu hình; hai cấu hình dùng chung token bị chặn bằng unique trên `api_token_fingerprint`.

**Nguyên tắc thiết kế**

1. **Ngân hàng là nguồn sự thật về tiền**, sổ cái là nguồn sự thật về công nợ. Không sửa cái này để khớp cái kia.
2. **Chỉ dữ liệu từ API SePay mới ghi tiền.** Webhook giả hay lộ secret không tạo ra được đồng nào.
3. **Tiền thật không bao giờ bị bỏ.** Mọi giao dịch tiền vào hoặc thành biên lai, hoặc nằm trong hàng chờ, hoặc bị bỏ qua có lý do và có người hay quy tắc chịu trách nhiệm.
4. **Tách nhận và xử lý.** Webhook chỉ xác thực, ghi nhật ký và đánh dấu.
5. **Chỉ một đường ghi tiền chuyển khoản**, đi từ `bank_transactions`.
6. **Dữ liệu tài chính chỉ thêm, không xoá.** Sửa bằng huỷ có lý do.
7. **Ràng buộc đặt ở DB**, code sai thì DB vẫn chặn.
8. **Không gọi mạng khi giữ khoá hay transaction.**
9. **Không phụ thuộc thứ tự** hay sự có mặt của webhook.
10. **Nhân viên chỉ trả lời câu hỏi nghiệp vụ**, việc kỹ thuật do hệ thống tự làm hoặc báo cho Chủ.

Pháp lý: tiền về thẳng tài khoản trung tâm, nền tảng không giữ tiền hộ. Nên xác nhận với tư vấn pháp lý trước khi thương mại hoá.

### Chốt kỹ thuật

**Dùng**

| Kỹ thuật | Chốt | Áp dụng thế nào |
|---|---|---|
| **Thin webhook / fetch-on-notify** | **Có** | Webhook chỉ là chuông, giao dịch luôn lấy từ API v2 |
| **DB làm queue** | **Có**, Postgres là hàng đợi duy nhất | Cờ `pull_requested_at` trên cấu hình cho bước Kéo; `bank_transactions` cho bước Ghi sổ |
| **Nhận việc bằng lease** | **Có**, theo đúng mẫu `EmailOutboxProcessor` | `FOR UPDATE SKIP LOCKED`, ghi `processing_id` + `processing_until_utc` (2 phút), commit rồi mới gọi SePay. Worker chết thì lease hết hạn, việc được nhận lại. Mỗi cấu hình chỉ một worker kéo tại một thời điểm |
| **Inbox** | **Có**, `bank_transactions` chính là Inbox | Chỉ bước Kéo ghi vào đây. Mọi xử lý tiền bắt đầu từ bảng này |
| **Nhật ký chuông** | **Có**, `payment_webhook_deliveries` | **Chỉ lưu chuông ký đúng**: nguyên body, `delivery_count`, `last_seen_at`. Chuông sai chữ ký chỉ tăng `invalid_signature_count` trên cấu hình, nên kẻ xấu không chiếm trước được `webhook_id` và không làm đầy ổ đĩa. Chỉ để tra soát và đo, không ghi tiền |
| **Gom chuông** | **Có**, cờ trong DB | Nhiều chuông trong lúc đang kéo chỉ đặt lại `next_pull_at`, dẫn tới một lần kéo tiếp theo |
| **Giới hạn tốc độ gọi SePay** | **Có** | Khoảng cách 1 giây giữa hai lần kéo lưu trong DB; token bucket 3 request/giây trong một lần kéo. Chờ khi hết lượt, không tính là lỗi. Xem "Giới hạn tốc độ khi chạy nhiều Worker" |
| **Xử lý xong có xoá không** | **Không xoá**, kể cả `raw_payload` | Đây là chứng từ tiền. Xong thì đổi trạng thái |
| **Outbox** | **Có**, dùng lại `iam.email_outbox_messages` qua `QueueEmailsAsync` | Email xác nhận và email cảnh báo được xếp trong cùng transaction với thay đổi gây ra chúng |
| **Idempotency** | **Có**, ở từng tầng (bảng dưới) | Đảm bảo bằng ràng buộc DB, không chỉ bằng code |
| **Checksum dữ liệu** | **Có**, `payload_checksum` trên `bank_transactions` | Tính trên **các trường nghiệp vụ đã chuẩn hoá** (số TK, chiều, số tiền, nội dung, thời điểm, `referenceCode`), không tính trên JSON thô, để SePay thêm trường hay đổi thứ tự key không gây báo động giả. Không bao giờ dùng làm khoá chống trùng. Lần kéo sau thấy cùng `id` mà checksum khác → giữ bản đã ghi, email cảnh báo Chủ (D10) |
| **Cập nhật có điều kiện** | **Có**, mọi chuyển trạng thái | `UPDATE ... WHERE id = @id AND status = @expected AND processing_id = @lease`. Không đổi dòng nào thì phân biệt: đã xử lý rồi (bỏ qua), mất lease (bỏ qua), trạng thái lạ (ghi log, cảnh báo) |
| **Chống spam chuông** | **Có** | Chuông chỉ được đánh dấu sau khi chữ ký đúng; gom chuông và khoảng cách tối thiểu 1 giây khiến chuông giả không đốt được hạn mức 3 req/giây |
| **Lưu trữ** | **Giữ mãi** (T13) | `bank_transactions`, `receipts`, audit, `payment_webhook_deliveries` đều không xoá. Lưu lượng thấp, mỗi webhook vài KB |
| **Khoá ghi sổ** | **Có**, `pg_advisory_xact_lock` theo trung tâm qua `BeginCenterGuardAsync` | Mọi thao tác đổi sổ đi qua đúng một khoá này |
| **Optimistic concurrency** | **Có** cho thao tác của người | `Version` trên `bank_transactions` và cấu hình, chặn hai nhân viên cùng gán hoặc cùng sửa |
| **Transaction** | **Có**, Read Committed như hiện tại | Biên lai, phân bổ, trạng thái, email chung một transaction |
| **Retry** | **Có** | Ghi sổ lỗi: sau 1, 5, 15, 60 phút, tối đa 5 lần rồi sang `Failed` và email Chủ. SePay mất kết nối: chờ tiếp, không tính vào số lần thử, cảnh báo sau 1 giờ |
| **Dead letter** | **Có**, trạng thái `Failed` | Không có bảng riêng. Không hiện cho nhân viên; Chủ hoặc kỹ thuật bấm "Thử lại" sau khi sửa |
| **Đối soát** | **Có** | Kéo định kỳ 5 phút (D9), quét lại 3 ngày mỗi đêm, so tổng K17. Mỗi webhook ký đúng có `referenceCode` phải tìm thấy giao dịch API cùng ngân hàng, TK, `referenceCode`, số tiền; quá 1 giờ không thấy → email Chủ. Đây chỉ là kiểm tra, không phải đường ghi tiền |
| **Phát lại** | **Có**, chỉ qua inbox | Chạy lại là đưa dòng về `Received` qua endpoint `retry` hoặc kéo lại, để đi qua đúng các lớp chống trùng của đường chính. **Không sửa dữ liệu tiền bằng script SQL** |

**Idempotency ở từng tầng**

| Tầng | Khoá chống trùng | Khi trùng |
|---|---|---|
| Tạo QR | Cùng học sinh + cùng khoản + cùng số tiền + còn hạn, kiểm tra trong khoá trung tâm | Trả lại intent cũ |
| Chuông | Unique `(config_id, webhook_id)` trên nhật ký chuông, chỉ chuông ký đúng | Tăng `delivery_count`, vẫn trả `{"success": true}` |
| Kéo / kéo định kỳ / đối soát | **Unique `(provider, provider_txn_id)`**, không kèm `config_id` (xem "Phạm vi khoá"), `provider_txn_id` = `id` API v2, `ON CONFLICT DO NOTHING` | Bỏ qua, chỉ so checksum |
| Worker chạy lại | Trạng thái kiểm tra trong khoá + partial unique trên `receipts` | Bỏ qua |
| Gán tay | `expectedVersion` + partial unique trên `receipts` | Gán lặp cùng học sinh trả biên lai cũ; khác học sinh báo xung đột |
| Ghi tiền mặt | `requestId` sẵn có | Trả biên lai cũ |
| Email | Xếp trong transaction tạo biên lai | Không có biên lai mới thì không có email mới |

**Phạm vi khoá (chìa khoá số 1 của bài viết: khoá phải đại diện cho đúng một tác động nghiệp vụ)**

Tác động nghiệp vụ ở đây là **một khoản tiền vào một tài khoản ngân hàng**. Khoá phải trùng đúng phạm vi đó, không rộng hơn và không hẹp hơn:

| Khoá thử | Vấn đề | Chốt |
|---|---|---|
| `(config_id, provider_txn_id)` | **Quá hẹp.** Chủ xoá cấu hình rồi tạo lại sẽ ra `config_id` mới, cùng giao dịch lọt qua khoá thành dòng thứ hai | Không dùng |
| `(provider, provider_txn_id)` | Đúng một giao dịch SePay, không phụ thuộc cấu hình | **Dùng** |
| Cùng TK ngân hàng gắn vào hai tài khoản SePay khác nhau | SePay cấp hai `id`, khoá trên không bắt được | Partial unique `(bank_bin, account_number) WHERE mode = 'Auto'` trên cấu hình: một TK chỉ tự ghi ở một nơi |
| Mã thanh toán, intent, hay "số tiền + nội dung" | **Quá rộng**, giống bẫy `order_id` trong bài: phụ huynh chuyển hai lần cùng mã, cùng số tiền là **hai khoản tiền thật**, phải ghi cả hai | Không bao giờ dùng để chống trùng |
| Hash payload | Bài viết đã chỉ ra: payload đổi hình thức mà cùng nghĩa | Chỉ dùng làm checksum |
| `(ngân hàng, số TK, referenceCode)` | Đại diện đúng nhất cho "một khoản tiền vào một TK", vì đó là mã của ngân hàng chứ không phải mã của SePay. Nhưng SePay ghi rõ trường này "có thể rỗng" | **Dùng làm lớp thứ hai** (T8), chỉ khi khác rỗng. Không bao giờ là lớp duy nhất |

Cách ghi hai lớp trong một transaction của bước Kéo (mỗi cấu hình chỉ một worker kéo tại một thời điểm):

1. `INSERT ... ON CONFLICT DO NOTHING` với cả khoá chính lẫn cột `semantic_key` (unique, null khi `referenceCode` rỗng).
2. Không chèn được: đã có cùng `id` thì đó là lần thấy lại, tăng `seen_count` và so checksum. Khác `id` mà trùng `semantic_key` thì chèn lại với `semantic_key = null`, `status = Held`, `duplicate_of_id` trỏ về dòng cũ, và xếp email cho Chủ.

**Chống trùng nằm ngay trên đường tạo tác động (chìa khoá số 2)**

Khoá ở inbox chặn trùng lúc nhận. Nhưng tác động thật là **biên lai**, nên biên lai phải tự có khoá, nằm trong cùng transaction với lệnh tạo biên lai. Như vậy cả đường thử lại, nút phát lại hay một lỗi trong code điều phối đều không vượt qua được:

- Partial unique `bank_transaction_id WHERE voided_at_utc IS NULL`: mỗi giao dịch có tối đa **một biên lai còn hiệu lực**.
- Partial unique `bank_transaction_id WHERE source = 'Auto'`, **tính cả biên lai đã huỷ**: mỗi giao dịch được **tự ghi tối đa một lần trong đời**. Nhân viên đã huỷ biên lai tự động thì chỉ người mới gán lại được. Một lần phát lại không thể tự ghi lại, làm mất quyết định huỷ của nhân viên.
- Email xếp trong cùng transaction với biên lai, nên không có biên lai mới thì không có email mới.
- Không có kho chống trùng nào nằm trong bộ nhớ hay Redis. Token bucket trong bộ nhớ chỉ là tối ưu: mất đi thì chỉ phải chờ hoặc gọi lại API, không ghi trùng. Cờ gom chuông và nhịp kéo nằm trong DB.

**Đảm bảo tổng thể:** API SePay có thể trả một giao dịch nhiều lần, hệ thống chống trùng bằng một khoá ở mọi đường, nên **mỗi giao dịch được ghi đúng một lần**.

**Không dùng**

| Kỹ thuật | Lý do |
|---|---|
| Ghép `id` webhook với `id` API (record linkage) | Không có khoá chung chắc chắn; ghép theo `referenceCode` có thể sai hoặc không ghép được, sinh ra việc cho nhân viên |
| Ghi tiền từ nội dung webhook | Webhook giả hay lộ secret sẽ thành tiền giả |
| Nonce + Redis | SePay không gửi nonce; hai kênh là hai tin khác nhau; khoá TTL không bền cho tiền. Unique Postgres đã đủ |
| RabbitMQ / Kafka | Lưu lượng thấp; broker vẫn cần Outbox và idempotency |
| Redis / khoá phân tán | Advisory lock của Postgres đã đủ |
| Bảng Outbox thanh toán riêng | Bên ngoài chỉ cần nhận email, chưa có hệ thống khác cần sự kiện |
| Saga / 2PC | Mọi thay đổi nằm trong một DB |
| Event sourcing, CQRS tách DB | Sổ biên lai + phân bổ đã đủ truy vết; biên lai không xoá |
| LISTEN/NOTIFY | Poll 1 giây trên partial index đã đủ nhanh; thêm vào sau nếu đo thấy cần |

### Đối chiếu với bài "Webhook Idempotency cho Payment Systems"

Bài viết giả định webhook là nguồn sự kiện. Ở đây API là nguồn, nhưng các nguyên tắc vẫn áp dụng nguyên vẹn:

| Bài viết yêu cầu | Ở đây |
|---|---|
| Khoá chống trùng đúng ngữ nghĩa: ưu tiên `id` ổn định của nhà cung cấp; nhiều nguồn thì phải có khoá chuẩn hoá chung | Gộp về một nguồn, nên không cần khoá logic dễ vỡ (phương án 3 vỡ khi `referenceCode` rỗng, mà SePay ghi rõ trường này "có thể rỗng"). Khoá chính `(provider, provider_txn_id)`, khoá ngữ nghĩa `(ngân hàng, số TK, referenceCode)` làm lớp thứ hai. Xem "Phạm vi khoá" |
| Không dùng hash payload, không dùng `order_id` làm khoá | Checksum chỉ để phát hiện đổi dữ liệu; mã thanh toán và intent không bao giờ dùng để chống trùng |
| Kho chống trùng bền, có unique, có trạng thái, mốc thời gian, nguồn, tham chiếu thô, tra cứu được cho hỗ trợ | Unique Postgres; `status`, `first_seen_at`, `last_seen_at`, `seen_count`, `booked_at`, `last_error`, `fetched_by`, `raw_payload`, `reference_code`; index tra theo ngày và số tiền |
| Kho chống trùng nằm trên đường tác động, không ở bộ nhớ | Hai partial unique trên `receipts`, cùng transaction với biên lai |
| "Đã có dòng" không có nghĩa là "đã xử lý xong" | Trạng thái `Received` → `Booked`, lease hết hạn thì nhận lại |
| Inbox hai pha, trả ACK sau khi đã ghi bền; ghi lỗi thì trả lỗi | Chuông ghi bền rồi mới 200, lỗi thì 500. Giao dịch ghi bền ở bước Kéo |
| Chuyển trạng thái có điều kiện | `UPDATE ... WHERE status = ... AND processing_id = ...` |
| Đua giữa nhiều worker phải được quyết định nguyên tử | `ON CONFLICT DO NOTHING`, `SKIP LOCKED`, advisory lock theo trung tâm |
| Sổ cái có unique riêng, không chỉ tin lớp webhook | Partial unique `bank_transaction_id` trên `receipts` |
| Sự kiện đến sai thứ tự | Chỉ có một loại sự kiện (tiền vào), mỗi giao dịch độc lập. Trường hay chiều lạ → `Failed`, không đoán |
| Chữ ký và idempotency là hai lớp riêng, cần cả hai | Có cả hai. Ở đây chữ ký bảo vệ hạn mức API, không phải tiền |
| Đo tỉ lệ trùng, không chỉ tỉ lệ lỗi | Bộ chỉ số ở Đợt 5 |
| Giữ khoá chống trùng đủ lâu; tách lưu trữ payload | Giữ mãi cả khoá lẫn dữ liệu thô (T13) |
| Đối soát là lưới an toàn cuối | Kéo 5 phút + quét 3 ngày mỗi đêm, so bốn lớp |
| Phát lại qua inbox, không chạy script trực tiếp | Endpoint `retry` hoặc kéo lại |

### Trạng thái giao dịch

```
Received → Booked                       (khớp, có biên lai)
Received → NeedsReview → Booked         (nhân viên gán)
Received → NeedsReview → Ignored        (Chủ/QL: không phải học phí, có lý do)
Received → Ignored                      (tự động: không phải tài khoản học phí, có lý do)
Received → Failed → Received            (lỗi kỹ thuật, Chủ/kỹ thuật bấm Thử lại)
Held     → Received                     (Chủ xác nhận là giao dịch riêng, không phải bản trùng)
Held     → Ignored                      (Chủ xác nhận là bản trùng của SePay)
Booked   → NeedsReview                  (biên lai bị huỷ mà không gán lại ngay)
```

`review_reason` (nhân viên thấy, tất cả hiện chung một màn "Tiền chưa rõ của ai"): `NoCode`, `UnknownCode`, `AmbiguousCode`, `VoidedReceipt`. `TooLarge` chỉ Chủ/QL thấy.

`Held`: chỉ Chủ thấy, có hai nút **Là giao dịch riêng** (về `Received`) và **Là bản trùng** (`Ignored`, `ignore_reason = Duplicate`). Trước khi chọn, Chủ phải đối chiếu sao kê ngân hàng.

`ignore_reason` tự động: `OtherAccount`. Giao dịch tiền ra không lưu. Chuông thử (`id <= 0`) chỉ là một lần kéo thừa.

### Quy tắc khớp

1. Chiều tiền vào, số tiền nguyên dương.
2. Số TK là TK đang cấu hình hoặc TK đã chụp trong một intent của trung tâm; nếu không → `Ignored` (`OtherAccount`), vẫn lưu và hiện trong nhật ký.
3. Số tiền ≤ giới hạn của biên lai; nếu không → `TooLarge`.
4. Chuẩn hoá `content` (viết hoa, bỏ ký tự ngoài chữ số), tìm mã dạng `HP` + 8 ký tự + 1 ký tự kiểm tra. Hơn một mã hợp lệ → `AmbiguousCode`.
5. Có intent thuộc trung tâm này → `Resolve` về hồ sơ giữ lại, phân bổ ưu tiên các khoản của intent. Intent đã `Paid`, `Expired` hay `Cancelled` vẫn ghi tiền; khoản dư thành số dư.
6. Không có mã mới → thử mã học sinh theo đúng cách `TuitionViews.TransferContent` tạo ra, phải khớp duy nhất (D2).
7. Còn lại → `NoCode` / `UnknownCode`, kèm danh sách gợi ý (D1).

### Nhân viên xử lý thế nào

Một màn hình, một câu hỏi: **tiền này của học sinh nào?** Luôn đối chiếu với sao kê ngân hàng nếu không chắc.

| Trường hợp | Nhân viên làm gì |
|---|---|
| Không có mã, mã sai | Xem nội dung và gợi ý, chọn học sinh, bấm **Gán**. Không phải học phí thì Chủ/QL bấm **Không phải học phí** và ghi lý do |
| Hai mã (chuyển gộp) | Gán cho một em theo thoả thuận với phụ huynh (T6, M3) |
| Huỷ biên lai tự động | Màn huỷ hỏi luôn "gán lại cho ai?". Bỏ trống thì giao dịch vào hàng chờ |
| Chủ nhận email "giao dịch nghi trùng" (`Held`, rất hiếm) | Mở sao kê ngân hàng: thấy **hai** khoản tiền thật thì bấm **Là giao dịch riêng**; chỉ thấy **một** thì bấm **Là bản trùng** |
| Phụ huynh báo đã chuyển mà chưa thấy | Tìm trong hàng chờ theo số tiền và ngày. Không có và đã quá 10 phút: kiểm tra trên my.sepay.vn; có trên SePay mà không có trong hệ thống thì báo kỹ thuật |

## 4. Dữ liệu (schema `finance`)

| Bảng | Cột chính | Ràng buộc |
|---|---|---|
| `payment_provider_configs` | id, center_id, provider, endpoint_key, webhook_secret (mã hoá), api_token (mã hoá), api_token_fingerprint (HMAC của token, không giải mã được), key_id, bank_bin, account_number, mode (`Off`/`Shadow`/`Auto`), auto_since_utc, pull_requested_at, next_pull_at, pull_retry_count, last_pulled_at, pull_cursor, pull_processing_id, pull_processing_until_utc, last_pull_error, invalid_signature_count, last_invalid_signature_at, version | unique `(center_id, provider)`; unique `endpoint_key`; unique `(provider, api_token_fingerprint)`; partial unique `(bank_bin, account_number) WHERE mode = 'Auto'`; partial index `next_pull_at WHERE mode <> 'Off'` |
| `payment_webhook_deliveries` | id, config_id, webhook_id, reference_code, amount, raw_body, delivery_count, first_seen_at, last_seen_at | unique `(config_id, webhook_id)`; **chỉ chuông ký đúng**; chỉ INSERT/UPDATE đếm. `reference_code`, `amount` để đối soát chuông ↔ API |
| `payment_intents` | id, center_id, student_id, code, amount, charge_ids, bank_bin, account_number, account_name, status (`Pending`/`Cancelled`, `Paid` thêm ở Đợt 3; hết hạn suy ra từ `expires_at_utc`), expires_at_utc, cancelled_at_utc, created_by_user_id | unique `code` toàn hệ thống; check `amount > 0` |
| `bank_transactions` | id, config_id, center_id, **provider**, provider_txn_id (`id` API v2), **bank_code** (mã ngân hàng SePay trả, chuẩn hoá), **bank_bin** (lấy từ cấu hình khi số TK khớp; TK khác thì null), reference_code, account_number, provider_bank_account_id (`bank_account_id`), virtual_account (`va`), transfer_type, amount, content, transacted_at_utc, raw_payload (jsonb), semantic_key, duplicate_of_id, payload_checksum, fetched_by (`Doorbell`/`Periodic`/`Nightly`/`Manual`), first_seen_at, last_seen_at, seen_count, booked_at, status, review_reason, ignore_reason, intent_id, student_id, attempt_count, next_attempt_at, processing_id, processing_until_utc, last_error, version, **shadow_outcome, shadow_reason, shadow_intent_id, shadow_student_id, shadow_evaluated_at** (kết quả dự kiến ở `Shadow`: thêm cột ngay ở Đợt 2, điền từ Đợt 3) | **unique `(provider, provider_txn_id)`**; **unique `semantic_key`** (null được phép, khi `referenceCode` rỗng hoặc dòng `Held`); check `amount > 0`; partial index theo trạng thái chờ; index `(center_id, transacted_at_utc)` và `(center_id, amount)` để tra khi phụ huynh hỏi |
| `receipts` (sửa) | thêm `bank_transaction_id`, `source` (`Manual`/`Auto`) | **partial unique `bank_transaction_id WHERE bank_transaction_id IS NOT NULL AND voided_at_utc IS NULL`**; **partial unique `bank_transaction_id WHERE source = 'Auto'`** (tính cả biên lai đã huỷ) |
| `payment_audit_events` | center_id, actor, action, target, before, after, at | chỉ INSERT |

Partial unique thứ nhất cho phép huỷ rồi gán lại, nhưng một giao dịch **chỉ có tối đa một biên lai còn hiệu lực** ở mức DB. Partial unique thứ hai khiến giao dịch **chỉ được tự ghi một lần trong đời**.

Một số TK chỉ được bật tự động ở một trung tâm, để cùng một khoản tiền không vào hai sổ: partial unique `(bank_bin, account_number) WHERE mode = 'Auto'` trên `payment_provider_configs`.

`semantic_key` = `bank_code|account_number|reference_code` sau khi chuẩn hoá (viết hoa, bỏ khoảng trắng); null khi `reference_code` rỗng. Dùng `bank_code` của SePay chứ không dùng `bank_bin`, vì giao dịch của TK lạ không có `bank_bin`.

## 5. API (prefix `/api/v1`)

| Method & route | Quyền | Ghi chú |
|---|---|---|
| `POST centers/{c}/students/{id}/payment-intents` | Chủ, QL, NV | `{chargeIds?}`, trả `{code, amount, transferContent, qrPayload, bank, chargeIds, expiresAtUtc}` |
| `POST me/tuition/students/{id}/payment-intents` | Học sinh | Như trên, chỉ hồ sơ của mình |
| `POST payments/webhooks/sepay/{endpointKey}` | Anonymous + HMAC | Rate limit riêng, body tối đa 64 KB, đọc raw body. Trả 200 `{"success": true}`; 401 khi sai chữ ký hoặc timestamp; 404 body rỗng khi không có `endpointKey`; 500 khi không ghi được |
| `GET centers/{c}/bank-transactions?status=` | Chủ, QL, NV | Hàng chờ, kèm gợi ý học sinh |
| `POST centers/{c}/bank-transactions/{id}/assign` | Chủ, QL, NV | `{studentId, chargeIds?, expectedVersion}` |
| `POST centers/{c}/bank-transactions/{id}/ignore` | Chủ, QL | `{reason, expectedVersion}` |
| `POST centers/{c}/bank-transactions/{id}/retry` | Chủ | Chỉ cho `Failed` |
| `POST centers/{c}/bank-transactions/{id}/resolve-held` | Chủ | `{isDuplicate, reason, expectedVersion}`, chỉ cho `Held` |
| `POST centers/{c}/tuition/payment-provider/pull` | Chủ, QL | Kéo ngay, không chờ chu kỳ |
| `GET/PUT centers/{c}/tuition/payment-provider` | Xem: Chủ, QL · Sửa: Chủ | Không bao giờ trả secret về client. Ở Đợt 2 chỉ nhận `mode` là `Off` hoặc `Shadow`; `Auto` bị từ chối đến khi có bước ghi sổ (Đợt 3) |
| `GET centers/{c}/tuition/reconciliation?from&to` | Chủ, QL | Tổng tiền vào, tổng biên lai, danh sách lệch |

`endpointKey` chỉ để định tuyến, có thể xuất hiện trong log; **secret là khoá HMAC**.

## 6. Kỹ thuật áp dụng

### Chính xác

| # | Rủi ro | Kỹ thuật |
|---|---|---|
| K1 | Gõ sai tiền, sai nội dung | QR chuẩn EMVCo do server sinh: tag 54 số tiền, tag 62.08 nội dung, CRC16 |
| K2 | Mã bị gõ sai hoặc biến dạng | Base32 bỏ ký tự dễ nhầm, có ký tự kiểm tra, chuẩn hoá trước khi tìm, bộ test mẫu nội dung thật |
| K3 | Webhook giả | Không ghi được tiền (T7). HMAC constant-time và timestamp chỉ để chặn spam chuông |
| K4 | Secret webhook bị lộ | Như K3. Tỉ lệ chuông mà API không có gì mới tăng bất thường → email Chủ đổi secret |
| K5 | Một giao dịch thấy nhiều lần | Một khoá unique `(provider, provider_txn_id)` cho mọi đường |
| K6 | Webhook mất | Kéo định kỳ 5 phút + quét 3 ngày mỗi đêm, `since_id` kèm khoảng chồng lấn |
| K7 | Ghi hai biên lai | Hai partial unique ở `receipts`: một biên lai còn hiệu lực mỗi giao dịch, và một biên lai `Auto` trong suốt đời giao dịch |
| K8 | Ghi dở dang | Biên lai, phân bổ, trạng thái, email outbox trong **một transaction** |
| K9 | Hai luồng đè nhau | Khoá trung tâm sẵn có, tái dùng `BeginCenterGuardAsync` |
| K10 | Ghi tay trùng tự động | Chặn `BankTransfer` thủ công khi `mode = Auto`; không chặn ở `Shadow` |
| K11 | Ghi nhầm học sinh | Không đoán; gợi ý chỉ để chọn; mã học sinh phải khớp duy nhất; gán tay có audit |
| K12 | Lệch tiền | Luôn ghi số thực nhận, phân bổ theo `CenterLedger.Spread` |
| K13 | Hồ sơ đã gộp | `Resolve` trước khi ghi |
| K14 | Quét lại QR cũ | Intent đã trả vẫn ghi nhận, phần dư thành số dư và báo cho nhân viên |
| K15 | Đổi TK để chiếm tiền | D7 + audit; intent chụp sẵn TK; QR luôn hiện tên chủ TK |
| K16 | Huỷ biên lai làm "mất" tiền | Hỏi gán lại khi huỷ, không thì về hàng chờ (`VoidedReceipt`); tổng đối soát vẫn khớp |
| K17 | Lệch âm thầm | Bất biến: Σ tiền vào = Σ biên lai Auto còn hiệu lực + Σ chờ xử lý + Σ lỗi + Σ bỏ qua |
| K18 | Phát hành sai logic khớp | **Shadow mode**: chạy khớp trên giao dịch thật, chỉ ghi kết quả dự kiến, chưa tạo biên lai |
| K19 | SePay đổi dữ liệu của giao dịch đã ghi | `payload_checksum` trên trường đã chuẩn hoá; giữ bản đã ghi, email Chủ |
| K20 | `id` API không ổn định, hoặc SePay sinh hai bản ghi cho cùng một giao dịch ngân hàng | **Điều kiện qua Đợt 2**: kéo cùng giao dịch nhiều lần, `id` phải giữ nguyên. Khi chạy thật: khoá ngữ nghĩa (T8) chặn ở DB, bản thứ hai thành `Held`, email Chủ. Khi `referenceCode` rỗng thì chỉ còn khoá chính |
| K21 | Bật `Auto` làm ghi trùng tiền nhân viên đã ghi tay thời `Shadow` | Lưu `auto_since_utc` khi bật; giao dịch có thời điểm trước mốc này không bao giờ tự ghi |
| K22 | Cập nhật lặp hay đến muộn đè trạng thái | Cập nhật có điều kiện theo trạng thái và lease |

### Performance và thời gian phản hồi

Ước tính, **đo lại ở Đợt 2**:

| Chặng | Thời gian | Ghi chú |
|---|---|---|
| Webhook trả lời SePay | vài mili giây | Một INSERT nhật ký + một UPDATE cờ |
| Worker thấy chuông | ≤ 1 giây | Poll 1 giây trên partial index cấu hình cần kéo, truy vấn rất nhẹ |
| Gọi API v2 | vài trăm mili giây (chưa đo) | Một lần gọi lấy được nhiều giao dịch |
| API chưa thấy giao dịch | +1, 3, 10, 30 giây | Chỉ khi SePay trễ |
| Ghi sổ | ≤ 1 giây sau khi kéo | Poll 1 giây; nạp sổ một lần cho cả lô |
| **Từ lúc chuyển tiền tới lúc có biên lai** | **thường 2–5 giây** | Gần như bằng thiết kế cũ có xác minh |
| Webhook mất hẳn | ≤ 5 phút | Kéo định kỳ (D9), thay vì chờ đối soát vài giờ |
| API SePay sập | Tiền chờ, không mất | Ghi khi API hoạt động lại; email Chủ sau 1 giờ |

| # | Kỹ thuật | Tác dụng |
|---|---|---|
| P1 | Webhook chỉ ghi nhật ký + cờ | Trả lời trong vài mili giây, không dính khoá sổ, không gọi mạng |
| P2 | Gom chuông theo cấu hình | Đầu kỳ 100 chuông dồn lại vẫn chỉ vài lần gọi API |
| P3 | Kéo theo `since_id`, nhiều bản ghi mỗi lần | 3 request/giây × `per_page` dư sức cho một trung tâm; kéo định kỳ chỉ tốn 1 request/5 phút/trung tâm |
| P4 | Token bucket theo token | Không bị SePay chặn vì vượt giới hạn |
| P5 | Xử lý theo lô từng trung tâm | Nhiều giao dịch vẫn chỉ nạp sổ một lần |
| P6 | Giới hạn kích thước lô | Không giữ khoá lâu, không chặn nhân viên đang thao tác |
| P7 | Gọi SePay ngoài transaction, có timeout | SePay chậm không kéo cả trung tâm |
| P8 | Partial index trên trạng thái chờ và cờ cần kéo | Quét nhanh, không phụ thuộc lịch sử |
| P9 | Backoff + giới hạn số lần | Lỗi không quay vòng tốn tài nguyên |
| P10 | Cô lập dòng lỗi | Lô lỗi thì rollback, chạy lại từng giao dịch để tìm dòng hỏng, lô còn lại đi tiếp |
| P11 | Worker riêng | Thanh toán, email, nhắc nợ không chặn nhau |

**Chưa cần:** RabbitMQ/Kafka, LISTEN/NOTIFY, chia khoá theo học sinh, partition. Chỉ làm khi đo thấy chậm.

## 7. Các đợt

### Đợt 0 — Chuẩn bị (xong)
- Tài khoản test SePay, nhận webhook thử, so payload webhook và API v2 (kết quả ở mục 2).
- Còn lại: mẫu `content` thật từ các ngân hàng có khách, quét QR bằng tài khoản thật.

### Đợt 1 — QR động (xong)
- `payment_intents`, sinh mã, bộ dựng chuỗi VietQR, hai endpoint tạo QR, frontend hiện QR và tên chủ TK.
- Tái dùng intent còn hiệu lực nếu cùng khoản và cùng số tiền; số nợ đổi thì tạo mới.

### Đợt 2 — Kết nối và nhận giao dịch

Chia thành các PR nhỏ, theo thứ tự:

| Bước | Nội dung |
|---|---|
| **2a** | **Đo trên sandbox trước** các điểm ở cuối mục 2. Điểm 1 không đạt thì dừng |
| **2b** | Domain + migration: `payment_provider_configs`, `payment_webhook_deliveries`, `bank_transactions` (kể cả cột `shadow_*`), `payment_audit_events`. Dịch vụ mã hoá AES-256-GCM có `key_id`, dùng chung API và Worker. Contract `ListActiveUserIdsAsync` để lấy Chủ |
| **2c** | `GET/PUT payment-provider`: chỉ Chủ sửa, nhập lại mật khẩu (D7), email cảnh báo, audit, không trả secret. **Chỉ cho `Off`/`Shadow`** |
| **2d** | Webhook: raw body, HMAC constant-time, cửa sổ timestamp, 401/404/500, body cố định tránh 406, rate limit, đánh dấu cần kéo |
| **2e** | `IBankFeedProvider` + SePay v2 client, `PaymentFetcher` trong `PaymentWorker` riêng: nhịp kéo lưu DB, lease, hai lớp khoá, checksum, `Held`, kéo định kỳ, endpoint `pull` |
| **2f** | Test: unit (HMAC, checksum, giờ VN → UTC, `semantic_key`), Postgres thật (kéo lặp, hai worker song song, lease hết hạn, Worker khởi động lại vẫn giữ nhịp kéo, `Held`), SePay giả lập trả trùng/429/500, snapshot OpenAPI |

**Xong khi:** giao dịch sandbox nằm trong `bank_transactions` vài giây sau khi chuyển; gửi lại webhook không tạo dòng mới; chữ ký sai bị từ chối và không tạo dòng nhật ký; không bật được `Auto`; tắt webhook thì kéo định kỳ vẫn lấy đủ; webhook giả không tạo dòng nào; **`id` API giữ nguyên qua nhiều lần kéo** (K20); worker chết giữa lúc kéo thì lần sau không mất, không trùng. **Chưa ghi tiền.**

### Đợt 3 — Khớp và ghi biên lai
- Parser, bộ khớp, tách phần ghi sổ dùng chung, tài khoản hệ thống, `PaymentBooker`, thay đổi ở `receipts`.
- Chạy `Shadow` trên giao dịch thật và so với biên lai nhân viên ghi tay.
- Chỉ chuyển trung tâm sang `Auto` khi kết quả dự kiến khớp hoàn toàn.

**Xong khi:** test song song và kéo lặp không tạo trùng, bất biến K17 đúng, Shadow không có khớp sai.

**3a (xong 10/11/2026):** `PaymentCode.FindIn`, `PaymentMatcher`, `PaymentBooker` ghi `shadow_*` cho trung tâm Shadow, chạy trong `PaymentWorker`. Thử trên sandbox: QR `HPD1KMBCXR4` → chuyển khoản → kéo về → `Book (PaymentCode HPD1KMBCXR4)` đúng học sinh. Lưu ý:

- Shadow đánh giá mỗi giao dịch **một lần**. Đổi tài khoản nhận hay tạo mã QR sau đó không làm đánh giá lại. Bước ghi thật (3b) phải khớp lại ngay lúc ghi, không dùng lại `shadow_*`.
**3b (xong 10/11/2026):**

- `receipts` có `bank_transaction_id`, `source` (`Manual`/`Auto`) và hai partial unique: một biên lai còn hiệu lực mỗi giao dịch, một biên lai `Auto` trong đời giao dịch (migration `PaymentBooking`). `payment_intents` có trạng thái `Paid`.
- `PaymentBooker.RunAutoOnceAsync`: mỗi trung tâm trong khoá sổ, nạp sổ một lần, khớp lại ngay lúc ghi, tạo biên lai đứng tên `SystemActors.PaymentFeed` ("Tự động (SePay)"), phân bổ ưu tiên các khoản của mã QR, đánh dấu QR `Paid`; lô lỗi thì thử lại từng dòng (P10).
- **Chốt:** giao dịch trước `auto_since_utc` → `Ignored` (`BeforeAuto`), không vào hàng chờ, vẫn có trong đối soát.
- Huỷ biên lai tự động → giao dịch về `NeedsReview` (`VoidedReceipt`); phát lại không ghi lại được (unique `Auto`).
- `Auto` chỉ bật được từ `Shadow`; khi `Auto`, ghi tay biên lai chuyển khoản bị từ chối (`tuition_bank_transfer_automatic`), tiền mặt vẫn ghi được.
- `GET .../payment-provider/shadow-report?from&to` (Chủ, QL): mỗi giao dịch kèm biên lai ghi tay tương ứng và kết luận `Match` / `DifferentStudent` / `MissingManual` / `ManualOnly` / `BothSkipped`, cùng danh sách biên lai chuyển khoản không có giao dịch; `agrees` cho biết đã nên bật `Auto` chưa.
- Test Postgres thật: ghi đúng một lần kể cả hai Worker song song, phân bổ vào khoản của QR, bất biến K17, huỷ rồi phát lại.
- **Thử Auto thật trên sandbox (10/11/2026):** Chủ bật `Auto` qua API (Quản lý bị 403); webhook không tới được vì tunnel đã tắt nhưng lượt kéo vẫn lấy về giao dịch. Chuyển khoản `HPA2AK314C0 HOC PHI` 150.000đ → `Booked`, biên lai `PT-000001` nguồn `Auto` (học sinh chưa có khoản nợ nên thành số dư), QR chuyển `Paid`; 7 giao dịch thời Shadow → `Ignored` (`BeforeAuto`). **Frontend chưa có:** trường `source`, chế độ `Auto`, mã lỗi mới và màn báo cáo Shadow (Đợt 4).

### Đợt 4 — Hàng chờ và siết luồng tay
- Màn "Tiền chưa rõ của ai" với gợi ý, gán, bỏ qua; huỷ biên lai hỏi gán lại.
- Chặn ghi tay chuyển khoản khi `Auto`.
- Email xác nhận cho phụ huynh qua outbox.

**Xong khi:** mọi giao dịch đều có đường xử lý, không còn cách ghi trùng.

### Đợt 5 — Đối soát và vận hành
- Quét 3 ngày mỗi đêm và báo cáo đối soát, so bốn lớp: SePay ↔ `bank_transactions` ↔ biên lai ↔ phân bổ. Phát hiện: SePay có mà mình thiếu; webhook ký đúng mà API không có giao dịch tương ứng; `Booked` mà không có biên lai còn hiệu lực; biên lai `Auto` không có giao dịch; tổng K17 lệch.
- Bộ chỉ số: chuông theo giờ, tỉ lệ chuông gửi lại (`delivery_count > 1`), chuông sai chữ ký, chuông mà API không có gì mới, giao dịch thấy lại (`seen_count > 1`), số dòng theo trạng thái, số cập nhật có điều kiện bị từ chối, số lần đụng unique ở `receipts`, số lần thử lại thủ công, tuổi dòng `Received` cũ nhất, độ trễ chuyển tiền → biên lai.
- Cảnh báo D10, thêm: dòng `Received` cũ hơn 10 phút; tiền vào tăng mà biên lai không tăng; đụng unique ở `receipts` (lẽ ra không bao giờ xảy ra); trường lạ từ SePay.
- Tài liệu hướng dẫn trung tâm tự kết nối.
- Mở rộng dần từng trung tâm.

**Xong khi:** báo cáo tổng khớp trên dữ liệu thật nhiều ngày, cảnh báo bắn đúng khi giả lập lỗi.

### Kiểm thử xuyên suốt
- Unit: chuỗi VietQR và CRC, mã kiểm tra, parser, bộ khớp, gom chuông, token bucket.
- Tích hợp Postgres thật theo mẫu `RealPostgresTuitionTests.cs`: kéo lặp, kéo song song hai worker, huỷ rồi gán lại, hồ sơ gộp, lô có dòng hỏng, checksum đổi.
- SePay giả lập trong test: trả trùng, trả trễ, trả lỗi 429/500.
- Bất biến sổ cái: Σ phân bổ + Σ số dư = Σ biên lai chưa huỷ.
- Contract test: cập nhật `tests/ContractTests/Snapshots/openapi.v1.json`.

## 8. Còn mở

| # | Nội dung | Ảnh hưởng |
|---|---|---|
| M1 | Frontend `D:\Project\QuanLyDayThemFE` (Next.js) đã dùng `payment-intents` ở cổng học sinh và màn trung tâm, QR vẽ tại chỗ bằng `qrcode-generator`. Màn Cài đặt vẫn dùng `img.vietqr.io` cho mã mẫu. QR chỉ tạo cho khoản còn nợ, nên đóng trước qua QR chưa có | Còn thiếu quét trên app ngân hàng thật bằng một tài khoản có thật |
| M2 | Các điểm đo ở cuối mục 2 | Có thể đổi nhịp thử lại, chu kỳ kéo, cách dùng `since_id` |
| M3 | Chuyển gộp: nội dung có 2 mã và số tiền **đúng bằng tổng** hai intent thì tự chia | Đề xuất, chưa chốt. Bớt việc cho nhân viên, cần sửa T6 |
| M4 | Đổi sang cổng thanh toán có mã đơn chung (vd. PayOS) | Không cần với thiết kế hiện tại; xem lại nếu API SePay không ổn định |
| M5 | ~~D7 cho `PUT tuition/settings`~~ **Đã làm (10/11/2026):** đổi tài khoản nhận học phí chỉ Chủ, hỏi lại mật khẩu, ghi `payment_audit_events`, email mọi Chủ; cấu hình SePay đi theo tài khoản mới (bỏ tài khoản thì SePay về `Off`). Quản lý vẫn sửa được hạn đóng và nhắc nợ | Frontend phải gửi `currentPassword` khi đổi tài khoản |

Nguồn: [Tích hợp webhook](https://developer.sepay.vn/vi/sepay-webhooks/tich-hop-webhook) · [Xác thực](https://developer.sepay.vn/vi/sepay-webhooks/xac-thuc) · [Xử lý lỗi](https://developer.sepay.vn/vi/sepay-webhooks/xu-ly-loi) · [API giao dịch](https://docs.sepay.vn/api-giao-dich.html) · [Bảng giá](https://sepay.vn/bang-gia.html)
