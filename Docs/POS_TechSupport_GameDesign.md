# POS Tech Support — Game Design Document

> Tài liệu này dùng để giao cho **Claude Code** dựng dự án Unity. Đọc toàn bộ trước khi code — kể cả 3 file tách riêng: **[schema.md](schema.md)** (SO schema + runtime class), **[app.md](app.md)** (app trên virtual desktop + dependency graph), **[manager.md](manager.md)** (schema + cơ chế + cách dùng của từng manager). Build **theo thứ tự Milestone ở Mục 12** — không nhảy cóc.

> **NGÔN NGỮ:** Toàn bộ game bằng **tiếng Anh** — UI, nội dung, và đặc biệt là hội thoại player ↔ customer AI (chat + voice) đều bằng tiếng Anh. Mọi văn bản do người chơi thấy (`symptom.layman`, `clueText`, `resultText`, câu customer, knowledge base, mail...) phải viết bằng tiếng Anh. Model LLM chọn ở Mục 13 phải mạnh về **hội thoại tiếng Anh**.

---

## 1. Tóm tắt game

Player đóng vai **nhân viên technical support** cho hệ thống POS (Point of Sale). Mỗi màn chơi là một **ca đêm** (20h → 4h sáng). Customer gọi/nhắn tới báo lỗi, player phải:

1. Nhận call → hỏi & **verify** đúng store/máy.
2. **Remote** vào desktop ảo của customer.
3. **Chẩn đoán** lỗi (lần theo phụ thuộc giữa các thành phần).
4. **Fix** lỗi (đúng thứ tự, tránh làm hỏng thêm).
5. Xác nhận với customer → đóng ticket.

**Cảm hứng:** Home Safety Hotline (tra cứu, suy luận, ra quyết định) + mô phỏng desktop để thao tác trực tiếp.

**Engine:** Unity. **AI customer:** một LLM nhỏ (xem Mục 13) + luật game cứng.

**NGÔN NGỮ:** Toàn bộ game bằng **tiếng Anh** — UI, nội dung, và **mọi hội thoại player ↔ AI đều bằng tiếng Anh**. Tất cả text trong SO (`symptom.layman`, `clueText`, `laymanVocabulary`, `misnaming`, system prompt, template câu trả lời...) viết bằng tiếng Anh. Không hỗ trợ đa ngôn ngữ ở bản đầu (có thể thêm localization sau, nhưng không nằm trong phạm vi hiện tại).

### Win / Lose
- **WIN:** hoàn thành **60 ngày thử việc** (60 màn) và xử lý **≥ 150 ticket** đạt ngưỡng chất lượng → được nhận chính thức.
- **LOSE:** số **warning tích lũy** vượt ngưỡng (bị đuổi việc). *3 mail phàn nàn = fail 1 đêm; fail đêm cộng vào warning; fail nhiều đêm mới game over.*
- Trung bình ~2.5 ticket/đêm. Đêm đầu thưa & dễ, đêm cuối dồn & khó.

---

## 2. Nguyên tắc thiết kế BẤT BIẾN (không được vi phạm khi code)

1. **IssueSO là nguồn sự thật duy nhất** về một lỗi. Mọi hệ thống khác đọc từ đó.
2. **Customer AI KHÔNG BAO GIỜ biết root cause.** Nó chỉ được cấp *triệu chứng dân dã* + thông tin định danh. Fault/Resolution/technical symptom **không nằm trong context của AI**.
3. **Lỗi = state sai trong module**, KHÔNG phải cờ boolean "đúng/sai". Fix = đưa state về đúng.
4. **Các thành phần LINK với nhau** (dependency graph). Lỗi lan xuôi (triệu chứng hiện ở downstream), fix đi ngược (sửa upstream trước).
5. **Customer là người NON-TECHNICAL.** Họ mô tả sai, gọi nhầm tên thiết bị, quy sai nguyên nhân. Player phải tự verify, không tin nguyên văn.
6. **ScriptableObject chỉ chứa dữ liệu tĩnh.** State runtime nằm trong class thường (clone từ SO). Không ghi state runtime vào asset SO.
7. **DialoguePolicy là "não", LLM chỉ là "miệng".** LLM diễn đạt cái policy đã quyết, không tự quyết nói gì về lỗi.

---

## 3. Kiến trúc 4 lớp

```
LỚP DATA (viết sẵn, ScriptableObject)
  IssueSO, StoreProfileSO, PersonaProfileSO,
  DesktopActionSO, KnowledgeArticleSO, ReceiptTemplateSO, GameConfigSO
        │
LỚP SIMULATION (state runtime)
  VirtualDesktopInstance = tập Module có state
  Modules: OS, POSSoftware, Terminal, Printer, CashDrawer, Network, Database, Browser
  DependencyGraph (các module link nhau, EffectiveStatus())
        │
LỚP LOGIC (luật chơi)
  ProblemGenerator, ResolutionChecker, FaultDependency,
  TransactionState (batch + history)
        │
LỚP AI (customer)
  CustomerAgent (GroundTruth), DialoguePolicy (trick, tiết lộ có kiểm soát),
  LLM (chỉ hiểu input + diễn đạt), GroundingGuard (chốt chặn output)
```

**Ranh giới code-level quan trọng:** DialoguePolicy nhận một **view rút gọn** của Issue (chỉ `symptom.layman` + thông tin định danh), KHÔNG nhận cả `IssueSO`. Đây là cách bảo đảm AI không có đáp án để lộ.

---

## 4. Hệ sinh thái POS (mô hình thật — dùng để tạo problem)

> **Đã tách sang [app.md](app.md)** — danh sách app trên virtual desktop, sơ đồ component & LINK (Network → POS Software (HUB) → Terminal/Database/Printer → Cash Drawer), POS Software (HUB), Terminal, Caller Authorization, Transaction data model, Printer receipt types, Terminal network identity (P6 vs P7).

---

## 5. ScriptableObject Schema

> **Đã tách sang [schema.md](schema.md)** — toàn bộ SO schema (`IssueSO`, `StoreProfileSO`, `PersonaProfileSO`, `DesktopActionSO`, `KnowledgeArticleSO`, `ReceiptTemplateSO`, `GameConfigSO`) + danh sách enum, kèm sơ đồ dependency data-level giữa các class.

---

## 6. Runtime classes (KHÔNG phải SO)

> **Đã tách sang [schema.md](schema.md)** — `VirtualDesktopInstance`, `ProblemInstance`, `ActiveFault`, `VerificationState`, `TransactionState`, `TicketState` (+ sub-class: `CallerInfo`, `CrmLookupState`, `CompareState`, `AuthorizationState`, `RemoteConnectState`).

---

## 7. Dependency & Fault chain

> **Đã tách sang [app.md](app.md)** ("App dependency graph") — thứ tự cascade `Network → POSSoftware → Terminal/Printer → CashDrawer`, quy tắc Latent/Active, phân biệt **Blocked** (lỗi upstream, ẩn clue) vs **Error** (lỗi cục bộ, luôn phải chẩn đoán được).

---

## 8. ResolutionChecker

```csharp
ResolveStatus EvaluateIssue(desktop, activeFault):
  if status == Latent: return Hidden
  statesOk = rootCauseFixed.All(check.Evaluate(desktop))
  testOk   = !requiresTestPass || RunTest(desktop, testReceiptType)
  if statesOk && testOk: return Resolved
  if HasUnexpectedFault(desktop, issue): return MadeWorse   // → HarmEvent
  return Unresolved

TicketStatus EvaluateTicket(desktop, faults):
  if any Latent: return InProgress          // còn lỗi bị che, chưa tới lúc chấm toàn bộ
  if all Resolved: return Resolved
  if any MadeWorse: return Degraded         // → HarmEvent → Mailbox
  return InProgress
```

**Fix tạm vs gốc:** nếu `symptomCleared` đạt nhưng `rootCauseFixed` chưa → ticket đóng được, customer hài lòng, NHƯNG đánh dấu **recurring** → tái phát muộn (trong đêm hoặc đêm sau).

---

## 9. Customer AI (chi tiết — xem Mục 13 cho model)

### Kiến trúc lai (tầng + fallback)
```
Player utterance (text; voice = GĐ2 qua STT)
   │
1. LLM/NLU: intent classification + slot extraction → PlayerIntent
   │
2. DialoguePolicy (LUẬT): quyết NÊN tiết lộ gì lúc này
      - đọc GroundTruth (chỉ symptom.layman + định danh) + persona + DialogueState
      - KnowledgeBoundary: chặn intent hỏi kỹ thuật/root cause
      - đây là nơi cơ chế "trick" sống
   │
3. Sinh câu:
      - mặc định: template/grammar có biến (an toàn, rẻ, đa dạng)
      - nâng cao: LLM diễn đạt lại nội dung policy đã chọn
   │
4. GroundingGuard: quét output
      - chứa từ kỹ thuật cấm? khẳng định ngoài GroundTruth? → chặn → fallback template
   │
Response → chat / (TTS voice) / SMS
```

### Ràng buộc NON-TECHNICAL (cưỡng chế 5 tầng)
1. **Data:** customer chỉ nhận `symptom.layman`, không nhận `technical`/fault/resolution.
2. **Persona:** `techLiteracy <= 0.7`. Mức cao nhất chỉ **mô tả hiện tượng chính xác hơn**, KHÔNG chẩn đoán nguyên nhân.
3. **Policy:** intent `ask_technical` → phản ứng bối rối, đẩy về player ("cái đó em không rành").
4. **Sinh câu:** chỉ dùng `laymanVocabulary` (tiếng Anh); áp `misnaming` (gọi nhầm tên) theo techLiteracy. Ví dụ misnaming: POS screen → "the till" / "the computer"; terminal → "the card machine"; receipt printer → "the printer thingy".
5. **Guard:** danh sách từ cấm (driver, firewall, spooler, service, config, port, DNS, batch settlement, permission, registry...) → chặn output vi phạm.

### Trick tự nhiên từ non-technical
- Gọi nhầm thiết bị (misnaming map): "the card machine won't work" khi thật ra là POS software.
- Quy sai nguyên nhân ("the internet is down" khi thật ra POS không connect terminal).
- Nhớ nhầm định danh (memoryAccuracy thấp) → player phải đối chiếu Registry.
- (Hiếm, tùy chọn) customer "tự nhận rành" nhưng chẩn đoán SAI → dẫn player đi lạc. Vẫn không phải đáp án thật.

### System prompt mẫu cho LLM (tiếng Anh — khi dùng Mục 13)
```
You are {persona.displayName}, the owner/staff of a small shop. You are NOT tech-savvy.
You only describe what YOU SEE: {symptom.layman}.
You do NOT know the cause. Do NOT use technical words (driver, firewall, service, config...).
If asked about a technical cause -> say you don't really understand that and ask the
support agent to check it for you.
Personality: {persona}. Keep replies SHORT, natural, and in character.
Your shop details: {store identity}.
Always respond in English.
```
GroundTruth truyền vào prompt **chỉ gồm** symptom.layman + định danh + persona. Tuyệt đối KHÔNG truyền fault/resolution/technical.

---

## 10. Communication channels
```
CommunicationManager điều phối 4 kênh, cùng 1 CustomerAgent phía sau:
  - VoiceCall (đồng bộ, realtime) — GĐ2, STT/TTS
  - Chat (đồng bộ, text)
  - SMS (bất đồng bộ) — xin receipt; AI có thể gửi RECEIPT SAI (nhầm loại/cũ/máy khác)
  - Mailbox (một chiều) — mail phàn nàn + strike
```
**SMS receipt trick:** player xin receipt để đối chiếu; AI gửi lại theo persona (honesty/cooperativeness thấp → gửi sai). Player phải xác minh (timestamp vs thời điểm lỗi, mã máy/store, loại receipt, nhất quán với state). Dùng receipt sai để chẩn đoán → fix sai → HarmEvent.

---

## 11. Managers (danh sách class quản lý)

> **Đã tách sang [manager.md](manager.md)** — schema C# đầy đủ, cơ chế chạy, purpose, và cách các manager gọi nhau, cho cả 16 manager: `CampaignManager, ShiftManager, TicketManager, ProblemGenerator, VerificationManager, DesktopManager, ActionManager, ResolutionChecker, TransactionManager, DialogueManager, CommunicationManager, MailboxManager, ConsequenceManager, ScoreManager, KnowledgeBaseManager, SaveManager`.

---

## 12. Thứ tự BUILD (Milestone — Claude Code làm theo đúng thứ tự)

**M1 — Data & Simulation core**
- Định nghĩa tất cả enum, SO schema (Mục 5), runtime class (Mục 6).
- Module state (bắt đầu: Printer, OS, Network, POSSoftware) + DependencyGraph + EffectiveStatus.
- Viết 5 IssueSO mẫu (bộ Printer P1–P5 bên dưới) + 1 Store + 1 Persona.
- **Tiêu chí xong:** inject fault vào desktop, in ra state đúng qua log.

**M2 — Một ticket chơi được (CHƯA AI)**
- DesktopActionSO + ActionManager (diagnostic + fix, precondition).
- ResolutionChecker (gốc/tạm/MadeWorse).
- UI Remote Desktop tối thiểu (mở app, xem state, bấm action).
- Customer giả lập bằng **menu chọn câu hỏi** (placeholder cho DialoguePolicy).
- **Tiêu chí xong:** chơi trọn 1 ticket printer từ nhận → verify đơn giản → remote → fix → đóng.

**M3 — Một đêm chơi được**
- ShiftManager (đồng hồ + tempo), TicketManager (hàng đợi).
- ScoreManager, end-of-night screen.
- **Tiêu chí xong:** chơi hết 1 ca 8 phút với nhiều ticket.

**M4 — Customer AI** ✅ đã implement, namespace `POSTechSupport.AI` (chi tiết ở [manager.md](manager.md) DialogueManager)
- `GroundTruth` (ranh giới Mục 3) → `IntentClassifier` → `DialoguePolicy` (+KnowledgeBoundary) → `ILlmClient` → `GroundingGuard`.
- LLM là **tuỳ chọn**, mặc định tắt: `TemplateLlmClient` (Phương án A Mục 13). Bật `GameConfigSO.useLlm` để dùng `OllamaLlmClient` (Phương án B).
- Ô chat tự do đã thay menu M2; 8 nút quick-ask giữ lại nhưng đi vào CÙNG pipeline.
- **Tiêu chí xong:** chat tự nhiên, không lộ đáp án, giữ non-technical.

**M5 — Verify + Mailbox + SMS**
- VerificationManager + Store Registry UI.
- MailboxManager (strike, 3=fail đêm) nhận HarmEvent.
- SMS + receipt trick.

**M6 — Campaign (60 ngày)**
- ConsequenceManager (recurring, trust, backlog, warning, narrative).
- CampaignManager + SaveManager. Win/lose.

**M7 — Voice (tùy chọn, GĐ2)**
- STT/TTS cắm vào DialogueController (đã agnostic input).

**Nội dung song song:** viết dần ~35–40 base issue (3 nhóm Terminal/POS/Printer + blocker Windows + nhóm "mềm"), 3 mức khó. Đủ cho ≥150 ticket qua tổ hợp store × persona × dây chuyền.

---

## 13. RECOMMEND LLM (1 model nhỏ duy nhất)

Yêu cầu của game: model **nhỏ**, chạy được **on-device hoặc self-host rẻ**, đủ khả năng (a) phân loại intent + trích slot, (b) sinh 1–2 câu hội thoại ngắn tự nhiên giữ vai non-technical. **KHÔNG cần** suy luận phức tạp vì luật game do DialoguePolicy lo — LLM chỉ hiểu input + diễn đạt.

**Game bằng TIẾNG ANH** → ưu tiên model English-first nhỏ, có nhiều lựa chọn tốt hơn so với model đa ngữ.

### Khuyến nghị chính: **Llama 3.2 3B Instruct** (English-first, nhỏ, license thương mại rộng rãi)
Lý do:
- English-first, hội thoại ngắn tự nhiên rất tốt ở phân khúc 3B.
- GGUF Q4 chỉ ~2GB — chạy trên máy tầm trung.
- Bám system prompt tốt — quan trọng để giữ ràng buộc non-technical.
- Chạy dễ qua Ollama / llama.cpp, hoặc convert ONNX.

### Lựa chọn thay thế cùng phân khúc (chọn 1)
- **Phi-3.5-mini (~3.8B):** English rất mạnh, bám prompt tốt, hơi lớn hơn chút.
- **Qwen2.5-3B-Instruct:** English tốt + đa ngữ (chọn nếu sau này muốn thêm ngôn ngữ khác).
- **Gemma 2 2B:** nhẹ nhất, đủ cho câu hội thoại ngắn, nếu cần tối ưu bộ nhớ.

> Cả bốn đều đủ sức cho game này vì LLM chỉ hiểu input + diễn đạt 1–2 câu; luật do DialoguePolicy lo. Chọn theo: dung lượng máy đích + độ ưu tiên English thuần vs đa ngữ.

### Về Unity Sentis
- Sentis chạy model **ONNX on-device**. Phù hợp nhất cho phần **intent classification + slot extraction** (model rất nhỏ, ổn định).
- Sinh hội thoại tự do bằng LLM 3B qua Sentis thuần là **nặng và khó** trên máy người dùng phổ thông. Thực tế:
  - **Phương án A (khuyên):** Sentis lo NLU (intent/slot, model nhỏ). Sinh câu dùng **template/grammar** (không cần LLM) — an toàn, rẻ, đủ đa dạng cho game này.
  - **Phương án B:** nếu muốn câu tự nhiên hơn, chạy **Qwen2.5-3B qua Ollama/llama.cpp self-host** (local server), Unity gọi HTTP. Không phụ thuộc cloud, không tốn phí API.
  - **Phương án C (đơn giản nhất để prototype):** gọi một API cloud model nhỏ trong lúc phát triển, rồi thay bằng B khi ship.

### Kết luận 1 dòng
Dùng **Llama 3.2 3B Instruct (GGUF Q4) self-host qua Ollama** làm model duy nhất cho cả intent lẫn sinh câu tiếng Anh; giữ **template fallback** để không bao giờ phụ thuộc hoàn toàn vào model. Nếu ưu tiên on-device thuần Unity → dùng **Sentis cho intent nhỏ + template tiếng Anh cho sinh câu**, để LLM 3B là tùy chọn nâng cao.

> Lưu ý: tên và phiên bản model thay đổi nhanh. Trước khi chốt, kiểm tra bản mới nhất cùng phân khúc (~1.5–3B) và giấy phép sử dụng thương mại.

---

## 14. Bộ Issue mẫu (Printer P1–P5, Terminal P6–P7) — dùng cho M1

Cùng triệu chứng bề mặt gần giống nhau, nhưng fault/clue/resolution khác → dạy chẩn đoán phân biệt.

- **P1 Hết giấy** — fault `Printer.paperLevel=Empty`; clue: queue "out of paper"; resolution `paperLevel==OK` + test page. Basic.
- **P2 Driver hỏng** — fault `Printer.driverState=Corrupted`; clue: Device Manager "error 39", queue kẹt; resolution `driverState==OK` + test page; worsening: xóa nhầm printer → `connection=Removed`. Medium.
- **P3 Cash drawer chiếm cổng** — fault `CashDrawer.port==Printer.port (COM3)`; clue: port config trùng, driver OK (loại trừ P2); resolution: khác port + test page. Bẫy: dễ tưởng P2. Hard.
- **P4 Printer offline do mạng** — fault `Network.isOnline=false` (BLOCKER) + network printer; clue: ping fail, status offline; resolution: online + reconnect + test page. Vai trò blocker cho ticket dây chuyền. Hard-khi-ghép.
- **P5 Template receipt lỗi (POS, KHÔNG phải printer)** — fault `POS.receiptTemplate=Broken`; clue: **test page OK** nhưng customer copy thiếu field → gốc ở POS config; resolution `receiptTemplate==OK` + customer copy đúng. Dạy: không phải cứ liên quan in là lỗi printer. Hard.
- **P6 Terminal join nhầm Wi-Fi** — fault `Terminal.wifiNetwork` ≠ `Network.ssid` (vd bị join vào "SunriseDiner-Guest" thay vì "SunriseDiner-Main"); vì mỗi mạng có dải IP/gateway riêng (DHCP), join nhầm mạng kéo theo IP/gateway đổi sang hẳn dải khác; clue: mở Terminal ▸ Network thấy SSID hiện tại, đối chiếu với Network Settings ▸ Connection Details thấy SSID đúng của cửa hàng; resolution: chọn lại đúng Wi-Fi từ danh sách mạng xung quanh trong Terminal ▸ Network (IP/gateway tự cập nhật theo). Không cần test page (không liên quan printer). Dạy: "không kết nối được" không phải lúc nào cũng là do POS/Network chết hẳn — có thể do đúng thiết bị join sai mạng. Medium.
- **P7 POS lưu IP đăng ký cũ (stale) cho terminal** — fault ở **phía POS**: `POSSoftware.registeredTerminalIp` ≠ IP thật hiện tại của terminal (dù terminal vẫn đang join ĐÚNG Wi-Fi — vd router reboot cấp lại IP qua DHCP nhưng chưa ai cập nhật lại POS); clue: Terminal ▸ Network hiện IP thật hiện tại, đối chiếu POS Manager ▸ Connections thấy IP khác (cũ) đang được đăng ký; resolution: đăng ký lại đúng IP ở POS Manager ▸ Connections (có sẵn IP thật của terminal để đối chiếu/copy, không phải đoán mò). Dạy: terminal có thể hoàn toàn khỏe (đúng wifi, IP tự cấp đúng) mà vẫn bị từ chối vì hồ sơ phía POS chưa cập nhật — lỗi nằm ở bên nhận diện, không phải bên kết nối. Medium.

**Ghi chú:** P6/P7 dùng chung state mới ở module Terminal/POSSoftware (xem [app.md](app.md) "Terminal — network identity"). Chúng KHÔNG che (`blockedByIssueIds`) các issue Printer P1–P5, vì Terminal không nằm trên đường phụ thuộc của Printer/CashDrawer (xem sơ đồ component & LINK trong app.md) — nhưng NGƯỢC LẠI, P6/P7 vẫn bị P4 (Network chết) che theo đúng chain, vì Terminal phụ thuộc POS Software phụ thuộc Network.

### Nhóm "mềm" — Business/permission (P8–P12)

Khác hẳn P1–P7: fault nằm ở **hồ sơ/cấu hình phía POS**, không phải thiết bị. POS vẫn `OK`, Terminal vẫn `OK`,
chỉ MỘT người không login được — đúng "Layer riêng, KHÔNG nằm trong cascade" ở [app.md](app.md) Mục 7. Vì thế
nhóm này **không có Fix action riêng**: chẩn đoán bằng diagnostic (`check_staff_account` /
`check_pos_connections`), còn sửa bằng các nút inline trên sub-tab POS Manager ▸ Staff Mgmt / Connections.

- **P8 Staff chưa được cấp role** — fault `POSSoftware.staffRole = None`. Chính là ví dụ Mục 15. Clue: role
  trống trong Staff Management, trong khi terminal vẫn charge thẻ bình thường (loại trừ hardware).
  Resolution `staffRole == "Sale"`. **worsening: `staffRole = Admin`** — cấp Admin cũng hết triệu chứng
  nhưng thừa quyền refund/void/close batch → `MadeWorse` → Degraded. Medium.
- **P9 Chưa assign vào terminal nào** — fault `POSSoftware.staffTerminal = ""`. Có role nhưng không gắn máy
  nào → bị từ chối ở mọi register. Dạy: role và assignment là 2 tầng khác nhau. Medium.
- **P10 Assign nhầm register** — fault `staffTerminal = "REG-4"` trong khi `Terminal.machineId = "REG-1"`.
  Khác P9 ở chỗ hồ sơ KHÔNG trống, chỉ trỏ sai máy. Đối chiếu với ID mà terminal tự khai, không tin lời
  customer (họ nhầm till suốt). Kèm red herring: cảnh báo sắp hết giấy trên cùng màn hình. Medium.
- **P11 Đổi rồi nhưng chưa sync** — fault `terminalSynced = false`. Role đúng, assignment đúng, vẫn fail —
  vì terminal còn giữ permission cũ. Dạy: đừng sửa lại thứ vốn đã đúng. Hard.
- **P12 Sai host database** — fault `POSSoftware.dbHost = "db.sunrise-diner.local"` (thừa 1 dấu gạch).
  Tra cứu/reprint receipt fail trong khi in vẫn tốt. Dạy: test page KHÔNG cần dữ liệu giao dịch nên nó pass
  ngay cả khi kho bản ghi không với tới được. Hard.

`P4` (Network chết) che được cả nhóm này theo chain, nên pool 46+ có thêm combo `P4+P8`, `P4+P12`.

### Nhóm Windows/OS (P13–P16)

Module `OS` là gốc thật sự của cascade (xem [app.md](app.md) Mục 7). Nhóm này dạy đúng một bài: **root cause
có thể nằm THẤP HƠN chỗ triệu chứng nổi lên một tầng.** Hai loại, khác nhau về bản chất:

**Blocker (machine-wide)** — `OsBlocking()` true, cả chuỗi dưới thành `Blocked`:
- **P14 Đầy ổ đĩa** — `OS.diskSpace = Full`. Không app nào chạy nổi. Vai trò blocker thứ 2 bên cạnh `P4`.
  Sau khi dọn xong phải đọc lại mọi app: lúc `Blocked` chúng chưa hề được chẩn đoán, thường có fault thật
  nằm dưới. Hard.
- **P15 Chờ restart sau update** — `OS.pendingReboot = true`. Fix là `restart_pc`, đánh dấu **risky** không
  phải vì làm hỏng state mà vì nó rớt phiên remote và tắt register giữa ca — UI bắt confirm trước. Medium.

**Service-level (KHÔNG block)** — nổi lên thành `Error` ở module cần dịch vụ:
- **P13 Print Spooler dừng** — `OS.spoolerService = Stopped` → `Printer` báo Error. Clue: job kẹt ở
  "Spooling", giấy đủ, driver OK. Dạy phân biệt với P1/P2: reinstall driver KHÔNG khởi động lại được một
  service đã dừng. Medium.
- **P16 Lệch giờ hệ thống** — `OS.systemTime = Skewed` → `Terminal` báo Error, mọi giao dịch thẻ bị từ chối
  trong khi Wi-Fi đúng, IP đã đăng ký đúng (loại trừ P6 lẫn P7), tiền mặt vẫn chạy. Card auth đi qua TLS
  nên clock sai là handshake bị processor chối. Hard.

**`blockedByIssueIds` giờ được nối theo LUẬT, không khai tay từng issue** (`SampleContentBootstrap.WireBlockers`):
blocker OS (`P14`/`P15`) che mọi thứ kể cả `P4`; `P4` che mọi issue không phải blocker. Trước đây field này
luôn rỗng nên nhánh Latent→Active trong `DesktopManager.OnFixApplied` (Mục 7) chưa từng chạy.

### P17–P40 — đủ bộ 40 issue

**Nguyên tắc chọn nội dung:** mỗi issue phải tách được khỏi một issue player ĐÃ biết. Một fault sinh ra
triệu chứng người chơi đã học đọc rồi thì không phải content, chỉ là độn số lượng. Cột cuối ghi rõ nó dễ
bị nhầm với cái nào.

| # | Fault | Nhầm với | Bài học phân biệt |
|---|---|---|---|
| P17 | `Printer.paperJam = Jammed` | P1 hết giấy | Có giấy + tiếng kêu cơ khí = kẹt, không phải hết |
| P18 | `Printer.cableConnected = false` | P2 driver | Device Manager **không liệt kê** thiết bị — vắng mặt khác báo lỗi, không có gì để reinstall |
| P19 | `Printer.queuePaused = true` | P13 spooler | Paused = service chạy nhưng bị bảo dừng; Stopped = không có service nào |
| P20 | `Printer.defaultPrinter = OfficeInkjet` | P13/P19 | Giấy in ra ở máy khác — printer khoẻ, chỉ là không được gửi gì |
| P21 | `POSSoftware.printerVisible = false` | P20 | 2 lớp đăng ký riêng: Windows có printer ≠ POS có printer. Test page pass không chứng minh gì |
| P22 | `Printer.connection = Offline` | P2, P18 | "Use Printer Offline" là checkbox ai đó tick, không phải lỗi |
| P23 | `Printer.paperWidth = 58mm` | P5 template | Thiếu **field** = template; nội dung đủ nhưng bị cắt = sai khổ giấy |
| P24 | `CashDrawer.lockState = Locked` | P3 port | Nghe tiếng click = điện đã chạy xong, vật lý đang giữ |
| P25 | `CashDrawer.triggerMode = Manual` | P3, P24 | "Phải bấm tay" khác "không mở được" |
| P26 | `Terminal.pairingState = Unpaired` | P6, P7 | Tầng thứ 3 giữa terminal và POS: mạng đúng, IP đúng, nhưng mất pairing token |
| P27 | `Terminal.firmwareVersion = 3.1` | P26 | Hỏng ngay sau update → nghi lệch version trước tiên |
| P28 | `Terminal.emvConfig = Corrupt` | P6, P37 | Chip fail mà swipe được = **sai cấu hình**, không phải mất kết nối (mất kết nối chết cả hai) |
| P29 | `Terminal.mode = Training` | P32 | Duyệt hết mà không có tiền về — terminal giả lập trọn quy trình, in cả receipt |
| P30 | `POSSoftware.licenseState = Expired` | P38 | App **từ chối khởi động** chứ không crash ngẫu nhiên |
| P31 | `POSSoftware.offlineMode = true` | P4 | Mạng khoẻ mà POS tự ở offline — store-and-forward, nhân viên không thấy gì bất thường |
| P32 | `POSSoftware.batchState = SettleFailed` | P29 | Authorize và settle là 2 sự kiện khác nhau; giao dịch CÓ tồn tại, chỉ là chưa gửi đi |
| P33 | `POSSoftware.taxRate = 0` | P5 | Thiếu field = template; field có mà **số sai** = cấu hình |
| P34 | `POSSoftware.priceSync = Stale` | P33 | Kết nối sống chung hoà bình với price list cũ — hỏng cái job, không phải cái kết nối |
| P35 | `Network.signalStrength = Weak` | P4 | **Chập chờn là một chẩn đoán**: mạng chết thì chết mọi lúc, sóng yếu chỉ fail khi tải nặng |
| P36 | `Network.dnsServer = 8.8.8.8` | P12 | Cùng một câu báo lỗi: P12 gõ sai tên host, P36 tên đúng mà không ai phân giải nổi |
| P37 | `Network.firewallBlocking = true` | P16 | Cả hai đều "internet ổn mà thẻ chết". Xem clock trước — 1 giây và loại được nửa vấn đề |
| P38 | `OS.antivirusQuarantine = true` | P30 | Bắt đầu ngay sau cảnh báo bảo mật; POS không hỏng, một mảnh của nó bị lấy đi |
| P39 | `OS.userAccount = Standard` | P8 | **2 hệ quyền độc lập**: POS role quản "được làm gì trong till", Windows account quản "app có chạy được không" |
| P40 | `OS.powerPlan = Sleep` | P26, P35 | "Chỉ khi không ai đụng vào" là power setting; lúc bạn connect vào thì nó đã thức rồi |

**Mạng: DOWN khác DEGRADED.** Chỉ `isOnline == false` mới block cả chuỗi. P35/P36/P37 là "link vẫn sống,
sai đúng một thứ" — POS vẫn chạy và clue vẫn đọc được, vì giấu chúng sau `Blocked` là xoá mất đường lần.
Chi tiết ở [app.md](app.md) Mục 7.

**Fix bằng cách hướng dẫn customer.** `ask_customer_reseat_cable`, `ask_customer_clear_jam`,
`ask_customer_unlock_drawer`, `ask_customer_move_router` là action bình thường nhưng biểu diễn việc agent
hướng dẫn người ở hiện trường. Không phải thứ gì trên quầy POS cũng với tới được qua remote — giả vờ ngược
lại là dạy sai phản xạ.

**Tất cả 24 fix đều là `DesktopActionSO` thuần**, nên `RenderActionsForTab` tự vẽ ra, không cần thêm UI nào.

### Knowledge base — 7 bài phủ P1–P7 (xem [manager.md](manager.md) KnowledgeBaseManager)

Bộ article có **2 vai trò tách biệt**, đừng gộp:

1. **Onboarding (auto-attach).** Tier `day <= 5` của `poolForDay` có `P1`/`P17`/`P18` — cả 3 đều `Basic` và
   đều có bài riêng, nên 5 ngày đầu ticket nào cũng được gắn sẵn `TicketState.attachedArticle`. Từ ngày 6
   (khi `P2` mở khoá) auto-attach tắt hẳn.
2. **Tra cứu (day 6+).** Sau khi bánh xe tập tháo đi, player tự mở Knowledge Base mà tra theo category hoặc
   mã lỗi. Đây là lý do phải có **đủ 40 bài, 1 bài / 1 issue** — thiếu thì cơ chế fade-out tháo bánh xe rồi
   không còn gì đỡ.

Bảng dưới liệt kê 16 bài đầu; KB-017 → KB-040 khớp 1-1 với P17 → P40 (xem bảng P17–P40 ở trên).

| ID | Title | Category | `guidanceForIssueIds` | `relatedErrorCodes` |
|---|---|---|---|---|
| KB-001 | Receipt printer won't print — start here | Printer | `P1` | — |
| KB-002 | Printer reports Code 39 / device cannot start | Printer | `P2` | `39`, `Code 39` |
| KB-003 | Cash drawer stopped popping open | CashDrawer | `P3` | — |
| KB-004 | Everything at the front is dead | Network | `P4` | — |
| KB-005 | Test page prints fine but the customer's receipt is wrong | POS | `P5` | — |
| KB-006 | Register sits there and won't ring anything up | Terminal | `P6` | — |
| KB-007 | Register is healthy but POS still refuses it | POS | `P7` | — |
| KB-008 | A staff member can't log in (but everyone else can) | Business | `P8` | — |
| KB-009 | Login refused and the account has no terminal at all | Business | `P9` | — |
| KB-010 | Account is assigned — just not to this register | Business | `P10` | — |
| KB-011 | The account looks right and login still fails | Business | `P11` | — |
| KB-012 | Can't look up or re-send an old receipt | POS | `P12` | — |
| KB-013 | Jobs queue up and never print | **Printer** | `P13` | — |
| KB-014 | The whole machine is unusable | OS | `P14` | — |
| KB-015 | Machine is nagging about an update | OS | `P15` | — |
| KB-016 | Every card is declined but the terminal looks fine | **Terminal** | `P16` | — |

`KnowledgeArticleSO.category` là **nơi player sẽ tìm**, không phải tầng chứa fault. P13/P16 có fault ở `OS`
nhưng xếp dưới `Printer`/`Terminal` — người chơi tra theo triệu chứng nhìn thấy, và chính bài viết mới là
thứ chỉ cho họ rằng gốc nằm ở Windows.

**Mỗi bài phải dạy phần PHÂN BIỆT**, vì clue chỉ báo hiện tượng thô: KB-002 nói rõ port conflict trông y
như driver lỗi (trỏ sang KB-003) và cảnh báo "Remove & re-add" là nước đi dễ ăn Degraded; KB-005 dạy test
page không mang dữ liệu giao dịch nên "test page OK mà customer copy sai" là loại trừ printer; KB-007 tách
"POS chưa cập nhật hồ sơ" khỏi "staff không login được".

**Chỉ KB-002 có `relatedErrorCodes`** — "Code 39" là mã lỗi DUY NHẤT xuất hiện trong clue text của bộ issue
hiện tại. Bịa thêm mã cho các bài khác sẽ làm ô tra mã trả kết quả cho những mã game không bao giờ hiện.

Nội dung đầy đủ 7 bài: `SampleContentBootstrap.CreateKnowledgeArticles()`.

---

## 15. Ví dụ ticket đầy đủ (tham khảo khi làm M4/M5) — "Staff mới không login được terminal"

> Lời thoại minh họa viết bằng tiếng Anh (đúng ngôn ngữ game); phần diễn giải để tiếng Việt cho dễ đọc.

- **Customer báo (EN):** *"My new girl can't get into the register, I think the machine's broken."* (đổ cho terminal).
- **Root cause thật:** trong POS Staff Management, account staff mới có role trống + chưa assign vào terminal. Terminal hoàn toàn khỏe.
- **Chẩn đoán (lần theo link):** Terminal EffectiveStatus báo *"Login failed: permission denied by POS"* → loại trừ hardware; terminal vẫn charge khách cũ → loại trừ network; staff cũ login OK → lỗi cục bộ theo user; vào POS Staff Management → thấy role trống + chưa assign.
- **Fix (precondition chain):** gán role Sale → assign terminal → sync POS→terminal.
- **MadeWorse:** cấp role Admin "cho nhanh" → thừa quyền (refund/void/close batch) → HarmEvent.
- **Trick (EN):** customer nói mơ hồ *"I think we set her up already... one of the other kids did it."* → player phải tự verify trong Staff Management.
- **Red herring:** terminal có cảnh báo giấy sắp hết (printer) — không liên quan.

---

*Hết tài liệu. Build theo Milestone Mục 12. Mọi lúc, giữ 7 nguyên tắc bất biến ở Mục 2.*
