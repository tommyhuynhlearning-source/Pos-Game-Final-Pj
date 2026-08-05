# Managers — POS Tech Support (Unity)

> Tách từ [POS_TechSupport_GameDesign.md](POS_TechSupport_GameDesign.md) Mục 11 (trước chỉ có bảng tên + 1 dòng trách nhiệm). File này viết đủ: **schema** (class C#), **cơ chế** (chạy thế nào), **purpose** (vì sao tồn tại), **cách sử dụng** (ai gọi ai, theo thứ tự nào).
>
> Data shape dùng ở đây (ProblemInstance, TicketState, PersonaInstance, IssueSO...) xem [schema.md](schema.md). App/module/dependency graph xem [app.md](app.md). Số Mục giữ nguyên như GDD chính.

---

## Luồng gọi tổng quát (1 đêm chơi)

```
CampaignManager.StartNight()
  → ShiftManager.BeginShift(day, config)
       → TicketManager.ScheduleSpawns(day)                 // dùng ProblemGenerator để tạo trước hàng đợi
            → ProblemGenerator.GenerateAuto(day) → RandomPoolProblemFactory.Create(day) → ProblemInstance
       → [mỗi frame] ShiftManager.Tick(dt)
            → đến spawnTime  → TicketManager.Enqueue(problem) → hiện popup "Incoming call"
            → player Answer  → TicketManager.Activate(problem)
                 → VerificationManager theo dõi CRM lookup/compare/remote connect trên problem.ticket
                 → DesktopManager cấp EffectiveStatus cho DialogueManager (clue) và ActionManager (precondition)
                 → ActionManager.RunAction(actionId) → DesktopManager.ApplyChange() → ResolutionChecker.Evaluate()
                 → TransactionManager xử lý Sale/Refund/Void/Close batch (gate bởi VerificationManager.Authorization)
                 → DialogueManager/CommunicationManager render chat/SMS dựa trên GroundTruth rút gọn
            → player Hang Up / customer tự cúp (unauthorized) → TicketManager.Close(problem)
                 → ResolutionChecker.EvaluateTicket() → verdict → nếu MadeWorse/Degraded → MailboxManager.FileComplaint()
       → hết giờ → ShiftManager.EndShift()
            → TicketManager.FlushRemaining()                // đóng nốt queue/active/ringing còn dở
            → ScoreManager.Compute(history) → currencyEarned
            → MailboxManager.StrikeCount() → nightFailed?
            → ConsequenceManager.Commit(history)             // recurring fault, narrative flags, trust
  → CampaignManager.OnNightEnded(ScoreBreakdown, nightFailed)
       → cộng dồn CampaignState, check Win/Lose (Mục 1)
       → SaveManager.Persist(CampaignState, ConsequenceLedger)
```

---

## CampaignManager

**Schema**
```csharp
public class CampaignState {
    public int day;             // 1..GameConfigSO.totalDays
    public int ticketsResolved; // cộng dồn toàn campaign
    public int warnings;        // cộng dồn toàn campaign
    public int currency;        // paycheck, cộng dồn
}

public class CampaignManager : MonoBehaviour {
    public GameConfigSO config;
    public CampaignState state;
    public void StartNewCampaign();                          // state = default, xoá save cũ
    public void StartNight();                                // gọi ShiftManager.BeginShift(state.day, config)
    public void OnNightEnded(ScoreBreakdown score, bool nightFailed);
    public GameResult CheckWinLose();                         // Win/Lose/None — xem Mục 1
}
public enum GameResult { None, Win, Lose }
```

**Cơ chế:** giữ `CampaignState` sống suốt 60 ngày (không reset mỗi đêm, khác `NightState` của ShiftManager). Sau mỗi đêm: `ticketsResolved += resolvedCount`, `currency += currencyEarned` (từ ScoreManager), `warnings += 1` nếu `nightFailed`. Sau khi cộng, tăng `day` rồi gọi `CheckWinLose()`: `day > totalDays` → Win nếu `ticketsResolved >= minTotalTickets` else Lose (không đạt quota); `warnings > warningsToGameOver` → Lose ngay (không cần chờ hết 60 ngày).

**Purpose:** đây là state DUY NHẤT sống xuyên suốt nhiều đêm — mọi manager khác (`ShiftManager`, `TicketManager`...) đều bị huỷ/tạo lại mỗi đêm, chỉ `CampaignManager` (+ `ConsequenceLedger` nó giữ) là persistent.

**Cách sử dụng:** UI Hub gọi `CampaignManager.StartNight()` khi player bấm "Start Night". Sau khi `ShiftManager` chạy xong 1 đêm, nó tự gọi ngược lại `CampaignManager.OnNightEnded(...)`. Cuối cùng `CampaignManager` gọi `SaveManager.Persist(...)` — không manager nào khác được phép ghi save trực tiếp.

---

## ShiftManager

**Schema**
```csharp
public class NightState {
    public int day;
    public float elapsed;                    // giây thật, 0..config.shiftRealDurationSec
    public bool ended;
    public int ticketsTarget;                // tính từ day, xem TicketManager
    public float[] spawnTimes;                // lấy từ GameConfigSO.ticketTempoOverNight
    public int spawnedCount;
}

public class ShiftManager : MonoBehaviour {
    public GameConfigSO config;
    public NightState night;
    public void BeginShift(int day, GameConfigSO config);
    public void Tick(float deltaTime);        // gọi mỗi frame (Update)
    public string ClockLabel();               // "8:00 PM" .. "4:00 AM", suy từ elapsed
    public void EndShift();                   // trigger khi elapsed >= duration
}
```

**Cơ chế:** `Tick()` cộng dồn `elapsed`; giờ hiển thị = `20 + (elapsed/duration)*8` (20h→4h, đúng Mục 1). Tới mỗi mốc trong `spawnTimes` → gọi `TicketManager.Enqueue(...)`. `elapsed >= config.shiftRealDurationSec` (mặc định 480s = 8 phút, dev-tunable qua `cfg-shift-sec`) và chưa `ended` → gọi `EndShift()` một lần duy nhất.

**Purpose:** quy đổi thời gian THẬT (vài phút chơi) thành thời gian TRONG GAME (8 tiếng đêm) và làm tempo phát sinh ticket — tách biệt khỏi `TicketManager` để đổi tempo/độ dài ca không đụng vào logic hàng đợi.

**Cách sử dụng:** `CampaignManager` gọi `BeginShift`; `MonoBehaviour.Update()` gọi `Tick(Time.deltaTime)` mỗi frame; khi `EndShift()` chạy, nó gọi `TicketManager.FlushRemaining()` rồi `ScoreManager.Compute(...)` rồi trả kết quả ngược lên `CampaignManager.OnNightEnded(...)`.

---

## TicketManager

**Schema**
```csharp
public class TicketManager : MonoBehaviour {
    public List<ProblemInstance> queue;       // đã spawn, đợi tới lượt (chưa ringing)
    public ProblemInstance ringing;           // đang hiện popup "Incoming call", null nếu không có
    public ProblemInstance active;            // ticket đang mở (player đang xử lý)
    public List<ProblemInstance> history;     // đã đóng trong đêm này (Closed-*/Missed/Abandoned)

    public void Enqueue(ProblemInstance p);
    public void PromoteNextToRinging();        // queue.Pop() → ringing, set ringDeadline
    public void Answer();                      // ringing → active
    public void Decline();                     // ringing → Missed, vào history
    public void Close(ProblemInstance p, CallLifecycleStatus finalLifecycle);
    public void FlushRemaining();              // cuối ca: đóng nốt queue/ringing/active còn dở
}
```

**Cơ chế:** hàng đợi FIFO đơn giản — `ringing` timeout sau `config.ringTimeoutSec` giây không Answer → tự động `Decline()` (status `Missed`) → `MailboxManager.FileComplaint(HarmType.MissedCall)`. `FlushRemaining()` (gọi bởi `ShiftManager.EndShift()`) xử lý 3 trường hợp còn sót: `ringing` chưa trả lời → Missed; `active` đang dở → chấm bằng `ResolutionChecker.EvaluateTicket()` rồi đóng Closed-Resolved/Closed-Degraded/Abandoned; `queue` còn lại chưa tới lượt → Missed hàng loạt. Mỗi trường hợp lỗi đều sinh `HarmEvent` tương ứng.

**Purpose:** đây là "nguồn sự thật" cho **`CallLifecycleStatus`** của mọi ticket trong đêm (Queued/Ringing/Active/Closed/Missed/Abandoned) — tách biệt khỏi **`TicketStatus`** (verdict sức khỏe, do `ResolutionChecker` tính) đúng như đã ghi rõ ở [schema.md](schema.md).

**Cách sử dụng:** `ShiftManager` gọi `Enqueue`/`FlushRemaining`. UI popup "Incoming Call" gọi `Answer()`/`Decline()`. Khi đóng ticket (player bấm Hang Up, hoặc customer tự cúp vì `unauthorizedActionTaken`/authorization fail — xem app.md Caller Authorization), UI gọi `Close(active, ...)`.

---

## ProblemGenerator (Factory Pattern)

Trong prototype web, `makeTicket(day, forcedIssueIds?)` gộp chung 2 nguồn tạo ticket (random theo pool vs
forced từ dev-picker) bằng 1 tham số optional. Đây đúng dấu hiệu cần **Factory Method**: 2 biến thể cùng
tạo ra 1 `ProblemInstance`, chỉ khác NGUỒN chọn issue combo — tách thành factory riêng thay vì branch
if/else, và tách tiếp phần lắp ráp dùng chung (persona/desktop) thành factory con để test độc lập được.

**Schema**
```csharp
// Factory Method: nguồn issueIds khác nhau, cùng 1 hợp đồng tạo ProblemInstance
public interface IProblemFactory {
    ProblemInstance Create(int day);
}

// Bước LẮP RÁP dùng chung — tách hẳn ra khỏi factory (thay vì private Assemble lặp ở từng biến thể),
// nên mọi factory chỉ còn lo đúng 1 việc: lấy issueIds ở đâu.
public class ProblemAssembler {
    public ProblemAssembler(ContentDatabaseSO content, PersonaFactory personaFactory,
                            DesktopFactory desktopFactory, IGuidanceSource guidance, int guidanceMaxDay);
    public ProblemInstance Assemble(int day, string[] issueIds);   // 3 bước, xem "Cơ chế"; giữ luôn ticketSeq
}

// Biến thể "auto-spawn" — đúng cơ chế poolForDay/pickIssueCombo hiện có trong app.js
public class RandomPoolProblemFactory : IProblemFactory {
    public IssuePool[] poolByDayThreshold;   // day<=5:[P1] | <=15:[P1,P2] | <=30:[+P3,P6,P7] | <=45:[+P4-chain] | else: full
    public RandomPoolProblemFactory(ProblemAssembler assembler, IssuePool[] pool = null);
    public ProblemInstance Create(int day) => assembler.Assemble(day, PickCombo(day));
    public string[] PickCombo(int day);      // poolForDay(day) rồi random 1 combo
}
// combos là IssueCombo[] chứ không phải string[][]: Unity KHÔNG serialize được jagged array.
[Serializable] public class IssueCombo { public string[] issueIds; }
[Serializable] public class IssuePool  {
    public int maxDay; public IssueCombo[] combos;
    public static IssuePool[] DefaultTable();
    public static int OnboardingMaxDay(IssuePool[] table);   // = table[0].maxDay, xem KnowledgeBaseManager
}

// Biến thể "dev-picker" — ép issueIds cụ thể, KHÔNG random
public class ForcedIssueProblemFactory : IProblemFactory {
    public string[] issueIds;
    public ForcedIssueProblemFactory(ProblemAssembler assembler, string[] issueIds);
    public ProblemInstance Create(int day);  // bỏ qua PickCombo, Assemble thẳng với issueIds đã ép
}

// Biến thể "tái phát" — bọc 1 factory khác, ưu tiên issue tới hạn của ConsequenceManager (xem mục dưới)
public class RecurringProblemFactory : IProblemFactory {
    public RecurringProblemFactory(ProblemAssembler assembler, IProblemFactory fallback,
                                   Func<int, List<string>> dueToday, Action<string> consume);
    public ProblemInstance Create(int day);  // có recurrence tới hạn → dùng nó (và consume); không thì fallback
}

// Factory con dùng chung bởi mọi factory trên — tách riêng để test được từng phần
public class PersonaFactory {
    public PersonaFactory(PersonaProfileSO[] pool, string[] staffCallerNames, StoreProfileSO store, MachineConfig machine);
    public PersonaInstance Create(bool isRefundVoidCase);   // roll callerRole/name/stated* — xem schema.md
}
public class DesktopFactory {
    public DesktopFactory(ModuleBaseline baseline);
    public VirtualDesktopInstance Create(IssueSO[] issues); // clone baseline rồi apply từng issue.faults
}

// Hợp đồng tra bài hướng dẫn — KnowledgeBaseManager implement; giữ Assembler khỏi phụ thuộc lớp Managers
public interface IGuidanceSource { KnowledgeArticleSO FindGuidanceArticle(IssueSO issue); }

// Composition root — KHÔNG tự tạo ProblemInstance nữa, chỉ giữ sub-factory + trỏ đúng factory
public class ProblemGenerator {
    public IProblemFactory autoFactory;      // = RandomPoolProblemFactory, dùng cho auto-spawn
    public ProblemGenerator(ContentDatabaseSO content, IGuidanceSource guidance = null);
    public ProblemInstance GenerateAuto(int day) => autoFactory.Create(day);
    public ProblemInstance GenerateForced(int day, string[] issueIds);
    public void EnableRecurring(Func<int, List<string>> dueToday, Action<string> consume);  // bọc autoFactory
    public static int TicketCountForDay(int day);
}
```

**Cơ chế:** `RandomPoolProblemFactory.PickCombo` = đúng `poolForDay(day)` hiện có trong `app.js` — ngày 1–5
chỉ pool `[P1]`; 6–15 thêm `P2`; 16–30 thêm `P3/P6/P7`; 31–45 thêm combo blocker `P4+P1`/`P4+P2`; 46+ full
pool kể cả `P4+P3`/`P4+P5`/`P4+P6`. `Assemble` (dùng chung bởi cả 2 factory) chạy đúng 3 bước của
`makeTicket`: `DesktopFactory.Create` (clone baseline + inject `issue.faults` từng module) →
`PersonaFactory.Create` (roll `isRefundVoidCase` ~40%, từ đó suy `callerRole`/`callerAuthorized`/`stated*`
theo `profile.memoryAccuracy` — xem app.md Caller Authorization) → nếu `day <= 5` (tier `[P1]`), gọi thêm
`KnowledgeBaseManager.FindGuidanceArticle(issue)` gán vào `ticket.attachedArticle` (xem mục
KnowledgeBaseManager — "phân tầng hướng dẫn") → gói thành `ProblemInstance` mới với
`verification`/`transactions`/`ticket` ở trạng thái khởi tạo rỗng.

**Purpose:** đây là NƠI DUY NHẤT tạo ra `ProblemInstance` — không service nào khác được tự ý new một cái.
Tách factory con (`PersonaFactory`/`DesktopFactory`) khỏi factory chọn nguồn issue
(`RandomPoolProblemFactory`/`ForcedIssueProblemFactory`) để đổi 1 phần (vd thêm 1 nguồn issue mới — xem
`ConsequenceManager.DueRecurringToday`) mà không đụng logic roll persona/desktop, và test được từng factory
độc lập (vd assert tỉ lệ `isRefundVoidCase` đúng ~40% mà không cần dựng desktop thật).

**Cách sử dụng:** `TicketManager.ScheduleSpawns()`/`ShiftManager` gọi `ProblemGenerator.GenerateAuto(day)`
mỗi khi tới `spawnTimes` mốc. Dev picker ("Force this call now") gọi thẳng
`ProblemGenerator.GenerateForced(day, issueIds)`. `ConsequenceManager` (recurring fault xuyên đêm, xem mục
dưới) đã có `RecurringProblemFactory` riêng — `GameManager` gọi `Generator.EnableRecurring(...)` lúc dựng
service để bọc `autoFactory` lại; `RandomPoolProblemFactory`/`ForcedIssueProblemFactory` KHÔNG bị sửa gì.

---

## VerificationManager

**Schema**
```csharp
public class VerificationManager : MonoBehaviour {
    public void SetCrmQuery(TicketState t, string query);              // → t.crmLookup.results
    public void SelectCrmResult(TicketState t, int index);
    public void CompareClick(ProblemInstance p, CompareSource source, FactType type, string value);
        // 2 lần gọi liên tiếp (1 field CRM + 1 field chat cùng type) → chấm Match/Mismatch, ghi p.ticket.compare
    public bool TryRemoteConnect(TicketState t, string remoteId, string passcode);
        // đúng StoreProfileSO.remoteId THẬT của record được chọn + đúng passcode phiên này → true
    public bool CanGrantRemote(VerificationState v);                    // v.storeId/identity/machine đều Verified
}
public enum CompareSource { Crm, Chat }
```

**Cơ chế:** KHÔNG có ground-truth tự động hiện ra (đã bỏ ý tưởng "Incoming Caller ID" — xem app.md Caller Authorization, đây là challenge cố ý cho player). Player tự bấm 1 field CRM + 1 câu chat cùng loại (`FactType`) → `CompareClick` so sánh giá trị thật, set `pending`→`result` (Match/Mismatch). Match trên `OwnerName` tự động set `authorization.confirmed = true` (xem app.md). `TryRemoteConnect` không hard-block chọn sai record — record nào cũng có remoteId/passcode RIÊNG của nó, chọn record sai (decoy) thì connect chỉ đơn giản fail, không có gì "sập" cả — verify thật sự là việc của player, không phải của hệ thống.

**Purpose:** tách lớp "đúng tiệm nào" (CRM lookup, `VerificationState.storeId/machine`) khỏi lớp "đúng người nào" (`identity`, gắn với `PersonaInstance.role`/Caller Authorization) — 2 lớp verify độc lập, có thể đúng cái này sai cái kia.

**Cách sử dụng:** UI 3 cột trong ticket window (CRM panel giữa, remote-connect form bên phải) gọi trực tiếp các hàm trên theo click của player. `TransactionManager` hỏi `VerificationManager` (qua `ProblemInstance.ticket.authorization`) trước khi cho phép Refund/Void.

---

## DesktopManager

**Schema**
```csharp
public class DesktopManager : MonoBehaviour {
    public VirtualDesktopInstance Build(StoreProfileSO.MachineConfig baseline, IssueSO[] issues);
    public Status EffectiveStatus(VirtualDesktopInstance d, ModuleType m);   // xem app.md cho cascade thật
    public void ApplyChange(VirtualDesktopInstance d, FaultInjection change);
    public void OnFixApplied(VirtualDesktopInstance d, FaultInjection change); // xem Mục 8 GDD chính: Latent→Active, worseningFaults
}
```

**Cơ chế:** implement đúng cascade mô tả ở [app.md](app.md) — `Network` là gốc; `POSSoftware`/`Printer` phụ thuộc `Network`; `Terminal` phụ thuộc `POSSoftware`; `CashDrawer` phụ thuộc `Printer`. `EffectiveStatus` trả `Blocked` (kèm reason trỏ ngược upstream) nếu upstream chưa `OK`, `Error` nếu chính module đó có own-fault, `OK` nếu sạch. Đây là NƠI DUY NHẤT phân biệt `Blocked` vs `Error` — mọi module khác (ActionManager, DialogueManager) đọc kết quả này, không tự suy luận lại.

**Purpose:** mô phỏng đúng "lỗi lan xuôi, fix đi ngược" (nguyên tắc bất biến #4) mà không cần mỗi module tự viết lại logic dependency của riêng nó.

**Cách sử dụng:** `DesktopFactory.Create()` (trong `ProblemGenerator`, xem mục ProblemGenerator) gọi `Build()` lúc tạo ticket. `ActionManager` gọi `EffectiveStatus()` trước khi cho action chạy (precondition) và sau khi chạy fix (`ApplyChange` → `OnFixApplied`). `DialogueManager` gọi `EffectiveStatus()` gián tiếp qua clue reveal (`Blocked` → ẩn clue, xem app.md).

---

## ActionManager

**Schema**
```csharp
public class ActionManager : MonoBehaviour {
    public DesktopActionSO[] allActions;      // xem app.md "Action theo từng app"
    public bool CanExecute(VirtualDesktopInstance d, DesktopActionSO action);   // preconditions.All(...)
    public ActionResult RunAction(ProblemInstance p, string actionId);
    public HashSet<string> revealedActions;   // đã chạy actionId nào rồi — mirror TicketState.revealedActions
}
public class ActionResult { public string resultText; public bool triggeredMadeWorse; public bool triggeredRiskyWarning; }
```

**Cơ chế:** `CanExecute` chặn 2 lớp đúng Mục 7 GDD chính: nếu upstream `Blocked` → ẩn chẩn đoán (clue không hiện) VÀ khoá thực thi. Fix action risky (`isRisky == true`, vd "Remove & re-add printer device") → UI phải confirm trước, nếu chạy mà root cause thật không phải cái vừa risky đó → `stateChanges` áp `worseningFaults` của issue → `DesktopManager.OnFixApplied` phát hiện fault ngoài dự tính → `ResolutionChecker` trả `MadeWorse`.

**Purpose:** đại diện cho MỌI thao tác player làm trên virtual desktop (Diagnostic đọc, Fix ghi) — action nào cũng đi qua đây để precondition/risky/reveal được áp dụng nhất quán, không rải rác mỗi app tự check riêng.

**Cách sử dụng:** UI mỗi app (Printer/Network/Device Manager/...) gọi `ActionManager.RunAction(problem, actionId)` khi player bấm nút trong app đó. Sau khi chạy, `ResolutionChecker` được gọi lại để tính verdict mới cho ticket.

---

## ResolutionChecker

**Schema**
```csharp
public static class ResolutionChecker {
    public static ResolveStatus EvaluateIssue(VirtualDesktopInstance d, ActiveFault f);
    public static TicketStatus EvaluateTicket(ProblemInstance p);
}
```

**Cơ chế:** thuần hàm, không giữ state (đã có pseudocode đầy đủ ở Mục 8 GDD chính). `EvaluateIssue`: `Latent` → `Hidden`; check `rootCauseFixed` + test pass → `Resolved`; fault ngoài dự tính → `MadeWorse`; còn lại `Unresolved`. `EvaluateTicket`: bất kỳ issue nào `Hidden` → `InProgress`; tất cả `Resolved` → `Resolved`; có `MadeWorse` HOẶC `ticket.authorization.unauthorizedActionTaken` → `Degraded` (xem app.md — đây là 1 loại HarmEvent nghiệp vụ, không phải kỹ thuật, nhưng bị chấm CÙNG mức nghiêm trọng); còn lại `InProgress`.

**Purpose:** verdict (`TicketStatus`) phải luôn tính được LẠI TỪ ĐẦU từ state hiện tại của desktop + ticket — không bao giờ lưu verdict như 1 field độc lập rồi quên cập nhật (tránh bug "state đúng nhưng UI vẫn báo sai" từng gặp khi sửa `posState` reference).

**Cách sử dụng:** gọi sau MỖI thay đổi state có ý nghĩa — sau `ActionManager.RunAction`, sau `TransactionManager` xử lý Refund/Void, và bắt buộc gọi lại lúc `TicketManager.Close()`/`FlushRemaining()` để chốt verdict cuối cùng trước khi đưa vào `history`.

---

## TransactionManager

**Schema**
```csharp
public class TransactionManager : MonoBehaviour {
    public TransactionState state;             // history + batches, xem schema.md
    public Transaction Authorize(decimal amount, TransType type);   // → status Open, thêm vào batch hiện tại
    public bool Void(ProblemInstance p, Transaction t);             // chỉ hợp lệ khi status == Open
    public bool Refund(ProblemInstance p, Transaction t);           // hợp lệ cả sau Settled
    public void CloseBatch();                                       // mọi Open → Settled, mở batch mới
    public Transaction Reprint(string transactionId);                // đọc receiptSnapshot từ history, cần DB connection
}
```

**Cơ chế:** Void/Refund đều phải qua gate `ProblemInstance.ticket.authorization.confirmed` trước (xem `VerificationManager`/app.md Caller Authorization) — nếu chưa confirmed mà vẫn thực hiện: UI phải cảnh báo trước (`confirm()`-style), nếu player vẫn tiếp tục VÀ `callerAuthorized` ground-truth hoá ra false → set `ticket.authorization.unauthorizedActionTaken = true` (rồi `ResolutionChecker` cap verdict ở Degraded). `Reprint` cần `DesktopManager.EffectiveStatus(POSSoftware)` không `Blocked` (cần DB) — dùng để dạy trick "SMS receipt sai" (Mục 10 GDD chính).

**Purpose:** modelling đúng thật một giao dịch POS thật — tách Batch (tài chính, để settle) khỏi Transaction History (lưu trữ, không mất khi close batch), đúng app.md.

**Cách sử dụng:** tab **POS Terminal ▸ Batch** gọi `Authorize`/`TryTransaction(Void|Refund)`/`CloseBatch` khi player bấm nút nghiệp vụ; nếu `authorization.confirmed == false`, UI bật confirm dialog cảnh báo trước, chọn "Proceed anyway" mới truyền `proceedUnconfirmed = true`. Tab **POS Manager ▸ Database** gọi `Reprint` khi customer xin gửi lại receipt qua SMS (`CommunicationManager`) — tab này khoá khi `DependencyGraph.DbConnected()` fail.

---

## DialogueManager

**Schema** — sống ở namespace `POSTechSupport.AI`, đã implement (M4).
```csharp
// Ranh giới code-level của nguyên tắc bất biến #2 — thứ DUY NHẤT AI được cầm
public class GroundTruth {
    public string callerName; public CallerRole callerRole; public PersonaProfileSO persona;
    public string statedStoreName, statedOwnerName, statedMachineId;   // có thể SAI theo memoryAccuracy
    public bool callerAuthorized, isRefundVoidCase;
    public List<string> visibleSymptoms;        // symptom.layman ONLY — .technical không được copy sang
    public static GroundTruth From(ProblemInstance p);
    public string Misname(string text, Random rng);   // áp misnaming theo (1 - techLiteracy)
}
// KHÔNG có trong GroundTruth: IssueSO, ActiveFault, Symptom.technical, DiagnosticClue,
// ResolutionCondition, VirtualDesktopInstance. AI không thể lộ thứ nó chưa từng được cấp.

public enum PlayerIntent { Unknown, Greeting, AskSymptom, AskStoreName, AskOwnerName, AskMachineId,
                           AskAuthorized, AskWhenStarted, AskWhatTried, InstructCustomer,
                           RequestSmsReceipt, AskTechnical, Reassure, Goodbye }

public class IntentClassifier { ParsedUtterance Classify(string text); }   // keyword, offline
public static class TechnicalVocabulary { string[] Banned; bool ContainsAny(s); string FirstHit(s); }

public class DialoguePolicy {                       // "não"
    DialogueAct Decide(GroundTruth truth, DialogueState state, PlayerIntent intent);
}
public class DialogueAct { DialogueActKind kind; string content; FactRef fact; bool endsCall; }

public interface ILlmClient {                        // "miệng" — chỉ diễn đạt lại
    bool Enabled { get; }
    IEnumerator Rephrase(string systemPrompt, string line, Action<string> onDone);
}
public class TemplateLlmClient : ILlmClient { }      // mặc định, Enabled = false
public class OllamaLlmClient  : ILlmClient { }       // opt-in qua GameConfigSO.useLlm

public class GroundingGuard {
    bool IsSafe(string text, ProblemInstance p);     // guard NẰM NGOÀI ranh giới AI nên được thấy sự thật
    string Filter(string candidate, string safeFallback, ProblemInstance p);
}

public class DialogueManager {
    DialogueManager(ILlmClient llm, MonoBehaviour runner, int seed = 0);
    void OpenCall(ProblemInstance p);
    DialogueAct HandlePlayerUtterance(ProblemInstance p, string playerText);   // player tự gõ
    DialogueAct HandleIntent(ProblemInstance p, PlayerIntent intent, string agentLine = null); // quick-ask
}
```

**Cơ chế:** đúng 4 bước Mục 9 GDD chính — (1) `IntentClassifier` → intent; (2) `DialoguePolicy` đọc `GroundTruth` + `TicketState.dialogue` rồi quyết `DialogueAct`, chặn `AskTechnical` bằng KnowledgeBoundary; (3) sinh câu; (4) `GroundingGuard` quét trước khi lên màn hình.

Điểm quan trọng về **thứ tự**: bước 3 chạy SAU khi câu template đã được post, không phải trước. Template hiện ngay lập tức và luôn an toàn; nếu bật model và nó trả lời kịp, cùng một object `ChatLine` bị ghi đè tại chỗ. Nhờ vậy bật LLM chỉ có thể làm câu chữ tự nhiên hơn chứ không bao giờ làm treo cuộc gọi, và gỡ model ra thì game vẫn chơi được y nguyên.

`GroundingGuard` kiểm 2 thứ: (a) từ cấm trong `TechnicalVocabulary` — dùng CHUNG list với `IntentClassifier` nên "agent không hỏi được" và "customer không nói được" không bao giờ lệch nhau; (b) rò rỉ **tên field state** của fault thật. Chỉ kiểm tên field, KHÔNG kiểm giá trị fault — "Empty" là từ hoàn toàn bình thường để nói về khay giấy, cấm nó là bịt miệng câu nói lương thiện. Guard cũng chạy trên chính câu template: nếu template trượt thì đó là bug nội dung, cần bắt tại đây thay vì ship ra ngoài.

**Purpose:** AI KHÔNG BAO GIỜ lộ root cause (nguyên tắc bất biến #2) — nhưng cách bảo đảm không phải là kiểm duyệt output, mà là **không đưa đáp án cho nó ngay từ đầu** (`GroundTruth.From`). Guard chỉ là lớp thứ hai.

**Cách sử dụng:** UI chat gọi `CommunicationManager.SendChat` (ô nhập tự do) hoặc các nút quick-ask — cả hai đều đi vào cùng `DialogueManager`, nên nút bấm và ô gõ không bao giờ hành xử như 2 customer khác nhau. Bật model: tick `GameConfigSO.useLlm`, chỉnh `llmEndpoint`/`llmModel`/`llmTimeoutSec`.

---

## CommunicationManager

**Schema**
```csharp
public class CommunicationManager : MonoBehaviour {
    public void SendChat(ProblemInstance p, string text);
    public void RequestSmsReceipt(ProblemInstance p);      // → DialogueManager quyết AI gửi receipt ĐÚNG hay SAI theo persona
    public void FileMailboxComplaint(HarmEvent e);          // proxy sang MailboxManager
    public VoiceSession StartVoiceCall(ProblemInstance p);  // GĐ2, STT/TTS — optional
}
```

**Cơ chế:** điều phối 4 kênh (Voice/Chat/SMS/Mailbox) nhưng LUÔN dùng chung 1 `DialogueManager`/`PersonaInstance` phía sau — không kênh nào có "AI riêng". SMS receipt trick: gọi `DialogueManager` với `persona.profile.honesty`/`cooperativeness` thấp → có xác suất trả về receipt SAI (nhầm loại/cũ/máy khác) — player phải tự đối chiếu (timestamp, mã máy/store, loại receipt) trước khi tin.

**Purpose:** giữ 1 nguồn hành vi persona duy nhất dù player chọn kênh nào để liên lạc — tránh tình trạng "customer lịch sự trên chat nhưng cộc lốc trên SMS" (persona không nhất quán).

**Cách sử dụng:** UI ticket window gọi `SendChat`/`RequestSmsReceipt`. Mọi harm phát sinh (missed call, degraded ticket...) từ các manager khác gọi `FileMailboxComplaint` — thực chất chỉ proxy, xử lý thật nằm ở `MailboxManager`.

---

## MailboxManager

**Schema**
```csharp
public class Mail { public string subject; [TextArea] public string body; public HarmEvent cause; }
public class HarmEvent { public HarmType type; public string ticketId; [TextArea] public string description; }

public class MailboxManager : MonoBehaviour {
    public List<Mail> nightMails;                     // reset mỗi đêm bởi ShiftManager.BeginShift
    public void FileComplaint(HarmType type, string ticketId, string description);
    public int StrikeCount();                          // = nightMails.Count
    public bool NightFailed(GameConfigSO config);       // StrikeCount() >= config.strikesPerNightFail
}
```

**Cơ chế:** mỗi `HarmEvent` (missed call, ticket đóng Degraded, call bị cắt ngang hết ca, unauthorized transaction...) → 1 `Mail`. Đúng nguyên văn Mục 1 GDD chính: **3 mail phàn nàn = fail 1 đêm**; fail đêm mới cộng vào `CampaignState.warnings` (không phải bản thân số mail cộng trực tiếp vào warnings).

**Purpose:** tách "có bao nhiêu sự cố trong 1 đêm" (nightMails, reset mỗi đêm) khỏi "tích luỹ bao nhiêu đêm fail" (`CampaignState.warnings`, không reset) — 2 bộ đếm khác tầng, dễ nhầm nếu gộp chung.

**Cách sử dụng:** mọi manager khác (`TicketManager`, `TransactionManager`, `ActionManager`...) gọi `FileComplaint(...)` ngay khi phát hiện harm — KHÔNG tự cộng dồn warning; `ShiftManager.EndShift()` đọc `StrikeCount()`/`NightFailed()` để quyết định trả `nightFailed` về `CampaignManager`. Player đọc mail qua nút **✉ Mailbox** ở màn Night (overlay liệt kê subject/body/`HarmType` + đếm strike). Ticket đóng Degraded chỉ sinh ĐÚNG 1 mail: `HarmType.UnauthorizedTransaction` nếu do `unauthorizedActionTaken`, còn lại `HarmType.DegradedTicket` — không cộng đôi.

---

## ConsequenceManager

**Schema**
```csharp
public class ConsequenceLedger {                          // persistent, sống trong CampaignManager, xuyên nhiều đêm
    public List<RecurringFault> pendingRecurring;          // fix tạm (symptomCleared) nhưng chưa fix gốc (rootCauseFixed)
    public float trust;                                     // tuỳ chọn, GĐ2 — ảnh hưởng tone/patience của persona
    public List<string> narrativeFlags;                     // vd "từng cấp role Admin thừa quyền cho staff X"
}
[Serializable] public class RecurringFault { public string issueId; public int dueDay; }

public class ConsequenceManager : MonoBehaviour {
    public ConsequenceLedger ledger;
    public void Commit(List<ProblemInstance> nightHistory);   // gọi cuối mỗi đêm
    public List<string> DueRecurringToday(int day);             // → ProblemGenerator ưu tiên tái phát issue này
    public void ConsumeRecurring(string issueId);               // xoá entry sau khi đã spawn — tái phát 1 lần, không lặp mãi
}
```

**Cơ chế:** cuối mỗi đêm, quét `nightHistory` tìm ticket đóng kiểu "fix tạm" (`symptomCleared` đạt nhưng `rootCauseFixed` chưa — xem Mục 8 GDD chính) → thêm `RecurringFault` với `dueDay` = đêm này hoặc đêm sau. Khi có `RecurringFault` tới hạn, `ProblemGenerator` nên dùng thêm 1 `IProblemFactory` mới (`RecurringProblemFactory`, xem mục ProblemGenerator) ưu tiên issueId đang `DueRecurringToday` — KHÔNG sửa `RandomPoolProblemFactory` để nhét logic này vào.

**Purpose:** đây là cơ chế DUY NHẤT tạo hậu quả XUYÊN ĐÊM (khác `MailboxManager`/`ScoreManager` chỉ tính trong phạm vi 1 đêm) — chưa implement trong prototype web (mọi ticket hiện độc lập theo đêm), là phần cần bổ sung khi build Unity thật.

**Cách sử dụng:** `GameManager.HandleNightEnded()` gọi `Commit(history, day)` sau khi `ShiftManager.EndShift()` đã `FlushRemaining()`. `RecurringProblemFactory` (bọc `autoFactory` qua `Generator.EnableRecurring(...)`) đọc `DueRecurringToday()` mỗi khi tạo ticket mới, rồi `ConsumeRecurring()` ngay để 1 recurrence chỉ tái phát đúng 1 lần.

> Với bộ content mẫu hiện tại, `symptomCleared` và `rootCauseFixed` được sinh giống hệt nhau (xem `SampleContentBootstrap.Resolution()`), nên chưa ticket nào rơi vào diện "fix tạm" — cơ chế đã nối dây đầy đủ nhưng còn nằm im cho tới khi có issue tách 2 điều kiện đó ra.

---

## ScoreManager

**Schema**
```csharp
public class ScoreBreakdown {
    public int resolvedCount, degradedCount;
    public int currencyEarned;     // công thức hiện tại (prototype): resolvedCount*10 - degradedCount*15, clamp >= 0
}
public class ScoreManager : MonoBehaviour {
    public ScoreBreakdown Compute(List<ProblemInstance> nightHistory);
}
```

**Cơ chế:** đếm `ticket.verdict == Resolved` / `== Degraded` trong `nightHistory`, áp công thức tuyến tính đơn giản (đúng những gì prototype đang chạy: `earned = resolved*10 - degraded*15`, không âm). GDD Mục 8/12 còn gợi ý chấm điểm chi tiết hơn (root cause đúng, bước thừa, fault phụ, thời gian, gốc vs tạm — bảng Mục 11) — CHƯA implement trong prototype, để mở rộng `ScoreBreakdown` sau nếu cần.

**Purpose:** quy đổi kết quả kỹ thuật (Resolved/Degraded) thành currency — input duy nhất cho `CampaignManager.state.currency`.

**Cách sử dụng:** `ShiftManager.EndShift()` gọi `Compute(history)` rồi truyền `ScoreBreakdown` lên `CampaignManager.OnNightEnded(...)`.

---

## KnowledgeBaseManager

**Schema**
```csharp
public class KnowledgeBaseManager : MonoBehaviour {
    public KnowledgeArticleSO[] articles;    // xem schema.md
    public KnowledgeArticleSO[] SearchByCategory(IssueCategory c);
    public KnowledgeArticleSO[] SearchByErrorCode(string code);

    // Guidance onboarding — xem "Cơ chế phân tầng hướng dẫn" bên dưới
    public KnowledgeArticleSO FindGuidanceArticle(IssueSO issue);   // khớp article.guidanceForIssueIds ∋ issue.issueId
}
```

**Cơ chế tra cứu (mọi lúc):** index tĩnh trên `articles` (schema `KnowledgeArticleSO` — `articleId, title, category, content, relatedErrorCodes, guidanceForIssueIds`), không có state runtime, không ghi gì lại. `SearchByErrorCode` so khớp không phân biệt hoa/thường và tự trim.

**Vì sao `FindGuidanceArticle` khớp theo `issueId` chứ không theo `category`:** nhiều issue dùng chung category (P1/P2 đều `Printer`, P5/P7 đều `POS`). Khớp theo category thì kết quả phụ thuộc **thứ tự mảng** `articles` — xê dịch một cái là ticket hết giấy ngày 1–5 được phát bài "driver hỏng". Nên "bài nào là bánh xe tập của issue nào" là quyết định của người viết content (`guidanceForIssueIds`), không phải thứ hệ thống tự đoán. Issue chưa ai viết bài → trả `null` → panel guidance tự ẩn, đúng như hành vi day > 5.

**Cơ chế phân tầng hướng dẫn (fade-out theo level):** dùng LẠI đúng ngưỡng ngày đã có ở `poolForDay` (day <= 5, tier chỉ có `P1`) làm ranh giới — KHÔNG thêm 1 config số riêng (`guidanceDays` hay tương tự) chỉ để lặp lại cùng 1 ý "mấy ngày đầu". Cơ chế:
- **`day <= 5`** (đúng tier `[P1]` của `poolForDay`): `ProblemGenerator.Assemble()` (xem mục ProblemGenerator) gọi thêm `KnowledgeBaseManager.FindGuidanceArticle(issue)` cho issue đầu tiên trong combo, gán thẳng vào `TicketState.attachedArticle` (xem schema.md) — bài viết tự hiện sẵn trong ticket window, KHÔNG cần player tự mở Knowledge Base app.
- **`day > 5`**: `Assemble()` không gọi `FindGuidanceArticle` nữa → `attachedArticle` luôn `null`. Panel hiển thị guidance trong ticket window (nếu có) sẽ không render gì — player bắt buộc tự mở app Knowledge Base, tự `SearchByCategory`/`SearchByErrorCode`.
- Vì ranh giới này CHÍNH LÀ ranh giới độ khó issue (P1-only), 2 việc "issue dễ nhất" và "có hướng dẫn" fade cùng lúc, đúng 1 nhịp — không lệch tầng.

**Purpose:** cho player (không phải AI) tra cứu mã lỗi/category khi bí — một dạng "help doc" trong game, KHÔNG liên quan tới `DialoguePolicy`/GroundTruth của customer AI. Phần "auto-attach rồi fade" là bánh xe tập (training wheels) tháo dần, không phải easy-mode vĩnh viễn — sau tier đầu, KB vẫn tồn tại, chỉ là không còn ai bê sẵn bài viết tới tận tay player nữa.

**Cách sử dụng:** UI ticket window đọc `TicketState.attachedArticle` — khác null thì render sẵn ở panel guidance (cột Remote), null thì panel tự ẩn. Nút "📚 Knowledge Base" ở footer ticket window LUÔN có (overlay riêng, không phải app trên desktop customer — đây là sổ tay của chính agent): chọn category → `SearchByCategory`, gõ mã lỗi → `SearchByErrorCode`. `ProblemAssembler` là nơi DUY NHẤT quyết định có gọi `FindGuidanceArticle` hay không — `KnowledgeBaseManager` bản thân không biết gì về "day"/"tier", chỉ lo tra cứu thuần.

---

## SaveManager

**Schema**
```csharp
public class SaveManager : MonoBehaviour {
    public void Persist(CampaignState state, ConsequenceLedger ledger, GameConfigSO configOverrides);
    public (CampaignState, ConsequenceLedger)? Load();
    public void ResetSave();
}
```

**Cơ chế:** serialize toàn bộ persistent state (`CampaignState` + `ConsequenceLedger` + config override đã tunable) thành 1 blob JSON, ghi vào `PlayerPrefs`/file lúc build Unity (prototype web đang dùng `localStorage` với key cố định — xem `SAVE_KEY` trong `app.js`). KHÔNG persist `NightState`/`ProblemInstance` đang dở — nếu thoát giữa đêm, đêm đó coi như mất (đúng hành vi hiện tại của prototype, không có "resume giữa ca").

**Purpose:** đây là NƠI DUY NHẤT chạm vào storage — mọi manager khác (kể cả `CampaignManager`) không tự ý đọc/ghi `PlayerPrefs` trực tiếp, tránh 2 chỗ ghi đè lẫn nhau.

**Cách sử dụng:** `CampaignManager.OnNightEnded(...)` gọi `Persist(...)` sau khi cộng dồn xong. App khởi động gọi `Load()` một lần; nếu null → `CampaignManager.StartNewCampaign()`. Nút "Reset Campaign" ở Hub gọi `ResetSave()`.
