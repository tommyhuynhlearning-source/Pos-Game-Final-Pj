using System;
using UnityEngine;

namespace POSTechSupport.UI
{
    /// <summary>
    /// Every sprite the in-game customer desktop draws itself with: the Retro Windows GUI pack
    /// (Assets/RetroWindowsGUI — sliced window frame, buttons, inner frames) plus the Luna-blue XP shell
    /// pieces generated into Assets/Art/XP (wallpaper, title bar, taskbar, Start button, app icons).
    ///
    /// The asset lives at Assets/Resources/XPSkin.asset so runtime code (GameUIController spawns desktop
    /// icons and taskbar buttons per call) can reach it without a serialized reference on every screen.
    /// Regenerate both the sprites and this asset with POS ▸ Generate XP Desktop Sprites.
    /// </summary>
    public class XPSkin : ScriptableObject
    {
        [Serializable]
        public struct IconEntry
        {
            public string key;
            public Sprite sprite;
        }

        [Header("Retro Windows GUI — window chrome")]
        public Sprite windowBase;
        public Sprite windowHeader;
        public Sprite windowHeaderInactive;
        public Sprite button;
        public Sprite buttonFocus;
        public Sprite buttonPressed;
        public Sprite buttonInactive;
        public Sprite innerFrame;           // sunken: list boxes, text areas
        public Sprite innerFrameInverted;   // raised: group boxes, status strips
        public Sprite sliderBackground;
        public Sprite sliderHandle;

        [Header("Generated — XP Luna shell")]
        public Sprite wallpaper;
        public Sprite titleBar;
        public Sprite titleBarInactive;
        public Sprite taskbar;
        public Sprite startButton;
        public Sprite startButtonPressed;
        public Sprite taskButton;
        public Sprite taskButtonActive;
        public Sprite tray;
        public Sprite closeButton;
        public Sprite minimizeButton;
        public Sprite maximizeButton;
        public Sprite startLogo;

        [Header("Generated — desktop icons (key matches GameUIController.AppKeys)")]
        public IconEntry[] icons = Array.Empty<IconEntry>();

        public Sprite Icon(string key)
        {
            if (icons == null) return null;
            foreach (var e in icons)
                if (e.key == key) return e.sprite;
            return null;
        }

        private static XPSkin _cached;

        /// <summary>
        /// The skin, or null before POS ▸ Generate XP Desktop Sprites has ever been run — every caller
        /// falls back to the flat-colour UI in that case rather than throwing.
        /// </summary>
        public static XPSkin Get()
        {
            if (_cached == null) _cached = Resources.Load<XPSkin>("XPSkin");
            return _cached;
        }
    }
}
