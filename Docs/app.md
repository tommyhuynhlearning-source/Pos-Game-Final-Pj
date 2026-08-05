# Desktop Apps — POS Tech Support

> Tách từ [POS_TechSupport_GameDesign.md](POS_TechSupport_GameDesign.md) Mục 4 (Hệ sinh thái POS) + Mục 7 (Dependency & Fault chain). Số Mục giữ nguyên như tài liệu gốc — code (`app.js`) và chính GDD tham chiếu `GDD Mục 4` v.v., đổi số ở đây sẽ làm sai hết tham chiếu đó.
>
> Data shape (SO/runtime class) xem [schema.md](schema.md) — file này chỉ nói **app nào tồn tại trên virtual desktop, module nào đứng sau nó, và app nào phụ thuộc app nào**.

---

## Danh sách app trên virtual desktop

Mỗi app là một cửa sổ player mở được khi remote vào máy customer. Nhiều app có thể trỏ vào **cùng một module** (2 cách nhìn khác nhau vào cùng 1 state) — đây là điểm hay bị hiểu lầm là "2 lỗi khác nhau" trong khi thực ra là 1 root cause.

| App (khoá nội bộ) | Tên hiển thị | Module đứng sau | Sub-tab (`appTab`) | Ghi chú |
|---|---|---|---|---|
| `system` | System (Windows) | `OS` | `health`, `services` | Nền của cả máy — disk/update/clock + Print Spooler |
| `possoftware` | POS Manager | `POSSoftware` | `receipt`, `connections`, `staff`, `database` | HUB — Staff Management, Connections (dbHost/registeredTerminalIp), receipt config |
| `printer` | Printer & Print Queue | `Printer` | `queue` | Print queue, paper level |
| `devicemanager` | Device Manager | `Printer` | `printer` | **Cùng module với `printer`** — driver/device state, khác view chứ không khác lỗi |
| `network` | Network Settings | `Network` | `adapter` | Ground truth SSID thật của cửa hàng (`Network.ssid`) |
| `cashdrawer` | Cash Drawer Config | `CashDrawer` | `port` | Port config |
| `terminal` | POS Terminal | `Terminal` | `status`, `batch` | Wi-Fi đang join (`Terminal.wifiNetwork`), charge/refund/void, batch |

> `DesktopActionSO` có **cả `appKey` lẫn `appTab`** — vì "action thuộc module nào" và "action nằm ở cửa sổ
> nào" là 2 chuyện khác nhau. Rõ nhất là `print_customer_copy`: `targetModule = Printer` nhưng
> `appKey = possoftware`, `appTab = database`, vì nó cần dữ liệu giao dịch thật. `appTab` rỗng = action
> hiện ở MỌI tab của app đó. Tab đang mở của từng app lưu trong `TicketState.appTabs`, giữ nguyên khi
> đóng/mở lại app; mở tab nào thì `AutoRevealApp` chỉ reveal clue của các action thuộc tab đó.

### Action theo từng app (DesktopActionSO mẫu — xem schema.md cho shape)
- **Network Settings**: `check_network_status` (Diagnostic), `reconnect_network` (Fix, chỉ chạy được khi `isOnline == false`).
- **POS Manager**: `check_pos_receipt_config` / `reset_pos_receipt_template` (receipt template); ngoài ra Staff Management (role/terminal assignment/sync — Mục 15) và Connections (`dbHost`, `registeredTerminalIp`) là các sub-tab wire riêng, không đi qua danh sách action chung.
- **POS Terminal**: `check_terminal_network` (Diagnostic — tiết lộ cả Wi-Fi lẫn IP hiện tại của terminal, là clue nguồn cho P6/P7); Sale/Refund/Void/Close batch/Check history là các action nghiệp vụ riêng (xem "Caller Authorization" bên dưới).
- **Printer & Print Queue**: `check_print_queue` (Diagnostic), `print_test_page` (test hardware/driver, KHÔNG cần dữ liệu giao dịch), `refill_paper_tray` (Fix, chỉ khi `paperLevel == Empty`). `print_customer_copy` (test có dữ liệu giao dịch thật, cần DB) được gọi từ **POS Manager ▸ Database**, không nằm trong app Printer dù target module vẫn là Printer.
- **Device Manager**: `open_device_manager` (Diagnostic), `reinstall_printer_driver` (Fix, chỉ khi `driverState == Corrupted`), `remove_readd_printer` (Fix, **risky** — có thể MadeWorse nếu driver không thật sự là nguyên nhân).
- **Cash Drawer Config**: `check_port_config` (Diagnostic), `move_cash_drawer_port` (Fix, chỉ khi trùng port với Printer).

---

## 4. Hệ sinh thái POS (mô hình thật — dùng để tạo problem)

### Thành phần & LINK
```
Network ──► POS Software (HUB) ──► Terminal (charge thẻ)
              │
              ├──► Database (transaction history)
              │
              └──► Printer (qua lớp "POS thấy printer không")
                        │
                        ├─ Printer config riêng (driver, port, spooler)
                        └──► Cash Drawer (share cổng / trigger mở khi in)
```

### POS Software (HUB)
- Connect tới Terminal.
- Tạo ticket cho staff, staff management (role/permission).
- Printer config phía POS: **chỉ kiểm tra "có thấy printer không"** (không cấu hình sâu).
- Template receipt, license, version, kết nối database.

### Terminal (máy tính tiền)
- Phần cứng charge thẻ, nhưng mang các **chức năng nghiệp vụ**: Sale, Refund, Void, Delete transaction, Close batch, Check history.
- Phụ thuộc POS cấp quyền + phiên đăng nhập.

### Caller Authorization (RẤT QUAN TRỌNG cho Refund/Void)
Người gọi KHÔNG nhất thiết là chủ tiệm (owner) trên hồ sơ CRM — có thể là nhân viên gọi thay chủ.
Đây là verification tầng thứ 2, tách biệt với việc chọn đúng CRM record (đúng tiệm) — vẫn có thể đúng
tiệm nhưng SAI người, hoặc đúng người nhưng chưa chắc có quyền.

- **Chỉ áp dụng rủi ro thật cho ticket thuộc case Refund/Void** (`isRefundVoidCase`, ~40% số ticket).
  Với các ticket "bình thường" khác (không liên quan Refund/Void) → **luôn 100% authorized** mặc định,
  để việc hỏi "Ask if owner authorized this" không bao giờ tự dưng cúp máy một ticket chẳng liên quan gì
  tới giao dịch nhạy cảm. Chỉ trong ticket thuộc case Refund/Void mới thực sự roll **50/50**
  (`callerAuthorized`) xem người gọi có thật sự được ủy quyền hay không.
- **`callerRole` (`Owner`/`Staff`) là nguồn sự thật DUY NHẤT cho danh tính người gọi** — không suy ra
  bằng cách so sánh chuỗi tên (dễ vỡ, dễ trùng lặp tình cờ). Ticket bình thường (không phải Refund/Void
  case) → `callerRole = Owner`, tên lấy thẳng từ CRM (`StoreProfile.ownerName`), hỏi "authorized" thì họ
  trả lời kiểu "tôi là chủ, không cần ai cho phép". Ticket thuộc case Refund/Void → `callerRole = Staff`,
  tên là một nhân viên khác hẳn (vd `STAFF_CALLER_NAMES` trong prototype) — lúc đó hỏi "owner có
  authorize không" mới có ý nghĩa, và mismatch trên Owner Name mới là thật (không phải lỗi nhớ nhầm
  chính tả tên mình).
- **Phát hiện nghi vấn:** player dùng click-to-compare (đối chiếu field CRM ↔ phát biểu của customer
  trong chat) trên **Owner Name**. Nếu **MISMATCH** → tên người đang gọi không khớp Owner trên hồ sơ →
  không thể mặc định người này có quyền thao tác nghiệp vụ (Refund/Void).
- **Player phải hỏi thêm** (action mới: "Ask if owner authorized this") — customer trả lời theo ground
  truth cố định của ticket (không phải random mỗi lần hỏi, giống người thật trả lời nhất quán):
  - **Được chủ tiệm ủy quyền** → xác nhận ("yeah, [owner] told me to call about this") → tiếp tục xử lý
    ticket bình thường, coi như đã xác minh quyền.
  - **KHÔNG được ủy quyền** → customer bối rối/thừa nhận, rồi **cúp máy ngay** (customer-initiated
    hangup, không phải agent). Đóng ticket ở trạng thái trung lập: KHÔNG tính strike (agent đã làm đúng
    khi không tiếp tục với người gọi chưa xác minh), nhưng CŨNG KHÔNG tính vào quota "≥150 ticket resolved"
    (không fix được vấn đề kỹ thuật thật).
- **MATCH tuyệt đối trên Owner Name** (qua compare) tự động coi là đã xác minh quyền — không cần hỏi
  thêm, vì CRM + đối chiếu tên đã chứng minh đúng là chủ tiệm.
- **Ràng buộc nghiệp vụ — đây là lý do cơ chế này tồn tại:** thực hiện **Refund** hoặc **Void** trên
  Terminal mà **chưa xác minh quyền** (chưa MATCH Owner Name, chưa hỏi-và-được-xác-nhận) là một rủi ro
  thật: nếu người gọi hoá ra KHÔNG được ủy quyền, đây là **HarmEvent** ("xử lý giao dịch không xác minh
  quyền") → ticket bị ép về **Degraded** dù mọi issue kỹ thuật khác đã Resolved sạch sẽ. Nếu vẫn thực
  hiện nhưng người gọi hoá ra CÓ được ủy quyền (may mắn đoán đúng) thì không bị phạt — chỉ phạt khi thật
  sự gây hại, đúng tinh thần MadeWorse hiện có (xem Mục 7 dưới), không phạt theo quy trình.

### Transaction data model (RẤT QUAN TRỌNG cho problem nghiệp vụ)
Một giao dịch nằm ở **2 nơi khác nhau**:
- **Batch** (trên terminal/processor): dữ liệu *tài chính* để settle tiền. Sống trong một batch.
- **Transaction History** (trong POS/DB): bản ghi *lưu trữ* để tra cứu, in lại, báo cáo. **Không mất khi close batch.**

Vòng đời:
```
AUTHORIZE (charge) → trans "Open" trong batch hiện tại (tiền mới HOLD)
   → có thể VOID/REFUND/in lại, tiền CHƯA chuyển
CLOSE BATCH → settle → tiền THẬT chuyển → trans "Settled"
   → sau settle KHÔNG void được, chỉ REFUND
```

Quy tắc trạng thái (nguồn của nhiều problem):
- Void chỉ hợp lệ khi `status == Open` (batch chưa đóng).
- Refund hợp lệ cả sau settle.
- Reprint đọc `receiptSnapshot` từ **history**, cần DB connection.
- Close batch: mọi trans Open → Settled, mở batch mới.

### Printer — 4 loại receipt
- **PrinterTestPage**: test phần cứng/driver, KHÔNG cần dữ liệu giao dịch.
- **MerchantReceipt / CustomerCopy / StoreReceipt**: output của POS + printer cùng đúng.
- Dùng test page để **tách lỗi phần cứng khỏi lỗi phần mềm** (vd P5 template: test page OK nhưng customer copy sai).

### Terminal — network identity (RẤT QUAN TRỌNG cho problem "terminal mất kết nối")
Terminal có 2 lớp kết nối tới POS, dạy phân biệt 2 loại lỗi khác nhau — và mô phỏng đúng DHCP thật
(join mạng nào thì được cấp IP của dải mạng đó, KHÔNG phải 2 field rời rạc gõ tay độc lập):
- **Wi-Fi network** (`Terminal.wifiNetwork`) — thứ DUY NHẤT player chọn ở Terminal ▸ Network (dropdown
  danh sách mạng xung quanh, giống màn hình chọn Wi-Fi thật). Terminal phải join đúng mạng của cửa hàng
  (`Network.ssid` — ground truth xem qua Network Settings). Join nhầm mạng xung quanh (guest network,
  quán kế bên, hotspot điện thoại...) → **IP/Gateway đổi theo hẳn sang dải mạng khác** (mỗi SSID có
  dải IP/gateway riêng), terminal không thấy POS dù internet của chính nó vẫn "up". Đây là root cause
  của **P6**.
- **IP registration** (`POSSoftware.registeredTerminalIp` so với IP thật terminal đang có — IP thật
  luôn suy ra từ Wi-Fi đang join, KHÔNG tự gõ được): POS giữ một "registered terminal roster". Ngay cả
  khi terminal join ĐÚNG mạng, IP thật của nó (đúng) vẫn có thể lệch với IP mà **POS** đang lưu trên hồ
  sơ (vd router reboot cấp lại IP qua DHCP nhưng chưa ai cập nhật lại POS) → fault nằm ở **phía POS**,
  fix bằng cách đăng ký lại đúng IP ở **POS Manager ▸ Connections** (có hiện luôn IP thật hiện tại của
  terminal để đối chiếu/copy). Đây là root cause của **P7**.
- **Tách biệt với staff login** (Mục 15 trong GDD chính): terminal có thể kết nối tốt (đúng wifi + IP đã
  đăng ký đúng) mà một nhân viên cụ thể vẫn không login được (role/assignment riêng của người đó) — 2
  tầng lỗi khác nhau, không được gộp làm một.

---

## 7. Dependency & Fault chain (App dependency graph)

Đây là dependency **runtime** (module A hỏng → module B báo lỗi theo), khác với dependency **data-level**
giữa các class (xem [schema.md](schema.md)).

### Thứ tự cascade (khớp `effectiveStatus()` trong prototype)
```
OS (Windows) ──► Network  ──────────────► POSSoftware ──────────────► Terminal
(gốc thật sự)   (own-fault: offline)      │                            (own-fault: sai Wi-Fi / IP cũ / clock lệch)
                                           └───────────────────────► Printer ──────► CashDrawer
                                (own-fault: receiptTemplate)  (own-fault: driver/paper/removed/spooler)  (own-fault: port trùng)
```

- **OS** — gốc thật sự, không phụ thuộc gì. Có **2 LOẠI fault khác hẳn nhau, không được gộp**:
  - **Machine-wide** (`diskSpace == Full`, `pendingReboot == true`) → `DependencyGraph.OsBlocking()` true →
    **Network `Blocked`**, kéo theo cả chuỗi `Blocked`. Đây là blocker Windows, vai trò giống hệt `P4`.
  - **Service-level** (`spoolerService == Stopped`, `systemTime == Skewed`) → **KHÔNG block chuỗi**. Chúng
    nổi lên thành `Error` CỤC BỘ ở đúng module cần dịch vụ đó: spooler chết → `Printer` báo Error "Print
    Spooler service is not running"; clock lệch → `Terminal` báo Error "card authorization rejected".
    Cố tình làm vậy vì fix nằm ở tầng OS mà player phải LẦN được tới đó — nếu để `Blocked` thì clue bị ẩn
    và người chơi đâm vào tường. Đây chính là chỗ dễ vi phạm quy tắc Blocked-vs-Error nhất.
- **Network** — phụ thuộc OS (chỉ loại machine-wide). Phân biệt **DOWN vs DEGRADED**:
  - `isOnline == false` → `Error`, và **CHỈ trường hợp này mới block** POSSoftware xuống dưới (`NetworkModule.IsDown()`).
  - `signalStrength == Weak` / `dnsServer` sai / `firewallBlocking` → `Error` nhưng **KHÔNG block**: link vẫn
    sống, POS vẫn chạy. Giấu cả máy sau `Blocked` chỉ vì một mục DNS sai là xoá mất đường lần của player.
    Mỗi cái làm hỏng một thứ khác nhau ở downstream — sóng yếu và firewall nổi lên ở `Terminal`, DNS sai nổi
    lên ở `DbConnected()`.
- **POSSoftware** — phụ thuộc Network. Network lỗi → **Blocked** ("cannot operate — reason: Network offline"). Ngoài ra còn own-fault: `receiptTemplate == Broken` → `Error`.
- **Terminal** — phụ thuộc POSSoftware. POS Blocked → **Blocked** ("cannot operate — reason: POS not connected"). Own-fault (KHÔNG phải Blocked, vẫn phải chẩn đoán được): sai Wi-Fi (P6) hoặc IP đăng ký cũ phía POS (P7) → `Error`.
- **Printer** — phụ thuộc POSSoftware (không phụ thuộc Terminal). POS Blocked → **Blocked**. Own-fault: `connection == Removed` / `driverState == Corrupted` / `paperLevel == Empty` → `Error`.
- **CashDrawer** — phụ thuộc Printer (không phải POSSoftware trực tiếp). Printer Blocked → **Blocked**. Own-fault: `port` trùng với Printer → `Error`.

**Layer riêng, KHÔNG nằm trong cascade trên:** login của một staff cụ thể (POS Manager ▸ Staff Management: role/terminal assignment/sync) — phụ thuộc Terminal đã `OK`, nhưng có thể fail cho MỘT người trong khi Terminal + mọi người khác hoàn toàn khỏe (xem Mục 15 GDD chính). Tương tự, kết nối Database (`POSSoftware.dbHost`, POS Manager ▸ Connections) là một check độc lập, phụ thuộc POS không bị Blocked nhưng không nằm trên chain Terminal/Printer/CashDrawer.

`StaffLoginStatus()` chấm theo đúng 4 bậc, dừng ở bậc sai đầu tiên (nguồn của P8–P11):
1. Terminal chưa `OK` → không phải chuyện permission, đi fix chain trước.
2. `staffRole` rỗng/`None` → **P8**.
3. `staffTerminal` rỗng → **P9**; hoặc có giá trị nhưng **khác `Terminal.machineId`** → **P10** ("assign rồi, nhưng cho máy khác"). `Terminal.machineId` là register tự khai mình là ai — đối chiếu với nó, KHÔNG đối chiếu với tên máy customer nói (họ nhầm till liên tục).
4. `terminalSynced == false` → **P11** (đổi rồi nhưng terminal chưa nhận).

Fix cho cả 4 bậc là nút inline trên sub-tab, không đi qua danh sách action chung — chỉ phần ĐỌC (`check_staff_account`, `check_pos_connections`) mới là `DesktopActionSO` để `DiagnosticClue.revealedBy` bám vào được.

### Quy tắc lan lỗi
- Mỗi module có `EffectiveStatus()`: nếu upstream lỗi → downstream trả trạng thái lỗi **kèm lý do trỏ ngược upstream** (vd "Terminal: cannot operate — reason: POS not connected"). *Lý do* này là manh mối để player lần chuỗi.
- `DependencyGraph`:
  - `PropagateFault(module)` → downstream nào bị ảnh hưởng (lan triệu chứng).
  - `CanFix(target)` → mọi upstream đã khỏe chưa (precondition).
- **FaultStatus.Latent**: fault đã inject nhưng bị blocker che → player không quan sát/fix được. Sau mỗi fix chạy `OnFixApplied`:
  1. cập nhật fault vừa tác động (→ Resolved nếu đạt).
  2. quét Latent: bỏ blocker đã Resolved khỏi `blockedBy`; rỗng → **Latent→Active** (lỗi mới lộ ra).
  3. (nếu thao tác sai) inject worseningFaults, có thể thành blocker mới.
- Fix action bị chặn 2 lớp khi blocker chưa fix: **ẩn chẩn đoán** (blocker báo lỗi trước) + **khóa thực thi** (`CanExecute` false).
- **Blocked vs Error — phân biệt bắt buộc đúng:** `Blocked` = lỗi do upstream (module này tự nó hoàn toàn khỏe, chỉ là "chưa nói chuyện được với upstream") → clue bị ẩn (Hidden) cho tới khi upstream fix xong. `Error` = lỗi cục bộ của chính module này → PHẢI vẫn chẩn đoán được ngay, không được ẩn. Nhầm lẫn 2 cái này (từng xảy ra với P6/P7 khi mới viết) làm sai cả `autoRevealApp` lẫn `evaluateIssue`.
