using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SDVMobileControls
{
    public enum MobileControlMode
    {
        VirtualGamepad = 0,        // Original SDViOSTouchControls gamepad overlay
        InvisibleJoystick2Btn = 1, // Floating invisible joystick (left rect) + 2 tall vertical rectangular zones (right)
        VisibleJoystick2Btn = 2,   // Clean minimal joystick + 2 circular buttons
        TapToMove = 3,             // Tap-to-Move A* + Hold-to-Walk + Tap-to-Interact
        TapAndJoystick = 4         // Hybrid: Tap-to-Move + Floating invisible joystick + 2 vertical rectangular zones
    }

    public sealed class CustomKeyButton
    {
        public int Id { get; set; }
        public string Label { get; set; } = "P";
        public int KeyCode { get; set; } = 80; // Keys.P
        public float X { get; set; } = 0.84f;
        public float Y { get; set; } = 0.05f;
        public float W { get; set; } = 0.068f;
        public float H { get; set; } = 0.105f;
    }

    public sealed class MobileConfig
    {
        public MobileControlMode Mode { get; set; } = MobileControlMode.InvisibleJoystick2Btn;
        public bool AutoTool { get; set; } = true;
        public bool AutoAttack { get; set; } = false; // Default OFF
        public bool PinchZoom { get; set; } = true;
        public bool ShowTouchHint { get; set; } = false; // Default OFF (no flashing on invisible buttons!)
        public bool ShowTopUtility { get; set; } = true;
        public bool SwapRightButtons { get; set; } = false;
        public bool MobileShopAndDrag { get; set; } = true; // Mobile Shop quantity slider + Drag-and-Drop + Chest 1-tap transfer
        public bool AutoKeyboard { get; set; } = true;      // Auto-open iOS keyboard when tapping any TextBox
        public bool SafeCrafting { get; set; } = true;      // Hover-first 2-tap safe crafting/cooking/bundles
        public int AutoSaveMinutes { get; set; } = 0;       // 0 = OFF (default, zero stutter), 3 / 5 / 10 minutes periodic AutoSave slot
        public bool ReduceWeather { get; set; } = true;     // Cap excessive rain/wind debris particles for smoother FPS
        public float UiScaleOverride { get; set; } = 0.0f;  // 0 = game default, or 0.75..1.50 live UI scale for PC mod menus
        public float ButtonScale { get; set; } = 1.0f;

        // Normalized layout (0..1 of viewport) for Invisible Mode: Left Joystick Rect + 2 Right Vertical Rectangular Bars
        public float InvJoyX { get; set; }
        public float InvJoyY { get; set; }
        public float InvJoyW { get; set; }
        public float InvJoyH { get; set; }

        public float InvToolX { get; set; }
        public float InvToolY { get; set; }
        public float InvToolW { get; set; }
        public float InvToolH { get; set; }

        public float InvActX { get; set; }
        public float InvActY { get; set; }
        public float InvActW { get; set; }
        public float InvActH { get; set; }

        // Normalized layout (0..1) for Visible 2-Button Mode
        public float VisJoyX { get; set; }
        public float VisJoyY { get; set; }
        public float VisJoyR { get; set; }

        public float VisToolX { get; set; }
        public float VisToolY { get; set; }
        public float VisToolR { get; set; }

        public float VisActX { get; set; }
        public float VisActY { get; set; }
        public float VisActR { get; set; }

        // Top utility bar normalized top-left
        public float TopBarX { get; set; }
        public float TopBarY { get; set; }

        // User-added custom PC keyboard buttons (movable & resizable in Edit Layout!)
        public List<CustomKeyButton> CustomKeys { get; } = new List<CustomKeyButton>();

        private readonly string _path;
        private readonly string _editFlagPath;
        private readonly string _ramAlertPath;
        private DateTime _lastWriteUtc = DateTime.MinValue;
        private int _pollCounter;
        private int _flagCounter;
        private int _ramAlertCounter;

        public MobileConfig()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            _path = Path.Combine(docs, "sdv_mobile_ctrl.cfg");
            _editFlagPath = Path.Combine(docs, "sdv_mobile_edit.flag");
            _ramAlertPath = Path.Combine(docs, "sdv_ram_alert.txt");
            ResetLayoutDefaults();
            Reload(force: true);
        }

        public void ResetLayoutDefaults()
        {
            InvJoyX = 0.02f;  InvJoyY = 0.22f;  InvJoyW = 0.42f;  InvJoyH = 0.74f;
            InvToolX = 0.72f; InvToolY = 0.20f; InvToolW = 0.125f; InvToolH = 0.74f;
            InvActX = 0.855f; InvActY = 0.20f;  InvActW = 0.125f;  InvActH = 0.74f;

            VisJoyX = 0.15f;  VisJoyY = 0.76f;  VisJoyR = 0.115f;
            VisToolX = 0.79f; VisToolY = 0.80f; VisToolR = 0.075f;
            VisActX = 0.90f;  VisActY = 0.70f;  VisActR = 0.075f;

            TopBarX = 0.055f; TopBarY = 0.028f;
        }

        public CustomKeyButton AddCustomKey(string label, int keyCode)
        {
            if (CustomKeys.Count >= 16)
                return null;

            int nextId = 1;
            foreach (var b in CustomKeys)
                if (b.Id >= nextId) nextId = b.Id + 1;

            float x = 0.56f;
            float y = 0.16f;
            for (int slot = 0; slot < 16; slot++)
            {
                int col = slot % 4;
                int row = slot / 4;
                float cx = Math.Clamp(0.56f + col * 0.085f, 0.05f, 0.90f);
                float cy = Math.Clamp(0.16f + row * 0.135f, 0.08f, 0.84f);
                bool overlap = false;
                for (int i = 0; i < CustomKeys.Count; i++)
                {
                    if (Math.Abs(CustomKeys[i].X - cx) < 0.045f && Math.Abs(CustomKeys[i].Y - cy) < 0.065f)
                    {
                        overlap = true;
                        break;
                    }
                }
                if (!overlap)
                {
                    x = cx;
                    y = cy;
                    break;
                }
            }

            string cleanLabel = SanitizeLabel(label);
            var btn = new CustomKeyButton
            {
                Id = nextId,
                Label = cleanLabel,
                KeyCode = keyCode,
                X = x,
                Y = y,
                W = 0.068f,
                H = 0.105f
            };
            CustomKeys.Add(btn);
            Save();
            return btn;
        }

        public bool RemoveCustomKey(int id)
        {
            for (int i = 0; i < CustomKeys.Count; i++)
            {
                if (CustomKeys[i].Id == id)
                {
                    CustomKeys.RemoveAt(i);
                    Save();
                    return true;
                }
            }
            return false;
        }

        private static string SanitizeLabel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "KEY";
            string s = raw.Trim().Replace("|", "").Replace("\n", "").Replace("\r", "");
            if (s.Length > 6) s = s.Substring(0, 6);
            return s.ToUpperInvariant();
        }

        public bool ConsumeEditLayoutFlag()
        {
            if (++_flagCounter < 8)
                return false;
            _flagCounter = 0;
            try
            {
                if (!File.Exists(_editFlagPath))
                    return false;
                File.Delete(_editFlagPath);
                Reload(force: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryConsumeRamAlertFlag(out string alertMsg)
        {
            alertMsg = null;
            if (++_ramAlertCounter < 60)
                return false;
            _ramAlertCounter = 0;
            try
            {
                if (!File.Exists(_ramAlertPath))
                    return false;
                alertMsg = File.ReadAllText(_ramAlertPath)?.Trim();
                File.Delete(_ramAlertPath);
                return !string.IsNullOrEmpty(alertMsg);
            }
            catch
            {
                return false;
            }
        }

        public void PollReload()
        {
            if (++_pollCounter < 25)
                return;
            _pollCounter = 0;
            Reload(force: false);
        }

        public void Reload(bool force)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    if (force)
                        Save();
                    return;
                }

                DateTime mtime = File.GetLastWriteTimeUtc(_path);
                if (!force && mtime == _lastWriteUtc)
                    return;
                _lastWriteUtc = mtime;

                string[] lines = File.ReadAllLines(_path);
                CustomKeys.Clear();
                bool sawTouchHint2 = false;

                foreach (string raw in lines)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;
                    int eq = raw.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    string k = raw.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = raw.Substring(eq + 1).Trim();

                    switch (k)
                    {
                        case "mode":
                            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) && m >= 0 && m <= 4)
                                Mode = (MobileControlMode)m;
                            break;
                        case "autotool": AutoTool = ParseBool(v, true); break;
                        case "autoattack": AutoAttack = ParseBool(v, false); break;
                        case "pinchzoom": PinchZoom = ParseBool(v, true); break;
                        case "touchhint2":
                            ShowTouchHint = ParseBool(v, false);
                            sawTouchHint2 = true;
                            break;
                        case "toputility": ShowTopUtility = ParseBool(v, true); break;
                        case "swapbuttons": SwapRightButtons = ParseBool(v, false); break;
                        case "mobileshopdrag": MobileShopAndDrag = ParseBool(v, true); break;
                        case "autokeyboard": AutoKeyboard = ParseBool(v, true); break;
                        case "safecrafting": SafeCrafting = ParseBool(v, true); break;
                        case "autosavemin":
                            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int am) && (am == 0 || am == 3 || am == 5 || am == 10))
                                AutoSaveMinutes = am;
                            break;
                        case "reduceweather": ReduceWeather = ParseBool(v, true); break;
                        case "uiscale":
                            float us = ParseFloat(v, 0.0f, 0.0f, 1.50f);
                            UiScaleOverride = (us >= 0.70f) ? Math.Clamp(us, 0.75f, 1.50f) : 0.0f;
                            break;
                        case "buttonscale": ButtonScale = ParseFloat(v, 1.0f, 0.75f, 1.45f); break;

                        case "inv_joy_x": InvJoyX = ParseFloat(v, InvJoyX, 0.0f, 0.90f); break;
                        case "inv_joy_y": InvJoyY = ParseFloat(v, InvJoyY, 0.0f, 0.90f); break;
                        case "inv_joy_w": InvJoyW = ParseFloat(v, InvJoyW, 0.12f, 0.75f); break;
                        case "inv_joy_h": InvJoyH = ParseFloat(v, InvJoyH, 0.18f, 0.95f); break;

                        case "inv_tool_x": InvToolX = ParseFloat(v, InvToolX, 0.0f, 0.94f); break;
                        case "inv_tool_y": InvToolY = ParseFloat(v, InvToolY, 0.0f, 0.90f); break;
                        case "inv_tool_w": InvToolW = ParseFloat(v, InvToolW, 0.055f, 0.45f); break;
                        case "inv_tool_h": InvToolH = ParseFloat(v, InvToolH, 0.12f, 0.95f); break;

                        case "inv_act_x": InvActX = ParseFloat(v, InvActX, 0.0f, 0.94f); break;
                        case "inv_act_y": InvActY = ParseFloat(v, InvActY, 0.0f, 0.90f); break;
                        case "inv_act_w": InvActW = ParseFloat(v, InvActW, 0.055f, 0.45f); break;
                        case "inv_act_h": InvActH = ParseFloat(v, InvActH, 0.12f, 0.95f); break;

                        case "vis_joy_x": VisJoyX = ParseFloat(v, VisJoyX, 0.05f, 0.95f); break;
                        case "vis_joy_y": VisJoyY = ParseFloat(v, VisJoyY, 0.08f, 0.95f); break;
                        case "vis_joy_r": VisJoyR = ParseFloat(v, VisJoyR, 0.07f, 0.22f); break;

                        case "vis_tool_x": VisToolX = ParseFloat(v, VisToolX, 0.05f, 0.95f); break;
                        case "vis_tool_y": VisToolY = ParseFloat(v, VisToolY, 0.08f, 0.95f); break;
                        case "vis_tool_r": VisToolR = ParseFloat(v, VisToolR, 0.045f, 0.16f); break;

                        case "vis_act_x": VisActX = ParseFloat(v, VisActX, 0.05f, 0.95f); break;
                        case "vis_act_y": VisActY = ParseFloat(v, VisActY, 0.08f, 0.95f); break;
                        case "vis_act_r": VisActR = ParseFloat(v, VisActR, 0.045f, 0.16f); break;

                        case "topbar_x": TopBarX = ParseFloat(v, TopBarX, 0.01f, 0.85f); break;
                        case "topbar_y": TopBarY = ParseFloat(v, TopBarY, 0.005f, 0.90f); break;

                        case "ckey":
                            TryParseCustomKey(v);
                            break;
                    }
                }

                if (!sawTouchHint2)
                    ShowTouchHint = false;
            }
            catch
            {
                // Keep previous safe settings
            }
        }

        private void TryParseCustomKey(string v)
        {
            if (CustomKeys.Count >= 16) return;
            string[] parts = v.Split('|');
            if (parts.Length < 7) return;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) return;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kc) || kc <= 0) return;

            CustomKeys.Add(new CustomKeyButton
            {
                Id = id,
                Label = SanitizeLabel(parts[1]),
                KeyCode = kc,
                X = ParseFloat(parts[3], 0.80f, 0.0f, 0.95f),
                Y = ParseFloat(parts[4], 0.15f, 0.0f, 0.92f),
                W = ParseFloat(parts[5], 0.068f, 0.035f, 0.35f),
                H = ParseFloat(parts[6], 0.105f, 0.050f, 0.45f)
            });
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                string F(float x) => x.ToString("0.000", CultureInfo.InvariantCulture);
                var sb = new StringBuilder(512);
                sb.AppendLine($"mode={(int)Mode}");
                sb.AppendLine($"autotool={(AutoTool ? 1 : 0)}");
                sb.AppendLine($"autoattack={(AutoAttack ? 1 : 0)}");
                sb.AppendLine($"pinchzoom={(PinchZoom ? 1 : 0)}");
                sb.AppendLine($"touchhint2={(ShowTouchHint ? 1 : 0)}");
                sb.AppendLine($"toputility={(ShowTopUtility ? 1 : 0)}");
                sb.AppendLine($"swapbuttons={(SwapRightButtons ? 1 : 0)}");
                sb.AppendLine($"mobileshopdrag={(MobileShopAndDrag ? 1 : 0)}");
                sb.AppendLine($"autokeyboard={(AutoKeyboard ? 1 : 0)}");
                sb.AppendLine($"safecrafting={(SafeCrafting ? 1 : 0)}");
                sb.AppendLine($"autosavemin={AutoSaveMinutes}");
                sb.AppendLine($"reduceweather={(ReduceWeather ? 1 : 0)}");
                sb.AppendLine($"uiscale={UiScaleOverride.ToString("0.00", CultureInfo.InvariantCulture)}");
                sb.AppendLine($"buttonscale={ButtonScale.ToString("0.00", CultureInfo.InvariantCulture)}");
                sb.AppendLine($"inv_joy_x={F(InvJoyX)}");
                sb.AppendLine($"inv_joy_y={F(InvJoyY)}");
                sb.AppendLine($"inv_joy_w={F(InvJoyW)}");
                sb.AppendLine($"inv_joy_h={F(InvJoyH)}");
                sb.AppendLine($"inv_tool_x={F(InvToolX)}");
                sb.AppendLine($"inv_tool_y={F(InvToolY)}");
                sb.AppendLine($"inv_tool_w={F(InvToolW)}");
                sb.AppendLine($"inv_tool_h={F(InvToolH)}");
                sb.AppendLine($"inv_act_x={F(InvActX)}");
                sb.AppendLine($"inv_act_y={F(InvActY)}");
                sb.AppendLine($"inv_act_w={F(InvActW)}");
                sb.AppendLine($"inv_act_h={F(InvActH)}");
                sb.AppendLine($"vis_joy_x={F(VisJoyX)}");
                sb.AppendLine($"vis_joy_y={F(VisJoyY)}");
                sb.AppendLine($"vis_joy_r={F(VisJoyR)}");
                sb.AppendLine($"vis_tool_x={F(VisToolX)}");
                sb.AppendLine($"vis_tool_y={F(VisToolY)}");
                sb.AppendLine($"vis_tool_r={F(VisToolR)}");
                sb.AppendLine($"vis_act_x={F(VisActX)}");
                sb.AppendLine($"vis_act_y={F(VisActY)}");
                sb.AppendLine($"vis_act_r={F(VisActR)}");
                sb.AppendLine($"topbar_x={F(TopBarX)}");
                sb.AppendLine($"topbar_y={F(TopBarY)}");

                foreach (var b in CustomKeys)
                {
                    sb.AppendLine($"ckey={b.Id}|{SanitizeLabel(b.Label)}|{b.KeyCode}|{F(b.X)}|{F(b.Y)}|{F(b.W)}|{F(b.H)}");
                }

                File.WriteAllText(_path, sb.ToString());
                _lastWriteUtc = File.GetLastWriteTimeUtc(_path);
            }
            catch
            {
                // Ignore transient I/O errors
            }
        }

        private static bool ParseBool(string v, bool fallback)
        {
            if (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        private static float ParseFloat(string v, float fallback, float min, float max)
        {
            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                return Math.Clamp(f, min, max);
            return fallback;
        }
    }
}
