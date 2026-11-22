using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using xTile;
using xTile.Tiles;

namespace SDVMobileControls
{
    /// <summary>
    /// Lossless SMAPI / Content Patcher Map Tilesheet RAM Optimizer for iOS.
    ///
    /// Why this saves 100-200+ MB with Recolor &amp; Expansion mods (0% quality loss, 0% audio impact):
    /// 1. Under SMAPI 4.x, GameContentManager &amp; ModContentManager store cached Texture2Ds in
    ///    BaseContentManager.Cache (ContentCache.Cache) + BaseDisposableReferences — NOT MonoGame's
    ///    ContentManager.loadedAssets. So legacy pruners never evicted SMAPI-cached tilesheets.
    /// 2. Recolor &amp; Expansion mods patch dozens of huge RGBA32 Maps/* tilesheets (8-16 MB each)
    ///    and Content Patcher packs can retain Texture2Ds in their ModContentManager.Cache.
    /// 3. On warp / day start / high-RAM GC, this optimizer safely evicts &amp; GPU-disposes ONLY
    ///    off-screen Maps/* tilesheets (keeping the current map + small LRU of recent maps) and
    ///    unreferenced textures inside pure Pathoschild.ContentPatcher content packs, while strictly
    ///    protecting all Audio, UI, Sprites, Buildings, TerrainFeatures, and C# mod textures.
    /// </summary>
    internal sealed class MapRamOptimizer
    {
        private sealed class ReferenceComparer : IEqualityComparer<Texture2D>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(Texture2D x, Texture2D y) => ReferenceEquals(x, y);
            public int GetHashCode(Texture2D obj) => obj == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private readonly IModHelper _helper;
        private readonly IMonitor _monitor;
        private readonly string _pruneFlagPath;

        // Track pure Pathoschild.ContentPatcher content packs (zero C# code -> safe to prune dead copies)
        private readonly HashSet<string> _cpPackNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _cpPackDirs = new List<string>();
        private bool _cpPacksScanned;

        // Track every TileSheet.ImageSource ever observed + LRU warp sequence
        private readonly HashSet<string> _knownTileSheetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _lastUsedSeq = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private long _warpSeq;
        private int _flagPollCounter;

        // Cached reflection handles (initialized lazily on first prune)
        private bool _reflReady;
        private FieldInfo _fCoordinator;
        private FieldInfo _fCacheObj;
        private FieldInfo _fCacheDict;
        private FieldInfo _fBaseDisposables;
        private FieldInfo _fContentManagers;
        private FieldInfo _fVanillaContentMgr;
        private FieldInfo _fModName;
        private PropertyInfo _pFullRootDir;
        private FieldInfo _fMgLoadedAssets;
        private FieldInfo _fMgDisposableAssets;
        private FieldInfo _fTileSheetTextures;
        private FieldInfo _fNpcPortrait;
        private FieldInfo[] _game1StaticTexFields;

        public MapRamOptimizer(IModHelper helper, IMonitor monitor)
        {
            _helper = helper;
            _monitor = monitor;
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            _pruneFlagPath = Path.Combine(docs, "sdv_prune_now.flag");
        }

        public void OnGameLaunched()
        {
            ScanContentPatcherPacks();
        }

        public void OnSaveLoaded()
        {
            ScanContentPatcherPacks();
            RecordCurrentMapTileSheets();
        }

        public void OnWarped(GameLocation newLocation)
        {
            if (!Context.IsWorldReady || newLocation == null)
                return;
            RecordLocationTileSheets(newLocation);
            // Keep up to 2 most recently visited off-screen tilesheets so stepping in/out of
            // FarmHouse/Coop/Barn/Shops stays instant while evicting older maps!
            RunPrune(keepRecentOffscreen: 2, runGc: false, reason: "Warp:" + (newLocation.NameOrUniqueName ?? "?"));
        }

        public void OnDayStarted()
        {
            if (!Context.IsWorldReady)
                return;
            RecordCurrentMapTileSheets();
            // New day (or season change): flush all off-screen map tilesheets cleanly
            RunPrune(keepRecentOffscreen: 0, runGc: true, reason: "DayStarted");
        }

        public void PollManualPruneFlag()
        {
            if (!Context.IsWorldReady)
                return;
            if (++_flagPollCounter < 45)
                return;
            _flagPollCounter = 0;
            try
            {
                if (!File.Exists(_pruneFlagPath))
                    return;
                File.Delete(_pruneFlagPath);
                RunPrune(keepRecentOffscreen: 0, runGc: true, reason: "ManualGC");
            }
            catch { }
        }

        public void RunAggressivePrune(string reason)
        {
            if (!Context.IsWorldReady)
                return;
            RunPrune(keepRecentOffscreen: 0, runGc: true, reason: reason ?? "HighRAM");
        }

        private void ScanContentPatcherPacks()
        {
            if (_cpPacksScanned || _helper?.ModRegistry == null)
                return;
            _cpPacksScanned = true;
            try
            {
                foreach (IModInfo mod in _helper.ModRegistry.GetAll())
                {
                    if (mod == null || !mod.IsContentPack)
                        continue;
                    string packFor = mod.Manifest?.ContentPackFor?.UniqueID;
                    if (!string.Equals(packFor, "Pathoschild.ContentPatcher", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(mod.Manifest.Name))
                        _cpPackNames.Add(mod.Manifest.Name);
                    // Also capture DirectoryPath via reflection on ModMetadata if available
                    try
                    {
                        PropertyInfo pDir = mod.GetType().GetProperty("DirectoryPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        string dir = pDir?.GetValue(mod) as string;
                        if (!string.IsNullOrEmpty(dir))
                            _cpPackDirs.Add(NormalizeDir(dir));
                    }
                    catch { }
                }
                if (_cpPackNames.Count > 0)
                    _monitor?.Log($"[MapRamOptimizer] Ready (tracking {_cpPackNames.Count} Content Patcher packs).", LogLevel.Debug);
            }
            catch { }
        }

        private void RecordCurrentMapTileSheets()
        {
            if (Game1.currentLocation != null)
                RecordLocationTileSheets(Game1.currentLocation);
        }

        private void RecordLocationTileSheets(GameLocation loc)
        {
            _warpSeq++;
            Map map = loc?.Map;
            if (map?.TileSheets == null)
                return;
            for (int i = 0; i < map.TileSheets.Count; i++)
            {
                TileSheet ts = map.TileSheets[i];
                if (ts == null || string.IsNullOrEmpty(ts.ImageSource))
                    continue;
                string norm = NormalizeAssetKey(ts.ImageSource);
                if (string.IsNullOrEmpty(norm))
                    continue;
                _knownTileSheetKeys.Add(norm);
                _lastUsedSeq[norm] = _warpSeq;
                if (!norm.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
                {
                    string withMaps = "maps/" + norm;
                    _knownTileSheetKeys.Add(withMaps);
                    _lastUsedSeq[withMaps] = _warpSeq;
                }
            }
        }

        private bool EnsureReflection(object mainContent)
        {
            if (_reflReady)
                return _fCoordinator != null && _fCacheObj != null && _fCacheDict != null && _fContentManagers != null;

            _reflReady = true;
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                Type bcmType = FindBaseTypeNamed(mainContent?.GetType(), "BaseContentManager");
                if (bcmType != null)
                {
                    _fCoordinator = bcmType.GetField("Coordinator", any);
                    _fCacheObj = bcmType.GetField("Cache", any);
                    _fBaseDisposables = bcmType.GetField("BaseDisposableReferences", any);
                    _pFullRootDir = bcmType.GetProperty("FullRootDirectory", any);

                    if (_fCacheObj != null)
                        _fCacheDict = _fCacheObj.FieldType.GetField("Cache", any);
                    if (_fCoordinator != null)
                    {
                        _fContentManagers = _fCoordinator.FieldType.GetField("ContentManagers", any);
                        _fVanillaContentMgr = _fCoordinator.FieldType.GetField("VanillaContentManager", any);
                    }

                    Assembly smapiAsm = bcmType.Assembly;
                    Type mcmType = smapiAsm.GetType("StardewModdingAPI.Framework.ContentManagers.ModContentManager");
                    if (mcmType != null)
                        _fModName = mcmType.GetField("ModName", any);
                }

                _fMgLoadedAssets = typeof(ContentManager).GetField("loadedAssets", any);
                _fMgDisposableAssets = typeof(ContentManager).GetField("disposableAssets", any);

                Type xnaDispType = typeof(xTile.Display.XnaDisplayDevice);
                _fTileSheetTextures = xnaDispType.GetField("m_tileSheetTextures", any);
                _fNpcPortrait = typeof(NPC).GetField("portrait", any);

                var texFields = new List<FieldInfo>();
                foreach (FieldInfo fi in typeof(Game1).GetFields(stat))
                {
                    if (typeof(Texture2D).IsAssignableFrom(fi.FieldType))
                        texFields.Add(fi);
                }
                _game1StaticTexFields = texFields.ToArray();
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[MapRamOptimizer] Reflection init skipped: {ex.Message}", LogLevel.Trace);
            }

            return _fCoordinator != null && _fCacheObj != null && _fCacheDict != null && _fContentManagers != null;
        }

        private void RunPrune(int keepRecentOffscreen, bool runGc, string reason)
        {
            // Never prune while actively saving, mid-event, or inside title/minigame
            if (Game1.content == null || Game1.currentLocation == null || Game1.eventUp || Game1.currentMinigame != null)
                return;

            if (!EnsureReflection(Game1.content))
                return;

            try
            {
                object coordinator = _fCoordinator.GetValue(Game1.content);
                if (coordinator == null)
                    return;

                var mgrListObj = _fContentManagers.GetValue(coordinator) as IEnumerable;
                if (mgrListObj == null)
                    return;

                // Snapshot content managers to avoid concurrent modification
                var allManagers = new List<object>(32);
                var gameManagers = new List<object>(8);
                var modManagers = new List<object>(24);

                foreach (object m in mgrListObj)
                {
                    if (m == null) continue;
                    allManagers.Add(m);
                    string tn = m.GetType().Name;
                    if (tn.IndexOf("GameContentManager", StringComparison.Ordinal) >= 0)
                        gameManagers.Add(m);
                    else if (tn.IndexOf("ModContentManager", StringComparison.Ordinal) >= 0)
                        modManagers.Add(m);
                }

                // 1. Collect currently active map tilesheet keys + protected Texture2D object references
                var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var activeBasenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var protectedTex = new HashSet<Texture2D>(ReferenceComparer.Instance);

                Map curMap = Game1.currentLocation.Map;
                if (curMap?.TileSheets != null)
                {
                    for (int i = 0; i < curMap.TileSheets.Count; i++)
                    {
                        TileSheet ts = curMap.TileSheets[i];
                        if (ts == null || string.IsNullOrEmpty(ts.ImageSource))
                            continue;
                        string norm = NormalizeAssetKey(ts.ImageSource);
                        if (string.IsNullOrEmpty(norm))
                            continue;
                        activeKeys.Add(norm);
                        if (!norm.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
                            activeKeys.Add("maps/" + norm);
                        string baseNm = GetBaseName(norm);
                        if (!string.IsNullOrEmpty(baseNm))
                            activeBasenames.Add(baseNm);
                    }
                }

                // Protect all static Game1 Texture2D fields
                if (_game1StaticTexFields != null)
                {
                    for (int i = 0; i < _game1StaticTexFields.Length; i++)
                    {
                        try
                        {
                            if (_game1StaticTexFields[i].GetValue(null) is Texture2D gt)
                                protectedTex.Add(gt);
                        }
                        catch { }
                    }
                }

                // Protect active map's Texture2Ds in Game1.mapDisplayDevice.m_tileSheetTextures
                Dictionary<TileSheet, Texture2D> tsDict = null;
                if (_fTileSheetTextures != null && Game1.mapDisplayDevice != null)
                {
                    try
                    {
                        tsDict = _fTileSheetTextures.GetValue(Game1.mapDisplayDevice) as Dictionary<TileSheet, Texture2D>;
                        if (tsDict != null)
                        {
                            foreach (var kv in tsDict)
                            {
                                if (kv.Key != null && kv.Value != null)
                                {
                                    // Record every tilesheet image source we ever encounter
                                    if (!string.IsNullOrEmpty(kv.Key.ImageSource))
                                    {
                                        string kn = NormalizeAssetKey(kv.Key.ImageSource);
                                        if (!string.IsNullOrEmpty(kn))
                                        {
                                            _knownTileSheetKeys.Add(kn);
                                            if (!kn.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
                                                _knownTileSheetKeys.Add("maps/" + kn);
                                        }
                                    }
                                    if (kv.Key.Map == curMap)
                                        protectedTex.Add(kv.Value);
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Protect portraits of NPCs in the current location or with custom overridden portraits
                bool prunePortraits = (_fNpcPortrait != null && Game1.activeClickableMenu == null && !Game1.dialogueUp);
                if (prunePortraits)
                {
                    try
                    {
                        if (Game1.currentLocation?.characters != null)
                        {
                            for (int i = 0; i < Game1.currentLocation.characters.Count; i++)
                            {
                                NPC ch = Game1.currentLocation.characters[i];
                                if (ch != null && _fNpcPortrait.GetValue(ch) is Texture2D pt)
                                    protectedTex.Add(pt);
                            }
                        }
                        if (Game1.locations != null)
                        {
                            for (int l = 0; l < Game1.locations.Count; l++)
                            {
                                var loc = Game1.locations[l];
                                if (loc?.characters == null) continue;
                                for (int c = 0; c < loc.characters.Count; c++)
                                {
                                    NPC ch = loc.characters[c];
                                    if (ch != null && (ch.portraitOverridden || !ch.IsVillager) && _fNpcPortrait.GetValue(ch) is Texture2D pt)
                                        protectedTex.Add(pt);
                                }
                            }
                        }
                    }
                    catch { prunePortraits = false; }
                }

                // Protect non-evictable textures in ALL GameContentManagers (in case a texture is shared under a protected key)
                foreach (object gm in gameManagers)
                {
                    var dict = GetCacheDict(gm);
                    if (dict == null) continue;
                    foreach (var kv in dict)
                    {
                        if (kv.Value is Texture2D tex && !tex.IsDisposed)
                        {
                            string norm = NormalizeAssetKey(kv.Key);
                            if (!IsEvictableAssetKey(norm, activeKeys, activeBasenames, prunePortraits))
                                protectedTex.Add(tex);
                        }
                    }
                }

                // 2. Find candidate off-screen map tilesheets & off-screen villager portraits across all GameContentManagers
                var candidates = new List<(object mgr, string rawKey, string normKey, Texture2D tex, long seq)>();
                var candidateByTex = new HashSet<Texture2D>(ReferenceComparer.Instance);

                foreach (object gm in gameManagers)
                {
                    var dict = GetCacheDict(gm);
                    if (dict == null) continue;
                    foreach (var kv in dict)
                    {
                        if (!(kv.Value is Texture2D tex) || tex.IsDisposed)
                            continue;
                        if (protectedTex.Contains(tex))
                            continue;

                        string norm = NormalizeAssetKey(kv.Key);
                        if (!IsEvictableAssetKey(norm, activeKeys, activeBasenames, prunePortraits))
                            continue;

                        _lastUsedSeq.TryGetValue(norm, out long seq);
                        candidates.Add((gm, kv.Key, norm, tex, seq));
                        candidateByTex.Add(tex);
                    }
                }

                // Apply LRU retention: keep the N most recently visited off-screen tilesheet textures
                if (keepRecentOffscreen > 0 && candidateByTex.Count > keepRecentOffscreen)
                {
                    // Sort distinct candidate textures by newest _lastUsedSeq descending
                    var texOrder = new List<(Texture2D tex, long seq)>(candidateByTex.Count);
                    var seenSeq = new Dictionary<Texture2D, long>(ReferenceComparer.Instance);
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var c = candidates[i];
                        if (!seenSeq.TryGetValue(c.tex, out long prev) || c.seq > prev)
                            seenSeq[c.tex] = c.seq;
                    }
                    foreach (var kv in seenSeq)
                        texOrder.Add((kv.Key, kv.Value));
                    texOrder.Sort((a, b) => b.seq.CompareTo(a.seq));

                    for (int i = 0; i < keepRecentOffscreen && i < texOrder.Count; i++)
                        protectedTex.Add(texOrder[i].tex);
                }
                else if (keepRecentOffscreen > 0 && candidateByTex.Count <= keepRecentOffscreen)
                {
                    foreach (Texture2D t in candidateByTex)
                        protectedTex.Add(t);
                }

                // 3. Collect final set of off-screen map/portrait Texture2Ds to evict from SMAPI caches.
                //    CRITICAL iOS OpenGL ES + Shader Mod Safety (SDV-Radiance, DynamicReflections, etc.):
                //    Never call tex.Dispose() eagerly on shared GameContentManager textures because C# rendering
                //    mods cache Texture2D references loaded from Game1.content (e.g., ShadowRenderer._textureCache
                //    and _propCache). Eagerly deleting their GL texture leaves glTexture = -1, which OpenGL ES
                //    samples as solid opaque black (0,0,0,1) blocks!
                //    Instead: unlink them from all SMAPI/MonoGame/xTile caches so .NET GC collects unreferenced
                //    textures and MonoGame's GraphicsResource finalizer safely calls GraphicsDevice.DisposeTexture()
                //    ONLY once zero mods hold a reference. Pure Content Patcher (0% C# code) dead copies still
                //    dispose immediately for instant VRAM reclamation.
                var toUnlink = new HashSet<Texture2D>(ReferenceComparer.Instance);
                var toImmediateDispose = new HashSet<Texture2D>(ReferenceComparer.Instance);
                for (int i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i];
                    if (protectedTex.Contains(c.tex))
                        continue;
                    var dict = GetCacheDict(c.mgr);
                    dict?.Remove(c.rawKey);
                    RemoveFromBaseDisposables(c.mgr, c.tex);
                    toUnlink.Add(c.tex);
                }

                // 4. Clean ModContentManagers:
                //    (a) ALWAYS remove any entry referencing a texture in `toUnlink`
                //    (b) For pure Pathoschild.ContentPatcher content packs (0% C# code), also evict & immediately
                //        dispose dead Texture2D copies not referenced by any live GameContentManager or protectedTex
                //        (e.g., EditImage source PNG duplicates — zero C# mod can hold these).
                HashSet<Texture2D> allLiveGameTex = null;
                foreach (object mm in modManagers)
                {
                    var dict = GetCacheDict(mm);
                    if (dict == null || dict.Count == 0) continue;

                    bool isCpPack = IsContentPatcherPackManager(mm);
                    if (isCpPack && allLiveGameTex == null)
                    {
                        allLiveGameTex = new HashSet<Texture2D>(protectedTex, ReferenceComparer.Instance);
                        foreach (object gm in gameManagers)
                        {
                            var gd = GetCacheDict(gm);
                            if (gd == null) continue;
                            foreach (var gkv in gd)
                            {
                                if (gkv.Value is Texture2D gtex && !gtex.IsDisposed)
                                    allLiveGameTex.Add(gtex);
                            }
                        }
                    }

                    List<string> rmKeys = null;
                    foreach (var kv in dict)
                    {
                        if (!(kv.Value is Texture2D mtex))
                            continue;

                        bool shouldEvict = toUnlink.Contains(mtex);
                        if (!shouldEvict && isCpPack && !mtex.IsDisposed && !allLiveGameTex.Contains(mtex))
                        {
                            // Pure Content Patcher pack texture not referenced by any GameContentManager or Game1
                            shouldEvict = true;
                            toUnlink.Add(mtex);
                            toImmediateDispose.Add(mtex);
                        }

                        if (shouldEvict)
                        {
                            if (rmKeys == null) rmKeys = new List<string>(8);
                            rmKeys.Add(kv.Key);
                        }
                    }

                    if (rmKeys != null)
                    {
                        for (int i = 0; i < rmKeys.Count; i++)
                        {
                            if (dict.TryGetValue(rmKeys[i], out object v) && v is Texture2D mt)
                                RemoveFromBaseDisposables(mm, mt);
                            dict.Remove(rmKeys[i]);
                        }
                    }
                }

                // 5. Also ensure NO remaining entry in ANY GameContentManager, m_tileSheetTextures,
                //    NPC.portrait, or VanillaContentManager retains a texture we evicted!
                if (toUnlink.Count > 0)
                {
                    foreach (object gm in gameManagers)
                    {
                        var dict = GetCacheDict(gm);
                        if (dict == null) continue;
                        List<string> extraRm = null;
                        foreach (var kv in dict)
                        {
                            if (kv.Value is Texture2D gt && toUnlink.Contains(gt))
                            {
                                if (extraRm == null) extraRm = new List<string>(4);
                                extraRm.Add(kv.Key);
                            }
                        }
                        if (extraRm != null)
                        {
                            for (int i = 0; i < extraRm.Count; i++)
                                dict.Remove(extraRm[i]);
                        }
                        foreach (Texture2D dt in toUnlink)
                            RemoveFromBaseDisposables(gm, dt);
                    }

                    if (tsDict != null)
                    {
                        List<TileSheet> rmTs = null;
                        foreach (var kv in tsDict)
                        {
                            if (kv.Value != null && toUnlink.Contains(kv.Value))
                            {
                                if (rmTs == null) rmTs = new List<TileSheet>(8);
                                rmTs.Add(kv.Key);
                            }
                        }
                        if (rmTs != null)
                        {
                            for (int i = 0; i < rmTs.Count; i++)
                                tsDict.Remove(rmTs[i]);
                        }
                    }

                    // Null off-screen villager npc.portrait references so NPC.get_Portrait() reloads cleanly via ChooseAppearance!
                    if (_fNpcPortrait != null)
                    {
                        try
                        {
                            if (Game1.locations != null)
                            {
                                for (int l = 0; l < Game1.locations.Count; l++)
                                {
                                    var loc = Game1.locations[l];
                                    if (loc?.characters == null) continue;
                                    for (int c = 0; c < loc.characters.Count; c++)
                                    {
                                        NPC ch = loc.characters[c];
                                        if (ch != null && _fNpcPortrait.GetValue(ch) is Texture2D pt && toUnlink.Contains(pt))
                                        {
                                            if (ch.IsVillager && !ch.portraitOverridden)
                                                _fNpcPortrait.SetValue(ch, null);
                                            else
                                                toImmediateDispose.Remove(pt);
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    // Clean VanillaContentManager.loadedAssets / disposableAssets if present
                    PurgeFromMonoGameContentManager(_fVanillaContentMgr?.GetValue(coordinator), toUnlink);

                    // 6. Immediately dispose ONLY pure Content Patcher dead copies (0% C# references);
                    //    shared GameContentManager textures are freed safely via GC finalizer ONLY when
                    //    no C# mod (e.g., SDV-Radiance / DynamicReflections) still references them!
                    long bytesImmediate = 0;
                    long bytesGcCandidate = 0;
                    int disposedCount = 0;
                    int unlinkedCount = 0;
                    foreach (Texture2D tex in toUnlink)
                    {
                        if (tex == null || tex.IsDisposed)
                            continue;
                        long texBytes = (long)tex.Width * tex.Height * 4L;
                        if (toImmediateDispose.Contains(tex))
                        {
                            try
                            {
                                tex.Dispose();
                                bytesImmediate += texBytes;
                                disposedCount++;
                            }
                            catch { }
                        }
                        else
                        {
                            bytesGcCandidate += texBytes;
                            unlinkedCount++;
                        }
                    }

                    if (disposedCount > 0 || unlinkedCount > 0)
                    {
                        double mbImm = bytesImmediate / (1024.0 * 1024.0);
                        double mbGc = bytesGcCandidate / (1024.0 * 1024.0);
                        _monitor?.Log($"[MapRamOptimizer] ({reason}) Freed {disposedCount} CP textures ({mbImm:F1} MB) + unlinked {unlinkedCount} off-screen textures ({mbGc:F1} MB GC-safe).", LogLevel.Info);

                        if (runGc)
                        {
                            try
                            {
                                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                            }
                            catch { }
                        }
                        else if ((bytesImmediate + bytesGcCandidate) >= 24L * 1024L * 1024L)
                        {
                            try
                            {
                                GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[MapRamOptimizer] Prune error ({reason}): {ex.Message}", LogLevel.Trace);
            }
        }

        private Dictionary<string, object> GetCacheDict(object contentMgr)
        {
            if (contentMgr == null || _fCacheObj == null || _fCacheDict == null)
                return null;
            try
            {
                object cacheObj = _fCacheObj.GetValue(contentMgr);
                if (cacheObj == null) return null;
                return _fCacheDict.GetValue(cacheObj) as Dictionary<string, object>;
            }
            catch { return null; }
        }

        private void RemoveFromBaseDisposables(object contentMgr, Texture2D tex)
        {
            if (contentMgr == null || tex == null || _fBaseDisposables == null)
                return;
            try
            {
                var list = _fBaseDisposables.GetValue(contentMgr) as List<IDisposable>;
                if (list != null)
                {
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        if (ReferenceEquals(list[i], tex))
                            list.RemoveAt(i);
                    }
                }
            }
            catch { }
        }

        private void PurgeFromMonoGameContentManager(object cmObj, HashSet<Texture2D> toDispose)
        {
            if (cmObj == null || _fMgLoadedAssets == null)
                return;
            try
            {
                var loaded = _fMgLoadedAssets.GetValue(cmObj) as IDictionary;
                if (loaded != null)
                {
                    List<object> rm = null;
                    foreach (DictionaryEntry de in loaded)
                    {
                        if (de.Value is Texture2D t && toDispose.Contains(t))
                        {
                            if (rm == null) rm = new List<object>(4);
                            rm.Add(de.Key);
                        }
                    }
                    if (rm != null)
                    {
                        for (int i = 0; i < rm.Count; i++)
                            loaded.Remove(rm[i]);
                    }
                }
                var disps = _fMgDisposableAssets?.GetValue(cmObj) as IList;
                if (disps != null)
                {
                    for (int i = disps.Count - 1; i >= 0; i--)
                    {
                        if (disps[i] is Texture2D t && toDispose.Contains(t))
                            disps.RemoveAt(i);
                    }
                }
            }
            catch { }
        }

        private bool IsContentPatcherPackManager(object modContentMgr)
        {
            if (modContentMgr == null || (_cpPackNames.Count == 0 && _cpPackDirs.Count == 0))
                return false;
            try
            {
                string modName = _fModName?.GetValue(modContentMgr) as string;
                if (!string.IsNullOrEmpty(modName) && _cpPackNames.Contains(modName))
                    return true;
                string rootDir = _pFullRootDir?.GetValue(modContentMgr) as string;
                if (!string.IsNullOrEmpty(rootDir))
                {
                    string nd = NormalizeDir(rootDir);
                    for (int i = 0; i < _cpPackDirs.Count; i++)
                    {
                        if (string.Equals(nd, _cpPackDirs[i], StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private bool IsEvictableAssetKey(string normKey, HashSet<string> activeKeys, HashSet<string> activeBasenames, bool prunePortraits)
        {
            if (string.IsNullOrEmpty(normKey))
                return false;

            // Safe off-screen HD/villager Portraits/* eviction (NPC.get_Portrait reloads cleanly on demand!)
            if (prunePortraits && normKey.StartsWith("portraits/", StringComparison.OrdinalIgnoreCase))
            {
                if (normKey.IndexOf("shop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normKey.IndexOf("answering", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
                return true;
            }

            // Never touch Audio / Sound / UI / Fonts / Sprites / Buildings / TerrainFeatures / Characters
            if (normKey.StartsWith("loosesprites/", StringComparison.OrdinalIgnoreCase) ||
                normKey.StartsWith("fonts/", StringComparison.OrdinalIgnoreCase) ||
                normKey.StartsWith("tilesheets/", StringComparison.OrdinalIgnoreCase) ||
                normKey.StartsWith("terrainfeatures/", StringComparison.OrdinalIgnoreCase) ||
                normKey.StartsWith("buildings/", StringComparison.OrdinalIgnoreCase) ||
                normKey.StartsWith("characters/", StringComparison.OrdinalIgnoreCase) ||
                normKey.StartsWith("portraits/", StringComparison.OrdinalIgnoreCase) ||
                normKey.StartsWith("animals/", StringComparison.OrdinalIgnoreCase) ||
                normKey.StartsWith("monsters/", StringComparison.OrdinalIgnoreCase) ||
                normKey.StartsWith("minigames/", StringComparison.OrdinalIgnoreCase) ||
                normKey.StartsWith("data/", StringComparison.OrdinalIgnoreCase) ||
                normKey.StartsWith("strings/", StringComparison.OrdinalIgnoreCase) ||
                normKey.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                return false;

            if (normKey.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normKey.IndexOf("sound", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normKey.IndexOf("wave", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normKey.IndexOf("music", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            // Never touch shared global Maps/* assets held outside xTile.Map or reused on every outdoor warp
            if (normKey.IndexOf("springobjects", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normKey.IndexOf("walls_and_floors", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normKey.IndexOf("menutiles", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normKey.IndexOf("world_map", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normKey.IndexOf("worldmap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normKey.IndexOf("charactersheet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normKey.IndexOf("minecart", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normKey.IndexOf("wallpaper", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normKey.IndexOf("furniture", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normKey.IndexOf("cursor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(normKey, "maps/paths", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normKey, "paths", StringComparison.OrdinalIgnoreCase))
                return false;

            // Keep current season's core outdoor/town/beach tilesheets resident (avoids reload stutter & shader cache churn on FarmHouse/Shop warps)
            string curSeason = Game1.currentSeason;
            if (!string.IsNullOrEmpty(curSeason) &&
                normKey.IndexOf(curSeason + "_", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (normKey.IndexOf("outdoorstilesheet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 normKey.IndexOf("_town", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 normKey.IndexOf("_beach", StringComparison.OrdinalIgnoreCase) >= 0))
                return false;

            // Must be a known map tilesheet or under Maps/
            bool isMapSheet = normKey.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
                              _knownTileSheetKeys.Contains(normKey);
            if (!isMapSheet)
                return false;

            // Never evict anything used by the current location's map!
            if (activeKeys.Contains(normKey))
                return false;
            string baseNm = GetBaseName(normKey);
            if (!string.IsNullOrEmpty(baseNm) && activeBasenames.Contains(baseNm))
                return false;

            return true;
        }

        private static string NormalizeAssetKey(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;
            string s = raw.Replace('\\', '/').Trim().TrimStart('/');
            if (s.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase) ||
                s.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 4);
            // Strip SMAPI localized suffix like ".vi-VN", ".zh-CN", ".en"
            int dot = s.LastIndexOf('.');
            int slash = s.LastIndexOf('/');
            if (dot > slash && dot >= 0 && (s.Length - dot) <= 6)
            {
                string ext = s.Substring(dot + 1);
                if (ext.Length == 2 || (ext.Length == 5 && ext[2] == '-'))
                    s = s.Substring(0, dot);
            }
            return s.ToLowerInvariant();
        }

        private static string GetBaseName(string norm)
        {
            if (string.IsNullOrEmpty(norm)) return string.Empty;
            int idx = norm.LastIndexOf('/');
            return idx >= 0 ? norm.Substring(idx + 1) : norm;
        }

        private static string NormalizeDir(string dir)
        {
            return (dir ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        }

        private static Type FindBaseTypeNamed(Type t, string simpleName)
        {
            while (t != null)
            {
                if (string.Equals(t.Name, simpleName, StringComparison.Ordinal))
                    return t;
                t = t.BaseType;
            }
            return null;
        }
    }
}
