using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using POSTechSupport.Data;
using POSTechSupport.UI;

namespace POSTechSupport.EditorTools
{
    /// <summary>
    /// Builds the entire playable UI into the OPEN scene as persistent, editable GameObjects (not
    /// runtime-generated), then wires GameManager + GameUIController references. Menu: POS ▸ Build Game
    /// Scene. Re-runnable — it removes a previous GameCanvas/GameManager/EventSystem first.
    /// </summary>
    public static class GameSceneBuilder
    {
        [MenuItem("POS/Build Game Scene")]
        public static void Build()
        {
            RemoveExisting("GameCanvas");
            RemoveExisting("GameSystem");
            RemoveExisting("EventSystem");

            // --- Event system (Input System module — project uses new input) ---------------------
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            // --- Canvas --------------------------------------------------------------------------
            var canvasGo = new GameObject("GameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 540);   // smaller ref res => larger, readable text
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            UIFactory.Stretch(UIFactory.Box("Background", root, UIFactory.Bg).rectTransform);

            // --- GameManager + UI controller -----------------------------------------------------
            var system = new GameObject("GameSystem", typeof(GameManager), typeof(GameUIController));
            var controller = system.GetComponent<GameUIController>();
            var so = new SerializedObject(controller);

            // Assign ContentDatabase to GameManager (if generated).
            var db = AssetDatabase.LoadAssetAtPath<ContentDatabaseSO>("Assets/Content/Generated/ContentDatabase.asset");
            var gmSo = new SerializedObject(system.GetComponent<GameManager>());
            if (db != null) gmSo.FindProperty("content").objectReferenceValue = db;
            gmSo.ApplyModifiedPropertiesWithoutUndo();
            Set(so, "game", system.GetComponent<GameManager>());

            // ================= Screens =================
            BuildHub(root, so);
            BuildNight(root, so);
            BuildIncoming(root, so);
            BuildTicket(root, so);
            BuildRemote(root, so);
            BuildEnd(root, so);
            BuildKnowledgeBase(root, so);
            BuildMailbox(root, so);
            BuildConfirm(root, so);     // last = topmost sibling, so it draws over every other overlay

            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Selection.activeObject = system;
            Debug.Log(db != null
                ? "[GameSceneBuilder] Scene built + ContentDatabase wired. Press Play."
                : "[GameSceneBuilder] Scene built. Run POS ▸ Generate Sample Content, then re-run this to wire content.");
        }

        // ---------------------------------------------------------------- screens
        private static void BuildHub(Transform root, SerializedObject so)
        {
            var scr = Screen("Screen_Hub", root, true);
            Set(so, "screenHub", scr);
            var stats = Label(scr.transform, "Stats", "", 22, new Vector2(0.25f, 0.35f), new Vector2(0.75f, 0.9f));
            Set(so, "hubStats", stats);
            Set(so, "startNightBtn", Btn(scr.transform, "StartNight", "Start Night ▶", UIFactory.Accent, new Vector2(0.35f, 0.2f), new Vector2(0.65f, 0.28f)));
            Set(so, "newCampaignBtn", Btn(scr.transform, "NewCampaign", "New Campaign", UIFactory.Panel, new Vector2(0.35f, 0.1f), new Vector2(0.65f, 0.18f)));
            // Call volume is a dev tunable, not a player-facing setting: GameConfigSO.callsPerHour in the
            // Inspector (GameConfig.asset). No in-game control on purpose.
        }

        private static void BuildNight(Transform root, SerializedObject so)
        {
            var scr = Screen("Screen_Night", root, false);
            Set(so, "screenNight", scr);
            Set(so, "nightClock", Label(scr.transform, "Clock", "🕗 8:00 PM", 24, new Vector2(0.03f, 0.9f), new Vector2(0.3f, 0.98f)));
            // Two lines now (volume + outcome tally), so it needs the height and a smaller size.
            Set(so, "nightCounts", Label(scr.transform, "Counts", "", 14, new Vector2(0.3f, 0.87f), new Vector2(0.69f, 0.99f)));
            Set(so, "openMailboxBtn", Btn(scr.transform, "OpenMailbox", "✉ Mailbox", UIFactory.Panel, new Vector2(0.84f, 0.91f), new Vector2(0.97f, 0.98f)));

            // The log grows one line per call, so it scrolls instead of spilling out of its panel.
            var logContent = ScrollPanel(scr.transform, "CallLogPanel", UIFactory.Panel,
                new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.88f), out _, 4, new RectOffset(10, 16, 10, 10));
            Set(so, "callLogText", UIFactory.Label("CallLog", logContent, "Waiting for the phone to ring…", 15));

            // The force-call strip is a debug tool, not part of the game — hidden until asked for, so it
            // stops crowding the night screen.
            Set(so, "toggleDevBtn", Btn(scr.transform, "ToggleDev", "🛠 Dev", UIFactory.Panel, new Vector2(0.70f, 0.91f), new Vector2(0.82f, 0.98f)));
            var dev = Panel(scr.transform, "DevRow", new Color(0, 0, 0, 0), new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.16f));
            // A GRID, not a horizontal strip: twenty buttons in a HorizontalLayoutGroup get squeezed to a
            // few pixels each and every label breaks one character per line. The grid wraps instead.
            UIFactory.Grid(dev.gameObject, new Vector2(78, 26), 4, new RectOffset(4, 4, 4, 4));
            Set(so, "devRow", dev.transform);   // without this the force-call buttons never get wired
            // 40 issues won't fit on one strip — these are the ones worth reaching for by hand: one per
            // discrimination pair, plus the blocker chains. Everything else comes from "random".
            foreach (var ids in new[] { "random", "P1", "P2", "P3", "P5", "P8", "P13", "P14", "P16",
                                        "P17", "P18", "P21", "P26", "P29", "P36", "P37", "P39",
                                        "P4,P1", "P14,P2", "P15,P32" })
                UIFactory.Button("Dev_" + ids, dev.transform, ids, UIFactory.Panel, 12);
            dev.gameObject.SetActive(false);   // 🛠 Dev toggles it; GameUIController still wires it while hidden
        }

        private static void BuildIncoming(Transform root, SerializedObject so)
        {
            var ov = Screen("Overlay_IncomingCall", root, false);
            ov.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);
            Set(so, "overlayIncoming", ov);
            var win = Panel(ov.transform, "Window", UIFactory.Panel, new Vector2(0.38f, 0.35f), new Vector2(0.62f, 0.65f));
            Set(so, "incomingCaller", Label(win.transform, "Caller", "☎ Incoming Call", 20, new Vector2(0, 0.5f), new Vector2(1, 1)));
            var bar = UIFactory.Box("RingBar", win.transform, UIFactory.Accent);
            UIFactory.Anchor(bar.rectTransform, new Vector2(0.05f, 0.42f), new Vector2(0.95f, 0.48f), Vector2.zero, Vector2.zero);
            bar.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            bar.type = Image.Type.Filled; bar.fillMethod = Image.FillMethod.Horizontal; bar.fillAmount = 1f;
            Set(so, "ringBar", bar);
            Set(so, "answerBtn", Btn(win.transform, "Answer", "Answer", UIFactory.Accent, new Vector2(0.08f, 0.1f), new Vector2(0.48f, 0.32f)));
            Set(so, "declineBtn", Btn(win.transform, "Decline", "Decline", UIFactory.Danger, new Vector2(0.52f, 0.1f), new Vector2(0.92f, 0.32f)));
        }

        private static void BuildTicket(Transform root, SerializedObject so)
        {
            var ov = Screen("Overlay_Ticket", root, false);
            ov.GetComponent<Image>().color = new Color(0, 0, 0, 0.55f);
            Set(so, "overlayTicket", ov);
            var win = Panel(ov.transform, "Window", UIFactory.Panel, new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.95f));

            Set(so, "ticketHeader", Label(win.transform, "Header", "On call", 18, new Vector2(0.01f, 0.93f), new Vector2(0.99f, 0.99f), 6));

            // --- Left: chat ---
            var left = Panel(win.transform, "Chat", new Color(0, 0, 0, 0.2f), new Vector2(0.01f, 0.12f), new Vector2(0.34f, 0.92f));
            Label(left.transform, "Title", "Customer Chat", 14, new Vector2(0, 0.94f), new Vector2(1, 1), 6);

            // The conversation is unbounded, so the log lives in a scroll view (GameUIController keeps it
            // pinned to the newest line). Before this it was a fixed Text that just overflowed its panel.
            var chatContent = ScrollPanel(left.transform, "ChatScroll", new Color(0, 0, 0, 0.15f),
                new Vector2(0.02f, 0.50f), new Vector2(0.98f, 0.93f), out var chatScroll, 4, new RectOffset(6, 14, 6, 6));
            Set(so, "chatScroll", chatScroll);
            Set(so, "chatText", UIFactory.Label("ChatLog", chatContent, "", 14));

            // Facts scroll too: one per answered question, each as wide as its label needs.
            var factsContent = ScrollPanel(left.transform, "CustomerFacts", new Color(0, 0, 0, 0.12f),
                new Vector2(0.02f, 0.36f), new Vector2(0.98f, 0.49f), out _, 3, new RectOffset(4, 14, 4, 4));
            Set(so, "customerFacts", factsContent);

            // M4: type anything. The quick-asks below are shortcuts into the same pipeline, kept because
            // they double as a discoverability aid for what the customer can actually be asked.
            Set(so, "chatInput", Inp(left.transform, "ChatInput", "Type what you want to ask…",
                new Vector2(0.02f, 0.29f), new Vector2(0.74f, 0.35f)));
            Set(so, "chatSendBtn", Btn(left.transform, "ChatSend", "Send", UIFactory.Accent,
                new Vector2(0.76f, 0.29f), new Vector2(0.98f, 0.35f)));

            // Two columns — eight of these will not fit stacked in the space the chat log needs.
            var colL = Panel(left.transform, "AskCol1", new Color(0, 0, 0, 0), new Vector2(0.02f, 0.01f), new Vector2(0.49f, 0.28f));
            var colR = Panel(left.transform, "AskCol2", new Color(0, 0, 0, 0), new Vector2(0.51f, 0.01f), new Vector2(0.98f, 0.28f));
            UIFactory.VList(colL.gameObject, 2, new RectOffset(0, 2, 2, 2));
            UIFactory.VList(colR.gameObject, 2, new RectOffset(2, 0, 2, 2));
            Set(so, "askSymptom", AskBtn(colL.transform, "AskSymptom", "What's wrong?"));
            Set(so, "askStore", AskBtn(colL.transform, "AskStore", "Store name?"));
            Set(so, "askOwner", AskBtn(colL.transform, "AskOwner", "Who am I speaking to?"));
            Set(so, "askMachine", AskBtn(colL.transform, "AskMachine", "Which register?"));
            // Sits under the three identity questions because it only ever refers to one of them.
            Set(so, "askSure", AskBtn(colL.transform, "AskSure", "Are you sure?"));
            Set(so, "askAuth", AskBtn(colR.transform, "AskAuth", "Owner authorized?"));
            Set(so, "askWhenStarted", AskBtn(colR.transform, "AskWhenStarted", "When did it start?"));
            Set(so, "askWhatTried", AskBtn(colR.transform, "AskWhatTried", "Tried anything?"));
            Set(so, "askSms", AskBtn(colR.transform, "AskSms", "Text me the receipt"));
            // The session passcode exists nowhere but the customer's screen, so asking for it is a
            // first-class step of the connect flow, not an afterthought.
            Set(so, "askCode", AskBtn(colR.transform, "AskCode", "Read me the code on screen"));

            // --- Mid: CRM ---
            var mid = Panel(win.transform, "CRM", new Color(0, 0, 0, 0.2f), new Vector2(0.35f, 0.12f), new Vector2(0.66f, 0.92f));
            Label(mid.transform, "Title", "CRM — Account Lookup", 14, new Vector2(0, 0.94f), new Vector2(1, 1), 6);
            Set(so, "crmSearch", Inp(mid.transform, "Search", "Store ID or name…", new Vector2(0.02f, 0.87f), new Vector2(0.72f, 0.93f)));
            Set(so, "crmSearchBtn", Btn(mid.transform, "SearchBtn", "Search", UIFactory.Accent, new Vector2(0.74f, 0.87f), new Vector2(0.98f, 0.93f)));
            Set(so, "compareStatus", Label(mid.transform, "CompareStatus", "", 12, new Vector2(0.02f, 0.79f), new Vector2(0.98f, 0.87f)));
            // A name search can return a whole confusable family, and the record below it is ~6 rows —
            // both scroll rather than growing past the panel.
            Set(so, "crmResults", ScrollPanel(mid.transform, "Results", new Color(0, 0, 0, 0.12f),
                new Vector2(0.02f, 0.48f), new Vector2(0.98f, 0.79f), out _, 3, new RectOffset(4, 14, 4, 4)));
            Set(so, "crmRecord", ScrollPanel(mid.transform, "Record", new Color(0, 0, 0, 0.12f),
                new Vector2(0.02f, 0.01f), new Vector2(0.98f, 0.47f), out _, 3, new RectOffset(4, 14, 4, 4)));

            // --- Right: remote connect ---
            var right = Panel(win.transform, "Remote", new Color(0, 0, 0, 0.2f), new Vector2(0.67f, 0.12f), new Vector2(0.99f, 0.92f));
            Label(right.transform, "Title", "Remote Desktop Software", 14, new Vector2(0, 0.94f), new Vector2(1, 1), 6);
            Set(so, "remoteId", Inp(right.transform, "RemoteId", "Remote ID", new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.9f)));
            Set(so, "remotePass", Inp(right.transform, "RemotePass", "Passcode", new Vector2(0.05f, 0.76f), new Vector2(0.95f, 0.82f)));
            Set(so, "connectBtn", Btn(right.transform, "Connect", "Connect", UIFactory.Accent, new Vector2(0.05f, 0.68f), new Vector2(0.95f, 0.74f)));
            Set(so, "connectStatus", Label(right.transform, "ConnectStatus", "", 12, new Vector2(0.05f, 0.5f), new Vector2(0.95f, 0.67f)));
            Set(so, "openRemoteBtn", Btn(right.transform, "OpenRemote", "Open Remote Desktop", UIFactory.Panel, new Vector2(0.05f, 0.42f), new Vector2(0.95f, 0.49f)));

            // Onboarding guidance — shown only while ProblemAssembler still attaches an article.
            // A full KB article body, so it scrolls like the KB overlay does. The controller toggles the
            // PANEL, so that stays the serialized reference; the text goes in the scrolling content.
            var guide = Panel(right.transform, "Guidance", new Color(0, 0, 0, 0.25f), new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.39f));
            UIFactory.ScrollView(guide.gameObject, out var guideContent, 4, new RectOffset(6, 14, 6, 6));
            Set(so, "guidancePanel", guide.gameObject);
            Set(so, "guidanceText", UIFactory.Label("GuidanceText", guideContent, "", 12));
            guide.gameObject.SetActive(false);

            // --- Footer ---
            Set(so, "ticketStatusLine", Label(win.transform, "StatusLine", "Status: In Progress", 14, new Vector2(0.01f, 0.02f), new Vector2(0.58f, 0.11f), 6));
            Set(so, "openKbBtn", Btn(win.transform, "OpenKb", "📚 Knowledge Base", UIFactory.Panel, new Vector2(0.59f, 0.02f), new Vector2(0.75f, 0.11f)));
            Set(so, "hangUpBtn", Btn(win.transform, "HangUp", "Hang Up / End Call", UIFactory.Danger, new Vector2(0.76f, 0.02f), new Vector2(0.99f, 0.11f)));
        }

        /// <summary>
        /// The customer's machine, once Remote Desktop connects: a Windows XP session, not a panel of
        /// buttons. Wallpaper, a column of desktop icons, draggable Luna windows, a taskbar with Start
        /// menu / window buttons / clock tray, and the mstsc connection bar pinned along the top.
        /// Chrome comes from Assets/RetroWindowsGUI + the generated XP sprites (POS ▸ Generate XP Desktop
        /// Sprites); with neither present every piece falls back to a flat colour and still functions.
        /// </summary>
        private static void BuildRemote(Transform root, SerializedObject so)
        {
            var ov = Screen("Overlay_RemoteDesktop", root, false);
            ov.GetComponent<Image>().color = Color.black;
            Set(so, "overlayRemote", ov);

            var skin = XPSkin.Get();
            if (skin == null)
                Debug.LogWarning("[GameSceneBuilder] No Assets/Resources/XPSkin.asset — run POS ▸ Generate XP " +
                                 "Desktop Sprites, then build the scene again for the real XP look.");

            // --- Wallpaper -----------------------------------------------------------------------
            var paper = XPFactory.Picture("Wallpaper", ov.transform, skin != null ? skin.wallpaper : null, false);
            if (skin == null || skin.wallpaper == null) paper.color = XPFactory.Hex(0x3A6EA5);
            UIFactory.Stretch(paper.rectTransform);

            // --- Desktop icons: filled top-to-bottom, wrapping into a second column like a real desktop.
            // Seven rows is what fits above the taskbar; a fixed single column ran the last icons under it.
            var icons = Panel(ov.transform, "DesktopIcons", new Color(0, 0, 0, 0),
                new Vector2(0.004f, 0.065f), new Vector2(0.18f, 0.99f));
            var grid = UIFactory.Grid(icons.gameObject, new Vector2(78, 62), 2, new RectOffset(4, 4, 4, 4));
            grid.startAxis = GridLayoutGroup.Axis.Vertical;
            grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            grid.constraintCount = 7;
            Set(so, "desktopIcons", icons.transform);

            // --- Window layer: everything above the taskbar, and the drag bounds for open windows ----
            var layer = UIFactory.Rect(UIFactory.Node("WindowLayer", ov.transform));
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = new Vector2(0f, XPFactory.TaskbarHeight);
            layer.offsetMax = Vector2.zero;
            Set(so, "desktopArea", layer);

            BuildAppWindow(layer, so);
            BuildConnectionBar(ov.transform, so);
            BuildStartMenu(ov.transform, so);
            BuildTaskbar(ov.transform, so);
        }

        private static void BuildAppWindow(RectTransform layer, SerializedObject so)
        {
            var client = XPFactory.Window("AppWindow", layer, out _, out var caption, out var capIcon,
                out var close, out var minimise, out var maximise);
            var frame = (RectTransform)client.parent;
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(640f, 420f);
            frame.anchoredPosition = new Vector2(40f, 6f);   // clear of the desktop icon column

            Set(so, "appWindow", frame.gameObject);
            Set(so, "appWindowRect", frame);
            Set(so, "appTitle", caption);
            Set(so, "appTitleIcon", capIcon);
            Set(so, "closeAppBtn", close);
            Set(so, "minimiseAppBtn", minimise);
            Set(so, "maximiseAppBtn", maximise);

            // Sub-tab strip (POS Manager / POS Terminal have several; single-tab apps hide it).
            var tabs = Panel(client, "AppTabs", new Color(0, 0, 0, 0), new Vector2(0f, 0.925f), new Vector2(1f, 1f));
            UIFactory.HList(tabs.gameObject, 3, new RectOffset(2, 2, 2, 2));
            Set(so, "appTabRow", tabs.transform);

            // Longest list in the game: status + every action hosted by the tab + tab extras + session
            // log. It has to scroll — the batch tab alone can run past the window. The ScrollRect sits on
            // the white well itself, not on a child: the wheel only reaches a ScrollRect that is an
            // ANCESTOR of whatever the pointer hit, and the well is the graphic under the empty space.
            var well = XPFactory.Sunken("AppBody", client);
            UIFactory.Anchor(well.rectTransform, Vector2.zero, new Vector2(1f, 0.915f), Vector2.zero, Vector2.zero);
            UIFactory.ScrollView(well.gameObject, out var body, 4, new RectOffset(8, 16, 8, 8));
            Set(so, "appBody", body);

            frame.gameObject.SetActive(false);
        }

        /// <summary>The real Remote Desktop client pins a host bar to the top of the session — so does this.</summary>
        private static void BuildConnectionBar(Transform ov, SerializedObject so)
        {
            var skin = XPSkin.Get();
            var bar = XPFactory.Sliced("ConnectionBar", ov, skin != null ? skin.titleBar : null,
                XPFactory.Hex(0x0A246A), 1f);
            var rt = bar.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(420f, 22f);
            rt.anchoredPosition = Vector2.zero;

            var label = XPFactory.Label("ConnLabel", bar.transform, "Remote Desktop", 12,
                TextAnchor.MiddleCenter, XPFactory.TitleInk);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Stretch(UIFactory.Rect(label.gameObject), 24, 0, 30, 0);
            Set(so, "connBarLabel", label);

            var disconnect = XPFactory.Picture("Disconnect", bar.transform, skin != null ? skin.closeButton : null, false);
            if (skin == null || skin.closeButton == null) disconnect.color = UIFactory.Danger;
            var drt = disconnect.rectTransform;
            drt.anchorMin = drt.anchorMax = new Vector2(1f, 0.5f);
            drt.pivot = new Vector2(1f, 0.5f);
            drt.sizeDelta = new Vector2(18f, 18f);
            drt.anchoredPosition = new Vector2(-3f, 0f);
            var btn = disconnect.gameObject.AddComponent<Button>();
            btn.targetGraphic = disconnect;
            Set(so, "closeRemoteBtn", btn);
        }

        private static void BuildStartMenu(Transform ov, SerializedObject so)
        {
            var skin = XPSkin.Get();
            var menu = XPFactory.Sliced("StartMenu", ov, skin != null ? skin.windowBase : null, XPFactory.Face);
            var rt = menu.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(210f, 280f);
            rt.anchoredPosition = new Vector2(2f, XPFactory.TaskbarHeight - 1f);

            // Blue banner across the top — the XP start menu's user strip.
            var banner = XPFactory.Sliced("Banner", menu.transform, skin != null ? skin.titleBar : null,
                XPFactory.Hex(0x0A246A), 1f);
            var brt = banner.rectTransform;
            brt.anchorMin = new Vector2(0f, 1f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.sizeDelta = new Vector2(-8f, 28f);
            brt.anchoredPosition = new Vector2(0f, -4f);
            var who = XPFactory.Label("BannerText", banner.transform, "Store Manager", 13,
                TextAnchor.MiddleLeft, XPFactory.TitleInk);
            who.fontStyle = FontStyle.Bold;
            UIFactory.Stretch(UIFactory.Rect(who.gameObject), 8, 0, 4, 0);

            var list = Panel(menu.transform, "Programs", new Color(1f, 1f, 1f, 0.55f),
                new Vector2(0f, 0f), new Vector2(1f, 1f));
            UIFactory.Anchor(list, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(5f, 5f), new Vector2(-5f, -36f));
            UIFactory.VList(list.gameObject, 1, new RectOffset(3, 3, 3, 3));
            Set(so, "startMenuList", list.transform);

            Set(so, "startMenu", menu.gameObject);
            menu.gameObject.SetActive(false);
        }

        private static void BuildTaskbar(Transform ov, SerializedObject so)
        {
            var skin = XPSkin.Get();
            var bar = XPFactory.Sliced("Taskbar", ov, skin != null ? skin.taskbar : null, XPFactory.Hex(0x245EDB), 1f);
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, XPFactory.TaskbarHeight);
            rt.anchoredPosition = Vector2.zero;

            // Start button.
            var start = XPFactory.Sliced("StartButton", bar.transform, skin != null ? skin.startButton : null,
                XPFactory.Hex(0x3C8A22), 1f);
            var srt = start.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(78f, 26f);
            srt.anchoredPosition = new Vector2(1f, 0f);
            var startBtn = start.gameObject.AddComponent<Button>();
            startBtn.targetGraphic = start;
            if (skin != null && skin.startButtonPressed != null)
            {
                startBtn.transition = Selectable.Transition.SpriteSwap;
                startBtn.spriteState = new SpriteState { pressedSprite = skin.startButtonPressed };
            }
            var flag = XPFactory.Picture("StartLogo", start.transform, skin != null ? skin.startLogo : null);
            var frt = flag.rectTransform;
            frt.anchorMin = frt.anchorMax = new Vector2(0f, 0.5f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.sizeDelta = new Vector2(18f, 18f);
            frt.anchoredPosition = new Vector2(6f, 0f);
            var startText = XPFactory.Label("StartText", start.transform, "start", 16,
                TextAnchor.MiddleLeft, XPFactory.TitleInk);
            startText.fontStyle = FontStyle.BoldAndItalic;
            UIFactory.Stretch(UIFactory.Rect(startText.gameObject), 28, 1, 6, 3);
            Set(so, "startBtn", startBtn);

            // Notification area, right-aligned, holding the shift clock.
            var tray = XPFactory.Sliced("Tray", bar.transform, skin != null ? skin.tray : null, XPFactory.Hex(0x0F94D9), 1f);
            var trt = tray.rectTransform;
            trt.anchorMin = new Vector2(1f, 0f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(1f, 0.5f);
            trt.sizeDelta = new Vector2(96f, 0f);
            trt.anchoredPosition = Vector2.zero;
            var clock = XPFactory.Label("Clock", tray.transform, "8:00 PM", 12, TextAnchor.MiddleCenter, XPFactory.TitleInk);
            UIFactory.Stretch(UIFactory.Rect(clock.gameObject), 6, 0, 6, 0);
            Set(so, "trayClock", clock);

            // Window buttons live between the two, and grow left-to-right like the real shell.
            var tasks = Panel(bar.transform, "TaskButtons", new Color(0, 0, 0, 0), new Vector2(0f, 0f), new Vector2(1f, 1f));
            UIFactory.Anchor(tasks, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(82f, 3f), new Vector2(-100f, -3f));
            UIFactory.HList(tasks.gameObject, 3, new RectOffset(0, 0, 0, 0));
            Set(so, "taskbarApps", tasks.transform);
        }

        private static void BuildConfirm(Transform root, SerializedObject so)
        {
            var ov = Screen("Overlay_Confirm", root, false);
            ov.GetComponent<Image>().color = new Color(0, 0, 0, 0.7f);
            Set(so, "overlayConfirm", ov);
            var win = Panel(ov.transform, "Window", UIFactory.Panel, new Vector2(0.3f, 0.32f), new Vector2(0.7f, 0.68f));
            Label(win.transform, "Title", "⚠ Are you sure?", 18, new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.95f));
            // The risky-fix and unauthorized-money warnings are several sentences long — scroll rather
            // than let them run down over the Yes/Cancel buttons.
            var confirmBody = ScrollPanel(win.transform, "Body", new Color(0, 0, 0, 0.15f),
                new Vector2(0.05f, 0.24f), new Vector2(0.95f, 0.8f), out _, 4, new RectOffset(8, 16, 8, 8));
            Set(so, "confirmText", UIFactory.Label("BodyText", confirmBody, "", 14));
            Set(so, "confirmYes", Btn(win.transform, "Yes", "Yes, do it", UIFactory.Danger, new Vector2(0.08f, 0.07f), new Vector2(0.48f, 0.2f)));
            Set(so, "confirmNo", Btn(win.transform, "No", "Cancel", UIFactory.Panel, new Vector2(0.52f, 0.07f), new Vector2(0.92f, 0.2f)));
        }

        private static void BuildKnowledgeBase(Transform root, SerializedObject so)
        {
            var ov = Screen("Overlay_KnowledgeBase", root, false);
            ov.GetComponent<Image>().color = new Color(0, 0, 0, 0.7f);
            Set(so, "overlayKb", ov);
            var win = Panel(ov.transform, "Window", UIFactory.Panel, new Vector2(0.15f, 0.1f), new Vector2(0.85f, 0.9f));
            Label(win.transform, "Title", "📚 Knowledge Base — support handbook", 18, new Vector2(0.02f, 0.92f), new Vector2(0.8f, 0.98f), 6);
            Set(so, "closeKbBtn", Btn(win.transform, "CloseKb", "Close", UIFactory.Danger, new Vector2(0.85f, 0.92f), new Vector2(0.98f, 0.98f)));

            Set(so, "kbSearch", Inp(win.transform, "KbSearch", "Error code (e.g. 39)…", new Vector2(0.02f, 0.85f), new Vector2(0.7f, 0.91f)));
            Set(so, "kbSearchBtn", Btn(win.transform, "KbSearchBtn", "Search code", UIFactory.Accent, new Vector2(0.72f, 0.85f), new Vector2(0.98f, 0.91f)));

            // Seven categories in a fixed-cell grid: "CashDrawer" at size 12 needs ~74px and a
            // HorizontalLayoutGroup would divide the row up without asking.
            var cats = Panel(win.transform, "KbCategories", new Color(0, 0, 0, 0), new Vector2(0.02f, 0.77f), new Vector2(0.98f, 0.84f));
            UIFactory.Grid(cats.gameObject, new Vector2(84, 24), 4, new RectOffset(2, 2, 2, 2));
            Set(so, "kbCategories", cats.transform);

            // Articles are full TextArea bodies — the whole point of this panel is scrolling through them.
            Set(so, "kbResults", ScrollPanel(win.transform, "KbResults", new Color(0, 0, 0, 0.2f),
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.75f), out _, 8, new RectOffset(10, 18, 10, 10)));
        }

        private static void BuildMailbox(Transform root, SerializedObject so)
        {
            var ov = Screen("Overlay_Mailbox", root, false);
            ov.GetComponent<Image>().color = new Color(0, 0, 0, 0.7f);
            Set(so, "overlayMailbox", ov);
            var win = Panel(ov.transform, "Window", UIFactory.Panel, new Vector2(0.2f, 0.12f), new Vector2(0.8f, 0.88f));
            Label(win.transform, "Title", "✉ Mailbox — complaints filed tonight", 18, new Vector2(0.02f, 0.92f), new Vector2(0.8f, 0.98f), 6);
            Set(so, "closeMailboxBtn", Btn(win.transform, "CloseMail", "Close", UIFactory.Danger, new Vector2(0.85f, 0.92f), new Vector2(0.98f, 0.98f)));
            Set(so, "mailboxList", ScrollPanel(win.transform, "MailList", new Color(0, 0, 0, 0.2f),
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.9f), out _, 8, new RectOffset(10, 18, 10, 10)));
        }

        private static void BuildEnd(Transform root, SerializedObject so)
        {
            var eon = Screen("Screen_EndOfNight", root, false);
            Set(so, "screenEndOfNight", eon);
            Set(so, "eonSummary", Label(eon.transform, "Summary", "", 18, new Vector2(0.25f, 0.3f), new Vector2(0.75f, 0.85f)));
            Set(so, "continueBtn", Btn(eon.transform, "Continue", "Continue to next day ▶", UIFactory.Accent, new Vector2(0.35f, 0.15f), new Vector2(0.65f, 0.24f)));

            var go = Screen("Screen_GameOver", root, false);
            Set(so, "screenGameOver", go);
            Set(so, "gameOverText", Label(go.transform, "Text", "Terminated.", 20, new Vector2(0.25f, 0.4f), new Vector2(0.75f, 0.7f)));
            Set(so, "gameOverRestart", Btn(go.transform, "Restart", "Start New Campaign", UIFactory.Accent, new Vector2(0.35f, 0.25f), new Vector2(0.65f, 0.34f)));

            var win = Screen("Screen_Win", root, false);
            Set(so, "screenWin", win);
            Set(so, "winText", Label(win.transform, "Text", "Hired!", 20, new Vector2(0.25f, 0.4f), new Vector2(0.75f, 0.7f)));
            Set(so, "winRestart", Btn(win.transform, "Restart", "Start New Campaign", UIFactory.Accent, new Vector2(0.35f, 0.25f), new Vector2(0.65f, 0.34f)));
        }

        // ---------------------------------------------------------------- builder helpers
        private static GameObject Screen(string name, Transform parent, bool active)
        {
            var img = UIFactory.Box(name, parent, new Color(0, 0, 0, 0));
            UIFactory.Stretch(img.rectTransform);
            img.gameObject.SetActive(active);
            return img.gameObject;
        }

        private static RectTransform Panel(Transform parent, string name, Color color, Vector2 min, Vector2 max)
        {
            var img = UIFactory.Box(name, parent, color);
            UIFactory.Anchor(img.rectTransform, min, max, Vector2.zero, Vector2.zero);
            return img.rectTransform;
        }

        /// <summary>
        /// Panel whose children scroll. Returns the CONTENT transform — parent dynamic children to that,
        /// not to the panel, or they will not be inside the scrolling area.
        /// </summary>
        private static RectTransform ScrollPanel(Transform parent, string name, Color color, Vector2 min, Vector2 max,
            out ScrollRect scroll, float spacing = 4, RectOffset padding = null)
        {
            var panel = Panel(parent, name, color, min, max);
            scroll = UIFactory.ScrollView(panel.gameObject, out var content, spacing, padding);
            return content;
        }

        private static Text Label(Transform parent, string name, string text, int size, Vector2 min, Vector2 max, float pad = 0)
        {
            var t = UIFactory.Label(name, parent, text, size, TextAnchor.UpperLeft);
            UIFactory.Anchor(UIFactory.Rect(t.gameObject), min, max, new Vector2(pad, pad), new Vector2(-pad, -pad));
            return t;
        }

        private static Button Btn(Transform parent, string name, string label, Color color, Vector2 min, Vector2 max)
        {
            var b = UIFactory.Button(name, parent, label, color);
            UIFactory.Anchor(UIFactory.Rect(b.gameObject), min, max, Vector2.zero, Vector2.zero);
            return b;
        }

        private static Button AskBtn(Transform parent, string name, string label)
        {
            var b = UIFactory.Button(name, parent, label, UIFactory.Panel, 12);
            // Pinned, not just floored: the column holds five of these now, and a row that stretches to
            // fill spare space pushes the last one out over the status line below the panel.
            UIFactory.FixedHeight(b.gameObject, 26);
            return b;
        }

        private static InputField Inp(Transform parent, string name, string ph, Vector2 min, Vector2 max)
        {
            var f = UIFactory.Input(name, parent, ph);
            UIFactory.Anchor(UIFactory.Rect(f.gameObject), min, max, Vector2.zero, Vector2.zero);
            return f;
        }

        private static void Set(SerializedObject so, string prop, Object value)
        {
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning($"[GameSceneBuilder] No serialized field '{prop}'."); return; }
            p.objectReferenceValue = value;
        }

        private static void RemoveExisting(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) Object.DestroyImmediate(go);
        }
    }
}
