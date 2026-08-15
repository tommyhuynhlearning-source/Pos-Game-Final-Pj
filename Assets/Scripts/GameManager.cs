using System;
using UnityEngine;
using POSTechSupport.AI;
using POSTechSupport.Core;
using POSTechSupport.Data;
using POSTechSupport.Logic;
using POSTechSupport.Managers;

namespace POSTechSupport
{
    /// <summary>
    /// Single orchestrator that owns every manager and drives the night loop (Docs/manager.md
    /// "Luồng gọi tổng quát"). The GDD lists managers as MonoBehaviours; here they're plain,
    /// testable service classes owned by this one MonoBehaviour, which provides the Unity lifecycle
    /// (Awake/Update) and the campaign→shift→campaign flow. UI binds to the events + action methods.
    ///
    /// Scope: M1–M3 + M5–M6. M4 (Customer AI) is a canned-line placeholder (CommunicationManager);
    /// M7 (Voice) is not present.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [SerializeField] private ContentDatabaseSO content;
        public ContentDatabaseSO Content => content;

        [Header("Call volume — override for THIS scene")]
        [Tooltip("Off = dùng nguyên GameConfig asset (dùng chung cho mọi scene). On = scene này tự " +
                 "quyết số call, GameConfig không bị sửa — GameManager làm việc trên một bản sao runtime.")]
        [SerializeField] private bool overrideCallVolume;

        [Tooltip("Số call mỗi GIỜ in-game. Ca đêm dài 8 giờ (20:00→04:00) nên 0.25 ≈ 2 call/đêm. " +
                 "Bật override là bỏ luôn ramp theo ngày: mọi đêm giữ đúng nhịp này.")]
        [Min(0f)][SerializeField] private float callsPerHour = 0.25f;

        [Tooltip("Số call CẢ CA, chốt cứng. > 0 sẽ đè lên callsPerHour. Để 0 = tính từ callsPerHour.")]
        [Min(0)][SerializeField] private int callsPerShift;

        /// <summary>
        /// The config every service actually runs on — the shared asset, or a runtime clone carrying
        /// this scene's call-volume override. Never the asset when <see cref="overrideCallVolume"/> is on,
        /// so tuning a scene can't dirty Assets/Content/Generated/GameConfig.asset.
        /// </summary>
        public GameConfigSO Config { get; private set; }

        // --- Services -----------------------------------------------------------------------------
        public CampaignManager Campaign { get; private set; }
        public ConsequenceManager Consequence { get; private set; }
        public SaveManager Save { get; private set; }
        public MailboxManager Mailbox { get; private set; }
        public ScoreManager Scorer { get; private set; }
        public ProblemGenerator Generator { get; private set; }
        public DesktopManager Desktop { get; private set; }
        public ActionManager Actions { get; private set; }
        public VerificationManager Verification { get; private set; }
        public TransactionManager Transactions { get; private set; }
        public DialogueManager Dialogue { get; private set; }
        public CommunicationManager Comms { get; private set; }
        public KnowledgeBaseManager Knowledge { get; private set; }
        public TicketManager Tickets { get; private set; }
        public ShiftManager Shift { get; private set; }

        // --- Events for UI ------------------------------------------------------------------------
        public event Action<ProblemInstance> IncomingCall;
        public event Action<ScoreBreakdown, bool> NightEnded;
        public event Action<GameResult> GameFinished;

        private bool nightRunning;

        private void Awake()
        {
            if (content == null)
            {
                Debug.LogError("[GameManager] ContentDatabase not assigned.");
                enabled = false;
                return;
            }
            BuildServices();
            LoadOrNew();
        }

        /// <summary>
        /// Always a runtime clone of the asset, never the asset itself. Cloning unconditionally (rather
        /// than only when the override is on) buys two things: the shared GameConfig can never be dirtied
        /// by a scene tweak, and <see cref="overrideCallVolume"/> becomes a live toggle — the services
        /// below hold this one reference for the whole session, so there is nothing to hot-swap later.
        /// </summary>
        private GameConfigSO ResolveConfig()
        {
            var asset = content.config;
            if (asset == null) return null;

            var clone = Instantiate(asset);
            clone.name = asset.name + " (scene)";
            ApplyCallVolume(clone);
            return clone;
        }

        /// <summary>
        /// Folds the two Inspector knobs into the fields <see cref="GameConfigSO.CallsForNight"/> reads.
        /// callsPerShift wins by pinning the min/max clamp to one number; otherwise the rate is used and
        /// the day ramp is zeroed, so an explicitly authored rate stays what it says on every night.
        /// With the override off, the asset's own four values are copied back, so unticking restores the
        /// authored volume instead of leaving the last override stuck on the clone.
        /// </summary>
        private void ApplyCallVolume(GameConfigSO cfg)
        {
            var src = content != null ? content.config : null;
            if (cfg == null || src == null) return;

            if (!overrideCallVolume)
            {
                cfg.callsPerHour = src.callsPerHour;
                cfg.callsPerHourPerDay = src.callsPerHourPerDay;
                cfg.minCallsPerNight = src.minCallsPerNight;
                cfg.maxCallsPerNight = src.maxCallsPerNight;
                return;
            }

            if (callsPerShift > 0)
            {
                cfg.minCallsPerNight = callsPerShift;
                cfg.maxCallsPerNight = callsPerShift;
                return;
            }

            cfg.callsPerHour = callsPerHour;
            cfg.callsPerHourPerDay = 0f;
            cfg.minCallsPerNight = 0;
            cfg.maxCallsPerNight = 0;          // 0 = no ceiling, the rate alone decides
        }

        /// <summary>
        /// Lets the knobs be dragged during play: the clone is re-stamped immediately, and ShiftManager
        /// re-reads it in BeginShift, so an edit lands on the next night that starts. The night already
        /// running rolled its whole spawn schedule up front and is deliberately left alone.
        /// </summary>
        private void OnValidate()
        {
            if (Application.isPlaying) ApplyCallVolume(Config);
        }

        private void BuildServices()
        {
            var cfg = Config = ResolveConfig();
            Save = new SaveManager();
            Consequence = new ConsequenceManager();
            Campaign = new CampaignManager(cfg);
            Mailbox = new MailboxManager();
            Scorer = new ScoreManager();
            Knowledge = new KnowledgeBaseManager(content);          // built before the generator: it is
            Generator = new ProblemGenerator(content, Knowledge);   // the assembler's guidance source
            Generator.EnableRecurring(Consequence.DueRecurringToday, Consequence.ConsumeRecurring);
            Desktop = new DesktopManager();
            Actions = new ActionManager(content, Desktop);
            Verification = new VerificationManager();
            Transactions = new TransactionManager();
            // M4: template phrasing by default; the local model is opt-in and can only reword what
            // DialoguePolicy already decided (GDD §13 / nguyên tắc bất biến #7).
            ILlmClient llm = cfg != null && cfg.useLlm
                ? new OllamaLlmClient(cfg.llmEndpoint, cfg.llmModel, cfg.llmTimeoutSec)
                : new TemplateLlmClient();
            Dialogue = new DialogueManager(llm, this);
            Comms = new CommunicationManager(Dialogue);
            Tickets = new TicketManager(Mailbox);
            Shift = new ShiftManager(cfg, Generator, Tickets, Mailbox, Scorer);

            Shift.OnIncomingCall += p => IncomingCall?.Invoke(p);
            Shift.OnNightEnded += HandleNightEnded;
        }

        private void LoadOrNew()
        {
            var loaded = Save.Load();
            if (loaded.HasValue)
            {
                Campaign.state = loaded.Value.campaign;
                Consequence.ledger = loaded.Value.ledger;
            }
            else
            {
                Campaign.StartNewCampaign();
            }
        }

        private void Update()
        {
            if (nightRunning && Shift.night != null && !Shift.night.ended)
                Shift.Tick(Time.deltaTime);
        }

        // --- Flow API (UI calls these) ------------------------------------------------------------
        public void StartNight()
        {
            Shift.BeginShift(Campaign.state.day);
            nightRunning = true;
        }

        private void HandleNightEnded(ScoreBreakdown score, bool nightFailed)
        {
            nightRunning = false;
            Consequence.Commit(Tickets.history, Campaign.state.day);
            var result = Campaign.OnNightEnded(score, nightFailed);
            Save.Persist(Campaign.state, Consequence.ledger);
            NightEnded?.Invoke(score, nightFailed);
            if (result != GameResult.None) GameFinished?.Invoke(result);
        }

        public void AnswerCall()
        {
            Tickets.Answer(Shift.night.elapsed);
            if (Tickets.active != null) Comms.OpenCall(Tickets.active);
        }

        public void DeclineCall() => Tickets.MissRinging("declined by agent");

        public ClosedOutcome HangUp() => Tickets.CloseActive();

        public void StartNewCampaign()
        {
            Campaign.StartNewCampaign();
            Consequence.ledger = new ConsequenceLedger();
            Save.ResetSave();
        }

        /// <summary>
        /// Dev helper (prototype "Force this call now"): jump straight into a chosen ticket.
        /// A null issueIds means "roll a normal one for today".
        /// </summary>
        public ProblemInstance ForceCall(string[] issueIds)
        {
            if (Shift.night == null || Tickets.active != null || Tickets.ringing != null) return null;
            var p = issueIds == null
                ? Generator.GenerateAuto(Shift.night.day)
                : Generator.GenerateForced(Shift.night.day, issueIds);
            Tickets.active = p;
            p.ticket.lifecycle = CallLifecycleStatus.Active;
            p.ticket.answeredAtElapsed = Shift.night.elapsed;
            Comms.OpenCall(p);
            return p;
        }
    }
}
