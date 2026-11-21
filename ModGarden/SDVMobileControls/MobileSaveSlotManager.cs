using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace SDVMobileControls
{
    internal sealed class SaveSlotInfo
    {
        public string SlotId { get; set; }
        public string SaveFolderName { get; set; }
        public string PlayerName { get; set; }
        public string LocationName { get; set; }
        public string Season { get; set; }
        public int DayOfMonth { get; set; }
        public int Year { get; set; }
        public int TimeOfDay { get; set; }
        public int Money { get; set; }
        public int Health { get; set; }
        public int Stamina { get; set; }
        public int MineLevel { get; set; }
        public string SavedAtText { get; set; }
        public string SlotDir { get; set; }

        public string FormatGameTime()
        {
            int hh = (TimeOfDay / 100) % 24;
            int mm = TimeOfDay % 100;
            string ap = hh >= 12 && hh < 24 ? "PM" : "AM";
            int h12 = hh % 12;
            if (h12 == 0) h12 = 12;
            return $"{h12}:{mm:00}{ap}";
        }

        public string FormatHeaderLine()
        {
            string sShort = string.IsNullOrEmpty(Season) ? "SPR" : Season.ToUpperInvariant();
            if (sShort.Length > 3) sShort = sShort.Substring(0, 3);
            string loc = string.IsNullOrEmpty(LocationName) ? "FARM" : LocationName.ToUpperInvariant();
            if (MineLevel > 0 && loc.IndexOf("MINE", StringComparison.OrdinalIgnoreCase) >= 0)
                loc = $"MINE F{MineLevel}";
            if (loc.Length > 18) loc = loc.Substring(0, 18);
            return $"{sShort} {DayOfMonth} Y{Year} {FormatGameTime()} - {loc}";
        }

        public string FormatDetailLine()
        {
            string when = string.IsNullOrEmpty(SavedAtText) ? "" : $" | {SavedAtText}";
            return $"{Money}G | HP {Health} | EN {Stamina}{when}";
        }
    }

    internal sealed class MobileSaveSlotManager
    {
        private readonly IMonitor _monitor;
        private readonly string _slotsRoot;
        private SaveSlotInfo _pendingMeta;
        private string _pendingQuickSaveJsonPath;
        private readonly List<SaveSlotInfo> _cachedSlots = new List<SaveSlotInfo>();
        private string _cachedForSaveFolder;

        public MobileSaveSlotManager(string modDir, IMonitor monitor)
        {
            _monitor = monitor;
            _slotsRoot = Path.Combine(modDir, "SaveSlots");
            try { Directory.CreateDirectory(_slotsRoot); } catch { }
        }

        public IReadOnlyList<SaveSlotInfo> GetSlotsForCurrentSave(bool forceRefresh = false)
        {
            string folder = GetCurrentSaveFolderName();
            if (!forceRefresh && folder == _cachedForSaveFolder)
                return _cachedSlots;

            _cachedSlots.Clear();
            _cachedForSaveFolder = folder;
            if (string.IsNullOrEmpty(folder))
                return _cachedSlots;

            string charDir = Path.Combine(_slotsRoot, SanitizeName(folder));
            if (!Directory.Exists(charDir))
                return _cachedSlots;

            try
            {
                string[] dirs = Directory.GetDirectories(charDir);
                Array.Sort(dirs, (a, b) => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)));
                foreach (string d in dirs)
                {
                    string metaPath = Path.Combine(d, "meta.txt");
                    if (!File.Exists(metaPath))
                        continue;
                    SaveSlotInfo info = ReadMeta(metaPath, d);
                    if (info != null)
                        _cachedSlots.Add(info);
                }
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[SaveSlots] Error listing slots: {ex.Message}", LogLevel.Warn);
            }

            return _cachedSlots;
        }

        public void BeginPendingSnapshot(PadBridge bridge)
        {
            string folder = GetCurrentSaveFolderName();
            if (string.IsNullOrEmpty(folder) || Game1.player == null)
                return;

            int mineLvl = 0;
            try
            {
                if (Game1.currentLocation is MineShaft ms)
                    mineLvl = ms.mineLevel;
            }
            catch { }

            string loc = Game1.currentLocation?.DisplayName;
            if (string.IsNullOrEmpty(loc))
                loc = Game1.currentLocation?.Name ?? "Farm";

            DateTime now = DateTime.Now;
            _pendingMeta = new SaveSlotInfo
            {
                SlotId = now.ToString("yyyyMMdd_HHmmss_fff"),
                SaveFolderName = folder,
                PlayerName = Game1.player.Name ?? "Farmer",
                LocationName = StripNonAsciiForOverlay(loc),
                Season = Game1.currentSeason ?? "spring",
                DayOfMonth = Game1.dayOfMonth,
                Year = Game1.year,
                TimeOfDay = Game1.timeOfDay,
                Money = Game1.player.Money,
                Health = Game1.player.health,
                Stamina = (int)Math.Round(Game1.player.Stamina),
                MineLevel = mineLvl,
                SavedAtText = now.ToString("HH:mm dd/MM")
            };
            _pendingQuickSaveJsonPath = bridge?.GetQuickSaveStateFilePath(folder);
        }

        public void CancelPendingSnapshot()
        {
            _pendingMeta = null;
            _pendingQuickSaveJsonPath = null;
        }

        public bool FinalizePendingSnapshot(out string savedSummary)
        {
            savedSummary = null;
            if (_pendingMeta == null)
                return false;

            SaveSlotInfo meta = _pendingMeta;
            string qsPath = _pendingQuickSaveJsonPath;
            _pendingMeta = null;
            _pendingQuickSaveJsonPath = null;

            try
            {
                string saveDir = Constants.CurrentSavePath;
                if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir))
                    return false;

                string charDir = Path.Combine(_slotsRoot, SanitizeName(meta.SaveFolderName));
                string slotDir = Path.Combine(charDir, meta.SlotId);
                string filesDir = Path.Combine(slotDir, "save_files");
                Directory.CreateDirectory(filesDir);

                int copied = 0;
                foreach (string src in Directory.GetFiles(saveDir))
                {
                    string fn = Path.GetFileName(src);
                    if (fn.EndsWith("_old", StringComparison.OrdinalIgnoreCase) || fn.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                        continue;
                    File.Copy(src, Path.Combine(filesDir, fn), overwrite: true);
                    copied++;
                }

                if (copied == 0)
                    return false;

                if (!string.IsNullOrEmpty(qsPath) && File.Exists(qsPath))
                    File.Copy(qsPath, Path.Combine(slotDir, "quicksave.json"), overwrite: true);

                meta.SlotDir = slotDir;
                WriteMeta(Path.Combine(slotDir, "meta.txt"), meta);
                PruneOldSlots(charDir, maxKeep: 20);
                GetSlotsForCurrentSave(forceRefresh: true);

                savedSummary = meta.FormatHeaderLine();
                _monitor?.Log($"[SaveSlots] Saved snapshot '{meta.SlotId}': {savedSummary} ({copied} files)", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[SaveSlots] Failed to finalize snapshot: {ex.Message}", LogLevel.Warn);
                return false;
            }
        }

        public bool RestoreAndLoadSlot(SaveSlotInfo slot, PadBridge bridge, out string message)
        {
            message = "KHONG THE TAI BAN LUU";
            if (slot == null || string.IsNullOrEmpty(slot.SlotDir) || bridge == null)
                return false;

            try
            {
                string filesDir = Path.Combine(slot.SlotDir, "save_files");
                string targetSaveDir = Constants.CurrentSavePath;
                if (!Directory.Exists(filesDir) || string.IsNullOrEmpty(targetSaveDir) || !Directory.Exists(targetSaveDir))
                {
                    message = "THIEU FILE BAN LUU";
                    return false;
                }

                // 1. Copy snapshot save XMLs back into the live character save folder
                foreach (string src in Directory.GetFiles(filesDir))
                {
                    string fn = Path.GetFileName(src);
                    string dst = Path.Combine(targetSaveDir, fn);
                    string tmp = dst + ".restore_tmp";
                    File.Copy(src, tmp, overwrite: true);
                    if (File.Exists(dst))
                        File.Delete(dst);
                    File.Move(tmp, dst);
                }

                // 2. Restore snapshot quicksave.json so QuickSaveManager restores exact time/location/tile/HP/energy
                string snapQs = Path.Combine(slot.SlotDir, "quicksave.json");
                string liveQs = bridge.GetQuickSaveStateFilePath(slot.SaveFolderName);
                if (!string.IsNullOrEmpty(liveQs) && File.Exists(snapQs))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(liveQs));
                    File.Copy(snapQs, liveQs, overwrite: true);
                    bridge.InvalidateQuickSaveCache();
                }

                // 3. Trigger QuickSaveManager.QuickLoad() for seamless clean reload!
                return bridge.TryQuickLoad(out message);
            }
            catch (Exception ex)
            {
                message = "LOI TAI: " + ex.Message;
                _monitor?.Log($"[SaveSlots] RestoreAndLoadSlot error: {ex}", LogLevel.Warn);
                return false;
            }
        }

        public bool DeleteSlot(SaveSlotInfo slot)
        {
            if (slot == null || string.IsNullOrEmpty(slot.SlotDir))
                return false;
            try
            {
                if (Directory.Exists(slot.SlotDir))
                    Directory.Delete(slot.SlotDir, recursive: true);
                GetSlotsForCurrentSave(forceRefresh: true);
                return true;
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[SaveSlots] DeleteSlot error: {ex.Message}", LogLevel.Warn);
                return false;
            }
        }

        private static void PruneOldSlots(string charDir, int maxKeep)
        {
            try
            {
                string[] dirs = Directory.GetDirectories(charDir);
                if (dirs.Length <= maxKeep) return;
                Array.Sort(dirs, (a, b) => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)));
                for (int i = maxKeep; i < dirs.Length; i++)
                {
                    try { Directory.Delete(dirs[i], recursive: true); } catch { }
                }
            }
            catch { }
        }

        private static string GetCurrentSaveFolderName()
        {
            if (!string.IsNullOrEmpty(Constants.SaveFolderName))
                return Constants.SaveFolderName;
            if (Game1.player != null && Game1.uniqueIDForThisGame != 0)
                return $"{Game1.player.Name}_{Game1.uniqueIDForThisGame}";
            return null;
        }

        private static string SanitizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "default";
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            return sb.Length > 0 ? sb.ToString() : "default";
        }

        private static string StripNonAsciiForOverlay(string s)
        {
            if (string.IsNullOrEmpty(s)) return "FARM";
            // OverlayFont renders crisp ASCII glyphs; strip diacritics cleanly while keeping readable names
            string norm = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(norm.Length);
            for (int i = 0; i < norm.Length; i++)
            {
                char c = norm[i];
                if (c == '\u0111') { sb.Append('d'); continue; }
                if (c == '\u0110') { sb.Append('D'); continue; }
                if (c >= 32 && c <= 126) sb.Append(c);
            }
            string res = sb.ToString().Trim();
            return res.Length > 0 ? res : "FARM";
        }

        private static void WriteMeta(string path, SaveSlotInfo m)
        {
            var sb = new StringBuilder();
            sb.AppendLine("slotId=" + (m.SlotId ?? ""));
            sb.AppendLine("saveFolder=" + (m.SaveFolderName ?? ""));
            sb.AppendLine("player=" + (m.PlayerName ?? ""));
            sb.AppendLine("location=" + (m.LocationName ?? ""));
            sb.AppendLine("season=" + (m.Season ?? "spring"));
            sb.AppendLine("day=" + m.DayOfMonth);
            sb.AppendLine("year=" + m.Year);
            sb.AppendLine("time=" + m.TimeOfDay);
            sb.AppendLine("money=" + m.Money);
            sb.AppendLine("health=" + m.Health);
            sb.AppendLine("stamina=" + m.Stamina);
            sb.AppendLine("mineLevel=" + m.MineLevel);
            sb.AppendLine("savedAt=" + (m.SavedAtText ?? ""));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static SaveSlotInfo ReadMeta(string path, string slotDir)
        {
            try
            {
                var info = new SaveSlotInfo { SlotDir = slotDir, SlotId = Path.GetFileName(slotDir) };
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq).Trim();
                    string v = raw.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "slotId": info.SlotId = v; break;
                        case "saveFolder": info.SaveFolderName = v; break;
                        case "player": info.PlayerName = v; break;
                        case "location": info.LocationName = v; break;
                        case "season": info.Season = v; break;
                        case "day": int.TryParse(v, out int d); info.DayOfMonth = d; break;
                        case "year": int.TryParse(v, out int y); info.Year = y; break;
                        case "time": int.TryParse(v, out int t); info.TimeOfDay = t; break;
                        case "money": int.TryParse(v, out int mo); info.Money = mo; break;
                        case "health": int.TryParse(v, out int hp); info.Health = hp; break;
                        case "stamina": int.TryParse(v, out int st); info.Stamina = st; break;
                        case "mineLevel": int.TryParse(v, out int ml); info.MineLevel = ml; break;
                        case "savedAt": info.SavedAtText = v; break;
                    }
                }
                return info;
            }
            catch
            {
                return null;
            }
        }
    }
}
