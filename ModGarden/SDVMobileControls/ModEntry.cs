using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using StardewModdingAPI;
using StardewModdingAPI.Events;
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
            InstallMobileHarmonyPatches("Entry");
            InstallExternalInputHarmonyPatches("Entry");

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
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

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            RestoreStashedHudMessagesIfNeeded();
            try { ExternalInput?.PollFrame(); } catch { }
            try { Engine?.OnUpdateTickedGlobal(); } catch { }
            try { RamOptimizer?.PollManualPruneFlag(); } catch { }
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
            if (BoundPadTypes.Count >= 2 && _extHarmonyPatchesInstalled && PatchedTouchHookTypes.Count >= 1)
                return;
            if (BoundPadTypes.Count > 0 && _retryTicks > 300)
                return;
            if (++_retryTicks % 30 == 0)
            {
                BindAllLoadedTouchVirtualPads("UpdateTicked");
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

                _harmonyPatchesInstalled = true;
                _monitor?.Log($"[{phase}] Installed {applied}/7 Mobile Harmony patches (FishingRod + BobberBar + Direct HUDMessage + TapAction RightClick + BootAssetProgress).", LogLevel.Info);
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
                    catch
                    {
                        return 0;
                    }
                }

                int patchedNow = 0;

                if (!_extHarmonyPatchesInstalled)
                {
                    // 1. GamePad (Priority.High = 600 -> preempts SDViOSTouchControls when physical gamepad is active!)
                    patchedNow += ApplyExtPatch(typeof(GamePad).GetMethod("GetState", any, null, new[] { typeof(PlayerIndex) }, null), nameof(PrefixExtGamePadState), 600, null, 400);
                    patchedNow += ApplyExtPatch(typeof(InputState).GetMethod("GetGamePadState", any, null, Type.EmptyTypes, null), nameof(PrefixExtGamePadState), 600, null, 400);

                    // 2. Mouse (Priority.High = 600 -> full external mouse/hover/right-click/scroll + gamepad snapped cursor!)
                    patchedNow += ApplyExtPatch(typeof(Mouse).GetMethod("GetState", any, null, Type.EmptyTypes, null), nameof(PrefixExtMouseState), 600, null, 400);
                    patchedNow += ApplyExtPatch(typeof(Mouse).GetMethod("GetState", any, null, new[] { typeof(GameWindow) }, null), nameof(PrefixExtMouseState), 600, null, 400);
                    patchedNow += ApplyExtPatch(typeof(Mouse).GetMethod("PlatformGetState", any, null, new[] { typeof(GameWindow) }, null), nameof(PrefixExtMouseState), 600, null, 400);
                    patchedNow += ApplyExtPatch(typeof(InputState).GetMethod("GetMouseState", any, null, Type.EmptyTypes, null), nameof(PrefixExtMouseState), 600, null, 400);
                    patchedNow += ApplyExtPatch(typeof(Game1).GetMethod("setMousePositionRaw", any, null, new[] { typeof(int), typeof(int) }, null), nameof(PrefixExtSetMousePositionRaw), 600, null, 400);

                    // 3. Keyboard (Priority.Low = 200 Postfix -> merges physical hardware keys into ANY returned KeyboardState!)
                    patchedNow += ApplyExtPatch(typeof(Keyboard).GetMethod("GetState", any, null, Type.EmptyTypes, null), null, 400, nameof(PostfixExtKeyboardState), 200);
                    patchedNow += ApplyExtPatch(typeof(Game1).GetMethod("GetKeyboardState", any, null, Type.EmptyTypes, null), null, 400, nameof(PostfixExtKeyboardState), 200);
                    patchedNow += ApplyExtPatch(typeof(InputState).GetMethod("GetKeyboardState", any, null, Type.EmptyTypes, null), null, 400, nameof(PostfixExtKeyboardState), 200);

                    // 4. SMAPI SInputState (so ALL SMAPI mods & Events.Input.ButtonPressed receive real external Gamepad/Keyboard/Mouse!)
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
                        patchedNow += ApplyExtPatch(sInputType.GetMethod("GetGamePadState", any, null, Type.EmptyTypes, null), nameof(PrefixExtGamePadState), 600, null, 400);
                        patchedNow += ApplyExtPatch(sInputType.GetProperty("GamePadState", any)?.GetGetMethod(true), nameof(PrefixExtGamePadState), 600, null, 400);
                        patchedNow += ApplyExtPatch(sInputType.GetMethod("GetMouseState", any, null, Type.EmptyTypes, null), nameof(PrefixExtMouseState), 600, null, 400);
                        patchedNow += ApplyExtPatch(sInputType.GetProperty("MouseState", any)?.GetGetMethod(true), nameof(PrefixExtMouseState), 600, null, 400);
                        patchedNow += ApplyExtPatch(sInputType.GetMethod("GetKeyboardState", any, null, Type.EmptyTypes, null), null, 400, nameof(PostfixExtKeyboardState), 200);
                        patchedNow += ApplyExtPatch(sInputType.GetProperty("KeyboardState", any)?.GetGetMethod(true), null, 400, nameof(PostfixExtKeyboardState), 200);
                    }

                    _extHarmonyPatchesInstalled = true;
                }

                // 5. Patch SDViOSTouchControls.TouchDelegatingModHooks.OnGame1_UpdateControlInput so it yields cleanly to real external devices!
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
                        if (ApplyExtPatch(mUpdCtrl, nameof(PrefixExtUpdateControlInput), 600, null, 400) > 0)
                        {
                            PatchedTouchHookTypes.Add(thType);
                            patchedNow++;
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
                __result = ext.CurrentGamePadState;
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

            if (ext.HasPhysicalKeysPressed)
                keyboardState = ext.MergeKeyboardState(keyboardState);

            if (ext.ShouldOverrideMouse)
                mouseState = ext.CurrentMouseState;

            if (ext.IsPhysicalGamepadActive)
            {
                gamePadState = ext.CurrentGamePadState;
                Game1.lastCursorMotionWasMouse = false;
                // Show cursor in menus/title when gamepad snaps cursor, or during gameplay when right stick steers cursor
                if (Game1.activeClickableMenu != null || gamePadState.ThumbSticks.Right.LengthSquared() > 0.04f)
                    Game1.wasMouseVisibleThisFrame = true;
                else
                    Game1.wasMouseVisibleThisFrame = false;
            }
            else if (ext.IsExternalMouseActive)
            {
                gamePadState = default;
                Game1.lastCursorMotionWasMouse = true;
                Game1.wasMouseVisibleThisFrame = true;
            }
            else
            {
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
                ExternalInput?.PollFrame();
                Engine.TrackPadReference(pad);
                if (Engine.ShouldInterceptGameplay(pad))
                {
                    Engine.UpdateGameplay(pad, gameTime);
                    return true;
                }

                if (Engine.UpdateInMenu(pad, gameTime))
                    return true;

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
