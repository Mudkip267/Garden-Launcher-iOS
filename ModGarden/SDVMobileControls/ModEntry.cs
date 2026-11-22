using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Tools;

namespace SDVMobileControls
{
    public sealed class ModEntry : Mod
    {
        internal static MobileControlsEngine Engine;
        internal static MapRamOptimizer RamOptimizer;
        private static IMonitor _monitor;
        private static readonly HashSet<Type> BoundPadTypes = new HashSet<Type>();
        private static int _errCount;
        private static int _retryTicks;
        private static int _audioDiagTicks;
        private static bool _audioDiagRanLaunch;
        private static bool _audioDiagRanWorld;
        private static bool _audioDiagWroteThisSession;
        private static bool _fishingGuardInstalled;
        private static long _fishingHoldStartMs;
        private static FishingRod _fishingHoldRod;

        public override void Entry(IModHelper helper)
        {
            _monitor = Monitor;
            var cfg = new MobileConfig();
            Engine = new MobileControlsEngine(cfg, Monitor, helper.DirectoryPath);
            RamOptimizer = new MapRamOptimizer(helper, Monitor);

            BindAllLoadedTouchVirtualPads("Entry");
            InstallFishingCastGuard("Entry");

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.Saved += OnSaved;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.Player.Warped += OnWarped;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            BindAllLoadedTouchVirtualPads("GameLaunched");
            InstallFishingCastGuard("GameLaunched");
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
            }
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            try { Engine?.OnUpdateTickedGlobal(); } catch { }
            try { RamOptimizer?.PollManualPruneFlag(); } catch { }
            if (!_audioDiagRanLaunch && ++_audioDiagTicks >= 120)
            {
                _audioDiagRanLaunch = true;
                RunAudioDiagnosticAndRepair("GameLaunched+2s");
            }
            if (BoundPadTypes.Count >= 2 || (BoundPadTypes.Count > 0 && _retryTicks > 300))
                return;
            if (++_retryTicks % 30 == 0)
                BindAllLoadedTouchVirtualPads("UpdateTicked");
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
            }
            catch { }
        }

        private static void OnPopulateExtraKeysList(List<Keys> keyList)
        {
            try
            {
                Engine?.PopulateExtraKeysList(keyList);
            }
            catch { }
        }

        private static void InstallFishingCastGuard(string phase)
        {
            if (_fishingGuardInstalled)
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

                MethodInfo orig = typeof(FishingRod).GetMethod("startCasting", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo pref = typeof(ModEntry).GetMethod(nameof(PrefixFishingRodStartCasting), BindingFlags.Static | BindingFlags.NonPublic);
                if (orig == null || pref == null)
                    return;

                object harmony = Activator.CreateInstance(harmonyType, new object[] { "GardenLauncher.MobileControls.FishingGuard" });
                object hmPref = Activator.CreateInstance(harmonyMethodType, new object[] { pref });

                MethodInfo patchMethod = null;
                foreach (MethodInfo mi in harmonyType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (mi.Name != "Patch") continue;
                    var ps = mi.GetParameters();
                    if (ps.Length >= 2 && ps[0].ParameterType == typeof(MethodBase) && ps[1].ParameterType == harmonyMethodType)
                    {
                        patchMethod = mi;
                        break;
                    }
                }
                if (patchMethod == null)
                    return;

                var parms = patchMethod.GetParameters();
                object[] invokeArgs = new object[parms.Length];
                invokeArgs[0] = orig;
                invokeArgs[1] = hmPref;
                patchMethod.Invoke(harmony, invokeArgs);

                _fishingGuardInstalled = true;
                _monitor?.Log($"[{phase}] Installed FishingRod.startCasting charge-hold guard (works in ALL control modes).", LogLevel.Info);
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[{phase}] FishingRod guard skipped: {ex.Message}", LogLevel.Trace);
            }
        }

        private static bool PrefixFishingRodStartCasting(FishingRod __instance)
        {
            if (__instance == null || !__instance.isTimingCast)
            {
                _fishingHoldRod = null;
                _fishingHoldStartMs = 0;
                return true;
            }

            if (__instance.lastUser == null || !__instance.lastUser.IsLocalPlayer || Engine == null)
                return true;

            if (!Engine.IsFishingCastButtonHeld())
            {
                _fishingHoldRod = null;
                _fishingHoldStartMs = 0;
                return true;
            }

            long nowMs = Environment.TickCount64;
            if (!ReferenceEquals(_fishingHoldRod, __instance) || _fishingHoldStartMs <= 0)
            {
                _fishingHoldRod = __instance;
                _fishingHoldStartMs = nowMs;
            }
            else if (nowMs - _fishingHoldStartMs > 15000)
            {
                // Safety failsafe if touch state was somehow lost for >15 seconds
                _fishingHoldRod = null;
                _fishingHoldStartMs = 0;
                return true;
            }

            return false;
        }

        private static bool OnMobileUpdate(object pad, GameTime gameTime)
        {
            if (Engine == null)
                return false;

            try
            {
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
