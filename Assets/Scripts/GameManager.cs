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

        private void BuildServices()
        {
            var cfg = content.config;
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
