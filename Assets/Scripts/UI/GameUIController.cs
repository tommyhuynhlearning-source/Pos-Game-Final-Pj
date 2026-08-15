using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using POSTechSupport.Core;
using POSTechSupport.Data;
using POSTechSupport.Logic;
using POSTechSupport.Managers;

namespace POSTechSupport.UI
{
    /// <summary>
    /// Runtime UI driver for the playable slice (Hub → Night → Incoming call → Ticket window
    /// [chat | CRM + click-to-compare | remote connect] → Remote desktop apps → End/Win/Lose).
    /// All references are serialized and wired by GameSceneBuilder, so the whole UI lives in the scene
    /// at edit time. Dynamic lists (CRM results, action buttons) populate into the pre-placed containers.
    ///
    /// Covered fixes via generic actions: P1–P5. P6/P7 use the Terminal Wi-Fi picker + POS IP register
    /// rendered inline in the app body. Detailed per-app tabs (batch/DB/staff) are a later step.
    /// </summary>
    public class GameUIController : MonoBehaviour
    {
        [SerializeField] private GameManager game;

        [Header("Screens / overlays")]
        [SerializeField] private GameObject screenHub, screenNight, screenEndOfNight, screenGameOver, screenWin;
        [SerializeField] private GameObject overlayIncoming, overlayTicket, overlayRemote, appWindow;

        [Header("Hub")]
        [SerializeField] private Text hubStats;
        [SerializeField] private Button startNightBtn, newCampaignBtn;

        [Header("Night")]
        [SerializeField] private Text nightClock, nightCounts, callLogText;
        [SerializeField] private Transform devRow;
        [SerializeField] private Button toggleDevBtn;

        [Header("Incoming call")]
        [SerializeField] private Text incomingCaller;
        [SerializeField] private Image ringBar;
        [SerializeField] private Button answerBtn, declineBtn;

        [Header("Ticket — chat")]
        [SerializeField] private ScrollRect chatScroll;
        [SerializeField] private Text chatText, ticketHeader;
        [SerializeField] private Button askSymptom, askStore, askOwner, askMachine, askAuth, askSms;
        [SerializeField] private Button askWhenStarted, askWhatTried, askSure, askCode;
        [SerializeField] private Transform customerFacts;
        [SerializeField] private InputField chatInput;      // M4 — free-text; quick-asks are shortcuts
        [SerializeField] private Button chatSendBtn;

        [Header("Ticket — CRM")]
        [SerializeField] private InputField crmSearch;
        [SerializeField] private Button crmSearchBtn;
        [SerializeField] private Transform crmResults, crmRecord;
        [SerializeField] private Text compareStatus;

        [Header("Ticket — remote connect")]
        [SerializeField] private InputField remoteId, remotePass;
        [SerializeField] private Button connectBtn, openRemoteBtn;
        [SerializeField] private Text connectStatus, ticketStatusLine;
        [SerializeField] private Button hangUpBtn;

        [Header("Ticket — onboarding guidance (day <= first pool tier)")]
        [SerializeField] private GameObject guidancePanel;
        [SerializeField] private Text guidanceText;

        [Header("Remote desktop — the customer's Windows XP session")]
        [SerializeField] private Transform desktopIcons, appBody, appTabRow, taskbarApps, startMenuList;
        [SerializeField] private RectTransform appWindowRect, desktopArea;
        [SerializeField] private GameObject startMenu;
        [SerializeField] private Text appTitle, trayClock, connBarLabel;
        [SerializeField] private Image appTitleIcon;
        [SerializeField] private Button closeRemoteBtn, closeAppBtn, minimiseAppBtn, maximiseAppBtn, startBtn;

        [Header("Confirm dialog (risky fix / unverified refund-void)")]
        [SerializeField] private GameObject overlayConfirm;
        [SerializeField] private Text confirmText;
        [SerializeField] private Button confirmYes, confirmNo;

        [Header("Knowledge Base (agent's own tool, not the customer's desktop)")]
        [SerializeField] private GameObject overlayKb;
        [SerializeField] private InputField kbSearch;
        [SerializeField] private Button kbSearchBtn, openKbBtn, closeKbBtn;
        [SerializeField] private Transform kbCategories, kbResults;

        [Header("Mailbox")]
        [SerializeField] private GameObject overlayMailbox;
        [SerializeField] private Button openMailboxBtn, closeMailboxBtn;
        [SerializeField] private Transform mailboxList;

        [Header("End / Win / Lose")]
        [SerializeField] private Text eonSummary, gameOverText, winText;
        [SerializeField] private Button continueBtn, gameOverRestart, winRestart;

        private ProblemInstance Active => game != null ? game.Tickets?.active : null;
        private string openAppKey;

        // XP shell state: the window remembers where it was before it was maximised, and the taskbar
        // button for the open app doubles as the restore target once it is minimised.
        private bool appMaximised;
        private Vector2 restoreSize, restorePos;

        // Chat auto-follow: only snap to the newest line when one is actually added, so scrolling back
        // through the call survives the every-frame RefreshChatText.
        private int lastChatCount = -1;
        private bool stickChatToBottom;

        private static readonly string[] AppKeys = { "system", "network", "possoftware", "terminal", "printer", "devicemanager", "cashdrawer" };
        private static readonly Dictionary<string, (string title, ModuleType mod)> AppDefs = new()
        {
            { "system",       ("System (Windows)", ModuleType.OS) },
            { "network",      ("Network Settings", ModuleType.Network) },
            { "possoftware",  ("POS Manager", ModuleType.POSSoftware) },
            { "terminal",     ("POS Terminal", ModuleType.Terminal) },
            { "printer",      ("Printer & Print Queue", ModuleType.Printer) },
            { "devicemanager",("Device Manager", ModuleType.Printer) },
            { "cashdrawer",   ("Cash Drawer Config", ModuleType.CashDrawer) },
        };

        /// <summary>Sub-tabs per app (Docs/app.md). First entry is the default, matching TicketState.appTabs.</summary>
        private static readonly Dictionary<string, string[]> AppTabDefs = new()
        {
            { "system",       new[] { "health", "services" } },
            { "network",      new[] { "adapter" } },
            { "possoftware",  new[] { "receipt", "connections", "staff", "database" } },
            { "terminal",     new[] { "status", "batch" } },
            { "printer",      new[] { "queue" } },
            { "devicemanager",new[] { "printer" } },
            { "cashdrawer",   new[] { "port" } },
        };

        private static readonly Dictionary<string, string> TabLabels = new()
        {
            { "adapter", "Adapter" },   { "receipt", "Receipt Config" }, { "connections", "Connections" },
            { "staff", "Staff Mgmt" },  { "database", "Database" },      { "status", "Status / Network" },
            { "batch", "Batch" },       { "queue", "Print Queue" },      { "printer", "Printer Device" },
            { "port", "Port Config" },  { "health", "Health" },          { "services", "Services" },
        };

        private void Awake()
        {
            if (game == null) game = GetComponent<GameManager>();
            ApplyOSFontToAllTexts();
        }

        /// <summary>
        /// The scene is built with whatever font the editor machine had; this re-resolves it against the
        /// player's OS so Vietnamese diacritics survive. The XP desktop is skipped on purpose — its text
        /// is Tahoma (see XPFactory), and stamping the technician UI's font over it would flatten the one
        /// thing that makes the customer's machine look like a different computer.
        /// </summary>
        private void ApplyOSFontToAllTexts()
        {
            var texts = FindObjectsByType<Text>(FindObjectsInactive.Include);
            var targetFont = UIFactory.Font;
            var xpFont = XPFactory.Font;
            foreach (var t in texts)
            {
                if (t == null || t.font == xpFont) continue;
                t.font = targetFont;
            }
        }

        private void OnEnable()
        {
            if (game == null) return;
            game.IncomingCall += OnIncomingCall;
            game.NightEnded += OnNightEnded;
            game.GameFinished += OnGameFinished;
        }

        private void OnDisable()
        {
            if (game == null) return;
            game.IncomingCall -= OnIncomingCall;
            game.NightEnded -= OnNightEnded;
            game.GameFinished -= OnGameFinished;
        }

        private void Start()
        {
            WireStaticButtons();
            ShowScreen(screenHub);
            HideAll(overlayIncoming, overlayTicket, overlayRemote, appWindow, overlayConfirm, overlayKb, overlayMailbox);
            RenderHub();
        }

        private void Update()
        {
            if (game == null || game.Shift?.night == null || game.Shift.night.ended) return;
            if (screenNight != null && screenNight.activeSelf) RenderNightHud();
            if (overlayIncoming != null && overlayIncoming.activeSelf && ringBar != null)
                ringBar.fillAmount = game.Shift.RingFractionLeft();
            if (overlayTicket != null && overlayTicket.activeSelf && Active != null)
            {
                RenderTicketStatus();
                RefreshChatText();   // picks up a line the LLM rewrote after it was posted
            }
            if (overlayRemote != null && overlayRemote.activeSelf) RenderTrayClock();
        }

        /// <summary>
        /// Content height is one layout pass behind the text that was just set, so the scroll has to be
        /// pinned after the canvas rebuild — not inside RefreshChatText.
        /// </summary>
        private void LateUpdate()
        {
            if (!stickChatToBottom) return;
            stickChatToBottom = false;
            if (chatScroll == null) return;
            Canvas.ForceUpdateCanvases();
            chatScroll.verticalNormalizedPosition = 0f;
        }

        // ---------------------------------------------------------------- static wiring
        private void WireStaticButtons()
        {
            Wire(startNightBtn, () => { game.StartNight(); ShowScreen(screenNight); RenderNightHud(); });
            Wire(newCampaignBtn, () => { game.StartNewCampaign(); RenderHub(); });
            Wire(answerBtn, OnAnswer);
            Wire(declineBtn, () => { game.DeclineCall(); Hide(overlayIncoming); });
            Wire(hangUpBtn, OnHangUp);
            Wire(openRemoteBtn, OpenRemoteDesktop);
            Wire(closeRemoteBtn, CloseRemoteSession);
            Wire(closeAppBtn, CloseApp);
            Wire(minimiseAppBtn, MinimiseApp);
            Wire(maximiseAppBtn, ToggleMaximiseApp);
            Wire(startBtn, ToggleStartMenu);
            Wire(toggleDevBtn, () =>
            {
                if (devRow != null) devRow.gameObject.SetActive(!devRow.gameObject.activeSelf);
            });
            Wire(continueBtn, () => { ShowScreen(screenHub); RenderHub(); });
            Wire(gameOverRestart, () => { game.StartNewCampaign(); ShowScreen(screenHub); RenderHub(); });
            Wire(winRestart, () => { game.StartNewCampaign(); ShowScreen(screenHub); RenderHub(); });

            Wire(askSymptom, () => { game.Comms.AskSymptom(Active); RenderTicket(); });
            Wire(askStore, () => { game.Comms.AskStoreName(Active); RenderTicket(); });
            Wire(askOwner, () => { game.Comms.AskOwnerName(Active); RenderTicket(); });
            Wire(askMachine, () => { game.Comms.AskMachineId(Active); RenderTicket(); });
            Wire(askAuth, () => { game.Comms.AskAuthorized(Active); RenderTicket(); });
            Wire(askWhenStarted, () => { game.Comms.AskWhenStarted(Active); RenderTicket(); });
            Wire(askWhatTried, () => { game.Comms.AskWhatTried(Active); RenderTicket(); });
            Wire(askSms, () => { game.Comms.RequestSmsReceipt(Active); RenderTicket(); });
            Wire(askSure, () => { game.Comms.AskDoubleCheck(Active); RenderTicket(); });
            Wire(askCode, () => { game.Comms.AskSessionCode(Active); RenderTicket(); });
            Wire(chatSendBtn, SendTypedChat);
            if (chatInput != null) chatInput.onSubmit.AddListener(_ => SendTypedChat());   // Enter sends

            Wire(crmSearchBtn, () => { game.Verification.SetCrmQuery(Active, crmSearch != null ? crmSearch.text : ""); RenderCrm(); });
            Wire(connectBtn, OnConnect);

            Wire(openKbBtn, OpenKnowledgeBase);
            Wire(closeKbBtn, () => Hide(overlayKb));
            Wire(kbSearchBtn, () => RenderKbResults(game.Knowledge.SearchByErrorCode(kbSearch != null ? kbSearch.text.Trim() : "")));
            Wire(openMailboxBtn, OpenMailbox);
            Wire(closeMailboxBtn, () => Hide(overlayMailbox));
            Wire(confirmNo, () => { pendingConfirm = null; Hide(overlayConfirm); });
            Wire(confirmYes, () => { var act = pendingConfirm; pendingConfirm = null; Hide(overlayConfirm); act?.Invoke(); });

            // Dev force-call buttons (children of devRow named "Dev_<issueIds>", e.g. "Dev_P1", "Dev_P4,P1").
            if (devRow != null)
                foreach (Transform child in devRow)
                {
                    var btn = child.GetComponent<Button>();
                    if (btn == null || !child.name.StartsWith("Dev_")) continue;
                    string ids = child.name.Substring(4);
                    Wire(btn, () => ForceCall(ids));
                }
        }

        // ---------------------------------------------------------------- hub / night
        private void RenderHub()
        {
            if (hubStats == null || game == null || game.Campaign == null) return;
            var s = game.Campaign.state; var c = game.Campaign.config;
            hubStats.text =
                $"<b>Campaign Hub — Probation Tracker</b>\n\n" +
                $"Day: {s.day} / {c.totalDays}\n" +
                $"Tickets resolved: {s.ticketsResolved} / {c.minTotalTickets}\n" +
                $"Warnings: {s.warnings} / {c.warningsToGameOver}\n" +
                $"Paycheck: ${s.currency}";
        }

        private void RenderNightHud()
        {
            var shift = game.Shift;
            if (nightClock != null) nightClock.text = $"🕗 {shift.ClockLabel()}";
            var tickets = game.Tickets;
            if (nightCounts != null)
                nightCounts.text =
                    $"Day {shift.night.day}   |   Calls in: {shift.night.spawnedCount}/{shift.night.ticketsTarget}   |   " +
                    $"On hold: {tickets.queue.Count}   |   Strikes: {game.Mailbox.StrikeCount()}/{game.Campaign.config.strikesPerNightFail}\n" +
                    $"Handled: {tickets.CountBy(CallLifecycleStatus.Closed)}   |   " +
                    $"<color=#cc4444>Missed: {tickets.CountBy(CallLifecycleStatus.Missed)}</color>   |   " +
                    $"Other tech: {tickets.CountBy(CallLifecycleStatus.HandledByOtherTech)}";
            if (callLogText != null)
            {
                var sb = new System.Text.StringBuilder();
                // Newest first: on a busy night the log is long, and the last thing that happened is the
                // thing you want to see without scrolling.
                for (int i = tickets.history.Count - 1; i >= 0; i--)
                {
                    var p = tickets.history[i];
                    sb.AppendLine($"{p.ticket.ticketId}  {p.store.storeName}  —  {CallOutcomeLabel(p)}");
                }
                callLogText.text = tickets.history.Count == 0 ? "Waiting for the phone to ring…" : sb.ToString();
            }
        }

        // ---------------------------------------------------------------- incoming call
        private void OnIncomingCall(ProblemInstance p)
        {
            if (incomingCaller != null) incomingCaller.text = $"☎ Incoming Call\n\n{p.store.storeName}\nRinging…";
            Show(overlayIncoming);
        }

        private void OnAnswer()
        {
            Hide(overlayIncoming);
            game.AnswerCall();
            OpenTicketWindow();
        }

        private void ForceCall(string ids)
        {
            var issueIds = ids == "random" ? null : ids.Split(',');
            var p = game.ForceCall(issueIds);
            if (p != null) OpenTicketWindow();
        }

        // ---------------------------------------------------------------- ticket window
        private void OpenTicketWindow()
        {
            if (Active == null) return;
            if (ticketHeader != null) ticketHeader.text = $"☎ On call — {Active.store.storeName} — {Active.ticket.ticketId}";
            if (crmSearch != null) crmSearch.text = "";
            if (remoteId != null) remoteId.text = "";
            if (remotePass != null) remotePass.text = "";
            if (connectStatus != null) connectStatus.text = "";
            HideAll(overlayRemote, appWindow, overlayKb, overlayConfirm);
            ResetRemoteShell();
            lastChatCount = -1;   // new call → start the chat view at its newest line
            Show(overlayTicket);
            RenderTicket();
        }

        private void RenderTicket()
        {
            RenderChat();
            RenderCrm();
            RenderRemotePanel();
            RenderGuidance();
            RenderTicketStatus();
        }

        /// <summary>
        /// Training wheels: during the onboarding tier ProblemAssembler pre-attaches an article, so it
        /// renders here. Once it stops attaching, the panel simply disappears and the player has to go
        /// look things up themselves (Docs/manager.md KnowledgeBaseManager).
        /// </summary>
        private void RenderGuidance()
        {
            var article = Active != null ? Active.ticket.attachedArticle : null;
            if (guidancePanel != null) guidancePanel.SetActive(article != null);
            if (guidanceText != null && article != null)
                guidanceText.text = Active.desktop.Identity.Resolve($"<b>📄 {article.title}</b>\n{article.content}");
        }

        /// <summary>
        /// Text only, no button rebuild — safe to call every frame. Needed because the optional LLM pass
        /// rewrites an already-posted ChatLine in place after its request comes back.
        /// </summary>
        private void RefreshChatText()
        {
            if (chatText == null || Active == null) return;
            if (Active.ticket.chat.Count != lastChatCount)
            {
                lastChatCount = Active.ticket.chat.Count;
                stickChatToBottom = true;     // a new line arrived — follow it (LateUpdate, after layout)
            }
            var sb = new System.Text.StringBuilder();
            foreach (var line in Active.ticket.chat)
            {
                string who = line.kind switch
                {
                    ChatKind.Customer => $"<b>{Active.persona.name}:</b> ",
                    ChatKind.Sms => "<b>[SMS]</b> ",
                    ChatKind.System => "",
                    _ => "<b>You:</b> ",
                };
                sb.AppendLine(who + line.text);
            }
            chatText.text = sb.ToString();
        }

        private void RenderChat()
        {
            var t = Active.ticket;
            RefreshChatText();

            // Customer-stated facts become click-to-compare buttons (Chat side).
            if (customerFacts != null)
            {
                UIFactory.Clear(customerFacts);
                // LATEST wins, not first: once the caller has gone and checked, the chip has to hold what
                // they read out, or the player would be comparing a value the customer has retracted.
                var latest = new Dictionary<FactType, FactRef>();
                foreach (var line in t.chat)
                    if (line.fact != null) latest[line.fact.type] = line.fact;

                foreach (var fact in latest.Values)
                {
                    var b = UIFactory.Button($"chatfact_{fact.type}", customerFacts,
                        $"🔍 {FactLabel(fact.type)}: {fact.value}", UIFactory.Panel, 13);
                    UIFactory.MinHeight(b.gameObject, 26);
                    b.onClick.AddListener(() => { game.Verification.CompareClick(Active, CompareSource.Chat, fact.type, fact.value); RenderCrm(); });
                }
            }

            bool hung = t.authorization.customerHungUp;
            SetInteractable(askSymptom, !hung); SetInteractable(askStore, !hung); SetInteractable(askOwner, !hung);
            SetInteractable(askMachine, !hung); SetInteractable(askAuth, !hung); SetInteractable(askSms, !hung);
            SetInteractable(askWhenStarted, !hung); SetInteractable(askWhatTried, !hung);
            SetInteractable(askSure, !hung); SetInteractable(askCode, !hung);
            SetInteractable(chatSendBtn, !hung);
            if (chatInput != null) chatInput.interactable = !hung;
        }

        /// <summary>
        /// M4 free-text path. The reply is appended synchronously from the template, so the UI can render
        /// immediately; if the optional model is on, it rewrites that same line a moment later and the
        /// next render picks it up.
        /// </summary>
        private void SendTypedChat()
        {
            if (Active == null || chatInput == null) return;
            string text = chatInput.text;
            if (string.IsNullOrWhiteSpace(text)) return;
            game.Comms.SendChat(Active, text);
            chatInput.text = "";
            chatInput.ActivateInputField();
            RenderTicket();
        }

        private void RenderCrm()
        {
            var t = Active.ticket;
            if (compareStatus != null)
            {
                var cmp = t.compare;
                if (cmp.result != CompareResult.None)
                    compareStatus.text = CompareLine(cmp);
                else if (cmp.pending != null)
                    compareStatus.text = $"🔍 Selected {FactLabel(cmp.pendingType)}: \"{cmp.pending.value}\" — now click its counterpart.";
                else
                    compareStatus.text = "Search the CRM, then compare a record field against what the customer said.";
            }

            if (crmResults != null)
            {
                UIFactory.Clear(crmResults);
                for (int i = 0; i < t.crmLookup.results.Count; i++)
                {
                    var rec = t.crmLookup.results[i];
                    int idx = i;
                    var b = UIFactory.Button($"crm_{i}", crmResults, $"{rec.storeName}  ({rec.storeId} — {rec.address})",
                        t.crmLookup.selectedIndex == i ? UIFactory.Accent : UIFactory.Panel, 13);
                    UIFactory.MinHeight(b.gameObject, 28);
                    b.onClick.AddListener(() => { game.Verification.SelectCrmResult(Active, idx); FillCredentials(rec); ClearConnectFailure(); RenderCrm(); });
                }
                if (t.crmLookup.results.Count == 0 && !string.IsNullOrEmpty(t.crmLookup.query))
                    UIFactory.Label("nomatch", crmResults, "No matches.", 13, TextAnchor.MiddleLeft);
            }

            if (crmRecord != null)
            {
                UIFactory.Clear(crmRecord);
                int sel = t.crmLookup.selectedIndex;
                if (sel >= 0 && sel < t.crmLookup.results.Count)
                {
                    var rec = t.crmLookup.results[sel];
                    UIFactory.Label("rec_id", crmRecord, $"Store ID: {rec.storeId}", 13, TextAnchor.MiddleLeft);
                    CrmFactButton(rec, FactType.StoreName, $"Store Name: {rec.storeName}", rec.storeName);
                    UIFactory.Label("rec_addr", crmRecord, $"Address: {rec.address}", 13, TextAnchor.MiddleLeft);
                    CrmFactButton(rec, FactType.OwnerName, $"Owner: {rec.ownerName}", rec.ownerName);
                    CrmFactButton(rec, FactType.MachineId, $"Machine on file: {(rec.machines != null && rec.machines.Length > 0 ? rec.machines[0].machineId : "?")}",
                        rec.machines != null && rec.machines.Length > 0 ? rec.machines[0].machineId : "");
                    // No passcode here, ever. The session code lives on the customer's screen — printing
                    // one on file would be printing a credential the connect then refuses.
                    UIFactory.Label("rec_remote", crmRecord,
                        $"<b>Remote access</b>\nDevice ID: {rec.remoteId}\nSession passcode: ask the customer to read it out.",
                        13, TextAnchor.MiddleLeft);
                }
            }
        }

        /// <summary>
        /// A compare only ever proves that the record agrees with what the customer SAID — and what they
        /// said may be the shop two doors down. Reporting that as a bare "✓ MATCH" hands the player a
        /// green tick for the wrong account and then fails the connect on them, which reads as the game
        /// lying rather than as the trap it is. So the line always names its source, and says plainly
        /// when that source is a memory nobody has checked yet.
        /// </summary>
        private string CompareLine(CompareState cmp)
        {
            bool checkedIt = Active.ticket.dialogue.reChecked.Contains(cmp.resultType);
            string what = FactLabel(cmp.resultType);

            if (cmp.result == CompareResult.Match)
                return checkedIt
                    ? $"✓ MATCH — {what}: \"{cmp.crmValue}\", and the customer checked it. Right account."
                    : $"✓ Agrees with what the customer SAID — {what}: \"{cmp.crmValue}\".\nThey're going from memory. Ask them to double-check.";

            return checkedIt
                ? $"✗ MISMATCH — {what}: CRM \"{cmp.crmValue}\" vs \"{cmp.chatValue}\", which they checked. Wrong record."
                : $"✗ MISMATCH — {what}: CRM \"{cmp.crmValue}\" vs customer \"{cmp.chatValue}\".\nWrong record, or just their memory — ask them to double-check.";
        }

        private void CrmFactButton(StoreRecord rec, FactType type, string label, string value)
        {
            var b = UIFactory.Button($"crmfact_{type}", crmRecord, label, UIFactory.Panel, 13);
            UIFactory.MinHeight(b.gameObject, 26);
            b.onClick.AddListener(() => { game.Verification.CompareClick(Active, CompareSource.Crm, type, value); RenderCrm(); });
        }

        /// <summary>
        /// Picking a record copies its device ID into the connect form, the way an agent would paste it
        /// across. Retyping a nine-digit ID by hand tests typing, not verifying — one transposed digit
        /// and the player gets the same failure as picking the wrong shop, with no way to tell them
        /// apart. The decision this screen is about is WHICH RECORD. The passcode box is left alone:
        /// nothing on file can fill it, it comes from the customer.
        /// </summary>
        private void FillCredentials(StoreRecord rec)
        {
            if (remoteId != null) remoteId.text = rec.remoteId ?? "";
        }

        private void RenderRemotePanel()
        {
            SetInteractable(openRemoteBtn, Active.ticket.remoteConnect.connected);
        }

        /// <summary>
        /// A new CRM pick puts different credentials on screen, so the previous failure no longer refers
        /// to anything. A session that IS connected survives it — browsing the CRM never drops a call.
        /// </summary>
        private void ClearConnectFailure()
        {
            if (connectStatus != null && !Active.ticket.remoteConnect.connected) connectStatus.text = "";
        }

        private void OnConnect()
        {
            bool ok = game.Verification.TryRemoteConnect(Active, remoteId != null ? remoteId.text : "", remotePass != null ? remotePass.text : "");
            if (connectStatus != null)
                connectStatus.text = Active.ticket.remoteConnect.outcome switch
                {
                    RemoteConnectOutcome.Connected => "✓ Connected.",
                    RemoteConnectOutcome.PasscodeRejected =>
                        "Passcode rejected. Ask the customer to read the code on their screen.",
                    RemoteConnectOutcome.NoSession => Active.ticket.remoteConnect.passcodeMatched
                        ? "That code is valid — but not for this device. Wrong shop's machine?"
                        : "That device isn't waiting for a connection. Is it the machine that called you?",
                    _ => "No device found with that ID — check the digits.",
                };
            SetInteractable(openRemoteBtn, ok);
            if (ok) OpenRemoteDesktop();
        }

        private void RenderTicketStatus()
        {
            if (ticketStatusLine == null) return;
            var t = Active.ticket;
            if (t.authorization.customerHungUp)
            {
                ticketStatusLine.text = "Status: Customer hung up (unauthorized caller) — Hang Up to close.";
                return;
            }
            var verdict = ResolutionChecker.EvaluateTicket(Active);
            ticketStatusLine.text = verdict switch
            {
                TicketStatus.Resolved => "Status: Resolved — you can hang up cleanly now.",
                TicketStatus.Degraded => "Status: Degraded (made worse) — hanging up files a complaint.",
                _ => "Status: In Progress — hanging up now counts as abandoned.",
            };
        }

        private void OnHangUp()
        {
            game.HangUp();
            HideAll(overlayRemote, appWindow, overlayTicket, overlayKb, overlayConfirm);
            ResetRemoteShell();
            pendingConfirm = null;
            RenderNightHud();
        }

        // ---------------------------------------------------------------- remote desktop (Windows XP)

        /// <summary>
        /// Drops the player into the customer's XP session. The desktop is built once and reused for every
        /// call — only the connection bar caption and the taskbar change per ticket.
        /// </summary>
        private void OpenRemoteDesktop()
        {
            if (Active == null || !Active.ticket.remoteConnect.connected) return;
            Show(overlayRemote);
            Hide(startMenu);
            BuildDesktopIcons();
            if (connBarLabel != null)
            {
                // What mstsc shows: the host you are actually driving, not the ticket you opened it from.
                string machine = Active.desktop.GetModule(ModuleType.Terminal).Get("machineId");
                connBarLabel.text = $"{Active.desktop.Identity.slug}-{machine}  —  Remote Desktop Connection";
            }
            RenderTrayClock();
        }

        private void BuildDesktopIcons()
        {
            if (desktopIcons == null || desktopIcons.childCount > 0) return;
            var skin = XPSkin.Get();
            foreach (var key in AppKeys)
            {
                string k = key;
                var b = XPFactory.DesktopIcon($"icon_{key}", desktopIcons, AppDefs[key].title,
                    skin != null ? skin.Icon(key) : null);
                b.onClick.AddListener(() => OpenApp(k));
            }
            // My Computer is the shell icon for this machine, so it opens the machine's own app — the same
            // place XP puts System Properties. Nothing on this desktop is decoration: an icon that does
            // nothing when clicked is a dead end the player will find within seconds.
            var mine = XPFactory.DesktopIcon("icon_mycomputer", desktopIcons, "My Computer",
                skin != null ? skin.Icon("mycomputer") : null);
            mine.onClick.AddListener(() => OpenApp("system"));
        }

        /// <summary>Start ▸ Programs — the same app list as the desktop, for players who look there first.</summary>
        private void ToggleStartMenu()
        {
            if (startMenu == null) return;
            bool open = !startMenu.activeSelf;
            startMenu.SetActive(open);
            if (!open || startMenuList == null || startMenuList.childCount > 0) return;

            var skin = XPSkin.Get();
            foreach (var key in AppKeys)
            {
                string k = key;
                var b = StartMenuEntry(AppDefs[key].title, skin != null ? skin.Icon(key) : null);
                b.onClick.AddListener(() => { Hide(startMenu); OpenApp(k); });
            }
            var off = StartMenuEntry("Disconnect session", null);
            off.onClick.AddListener(() => { Hide(startMenu); CloseRemoteSession(); });
        }

        private Button StartMenuEntry(string label, Sprite icon)
        {
            var b = XPFactory.TaskButton("start_" + label.GetHashCode(), startMenuList, label, icon, false);
            var img = b.GetComponent<Image>();
            img.sprite = null;                       // flat menu row, not a taskbar chip
            img.color = new Color(1f, 1f, 1f, 0f);
            b.transition = Selectable.Transition.ColorTint;
            var colors = b.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = XPFactory.Hex(0x316AC5);
            colors.pressedColor = XPFactory.Hex(0x1F4C9E);
            colors.fadeDuration = 0.05f;
            b.colors = colors;
            var text = b.GetComponentInChildren<Text>();
            if (text != null) text.color = XPFactory.Ink;
            UIFactory.MinHeight(b.gameObject, 24f);
            return b;
        }

        private void OpenApp(string key)
        {
            openAppKey = key;
            Active.ticket.openAppKey = key;
            Show(appWindow);
            if (appWindowRect != null) appWindowRect.SetAsLastSibling();
            RebuildTaskbar();
            OpenTab(CurrentTab());
        }

        private void CloseApp()
        {
            Hide(appWindow);
            openAppKey = null;
            if (Active != null) Active.ticket.openAppKey = null;   // appTabs survive; the open app doesn't
            RebuildTaskbar();
        }

        /// <summary>Hides the window but keeps its taskbar button — clicking that brings it back.</summary>
        private void MinimiseApp()
        {
            if (openAppKey == null) return;
            Hide(appWindow);
            RebuildTaskbar();
        }

        private void RestoreApp()
        {
            if (openAppKey == null) return;
            Show(appWindow);
            if (appWindowRect != null) appWindowRect.SetAsLastSibling();
            RebuildTaskbar();
        }

        private void ToggleMaximiseApp()
        {
            if (appWindowRect == null || desktopArea == null) return;
            if (!appMaximised)
            {
                restoreSize = appWindowRect.sizeDelta;
                restorePos = appWindowRect.anchoredPosition;
                appWindowRect.sizeDelta = desktopArea.rect.size;
                appWindowRect.anchoredPosition = Vector2.zero;
            }
            else
            {
                appWindowRect.sizeDelta = restoreSize;
                appWindowRect.anchoredPosition = restorePos;
            }
            appMaximised = !appMaximised;
        }

        /// <summary>One button per open window. Only one app runs at a time, so this is a list of zero or one.</summary>
        private void RebuildTaskbar()
        {
            if (taskbarApps == null) return;
            UIFactory.Clear(taskbarApps);
            if (openAppKey == null) return;

            var skin = XPSkin.Get();
            bool focused = appWindow != null && appWindow.activeSelf;
            var task = XPFactory.TaskButton($"task_{openAppKey}", taskbarApps,
                AppDefs[openAppKey].title, skin != null ? skin.Icon(openAppKey) : null, focused);
            task.onClick.AddListener(() =>
            {
                if (appWindow != null && appWindow.activeSelf) MinimiseApp();
                else RestoreApp();
            });
        }

        /// <summary>Session ends: the desktop closes, but the ticket and the machine's state carry on.</summary>
        private void CloseRemoteSession()
        {
            Hide(startMenu);
            Hide(overlayRemote);
        }

        /// <summary>
        /// Tears the session down between calls. The desktop icons are kept (they are the same seven apps
        /// every time) but the taskbar, Start menu and window geometry belong to the call that opened them.
        /// </summary>
        private void ResetRemoteShell()
        {
            openAppKey = null;
            Hide(startMenu);
            if (taskbarApps != null) UIFactory.Clear(taskbarApps);
            if (appMaximised && appWindowRect != null)
            {
                appWindowRect.sizeDelta = restoreSize;
                appWindowRect.anchoredPosition = restorePos;
            }
            appMaximised = false;
        }

        private void RenderTrayClock()
        {
            if (trayClock == null || game?.Shift == null) return;
            trayClock.text = game.Shift.ClockLabel();
        }

        /// <summary>Current sub-tab of the open app, persisted in TicketState.appTabs across open/close.</summary>
        private string CurrentTab()
        {
            var tabs = AppTabDefs[openAppKey];
            return Active.ticket.appTabs.TryGetValue(openAppKey, out var t) && System.Array.IndexOf(tabs, t) >= 0
                ? t : tabs[0];
        }

        private void OpenTab(string tab)
        {
            Active.ticket.appTabs[openAppKey] = tab;
            game.Actions.AutoRevealApp(Active, openAppKey, tab);
            RenderAppBody();
        }

        private void RenderAppBody()
        {
            if (openAppKey == null || appBody == null || Active == null) return;
            var def = AppDefs[openAppKey];
            string tab = CurrentTab();
            if (appTitle != null) appTitle.text = $"{def.title}  -  {TabLabels[tab]}";
            if (appTitleIcon != null)
            {
                var skin = XPSkin.Get();
                appTitleIcon.sprite = skin != null ? skin.Icon(openAppKey) : null;
                appTitleIcon.enabled = appTitleIcon.sprite != null;
            }

            RenderTabRow(tab);
            UIFactory.Clear(appBody);

            var es = Active.desktop.EffectiveStatus(def.mod);
            var statusColor = es.status == Status.OK ? XPFactory.Ink : XPFactory.InkRed;
            XPFactory.Label("appstatus", appBody, $"<b>{def.mod} status: {es.status}</b>{(string.IsNullOrEmpty(es.reason) ? "" : "\n" + es.reason)}",
                14, TextAnchor.UpperLeft, statusColor);

            RenderActionsForTab(openAppKey, tab);
            RenderTabExtras(openAppKey, tab);
            RenderSessionLog();
        }

        private void RenderTabRow(string current)
        {
            if (appTabRow == null) return;
            var tabs = AppTabDefs[openAppKey];
            appTabRow.gameObject.SetActive(tabs.Length > 1);
            UIFactory.Clear(appTabRow);
            if (tabs.Length <= 1) return;
            foreach (var t in tabs)
            {
                string key = t;
                var b = XPFactory.Button($"tab_{t}", appTabRow, TabLabels[t], 12);
                // The current tab is drawn pressed in — XP's property-sheet tabs, minus the notch.
                if (t == current)
                {
                    var skin = XPSkin.Get();
                    var img = b.GetComponent<Image>();
                    if (skin != null && skin.buttonPressed != null) img.sprite = skin.buttonPressed;
                }
                UIFactory.MinHeight(b.gameObject, 22);
                XPFactory.FitWidth(b);
                b.onClick.AddListener(() => OpenTab(key));
            }
        }

        /// <summary>Actions hosted by this app; an action with no appTab shows on every tab of the app.</summary>
        private void RenderActionsForTab(string appKey, string tab)
        {
            var all = game?.Content?.allActions;
            if (all == null) return;
            foreach (var a in all)
            {
                if (a == null || a.appKey != appKey) continue;
                if (!string.IsNullOrEmpty(a.appTab) && a.appTab != tab) continue;
                var action = a;
                var b = XPFactory.Button($"act_{action.actionId}", appBody, action.actionId + (action.isRisky ? "  (risky)" : ""),
                    13, action.isRisky ? XPFactory.InkRed : XPFactory.Ink);
                UIFactory.MinHeight(b.gameObject, 28);
                bool canRun = game.Actions.CanExecute(Active.desktop, action);
                // Anything hosted by POS Manager ▸ Database reads stored records, so it needs the DB up
                // (Docs/app.md: "Reprint đọc receiptSnapshot từ history, cần DB connection").
                if (appKey == "possoftware" && tab == "database") canRun &= Active.desktop.graph.DbConnected().ok;
                b.interactable = canRun;
                b.onClick.AddListener(() =>
                {
                    // A risky fix can inject worseningFaults when it isn't the real root cause —
                    // Docs/manager.md ActionManager puts that confirm on the UI.
                    if (action.isRisky)
                        Confirm($"\"{action.actionId}\" is a RISKY fix.\n\nIf this isn't the real root cause it can break " +
                                "something else and drag the ticket down to Degraded.\n\nRun it anyway?", () => RunAction(action.actionId));
                    else RunAction(action.actionId);
                });
            }
        }

        private void RunAction(string actionId)
        {
            game.Actions.RunAction(Active, actionId);
            RenderAppBody();
            RenderTicketStatus();
        }

        private void RenderTabExtras(string appKey, string tab)
        {
            switch (appKey)
            {
                case "system" when tab == "health":       RenderSystemHealth(); break;
                case "system" when tab == "services":     RenderSystemServices(); break;
                case "terminal" when tab == "status":     RenderTerminalStatus(); break;
                case "terminal" when tab == "batch":      RenderTerminalBatch(); break;
                case "possoftware" when tab == "receipt": RenderReceiptConfig(); break;
                case "possoftware" when tab == "connections": RenderConnections(); break;
                case "possoftware" when tab == "staff":   RenderStaffManagement(); break;
                case "possoftware" when tab == "database":RenderDatabase(); break;
            }
        }

        // --- System ▸ Health / Services (P13–P16) ---------------------------------------------------
        private void RenderSystemHealth()
        {
            var os = Active.desktop.GetModule(ModuleType.OS);
            bool blocking = Active.desktop.graph.OsBlocking(out string why);
            XPFactory.Label("sys_health", appBody,
                $"System drive: {Flag(os.Get("diskSpace"), "OK")}\n" +
                $"Pending restart: {Flag(os.Get("pendingReboot"), "false")}\n" +
                $"System clock: {Flag(os.Get("systemTime"), "OK")}" +
                (blocking ? $"\n\n<color={XPFactory.TagRed}>Machine-wide fault ({why}) — everything downstream reads Blocked until this clears.</color>" : ""),
                13, TextAnchor.UpperLeft);
        }

        private void RenderSystemServices()
        {
            var os = Active.desktop.GetModule(ModuleType.OS);
            XPFactory.Label("sys_services", appBody,
                $"Print Spooler: {Flag(os.Get("spoolerService"), "Running")}\n" +
                "<i>A stopped service is reported as an Error on the module that needed it, not as Blocked — " +
                "so the printer still shows you what's wrong.</i>", 13, TextAnchor.UpperLeft);
        }

        private static string Flag(string value, string healthy) =>
            value == healthy ? value : $"<color={XPFactory.TagRed}>{value}</color>";

        // --- Terminal ▸ Status (P6) ----------------------------------------------------------------
        private void RenderTerminalStatus()
        {
            var info = Active.desktop.graph.TerminalNetInfo();
            string joined = Active.desktop.GetModule(ModuleType.Terminal).Get("wifiNetwork");
            XPFactory.Label("netinfo", appBody,
                $"Joined Wi-Fi: <b>{joined}</b>\nIP (from DHCP): {info.ip}\nGateway: {info.gateway}", 13, TextAnchor.UpperLeft);

            XPFactory.Label("wifihdr", appBody, "Join Wi-Fi network:", 13, TextAnchor.MiddleLeft);
            foreach (var ssid in Simulation.WifiTable.NearbyNetworks(Active.desktop.Identity))
            {
                string s = ssid;
                var b = XPFactory.Button($"wifi_{ssid}", appBody, ssid + (s == joined ? "  ✓" : ""), 12);
                UIFactory.MinHeight(b.gameObject, 26);
                b.onClick.AddListener(() =>
                {
                    Active.desktop.GetModule(ModuleType.Terminal).Set("wifiNetwork", s);
                    game.Desktop.OnFixApplied(Active);
                    RenderAppBody(); RenderTicketStatus();
                });
            }
        }

        // --- Terminal ▸ Batch (Docs/app.md Transaction data model) ---------------------------------
        private void RenderTerminalBatch()
        {
            var tx = Active.transactions;
            var auth = Active.ticket.authorization;
            XPFactory.Label("batchhdr", appBody,
                $"<b>Batch #{tx.batchId}</b> — Void only while Open; Refund works after Settle too.\n" +
                (auth.confirmed
                    ? $"<color={XPFactory.TagGreen}>Caller authorization: CONFIRMED.</color>"
                    : $"<color={XPFactory.TagAmber}>Caller authorization: NOT confirmed — verify the owner name or ask before touching money.</color>"),
                13, TextAnchor.UpperLeft);

            for (int i = 0; i < tx.live.Count; i++)
            {
                int idx = i;
                var t = tx.live[i];
                XPFactory.Label($"tx_{i}", appBody, $"#{i + 1}  {t.type}  ${t.amount:0.00}  —  {t.status}", 13, TextAnchor.MiddleLeft);
                if (t.status == TransStatus.Open) TxButton("Void", idx, TransType.Void);
                if (t.status is TransStatus.Open or TransStatus.Settled) TxButton("Refund", idx, TransType.Refund);
            }
            if (tx.live.Count == 0) XPFactory.Label("tx_none", appBody, "Batch is empty.", 13, TextAnchor.MiddleLeft);

            var sale = XPFactory.Button("tx_sale", appBody, "Authorize new sale  $5.00", 12);
            UIFactory.MinHeight(sale.gameObject, 26);
            sale.onClick.AddListener(() =>
            {
                game.Transactions.Authorize(Active, 5.00, TransType.Sale);
                Log("New sale authorized — $5.00 held in this batch.");
                RenderAppBody();
            });

            var close = XPFactory.Button("tx_close", appBody, "Close batch (settle)", 12);
            UIFactory.MinHeight(close.gameObject, 26);
            close.onClick.AddListener(() => Confirm(
                "Closing the batch settles every Open transaction. After that they can only be REFUNDED, never voided.\n\nClose it?",
                () =>
                {
                    game.Transactions.CloseBatch(Active);
                    Log("Batch settled — a new batch is now open.");
                    RenderAppBody();
                }));
        }

        private void TxButton(string label, int index, TransType action)
        {
            var b = XPFactory.Button($"tx_{action}_{index}", appBody, $"   {label} #{index + 1}", 12,
                action == TransType.Refund ? XPFactory.InkRed : XPFactory.Ink);
            UIFactory.MinHeight(b.gameObject, 24);
            b.onClick.AddListener(() =>
            {
                if (Active.ticket.authorization.confirmed) { DoTransaction(index, action, false); return; }
                Confirm($"You have NOT verified that this caller is authorized.\n\nProcessing a {action} for someone who " +
                        "turns out to be unauthorized is a harm event — the ticket gets capped at Degraded even if every " +
                        "technical issue is fixed.\n\nProceed anyway?", () => DoTransaction(index, action, true));
            });
        }

        private void DoTransaction(int index, TransType action, bool proceedUnconfirmed)
        {
            var res = game.Transactions.TryTransaction(Active, index, action, proceedUnconfirmed);
            if (!string.IsNullOrEmpty(res.log)) Log(res.log);
            if (res.unauthorizedHarm)
                Log("⚠ That caller was never authorized — this ticket is now capped at Degraded.");
            RenderAppBody();
            RenderTicketStatus();
        }

        // --- POS Manager ▸ Receipt Config (P5) -----------------------------------------------------
        private void RenderReceiptConfig()
        {
            var pos = Active.desktop.GetModule(ModuleType.POSSoftware);
            bool broken = pos.Get("receiptTemplate") == "Broken";
            XPFactory.Label("rcfg", appBody, $"Receipt template: <b>{pos.Get("receiptTemplate")}</b>", 13, TextAnchor.MiddleLeft);

            var tpl = FindTemplate(ReceiptType.Customer);
            if (tpl?.fields != null)
            {
                var sb = new System.Text.StringBuilder("<b>Customer copy — field mapping</b>\n");
                foreach (var f in tpl.fields)
                {
                    // A broken template is exactly "a required field fell out of the mapping" (GDD §14 P5).
                    bool dropped = broken && f.required && f.label == "Total";
                    sb.AppendLine(dropped ? $"  <color={XPFactory.TagRed}>{f.label} — MISSING from mapping</color>" : $"  {f.label}");
                }
                XPFactory.Label("rtpl", appBody, sb.ToString(), 13, TextAnchor.UpperLeft);
            }
        }

        private ReceiptTemplateSO FindTemplate(ReceiptType type)
        {
            var all = game?.Content?.receiptTemplates;
            if (all == null) return null;
            foreach (var t in all) if (t != null && t.type == type) return t;
            return null;
        }

        // --- POS Manager ▸ Connections (P7 + DB host) ----------------------------------------------
        private void RenderConnections()
        {
            var pos = Active.desktop.GetModule(ModuleType.POSSoftware);
            string actualIp = Active.desktop.graph.TerminalNetInfo().ip;
            string registered = pos.Get("registeredTerminalIp");
            bool ipStale = actualIp != registered;

            XPFactory.Label("conn_ip", appBody,
                $"<b>Registered terminal roster</b>\nTerminal's actual IP: {actualIp}\nRegistered on POS: " +
                (ipStale ? $"<color={XPFactory.TagRed}>{registered} (stale)</color>" : registered), 13, TextAnchor.UpperLeft);

            var reg = XPFactory.Button("registerip", appBody, $"Re-register terminal at {actualIp}", 12);
            UIFactory.MinHeight(reg.gameObject, 28);
            reg.interactable = ipStale;
            reg.onClick.AddListener(() =>
            {
                pos.Set("registeredTerminalIp", actualIp);
                game.Desktop.OnFixApplied(Active);
                Log($"POS now has {actualIp} registered for this terminal.");
                RenderAppBody(); RenderTicketStatus();
            });

            var db = Active.desktop.graph.DbConnected();
            XPFactory.Label("conn_db", appBody,
                $"<b>Database</b>\nHost: {pos.Get("dbHost")}\nStatus: " +
                (db.ok ? $"<color={XPFactory.TagGreen}>connected</color>" : $"<color={XPFactory.TagRed}>{db.reason}</color>"),
                13, TextAnchor.UpperLeft);

            string correctDbHost = Active.desktop.Identity.dbHost;
            if (!db.ok && pos.Get("dbHost") != correctDbHost)
            {
                var fix = XPFactory.Button("fixdbhost", appBody,
                    $"Point DB host back to {correctDbHost}", 12);
                UIFactory.MinHeight(fix.gameObject, 28);
                fix.onClick.AddListener(() =>
                {
                    pos.Set("dbHost", correctDbHost);
                    game.Desktop.OnFixApplied(Active);
                    Log("DB host corrected.");
                    RenderAppBody(); RenderTicketStatus();
                });
            }
        }

        // --- POS Manager ▸ Staff Management (GDD §15) ----------------------------------------------
        private void RenderStaffManagement()
        {
            var pos = Active.desktop.GetModule(ModuleType.POSSoftware);
            var login = Active.desktop.graph.StaffLoginStatus();
            string thisMachine = Active.desktop.GetModule(ModuleType.Terminal).Get("machineId");
            XPFactory.Label("staff_state", appBody,
                $"<b>Staff account</b>  <i>(this register: {thisMachine})</i>\nRole: {Blank(pos.Get("staffRole"))}\n" +
                $"Assigned terminal: {Blank(pos.Get("staffTerminal"))}\nSynced to terminal: {pos.Get("terminalSynced")}\n\nLogin check: " +
                (login.ok ? $"<color={XPFactory.TagGreen}>OK</color>" : $"<color={XPFactory.TagRed}>{login.reason}</color>"),
                13, TextAnchor.UpperLeft);

            StaffFix("Assign role: Sale", () => pos.Set("staffRole", "Sale"));
            StaffFix($"Assign to this terminal ({thisMachine})", () => pos.Set("staffTerminal", thisMachine));
            StaffFix("Sync POS → terminal", () => pos.Set("terminalSynced", "true"));

            // GDD §15's trap: handing out Admin "to save time" grants refund/void/close-batch rights.
            var admin = XPFactory.Button("staff_admin", appBody, "Assign role: Admin  (risky — over-privileged)", 12, XPFactory.InkRed);
            UIFactory.MinHeight(admin.gameObject, 28);
            admin.onClick.AddListener(() => Confirm(
                "Admin includes refund, void and close-batch rights — far more than a new hire needs.\n\n" +
                "Grant it anyway?", () =>
                {
                    pos.Set("staffRole", "Admin");
                    game.Desktop.OnFixApplied(Active);
                    Log("⚠ Staff granted Admin — over-privileged for a sales role.");
                    RenderAppBody(); RenderTicketStatus();
                }));
        }

        private void StaffFix(string label, System.Action apply)
        {
            var b = XPFactory.Button("staff_" + label.GetHashCode(), appBody, label, 12);
            UIFactory.MinHeight(b.gameObject, 26);
            b.onClick.AddListener(() =>
            {
                apply();
                game.Desktop.OnFixApplied(Active);
                Log(label + " — done.");
                RenderAppBody(); RenderTicketStatus();
            });
        }

        private static string Blank(string v) => string.IsNullOrEmpty(v) || v == "None" ? "<i>(not set)</i>" : v;

        // --- POS Manager ▸ Database (archive + reprint) ---------------------------------------------
        private void RenderDatabase()
        {
            var db = Active.desktop.graph.DbConnected();
            XPFactory.Label("db_state", appBody, db.ok
                ? "<b>Transaction history</b> — reprints read the stored receipt snapshot."
                : $"<b>Transaction history</b>\n<color={XPFactory.TagRed}>Unavailable — {db.reason}</color>", 13, TextAnchor.UpperLeft);
            if (!db.ok) return;

            foreach (var record in Active.transactions.archive)
            {
                var rec = record;
                XPFactory.Label($"arc_{rec.GetHashCode()}", appBody,
                    $"{rec.day}  {rec.type}  ${rec.amount:0.00}  —  {rec.status}" +
                    (string.IsNullOrEmpty(rec.lastPrintResult) ? "" : $"\n    last reprint: {rec.lastPrintResult}"),
                    13, TextAnchor.MiddleLeft);
                var b = XPFactory.Button($"reprint_{rec.GetHashCode()}", appBody, "   Reprint customer copy", 12);
                UIFactory.MinHeight(b.gameObject, 24);
                b.onClick.AddListener(() =>
                {
                    bool ok = game.Transactions.Reprint(Active, rec, ReceiptType.Customer, out string reason);
                    Log(ok ? $"Reprint OK — {rec.day} {rec.type} ${rec.amount:0.00}."
                           : $"Reprint FAILED — {reason}");
                    RenderAppBody(); RenderTicketStatus();
                });
            }
        }

        private void RenderSessionLog()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in Active.ticket.sessionLog)
            {
                string tag = l.kind switch { SessionLogKind.Clue => "🔎", SessionLogKind.RedHerring => "⚠", SessionLogKind.Result => "•", _ => "" };
                sb.AppendLine($"{tag} {l.text}");
            }
            XPFactory.Label("sessionlog", appBody, "<b>Session log</b>\n" + sb, 13, TextAnchor.UpperLeft);
        }

        private void Log(string text) =>
            Active.ticket.sessionLog.Add(new SessionLogLine { kind = SessionLogKind.Result, text = text });

        // ---------------------------------------------------------------- confirm dialog
        private System.Action pendingConfirm;

        /// <summary>Shared yes/no gate for anything irreversible: risky fixes, unverified money moves.</summary>
        private void Confirm(string message, System.Action onYes)
        {
            if (overlayConfirm == null) { onYes(); return; }   // no dialog wired → don't block the player
            pendingConfirm = onYes;
            if (confirmText != null) confirmText.text = message;
            Show(overlayConfirm);
        }

        // ---------------------------------------------------------------- knowledge base
        private void OpenKnowledgeBase()
        {
            Show(overlayKb);
            if (kbCategories != null && kbCategories.childCount == 0)
                foreach (IssueCategory c in System.Enum.GetValues(typeof(IssueCategory)))
                {
                    var cat = c;
                    var b = UIFactory.Button($"kbcat_{c}", kbCategories, c.ToString(), UIFactory.Panel, 12);
                    UIFactory.MinHeight(b.gameObject, 24);
                    b.onClick.AddListener(() => RenderKbResults(game.Knowledge.SearchByCategory(cat)));
                }
            RenderKbResults(null);
        }

        private void RenderKbResults(KnowledgeArticleSO[] results)
        {
            if (kbResults == null) return;
            UIFactory.Clear(kbResults);
            if (results == null)
            {
                UIFactory.Label("kb_hint", kbResults, "Pick a category, or search by error code (e.g. \"39\").", 13, TextAnchor.UpperLeft);
                return;
            }
            if (results.Length == 0)
            {
                UIFactory.Label("kb_none", kbResults, "No articles match.", 13, TextAnchor.UpperLeft);
                return;
            }
            foreach (var a in results)
                UIFactory.Label($"kb_{a.articleId}", kbResults,
                    Active.desktop.Identity.Resolve($"<b>{a.articleId} — {a.title}</b>\n{a.content}"),
                    13, TextAnchor.UpperLeft);
        }

        // ---------------------------------------------------------------- mailbox
        private void OpenMailbox()
        {
            Show(overlayMailbox);
            if (mailboxList == null) return;
            UIFactory.Clear(mailboxList);
            var mails = game.Mailbox.nightMails;
            if (mails.Count == 0)
            {
                UIFactory.Label("mail_none", mailboxList, "Inbox is empty tonight. Keep it that way.", 13, TextAnchor.UpperLeft);
                return;
            }
            for (int i = 0; i < mails.Count; i++)
                UIFactory.Label($"mail_{i}", mailboxList,
                    $"<b>✉ {mails[i].subject}</b>\n{mails[i].body}\n<i>({mails[i].cause.type})</i>", 13, TextAnchor.UpperLeft);
            UIFactory.Label("mail_count", mailboxList,
                $"\n<b>{mails.Count} / {game.Campaign.config.strikesPerNightFail} strikes</b> — hitting the limit fails the night.",
                13, TextAnchor.UpperLeft);
        }

        // ---------------------------------------------------------------- end / win / lose
        private void OnNightEnded(ScoreBreakdown score, bool nightFailed)
        {
            if (eonSummary != null)
                eonSummary.text =
                    $"<b>End of Night — Day {game.Shift.night.day}</b>\n\n" +
                    $"Calls tonight: {game.Tickets.history.Count}\n" +
                    $"Resolved cleanly: {score.resolvedCount}\n" +
                    $"Degraded / made worse: {score.degradedCount}\n" +
                    $"Missed while you were free: {game.Tickets.CountBy(CallLifecycleStatus.Missed)}\n" +
                    $"Taken by another tech: {game.Tickets.CountBy(CallLifecycleStatus.HandledByOtherTech)}\n" +
                    $"Complaint mails: {game.Mailbox.StrikeCount()}\n" +
                    $"Paycheck: ${game.Campaign.state.currency}\n\n" +
                    (nightFailed ? $"<color=#cc4444>Night FAILED ({game.Mailbox.StrikeCount()} strikes) — +1 warning.</color>" : "Night passed.");
            HideAll(overlayIncoming, overlayTicket, overlayRemote, appWindow, overlayConfirm, overlayKb, overlayMailbox);
            ResetRemoteShell();
            ShowScreen(screenEndOfNight);
        }

        private void OnGameFinished(GameResult result)
        {
            if (result == GameResult.Win)
            {
                if (winText != null) winText.text = $"You completed probation and hit quota ({game.Campaign.state.ticketsResolved} tickets). Hired!";
                ShowScreen(screenWin);
            }
            else
            {
                if (gameOverText != null) gameOverText.text = $"You've been let go. Warnings: {game.Campaign.state.warnings}.";
                ShowScreen(screenGameOver);
            }
        }

        // ---------------------------------------------------------------- helpers
        private void ShowScreen(GameObject screen)
        {
            HideAll(screenHub, screenNight, screenEndOfNight, screenGameOver, screenWin);
            Show(screen);
        }

        private static void Show(GameObject go) { if (go != null) go.SetActive(true); }
        private static void Hide(GameObject go) { if (go != null) go.SetActive(false); }
        private static void HideAll(params GameObject[] gos) { foreach (var g in gos) Hide(g); }
        private static void SetInteractable(Button b, bool v) { if (b != null) b.interactable = v; }
        private static void Wire(Button b, UnityEngine.Events.UnityAction a) { if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(a); } }

        /// <summary>What the call log shows per closed call — lifecycle first, outcome only when judged.</summary>
        private static string CallOutcomeLabel(ProblemInstance p) => p.ticket.lifecycle switch
        {
            CallLifecycleStatus.HandledByOtherTech => "→ another tech took it",
            CallLifecycleStatus.Missed => "<color=#cc4444>✗ missed</color>",
            CallLifecycleStatus.Abandoned => "<color=#cc4444>✗ abandoned mid-call</color>",
            CallLifecycleStatus.Closed => p.ticket.closedOutcome switch
            {
                ClosedOutcome.Resolved => "<color=#66bb66>✓ resolved</color>",
                ClosedOutcome.Degraded => "<color=#cc4444>✗ degraded</color>",
                ClosedOutcome.Unauthorized => "⊘ unauthorized caller — refused",
                _ => p.ticket.closedOutcome.ToString(),
            },
            _ => p.ticket.lifecycle.ToString(),
        };

        private static string FactLabel(FactType t) => t switch
        {
            FactType.StoreName => "Store Name",
            FactType.OwnerName => "Owner Name",
            FactType.MachineId => "Machine ID",
            _ => t.ToString(),
        };
    }
}
