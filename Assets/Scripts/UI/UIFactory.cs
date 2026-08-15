using UnityEngine;
using UnityEngine.UI;

namespace POSTechSupport.UI
{
    /// <summary>
    /// Tiny legacy-uGUI construction helpers used by BOTH the edit-time scene builder
    /// (GameSceneBuilder) and the runtime controller (GameUIController). Legacy UI.Text is used
    /// (builtin LegacyRuntime font) so generated UI needs no TMP-essentials import.
    /// </summary>
    public static class UIFactory
    {
        public static readonly Color Bg = new(0.11f, 0.12f, 0.15f, 1f);
        public static readonly Color Panel = new(0.16f, 0.17f, 0.21f, 1f);
        public static readonly Color Accent = new(0.20f, 0.45f, 0.85f, 1f);
        public static readonly Color Danger = new(0.75f, 0.25f, 0.25f, 1f);
        public static readonly Color Ink = new(0.92f, 0.93f, 0.95f, 1f);

        private static Font _font;
        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    // Use standard, high-quality, Unicode-compliant OS fonts that support Vietnamese diacritics
                    string[] osFonts = { "Arial", "Helvetica", "Segoe UI", "Calibri", "Verdana", "Liberation Sans" };
                    _font = Font.CreateDynamicFontFromOSFont(osFonts, 16);
                    if (_font == null)
                    {
                        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    }
                }
                return _font;
            }
        }

        public static RectTransform Rect(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            return rt != null ? rt : go.AddComponent<RectTransform>();
        }

        public static GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>Stretch a rect to fill its parent with optional margins.</summary>
        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = offMin; rt.offsetMax = offMax;
            return rt;
        }

        public static Image Box(string name, Transform parent, Color color)
        {
            var go = Node(name, parent);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size = 16,
            TextAnchor anchor = TextAnchor.UpperLeft, Color? color = null)
        {
            var go = Node(name, parent);
            var t = go.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.text = text;
            t.alignment = anchor;
            t.color = color ?? Ink;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            return t;
        }

        public static Button Button(string name, Transform parent, string label, Color? color = null, int size = 15)
        {
            var img = Box(name, parent, color ?? Accent);
            var btn = img.gameObject.AddComponent<Button>();
            var t = Label(name + "_Label", img.transform, label, size, TextAnchor.MiddleCenter, Color.white);
            Stretch(Rect(t.gameObject));
            var colors = btn.colors; colors.fadeDuration = 0.05f; btn.colors = colors;
            return btn;
        }

        public static InputField Input(string name, Transform parent, string placeholder)
        {
            var img = Box(name, parent, new Color(0.08f, 0.09f, 0.11f, 1f));
            var field = img.gameObject.AddComponent<InputField>();

            var text = Label(name + "_Text", img.transform, "", 15, TextAnchor.MiddleLeft);
            Stretch(Rect(text.gameObject), 8, 2, 8, 2);
            var ph = Label(name + "_Placeholder", img.transform, placeholder, 15, TextAnchor.MiddleLeft,
                new Color(0.55f, 0.57f, 0.6f, 1f));
            Stretch(Rect(ph.gameObject), 8, 2, 8, 2);

            field.textComponent = text;
            field.placeholder = ph;
            field.lineType = InputField.LineType.SingleLine;
            return field;
        }

        /// <summary>Add a vertical layout + content-size fitter to a container (for dynamic lists).</summary>
        public static VerticalLayoutGroup VList(GameObject go, float spacing = 4, RectOffset padding = null)
        {
            var v = go.GetComponent<VerticalLayoutGroup>() ?? go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            v.padding = padding ?? new RectOffset(6, 6, 6, 6);
            return v;
        }

        public static HorizontalLayoutGroup HList(GameObject go, float spacing = 6, RectOffset padding = null)
        {
            var h = go.GetComponent<HorizontalLayoutGroup>() ?? go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            h.padding = padding ?? new RectOffset(0, 0, 0, 0);
            return h;
        }

        /// <summary>
        /// Wrapping row of same-sized cells. A HorizontalLayoutGroup cannot wrap, so a long strip of
        /// buttons gets squeezed until each label breaks one character per line — use this instead when
        /// the child count is not small and fixed.
        /// </summary>
        public static GridLayoutGroup Grid(GameObject go, Vector2 cell, float spacing = 4, RectOffset padding = null)
        {
            var g = go.GetComponent<GridLayoutGroup>() ?? go.AddComponent<GridLayoutGroup>();
            g.cellSize = cell;
            g.spacing = new Vector2(spacing, spacing);
            g.padding = padding ?? new RectOffset(0, 0, 0, 0);
            g.startCorner = GridLayoutGroup.Corner.UpperLeft;
            g.startAxis = GridLayoutGroup.Axis.Horizontal;
            g.childAlignment = TextAnchor.UpperLeft;
            g.constraint = GridLayoutGroup.Constraint.Flexible;
            return g;
        }

        /// <summary>
        /// Turns <paramref name="go"/> into a vertically scrolling viewport and returns the CONTENT
        /// transform that dynamic children must be parented to. Anything whose length is decided at
        /// runtime — the chat log, CRM record, app body, mailbox — belongs in one of these: a plain panel
        /// with a VerticalLayoutGroup keeps stacking children past its own edge and draws them over
        /// whatever sits below, which is exactly the "extra components" overlap.
        /// </summary>
        public static ScrollRect ScrollView(GameObject go, out RectTransform content,
            float spacing = 4, RectOffset padding = null, bool scrollbar = true)
        {
            var scroll = go.GetComponent<ScrollRect>() ?? go.AddComponent<ScrollRect>();
            // Clip to the viewport rect. RectMask2D (not Mask) — no extra material, no stencil buffer.
            if (go.GetComponent<RectMask2D>() == null) go.AddComponent<RectMask2D>();

            var contentGo = Node("Content", go.transform);
            content = Rect(contentGo);
            content.anchorMin = new Vector2(0f, 1f);      // full width, pinned to the top edge…
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;             // …height comes from the fitter below
            content.anchoredPosition = Vector2.zero;

            VList(contentGo, spacing, padding ?? new RectOffset(6, 14, 6, 6));
            var fitter = contentGo.GetComponent<ContentSizeFitter>() ?? contentGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = content;
            scroll.viewport = null;                      // null => the ScrollRect's own rect is the viewport
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            if (scrollbar)
            {
                scroll.verticalScrollbar = VScrollbar(go.transform);
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            }
            return scroll;
        }

        /// <summary>Thin right-edge scrollbar — the affordance that tells the player a list scrolls at all.</summary>
        private static Scrollbar VScrollbar(Transform parent)
        {
            var track = Box("Scrollbar", parent, new Color(0f, 0f, 0f, 0.25f));
            var rt = track.rectTransform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = Vector2.one;
            rt.pivot = Vector2.one;
            rt.sizeDelta = new Vector2(8f, 0f);
            rt.anchoredPosition = Vector2.zero;

            var sb = track.gameObject.AddComponent<Scrollbar>();
            var area = Node("SlidingArea", track.transform);
            Stretch(Rect(area));
            var handle = Box("Handle", area.transform, new Color(0.42f, 0.44f, 0.5f, 1f));
            Stretch(handle.rectTransform);

            sb.handleRect = handle.rectTransform;
            sb.targetGraphic = handle;
            sb.direction = Scrollbar.Direction.BottomToTop;
            return sb;
        }

        public static LayoutElement MinHeight(GameObject go, float h)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minHeight = h;
            return le;
        }

        /// <summary>
        /// Pins a child's height so a vertical layout can neither stretch it nor let it overflow. A row
        /// that only has a MINIMUM grows to share out spare space, which looks fine until one more row is
        /// added and the whole stack runs off the bottom of its panel — over whatever is underneath.
        /// </summary>
        public static LayoutElement FixedHeight(GameObject go, float h)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minHeight = h;
            le.preferredHeight = h;
            le.flexibleHeight = 0;
            return le;
        }

        /// <summary>Floor on a child's width, so a horizontal layout cannot crush its label.</summary>
        public static LayoutElement MinWidth(GameObject go, float w)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minWidth = w;
            return le;
        }

        public static void Clear(Transform container)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(container.GetChild(i).gameObject);
        }
    }
}
