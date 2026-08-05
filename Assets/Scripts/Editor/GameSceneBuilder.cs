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
        }

        private static void BuildNight(Transform root, SerializedObject so)
        {
            var scr = Screen("Screen_Night", root, false);
            Set(so, "screenNight", scr);
            Set(so, "nightClock", Label(scr.transform, "Clock", "🕗 8:00 PM", 24, new Vector2(0.03f, 0.9f), new Vector2(0.3f, 0.98f)));
            Set(so, "nightCounts", Label(scr.transform, "Counts", "", 16, new Vector2(0.3f, 0.9f), new Vector2(0.82f, 0.98f)));
            Set(so, "openMailboxBtn", Btn(scr.transform, "OpenMailbox", "✉ Mailbox", UIFactory.Panel, new Vector2(0.84f, 0.91f), new Vector2(0.97f, 0.98f)));

            var logPanel = Panel(scr.transform, "CallLogPanel", UIFactory.Panel, new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.88f));
            Set(so, "callLogText", Label(logPanel.transform, "CallLog", "Waiting for the phone to ring…", 15, new Vector2(0, 0), new Vector2(1, 1), 10));

            var dev = Panel(scr.transform, "DevRow", new Color(0, 0, 0, 0), new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.16f));
            UIFactory.HList(dev.gameObject, 6, new RectOffset(4, 4, 4, 4));
            Set(so, "devRow", dev.transform);   // without this the force-call buttons never get wired
            // 40 issues won't fit on one strip — these are the ones worth reaching for by hand: one per
            // discrimination pair, plus the blocker chains. Everything else comes from "random".
            foreach (var ids in new[] { "random", "P1", "P2", "P3", "P5", "P8", "P13", "P14", "P16",
                                        "P17", "P18", "P21", "P26", "P29", "P36", "P37", "P39",
                                        "P4,P1", "P14,P2", "P15,P32" })
            {
                var b = UIFactory.Button("Dev_" + ids, dev.transform, ids, UIFactory.Panel, 12);
                UIFactory.MinHeight(b.gameObject, 30);
            }
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
            Set(so, "chatText", Label(left.transform, "ChatLog", "", 14, new Vector2(0.02f, 0.50f), new Vector2(0.98f, 0.93f), 6));
            var facts = Panel(left.transform, "CustomerFacts", new Color(0, 0, 0, 0), new Vector2(0.02f, 0.41f), new Vector2(0.98f, 0.49f));
            UIFactory.HList(facts.gameObject, 4);
            Set(so, "customerFacts", facts.transform);

            // M4: type anything. The quick-asks below are shortcuts into the same pipeline, kept because
            // they double as a discoverability aid for what the customer can actually be asked.
            Set(so, "chatInput", Inp(left.transform, "ChatInput", "Type what you want to ask…",
                new Vector2(0.02f, 0.34f), new Vector2(0.74f, 0.40f)));
            Set(so, "chatSendBtn", Btn(left.transform, "ChatSend", "Send", UIFactory.Accent,
                new Vector2(0.76f, 0.34f), new Vector2(0.98f, 0.40f)));

            // Two columns — eight of these will not fit stacked in the space the chat log needs.
            var colL = Panel(left.transform, "AskCol1", new Color(0, 0, 0, 0), new Vector2(0.02f, 0.01f), new Vector2(0.49f, 0.33f));
            var colR = Panel(left.transform, "AskCol2", new Color(0, 0, 0, 0), new Vector2(0.51f, 0.01f), new Vector2(0.98f, 0.33f));
            UIFactory.VList(colL.gameObject, 2, new RectOffset(0, 2, 2, 2));
            UIFactory.VList(colR.gameObject, 2, new RectOffset(2, 0, 2, 2));
            Set(so, "askSymptom", AskBtn(colL.transform, "AskSymptom", "What's wrong?"));
            Set(so, "askStore", AskBtn(colL.transform, "AskStore", "Store name?"));
            Set(so, "askOwner", AskBtn(colL.transform, "AskOwner", "Who am I speaking to?"));
            Set(so, "askMachine", AskBtn(colL.transform, "AskMachine", "Which register?"));
            Set(so, "askAuth", AskBtn(colR.transform, "AskAuth", "Owner authorized?"));
            Set(so, "askWhenStarted", AskBtn(colR.transform, "AskWhenStarted", "When did it start?"));
            Set(so, "askWhatTried", AskBtn(colR.transform, "AskWhatTried", "Tried anything?"));
            Set(so, "askSms", AskBtn(colR.transform, "AskSms", "Text me the receipt"));

            // --- Mid: CRM ---
            var mid = Panel(win.transform, "CRM", new Color(0, 0, 0, 0.2f), new Vector2(0.35f, 0.12f), new Vector2(0.66f, 0.92f));
            Label(mid.transform, "Title", "CRM — Account Lookup", 14, new Vector2(0, 0.94f), new Vector2(1, 1), 6);
            Set(so, "crmSearch", Inp(mid.transform, "Search", "Store ID or name…", new Vector2(0.02f, 0.87f), new Vector2(0.72f, 0.93f)));
            Set(so, "crmSearchBtn", Btn(mid.transform, "SearchBtn", "Search", UIFactory.Accent, new Vector2(0.74f, 0.87f), new Vector2(0.98f, 0.93f)));
            Set(so, "compareStatus", Label(mid.transform, "CompareStatus", "", 12, new Vector2(0.02f, 0.79f), new Vector2(0.98f, 0.87f)));
            var results = Panel(mid.transform, "Results", new Color(0, 0, 0, 0), new Vector2(0.02f, 0.48f), new Vector2(0.98f, 0.79f));
            UIFactory.VList(results.gameObject, 3);
            Set(so, "crmResults", results.transform);
            var record = Panel(mid.transform, "Record", new Color(0, 0, 0, 0), new Vector2(0.02f, 0.01f), new Vector2(0.98f, 0.47f));
            UIFactory.VList(record.gameObject, 3);
            Set(so, "crmRecord", record.transform);

            // --- Right: remote connect ---
            var right = Panel(win.transform, "Remote", new Color(0, 0, 0, 0.2f), new Vector2(0.67f, 0.12f), new Vector2(0.99f, 0.92f));
            Label(right.transform, "Title", "Remote Desktop Software", 14, new Vector2(0, 0.94f), new Vector2(1, 1), 6);
            Set(so, "remoteId", Inp(right.transform, "RemoteId", "Remote ID", new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.9f)));
            Set(so, "remotePass", Inp(right.transform, "RemotePass", "Passcode", new Vector2(0.05f, 0.76f), new Vector2(0.95f, 0.82f)));
            Set(so, "connectBtn", Btn(right.transform, "Connect", "Connect", UIFactory.Accent, new Vector2(0.05f, 0.68f), new Vector2(0.95f, 0.74f)));
            Set(so, "connectStatus", Label(right.transform, "ConnectStatus", "", 12, new Vector2(0.05f, 0.5f), new Vector2(0.95f, 0.67f)));
            Set(so, "openRemoteBtn", Btn(right.transform, "OpenRemote", "Open Remote Desktop", UIFactory.Panel, new Vector2(0.05f, 0.42f), new Vector2(0.95f, 0.49f)));

            // Onboarding guidance — shown only while ProblemAssembler still attaches an article.
            var guide = Panel(right.transform, "Guidance", new Color(0, 0, 0, 0.25f), new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.39f));
            Set(so, "guidancePanel", guide.gameObject);
            Set(so, "guidanceText", Label(guide.transform, "GuidanceText", "", 12, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.97f), 6));
            guide.gameObject.SetActive(false);

            // --- Footer ---
            Set(so, "ticketStatusLine", Label(win.transform, "StatusLine", "Status: In Progress", 14, new Vector2(0.01f, 0.02f), new Vector2(0.58f, 0.11f), 6));
            Set(so, "openKbBtn", Btn(win.transform, "OpenKb", "📚 Knowledge Base", UIFactory.Panel, new Vector2(0.59f, 0.02f), new Vector2(0.75f, 0.11f)));
            Set(so, "hangUpBtn", Btn(win.transform, "HangUp", "Hang Up / End Call", UIFactory.Danger, new Vector2(0.76f, 0.02f), new Vector2(0.99f, 0.11f)));
        }

        private static void BuildRemote(Transform root, SerializedObject so)
        {
            var ov = Screen("Overlay_RemoteDesktop", root, false);
            ov.GetComponent<Image>().color = new Color(0.05f, 0.1f, 0.15f, 0.98f);
            Set(so, "overlayRemote", ov);
            Label(ov.transform, "DeskTitle", "🖥 Remote Desktop — Connected", 16, new Vector2(0.02f, 0.94f), new Vector2(0.8f, 0.99f), 6);
            Set(so, "closeRemoteBtn", Btn(ov.transform, "CloseRemote", "Disconnect", UIFactory.Danger, new Vector2(0.85f, 0.94f), new Vector2(0.99f, 0.99f)));

            var icons = Panel(ov.transform, "AppIcons", new Color(0, 0, 0, 0.2f), new Vector2(0.02f, 0.05f), new Vector2(0.28f, 0.93f));
            UIFactory.VList(icons.gameObject, 6, new RectOffset(8, 8, 8, 8));
            Set(so, "appIcons", icons.transform);

            var appWin = Panel(ov.transform, "AppWindow", UIFactory.Panel, new Vector2(0.3f, 0.05f), new Vector2(0.98f, 0.93f));
            Set(so, "appWindow", appWin.gameObject);
            Set(so, "appTitle", Label(appWin.transform, "AppTitle", "App", 16, new Vector2(0.01f, 0.93f), new Vector2(0.85f, 0.99f), 6));
            Set(so, "closeAppBtn", Btn(appWin.transform, "CloseApp", "Close", UIFactory.Danger, new Vector2(0.86f, 0.93f), new Vector2(0.99f, 0.99f)));

            // Sub-tab strip (POS Manager / POS Terminal have several; single-tab apps hide it).
            var tabs = Panel(appWin.transform, "AppTabs", new Color(0, 0, 0, 0), new Vector2(0.01f, 0.86f), new Vector2(0.99f, 0.92f));
            UIFactory.HList(tabs.gameObject, 4, new RectOffset(4, 4, 2, 2));
            Set(so, "appTabRow", tabs.transform);

            var body = Panel(appWin.transform, "AppBody", new Color(0, 0, 0, 0.15f), new Vector2(0.01f, 0.01f), new Vector2(0.99f, 0.85f));
            UIFactory.VList(body.gameObject, 4, new RectOffset(8, 8, 8, 8));
            Set(so, "appBody", body.transform);
            appWin.gameObject.SetActive(false);
        }

        private static void BuildConfirm(Transform root, SerializedObject so)
        {
            var ov = Screen("Overlay_Confirm", root, false);
            ov.GetComponent<Image>().color = new Color(0, 0, 0, 0.7f);
            Set(so, "overlayConfirm", ov);
            var win = Panel(ov.transform, "Window", UIFactory.Panel, new Vector2(0.3f, 0.32f), new Vector2(0.7f, 0.68f));
            Label(win.transform, "Title", "⚠ Are you sure?", 18, new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.95f));
            Set(so, "confirmText", Label(win.transform, "Body", "", 14, new Vector2(0.05f, 0.25f), new Vector2(0.95f, 0.8f)));
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

            var cats = Panel(win.transform, "KbCategories", new Color(0, 0, 0, 0), new Vector2(0.02f, 0.78f), new Vector2(0.98f, 0.84f));
            UIFactory.HList(cats.gameObject, 4, new RectOffset(2, 2, 2, 2));
            Set(so, "kbCategories", cats.transform);

            var res = Panel(win.transform, "KbResults", new Color(0, 0, 0, 0.2f), new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.76f));
            UIFactory.VList(res.gameObject, 6, new RectOffset(10, 10, 10, 10));
            Set(so, "kbResults", res.transform);
        }

        private static void BuildMailbox(Transform root, SerializedObject so)
        {
            var ov = Screen("Overlay_Mailbox", root, false);
            ov.GetComponent<Image>().color = new Color(0, 0, 0, 0.7f);
            Set(so, "overlayMailbox", ov);
            var win = Panel(ov.transform, "Window", UIFactory.Panel, new Vector2(0.2f, 0.12f), new Vector2(0.8f, 0.88f));
            Label(win.transform, "Title", "✉ Mailbox — complaints filed tonight", 18, new Vector2(0.02f, 0.92f), new Vector2(0.8f, 0.98f), 6);
            Set(so, "closeMailboxBtn", Btn(win.transform, "CloseMail", "Close", UIFactory.Danger, new Vector2(0.85f, 0.92f), new Vector2(0.98f, 0.98f)));
            var list = Panel(win.transform, "MailList", new Color(0, 0, 0, 0.2f), new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.9f));
            UIFactory.VList(list.gameObject, 8, new RectOffset(10, 10, 10, 10));
            Set(so, "mailboxList", list.transform);
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
            UIFactory.MinHeight(b.gameObject, 24);
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
