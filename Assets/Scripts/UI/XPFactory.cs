using UnityEngine;
using UnityEngine.UI;

namespace POSTechSupport.UI
{
    /// <summary>
    /// Widget helpers for the customer's Windows XP desktop — the screen behind Remote Desktop. Where
    /// UIFactory builds the technician's own dark tooling (hub, night HUD, ticket window), everything in
    /// here is light Luna chrome: #ECE9D8 faces, black text, sliced Retro Windows GUI sprites.
    ///
    /// Both halves of the UI use this: GameSceneBuilder lays the shell out at edit time, GameUIController
    /// fills the desktop icons / taskbar / app body at runtime. Every helper degrades to a flat coloured
    /// box when XPSkin is missing, so the game still runs before the sprites are generated.
    /// </summary>
    public static class XPFactory
    {
        // Luna palette (the real XP system colours).
        public static readonly Color Face = Hex(0xECE9D8);       // 3D face / dialog background
        public static readonly Color Field = Hex(0xFFFFFF);      // text + list backgrounds
        public static readonly Color Ink = Hex(0x000000);
        public static readonly Color InkDim = Hex(0x5A5A50);
        public static readonly Color InkRed = Hex(0xA00000);
        public static readonly Color InkGreen = Hex(0x1B6B1B);
        public static readonly Color InkAmber = Hex(0x8A5A00);
        public static readonly Color TitleInk = Hex(0xFFFFFF);
        public static readonly Color DesktopInk = Hex(0xFFFFFF);
        public static readonly Color ShadowInk = new(0f, 0f, 0f, 0.45f);

        // Rich-text colour tags, so app-body strings stay readable on a light window instead of using the
        // technician UI's pale-on-dark greens.
        public const string TagRed = "#A00000";
        public const string TagGreen = "#1B6B1B";
        public const string TagAmber = "#8A5A00";

        /// <summary>Fixed shell metrics, in canvas reference pixels (CanvasScaler ref is 960x540).</summary>
        public const float TaskbarHeight = 30f;
        public const float TitleBarHeight = 28f;
        public const float TitleButton = 20f;

        private static Font _font;

        /// <summary>Tahoma is the XP shell font; the rest are fallbacks that keep Vietnamese diacritics.</summary>
        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    string[] osFonts = { "Tahoma", "Verdana", "Segoe UI", "Arial", "Helvetica", "Liberation Sans" };
                    _font = Font.CreateDynamicFontFromOSFont(osFonts, 14) ?? UIFactory.Font;
                }
                return _font;
            }
        }

        public static XPSkin Skin => XPSkin.Get();

        public static Color Hex(uint rgb) =>
            new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        // ---------------------------------------------------------------- primitives

        /// <summary>
        /// Nine-sliced image. <paramref name="ppuMultiplier"/> below 1 fattens the sliced border: the pack's
        /// frames carry a 2px border at 100 PPU, which is a hairline once the canvas scales up, so 0.5
        /// renders them at the 4px weight the original chrome had.
        /// </summary>
        public static Image Sliced(string name, Transform parent, Sprite sprite, Color? fallback = null,
            float ppuMultiplier = 0.5f)
        {
            var img = UIFactory.Box(name, parent, sprite != null ? Color.white : (fallback ?? Face));
            if (sprite == null) return img;
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = ppuMultiplier;
            return img;
        }

        /// <summary>Unsliced image at its native proportions — icons, the Start logo, the wallpaper.</summary>
        public static Image Picture(string name, Transform parent, Sprite sprite, bool preserveAspect = true)
        {
            var img = UIFactory.Box(name, parent, sprite != null ? Color.white : new Color(0, 0, 0, 0));
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.preserveAspect = preserveAspect;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size = 13,
            TextAnchor anchor = TextAnchor.UpperLeft, Color? color = null)
        {
            var t = UIFactory.Label(name, parent, text, size, anchor, color ?? Ink);
            t.font = Font;
            return t;
        }

        /// <summary>Label with a hard 1px drop shadow — desktop icon captions over the wallpaper.</summary>
        public static Text ShadowLabel(string name, Transform parent, string text, int size = 12,
            TextAnchor anchor = TextAnchor.UpperCenter)
        {
            var t = Label(name, parent, text, size, anchor, DesktopInk);
            var sh = t.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            sh.effectColor = ShadowInk;
            sh.effectDistance = new Vector2(1f, -1f);
            return t;
        }

        /// <summary>Standard raised push button — sprite-swapped through the pack's four button states.</summary>
        public static Button Button(string name, Transform parent, string label, int size = 12, Color? ink = null)
        {
            var s = Skin;
            var img = Sliced(name, parent, s != null ? s.button : null, Hex(0xD4D0C8));
            var btn = img.gameObject.AddComponent<Button>();
            var t = Label(name + "_Label", img.transform, label, size, TextAnchor.MiddleCenter, ink ?? Ink);
            UIFactory.Stretch(UIFactory.Rect(t.gameObject), 6, 2, 6, 2);
            ApplyButtonSprites(btn, img, s);
            return btn;
        }

        private static void ApplyButtonSprites(Button btn, Image img, XPSkin s)
        {
            if (s == null || s.button == null)
            {
                var flat = btn.colors; flat.fadeDuration = 0.05f; btn.colors = flat;
                return;
            }
            btn.transition = Selectable.Transition.SpriteSwap;
            btn.targetGraphic = img;
            btn.spriteState = new SpriteState
            {
                highlightedSprite = s.buttonFocus,
                pressedSprite = s.buttonPressed,
                selectedSprite = s.buttonFocus,
                disabledSprite = s.buttonInactive,
            };
        }

        /// <summary>
        /// Sizes a button to its own caption. A horizontal layout hands out equal slices and lets the
        /// label wrap, which turns "Status / Network" into two clipped lines inside a 84px tab.
        /// </summary>
        public static Button FitWidth(Button b, float padding = 24f)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t == null) return b;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var le = b.GetComponent<LayoutElement>() ?? b.gameObject.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = t.preferredWidth + padding;
            return b;
        }

        /// <summary>Sunken white area — list boxes, the app body, read-only fields.</summary>
        public static Image Sunken(string name, Transform parent)
        {
            var s = Skin;
            var img = Sliced(name, parent, s != null ? s.innerFrame : null, Field);
            return img;
        }

        /// <summary>Raised strip — the status bar along the bottom of a window.</summary>
        public static Image Raised(string name, Transform parent)
        {
            var s = Skin;
            return Sliced(name, parent, s != null ? s.innerFrameInverted : null, Face);
        }

        // ---------------------------------------------------------------- shell pieces

        /// <summary>
        /// An XP application window: outer frame, Luna title bar (icon + caption + minimise/maximise/close)
        /// and an empty client area. Returns the client area — parent the app's own content to that.
        /// The title bar drags the window via <see cref="DragWindow"/>.
        /// </summary>
        public static RectTransform Window(string name, Transform parent, out RectTransform titleBar,
            out Text caption, out Image captionIcon, out Button close, out Button minimise, out Button maximise)
        {
            var s = Skin;
            var frame = Sliced(name, parent, s != null ? s.windowBase : null, Face);
            var frameRt = frame.rectTransform;

            // Title bar: full width, pinned to the top, fixed height so the gradient never stretches.
            var bar = Sliced("TitleBar", frame.transform, s != null ? s.titleBar : null, Hex(0x0A246A), 1f);
            titleBar = bar.rectTransform;
            titleBar.anchorMin = new Vector2(0f, 1f);
            titleBar.anchorMax = new Vector2(1f, 1f);
            titleBar.pivot = new Vector2(0.5f, 1f);
            titleBar.sizeDelta = new Vector2(-6f, TitleBarHeight);
            titleBar.anchoredPosition = new Vector2(0f, -3f);

            captionIcon = Picture("TitleIcon", bar.transform, null);
            var iconRt = captionIcon.rectTransform;
            iconRt.anchorMin = new Vector2(0f, 0.5f);
            iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.sizeDelta = new Vector2(16f, 16f);
            iconRt.anchoredPosition = new Vector2(5f, 0f);

            caption = Label("Caption", bar.transform, name, 13, TextAnchor.MiddleLeft, TitleInk);
            caption.fontStyle = FontStyle.Bold;
            caption.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Anchor(UIFactory.Rect(caption.gameObject), new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(25f, 0f), new Vector2(-(TitleButton * 3f + 10f), 0f));

            // Right-aligned _ □ ✕ cluster, laid out from the right edge inward like the real shell.
            close = TitleButtonAt(bar.transform, "Close", s != null ? s.closeButton : null, Hex(0xC1301A), "✕", 1);
            maximise = TitleButtonAt(bar.transform, "Maximise", s != null ? s.maximizeButton : null, Hex(0x3B78D8), "□", 2);
            minimise = TitleButtonAt(bar.transform, "Minimise", s != null ? s.minimizeButton : null, Hex(0x3B78D8), "_", 3);

            // Client area: everything under the title bar, inset by the frame's own border.
            var client = UIFactory.Rect(UIFactory.Node("Client", frame.transform));
            client.anchorMin = Vector2.zero;
            client.anchorMax = Vector2.one;
            client.offsetMin = new Vector2(5f, 5f);
            client.offsetMax = new Vector2(-5f, -(TitleBarHeight + 6f));

            var drag = bar.gameObject.AddComponent<DragWindow>();
            drag.target = frameRt;
            return client;
        }

        private static Button TitleButtonAt(Transform bar, string name, Sprite sprite, Color fallback, string glyph, int fromRight)
        {
            var img = Picture(name, bar, sprite, false);
            if (sprite == null) img.color = fallback;
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(TitleButton, TitleButton);
            rt.anchoredPosition = new Vector2(-3f - (fromRight - 1) * (TitleButton + 2f), 0f);

            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            if (sprite == null)
            {
                var t = Label(name + "_Glyph", img.transform, glyph, 12, TextAnchor.MiddleCenter, TitleInk);
                UIFactory.Stretch(UIFactory.Rect(t.gameObject));
            }
            return btn;
        }

        /// <summary>
        /// Desktop icon: 32px picture with a wrapped caption under it, the whole cell clickable. Sits
        /// directly on the wallpaper, so the caption is white with a drop shadow.
        /// </summary>
        public static Button DesktopIcon(string name, Transform parent, string label, Sprite icon)
        {
            var cell = UIFactory.Box(name, parent, new Color(0, 0, 0, 0));
            var btn = cell.gameObject.AddComponent<Button>();
            btn.targetGraphic = cell;
            var colors = btn.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = new Color(0.19f, 0.39f, 0.85f, 0.35f);
            colors.pressedColor = new Color(0.19f, 0.39f, 0.85f, 0.6f);
            colors.selectedColor = new Color(0.19f, 0.39f, 0.85f, 0.35f);
            colors.fadeDuration = 0.05f;
            btn.colors = colors;

            var pic = Picture(name + "_Icon", cell.transform, icon);
            var prt = pic.rectTransform;
            prt.anchorMin = new Vector2(0.5f, 1f);
            prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.sizeDelta = new Vector2(32f, 32f);
            prt.anchoredPosition = new Vector2(0f, -4f);

            var cap = ShadowLabel(name + "_Caption", cell.transform, label);
            UIFactory.Anchor(UIFactory.Rect(cap.gameObject), Vector2.zero, new Vector2(1f, 1f),
                new Vector2(1f, 2f), new Vector2(-1f, -38f));
            return btn;
        }

        /// <summary>Taskbar window button: 16px icon plus a clipped caption, XP blue.</summary>
        public static Button TaskButton(string name, Transform parent, string label, Sprite icon, bool active)
        {
            var s = Skin;
            var sprite = s != null ? (active ? s.taskButtonActive : s.taskButton) : null;
            var img = Sliced(name, parent, sprite, Hex(0x3C81F3), 1f);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            if (s != null && s.taskButton != null)
            {
                btn.transition = Selectable.Transition.SpriteSwap;
                btn.spriteState = new SpriteState { highlightedSprite = s.taskButtonActive, pressedSprite = s.taskButtonActive };
            }

            var pic = Picture(name + "_Icon", img.transform, icon);
            var prt = pic.rectTransform;
            prt.anchorMin = new Vector2(0f, 0.5f);
            prt.anchorMax = new Vector2(0f, 0.5f);
            prt.pivot = new Vector2(0f, 0.5f);
            prt.sizeDelta = new Vector2(16f, 16f);
            prt.anchoredPosition = new Vector2(5f, 0f);

            var t = Label(name + "_Label", img.transform, label, 12, TextAnchor.MiddleLeft, TitleInk);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIFactory.Stretch(UIFactory.Rect(t.gameObject), 24, 2, 6, 2);
            UIFactory.MinWidth(img.gameObject, 130f);
            UIFactory.MinHeight(img.gameObject, 22f);
            return btn;
        }
    }
}
