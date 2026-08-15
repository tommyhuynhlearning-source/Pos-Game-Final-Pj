using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using POSTechSupport.UI;

namespace POSTechSupport.EditorTools
{
    /// <summary>
    /// Renders the (normally hidden) Remote Desktop overlay to XPDesktop.png in EDIT mode, so the XP shell
    /// can be checked without playing a whole call through to a remote connection. The desktop icons,
    /// taskbar and app body are populated at runtime, so this fills them with representative placeholder
    /// content; the Start menu variant drives the real GameUIController.ToggleStartMenu.
    ///
    /// It temporarily repoints the canvas at an off-screen camera (a Screen Space Overlay canvas cannot be
    /// rendered to a RenderTexture) and puts every touched object back afterwards.
    /// </summary>
    public static class RemoteDesktopCapture
    {
        [MenuItem("POS/Debug/Capture Remote Desktop")]
        public static void Capture() => Shoot(false);

        [MenuItem("POS/Debug/Capture Remote Desktop (Start menu)")]
        public static void CaptureStart() => Shoot(true);

        private static void Shoot(bool startMenu)
        {
            var canvasGo = GameObject.Find("GameCanvas");
            if (canvasGo == null) { Debug.LogError("[Capture] No GameCanvas — build the scene first."); return; }
            var canvas = canvasGo.GetComponent<Canvas>();

            var overlay = FindChild(canvasGo.transform, "Overlay_RemoteDesktop");
            var hub = FindChild(canvasGo.transform, "Screen_Hub");
            if (overlay == null) { Debug.LogError("[Capture] No Overlay_RemoteDesktop."); return; }

            bool hubWas = hub != null && hub.gameObject.activeSelf;
            var modeWas = canvas.renderMode;
            var camWas = canvas.worldCamera;

            var icons = FindChild(overlay, "DesktopIcons");
            var taskbarApps = FindChild(FindChild(overlay, "Taskbar"), "TaskButtons");
            var window = FindChild(FindChild(overlay, "WindowLayer"), "AppWindow");
            var body = FindDeep(window, "Content");
            var tabs = FindDeep(window, "AppTabs");

            var skin = XPSkin.Get();
            FillPreview(icons, taskbarApps, window, body, tabs, skin);
            // Drives the real ToggleStartMenu so the menu under test is the one the game builds.
            var ui = Object.FindAnyObjectByType<GameUIController>();
            if (startMenu && ui != null)
                ui.GetType().GetMethod("ToggleStartMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                  ?.Invoke(ui, null);

            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var camGo = new GameObject("__CaptureCam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            cam.targetTexture = rt;
            cam.transform.position = new Vector3(0f, 0f, -100f);

            if (hub != null) hub.gameObject.SetActive(false);
            overlay.gameObject.SetActive(true);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 50f;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(canvasGo.GetComponent<RectTransform>());
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            shot.Apply();
            RenderTexture.active = prev;

            string path = Path.GetFullPath(startMenu ? "XPDesktopStart.png" : "XPDesktop.png");
            File.WriteAllBytes(path, shot.EncodeToPNG());

            // Restore.
            canvas.renderMode = modeWas;
            canvas.worldCamera = camWas;
            overlay.gameObject.SetActive(false);
            if (hub != null) hub.gameObject.SetActive(hubWas);
            ClearPreview(icons, taskbarApps, window, body, tabs);
            var menu = FindChild(overlay, "StartMenu");
            if (menu != null)
            {
                UIFactory.Clear(FindChild(menu, "Programs"));
                menu.gameObject.SetActive(false);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(shot);
            rt.Release();
            Object.DestroyImmediate(rt);

            Debug.Log("[Capture] Wrote " + path);
        }

        private static readonly (string key, string title)[] Apps =
        {
            ("system", "System (Windows)"), ("network", "Network Settings"), ("possoftware", "POS Manager"),
            ("terminal", "POS Terminal"), ("printer", "Printer & Print Queue"),
            ("devicemanager", "Device Manager"), ("cashdrawer", "Cash Drawer Config"),
        };

        private static void FillPreview(Transform icons, Transform taskbarApps, Transform window,
            Transform body, Transform tabs, XPSkin skin)
        {
            foreach (var (key, title) in Apps)
                XPFactory.DesktopIcon("icon_" + key, icons, title, skin != null ? skin.Icon(key) : null);
            XPFactory.DesktopIcon("icon_mycomputer", icons, "My Computer", skin != null ? skin.Icon("mycomputer") : null);

            XPFactory.TaskButton("task_terminal", taskbarApps, "POS Terminal",
                skin != null ? skin.Icon("terminal") : null, true);

            window.gameObject.SetActive(true);
            var caption = FindDeep(window, "Caption")?.GetComponent<Text>();
            if (caption != null) caption.text = "POS Terminal  -  Status / Network";
            var capIcon = FindDeep(window, "TitleIcon")?.GetComponent<Image>();
            if (capIcon != null && skin != null) { capIcon.sprite = skin.Icon("terminal"); capIcon.enabled = true; }

            foreach (var t in new[] { "Status / Network", "Batch" })
            {
                var b = XPFactory.Button("tab_" + t, tabs, t, 12);
                UIFactory.MinHeight(b.gameObject, 22);
                XPFactory.FitWidth(b);
            }

            XPFactory.Label("s1", body, "<b>Terminal status: Error</b>\nWrong Wi-Fi network — the terminal is on the guest SSID.",
                14, TextAnchor.UpperLeft, XPFactory.InkRed);
            XPFactory.Label("s2", body,
                "Joined Wi-Fi: <b>SunriseDiner-Guest</b>\nIP (from DHCP): 192.168.50.24\nGateway: 192.168.50.1",
                13, TextAnchor.UpperLeft);
            XPFactory.Label("s3", body, "Join Wi-Fi network:", 13, TextAnchor.MiddleLeft);
            foreach (var ssid in new[] { "SunriseDiner-Main", "SunriseDiner-Guest  ✓", "Linksys-2G" })
                UIFactory.MinHeight(XPFactory.Button("wifi_" + ssid, body, ssid, 12).gameObject, 26);
            XPFactory.Label("s4", body,
                "<b>Session log</b>\n🔎 Terminal reports gateway 192.168.50.1 — that is the guest range.\n• Reconnected the terminal.",
                13, TextAnchor.UpperLeft);
        }

        private static void ClearPreview(params Transform[] containers)
        {
            foreach (var c in containers)
            {
                if (c == null) continue;
                if (c.name == "AppWindow") { c.gameObject.SetActive(false); continue; }
                UIFactory.Clear(c);
            }
        }

        private static Transform FindChild(Transform parent, string name)
        {
            if (parent == null) return null;
            foreach (Transform c in parent) if (c.name == name) return c;
            return null;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent == null) return null;
            foreach (var t in parent.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
