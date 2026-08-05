# Schema — POS Tech Support

> Tách từ [POS_TechSupport_GameDesign.md](POS_TechSupport_GameDesign.md) Mục 5 (ScriptableObject Schema) + Mục 6 (Runtime classes). Số Mục giữ nguyên như tài liệu gốc — code (`app.js`) và chính GDD tham chiếu `GDD Mục 5` v.v., đổi số ở đây sẽ làm sai hết tham chiếu đó.
>
> App/desktop-app cụ thể (Printer, Network, POS Manager...) xem [app.md](app.md) — file này chỉ có **shape dữ liệu**, không có logic dependency giữa các app.

---

## 5. ScriptableObject Schema

### IssueSO (trung tâm — 4 tầng)
```csharp
[CreateAssetMenu(menuName="POS/Issue")]
public class IssueSO : ScriptableObject {
    public string issueId;
    public IssueCategory category;      // Terminal/POS/Printer/OS/Network/CashDrawer/Business
    public DifficultyTier tier;         // Basic/Medium/Hard
    public bool isBlocker;

    public FaultInjection[] faults;             // TẦNG 1
    public Symptom[] symptoms;                  // TẦNG 2
    public DiagnosticClue[] clues;              // TẦNG 3
    public ResolutionCondition resolution;      // TẦNG 4

    public string[] blockedByIssueIds;          // dây chuyền: bị che tới khi các id fixed
    public FaultInjection[] worseningFaults;    // player thao tác sai → inject thêm
}

[Serializable] public class FaultInjection {
    public ModuleType module;      // module nào — xem app.md để biết module ↔ app nào
    public string stateField;
    public string faultValue;
}

[Serializable] public class Symptom {
    [TextArea] public string layman;     // customer NÓI ĐƯỢC
    [TextArea] public string technical;  // CHỈ hiện khi remote — customer KHÔNG có
}

[Serializable] public class DiagnosticClue {
    public DesktopActionType revealedBy;  // action nào tiết lộ clue này — action sống trong app nào, xem app.md
    [TextArea] public string clueText;
    public bool isRedHerring;
}

[Serializable] public class ResolutionCondition {
    public StateCheck[] symptomCleared;  // fix tạm: triệu chứng hết, customer hài lòng
    public StateCheck[] rootCauseFixed;  // fix gốc: nguyên nhân khỏi
    public bool requiresTestPass;
    public ReceiptType testReceiptType;
}

[Serializable] public class StateCheck {
    public ModuleType module;
    public string field;
    public ComparisonOp op;              // Equals/NotEquals/GreaterThan/LessThan
    public string expectedValue;
}
```

### StoreProfileSO
```csharp
[CreateAssetMenu(menuName="POS/Store")]
public class StoreProfileSO : ScriptableObject {
    public string storeId, storeName, ownerName, phoneNumber, address;
    public MachineConfig[] machines;
}
[Serializable] public class MachineConfig {
    public string machineId, osVersion, posSoftwareVersion;
    public HardwareSpec hardware;
    public ModuleBaseline baseline;   // state "khỏe mạnh" mặc định — clone lúc runtime
}
```

### PersonaProfileSO
```csharp
[CreateAssetMenu(menuName="POS/Persona")]
public class PersonaProfileSO : ScriptableObject {
    public string personaId, displayName;
    [Range(0,1)] public float techLiteracy;    // trần THẤP cho customer thường (<=0.7)
    [Range(0,1)] public float cooperativeness;
    [Range(0,1)] public float memoryAccuracy;
    [Range(0,1)] public float emotionalState;
    [Range(0,1)] public float honesty;
    public MisnameEntry[] misnaming;            // "màn POS" → "máy tính tiền"
    public string[] laymanVocabulary;
    // (tùy chọn) khuôn câu theo cảm xúc cho fallback template
}
[Serializable] public class MisnameEntry { public string correctTerm, customerTerm; }
```

### DesktopActionSO
```csharp
[CreateAssetMenu(menuName="POS/DesktopAction")]
public class DesktopActionSO : ScriptableObject {
    public string actionId;
    public DesktopActionType actionType; // nối Diagnostic ↔ DiagnosticClue.revealedBy
    public ActionKind kind;             // Diagnostic (đọc) / Fix (ghi)
    public ModuleType targetModule;     // module bị tác động — KHÁC nơi action hiển thị
    public string appKey;               // cửa sổ app chứa action này — xem app.md
    public string appTab;               // sub-tab trong app đó; rỗng = hiện ở mọi tab
    public StateCheck[] preconditions;   // precondition chain
    public FaultInjection[] stateChanges;// fix ghi gì vào state
    [TextArea] public string resultText; // câu ghi vào sessionLog khi chạy xong (rỗng → fallback generic)
    public bool isRisky;                 // cảnh báo MadeWorse — UI PHẢI confirm trước khi RunAction
    public bool isTest;                  // chạy RunTest(testReceiptType) thay vì đọc state
    public ReceiptType testReceiptType;
}
```

### KnowledgeArticleSO / ReceiptTemplateSO / GameConfigSO
```csharp
[CreateAssetMenu(menuName="POS/KnowledgeArticle")]
public class KnowledgeArticleSO : ScriptableObject {
    public string articleId, title; public IssueCategory category;
    [TextArea] public string content; public string[] relatedErrorCodes;
    public string[] guidanceForIssueIds;   // issue nào dùng bài này làm guidance onboarding (auto-attach).
                                           // Khớp theo issueId, KHÔNG suy từ category: P1/P2 cùng Printer,
                                           // P5/P7 cùng POS → khớp category sẽ phụ thuộc thứ tự mảng.
                                           // Rỗng = chỉ để player tra, không bao giờ tự gắn.
}
[CreateAssetMenu(menuName="POS/ReceiptTemplate")]
public class ReceiptTemplateSO : ScriptableObject {
    public ReceiptType type; public ReceiptField[] fields;
}
[CreateAssetMenu(menuName="POS/GameConfig")]
public class GameConfigSO : ScriptableObject {
    public float shiftRealDurationSec = 480f;   // 8 phút — DEV CHỈNH ĐƯỢC
    public float shiftStartHour = 20f, shiftEndHour = 4f;
    public int totalDays = 60;
    public int minTotalTickets = 150;
    public int strikesPerNightFail = 3;
    public int warningsToGameOver = 3;
    public AnimationCurve ticketTempoOverNight; // spawn dồn về khuya
}
```

### Enums cần định nghĩa
`IssueCategory, DifficultyTier, ModuleType, ComparisonOp, ReceiptType (TestPage/Merchant/Customer/Store), ActionKind, DesktopActionType, FaultStatus (Latent/Active/Resolved), ResolveStatus (Unresolved/Resolved/MadeWorse/Hidden), TicketStatus (InProgress/Resolved/Degraded — verdict sức khỏe, KHÁC CallLifecycleStatus), CallLifecycleStatus (Queued/Ringing/Active/Closed/Missed/Abandoned), CallerRole (Owner/Staff), FactType (StoreName/OwnerName/MachineId), CompareResult (Match/Mismatch), ChatKind (Customer/Agent/System), TransType, TransStatus (Open/Voided/Settled/Refunded), BatchStatus (Open/Closed/SettleFailed), HarmType.`

---

## 6. Runtime classes (KHÔNG phải SO)

```csharp
public class VirtualDesktopInstance {   // clone baseline + inject fault — xem app.md cho từng module/app
    public Dictionary<ModuleType, ModuleBase> modules;
    public DependencyGraph graph;       // xem app.md Mục "App dependency graph" — logic cascade sống ở đó
    public ModuleBase GetModule(ModuleType t);
}

public abstract class ModuleBase {
    public abstract Status EffectiveStatus();  // trạng thái SAU khi xét dependency (app.md)
    public object GetField(string name);
    public void SetField(string name, object value);
}

public class ProblemInstance {
    public IssueSO[] issues;               // đọc-only
    public StoreProfileSO store;
    public PersonaInstance persona;        // runtime instance — xem định nghĩa ngay dưới TicketState
    public VirtualDesktopInstance desktop;
    public List<ActiveFault> faults;       // Latent/Active/Resolved
    public VerificationState verification;
    public TransactionState transactions;
    public TicketState ticket;
}

public class ActiveFault {
    public IssueSO issue;
    public FaultStatus status;
    public List<string> blockedBy;         // cập nhật runtime (kể cả MadeWorse tạo blocker mới)
}

public class VerificationState {
    public FieldStatus storeId, identity, machine; // Unknown/Claimed/Verified/Mismatch
    public bool CanGrantRemote();
}

public class TransactionState {
    public List<Transaction> history;      // KHÔNG mất khi close batch
    public List<Batch> batches;
}

// TicketState = state của MỘT cuộc gọi/ticket đang chạy. Lưu ý: CallLifecycleStatus (vòng đời cuộc gọi)
// và TicketStatus (verdict sức khỏe, từ EvaluateTicket — Mục 8 trong GDD chính) là HAI enum KHÁC NHAU dù
// tên nghe giống nhau — status="Closed" + verdict=Degraded là "cuộc gọi đã đóng, kết quả tệ", không phải
// một giá trị gộp chung "Closed-Degraded".
public class TicketState {
    public string ticketId;
    public int day;
    public string[] issueIds;                  // trỏ tới IssueSO[] tương ứng, đọc-only
    public CallLifecycleStatus lifecycle;      // Queued/Ringing/Active/Closed/Missed/Abandoned
    public TicketStatus verdict;               // InProgress/Resolved/Degraded — tính lại từ EvaluateTicket mỗi lần desktop đổi state
    public List<ChatLine> chat;
    public List<SessionLogLine> sessionLog;

    // KHÔNG có field "caller" riêng ở đây — danh tính người gọi (role/name/stated*) SỐNG TRONG
    // ProblemInstance.persona (PersonaInstance, xem dưới), vì nó là MỘT PHẦN của "ai đang gọi",
    // không phải state tách rời của cái ticket.
    public CrmLookupState crmLookup;
    public CompareState compare;
    public AuthorizationState authorization;
    public RemoteConnectState remoteConnect;

    public KnowledgeArticleSO attachedArticle; // guidance onboarding: ProblemGenerator.Assemble() tự gắn khi day <= tier
                                               // đầu của poolForDay, null từ đó về sau — xem manager.md KnowledgeBaseManager
    public string openAppKey;                  // app nào đang mở trong ticket window, null nếu đóng hết — xem app.md
    public HashSet<string> revealedActions;    // action nào đã được chạy ít nhất 1 lần (cho UI "đã xem")
    public Dictionary<string, string> appTabs;  // sub-tab đang mở của mỗi app, giữ khi đóng/mở lại app

    public int batchId;
    public List<Transaction> transactions;     // batch sống hôm nay — Terminal đọc cái này
    public List<Transaction> dbArchive;        // đã persist (POS Manager ▸ Database), sống qua nhiều đêm
    public string dbSelectedDay;
    public List<PrintJob> printJobs;
}

[Serializable] public class ChatLine { public ChatKind kind; public string text; public FactRef fact; } // fact optional — dùng cho click-to-compare
[Serializable] public class FactRef { public FactType type; public string value; }                       // storeName/ownerName/machineId
[Serializable] public class SessionLogLine { public ChatKind kind; public string text; }

// PersonaInstance = cặp runtime của PersonaProfileSO, giống hệt quan hệ IssueSO ↔ ActiveFault:
// PersonaProfileSO là TEMPLATE tĩnh tái dùng được (traits: techLiteracy/honesty/misnaming/...);
// PersonaInstance là "con người cụ thể đang gọi trong TICKET NÀY" — thêm role/tên/stated* lên
// trên cái template đó. Role/tên/stated* KHÔNG nằm trong PersonaProfileSO (SO tĩnh không được
// chứa state runtime — nguyên tắc bất biến #6 của GDD chính), nhưng cũng KHÔNG phải một class
// rời rạc không liên quan gì tới persona — nó chính là "persona đã instantiate cho ticket này".
//
// Caller Authorization (chi tiết đầy đủ ở app.md, phần POS Manager/Terminal): role là NGUỒN SỰ THẬT
// DUY NHẤT cho danh tính người gọi — name/stated* đều suy ra TỪ role, không suy role ngược lại từ so
// sánh tên (dễ vỡ, dễ trùng lặp tình cờ). statedStoreName/statedOwnerName/statedMachineId có thể SAI vì
// đây là profile.memoryAccuracy (persona trait) đang phát huy tác dụng — lý do đúng để 2 thứ này sống
// chung một class thay vì tách rời.
[Serializable] public class PersonaInstance {
    public PersonaProfileSO profile;   // template tĩnh — traits + misnaming + laymanVocabulary
    public CallerRole role;            // Owner/Staff — rolled riêng cho ticket này
    public string name;                // Owner → lấy thẳng StoreProfile.ownerName; Staff → tên khác hẳn
    public string statedStoreName;     // có thể sai theo profile.memoryAccuracy — player phải verify qua CRM
    public string statedOwnerName;
    public string statedMachineId;
}

[Serializable] public class CrmLookupState {
    public string query;
    public StoreProfileSO[] results;   // có thể nhiều kết quả (kể cả decoy) — player tự chọn đúng
    public int selectedIndex;
}

[Serializable] public class CompareState {
    public FactRef pending;            // 1 field đã chọn, đang chờ field thứ 2 để so sánh
    public CompareResult result;       // Match/Mismatch/null — chỉ có SAU khi player tự bấm so sánh
}

[Serializable] public class AuthorizationState {
    public bool isRefundVoidCase;          // ~40% ticket — chỉ nhóm này mới có rủi ro authorization thật
    public bool callerAuthorized;          // ground truth — roll 50/50 CHỈ khi isRefundVoidCase, còn lại luôn true
    public bool confirmed;                 // do PLAYER xác lập (compare Owner Name MATCH, hoặc hỏi thẳng)
    public bool asked;
    public bool customerHungUp;            // disable toàn bộ ticket window trừ nút Hang Up
    public bool unauthorizedActionTaken;   // Refund/Void bị thực hiện dù chưa confirmed → verdict cap ở Degraded
}

[Serializable] public class RemoteConnectState {
    public string passcode;                // one-time session code, sinh mới mỗi ticket
    public string queryId, queryPass;      // input hiện tại của player trong form
    public bool connected;
    public bool connectFailed;
}
```

---

## Dependency giữa các class (data-level — không phải EffectiveStatus)

Đây là dependency kiểu "class A CHỨA/tham chiếu class B", khác với dependency kiểu "module A hỏng làm module B hỏng theo" (cái đó ở [app.md](app.md)).

```
ProblemInstance
 ├─ issues            : IssueSO[]
 ├─ store              : StoreProfileSO
 ├─ persona            : PersonaInstance          ← "ai đang gọi thật sự" (role/tên/stated*) SỐNG Ở ĐÂY
 │    └─ profile        : PersonaProfileSO         ← template tĩnh (traits), tách biệt khỏi runtime ở trên
 ├─ desktop            : VirtualDesktopInstance
 │    ├─ modules       : Dictionary<ModuleType, ModuleBase>   ← xem app.md (module nào ↔ app nào)
 │    └─ graph         : DependencyGraph                       ← logic cascade, xem app.md
 ├─ faults             : List<ActiveFault>
 │    └─ issue         : IssueSO
 ├─ verification       : VerificationState
 ├─ transactions       : TransactionState
 │    ├─ history       : List<Transaction>
 │    └─ batches       : List<Batch>
 └─ ticket             : TicketState                          ← KHÔNG có field "caller" riêng, xem persona ở trên
      ├─ crmLookup     : CrmLookupState  → results: StoreProfileSO[]
      ├─ compare       : CompareState    → pending/result: FactRef
      ├─ authorization : AuthorizationState
      ├─ remoteConnect : RemoteConnectState
      ├─ chat          : List<ChatLine>  → fact: FactRef
      └─ sessionLog    : List<SessionLogLine>

IssueSO
 ├─ faults      : FaultInjection[]     → module: ModuleType
 ├─ symptoms    : Symptom[]            (layman/technical — layman là input DUY NHẤT của Persona/CustomerAgent)
 ├─ clues       : DiagnosticClue[]     → revealedBy: DesktopActionType  ← trỏ sang DesktopActionSO, xem app.md
 └─ resolution  : ResolutionCondition  → symptomCleared/rootCauseFixed: StateCheck[]

DesktopActionSO
 ├─ targetModule  : ModuleType    ← xem app.md (mapping module ↔ app)
 ├─ preconditions : StateCheck[]
 └─ stateChanges  : FaultInjection[]
```

**Nguyên tắc:** `IssueSO`/`DesktopActionSO`/`PersonaProfileSO`/`StoreProfileSO` là SO tĩnh (đọc-only lúc runtime). `ProblemInstance`, `PersonaInstance` và mọi thứ bên trong `TicketState` là runtime state — được ghép ra (`ProblemGenerator`) từ các SO đó cộng với `VirtualDesktopInstance`, KHÔNG bao giờ ghi ngược state runtime vào SO (nguyên tắc bất biến #6 trong GDD chính).
