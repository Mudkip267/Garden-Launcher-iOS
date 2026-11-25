using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.Menus;
using StardewValley.Mods;
using StardewValley.Tools;

namespace SDVMobileControls
{
    public sealed class ModEntry : Mod
    {
        internal static MobileControlsEngine Engine;
        internal static MapRamOptimizer RamOptimizer;
        internal static ExternalInputBridge ExternalInput;
        private static IMonitor _monitor;
        private static readonly HashSet<Type> BoundPadTypes = new HashSet<Type>();
        private static readonly HashSet<Type> PatchedTouchHookTypes = new HashSet<Type>();
        private static PropertyInfo _delegatingParentProp;
        private static MethodInfo _modHooksUpdateControlInput;
        private static int _errCount;
        private static int _retryTicks;
        private static int _audioDiagTicks;
        private static bool _audioDiagRanLaunch;
        private static bool _audioDiagRanWorld;
        private static bool _audioDiagWroteThisSession;
        private static bool _harmonyPatchesInstalled;
        private static bool _extHarmonyPatchesInstalled;
        private static long _fishingHoldStartMs;
        private static FishingRod _fishingHoldRod;
        private static int _fishingTraceCount;
        private static int _shaderDiagSaveTicks;
        private static bool _shaderDiagWaitingSave;
        private static bool _shaderDiagRanOutdoor;
        private static string _bootDocsDir;
        private static bool _bootGameLaunched;
        private static bool _bootReadySignaled;
        private static int _bootRenderedFrames;
        private static int _bootAssetLoadedCount;
        private static long _bootLastAssetReportMs;
        private static bool _bootNativeResolved;
        private static BootReportDelegate _bootNativeReport;
        internal static bool SynthesizingTapAction;
        private static readonly List<HUDMessage> _stashedHudMessages = new List<HUDMessage>(8);
        private static bool _hudMessagesStashed;
        private static FieldInfo _soundBankSoundsField;
        private static FieldInfo _game1HooksField;
        private static bool _gardenHooksInstalled;
        private static Dictionary<string, string> _vanillaBaseMailCache;
        private static bool _vanillaBaseMailLoaded;
        private static readonly List<string> _lastMailboxSnapshot = new List<string>(8);
        private static int _lastMailboxDayKey = -1;
        private static bool _mfmResolved;
        private static MethodInfo _mfmShowLetterMethod;
        private static readonly HashSet<string> _pendingPortraitKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _pendingReloadAllPortraits;
        private static int _portraitSyncDelayTicks = -1;
        private static string _portraitSyncReason;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void BootReportDelegate(
            int pct,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string stage,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string detail,
            int stepIdx);

        [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "dlsym")]
        private static extern IntPtr BootDarwinDlsym(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string symbol);

        private static void ReportNativeBoot(int pct, string stage, string detail, int stepIdx)
        {
            if (_bootReadySignaled)
                return;
            try
            {
                if (!_bootNativeResolved)
                {
                    _bootNativeResolved = true;
                    IntPtr fn = BootDarwinDlsym(new IntPtr(-2), "sdv_boot_report");
                    if (fn != IntPtr.Zero)
                        _bootNativeReport = Marshal.GetDelegateForFunctionPointer<BootReportDelegate>(fn);
                }
                _bootNativeReport?.Invoke(pct, stage ?? "", detail ?? "", stepIdx);
            }
            catch { }
        }

        private static string ClassifyBootAssetStage(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
                return "Đang nạp tài nguyên game…";
            if (assetName.StartsWith("Fonts/", StringComparison.OrdinalIgnoreCase))
                return "Đang nạp phông chữ & bảng mã…";
            if (assetName.StartsWith("LooseSprites/", StringComparison.OrdinalIgnoreCase))
                return "Đang nạp giao diện & biểu tượng…";
            if (assetName.StartsWith("TileSheets/", StringComparison.OrdinalIgnoreCase))
                return "Đang nạp TileSheets & vật phẩm…";
            if (assetName.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) ||
                assetName.StartsWith("Buildings/", StringComparison.OrdinalIgnoreCase) ||
                assetName.StartsWith("TerrainFeatures/", StringComparison.OrdinalIgnoreCase))
                return "Đang nạp bản đồ & công trình…";
            if (assetName.StartsWith("Characters/", StringComparison.OrdinalIgnoreCase) ||
                assetName.StartsWith("Portraits/", StringComparison.OrdinalIgnoreCase) ||
                assetName.StartsWith("Animals/", StringComparison.OrdinalIgnoreCase))
                return "Đang nạp nhân vật, chân dung & vật nuôi…";
            if (assetName.StartsWith("Data/", StringComparison.OrdinalIgnoreCase))
                return "Đang nạp dữ liệu nông trại & vật phẩm…";
            if (assetName.StartsWith("Strings/", StringComparison.OrdinalIgnoreCase))
                return "Đang nạp ngôn ngữ & hội thoại…";
            return "Đang nạp tài nguyên Stardew Valley…";
        }

        public override void Entry(IModHelper helper)
        {
            _monitor = Monitor;
            _bootDocsDir = ResolveBootDocsDir(helper.DirectoryPath);
            WriteBootStage("58|Đang khởi chạy SDV Mobile Controls…");
            ReportNativeBoot(58, "Đang khởi chạy SDV Mobile Controls…", "Khởi chạy Mod: SDV Mobile Controls · Cảm ứng & Thiết bị rời", 2);

            var cfg = new MobileConfig();
            ExternalInput = new ExternalInputBridge(Monitor);
            Engine = new MobileControlsEngine(cfg, Monitor, helper.DirectoryPath);
            RamOptimizer = new MapRamOptimizer(helper, Monitor);
            ExternalInput.ModeChanged += mode =>
            {
                try { Engine?.OnExternalInputModeChanged(mode); } catch { }
            };

            BindAllLoadedTouchVirtualPads("Entry");
            EnsureGardenModHooksInstalled("Entry");
            InstallMobileHarmonyPatches("Entry");
            InstallExternalInputHarmonyPatches("Entry");

            helper.Events.Content.AssetRequested += OnAssetRequestedMailGuard;
            helper.Events.Content.AssetsInvalidated += OnAssetsInvalidatedPortraits;
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.UpdateTicking += OnUpdateTicking;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.Rendered += OnRenderedBootCheck;
            helper.Events.Display.RenderingStep += OnRenderingStepHudRelocate;
            helper.Events.Display.RenderedStep += OnRenderedStepHudRelocate;
            helper.Events.GameLoop.Saved += OnSaved;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.Player.Warped += OnWarped;
        }

        private static string ResolveBootDocsDir(string modDir)
        {
            string docsRoot = null;
            try
            {
                if (!string.IsNullOrEmpty(modDir))
                {
                    var modsDir = Directory.GetParent(modDir);
                    var docsDir = modsDir?.Parent?.FullName;
                    if (!string.IsNullOrEmpty(docsDir) && Directory.Exists(docsDir))
                        docsRoot = docsDir;
                }
            }
            catch { }
            if (string.IsNullOrEmpty(docsRoot))
            {
                try
                {
                    string docs = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                    if (!string.IsNullOrEmpty(docs) && Directory.Exists(docs))
                        docsRoot = docs;
                }
                catch { }
            }
            if (string.IsNullOrEmpty(docsRoot))
                return null;
            string gardenDir = Path.Combine(docsRoot, ".garden");
            try { Directory.CreateDirectory(gardenDir); } catch { }
            return gardenDir;
        }

        private static void WriteBootStage(string stage)
        {
            try
            {
                if (string.IsNullOrEmpty(_bootDocsDir) || string.IsNullOrEmpty(stage))
                    return;
                File.WriteAllText(Path.Combine(_bootDocsDir, "sdv_boot_stage.txt"), stage, Encoding.UTF8);
            }
            catch { }
        }

        private static void SignalBootReady()
        {
            if (_bootReadySignaled)
                return;
            _bootReadySignaled = true;
            try
            {
                if (!string.IsNullOrEmpty(_bootDocsDir))
                    File.WriteAllText(Path.Combine(_bootDocsDir, "sdv_boot_ready.flag"), "1");
            }
            catch { }
        }

        private void OnRenderedBootCheck(object sender, RenderedEventArgs e)
        {
            if (_bootReadySignaled)
            {
                try { Helper.Events.Display.Rendered -= OnRenderedBootCheck; } catch { }
                return;
            }
            if (!_bootGameLaunched || Game1.ticks < 2)
                return;
            int f = ++_bootRenderedFrames;
            if (f == 1)
            {
                ReportNativeBoot(96, "Đang dựng khung hình đầu tiên…", "Hoàn tất kết xuất màn hình tiêu đề", 4);
            }
            else if (f >= 2)
            {
                SignalBootReady();
                try { Helper.Events.Display.Rendered -= OnRenderedBootCheck; } catch { }
            }
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            _bootGameLaunched = true;
            WriteBootStage("90|Kích hoạt GameLaunched — chuẩn bị vào game…");
            ReportNativeBoot(90, "Kích hoạt GameLaunched — chuẩn bị vào game…", "Hoàn tất đồng bộ Content Patcher & cảm ứng", 4);
            BindAllLoadedTouchVirtualPads("GameLaunched");
            EnsureGardenModHooksInstalled("GameLaunched");
            InstallMobileHarmonyPatches("GameLaunched");
            InstallExternalInputHarmonyPatches("GameLaunched");
            try { RamOptimizer?.OnGameLaunched(); } catch { }
        }

        private void OnSaved(object sender, SavedEventArgs e)
        {
            try { Engine?.OnGameSaved(); } catch { }
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            try { RamOptimizer?.OnSaveLoaded(); } catch { }
            SchedulePortraitSync(reloadAll: true, delayTicks: 4, reason: "SaveLoaded");
            // Automatically recover any missed Level 5 / Level 10 profession choices (#GL-LTQ82M)
            try
            {
                if (Game1.player != null)
                    LevelUpMenu.AddMissedProfessionChoices(Game1.player);
            }
            catch { }
            ResetMailboxWatchdogSnapshot();
            LogMailDiagnostic("SaveLoaded");
            if (!_audioDiagRanWorld)
            {
                _audioDiagRanWorld = true;
                RunAudioDiagnosticAndRepair("SaveLoaded");
            }
            _shaderDiagWaitingSave = true;
            _shaderDiagSaveTicks = 0;
            _shaderDiagRanOutdoor = false;
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            try { RamOptimizer?.OnDayStarted(); } catch { }
            SchedulePortraitSync(reloadAll: true, delayTicks: 4, reason: "DayStarted");
            ResetMailboxWatchdogSnapshot();
            LogMailDiagnostic("DayStarted");
        }

        private void LogMailDiagnostic(string phase)
        {
            try
            {
                int mbCount = Game1.mailbox?.Count ?? -1;
                int tomCount = Game1.player?.mailForTomorrow?.Count ?? -1;
                int recCount = Game1.player?.mailReceived?.Count ?? -1;
                int dictCount = -1;
                try
                {
                    var d = DataLoader.Mail(Game1.content);
                    dictCount = d?.Count ?? -1;
                }
                catch { }
                string preview = (mbCount > 0 && Game1.mailbox != null) ? string.Join(",", Game1.mailbox) : "none";
                _monitor?.Log($"[MailDiag:{phase}] {Game1.currentSeason} {Game1.dayOfMonth} Y{Game1.year} | mailbox={mbCount} ({preview}) | tomorrow={tomCount} | received={recCount} | Data/mail={dictCount}", LogLevel.Info);
            }
            catch { }
        }

        private static Dictionary<string, string> GetVanillaBaseMailDictionary()
        {
            if (_vanillaBaseMailLoaded)
                return _vanillaBaseMailCache;
            _vanillaBaseMailLoaded = true;
            try
            {
                var sp = Game1.content?.ServiceProvider;
                string root = Game1.content?.RootDirectory;
                if (sp != null && !string.IsNullOrEmpty(root))
                {
                    // Plain MonoGame ContentManager reads unmodified Content/Data/mail.xnb directly from disk,
                    // bypassing Content Patcher "Action": "Load" replacements from outdated 1.5 translation mods!
                    using var rawCm = new ContentManager(sp, root);
                    var rawDict = rawCm.Load<Dictionary<string, string>>("Data\\mail");
                    if (rawDict != null && rawDict.Count > 0)
                        _vanillaBaseMailCache = new Dictionary<string, string>(rawDict, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }
            return _vanillaBaseMailCache;
        }

        private void OnAssetRequestedMailGuard(object sender, AssetRequestedEventArgs e)
        {
            if (e == null || !e.NameWithoutLocale.IsEquivalentTo("Data/mail"))
                return;

            e.Edit(asset =>
            {
                try
                {
                    var baseMail = GetVanillaBaseMailDictionary();
                    if (baseMail == null || baseMail.Count == 0)
                        return;
                    var dict = asset.AsDictionary<string, string>()?.Data;
                    if (dict == null)
                        return;

                    int restored = 0;
                    foreach (var kv in baseMail)
                    {
                        if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value))
                            continue;
                        if (!dict.TryGetValue(kv.Key, out string existing) || string.IsNullOrWhiteSpace(existing))
                        {
                            dict[kv.Key] = kv.Value;
                            restored++;
                        }
                    }
                    if (restored > 0)
                    {
                        _monitor?.Log($"[MailGuard] Restored {restored} missing Stardew 1.6 entries into '{e.Name}' (prevents empty/auto-claimed mailbox from legacy 1.5 translation packs).", LogLevel.Info);
                    }
                }
                catch { }
            }, AssetEditPriority.Late);
        }

        private static void SchedulePortraitSync(bool reloadAll, int delayTicks, string reason)
        {
            if (reloadAll)
                _pendingReloadAllPortraits = true;
            if (_portraitSyncDelayTicks < 0 || delayTicks > _portraitSyncDelayTicks)
                _portraitSyncDelayTicks = delayTicks;
            if (string.IsNullOrEmpty(_portraitSyncReason) || reloadAll)
                _portraitSyncReason = reason;
        }

        private void OnAssetsInvalidatedPortraits(object sender, AssetsInvalidatedEventArgs e)
        {
            if (!Context.IsWorldReady || e?.NamesWithoutLocale == null)
                return;
            try
            {
                foreach (IAssetName asset in e.NamesWithoutLocale)
                {
                    if (asset == null || string.IsNullOrEmpty(asset.Name))
                        continue;
                    if (asset.IsEquivalentTo("Data/Characters"))
                    {
                        SchedulePortraitSync(reloadAll: true, delayTicks: 1, reason: "Data/Characters");
                        continue;
                    }
                    string raw = asset.Name;
                    if (raw.StartsWith("Portraits/", StringComparison.OrdinalIgnoreCase) ||
                        raw.StartsWith("Portraits\\", StringComparison.OrdinalIgnoreCase))
                    {
                        string norm = MapRamOptimizer.NormalizePortraitKey(raw);
                        if (!string.IsNullOrEmpty(norm))
                        {
                            _pendingPortraitKeys.Add(norm);
                            SchedulePortraitSync(reloadAll: false, delayTicks: 1, reason: "AssetsInvalidated");
                        }
                    }
                }
            }
            catch { }
        }

        private static void FlushPendingPortraitSync()
        {
            if (_portraitSyncDelayTicks < 0)
                return;
            if (!Context.IsWorldReady)
            {
                _portraitSyncDelayTicks = -1;
                _pendingReloadAllPortraits = false;
                _pendingPortraitKeys.Clear();
                _portraitSyncReason = null;
                return;
            }
            if (--_portraitSyncDelayTicks >= 0)
                return;

            bool reloadAll = _pendingReloadAllPortraits;
            string reason = _portraitSyncReason ?? "Sync";
            HashSet<string> keys = (!reloadAll && _pendingPortraitKeys.Count > 0)
                ? new HashSet<string>(_pendingPortraitKeys, StringComparer.OrdinalIgnoreCase)
                : null;
            _pendingReloadAllPortraits = false;
            _pendingPortraitKeys.Clear();
            _portraitSyncReason = null;

            try { RamOptimizer?.RefreshInvalidatedPortraits(keys, reloadAll, reason); } catch { }
        }

        private static void ResetMailboxWatchdogSnapshot()
        {
            try
            {
                _lastMailboxSnapshot.Clear();
                if (Game1.mailbox != null)
                {
                    for (int i = 0; i < Game1.mailbox.Count; i++)
                        _lastMailboxSnapshot.Add(Game1.mailbox[i] ?? string.Empty);
                }
                _lastMailboxDayKey = Game1.year * 1000 + (int)Game1.season * 100 + Game1.dayOfMonth;
            }
            catch { }
        }

        private static void PollMailboxWatchdog()
        {
            if (!Context.IsWorldReady || Game1.mailbox == null)
                return;
            try
            {
                int dayKey = Game1.year * 1000 + (int)Game1.season * 100 + Game1.dayOfMonth;
                if (dayKey != _lastMailboxDayKey)
                {
                    ResetMailboxWatchdogSnapshot();
                    return;
                }

                int curCount = Game1.mailbox.Count;
                int prevCount = _lastMailboxSnapshot.Count;
                if (curCount < prevCount)
                {
                    bool openedLetter = Game1.activeClickableMenu is LetterViewerMenu;
                    if (!openedLetter)
                    {
                        for (int i = 0; i < prevCount; i++)
                        {
                            string oldId = _lastMailboxSnapshot[i];
                            if (!string.IsNullOrEmpty(oldId) && !Game1.mailbox.Contains(oldId))
                            {
                                bool inReceived = Game1.player?.mailReceived != null && Game1.player.mailReceived.Contains(oldId);
                                _monitor?.Log($"[MailDiag:Watchdog] Mail '{oldId}' left Game1.mailbox without opening LetterViewerMenu (mailReceived={inReceived}, activeMenu={Game1.activeClickableMenu?.GetType().Name ?? "none"}).", LogLevel.Warn);
                            }
                        }
                    }
                }

                if (curCount != prevCount)
                {
                    _lastMailboxSnapshot.Clear();
                    for (int i = 0; i < curCount; i++)
                        _lastMailboxSnapshot.Add(Game1.mailbox[i] ?? string.Empty);
                }
            }
            catch { }
        }

        private void OnWarped(object sender, WarpedEventArgs e)
        {
            if (e != null && e.IsLocalPlayer)
            {
                try { RamOptimizer?.OnWarped(e.NewLocation); } catch { }
                if (!_shaderDiagRanOutdoor && e.NewLocation != null && e.NewLocation.IsOutdoors)
                {
                    _shaderDiagRanOutdoor = true;
                    RunShaderDiagnostic("FirstOutdoorWarp:" + e.NewLocation.Name);
                }
            }
        }

        private static void RestoreStashedHudMessagesIfNeeded()
        {
            if (!_hudMessagesStashed)
                return;
            _hudMessagesStashed = false;
            try
            {
                if (_stashedHudMessages.Count > 0 && Game1.hudMessages != null)
                {
                    if (Game1.hudMessages.Count == 0)
                    {
                        Game1.hudMessages.AddRange(_stashedHudMessages);
                    }
                    else
                    {
                        // Preserve any new HUDMessage pushed during drawHUD at the end
                        Game1.hudMessages.InsertRange(0, _stashedHudMessages);
                    }
                }
            }
            catch { }
            finally
            {
                _stashedHudMessages.Clear();
            }
        }

        private void OnRenderingStepHudRelocate(object sender, RenderingStepEventArgs e)
        {
            if (e == null || e.Step != RenderSteps.HUD || Engine == null || (Game1.game1 != null && Game1.game1.takingMapScreenshot))
                return;
            try
            {
                var list = Game1.hudMessages;
                if (list == null || list.Count == 0)
                    return;
                Rectangle tsarea = Game1.graphics.GraphicsDevice.Viewport.GetTitleSafeArea();
                if (!Engine.TryGetHudMessageRelocation(tsarea, out _, out _, out _))
                    return;

                // Zero-Harmony relocation: stash Game1.hudMessages right before vanilla Game1._draw draws them at bottom-left!
                _stashedHudMessages.Clear();
                _stashedHudMessages.AddRange(list);
                list.Clear();
                _hudMessagesStashed = true;
            }
            catch
            {
                RestoreStashedHudMessagesIfNeeded();
            }
        }

        private void OnRenderedStepHudRelocate(object sender, RenderedStepEventArgs e)
        {
            if (e == null || e.Step != RenderSteps.HUD || !_hudMessagesStashed)
                return;

            RestoreStashedHudMessagesIfNeeded();

            try
            {
                var list = Game1.hudMessages;
                if (list == null || list.Count == 0 || e.SpriteBatch == null || Engine == null || (Game1.game1 != null && Game1.game1.takingMapScreenshot))
                    return;
                int heightUsed = 0;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    HUDMessage m = list[i];
                    if (m != null)
                        PrefixHudMessageDraw(m, e.SpriteBatch, i, ref heightUsed);
                }
            }
            catch { }
        }

        private void OnUpdateTicking(object sender, UpdateTickingEventArgs e)
        {
            try
            {
                if (!_gardenHooksInstalled)
                    EnsureGardenModHooksInstalled("UpdateTicking");
                if (ExternalInput != null)
                {
                    ExternalInput.SetManagedSuppressed(Engine != null && Engine.IsUiOverlayBlockingExternalInput(null));
                    ExternalInput.ApplyStardewGamepadMode();
                }
            }
            catch { }
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            RestoreStashedHudMessagesIfNeeded();
            try
            {
                if (ExternalInput != null)
                {
                    ExternalInput.SetManagedSuppressed(Engine != null && Engine.IsUiOverlayBlockingExternalInput(null));
                    ExternalInput.PollFrame();
                    ExternalInput.UpdateControllerRumble();
                }
            }
            catch { }
            try { Engine?.OnUpdateTickedGlobal(); } catch { }
            try { RamOptimizer?.PollManualPruneFlag(); } catch { }
            FlushPendingPortraitSync();
            PollMailboxWatchdog();
            if (!_audioDiagRanLaunch && ++_audioDiagTicks >= 120)
            {
                _audioDiagRanLaunch = true;
                RunAudioDiagnosticAndRepair("GameLaunched+2s");
            }
            if (_shaderDiagWaitingSave && Context.IsWorldReady && ++_shaderDiagSaveTicks >= 180)
            {
                _shaderDiagWaitingSave = false;
                RunShaderDiagnostic("SaveLoaded+3s");
            }
            if (BoundPadTypes.Count >= 2 && _extHarmonyPatchesInstalled && (_gardenHooksInstalled || PatchedTouchHookTypes.Count >= 1))
                return;
            if (BoundPadTypes.Count > 0 && _retryTicks > 300)
                return;
            if (++_retryTicks % 30 == 0)
            {
                BindAllLoadedTouchVirtualPads("UpdateTicked");
                EnsureGardenModHooksInstalled("UpdateTicked");
                InstallExternalInputHarmonyPatches("UpdateTicked");
            }
        }

        private void RunAudioDiagnosticAndRepair(string phase)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var sb = new StringBuilder();
            sb.Append($"[AudioDiag:{phase}] ");
            try
            {
                if (SoundEffect.MasterVolume < 0.01f)
                {
                    SoundEffect.MasterVolume = 1.0f;
                    sb.Append("FixedMasterVol=1.0 ");
                }

                sb.Append($"MasterVol={SoundEffect.MasterVolume:F2} ");
                if (Game1.options != null)
                {
                    sb.Append($"Opt(mus={Game1.options.musicVolumeLevel:F2},sfx={Game1.options.soundVolumeLevel:F2},amb={Game1.options.ambientVolumeLevel:F2}) ");
                }
                sb.Append($"AE={Game1.audioEngine?.GetType().Name ?? "null"}(disp={Game1.audioEngine?.IsDisposed}) ");
                sb.Append($"SB={Game1.soundBank?.GetType().Name ?? "null"} ");
                sb.Append($"WB={Game1.waveBank != null}(prep={Game1.waveBank?.IsPrepared}) ");
                sb.Append($"WB14={Game1.waveBank1_4 != null}(prep={Game1.waveBank1_4?.IsPrepared}) ");

                Assembly mgAsm = typeof(SoundEffect).Assembly;
                Type oalType = mgAsm.GetType("Microsoft.Xna.Framework.Audio.OpenALSoundController");
                Type alcType = mgAsm.GetType("MonoGame.OpenAL.Alc");
                Type alType = mgAsm.GetType("MonoGame.OpenAL.AL");

                if (oalType != null)
                {
                    object inst = oalType.GetProperty("Instance", any)?.GetValue(null);
                    if (inst != null)
                    {
                        IntPtr dev = (IntPtr)(oalType.GetField("_device", any)?.GetValue(inst) ?? IntPtr.Zero);
                        IntPtr ctx = (IntPtr)(oalType.GetField("_context", any)?.GetValue(inst) ?? IntPtr.Zero);
                        bool adpcm = (bool)(oalType.GetProperty("SupportsAdpcm", any)?.GetValue(inst) ?? false);
                        var avail = oalType.GetField("availableSourcesCollection", any)?.GetValue(inst) as System.Collections.ICollection;
                        var inUse = oalType.GetField("inUseSourcesCollection", any)?.GetValue(inst) as System.Collections.ICollection;
                        sb.Append($"OAL(dev=0x{dev.ToInt64():X},ctx=0x{ctx.ToInt64():X},adpcm={adpcm},avail={avail?.Count ?? -1},inUse={inUse?.Count ?? -1}) ");

                        if (alcType != null && ctx != IntPtr.Zero)
                        {
                            var delGetCur = alcType.GetField("GetCurrentContext", any)?.GetValue(null) as Delegate;
                            var delMakeCur = alcType.GetField("MakeContextCurrent", any)?.GetValue(null) as Delegate;
                            var delProc = alcType.GetField("ProcessContext", any)?.GetValue(null) as Delegate;
                            IntPtr curCtx = delGetCur != null ? (IntPtr)delGetCur.DynamicInvoke() : IntPtr.Zero;
                            sb.Append($"curCtx=0x{curCtx.ToInt64():X} ");
                            if (curCtx == IntPtr.Zero && delMakeCur != null)
                            {
                                delMakeCur.DynamicInvoke(ctx);
                                delProc?.DynamicInvoke(ctx);
                                sb.Append("REBOUND_CTX ");
                            }
                            else
                            {
                                delProc?.DynamicInvoke(ctx);
                            }
                            var delAlcErr = alcType.GetField("GetError", any)?.GetValue(null) as Delegate;
                            if (delAlcErr != null && dev != IntPtr.Zero)
                                sb.Append($"alcErr={delAlcErr.DynamicInvoke(dev)} ");
                        }
                    }
                    else
                    {
                        sb.Append("OAL=null ");
                    }
                }

                if (alType != null)
                {
                    var delAlErr = alType.GetField("GetError", any)?.GetValue(null) as Delegate;
                    if (delAlErr != null)
                        sb.Append($"alErr={delAlErr.DynamicInvoke()} ");
                }

                // Test loading track 0 from WaveBank directly to catch any hidden exception!
                if (Game1.waveBank != null)
                {
                    MethodInfo mLoad = typeof(WaveBank).GetMethod("LoadSoundEffect", any);
                    if (mLoad != null)
                    {
                        try
                        {
                            object se = mLoad.Invoke(Game1.waveBank, new object[] { 0 });
                            if (se != null)
                            {
                                object buf = se.GetType().GetField("SoundBuffer", any)?.GetValue(se);
                                int alBuf = buf != null ? (int)(buf.GetType().GetField("openALDataBuffer", any)?.GetValue(buf) ?? -1) : -1;
                                int sz = buf != null ? (int)(buf.GetType().GetField("dataSize", any)?.GetValue(buf) ?? -1) : -1;
                                double dur = buf != null ? (double)(buf.GetType().GetProperty("Duration", any)?.GetValue(buf) ?? -1.0) : -1.0;
                                sb.Append($"WB0=OK(alBuf={alBuf},sz={sz},dur={dur:F2}s) ");
                            }
                            else
                            {
                                sb.Append("WB0=null ");
                            }
                        }
                        catch (Exception ex)
                        {
                            Exception inner = ex.InnerException ?? ex;
                            sb.Append($"WB0=ERR({inner.GetType().Name}:{inner.Message}) ");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sb.Append($"DiagErr={ex.GetType().Name}:{ex.Message}");
            }

            string line = sb.ToString();
            _monitor?.Log(line, LogLevel.Info);
            try
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string errDir = Path.Combine(docs, "ErrorLogs");
                Directory.CreateDirectory(errDir);
                string diagPath = Path.Combine(errDir, "audio-diag.log");
                string entryLine = $"[{DateTime.Now:HH:mm:ss}] {line}\n";
                if (!_audioDiagWroteThisSession)
                {
                    _audioDiagWroteThisSession = true;
                    File.WriteAllText(diagPath, entryLine);
                }
                else
                {
                    File.AppendAllText(diagPath, entryLine);
                }
            }
            catch { }
        }

        private void BindAllLoadedTouchVirtualPads(string phase)
        {
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type tvp = null;
                try
                {
                    tvp = asm.GetType("SDViOSTouchControls.TouchVirtualPad", throwOnError: false);
                }
                catch { continue; }

                if (tvp == null || BoundPadTypes.Contains(tvp))
                    continue;

                try
                {
                    FieldInfo fUpd = tvp.GetField("MobileUpdateHook", stat);
                    FieldInfo fDrw = tvp.GetField("MobileDrawHook", stat);
                    FieldInfo fKeys = tvp.GetField("MobileExtraKeysHook", stat);
                    FieldInfo fKeysList = tvp.GetField("MobileExtraKeysListHook", stat);
                    if (fUpd == null || fDrw == null)
                    {
                        _monitor.Log($"[{phase}] Found {tvp.FullName} in {asm.GetName().Name}, but MobileUpdateHook/MobileDrawHook fields missing!", LogLevel.Warn);
                        continue;
                    }

                    Func<object, GameTime, bool> updDel = OnMobileUpdate;
                    Func<object, SpriteBatch, bool> drwDel = OnMobileDraw;
                    fUpd.SetValue(null, updDel);
                    fDrw.SetValue(null, drwDel);
                    if (fKeys != null)
                    {
                        Action<HashSet<Keys>> keysDel = OnPopulateExtraKeys;
                        fKeys.SetValue(null, keysDel);
                    }
                    if (fKeysList != null)
                    {
                        Action<List<Keys>> keysListDel = OnPopulateExtraKeysList;
                        fKeysList.SetValue(null, keysListDel);
                    }

                    BoundPadTypes.Add(tvp);
                    _monitor.Log($"[{phase}] BOUND DIRECT IL HOOKS on {asm.GetName().Name} (ExtraKeys={fKeys != null}, ExtraKeysList={fKeysList != null})! Mode={Engine.CurrentMode}", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    _monitor.Log($"[{phase}] Failed binding hooks on {asm.GetName().Name}: {ex}", LogLevel.Error);
                }
            }
        }

        private static void OnPopulateExtraKeys(HashSet<Keys> keySet)
        {
            try
            {
                Engine?.PopulateExtraKeys(keySet);
                ExternalInput?.PopulateExtraKeys(keySet);
            }
            catch { }
        }

        private static void OnPopulateExtraKeysList(List<Keys> keyList)
        {
            try
            {
                Engine?.PopulateExtraKeysList(keyList);
                ExternalInput?.PopulateExtraKeysList(keyList);
            }
            catch { }
        }

        private static void InstallMobileHarmonyPatches(string phase)
        {
            if (_harmonyPatchesInstalled)
                return;

            try
            {
                Type harmonyType = null;
                Type harmonyMethodType = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (harmonyType == null)
                            harmonyType = asm.GetType("HarmonyLib.Harmony", throwOnError: false);
                        if (harmonyMethodType == null)
                            harmonyMethodType = asm.GetType("HarmonyLib.HarmonyMethod", throwOnError: false);
                        if (harmonyType != null && harmonyMethodType != null)
                            break;
                    }
                    catch { }
                }

                if (harmonyType == null || harmonyMethodType == null)
                    return;

                MethodInfo patchMethod = null;
                foreach (MethodInfo mi in harmonyType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (mi.Name != "Patch") continue;
                    var ps = mi.GetParameters();
                    if (ps.Length >= 3 &&
                        ps[0].ParameterType == typeof(MethodBase) &&
                        ps[1].ParameterType == harmonyMethodType &&
                        ps[2].ParameterType == harmonyMethodType)
                    {
                        patchMethod = mi;
                        break;
                    }
                }
                if (patchMethod == null)
                    return;

                object harmony = Activator.CreateInstance(harmonyType, new object[] { "GardenLauncher.MobileControls.Patches" });
                int paramCount = patchMethod.GetParameters().Length;
                const BindingFlags instAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                const BindingFlags statNonPub = BindingFlags.Static | BindingFlags.NonPublic;

                void ApplyPatch(MethodBase original, string prefixName, string postfixName)
                {
                    if (original == null)
                        return;
                    object hmPref = null;
                    object hmPost = null;
                    if (prefixName != null)
                    {
                        MethodInfo pm = typeof(ModEntry).GetMethod(prefixName, statNonPub);
                        if (pm != null) hmPref = Activator.CreateInstance(harmonyMethodType, new object[] { pm });
                    }
                    if (postfixName != null)
                    {
                        MethodInfo pm = typeof(ModEntry).GetMethod(postfixName, statNonPub);
                        if (pm != null) hmPost = Activator.CreateInstance(harmonyMethodType, new object[] { pm });
                    }
                    if (hmPref == null && hmPost == null)
                        return;
                    object[] invokeArgs = new object[paramCount];
                    invokeArgs[0] = original;
                    invokeArgs[1] = hmPref;
                    invokeArgs[2] = hmPost;
                    patchMethod.Invoke(harmony, invokeArgs);
                }

                int applied = 0;
                void TryApply(string label, MethodBase original, string prefixName, string postfixName)
                {
                    if (original == null)
                        return;
                    try
                    {
                        ApplyPatch(original, prefixName, postfixName);
                        applied++;
                    }
                    catch (Exception pex)
                    {
                        _monitor?.Log($"[{phase}] Mobile Harmony patch '{label}' failed: {pex.GetType().Name}: {pex.Message}", LogLevel.Warn);
                    }
                }

                // 1-3. FishingRod cast-charge guards (startCasting + non-inlineable NetEvent doStartCasting + DoFunction re-entry guard)
                MethodInfo mStartCast = typeof(FishingRod).GetMethod("startCasting", instAny);
                TryApply("FishingRod.startCasting", mStartCast, nameof(PrefixFishingRodStartCasting), null);

                MethodInfo mDoStartCast = typeof(FishingRod).GetMethod("doStartCasting", instAny);
                TryApply("FishingRod.doStartCasting", mDoStartCast, nameof(PrefixFishingRodDoStartCasting), null);

                MethodInfo mDoFunc = typeof(FishingRod).GetMethod(
                    "DoFunction",
                    instAny,
                    null,
                    new[] { typeof(GameLocation), typeof(int), typeof(int), typeof(int), typeof(Farmer) },
                    null);
                TryApply("FishingRod.DoFunction", mDoFunc, nameof(PrefixFishingRodDoFunction), null);

                // 4. BobberBar Mobile-feel physics (damping + crisp reversal braking + gentle edge bounce)
                MethodInfo mBobberUpd = typeof(BobberBar).GetMethod("update", instAny, null, new[] { typeof(GameTime) }, null);
                TryApply("BobberBar.update", mBobberUpd, null, nameof(PostfixBobberBarUpdate));

                // 5. Direct HUDMessage relocation when Vertical Toolbar is enabled (zero SpriteBatch restart, zero __state!)
                MethodInfo mHudDraw = typeof(HUDMessage).GetMethod(
                    "draw",
                    instAny,
                    null,
                    new[] { typeof(SpriteBatch), typeof(int), typeof(int).MakeByRefType() },
                    null);
                TryApply("HUDMessage.draw", mHudDraw, nameof(PrefixHudMessageDraw), null);

                // 6. Tap-to-Move right-click synthesis for ShippingBin.doAction, FishPond.doAction, Furniture & modded buildings
                MethodInfo mRightClick = typeof(Game1).GetMethod(
                    "didPlayerJustRightClick",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(bool) },
                    null);
                TryApply("Game1.didPlayerJustRightClick", mRightClick, null, nameof(PostfixDidPlayerJustRightClick));

                // 7. Live boot asset-loading progress telemetry (zero runtime cost after boot finishes!)
                try
                {
                    Type sCoreType = typeof(Mod).Assembly.GetType("StardewModdingAPI.Framework.SCore", throwOnError: false);
                    MethodInfo mOnAsset = sCoreType?.GetMethod("OnAssetLoaded", instAny);
                    TryApply("SCore.OnAssetLoaded", mOnAsset, null, nameof(PostfixSmapiAssetLoaded));
                }
                catch { }

                // 8-9. Repair upstream v1.0.1 MonoGame iOS SoundBank.Exists (which returned false and muted all audio!)
                //      while also returning a non-null CueDefinition so SVE Data/AudioChanges never throws NRE.
                try
                {
                    _soundBankSoundsField = typeof(SoundBank).GetField("_sounds", instAny);
                    MethodInfo mSbExists = typeof(SoundBank).GetMethod("Exists", instAny, null, new[] { typeof(string) }, null);
                    TryApply("SoundBank.Exists", mSbExists, nameof(PrefixSoundBankExists), null);
                    MethodInfo mSbGetCueDef = typeof(SoundBank).GetMethod("GetCueDefinition", instAny, null, new[] { typeof(string) }, null);
                    TryApply("SoundBank.GetCueDefinition", mSbGetCueDef, nameof(PrefixSoundBankGetCueDefinition), null);
                }
                catch { }

                _harmonyPatchesInstalled = true;
                _monitor?.Log($"[{phase}] Installed {applied}/9 Mobile Harmony patches (FishingRod + BobberBar + Direct HUDMessage + TapAction RightClick + BootAssetProgress + SoundBank Audio Fix).", LogLevel.Info);
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[{phase}] Mobile Harmony patches skipped: {ex.Message}", LogLevel.Warn);
            }
        }

        private static void InstallExternalInputHarmonyPatches(string phase)
        {
            try
            {
                Type harmonyType = null;
                Type harmonyMethodType = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (harmonyType == null)
                            harmonyType = asm.GetType("HarmonyLib.Harmony", throwOnError: false);
                        if (harmonyMethodType == null)
                            harmonyMethodType = asm.GetType("HarmonyLib.HarmonyMethod", throwOnError: false);
                        if (harmonyType != null && harmonyMethodType != null)
                            break;
                    }
                    catch { }
                }
                if (harmonyType == null || harmonyMethodType == null)
                    return;

                MethodInfo patchMethod = null;
                foreach (MethodInfo mi in harmonyType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (mi.Name != "Patch") continue;
                    var ps = mi.GetParameters();
                    if (ps.Length >= 3 &&
                        ps[0].ParameterType == typeof(MethodBase) &&
                        ps[1].ParameterType == harmonyMethodType &&
                        ps[2].ParameterType == harmonyMethodType)
                    {
                        patchMethod = mi;
                        break;
                    }
                }
                if (patchMethod == null)
                    return;

                object harmony = Activator.CreateInstance(harmonyType, new object[] { "GardenLauncher.ExternalInput.Patches" });
                int paramCount = patchMethod.GetParameters().Length;
                FieldInfo fPrio = harmonyMethodType.GetField("priority", BindingFlags.Instance | BindingFlags.Public);
                const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                const BindingFlags statNonPub = BindingFlags.Static | BindingFlags.NonPublic;

                int ApplyExtPatch(MethodBase original, string prefixName, int prefixPrio, string postfixName, int postfixPrio)
                {
                    if (original == null) return 0;
                    try
                    {
                        object hmPref = null;
                        object hmPost = null;
                        if (prefixName != null)
                        {
                            MethodInfo pm = typeof(ModEntry).GetMethod(prefixName, statNonPub);
                            if (pm != null)
                            {
                                hmPref = Activator.CreateInstance(harmonyMethodType, new object[] { pm });
                                fPrio?.SetValue(hmPref, prefixPrio);
                            }
                        }
                        if (postfixName != null)
                        {
                            MethodInfo pm = typeof(ModEntry).GetMethod(postfixName, statNonPub);
                            if (pm != null)
                            {
                                hmPost = Activator.CreateInstance(harmonyMethodType, new object[] { pm });
                                fPrio?.SetValue(hmPost, postfixPrio);
                            }
                        }
                        if (hmPref == null && hmPost == null) return 0;
                        object[] invokeArgs = new object[paramCount];
                        invokeArgs[0] = original;
                        invokeArgs[1] = hmPref;
                        invokeArgs[2] = hmPost;
                        patchMethod.Invoke(harmony, invokeArgs);
                        return 1;
                    }
                    catch (Exception ex)
                    {
                        _monitor?.Log($"[{phase}] ExternalInput patch failed on {original?.DeclaringType?.Name}.{original?.Name}: {ex.Message}", LogLevel.Warn);
                        return 0;
                    }
                }

                int patchedNow = 0;

                if (!_extHarmonyPatchesInstalled)
                {
                    // 1. GamePad (Priority.First = 800 -> always preempts SDViOSTouchControls [400] when physical gamepad is active!)
                    patchedNow += ApplyExtPatch(typeof(GamePad).GetMethod("GetState", any, null, new[] { typeof(PlayerIndex) }, null), nameof(PrefixExtGamePadState), 800, null, 400);
                    patchedNow += ApplyExtPatch(typeof(InputState).GetMethod("GetGamePadState", any, null, Type.EmptyTypes, null), nameof(PrefixExtGamePadState), 800, null, 400);
                    patchedNow += ApplyExtPatch(typeof(GamePad).GetMethod("SetVibration", any, null, new[] { typeof(int), typeof(float), typeof(float), typeof(float), typeof(float) }, null), nameof(PrefixExtSetVibration), 800, null, 400);

                    // 2. Mouse (Priority.First = 800 -> full external mouse/hover/right-click/scroll + gamepad right-stick & menu cursor!)
                    patchedNow += ApplyExtPatch(typeof(Mouse).GetMethod("GetState", any, null, Type.EmptyTypes, null), nameof(PrefixExtMouseState), 800, null, 400);
                    patchedNow += ApplyExtPatch(typeof(Mouse).GetMethod("GetState", any, null, new[] { typeof(GameWindow) }, null), nameof(PrefixExtMouseState), 800, null, 400);
                    patchedNow += ApplyExtPatch(typeof(Mouse).GetMethod("PlatformGetState", any, null, new[] { typeof(GameWindow) }, null), nameof(PrefixExtMouseState), 800, null, 400);
                    patchedNow += ApplyExtPatch(typeof(InputState).GetMethod("GetMouseState", any, null, Type.EmptyTypes, null), nameof(PrefixExtMouseState), 800, null, 400);
                    patchedNow += ApplyExtPatch(typeof(Game1).GetMethod("setMousePositionRaw", any, null, new[] { typeof(int), typeof(int) }, null), nameof(PrefixExtSetMousePositionRaw), 800, null, 400);

                    // 3. Keyboard (Priority.Low = 200 Postfix -> merges physical hardware keys into ANY returned KeyboardState!)
                    patchedNow += ApplyExtPatch(typeof(Keyboard).GetMethod("GetState", any, null, Type.EmptyTypes, null), null, 400, nameof(PostfixExtKeyboardState), 200);
                    patchedNow += ApplyExtPatch(typeof(Game1).GetMethod("GetKeyboardState", any, null, Type.EmptyTypes, null), null, 400, nameof(PostfixExtKeyboardState), 200);
                    patchedNow += ApplyExtPatch(typeof(InputState).GetMethod("GetKeyboardState", any, null, Type.EmptyTypes, null), null, 400, nameof(PostfixExtKeyboardState), 200);

                    // 4. SMAPI SInputState (so ALL SMAPI mods & UI Info Suite 2 receive real external Gamepad/Keyboard/Mouse!)
                    Type sInputType = null;
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            sInputType = asm.GetType("StardewModdingAPI.Framework.Input.SInputState", throwOnError: false);
                            if (sInputType != null) break;
                        }
                        catch { }
                    }
                    if (sInputType != null)
                    {
                        patchedNow += ApplyExtPatch(sInputType.GetMethod("GetGamePadState", any, null, Type.EmptyTypes, null), nameof(PrefixExtGamePadState), 800, null, 400);
                        patchedNow += ApplyExtPatch(sInputType.GetProperty("ControllerState", any)?.GetGetMethod(true), nameof(PrefixExtGamePadState), 800, null, 400);
                        patchedNow += ApplyExtPatch(sInputType.GetMethod("GetMouseState", any, null, Type.EmptyTypes, null), nameof(PrefixExtMouseState), 800, null, 400);
                        patchedNow += ApplyExtPatch(sInputType.GetProperty("MouseState", any)?.GetGetMethod(true), nameof(PrefixExtMouseState), 800, null, 400);
                        patchedNow += ApplyExtPatch(sInputType.GetMethod("GetKeyboardState", any, null, Type.EmptyTypes, null), null, 400, nameof(PostfixExtKeyboardState), 200);
                        patchedNow += ApplyExtPatch(sInputType.GetProperty("KeyboardState", any)?.GetGetMethod(true), null, 400, nameof(PostfixExtKeyboardState), 200);
                    }

                    _extHarmonyPatchesInstalled = true;
                }

                // 5. Patch SDViOSTouchControls.TouchDelegatingModHooks.OnGame1_UpdateControlInput ONLY if Zero-Harmony GardenDelegatingModHooks is not yet spliced in!
                if (!_gardenHooksInstalled)
                {
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type thType = null;
                        try { thType = asm.GetType("SDViOSTouchControls.TouchDelegatingModHooks", throwOnError: false); }
                        catch { continue; }
                        if (thType == null || PatchedTouchHookTypes.Contains(thType)) continue;

                        MethodInfo mUpdCtrl = thType.GetMethod("OnGame1_UpdateControlInput", any);
                        if (mUpdCtrl != null)
                        {
                            if (_delegatingParentProp == null && thType.BaseType != null)
                            {
                                _delegatingParentProp = thType.BaseType.GetProperty("Parent", any);
                                _modHooksUpdateControlInput = _delegatingParentProp?.PropertyType.GetMethod("OnGame1_UpdateControlInput", any);
                            }
                            if (ApplyExtPatch(mUpdCtrl, nameof(PrefixExtUpdateControlInput), 800, null, 400) > 0)
                            {
                                PatchedTouchHookTypes.Add(thType);
                                patchedNow++;
                            }
                        }
                    }
                }

                if (patchedNow > 0)
                {
                    _monitor?.Log($"[{phase}] Installed {patchedNow} External Peripheral Harmony patches (Full Gamepad + GCKeyboard + GCMouse, TouchHooks={PatchedTouchHookTypes.Count}).", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[{phase}] External Peripheral Harmony patches error: {ex.Message}", LogLevel.Warn);
            }
        }

        private static bool PrefixExtGamePadState(ref GamePadState __result)
        {
            var ext = ExternalInput;
            if (ext != null && ext.IsPhysicalGamepadActive)
            {
                __result = ext.GetEffectiveGamePadStateForGame();
                return false;
            }
            return true;
        }

        private static bool PrefixExtSetVibration(int index, float leftMotor, float rightMotor, ref bool __result)
        {
            var ext = ExternalInput;
            if (ext != null && ext.IsPhysicalGamepadActive && index == 0)
            {
                ext.SetRumbleDirect(leftMotor, rightMotor);
                __result = true;
                return false;
            }
            return true;
        }

        private static bool PrefixExtMouseState(ref MouseState __result)
        {
            var ext = ExternalInput;
            if (ext != null && ext.ShouldOverrideMouse)
            {
                __result = ext.CurrentMouseState;
                return false;
            }
            return true;
        }

        private static bool PrefixExtSetMousePositionRaw(int x, int y)
        {
            var ext = ExternalInput;
            if (ext != null && ext.ShouldOverrideMouse)
            {
                ext.OnGameSetMousePositionRaw(x, y);
                try
                {
                    Game1.input?.SetMousePosition(x, y);
                    Game1.InvalidateOldMouseMovement();
                    if (ext.IsPhysicalGamepadActive)
                        Game1.lastCursorMotionWasMouse = false;
                }
                catch { }
                return false;
            }
            return true;
        }

        private static void PostfixExtKeyboardState(ref KeyboardState __result)
        {
            var ext = ExternalInput;
            if (ext != null && ext.HasPhysicalKeysPressed)
            {
                __result = ext.MergeKeyboardState(__result);
            }
        }

        private static bool PrefixExtUpdateControlInput(object __instance, ref KeyboardState keyboardState, ref MouseState mouseState, ref GamePadState gamePadState, Action action)
        {
            var ext = ExternalInput;
            if (ext == null || !ext.IsExternalDeviceActive)
                return true;

            ext.ApplyStardewGamepadMode();
            ext.SyncDirectZeroHarmonyInputStates();

            if (ext.ShouldPushMouseStateNow)
                mouseState = ext.CurrentMouseState;

            if (ext.IsPhysicalGamepadActive)
            {
                ext.EnsureValidLocalMultiplayerWindow();
                // Never allow stale touch-simulated WASD keys from TouchVirtualPad to leak
                // into Game1.UpdateControlInput while a physical controller is driving movement!
                keyboardState = ext.BuildPhysicalKeyboardState();
                // When ExternalInputBridge is directly steering the free gameplay cursor with the right stick,
                // strip ThumbSticks.Right from the state passed into Game1.<UpdateControlInput>b__0 so Stardew
                // never double-moves the cursor or teleports it via localMultiplayerWindow/lastMousePositionBeforeFade!
                gamePadState = ext.GetEffectiveGamePadStateForGame();
                if (!ext.SuppressGamepadAButtonThisFrame)
                    Game1.lastCursorMotionWasMouse = false;
                // Keep cursor visible in menus AND throughout the 4-second fade window after releasing R-stick
                // so players can comfortably hover and read UI Info Suite 2 tooltips!
                if (Game1.activeClickableMenu != null || Game1.timerUntilMouseFade > 0 || ext.IsRightStickSteeringGameplayCursor || ext.IsRightStickMenuCursorActive)
                    Game1.wasMouseVisibleThisFrame = true;
                else
                    Game1.wasMouseVisibleThisFrame = false;
            }
            else if (ext.IsExternalMouseActive)
            {
                if (ext.HasPhysicalKeysPressed)
                    keyboardState = ext.MergeKeyboardState(keyboardState);
                gamePadState = default;
                Game1.lastCursorMotionWasMouse = true;
                Game1.wasMouseVisibleThisFrame = true;
            }
            else
            {
                if (ext.HasPhysicalKeysPressed)
                    keyboardState = ext.MergeKeyboardState(keyboardState);
                gamePadState = default;
            }

            try
            {
                object parent = (_delegatingParentProp != null && __instance != null) ? _delegatingParentProp.GetValue(__instance) : null;
                if (parent != null && _modHooksUpdateControlInput != null)
                {
                    object[] args = new object[] { keyboardState, mouseState, gamePadState, action };
                    _modHooksUpdateControlInput.Invoke(parent, args);
                    keyboardState = (KeyboardState)args[0];
                    mouseState = (MouseState)args[1];
                    gamePadState = (GamePadState)args[2];
                    return false;
                }
            }
            catch { }

            action?.Invoke();
            return false;
        }

        private static void EnsureGardenModHooksInstalled(string phase)
        {
            if (_gardenHooksInstalled)
                return;
            try
            {
                const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                if (_game1HooksField == null)
                    _game1HooksField = typeof(Game1).GetField("hooks", any);
                if (_game1HooksField == null)
                    return;

                ModHooks topHooks = _game1HooksField.GetValue(null) as ModHooks;
                if (topHooks == null)
                    return;

                // Walk DelegatingModHooks chain: ONLY splice inside TouchDelegatingModHooks.Parent once
                // SDViOSTouchControls has wrapped Game1.hooks (prevents EnsureModHooksWrapped from re-wrapping!).
                FieldInfo parentBackingField = typeof(DelegatingModHooks).GetField("<Parent>k__BackingField", any);
                if (parentBackingField == null)
                    return;

                ModHooks cur = topHooks;
                DelegatingModHooks touchWrapper = null;
                int guard = 0;
                while (cur is DelegatingModHooks dmh && guard++ < 16)
                {
                    if (cur is GardenDelegatingModHooks)
                    {
                        _gardenHooksInstalled = true;
                        return;
                    }
                    if (string.Equals(cur.GetType().FullName, "SDViOSTouchControls.TouchDelegatingModHooks", StringComparison.Ordinal))
                        touchWrapper = dmh;
                    cur = dmh.Parent;
                }

                if (touchWrapper != null && touchWrapper.Parent != null)
                {
                    var gardenHooks = new GardenDelegatingModHooks(touchWrapper.Parent);
                    parentBackingField.SetValue(touchWrapper, gardenHooks);
                    _gardenHooksInstalled = true;
                    _monitor?.Log($"[{phase}] Installed Zero-Harmony GardenDelegatingModHooks inside TouchDelegatingModHooks.Parent (ExternalInput + Mailbox Guard active on 0% JIT!).", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[{phase}] GardenDelegatingModHooks install skipped: {ex.Message}", LogLevel.Trace);
            }
        }

        internal static void ApplyExternalControlInputStates(ExternalInputBridge ext, ref KeyboardState keyboardState, ref MouseState mouseState, ref GamePadState gamePadState)
        {
            ext.ApplyStardewGamepadMode();
            ext.SyncDirectZeroHarmonyInputStates();

            if (ext.ShouldPushMouseStateNow)
                mouseState = ext.CurrentMouseState;

            if (ext.IsPhysicalGamepadActive)
            {
                ext.EnsureValidLocalMultiplayerWindow();
                keyboardState = ext.BuildPhysicalKeyboardState();
                gamePadState = ext.GetEffectiveGamePadStateForGame();
                if (!ext.SuppressGamepadAButtonThisFrame)
                    Game1.lastCursorMotionWasMouse = false;
                if (Game1.activeClickableMenu != null || Game1.timerUntilMouseFade > 0 || ext.IsRightStickSteeringGameplayCursor || ext.IsRightStickMenuCursorActive)
                    Game1.wasMouseVisibleThisFrame = true;
                else
                    Game1.wasMouseVisibleThisFrame = false;
            }
            else if (ext.IsExternalMouseActive)
            {
                if (ext.HasPhysicalKeysPressed)
                    keyboardState = ext.MergeKeyboardState(keyboardState);
                gamePadState = default;
                Game1.lastCursorMotionWasMouse = true;
                Game1.wasMouseVisibleThisFrame = true;
            }
            else
            {
                if (ext.HasPhysicalKeysPressed)
                    keyboardState = ext.MergeKeyboardState(keyboardState);
                gamePadState = default;
            }
        }

        private static bool TryShowMailFrameworkModLetter(string topMailId)
        {
            if (string.IsNullOrEmpty(topMailId))
                return false;
            try
            {
                if (!_mfmResolved)
                {
                    _mfmResolved = true;
                    const BindingFlags statAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (!string.Equals(asm.GetName().Name, "MailFrameworkMod", StringComparison.OrdinalIgnoreCase))
                            continue;
                        Type ctrl = asm.GetType("MailFrameworkMod.MailController", throwOnError: false);
                        if (ctrl != null)
                        {
                            _mfmShowLetterMethod = ctrl.GetMethod("ShowLetter", statAny, null, Type.EmptyTypes, null)
                                                ?? ctrl.GetMethod("mailbox", statAny, null, Type.EmptyTypes, null);
                        }
                        break;
                    }
                }
                if (_mfmShowLetterMethod == null)
                    return false;

                // Only invoke MailFrameworkMod when vanilla Data/mail does NOT contain topMailId
                var mailDict = DataLoader.Mail(Game1.content);
                if (mailDict != null && mailDict.TryGetValue(topMailId, out string vText) && !string.IsNullOrEmpty(vText))
                    return false;
                if (topMailId.StartsWith("passedOut", StringComparison.Ordinal))
                    return false;

                object ret = _mfmShowLetterMethod.Invoke(null, null);
                if ((ret is bool b && b) || Game1.activeClickableMenu is LetterViewerMenu)
                {
                    _monitor?.Log($"[MailGuard] Opened MailFrameworkMod letter '{topMailId}' via Zero-Harmony bridge.", LogLevel.Info);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsPlayerMailboxTile(GameLocation loc, int tx, int ty)
        {
            if (loc == null) return false;
            try
            {
                if (loc is Farm farm)
                {
                    Point mainMb = farm.GetMainMailboxPosition();
                    if (mainMb.X == tx && (mainMb.Y == ty || mainMb.Y - 1 == ty))
                        return true;
                }
                if (loc.buildings != null)
                {
                    for (int i = 0; i < loc.buildings.Count; i++)
                    {
                        var b = loc.buildings[i];
                        if (b == null) continue;
                        Point bMb = b.getMailboxPosition();
                        if (bMb.X == tx && (bMb.Y == ty || bMb.Y - 1 == ty))
                            return true;
                    }
                }
                string prop = loc.doesTileHaveProperty(tx, ty, "Action", "Buildings");
                if (!string.IsNullOrEmpty(prop) && prop.StartsWith("Mailbox", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
            return false;
        }

        private sealed class GardenDelegatingModHooks : DelegatingModHooks
        {
            public GardenDelegatingModHooks(ModHooks parent) : base(parent) { }

            public override void OnGame1_UpdateControlInput(ref KeyboardState keyboardState, ref MouseState mouseState, ref GamePadState gamePadState, Action action)
            {
                var ext = ExternalInput;
                if (ext != null && ext.IsExternalDeviceActive)
                {
                    ApplyExternalControlInputStates(ext, ref keyboardState, ref mouseState, ref gamePadState);
                }
                base.OnGame1_UpdateControlInput(ref keyboardState, ref mouseState, ref gamePadState, action);
            }

            public override bool OnGameLocation_CheckAction(GameLocation location, xTile.Dimensions.Location tileLocation, xTile.Dimensions.Rectangle viewport, Farmer who, Func<bool> action)
            {
                int mbBefore = Game1.mailbox?.Count ?? 0;
                string topMailBefore = (mbBefore > 0 && Game1.mailbox != null) ? Game1.mailbox[0] : null;
                bool isMbTile = mbBefore > 0 && IsPlayerMailboxTile(location, tileLocation.X, tileLocation.Y);

                // Zero-Harmony bridge for MailFrameworkMod on 0% JIT iOS
                if (isMbTile && !string.IsNullOrEmpty(topMailBefore) && TryShowMailFrameworkModLetter(topMailBefore))
                {
                    ResetMailboxWatchdogSnapshot();
                    return true;
                }

                bool result = base.OnGameLocation_CheckAction(location, tileLocation, viewport, who, action);

                int mbAfter = Game1.mailbox?.Count ?? 0;
                if (mbBefore > 0 && mbAfter < mbBefore)
                {
                    bool hasViewer = Game1.activeClickableMenu is LetterViewerMenu;
                    _monitor?.Log($"[MailDiag:Open] Popped '{topMailBefore}' -> viewer={hasViewer}, remaining={mbAfter}", LogLevel.Info);

                    // If topMailBefore was an empty/flag-only entry that had no text in Data/mail,
                    // automatically advance through remaining valid letters in Game1.mailbox instead of stranding the player!
                    int safety = 0;
                    while (!hasViewer && location != null && Game1.mailbox != null && Game1.mailbox.Count > 0 && safety++ < 8)
                    {
                        string nextId = Game1.mailbox[0];
                        if (TryShowMailFrameworkModLetter(nextId))
                        {
                            hasViewer = true;
                            break;
                        }
                        location.mailbox();
                        hasViewer = Game1.activeClickableMenu is LetterViewerMenu;
                    }
                    ResetMailboxWatchdogSnapshot();
                }

                return result;
            }
        }

        private static bool ShouldBlockPrematureFishingRelease(FishingRod rod, bool logRelease)
        {
            if (rod == null || !rod.isTimingCast)
            {
                _fishingHoldRod = null;
                _fishingHoldStartMs = 0;
                return false;
            }

            if (rod.lastUser == null || !rod.lastUser.IsLocalPlayer || Engine == null)
                return false;

            if (!Engine.IsFishingCastButtonHeld())
            {
                if (logRelease && _fishingTraceCount < 10)
                {
                    _fishingTraceCount++;
                    long heldMs = (_fishingHoldStartMs > 0 && ReferenceEquals(_fishingHoldRod, rod))
                        ? (Environment.TickCount64 - _fishingHoldStartMs)
                        : 0;
                    _monitor?.Log($"[FishingTrace] Cast released: power={rod.castingPower:F2}, heldMs={heldMs}", LogLevel.Info);
                }
                _fishingHoldRod = null;
                _fishingHoldStartMs = 0;
                return false;
            }

            long nowMs = Environment.TickCount64;
            if (!ReferenceEquals(_fishingHoldRod, rod) || _fishingHoldStartMs <= 0)
            {
                _fishingHoldRod = rod;
                _fishingHoldStartMs = nowMs;
            }
            else if (nowMs - _fishingHoldStartMs > 15000)
            {
                // Safety failsafe if touch state was somehow lost for >15 seconds
                _fishingHoldRod = null;
                _fishingHoldStartMs = 0;
                return false;
            }

            return true;
        }

        private static bool PrefixFishingRodStartCasting(FishingRod __instance)
        {
            return !ShouldBlockPrematureFishingRelease(__instance, logRelease: false);
        }

        private static bool PrefixFishingRodDoStartCasting(FishingRod __instance)
        {
            return !ShouldBlockPrematureFishingRelease(__instance, logRelease: true);
        }

        private static bool PrefixFishingRodDoFunction(FishingRod __instance, Farmer who)
        {
            if (__instance != null && __instance.isTimingCast && who != null && who.IsLocalPlayer)
                return false;
            return true;
        }

        private static void PostfixBobberBarUpdate(BobberBar __instance)
        {
            if (__instance == null || Engine == null || !Engine.IsMobileModeForBobberPhysics)
                return;
            if (__instance.fadeIn || __instance.fadeOut)
                return;

            // Mobile-feel bobber bar control: eliminate PC's icy inertia & overly bouncy edges
            float spd = __instance.bobberBarSpeed;
            bool reversing = (__instance.buttonPressed && spd > 0.35f) || (!__instance.buttonPressed && spd < -0.35f);
            spd *= reversing ? 0.84f : 0.94f;

            // Soften bottom & top edge rebound (preserve Lead Bobber "(O)692" near-zero bounce)
            bool hasLeadBobber = __instance.bobbers != null && __instance.bobbers.Contains("(O)692");
            float maxPos = 568f - __instance.bobberBarHeight;
            if (!hasLeadBobber && __instance.bobberBarPos >= maxPos - 0.5f && spd < -0.4f)
            {
                spd *= 0.50f;
            }
            else if (__instance.bobberBarPos <= 0.5f && spd > 0.4f)
            {
                spd *= 0.55f;
            }

            __instance.bobberBarSpeed = Math.Clamp(spd, -5.8f, 5.8f);
        }

        private static bool PrefixHudMessageDraw(HUDMessage __instance, SpriteBatch b, int i, ref int heightUsed)
        {
            if (__instance == null || b == null || Engine == null)
                return true;

            try
            {
                Rectangle tsarea = Game1.graphics.GraphicsDevice.Viewport.GetTitleSafeArea();
                if (!Engine.TryGetHudMessageRelocation(tsarea, out float targetX, out float topStartY, out float maxRightX))
                    return true;

                string msg = __instance.message ?? string.Empty;
                float alpha = __instance.transparency;

                if (__instance.noIcon)
                {
                    Vector2 textSz = Game1.dialogueFont.MeasureString(msg);
                    int boxH = (int)textSz.Y + 64;
                    int boxW = (int)textSz.X + 48;
                    int drawX = (int)Math.Round(targetX);
                    if (drawX + boxW > maxRightX)
                        drawX = Math.Max(tsarea.Left + 16, (int)(maxRightX - boxW));
                    int drawY = (int)Math.Round(topStartY + heightUsed);
                    heightUsed += boxH;

                    if (drawY + boxH <= tsarea.Bottom - 16)
                    {
                        IClickableMenu.drawHoverText(
                            b,
                            msg,
                            Game1.dialogueFont,
                            0, 0, -1, null, -1, null, null, 0, null, -1,
                            drawX,
                            drawY,
                            alpha);
                    }
                    return false;
                }

                const int boxStepH = 104;
                float msgWidth = Game1.smallFont.MeasureString(msg).X;
                float totalW = 104f + msgWidth + 24f;
                float drawXf = targetX;
                if (drawXf + totalW > maxRightX)
                    drawXf = Math.Max(tsarea.Left + 16f, maxRightX - totalW);
                float drawYf = topStartY + heightUsed;
                heightUsed += boxStepH;

                if (drawYf + 96f > tsarea.Bottom - 16f)
                    return false;

                Vector2 pos = new Vector2((float)Math.Floor(drawXf), (float)Math.Floor(drawYf));

                // 1. Native Stardew wooden scroll box (100% skinned by Content Patcher UI mods like Cat Valley UI!)
                bool rareItem = __instance.messageSubject is StardewValley.Object obj && obj.sellToStorePrice() > 500;
                Rectangle leftCapSrc = rareItem
                    ? new Rectangle(163, 399, 26, 24)
                    : new Rectangle(293, 360, 26, 24);
                b.Draw(Game1.mouseCursors, pos, leftCapSrc, Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
                b.Draw(Game1.mouseCursors, new Vector2(pos.X + 104f, pos.Y), new Rectangle(319, 360, 1, 24), Color.White * alpha, 0f, Vector2.Zero, new Vector2(msgWidth, 4f), SpriteEffects.None, 1f);
                b.Draw(Game1.mouseCursors, new Vector2(pos.X + 104f + msgWidth, pos.Y), new Rectangle(323, 360, 6, 24), Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);

                // 2. Item icon or HUD status icon with vanilla pop-in bounce
                Vector2 iconPos = new Vector2(pos.X + 16f, pos.Y + 16f);
                float bounce = Math.Max(0f, (__instance.timeLeft - 3000f) / 900f);
                if (__instance.messageSubject != null)
                {
                    __instance.messageSubject.drawInMenu(b, iconPos, 1f + bounce, alpha, 1f, StackDrawType.Hide);
                }
                else
                {
                    Vector2 iconCenter = iconPos + new Vector2(32f, 32f);
                    switch (__instance.whatType)
                    {
                        case 1: // newQuest_type
                            b.Draw(Game1.mouseCursors, iconCenter, new Rectangle(294, 392, 16, 16), Color.White * alpha, 0f, new Vector2(8f, 8f), 4f + bounce, SpriteEffects.None, 1f);
                            break;
                        case 2: // error_type
                            b.Draw(Game1.mouseCursors, iconCenter, new Rectangle(403, 496, 5, 14), Color.White * alpha, 0f, new Vector2(3f, 7f), 4f + bounce, SpriteEffects.None, 1f);
                            break;
                        case 3: // stamina_type
                            b.Draw(Game1.mouseCursors, iconCenter, new Rectangle(268, 470, 16, 16), Color.White * alpha, 0f, new Vector2(8f, 8f), 4f + bounce, SpriteEffects.None, 1f);
                            break;
                        case 4: // health_type
                            b.Draw(Game1.mouseCursors, iconCenter, new Rectangle(0, 411, 16, 16), Color.White * alpha, 0f, new Vector2(8f, 8f), 4f + bounce, SpriteEffects.None, 1f);
                            break;
                        case 5: // screenshot_type
                            b.Draw(Game1.mouseCursors, iconCenter, new Rectangle(16, 411, 16, 16), Color.White * alpha, 0f, new Vector2(8f, 8f), 4f + bounce, SpriteEffects.None, 1f);
                            break;
                        case 6: // speed_type
                            b.Draw(Game1.mouseCursors2, iconCenter, new Rectangle(96, 32, 16, 16), Color.White * alpha, 0f, new Vector2(8f, 8f), 4f + bounce, SpriteEffects.None, 1f);
                            break;
                    }
                }

                // 3. Stack count digits & item label
                Vector2 digitPos = new Vector2(iconPos.X + 51f, iconPos.Y + 51f);
                if (__instance.number > 1)
                {
                    Utility.drawTinyDigits(__instance.number, b, digitPos, 3f, 1f, Color.White * alpha);
                }

                Vector2 textPos = new Vector2(digitPos.X + 32f, digitPos.Y - 33f);
                Utility.drawTextWithShadow(b, msg, Game1.smallFont, textPos, Game1.textColor * alpha, 1f, 1f, -1, -1, alpha);
                return false;
            }
            catch
            {
                return true;
            }
        }

        private void RunShaderDiagnostic(string phase)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var sb = new StringBuilder();
            sb.Append($"[ShaderDiag:{phase}] ");
            try
            {
                GraphicsDevice gd = Game1.graphics?.GraphicsDevice;
                if (gd != null)
                {
                    int glMaj = (int)(typeof(GraphicsDevice).GetField("glMajorVersion", any)?.GetValue(gd) ?? -1);
                    int glMin = (int)(typeof(GraphicsDevice).GetField("glMinorVersion", any)?.GetValue(gd) ?? -1);
                    int maxTexSlots = (int)(typeof(GraphicsDevice).GetField("MaxTextureSlots", any)?.GetValue(gd) ?? -1);
                    int maxTexSz = (int)(typeof(GraphicsDevice).GetField("_maxTextureSize", any)?.GetValue(gd) ?? -1);
                    object gdCaps = typeof(GraphicsDevice).GetProperty("GraphicsCapabilities", any)?.GetValue(gd);
                    bool npot = gdCaps != null && (bool)(gdCaps.GetType().GetProperty("SupportsNonPowerOfTwo", any)?.GetValue(gdCaps) ?? false);

                    string glVer = "?";
                    string glRen = "?";
                    try
                    {
                        Type glType = typeof(GraphicsDevice).Assembly.GetType("MonoGame.OpenGL.GL");
                        MethodInfo mGetStr = glType?.GetMethod("GetString", any);
                        if (mGetStr != null)
                        {
                            Type strNameEnum = mGetStr.GetParameters()[0].ParameterType;
                            glVer = (mGetStr.Invoke(null, new[] { Enum.ToObject(strNameEnum, 0x1F02) }) as string) ?? "?";
                            glRen = (mGetStr.Invoke(null, new[] { Enum.ToObject(strNameEnum, 0x1F01) }) as string) ?? "?";
                        }
                    }
                    catch { }

                    var pp = gd.PresentationParameters;
                    var scr = Game1.game1?.screen;
                    bool shouldBuf = false;
                    try { shouldBuf = Game1.game1 != null && Game1.game1.ShouldDrawOnBuffer(); } catch { }
                    sb.Append($"GL='{glVer}' Ren='{glRen}' Ver={glMaj}.{glMin} Slots={maxTexSlots} MaxTex={maxTexSz} NPOT={npot} Prof={gd.GraphicsProfile} ");
                    sb.Append($"BB={pp?.BackBufferWidth ?? -1}x{pp?.BackBufferHeight ?? -1} VP={gd.Viewport.Width}x{gd.Viewport.Height} ");
                    sb.Append($"Screen={(scr != null ? $"{scr.Width}x{scr.Height}" : "null")} Zoom={Game1.options?.zoomLevel ?? -1f:F2} DrawBuf={shouldBuf} ");
                    sb.Append($"Loc={Game1.currentLocation?.Name ?? "null"}(Out={Game1.currentLocation?.IsOutdoors}) Time={Game1.timeOfDay} ");
                }

                // Reflect DynamicShader runtime state if installed
                Assembly dsAsm = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(asm.GetName().Name, "DynamicShader", StringComparison.OrdinalIgnoreCase))
                    {
                        dsAsm = asm;
                        break;
                    }
                }

                if (dsAsm == null)
                {
                    sb.Append("DS=NotLoaded");
                }
                else
                {
                    Type tRt = dsAsm.GetType("DynamicShader.Core.Rendering.RenderRuntime");
                    object rt = tRt?.GetProperty("Active", any)?.GetValue(null);
                    if (rt == null)
                    {
                        sb.Append("DS.Active=null");
                    }
                    else
                    {
                        object state = tRt.GetProperty("State", any)?.GetValue(rt);
                        sb.Append($"DS(State={state}");

                        object sm = tRt.GetProperty("ShaderManager", any)?.GetValue(rt);
                        if (sm != null)
                        {
                            Type tSm = sm.GetType();
                            bool hasEff = tSm.GetField("_shadowEffect", any)?.GetValue(sm) != null;
                            bool shRdy = (bool)(tSm.GetProperty("IsShadowReady", any)?.GetValue(sm) ?? false);
                            bool grRdy = (bool)(tSm.GetProperty("IsGodRayReady", any)?.GetValue(sm) ?? false);
                            bool clRdy = (bool)(tSm.GetProperty("IsCanopyLightReady", any)?.GetValue(sm) ?? false);
                            bool tsRdy = (bool)(tSm.GetProperty("IsTiltShiftReady", any)?.GetValue(sm) ?? false);
                            object caps = tSm.GetProperty("Capabilities", any)?.GetValue(sm);
                            sb.Append($" Eff={hasEff} Rdy(Sh={shRdy},GR={grRdy},CL={clRdy},TS={tsRdy}) Caps={caps}");
                        }

                        object wr = tRt.GetProperty("WaterReflectionRenderer", any)?.GetValue(rt);
                        if (wr != null)
                        {
                            Type tWr = wr.GetType();
                            bool wEff = tWr.GetField("_effect", any)?.GetValue(wr) != null;
                            bool wFault = (bool)(tWr.GetField("_effectLoadFaulted", any)?.GetValue(wr) ?? false);
                            object surf = tWr.GetField("_surfaceRenderer", any)?.GetValue(wr);
                            bool sLoadF = surf != null && (bool)(surf.GetType().GetField("_loadFaulted", any)?.GetValue(surf) ?? false);
                            bool sEffF = surf != null && (bool)(surf.GetType().GetField("_effectLoadFaulted", any)?.GetValue(surf) ?? false);
                            bool sDrawF = surf != null && (bool)(surf.GetType().GetField("_drawFaulted", any)?.GetValue(surf) ?? false);
                            object perScr = tWr.GetField("_screenStates", any)?.GetValue(wr);
                            object scrSt = perScr?.GetType().GetProperty("Value", any)?.GetValue(perScr);
                            bool wrStFault = scrSt != null && (bool)(scrSt.GetType().GetProperty("IsFaulted", any)?.GetValue(scrSt) ?? false);
                            bool wrHasOut = scrSt != null && (bool)(scrSt.GetType().GetProperty("HasPreparedOutput", any)?.GetValue(scrSt) ?? false);
                            sb.Append($" Water(Eff={wEff},EffFault={wFault},StFault={wrStFault},HasOut={wrHasOut},SurfFault={sLoadF}/{sEffF}/{sDrawF})");
                        }

                        Type tSs = dsAsm.GetType("DynamicShader.Core.Data.ShadowState");
                        object ssInst = tSs?.GetProperty("Instance", any)?.GetValue(null);
                        bool isShTime = ssInst != null && (bool)(tSs.GetProperty("IsShadowRenderingTime", any)?.GetValue(ssInst) ?? false);
                        object sr = tRt.GetProperty("ShadowRenderer", any)?.GetValue(rt);
                        bool shRend = sr != null && (bool)(sr.GetType().GetProperty("ShadowsRenderedThisFrame", any)?.GetValue(sr) ?? false);
                        sb.Append($" ShTime={isShTime} ShRend={shRend})");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.Append($"DiagErr={ex.GetType().Name}:{ex.Message}");
            }

            string line = sb.ToString();
            _monitor?.Log(line, LogLevel.Info);
            try
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string errDir = Path.Combine(docs, "ErrorLogs");
                Directory.CreateDirectory(errDir);
                string diagPath = Path.Combine(errDir, "shader-diag.log");
                File.AppendAllText(diagPath, $"[{DateTime.Now:HH:mm:ss}] {line}\n");
            }
            catch { }
        }

        private static bool PrefixSoundBankExists(SoundBank __instance, string __0, ref bool __result)
        {
            if (__instance == null || string.IsNullOrEmpty(__0))
            {
                __result = false;
                return false;
            }
            try
            {
                var f = _soundBankSoundsField ?? (_soundBankSoundsField = typeof(SoundBank).GetField("_sounds", BindingFlags.Instance | BindingFlags.NonPublic));
                if (f?.GetValue(__instance) is System.Collections.IDictionary dict)
                {
                    __result = dict.Contains(__0);
                    return false;
                }
            }
            catch { }
            __result = true;
            return false;
        }

        private static bool PrefixSoundBankGetCueDefinition(string __0, ref CueDefinition __result)
        {
            __result = new CueDefinition { name = __0 ?? string.Empty };
            return false;
        }

        private static void PostfixDidPlayerJustRightClick(ref bool __result)
        {
            if (SynthesizingTapAction)
                __result = true;
        }

        private static void PostfixSmapiAssetLoaded(IAssetName assetName)
        {
            if (_bootReadySignaled || assetName == null)
                return;
            int cnt = ++_bootAssetLoadedCount;
            long nowMs = Environment.TickCount64;
            if (nowMs - _bootLastAssetReportMs < 75 && cnt > 1)
                return;
            _bootLastAssetReportMs = nowMs;
            string name = assetName.Name ?? "";
            string stage = ClassifyBootAssetStage(name);
            int pct = Math.Min(87, 63 + (int)(Math.Min(1.0, cnt / 260.0) * 24.0));
            string shortName = name.Length > 34 ? name.Substring(name.Length - 34) : name;
            ReportNativeBoot(pct, stage, $"Tài nguyên #{cnt}: {shortName}", 3);
        }

        private static bool OnMobileUpdate(object pad, GameTime gameTime)
        {
            if (Engine == null)
                return false;

            try
            {
                Engine.TrackPadReference(pad);
                Engine.PollPendingEditLayoutFlag();
                bool blockExt = Engine.IsUiOverlayBlockingExternalInput(pad);
                if (ExternalInput != null)
                {
                    ExternalInput.SetManagedSuppressed(blockExt);
                    ExternalInput.PollFrame();
                }

                if (Engine.ShouldInterceptGameplay(pad))
                {
                    Engine.UpdateGameplay(pad, gameTime);
                    ExternalInput?.ApplyStardewGamepadMode();
                    return true;
                }

                if (Engine.UpdateInMenu(pad, gameTime))
                {
                    ExternalInput?.ApplyStardewGamepadMode();
                    return true;
                }

                // Even when VirtualGamepad UI mode is selected (IsMobileModeActive == false) or
                // during dialogue/cutscenes, if an external controller/keyboard/mouse is active
                // with 0 direct screen touches, NEVER fall through to SDViOSTouchControls.TouchVirtualPad.Update()
                // -> ForwardInputToGame() (which calls Game1.player.Halt() every frame when LeftStick==Zero,
                // resetting the farmer's running animation back to frame 0 every tick!).
                if (ExternalInput != null && ExternalInput.IsExternalDeviceActive && !blockExt &&
                    !Engine.IsLegacyFullScreenModalOpen(pad) && !Engine.IsQuickSaveReloading(pad))
                {
                    TouchCollection fallbackTouches = TouchPanel.GetState();
                    int activeTouchCnt = MobileControlsEngine.CountActiveTouches(fallbackTouches);
                    if (activeTouchCnt == 0)
                    {
                        Engine.ResetVirtualPadForExternal(pad);
                        ExternalInput.ApplyStardewGamepadMode();
                        return true;
                    }
                    if (Engine.TryHandleSkipCutsceneTap(pad, fallbackTouches))
                    {
                        Engine.ResetVirtualPadForExternal(pad);
                        ExternalInput.ApplyStardewGamepadMode();
                        return true;
                    }
                    ExternalInput.NotifyDirectTouch(activeTouchCnt);
                }

                return false;
            }
            catch (Exception ex)
            {
                if (++_errCount <= 5)
                    _monitor?.Log($"MobileControls Update error: {ex}", LogLevel.Warn);
                return false;
            }
        }

        private static bool OnMobileDraw(object pad, SpriteBatch spriteBatch)
        {
            if (Engine == null)
                return false;

            try
            {
                if (Engine.ShouldInterceptGameplay(pad))
                {
                    Engine.DrawGameplayOverlay(spriteBatch);
                    return true;
                }
                if (Engine.ShouldSuppressOriginalDraw(pad))
                {
                    Engine.DrawMenuContextOverlay(pad, spriteBatch);
                    return true;
                }
                if (Engine.ShouldAutoHideLegacyVirtualGamepad(pad))
                {
                    Engine.DrawSkipCutsceneButtonOnly(pad, spriteBatch);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                if (++_errCount <= 5)
                    _monitor?.Log($"MobileControls Draw error: {ex}", LogLevel.Warn);
                return false;
            }
        }
    }
}
