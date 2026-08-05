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

        [Header("Incoming call")]
        [SerializeField] private Text incomingCaller;
        [SerializeField] private Image ringBar;
        [SerializeField] private Button answerBtn, declineBtn;

        [Header("Ticket — chat")]
        [SerializeField] private Text chatText, ticketHeader;
        [SerializeField] private Button askSymptom, askStore, askOwner, askMachine, askAuth, askSms;
        [SerializeField] private Button askWhenStarted, askWhatTried;
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

        [Header("Remote desktop")]
        [SerializeField] private Transform appIcons, appBody, appTabRow;
        [SerializeField] private Text appTitle;
        [SerializeField] private Button closeRemoteBtn, closeAppBtn;

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

        private void ApplyOSFontToAllTexts()
        {
            var texts = FindObjectsByType<Text>(FindObjectsInactive.Include);
            var targetFont = UIFactory.Font;
            foreach (var t in texts)
            {
                if (t != null)
                {
                    t.font = targetFont;
                }
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
            Wire(closeRemoteBtn, () => Hide(overlayRemote));
            Wire(closeAppBtn, () =>
            {
                Hide(appWindow);
                openAppKey = null;
                if (Active != null) Active.ticket.openAppKey = null;   // appTabs survive; the open app doesn't
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
            if (nightCounts != null)
                nightCounts.text = $"Day {shift.night.day}   |   Calls: {shift.night.spawnedCount}   |   " +
                                   $"Waiting: {game.Tickets.queue.Count + (game.Tickets.ringing != null ? 1 : 0)}   |   " +
                                   $"Strikes: {game.Mailbox.StrikeCount()}/{game.Campaign.config.strikesPerNightFail}";
            if (callLogText != null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var p in game.Tickets.history)
                    sb.AppendLine($"{p.ticket.ticketId}  {p.store.storeName}  —  {p.ticket.lifecycle}/{p.ticket.closedOutcome}");
                callLogText.text = game.Tickets.history.Count == 0 ? "Waiting for the phone to ring…" : sb.ToString();
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
            openAppKey = null;
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
                var seen = new HashSet<FactType>();
                foreach (var line in t.chat)
                {
                    if (line.fact == null || seen.Contains(line.fact.type)) continue;
                    seen.Add(line.fact.type);
                    var fact = line.fact;
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
                    compareStatus.text = $"{(cmp.result == CompareResult.Match ? "✓ MATCH" : "✗ MISMATCH")} — {FactLabel(cmp.resultType)}: CRM \"{cmp.crmValue}\" vs customer \"{cmp.chatValue}\"";
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
                    b.onClick.AddListener(() => { game.Verification.SelectCrmResult(Active, idx); RenderCrm(); });
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
                    string passcode = Active.IsCallerRecord(rec) ? t.remoteConnect.passcode : rec.fixedPasscode;
                    UIFactory.Label("rec_id", crmRecord, $"Store ID: {rec.storeId}", 13, TextAnchor.MiddleLeft);
                    CrmFactButton(rec, FactType.StoreName, $"Store Name: {rec.storeName}", rec.storeName);
                    UIFactory.Label("rec_addr", crmRecord, $"Address: {rec.address}", 13, TextAnchor.MiddleLeft);
                    CrmFactButton(rec, FactType.OwnerName, $"Owner: {rec.ownerName}", rec.ownerName);
                    CrmFactButton(rec, FactType.MachineId, $"Machine on file: {(rec.machines != null && rec.machines.Length > 0 ? rec.machines[0].machineId : "?")}",
                        rec.machines != null && rec.machines.Length > 0 ? rec.machines[0].machineId : "");
                    UIFactory.Label("rec_remote", crmRecord, $"<b>Remote credentials</b>\nRemote ID: {rec.remoteId}\nPasscode: {passcode}", 13, TextAnchor.MiddleLeft);
                }
            }
        }

        private void CrmFactButton(StoreRecord rec, FactType type, string label, string value)
        {
            var b = UIFactory.Button($"crmfact_{type}", crmRecord, label, UIFactory.Panel, 13);
            UIFactory.MinHeight(b.gameObject, 26);
            b.onClick.AddListener(() => { game.Verification.CompareClick(Active, CompareSource.Crm, type, value); RenderCrm(); });
        }

        private void RenderRemotePanel()
        {
            if (remoteId != null && string.IsNullOrEmpty(remoteId.text)) { /* leave for player */ }
            SetInteractable(openRemoteBtn, Active.ticket.remoteConnect.connected);
        }

        private void OnConnect()
        {
            bool ok = game.Verification.TryRemoteConnect(Active, remoteId != null ? remoteId.text : "", remotePass != null ? remotePass.text : "");
            if (connectStatus != null)
                connectStatus.text = ok ? "✓ Connected." : "Connection failed — wrong Remote ID/passcode. Did you pick the right CRM record?";
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
            openAppKey = null;
            pendingConfirm = null;
            RenderNightHud();
        }

        // ---------------------------------------------------------------- remote desktop
        private void OpenRemoteDesktop()
        {
            if (Active == null || !Active.ticket.remoteConnect.connected) return;
            Show(overlayRemote);
            if (appIcons != null && appIcons.childCount == 0)
                foreach (var key in AppKeys)
                {
                    string k = key;
                    var b = UIFactory.Button($"icon_{key}", appIcons, AppDefs[key].title, UIFactory.Panel, 13);
                    UIFactory.MinHeight(b.gameObject, 30);
                    b.onClick.AddListener(() => OpenApp(k));
                }
        }

        private void OpenApp(string key)
        {
            openAppKey = key;
            Active.ticket.openAppKey = key;
            Show(appWindow);
            OpenTab(CurrentTab());
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
            if (appTitle != null) appTitle.text = $"{def.title}  ▸  {TabLabels[tab]}";

            RenderTabRow(tab);
            UIFactory.Clear(appBody);

            var es = Active.desktop.EffectiveStatus(def.mod);
            var statusColor = es.status == Status.OK ? UIFactory.Ink : UIFactory.Danger;
            UIFactory.Label("appstatus", appBody, $"<b>{def.mod} status: {es.status}</b>{(string.IsNullOrEmpty(es.reason) ? "" : "\n" + es.reason)}",
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
                var b = UIFactory.Button($"tab_{t}", appTabRow, TabLabels[t],
                    t == current ? UIFactory.Accent : UIFactory.Panel, 12);
                UIFactory.MinHeight(b.gameObject, 24);
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
                var b = UIFactory.Button($"act_{action.actionId}", appBody, action.actionId + (action.isRisky ? "  (risky)" : ""),
                    action.isRisky ? UIFactory.Danger : UIFactory.Accent, 13);
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
            UIFactory.Label("sys_health", appBody,
                $"System drive: {Flag(os.Get("diskSpace"), "OK")}\n" +
                $"Pending restart: {Flag(os.Get("pendingReboot"), "false")}\n" +
                $"System clock: {Flag(os.Get("systemTime"), "OK")}" +
                (blocking ? $"\n\n<color=#cc4444>Machine-wide fault ({why}) — everything downstream reads Blocked until this clears.</color>" : ""),
                13, TextAnchor.UpperLeft);
        }

        private void RenderSystemServices()
        {
            var os = Active.desktop.GetModule(ModuleType.OS);
            UIFactory.Label("sys_services", appBody,
                $"Print Spooler: {Flag(os.Get("spoolerService"), "Running")}\n" +
                "<i>A stopped service is reported as an Error on the module that needed it, not as Blocked — " +
                "so the printer still shows you what's wrong.</i>", 13, TextAnchor.UpperLeft);
        }

        private static string Flag(string value, string healthy) =>
            value == healthy ? value : $"<color=#cc4444>{value}</color>";

        // --- Terminal ▸ Status (P6) ----------------------------------------------------------------
        private void RenderTerminalStatus()
        {
            var info = Active.desktop.graph.TerminalNetInfo();
            string joined = Active.desktop.GetModule(ModuleType.Terminal).Get("wifiNetwork");
            UIFactory.Label("netinfo", appBody,
                $"Joined Wi-Fi: <b>{joined}</b>\nIP (from DHCP): {info.ip}\nGateway: {info.gateway}", 13, TextAnchor.UpperLeft);

            UIFactory.Label("wifihdr", appBody, "Join Wi-Fi network:", 13, TextAnchor.MiddleLeft);
            foreach (var ssid in Simulation.WifiTable.NearbyNetworks(Active.desktop.Identity))
            {
                string s = ssid;
                var b = UIFactory.Button($"wifi_{ssid}", appBody, ssid + (s == joined ? "  ✓" : ""), UIFactory.Panel, 12);
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
            UIFactory.Label("batchhdr", appBody,
                $"<b>Batch #{tx.batchId}</b> — Void only while Open; Refund works after Settle too.\n" +
                (auth.confirmed
                    ? "<color=#66bb66>Caller authorization: CONFIRMED.</color>"
                    : "<color=#cc8844>Caller authorization: NOT confirmed — verify the owner name or ask before touching money.</color>"),
                13, TextAnchor.UpperLeft);

            for (int i = 0; i < tx.live.Count; i++)
            {
                int idx = i;
                var t = tx.live[i];
                UIFactory.Label($"tx_{i}", appBody, $"#{i + 1}  {t.type}  ${t.amount:0.00}  —  {t.status}", 13, TextAnchor.MiddleLeft);
                if (t.status == TransStatus.Open) TxButton("Void", idx, TransType.Void);
                if (t.status is TransStatus.Open or TransStatus.Settled) TxButton("Refund", idx, TransType.Refund);
            }
            if (tx.live.Count == 0) UIFactory.Label("tx_none", appBody, "Batch is empty.", 13, TextAnchor.MiddleLeft);

            var sale = UIFactory.Button("tx_sale", appBody, "Authorize new sale  $5.00", UIFactory.Panel, 12);
            UIFactory.MinHeight(sale.gameObject, 26);
            sale.onClick.AddListener(() =>
            {
                game.Transactions.Authorize(Active, 5.00, TransType.Sale);
                Log("New sale authorized — $5.00 held in this batch.");
                RenderAppBody();
            });

            var close = UIFactory.Button("tx_close", appBody, "Close batch (settle)", UIFactory.Panel, 12);
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
            var b = UIFactory.Button($"tx_{action}_{index}", appBody, $"   {label} #{index + 1}",
                action == TransType.Refund ? UIFactory.Danger : UIFactory.Panel, 12);
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
            UIFactory.Label("rcfg", appBody, $"Receipt template: <b>{pos.Get("receiptTemplate")}</b>", 13, TextAnchor.MiddleLeft);

            var tpl = FindTemplate(ReceiptType.Customer);
            if (tpl?.fields != null)
            {
                var sb = new System.Text.StringBuilder("<b>Customer copy — field mapping</b>\n");
                foreach (var f in tpl.fields)
                {
                    // A broken template is exactly "a required field fell out of the mapping" (GDD §14 P5).
                    bool dropped = broken && f.required && f.label == "Total";
                    sb.AppendLine(dropped ? $"  <color=#cc4444>{f.label} — MISSING from mapping</color>" : $"  {f.label}");
                }
                UIFactory.Label("rtpl", appBody, sb.ToString(), 13, TextAnchor.UpperLeft);
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

            UIFactory.Label("conn_ip", appBody,
                $"<b>Registered terminal roster</b>\nTerminal's actual IP: {actualIp}\nRegistered on POS: " +
                (ipStale ? $"<color=#cc4444>{registered} (stale)</color>" : registered), 13, TextAnchor.UpperLeft);

            var reg = UIFactory.Button("registerip", appBody, $"Re-register terminal at {actualIp}", UIFactory.Panel, 12);
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
            UIFactory.Label("conn_db", appBody,
                $"<b>Database</b>\nHost: {pos.Get("dbHost")}\nStatus: " +
                (db.ok ? "<color=#66bb66>connected</color>" : $"<color=#cc4444>{db.reason}</color>"), 13, TextAnchor.UpperLeft);

            string correctDbHost = Active.desktop.Identity.dbHost;
            if (!db.ok && pos.Get("dbHost") != correctDbHost)
            {
                var fix = UIFactory.Button("fixdbhost", appBody,
                    $"Point DB host back to {correctDbHost}", UIFactory.Panel, 12);
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
            UIFactory.Label("staff_state", appBody,
                $"<b>Staff account</b>  <i>(this register: {thisMachine})</i>\nRole: {Blank(pos.Get("staffRole"))}\n" +
                $"Assigned terminal: {Blank(pos.Get("staffTerminal"))}\nSynced to terminal: {pos.Get("terminalSynced")}\n\nLogin check: " +
                (login.ok ? "<color=#66bb66>OK</color>" : $"<color=#cc4444>{login.reason}</color>"), 13, TextAnchor.UpperLeft);

            StaffFix("Assign role: Sale", () => pos.Set("staffRole", "Sale"));
            StaffFix($"Assign to this terminal ({thisMachine})", () => pos.Set("staffTerminal", thisMachine));
            StaffFix("Sync POS → terminal", () => pos.Set("terminalSynced", "true"));

            // GDD §15's trap: handing out Admin "to save time" grants refund/void/close-batch rights.
            var admin = UIFactory.Button("staff_admin", appBody, "Assign role: Admin  (risky — over-privileged)", UIFactory.Danger, 12);
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
            var b = UIFactory.Button("staff_" + label.GetHashCode(), appBody, label, UIFactory.Panel, 12);
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
            UIFactory.Label("db_state", appBody, db.ok
                ? "<b>Transaction history</b> — reprints read the stored receipt snapshot."
                : $"<b>Transaction history</b>\n<color=#cc4444>Unavailable — {db.reason}</color>", 13, TextAnchor.UpperLeft);
            if (!db.ok) return;

            foreach (var record in Active.transactions.archive)
            {
                var rec = record;
                UIFactory.Label($"arc_{rec.GetHashCode()}", appBody,
                    $"{rec.day}  {rec.type}  ${rec.amount:0.00}  —  {rec.status}" +
                    (string.IsNullOrEmpty(rec.lastPrintResult) ? "" : $"\n    last reprint: {rec.lastPrintResult}"),
                    13, TextAnchor.MiddleLeft);
                var b = UIFactory.Button($"reprint_{rec.GetHashCode()}", appBody, "   Reprint customer copy", UIFactory.Panel, 12);
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
            UIFactory.Label("sessionlog", appBody, "<b>Session log</b>\n" + sb, 13, TextAnchor.UpperLeft);
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
                    $"Complaint mails: {game.Mailbox.StrikeCount()}\n" +
                    $"Paycheck: ${game.Campaign.state.currency}\n\n" +
                    (nightFailed ? $"<color=#cc4444>Night FAILED ({game.Mailbox.StrikeCount()} strikes) — +1 warning.</color>" : "Night passed.");
            HideAll(overlayIncoming, overlayTicket, overlayRemote, appWindow, overlayConfirm, overlayKb, overlayMailbox);
            openAppKey = null;
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

        private static string FactLabel(FactType t) => t switch
        {
            FactType.StoreName => "Store Name",
            FactType.OwnerName => "Owner Name",
            FactType.MachineId => "Machine ID",
            _ => t.ToString(),
        };
    }
}
