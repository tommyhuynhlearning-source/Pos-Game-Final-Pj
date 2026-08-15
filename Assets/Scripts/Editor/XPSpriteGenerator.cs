using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using POSTechSupport.UI;

namespace POSTechSupport.EditorTools
{
    /// <summary>
    /// Draws the Windows XP shell sprites the customer's desktop needs and that the Retro Windows GUI pack
    /// does not ship — it is a Windows 95/98 kit, so it has the frames, buttons and inner bevels but no
    /// Luna-blue caption, no taskbar, no Start button, no icons. Everything here is generated pixel by
    /// pixel into Assets/Art/XP, then both halves are collected into Assets/Resources/XPSkin.asset.
    ///
    /// Menu: POS ▸ Generate XP Desktop Sprites. Re-runnable — it overwrites in place, so the scene keeps
    /// its references. To swap in hand-drawn or AI-generated art instead, drop a PNG with the same file
    /// name into Assets/Art/XP and re-run only the skin step (POS ▸ Rebuild XP Skin Asset).
    /// </summary>
    public static class XPSpriteGenerator
    {
        private const string ArtDir = "Assets/Art/XP";
        private const string SkinPath = "Assets/Resources/XPSkin.asset";
        private const string PackDir = "Assets/RetroWindowsGUI";

        [MenuItem("POS/Generate XP Desktop Sprites")]
        public static void Generate()
        {
            Directory.CreateDirectory(ArtDir);
            if (!Directory.Exists("Assets/Resources")) Directory.CreateDirectory("Assets/Resources");

            // Deliberately NOT wrapped in StartAssetEditing: each Save() reimports the PNG it just wrote so
            // it can set the sprite border, and a batched import window makes that importer unavailable.
            BuildWallpaper();
            BuildTitleBars();
            BuildTaskbar();
            BuildStartButton();
            BuildTaskButtons();
            BuildTray();
            BuildTitleButtons();
            BuildStartLogo();
            BuildIcons();
            AssetDatabase.Refresh();

            RebuildSkin();
            Debug.Log($"[XPSpriteGenerator] Wrote XP shell sprites to {ArtDir} and rebuilt {SkinPath}. " +
                      "Re-run POS ▸ Build Game Scene to pick them up.");
        }

        // ================================================================= shell sprites

        /// <summary>Bliss, from memory: a blue sky gradient, a few soft clouds and one rolling green hill.</summary>
        private static void BuildWallpaper()
        {
            const int w = 512, h = 288;   // 16:9, so it fills the desktop without stretching
            var t = NewTex(w, h);

            // Sky — deep at the zenith, washing out to near-white where it meets the hill.
            var sky = new[]
            {
                (0.00f, Hex(0x1B4FA8)), (0.28f, Hex(0x3E7FCE)), (0.52f, Hex(0x77B2E8)),
                (0.72f, Hex(0xB4DCF6)), (1.00f, Hex(0xE2F2FC)),
            };
            for (int y = 0; y < h; y++)
            {
                var c = Sample(sky, y / (h - 1f));
                for (int x = 0; x < w; x++) Px(t, x, y, c);
            }

            // Clouds — overlapping soft discs, hard-placed so the wallpaper is identical on every machine.
            var clouds = new[]
            {
                (60f, 48f, 26f), (84f, 40f, 20f), (104f, 50f, 16f), (40f, 56f, 15f),
                (330f, 70f, 30f), (362f, 62f, 22f), (392f, 74f, 18f), (306f, 78f, 17f),
                (200f, 30f, 18f), (222f, 36f, 13f),
            };
            foreach (var (cx, cy, r) in clouds)
                for (int y = Mathf.Max(0, (int)(cy - r)); y < Mathf.Min(h, cy + r); y++)
                    for (int x = Mathf.Max(0, (int)(cx - r)); x < Mathf.Min(w, cx + r); x++)
                    {
                        // Squashed vertically — clouds are wider than they are tall.
                        float d = Mathf.Sqrt(Sqr((x - cx) / r) + Sqr((y - cy) / (r * 0.55f)));
                        if (d >= 1f) continue;
                        Blend(t, x, y, Color.white, Mathf.SmoothStep(0f, 1f, (1f - d) * 1.4f) * 0.85f);
                    }

            // Hill — two crests so the horizon is not a straight line, then a grass gradient beneath it.
            var grass = new[]
            {
                (0.00f, Hex(0x9ED04A)), (0.12f, Hex(0x7CB53A)), (0.45f, Hex(0x559222)),
                (0.75f, Hex(0x3D7418)), (1.00f, Hex(0x2E5B12)),
            };
            for (int x = 0; x < w; x++)
            {
                float u = x / (w - 1f);
                float crest = 0.60f * h
                              + Mathf.Sin(u * Mathf.PI * 1.15f + 0.35f) * -26f
                              + Mathf.Sin(u * Mathf.PI * 3.1f) * 5f;
                int top = Mathf.Clamp(Mathf.RoundToInt(crest), 0, h - 1);
                for (int y = top; y < h; y++)
                {
                    var c = Sample(grass, (y - top) / (float)Mathf.Max(1, h - top));
                    Px(t, x, y, c);
                }
                // Sunlit rim along the crest.
                Blend(t, x, top, Color.white, 0.35f);
                Blend(t, x, top + 1, Color.white, 0.15f);
            }

            Save(t, "wallpaper", Vector4.zero, FilterMode.Bilinear);
        }

        /// <summary>Luna caption bar, 28px tall so it is drawn at 1:1 and the gradient never stretches.</summary>
        private static void BuildTitleBars()
        {
            Caption("titlebar", new[]
            {
                (0.00f, Hex(0x3C82E8)), (0.08f, Hex(0x1F63DE)), (0.34f, Hex(0x0B4AD2)),
                (0.58f, Hex(0x0A44C9)), (0.82f, Hex(0x1A5CDC)), (0.94f, Hex(0x4189F0)), (1.00f, Hex(0x0C3196)),
            }, Hex(0x08307F));

            Caption("titlebar_inactive", new[]
            {
                (0.00f, Hex(0xADC3EE)), (0.10f, Hex(0x8FA9E2)), (0.45f, Hex(0x7E97D9)),
                (0.80f, Hex(0x7791D4)), (0.94f, Hex(0x9AB2E6)), (1.00f, Hex(0x6A81C0)),
            }, Hex(0x6076B4));
        }

        private static void Caption(string file, (float, Color)[] stops, Color edge)
        {
            const int w = 40, h = 28;
            var t = NewTex(w, h);
            int[] inset = { 4, 2, 1 };            // rounded top corners, ~3px like the real caption

            for (int y = 0; y < h; y++)
            {
                var c = Sample(stops, y / (h - 1f));
                int cut = y < inset.Length ? inset[y] : 0;
                for (int x = cut; x < w - cut; x++) Px(t, x, y, c);
                // Outline the rounded shoulder so the caption reads as a shape, not a cropped rectangle.
                if (cut > 0) { Px(t, cut, y, edge); Px(t, w - 1 - cut, y, edge); }
            }
            for (int x = inset[0]; x < w - inset[0]; x++) Px(t, x, 0, edge);
            for (int y = inset.Length; y < h; y++) { Px(t, 0, y, edge); Px(t, w - 1, y, edge); }

            Save(t, file, new Vector4(12, 4, 12, 5));
        }

        /// <summary>The taskbar strip: bright band at the top, blue body, dark base line.</summary>
        private static void BuildTaskbar()
        {
            const int w = 8, h = 30;
            var t = NewTex(w, h);
            var body = new[]
            {
                (0.00f, Hex(0x1941A5)), (0.06f, Hex(0x4E93F7)), (0.14f, Hex(0x3A7CEF)),
                (0.30f, Hex(0x2A63E2)), (0.70f, Hex(0x2258D8)), (0.92f, Hex(0x1B49BE)), (1.00f, Hex(0x0C2879)),
            };
            for (int y = 0; y < h; y++)
            {
                var c = Sample(body, y / (h - 1f));
                for (int x = 0; x < w; x++) Px(t, x, y, c);
            }
            for (int x = 0; x < w; x++) Px(t, x, 0, Hex(0x0A2E8C));    // hairline against the desktop
            Save(t, "taskbar", new Vector4(2, 0, 2, 0));
        }

        private static void BuildStartButton()
        {
            StartPill("startbutton", new[]
            {
                (0.00f, Hex(0x53AE33)), (0.07f, Hex(0x86CE5C)), (0.28f, Hex(0x439E24)),
                (0.58f, Hex(0x328A18)), (0.85f, Hex(0x2A7614)), (1.00f, Hex(0x1E5C0E)),
            });
            StartPill("startbutton_pressed", new[]
            {
                (0.00f, Hex(0x2C7614)), (0.10f, Hex(0x3E9022)), (0.40f, Hex(0x2A7014)),
                (0.75f, Hex(0x24630F)), (1.00f, Hex(0x1A520B)),
            });
        }

        private static void StartPill(string file, (float, Color)[] stops)
        {
            const int w = 48, h = 24;
            var t = NewTex(w, h);
            // Gentle left rounding, a pronounced round on the right — the XP Start button's silhouette.
            int[] leftInset = { 3, 2, 1, 1 };
            int[] rightInset = { 6, 4, 3, 2, 1, 1 };

            for (int y = 0; y < h; y++)
            {
                int fromTop = y, fromBottom = h - 1 - y;
                int li = Mathf.Max(Inset(leftInset, fromTop), Inset(leftInset, fromBottom));
                int ri = Mathf.Max(Inset(rightInset, fromTop), Inset(rightInset, fromBottom));
                var c = Sample(stops, y / (h - 1f));
                for (int x = li; x < w - ri; x++) Px(t, x, y, c);
                Blend(t, li, y, Color.black, 0.25f);
                Blend(t, w - 1 - ri, y, Color.black, 0.35f);
            }
            Save(t, file, new Vector4(14, 6, 18, 6));
        }

        private static int Inset(int[] table, int distanceFromEdge) =>
            distanceFromEdge < table.Length ? table[distanceFromEdge] : 0;

        private static void BuildTaskButtons()
        {
            TaskChip("taskbutton", new[]
            {
                (0.00f, Hex(0x6FA9F8)), (0.10f, Hex(0x4A8CF3)), (0.45f, Hex(0x3277EA)),
                (0.80f, Hex(0x2A6ADF)), (1.00f, Hex(0x1E56C4)),
            });
            // The focused window's button is sunken: gradient flipped, so it reads as pressed in.
            TaskChip("taskbutton_active", new[]
            {
                (0.00f, Hex(0x1B4CB4)), (0.20f, Hex(0x2559C6)), (0.60f, Hex(0x3269D8)),
                (0.90f, Hex(0x4179E6)), (1.00f, Hex(0x5B92F0)),
            });
        }

        private static void TaskChip(string file, (float, Color)[] stops)
        {
            const int w = 24, h = 22;
            var t = NewTex(w, h);
            int[] corner = { 3, 1, 1 };
            for (int y = 0; y < h; y++)
            {
                int inset = Mathf.Max(Inset(corner, y), Inset(corner, h - 1 - y));
                var c = Sample(stops, y / (h - 1f));
                for (int x = inset; x < w - inset; x++) Px(t, x, y, c);
                Blend(t, inset, y, Hex(0x11347E), 0.75f);
                Blend(t, w - 1 - inset, y, Hex(0x11347E), 0.75f);
            }
            for (int x = corner[0]; x < w - corner[0]; x++)
            {
                Blend(t, x, 0, Hex(0x11347E), 0.75f);
                Blend(t, x, h - 1, Hex(0x11347E), 0.75f);
            }
            Save(t, file, new Vector4(8, 6, 8, 6));
        }

        /// <summary>Notification area — the lighter blue well at the right end of the taskbar.</summary>
        private static void BuildTray()
        {
            const int w = 16, h = 30;
            var t = NewTex(w, h);
            var stops = new[]
            {
                (0.00f, Hex(0x0E4FB0)), (0.08f, Hex(0x14A0E0)), (0.30f, Hex(0x1494DA)),
                (0.70f, Hex(0x0F84CE)), (0.95f, Hex(0x0B6FBC)), (1.00f, Hex(0x0A4FA0)),
            };
            for (int y = 0; y < h; y++)
            {
                var c = Sample(stops, y / (h - 1f));
                for (int x = 0; x < w; x++) Px(t, x, y, c);
            }
            for (int y = 0; y < h; y++)
            {
                Px(t, 0, y, Hex(0x0A3A93));       // divider against the task buttons
                Px(t, 1, y, Hex(0x1D6FD0));
            }
            Save(t, "tray", new Vector4(4, 0, 4, 0));
        }

        private static void BuildTitleButtons()
        {
            TitleGlyph("titlebtn_close", new[]
            {
                (0.00f, Hex(0xF09A7C)), (0.12f, Hex(0xE2674A)), (0.50f, Hex(0xD1402A)),
                (0.85f, Hex(0xB92E19)), (1.00f, Hex(0xE06A50)),
            }, Glyph.Close);
            TitleGlyph("titlebtn_max", new[]
            {
                (0.00f, Hex(0x9CC6FA)), (0.12f, Hex(0x5B95F0)), (0.50f, Hex(0x3273DE)),
                (0.85f, Hex(0x2159C4)), (1.00f, Hex(0x5A93F0)),
            }, Glyph.Maximise);
            TitleGlyph("titlebtn_min", new[]
            {
                (0.00f, Hex(0x9CC6FA)), (0.12f, Hex(0x5B95F0)), (0.50f, Hex(0x3273DE)),
                (0.85f, Hex(0x2159C4)), (1.00f, Hex(0x5A93F0)),
            }, Glyph.Minimise);
        }

        private enum Glyph { Close, Maximise, Minimise }

        private static void TitleGlyph(string file, (float, Color)[] stops, Glyph glyph)
        {
            const int s = 20;
            var t = NewTex(s, s);
            int[] corner = { 4, 2, 1, 1 };
            for (int y = 0; y < s; y++)
            {
                int inset = Mathf.Max(Inset(corner, y), Inset(corner, s - 1 - y));
                var c = Sample(stops, y / (s - 1f));
                for (int x = inset; x < s - inset; x++) Px(t, x, y, c);
                Blend(t, inset, y, Color.black, 0.28f);
                Blend(t, s - 1 - inset, y, Color.black, 0.28f);
            }

            var ink = Color.white;
            switch (glyph)
            {
                case Glyph.Close:
                    for (int i = 0; i < 8; i++)
                    {
                        Px(t, 6 + i, 6 + i, ink); Px(t, 7 + i, 6 + i, ink);
                        Px(t, 13 - i, 6 + i, ink); Px(t, 12 - i, 6 + i, ink);
                    }
                    break;
                case Glyph.Maximise:
                    for (int x = 5; x <= 14; x++) { Px(t, x, 5, ink); Px(t, x, 6, ink); Px(t, x, 14, ink); }
                    for (int y = 5; y <= 14; y++) { Px(t, 5, y, ink); Px(t, 14, y, ink); }
                    break;
                case Glyph.Minimise:
                    for (int x = 5; x <= 13; x++) { Px(t, x, 12, ink); Px(t, x, 13, ink); }
                    break;
            }
            Save(t, file, Vector4.zero);
        }

        private static void BuildStartLogo()
        {
            var art = new[]
            {
                "                ",
                "                ",
                "    rrr  nnnn   ",
                "   rrrr  nnnn   ",
                "  rrrrr  nnnn   ",
                "  rrrrr  nnnn   ",
                "  rrrrr  nnnn   ",
                "                ",
                "  bbbbb  yyyy   ",
                "  bbbbb  yyyy   ",
                "  bbbbb  yyyy   ",
                "   bbbb  yyyy   ",
                "    bbb  yyyy   ",
                "                ",
                "                ",
                "                ",
            };
            var t = NewTex(32, 32);
            DrawArt(t, art, 2);
            Save(t, "start_logo", Vector4.zero);
        }

        // ================================================================= desktop icons

        /// <summary>
        /// One 32x32 icon per remote app, drawn from 16x16 pixel art doubled up. The keys match
        /// GameUIController.AppKeys, plus two decorative shell icons that only sit on the wallpaper.
        /// </summary>
        private static void BuildIcons()
        {
            foreach (var kv in IconArt)
            {
                var t = NewTex(32, 32);
                DrawArt(t, kv.Value, 2);
                Save(t, "icon_" + kv.Key, Vector4.zero);
            }
        }

        private static readonly Dictionary<string, string[]> IconArt = new()
        {
            ["system"] = new[]
            {
                "                ",
                "  ............  ",
                "  .llllllllll.  ",
                "  .l........l.  ",
                "  .l.cccccc.l.  ",
                "  .l.cwcccc.l.  ",
                "  .l.cccccc.l.  ",
                "  .l.cccccc.l.  ",
                "  .l........l.  ",
                "  .llllllllll.  ",
                "  ............  ",
                "     .llll.     ",
                "     .llll.     ",
                "   ..........   ",
                "   .llllllll.   ",
                "   ..........   ",
            },
            ["network"] = new[]
            {
                "                ",
                " ......         ",
                " .llll.         ",
                " .lccl.         ",
                " ......         ",
                "  .ll.          ",
                " ......         ",
                "   .            ",
                "   ....         ",
                "      .         ",
                "      ......    ",
                "      .llll.    ",
                "      .lccl.    ",
                "      ......    ",
                "       .ll.     ",
                "      ......    ",
            },
            ["possoftware"] = new[]
            {
                "                ",
                "     ......     ",
                "     .cccc.     ",
                "     .cccc.     ",
                "  .........     ",
                "  .lllllll.     ",
                "  .l.....l.     ",
                "  .l.ggg.l.     ",
                "  .l.ggg.l.     ",
                "  .l.....l.     ",
                "  .lllllll.     ",
                "  .........     ",
                "  .yyyyyyy.     ",
                "  .........     ",
                "                ",
                "                ",
            },
            ["terminal"] = new[]
            {
                "                ",
                "    ........    ",
                "    .llllll.    ",
                "    .l....l.    ",
                "    .l.cc.l.    ",
                "    .l.cc.l.    ",
                "    .l....l.    ",
                "    .llllll.    ",
                "    .l....l.    ",
                "    .l.gg.l.    ",
                "    .l.gg.l.    ",
                "    .l.gg.l.    ",
                "    .l....l.    ",
                "    .llllll.    ",
                "    ........    ",
                "                ",
            },
            ["printer"] = new[]
            {
                "                ",
                "                ",
                "    ......      ",
                "    .wwww.      ",
                "    .w..w.      ",
                "  ..........    ",
                "  .llllllll.    ",
                "  .l......l.    ",
                "  .lgggggnl.    ",
                "  .llllllll.    ",
                "  ..........    ",
                "    .wwww.      ",
                "    .w..w.      ",
                "    .wwww.      ",
                "    ......      ",
                "                ",
            },
            ["devicemanager"] = new[]
            {
                "                ",
                "    .  .  .     ",
                "    .  .  .     ",
                "  ..........    ",
                "  .dddddddd.    ",
                ". .dwwwwwwd. .  ",
                ". .dwddddwd. .  ",
                ". .dwddddwd. .  ",
                ". .dwwwwwwd. .  ",
                ". .dddddddd. .  ",
                "  ..........    ",
                "    .  .  .     ",
                "    .  .  .     ",
                "                ",
                "                ",
                "                ",
            },
            ["cashdrawer"] = new[]
            {
                "                ",
                "                ",
                "  ...........   ",
                "  .lllllllll.   ",
                "  .l.......l.   ",
                "  .l.yyyyy.l.   ",
                "  .l.......l.   ",
                "  .lllllllll.   ",
                "  ...........   ",
                "  .lllllllll.   ",
                "  .l..ddd..l.   ",
                "  .lllllllll.   ",
                "  ...........   ",
                "                ",
                "                ",
                "                ",
            },
            ["mycomputer"] = new[]
            {
                "                ",
                "  ............  ",
                "  .llllllllll.  ",
                "  .l........l.  ",
                "  .l.BBBBBB.l.  ",
                "  .l.BccccB.l.  ",
                "  .l.BccccB.l.  ",
                "  .l.BBBBBB.l.  ",
                "  .l........l.  ",
                "  .llllllllll.  ",
                "  ............  ",
                "    .llllll.    ",
                "   ..........   ",
                "   .lggggggl.   ",
                "   .lgggggnl.   ",
                "   ..........   ",
            },
            ["recyclebin"] = new[]
            {
                "                ",
                "     ......     ",
                "     .llll.     ",
                "   ..........   ",
                "   .llllllll.   ",
                "   ..........   ",
                "    .ssssss.    ",
                "    .s.ss.s.    ",
                "    .s.ss.s.    ",
                "    .s.ss.s.    ",
                "    .s.ss.s.    ",
                "    .s.ss.s.    ",
                "    .ssssss.    ",
                "    ........    ",
                "                ",
                "                ",
            },
        };

        private static readonly Dictionary<char, Color> Palette = new()
        {
            [' '] = new Color(0, 0, 0, 0),
            ['.'] = Hex(0x000000),
            ['w'] = Hex(0xFFFFFF),
            ['l'] = Hex(0xD4D0C8),
            ['g'] = Hex(0x808080),
            ['d'] = Hex(0x404040),
            ['s'] = Hex(0xA0A0A8),
            ['b'] = Hex(0x3A6EA5),
            ['B'] = Hex(0x1B3E6E),
            ['c'] = Hex(0x6FC3F0),
            ['y'] = Hex(0xFFD700),
            ['o'] = Hex(0xE08A20),
            ['r'] = Hex(0xC0392B),
            ['n'] = Hex(0x3C8A22),
            ['G'] = Hex(0x7BC456),
        };

        private static void DrawArt(Texture2D t, string[] rows, int scale)
        {
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                {
                    if (!Palette.TryGetValue(rows[y][x], out var c) || c.a <= 0f) continue;
                    for (int dy = 0; dy < scale; dy++)
                        for (int dx = 0; dx < scale; dx++)
                            Px(t, x * scale + dx, y * scale + dy, c);
                }
        }

        // ================================================================= skin asset

        [MenuItem("POS/Rebuild XP Skin Asset")]
        public static void RebuildSkin()
        {
            var skin = AssetDatabase.LoadAssetAtPath<XPSkin>(SkinPath);
            if (skin == null)
            {
                skin = ScriptableObject.CreateInstance<XPSkin>();
                AssetDatabase.CreateAsset(skin, SkinPath);
            }

            skin.windowBase = Pack("Window_Base");
            skin.windowHeader = Pack("Window_Header");
            skin.windowHeaderInactive = Pack("Window_Header_Inactive");
            skin.button = Pack("Windows_Button");
            skin.buttonFocus = Pack("Windows_Button_Focus");
            skin.buttonPressed = Pack("Windows_Button_Pressed");
            skin.buttonInactive = Pack("Windows_Button_Inactive");
            skin.innerFrame = Pack("Windows_Inner_Frame");
            skin.innerFrameInverted = Pack("Windows_Inner_Frame_Inverted");
            skin.sliderBackground = Pack("Windows_Slider_Background");
            skin.sliderHandle = Pack("Windows_Slider_Handle");

            skin.wallpaper = Art("wallpaper");
            skin.titleBar = Art("titlebar");
            skin.titleBarInactive = Art("titlebar_inactive");
            skin.taskbar = Art("taskbar");
            skin.startButton = Art("startbutton");
            skin.startButtonPressed = Art("startbutton_pressed");
            skin.taskButton = Art("taskbutton");
            skin.taskButtonActive = Art("taskbutton_active");
            skin.tray = Art("tray");
            skin.closeButton = Art("titlebtn_close");
            skin.minimizeButton = Art("titlebtn_min");
            skin.maximizeButton = Art("titlebtn_max");
            skin.startLogo = Art("start_logo");

            var icons = new List<XPSkin.IconEntry>();
            foreach (var key in IconArt.Keys)
                icons.Add(new XPSkin.IconEntry { key = key, sprite = Art("icon_" + key) });
            skin.icons = icons.ToArray();

            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssets();
            Debug.Log($"[XPSpriteGenerator] {SkinPath} now references {icons.Count} icons + the Retro Windows GUI chrome.");
        }

        private static Sprite Pack(string file) => AssetDatabase.LoadAssetAtPath<Sprite>($"{PackDir}/{file}.png");
        private static Sprite Art(string file) => AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtDir}/{file}.png");

        // ================================================================= drawing helpers

        private static Texture2D NewTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var clear = new Color[w * h];
            t.SetPixels(clear);   // default Color is (0,0,0,0)
            return t;
        }

        /// <summary>Writes with y measured from the TOP, which is how the art above is authored.</summary>
        private static void Px(Texture2D t, int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= t.width || y >= t.height) return;
            t.SetPixel(x, t.height - 1 - y, c);
        }

        private static void Blend(Texture2D t, int x, int y, Color c, float alpha)
        {
            if (x < 0 || y < 0 || x >= t.width || y >= t.height) return;
            var under = t.GetPixel(x, t.height - 1 - y);
            var over = Color.Lerp(under, c, alpha);
            over.a = Mathf.Max(under.a, alpha);
            t.SetPixel(x, t.height - 1 - y, over);
        }

        private static Color Sample((float pos, Color color)[] stops, float t)
        {
            if (t <= stops[0].pos) return stops[0].color;
            for (int i = 1; i < stops.Length; i++)
            {
                if (t > stops[i].pos) continue;
                float span = Mathf.Max(0.0001f, stops[i].pos - stops[i - 1].pos);
                return Color.Lerp(stops[i - 1].color, stops[i].color, (t - stops[i - 1].pos) / span);
            }
            return stops[^1].color;
        }

        private static Color Hex(uint rgb) =>
            new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        private static float Sqr(float v) => v * v;

        private static void Save(Texture2D t, string file, Vector4 border, FilterMode filter = FilterMode.Point)
        {
            t.Apply();
            string path = $"{ArtDir}/{file}.png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is not TextureImporter imp) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = 100f;
            imp.spriteBorder = border;
            imp.filterMode = filter;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.alphaIsTransparency = true;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
    }
}
