$ErrorActionPreference = "Stop"
Add-Type -Path "$env:TEMP\sdv-viewport\Mono.Cecil.dll"

$srcTouch = "$env:TEMP\sdv-clean-ui\menubar-touch.dll"
$srcHost  = "$env:TEMP\sdv-clean-ui\menubar-host.dll"
$srcMG    = "$env:TEMP\sdv-clean-ui\MonoGame.Framework.dll"
$outTouch = "$env:TEMP\sdv-clean-ui\v7-touch.dll"
$outHost  = "$env:TEMP\sdv-clean-ui\v7-host.dll"
$outMG    = "$env:TEMP\sdv-clean-ui\v7-monogame.dll"

foreach ($f in @($srcTouch, $srcHost, $srcMG)) {
    if (!(Test-Path $f)) { throw "Missing input DLL: $f" }
}

function Verify-Assembly($path) {
    $a = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
    $mCount = 0
    foreach ($t in $a.MainModule.Types) {
        foreach ($m in $t.Methods) {
            if (-not $m.HasBody) { continue }
            $mCount++
            $set = New-Object 'System.Collections.Generic.HashSet[Mono.Cecil.Cil.Instruction]'
            foreach ($ins in $m.Body.Instructions) { [void]$set.Add($ins) }
            foreach ($ins in $m.Body.Instructions) {
                if ($ins.Operand -is [Mono.Cecil.Cil.Instruction]) {
                    if (-not $set.Contains($ins.Operand)) {
                        throw "Broken branch target in $($t.FullName).$($m.Name)"
                    }
                }
            }
            foreach ($eh in $m.Body.ExceptionHandlers) {
                foreach ($pt in @($eh.TryStart, $eh.TryEnd, $eh.HandlerStart, $eh.HandlerEnd)) {
                    if ($null -ne $pt -and -not $set.Contains($pt)) {
                        throw "Broken EH boundary in $($t.FullName).$($m.Name)"
                    }
                }
            }
        }
    }
    $a.Dispose()
    Write-Host "Verified IL & EH integrity OK: $path ($mCount methods)"
}

function Retarget-Branches($method, $oldTarget, $newTarget) {
    foreach ($ins in $method.Body.Instructions) {
        if ($ins.Operand -eq $oldTarget) {
            $ins.Operand = $newTarget
        } elseif ($ins.Operand -is [Mono.Cecil.Cil.Instruction[]]) {
            $arr = $ins.Operand
            for ($k = 0; $k -lt $arr.Length; $k++) {
                if ($arr[$k] -eq $oldTarget) { $arr[$k] = $newTarget }
            }
        }
    }
    foreach ($eh in $method.Body.ExceptionHandlers) {
        if ($eh.TryStart -eq $oldTarget) { $eh.TryStart = $newTarget }
        if ($eh.TryEnd -eq $oldTarget) { $eh.TryEnd = $newTarget }
        if ($eh.HandlerStart -eq $oldTarget) { $eh.HandlerStart = $newTarget }
        if ($eh.HandlerEnd -eq $oldTarget) { $eh.HandlerEnd = $newTarget }
    }
}

function Expand-ShortBranches($method) {
    $map = @{
        "br.s"        = [Mono.Cecil.Cil.OpCodes]::Br
        "brfalse.s"   = [Mono.Cecil.Cil.OpCodes]::Brfalse
        "brtrue.s"    = [Mono.Cecil.Cil.OpCodes]::Brtrue
        "beq.s"       = [Mono.Cecil.Cil.OpCodes]::Beq
        "bge.s"       = [Mono.Cecil.Cil.OpCodes]::Bge
        "bgt.s"       = [Mono.Cecil.Cil.OpCodes]::Bgt
        "ble.s"       = [Mono.Cecil.Cil.OpCodes]::Ble
        "blt.s"       = [Mono.Cecil.Cil.OpCodes]::Blt
        "bne.un.s"    = [Mono.Cecil.Cil.OpCodes]::Bne_Un
        "bge.un.s"    = [Mono.Cecil.Cil.OpCodes]::Bge_Un
        "bgt.un.s"    = [Mono.Cecil.Cil.OpCodes]::Bgt_Un
        "ble.un.s"    = [Mono.Cecil.Cil.OpCodes]::Ble_Un
        "blt.un.s"    = [Mono.Cecil.Cil.OpCodes]::Blt_Un
        "leave.s"     = [Mono.Cecil.Cil.OpCodes]::Leave
    }
    foreach ($ins in $method.Body.Instructions) {
        if ($map.ContainsKey($ins.OpCode.Name)) {
            $ins.OpCode = $map[$ins.OpCode.Name]
        }
    }
}

function Find-Ref($mod, $pattern) {
    foreach ($t in $mod.Types) {
        foreach ($m in $t.Methods) {
            if (-not $m.HasBody) { continue }
            foreach ($ins in $m.Body.Instructions) {
                if ($ins.Operand -is [Mono.Cecil.MethodReference] -and $ins.Operand.FullName -eq $pattern) {
                    return $ins.Operand
                }
            }
        }
    }
    return $null
}

# ==============================================================================
# PART 1: PATCH SDViOSTouchControls.dll (v7-touch.dll) — LOCKED 2.0x + MENU OPT
# ==============================================================================
Write-Host "Patching TouchControls -> $outTouch ..."
$tAsm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($srcTouch)
$tMod = $tAsm.MainModule

$padType = $tMod.Types | Where-Object { $_.Name -eq "TouchVirtualPad" }
$mwType  = $tMod.Types | Where-Object { $_.Name -eq "MemoryWatchdog" }
$meType  = $tMod.Types | Where-Object { $_.Name -eq "ModEntry" }
$syncM   = $padType.Methods | Where-Object { $_.Name -eq "SyncMenuBarConfig" }
$fcM     = $mwType.Methods | Where-Object { $_.Name -eq "ForceCollect" -and $_.Parameters.Count -eq 3 }
if (!$padType -or !$mwType -or !$meType -or !$syncM -or !$fcM) { throw "Missing TouchControls target types/methods" }

$mGetFolderPath = Find-Ref $tMod "System.String System.Environment::GetFolderPath(System.Environment/SpecialFolder)"
$mPathCombine   = Find-Ref $tMod "System.String System.IO.Path::Combine(System.String,System.String)"
$mFileExists    = Find-Ref $tMod "System.Boolean System.IO.File::Exists(System.String)"
$mFileDelete    = Find-Ref $tMod "System.Void System.IO.File::Delete(System.String)"
if (!$mGetFolderPath -or !$mPathCombine -or !$mFileExists) { throw "Missing IO/Env refs in TouchControls" }
if (!$mFileDelete) {
    $mFileDelete = New-Object Mono.Cecil.MethodReference("Delete", $tMod.TypeSystem.Void, $mFileExists.DeclaringType)
    $mFileDelete.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition($tMod.TypeSystem.String)))
}

# 1.1 TouchVirtualPad.CheckManualGcFlag() — Prunes stale map tilesheets + runs Compacting Gen2 GC!
$lddType = $tMod.Types | Where-Object { $_.Name -eq "LazyIosDisplayDevice" }
$mLddInst = $lddType.Methods | Where-Object { $_.Name -eq "get_Instance" }
$mLddPrune = $lddType.Methods | Where-Object { $_.Name -eq "PruneTileSheets" }
$mGetCurLoc = Find-Ref $tMod "StardewValley.GameLocation StardewValley.Game1::get_currentLocation()"
$fLocMap = $null
foreach ($t in $tMod.Types) {
    foreach ($m in $t.Methods) {
        if (-not $m.HasBody) { continue }
        foreach ($ins in $m.Body.Instructions) {
            if ($ins.Operand -is [Mono.Cecil.FieldReference] -and $ins.Operand.FullName -eq "xTile.Map StardewValley.GameLocation::map") {
                $fLocMap = $ins.Operand; break
            }
        }
        if ($fLocMap) { break }
    }
    if ($fLocMap) { break }
}

$gcMethod = New-Object Mono.Cecil.MethodDefinition(
    "CheckManualGcFlag",
    [Mono.Cecil.MethodAttributes]"Private, Static",
    $tMod.TypeSystem.Void
)
$gcMethod.Body.InitLocals = $true
$vPath = New-Object Mono.Cecil.Cil.VariableDefinition($tMod.TypeSystem.String)
$vDev  = New-Object Mono.Cecil.Cil.VariableDefinition($lddType)
$vLoc  = New-Object Mono.Cecil.Cil.VariableDefinition($mGetCurLoc.ReturnType)
$gcMethod.Body.Variables.Add($vPath)
$gcMethod.Body.Variables.Add($vDev)
$gcMethod.Body.Variables.Add($vLoc)
$il = $gcMethod.Body.GetILProcessor()

$lTryStart  = $il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_5)
$lExit      = $il.Create([Mono.Cecil.Cil.OpCodes]::Ret)
$lLeaveOk   = $il.Create([Mono.Cecil.Cil.OpCodes]::Leave, $lExit)
$lCatch     = $il.Create([Mono.Cecil.Cil.OpCodes]::Pop)
$lCallFc    = $il.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, "ManualModGarden")

$il.Append($lTryStart)
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Call, $mGetFolderPath))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, "sdv_gc_now.flag"))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Call, $mPathCombine))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vPath))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vPath))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Call, $mFileExists))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lLeaveOk))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vPath))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Call, $mFileDelete))
# Prune unused tilesheets from other maps before Gen2 GC so finalizers immediately GL.DeleteTextures!
if ($mLddInst -and $mLddPrune -and $mGetCurLoc -and $fLocMap) {
    $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Call, $mLddInst))
    $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vDev))
    $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vDev))
    $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lCallFc))
    $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Call, $mGetCurLoc))
    $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vLoc))
    $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vLoc))
    $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lCallFc))
    $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vDev))
    $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vLoc))
    $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $fLocMap))
    $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mLddPrune))
}
$il.Append($lCallFc)
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_0))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_1))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Call, $fcM))
$il.Append($lLeaveOk)
$il.Append($lCatch)
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Leave, $lExit))
$il.Append($lExit)

$eh = New-Object Mono.Cecil.Cil.ExceptionHandler([Mono.Cecil.Cil.ExceptionHandlerType]::Catch)
$eh.CatchType = $tMod.TypeSystem.Object
$eh.TryStart = $lTryStart
$eh.TryEnd = $lCatch
$eh.HandlerStart = $lCatch
$eh.HandlerEnd = $lExit
$gcMethod.Body.ExceptionHandlers.Add($eh)
$padType.Methods.Add($gcMethod)

# Hook SyncMenuBarConfig: 8 frames -> 45 frames (~0.75s) + call CheckManualGcFlag inside poll gate
Expand-ShortBranches $syncM
$sp = $syncM.Body.GetILProcessor()
$sIns = $syncM.Body.Instructions
$bumpedPoll = 0
$hookedGcInGate = $false
for ($i = 0; $i -lt $sIns.Count; $i++) {
    if ($sIns[$i].OpCode.Name -eq "ldc.i4.8" -and $i -ge 2 -and "$($sIns[$i-1].Operand)" -like "*_mbPollTick*") {
        $sIns[$i].OpCode = [Mono.Cecil.Cil.OpCodes]::Ldc_I4
        $sIns[$i].Operand = [int]45
        $bumpedPoll++
    }
    if (-not $hookedGcInGate -and $sIns[$i].OpCode.Name -eq "ldc.i4.5" -and ($i + 1) -lt $sIns.Count -and "$($sIns[$i+1].Operand)" -like "*GetFolderPath*") {
        $targetGate = $sIns[$i]
        $callGc = $sp.Create([Mono.Cecil.Cil.OpCodes]::Call, $gcMethod)
        $sp.InsertBefore($targetGate, $callGc)
        Retarget-Branches $syncM $targetGate $callGc
        $hookedGcInGate = $true
        $i++
    }
}
if (-not $hookedGcInGate) { throw "Could not hook CheckManualGcFlag inside SyncMenuBarConfig poll gate" }

# 1.2 Raise MemoryWatchdog.Loop soft-GC threshold 350 MB -> 600 MB (keeps RAM lower before Jetsam!)
$mwLoop = $mwType.Methods | Where-Object { $_.Name -eq "Loop" }
$bumped = 0
foreach ($ins in $mwLoop.Body.Instructions) {
    if ($ins.OpCode.Name -eq "ldc.i4" -and [int]$ins.Operand -eq 367001600) {
        $ins.Operand = [int]629145600
        $bumped++
    }
}

# 1.2b Disable EnableWarpTilesheetPrewarm by default (avoids eagerly uploading unused SVE tilesheets into VRAM!)
$tosType = $tMod.Types | Where-Object { $_.Name -eq "TouchOverlaySettings" }
$tosCtor = $tosType.Methods | Where-Object { $_.Name -eq ".ctor" }
$prewarmOff = 0
for ($i = 1; $i -lt $tosCtor.Body.Instructions.Count; $i++) {
    $ins = $tosCtor.Body.Instructions[$i]
    if ($ins.OpCode.Name -eq "stfld" -and "$($ins.Operand)" -like "*EnableWarpTilesheetPrewarm*") {
        $prev = $tosCtor.Body.Instructions[$i - 1]
        if ($prev.OpCode.Name -eq "ldc.i4.1") {
            $prev.OpCode = [Mono.Cecil.Cil.OpCodes]::Ldc_I4_0
            $prewarmOff++
        }
    }
}
Write-Host "  RAM Saver (TouchControls): PruneTileSheets on ManualGC + Watchdog 600MB ($bumped) + WarpTilesheetPrewarm=false ($prewarmOff)"

# 1.2c Fix DynamicReflections water/puddle/sky player reflection: make LazyIosDisplayDevice.DrawTile
# delegate to base.DrawTile (XnaDisplayDevice.DrawTile) after TryGetOrLoadTexture so Harmony patches on
# XnaDisplayDevice.DrawTile (like DynamicReflections's DisplayDevicePatch.DrawTilePrefix) fire!
$mLddDraw = $lddType.Methods | Where-Object { $_.Name -eq "DrawTile" }
$mTryTex  = $lddType.Methods | Where-Object { $_.Name -eq "TryGetOrLoadTexture" }
if ($mLddDraw -and $mTryTex) {
    $mTileGetTs = $null
    $mTexIsDisp = $null
    foreach ($m in $lddType.Methods) {
        if (-not $m.HasBody) { continue }
        foreach ($ins in $m.Body.Instructions) {
            if ($ins.Operand -is [Mono.Cecil.MethodReference]) {
                if ($ins.Operand.FullName -like "*Tile::get_TileSheet*") { $mTileGetTs = $ins.Operand }
                elseif ($ins.Operand.FullName -like "*GraphicsResource::get_IsDisposed*") { $mTexIsDisp = $ins.Operand }
            }
        }
    }
    $mBaseDraw = New-Object Mono.Cecil.MethodReference("DrawTile", $tMod.TypeSystem.Void, $lddType.BaseType)
    $mBaseDraw.HasThis = $true
    foreach ($p in $mLddDraw.Parameters) {
        $mBaseDraw.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition($p.Name, $p.Attributes, $p.ParameterType)))
    }
    $mLddDraw.Body.ExceptionHandlers.Clear()
    $mLddDraw.Body.Instructions.Clear()
    $mLddDraw.Body.Variables.Clear()
    $vTs  = New-Object Mono.Cecil.Cil.VariableDefinition($mTryTex.Parameters[0].ParameterType)
    $vTex = New-Object Mono.Cecil.Cil.VariableDefinition($mTryTex.ReturnType)
    $mLddDraw.Body.Variables.Add($vTs)
    $mLddDraw.Body.Variables.Add($vTex)
    $mLddDraw.Body.InitLocals = $true

    $dtIL = $mLddDraw.Body.GetILProcessor()
    $lRetDt = $dtIL.Create([Mono.Cecil.Cil.OpCodes]::Ret)
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_1))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lRetDt))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_1))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mTileGetTs))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vTs))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vTs))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lRetDt))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vTs))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Call, $mTryTex))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vTex))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vTex))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lRetDt))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vTex))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mTexIsDisp))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Brtrue, $lRetDt))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_1))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_2))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_3))
    $dtIL.Append($dtIL.Create([Mono.Cecil.Cil.OpCodes]::Call, $mBaseDraw))
    $dtIL.Append($lRetDt)
    Write-Host "  DynamicReflections Fix (TouchControls): LazyIosDisplayDevice.DrawTile now delegates to base XnaDisplayDevice.DrawTile!"
}

# 1.3 ModEntry.ApplyPerfResolutionScale(ModEntry self) — LOCKED TO 2.0x ON 3x DEVICES!
# If h <= 1400 && (w % 3 == 0) && (h % 3 == 0): w = (w / 3) * 2; h = (h / 3) * 2;
$dnrM   = $meType.Methods | Where-Object { $_.Name -eq "DetermineNativeResolution" }
$fNatW  = $meType.Fields  | Where-Object { $_.Name -eq "_nativeWidth" }
$fNatH  = $meType.Fields  | Where-Object { $_.Name -eq "_nativeHeight" }
$fSNatW = $meType.Fields  | Where-Object { $_.Name -eq "_staticNativeWidth" }
$fSNatH = $meType.Fields  | Where-Object { $_.Name -eq "_staticNativeHeight" }

$mApplyRes = New-Object Mono.Cecil.MethodDefinition(
    "ApplyPerfResolutionScale",
    [Mono.Cecil.MethodAttributes]"Private, Static",
    $tMod.TypeSystem.Void
)
$mApplyRes.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("self", [Mono.Cecil.ParameterAttributes]::None, $meType)))
$mApplyRes.Body.InitLocals = $true
$vWT = New-Object Mono.Cecil.Cil.VariableDefinition($tMod.TypeSystem.Int32)
$vHT = New-Object Mono.Cecil.Cil.VariableDefinition($tMod.TypeSystem.Int32)
$mApplyRes.Body.Variables.Add($vWT)
$mApplyRes.Body.Variables.Add($vHT)

$ilR = $mApplyRes.Body.GetILProcessor()
$lRetR = $ilR.Create([Mono.Cecil.Cil.OpCodes]::Ret)
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fSNatW))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vWT))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fSNatH))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vHT))

# v7: Keep 100% native device resolution (3x on 3x devices, 2x on 2x devices) to avoid aspect/scale glitches across devices

$ilR.Append($lRetR)
$meType.Methods.Add($mApplyRes)

Expand-ShortBranches $dnrM
$dp = $dnrM.Body.GetILProcessor()
$dIns = $dnrM.Body.Instructions
$hookedDnr = $false
for ($i = 0; $i -lt $dIns.Count; $i++) {
    if ($dIns[$i].OpCode.Name -eq "stsfld" -and $dIns[$i].Operand -eq $fSNatH) {
        $afterSts = $dIns[$i + 1]
        $dp.InsertBefore($afterSts, $dp.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
        $dp.InsertBefore($afterSts, $dp.Create([Mono.Cecil.Cil.OpCodes]::Call, $mApplyRes))
        $hookedDnr = $true
        break
    }
}
if (-not $hookedDnr) { throw "Could not hook ModEntry.DetermineNativeResolution" }

# 1.4 Harmony Prefix on iOSGameWindow.get_ClientBounds
$tRect     = $tMod.GetTypeReferences() | Where-Object { $_.FullName -eq "Microsoft.Xna.Framework.Rectangle" } | Select-Object -First 1
$tGameWin  = $tMod.GetTypeReferences() | Where-Object { $_.FullName -eq "Microsoft.Xna.Framework.GameWindow" } | Select-Object -First 1
$tHarmony  = $tMod.GetTypeReferences() | Where-Object { $_.FullName -eq "HarmonyLib.Harmony" } | Select-Object -First 1
$tType     = $tMod.GetTypeReferences() | Where-Object { $_.FullName -eq "System.Type" } | Select-Object -First 1
$tPropInfo = $tMod.GetTypeReferences() | Where-Object { $_.FullName -eq "System.Reflection.PropertyInfo" } | Select-Object -First 1
$tMethInfo = $tMod.GetTypeReferences() | Where-Object { $_.FullName -eq "System.Reflection.MethodInfo" } | Select-Object -First 1
$mRectCtor = Find-Ref $tMod "System.Void Microsoft.Xna.Framework.Rectangle::.ctor(System.Int32,System.Int32,System.Int32,System.Int32)"
$mGetTypeH = Find-Ref $tMod "System.Type System.Type::GetTypeFromHandle(System.RuntimeTypeHandle)"
$mGetAsm   = Find-Ref $tMod "System.Reflection.Assembly System.Type::get_Assembly()"
$mAsmGetT  = Find-Ref $tMod "System.Type System.Reflection.Assembly::GetType(System.String)"
$mGetProp  = Find-Ref $tMod "System.Reflection.PropertyInfo System.Type::GetProperty(System.String,System.Reflection.BindingFlags)"
$mGetGetM  = Find-Ref $tMod "System.Reflection.MethodInfo System.Reflection.PropertyInfo::GetGetMethod()"
$mGetMeth  = Find-Ref $tMod "System.Reflection.MethodInfo System.Type::GetMethod(System.String,System.Reflection.BindingFlags)"
$mHMCtor   = Find-Ref $tMod "System.Void HarmonyLib.HarmonyMethod::.ctor(System.Reflection.MethodInfo)"
$mHPatch   = Find-Ref $tMod "System.Reflection.MethodInfo HarmonyLib.Harmony::Patch(System.Reflection.MethodBase,HarmonyLib.HarmonyMethod,HarmonyLib.HarmonyMethod,HarmonyLib.HarmonyMethod,HarmonyLib.HarmonyMethod)"

$mPrefixCB = New-Object Mono.Cecil.MethodDefinition("PrefixClientBounds", [Mono.Cecil.MethodAttributes]"Private, Static", $tMod.TypeSystem.Boolean)
$byRefRect = New-Object Mono.Cecil.ByReferenceType($tRect)
$mPrefixCB.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("__result", [Mono.Cecil.ParameterAttributes]::None, $byRefRect)))
$ilCB = $mPrefixCB.Body.GetILProcessor()
$ilCB.Append($ilCB.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$ilCB.Append($ilCB.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_0))
$ilCB.Append($ilCB.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_0))
$ilCB.Append($ilCB.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fSNatW))
$ilCB.Append($ilCB.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fSNatH))
$ilCB.Append($ilCB.Create([Mono.Cecil.Cil.OpCodes]::Newobj, $mRectCtor))
$ilCB.Append($ilCB.Create([Mono.Cecil.Cil.OpCodes]::Stobj, $tRect))
$ilCB.Append($ilCB.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_0))
$ilCB.Append($ilCB.Create([Mono.Cecil.Cil.OpCodes]::Ret))
$meType.Methods.Add($mPrefixCB)

$mPatchCB = New-Object Mono.Cecil.MethodDefinition("PatchClientBounds", [Mono.Cecil.MethodAttributes]"Private, Static", $tMod.TypeSystem.Void)
$mPatchCB.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("harmony", [Mono.Cecil.ParameterAttributes]::None, $tHarmony)))
$mPatchCB.Body.InitLocals = $true
$vWinT  = New-Object Mono.Cecil.Cil.VariableDefinition($tType)
$vProp  = New-Object Mono.Cecil.Cil.VariableDefinition($tPropInfo)
$vGetM  = New-Object Mono.Cecil.Cil.VariableDefinition($tMethInfo)
$vPrefM = New-Object Mono.Cecil.Cil.VariableDefinition($tMethInfo)
foreach ($v in @($vWinT, $vProp, $vGetM, $vPrefM)) { $mPatchCB.Body.Variables.Add($v) }

$ilPC = $mPatchCB.Body.GetILProcessor()
$lRetPC   = $ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ret)
$lLeavePC = $ilPC.Create([Mono.Cecil.Cil.OpCodes]::Leave, $lRetPC)
$lCatchPC = $ilPC.Create([Mono.Cecil.Cil.OpCodes]::Pop)
$lTryPC   = $ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldtoken, $tGameWin)
$ilPC.Append($lTryPC)
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Call, $mGetTypeH))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mGetAsm))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, "Microsoft.Xna.Framework.iOSGameWindow"))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mAsmGetT))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vWinT))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vWinT))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lLeavePC))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vWinT))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, "ClientBounds"))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4, 52))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mGetProp))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vProp))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vProp))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lLeavePC))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vProp))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mGetGetM))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vGetM))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vGetM))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lLeavePC))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldtoken, $meType))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Call, $mGetTypeH))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, "PrefixClientBounds"))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4, 56))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mGetMeth))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vPrefM))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vPrefM))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lLeavePC))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vGetM))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vPrefM))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Newobj, $mHMCtor))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldnull))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldnull))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Ldnull))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mHPatch))
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Pop))
$ilPC.Append($lLeavePC)
$ilPC.Append($lCatchPC)
$ilPC.Append($ilPC.Create([Mono.Cecil.Cil.OpCodes]::Leave, $lRetPC))
$ilPC.Append($lRetPC)

$ehPC = New-Object Mono.Cecil.Cil.ExceptionHandler([Mono.Cecil.Cil.ExceptionHandlerType]::Catch)
$ehPC.CatchType = $tMod.TypeSystem.Object
$ehPC.TryStart = $lTryPC
$ehPC.TryEnd = $lCatchPC
$ehPC.HandlerStart = $lCatchPC
$ehPC.HandlerEnd = $lRetPC
$mPatchCB.Body.ExceptionHandlers.Add($ehPC)
$meType.Methods.Add($mPatchCB)

$ahpM = $meType.Methods | Where-Object { $_.Name -eq "ApplyHarmonyPatches" }
Expand-ShortBranches $ahpM
$ap = $ahpM.Body.GetILProcessor()
$afterStloc0 = $ahpM.Body.Instructions[3]
$ap.InsertBefore($afterStloc0, $ap.Create([Mono.Cecil.Cil.OpCodes]::Ldloc_0))
$ap.InsertBefore($afterStloc0, $ap.Create([Mono.Cecil.Cil.OpCodes]::Call, $mPatchCB))

# 1.5 Menu Lag Fixes: Skip Environment.StackTrace in PrefixSetActiveClickableMenu + GameMenu GC 350->140MB
$pSetMenu = $meType.Methods | Where-Object { $_.Name -eq "PrefixSetActiveClickableMenu" }
$mGetVerbose = $meType.Methods | Where-Object { $_.Name -eq "get_VerboseInputLogging" }
if ($pSetMenu -and $mGetVerbose) {
    Expand-ShortBranches $pSetMenu
    $pmp = $pSetMenu.Body.GetILProcessor()
    $pmFirst = $pSetMenu.Body.Instructions[0]
    $m0 = $pmp.Create([Mono.Cecil.Cil.OpCodes]::Call, $mGetVerbose)
    $m1 = $pmp.Create([Mono.Cecil.Cil.OpCodes]::Brtrue, $pmFirst)
    $m2 = $pmp.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_1)
    $m3 = $pmp.Create([Mono.Cecil.Cil.OpCodes]::Ret)
    $pmp.InsertBefore($pmFirst, $m0)
    $pmp.InsertBefore($pmFirst, $m1)
    $pmp.InsertBefore($pmFirst, $m2)
    $pmp.InsertBefore($pmFirst, $m3)
    Retarget-Branches $pSetMenu $pmFirst $m0
    $m1.Operand = $pmFirst
}

$pGMCtor = $meType.Methods | Where-Object { $_.Name -eq "PrefixGameMenuCtor" }
$gmLowered = 0
if ($pGMCtor) {
    foreach ($ins in $pGMCtor.Body.Instructions) {
        if ($ins.OpCode.Name -eq "ldc.i4" -and [int]$ins.Operand -eq 350) {
            $ins.Operand = [int]140
            $gmLowered++
        }
    }
}
# 1.6 Direct IL hooks on TouchVirtualPad.Update & TouchVirtualPad.Draw for SDVMobileControls (zero Harmony/JIT required!)
$coreScope = $tMod.TypeSystem.Object.Scope
$func3Ref = New-Object Mono.Cecil.TypeReference("System", "Func``3", $tMod, $coreScope, $false)
$func3Ref.GenericParameters.Add((New-Object Mono.Cecil.GenericParameter("T1", $func3Ref)))
$func3Ref.GenericParameters.Add((New-Object Mono.Cecil.GenericParameter("T2", $func3Ref)))
$func3Ref.GenericParameters.Add((New-Object Mono.Cecil.GenericParameter("TResult", $func3Ref)))

function Add-MobileHook($targetMethod, [string]$fieldName) {
    $arg1Type = $targetMethod.Parameters[0].ParameterType
    $funcInst = New-Object Mono.Cecil.GenericInstanceType($func3Ref)
    $funcInst.GenericArguments.Add($tMod.TypeSystem.Object)
    $funcInst.GenericArguments.Add($arg1Type)
    $funcInst.GenericArguments.Add($tMod.TypeSystem.Boolean)

    $fld = New-Object Mono.Cecil.FieldDefinition($fieldName, [Mono.Cecil.FieldAttributes]"Public, Static", $funcInst)
    $padType.Fields.Add($fld)

    $mInvoke = New-Object Mono.Cecil.MethodReference("Invoke", $func3Ref.GenericParameters[2], $funcInst)
    $mInvoke.HasThis = $true
    $mInvoke.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition($func3Ref.GenericParameters[0])))
    $mInvoke.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition($func3Ref.GenericParameters[1])))

    Expand-ShortBranches $targetMethod
    $hp = $targetMethod.Body.GetILProcessor()
    $orig0 = $targetMethod.Body.Instructions[0]

    $i0 = $hp.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fld)
    $i1 = $hp.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $orig0)
    $i2 = $hp.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fld)
    $i3 = $hp.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0)
    $i4 = $hp.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_1)
    $i5 = $hp.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mInvoke)
    $i6 = $hp.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $orig0)
    $i7 = $hp.Create([Mono.Cecil.Cil.OpCodes]::Ret)

    foreach ($ins in @($i0, $i1, $i2, $i3, $i4, $i5, $i6, $i7)) {
        $hp.InsertBefore($orig0, $ins)
    }
    Write-Host "  Injected direct IL hook TouchVirtualPad.$fieldName at instruction 0 of $($targetMethod.Name)($($arg1Type.Name))"
}

$mPadUpdate = $padType.Methods | Where-Object { $_.Name -eq "Update" -and $_.Parameters.Count -eq 1 } | Select-Object -First 1
$mPadDraw   = $padType.Methods | Where-Object { $_.Name -eq "Draw"   -and $_.Parameters.Count -eq 1 } | Select-Object -First 1
if (-not $mPadUpdate -or -not $mPadDraw) { throw "Missing TouchVirtualPad.Update or Draw" }
Add-MobileHook $mPadUpdate "MobileUpdateHook"
Add-MobileHook $mPadDraw   "MobileDrawHook"

# 1.6b Direct IL hook TouchVirtualPad.MobileExtraKeysHook (Action<HashSet<Keys>>) for unlimited custom PC hotkey buttons!
$fSimKeys = $padType.Fields | Where-Object { $_.Name -eq "_simKeysSet" }
$hashSetKeysType = $fSimKeys.FieldType
$action1Ref = New-Object Mono.Cecil.TypeReference("System", "Action``1", $tMod, $coreScope, $false)
$action1Ref.GenericParameters.Add((New-Object Mono.Cecil.GenericParameter("T", $action1Ref)))
$actionKeysInst = New-Object Mono.Cecil.GenericInstanceType($action1Ref)
$actionKeysInst.GenericArguments.Add($hashSetKeysType)

$fExtraKeys = New-Object Mono.Cecil.FieldDefinition("MobileExtraKeysHook", [Mono.Cecil.FieldAttributes]"Public, Static", $actionKeysInst)
$padType.Fields.Add($fExtraKeys)

$mActionInvoke = New-Object Mono.Cecil.MethodReference("Invoke", $tMod.TypeSystem.Void, $actionKeysInst)
$mActionInvoke.HasThis = $true
$mActionInvoke.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition($action1Ref.GenericParameters[0])))

# 1) get_HasActiveHotkeyInput: if (MobileExtraKeysHook != null) return true;
$mHasHotkey = $padType.Methods | Where-Object { $_.Name -eq "get_HasActiveHotkeyInput" }
if (-not $mHasHotkey) { throw "Missing get_HasActiveHotkeyInput" }
Expand-ShortBranches $mHasHotkey
$hhp = $mHasHotkey.Body.GetILProcessor()
$hh0 = $mHasHotkey.Body.Instructions[0]
$hhp.InsertBefore($hh0, $hhp.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fExtraKeys))
$hhp.InsertBefore($hh0, $hhp.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $hh0))
$hhp.InsertBefore($hh0, $hhp.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_1))
$hhp.InsertBefore($hh0, $hhp.Create([Mono.Cecil.Cil.OpCodes]::Ret))

# 2) get_CurrentSimulatedKeyboardState: right before final _simKeysSet.get_Count(), invoke MobileExtraKeysHook(_simKeysSet)
$mSimKb = $padType.Methods | Where-Object { $_.Name -eq "get_CurrentSimulatedKeyboardState" }
if (-not $mSimKb) { throw "Missing get_CurrentSimulatedKeyboardState" }
Expand-ShortBranches $mSimKb
$skp = $mSimKb.Body.GetILProcessor()
$skIns = $mSimKb.Body.Instructions
$countAnchor = $null
for ($i = $skIns.Count - 1; $i -ge 2; $i--) {
    if ($skIns[$i].OpCode.Name -eq "callvirt" -and "$($skIns[$i].Operand)" -like "*HashSet*get_Count*") {
        # Locate the FIRST of the two get_Count() calls (the `if (_simKeysSet.Count == 0)` check)
        $countAnchor = $skIns[$i - 2]
    }
}
if (-not $countAnchor) { throw "Could not find _simKeysSet.get_Count() anchor in get_CurrentSimulatedKeyboardState" }
$ek0 = $skp.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fExtraKeys)
$ek1 = $skp.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $countAnchor)
$ek2 = $skp.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fExtraKeys)
$ek3 = $skp.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0)
$ek4 = $skp.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $fSimKeys)
$ek5 = $skp.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mActionInvoke)
foreach ($ins in @($ek0, $ek1, $ek2, $ek3, $ek4, $ek5)) { $skp.InsertBefore($countAnchor, $ins) }
Retarget-Branches $mSimKb $countAnchor $ek0
$ek1.Operand = $countAnchor
Write-Host "  Injected direct IL hook TouchVirtualPad.MobileExtraKeysHook (Action<HashSet<Keys>>) for custom PC buttons!"

Write-Host "  v7 TouchControls: Native 3x/2x + Direct MobileHooks + PrefixClientBounds + MenuLag(stackSkip, GC 140MB x$gmLowered) + Poll 45f + Watchdog $bumped"
$tAsm.Write($outTouch)
$tAsm.Dispose()
Verify-Assembly $outTouch

# ==============================================================================
# PART 2: PATCH SDViOS.dll (v7-host.dll) — LOCKED 2.0x + TRACE LOG FILTER
# ==============================================================================
Write-Host "Patching SDViOS.dll -> $outHost ..."
$hAsm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($srcHost)
$hMod = $hAsm.MainModule

$touchBytes = [System.IO.File]::ReadAllBytes($outTouch)
$resReplaced = $false
for ($r = 0; $r -lt $hMod.Resources.Count; $r++) {
    if ($hMod.Resources[$r].Name -like "*SDViOSTouchControls.dll") {
        $rName = $hMod.Resources[$r].Name
        $hMod.Resources[$r] = New-Object Mono.Cecil.EmbeddedResource($rName, [Mono.Cecil.ManifestResourceAttributes]::Public, $touchBytes)
        Write-Host "  Embedded v6 TouchControls resource: $rName ($($touchBytes.Length) bytes)"
        $resReplaced = $true
    }
}
if (-not $resReplaced) { throw "Could not find embedded SDViOSTouchControls.dll resource!" }

$ghType = $hMod.GetType("SDViOS.Loader.GameHost")
$elType = $hMod.GetType("SDViOS.Diagnostics.EngineLogger")
$linkM  = $ghType.Methods | Where-Object { $_.Name -eq "LinkGameWindowToScene" }
$logM   = $elType.Methods | Where-Object { $_.Name -eq "Log" }
$fActiveView = $ghType.Fields | Where-Object { $_.Name -eq "_activeGameView" }

$hGetFolder   = Find-Ref $hMod "System.String System.Environment::GetFolderPath(System.Environment/SpecialFolder)"
$hPathCombine = Find-Ref $hMod "System.String System.IO.Path::Combine(System.String,System.String)"
$hFileExists  = Find-Ref $hMod "System.Boolean System.IO.File::Exists(System.String)"
$hReadAllText = Find-Ref $hMod "System.String System.IO.File::ReadAllText(System.String)"
$hSetCSF      = Find-Ref $hMod "System.Void UIKit.UIView::set_ContentScaleFactor(System.Runtime.InteropServices.NFloat)"
$hGetLayer    = Find-Ref $hMod "CoreAnimation.CALayer UIKit.UIView::get_Layer()"
$hSetLayerSc  = Find-Ref $hMod "System.Void CoreAnimation.CALayer::set_ContentsScale(System.Runtime.InteropServices.NFloat)"
$hNFloatFromF = Find-Ref $hMod "System.Runtime.InteropServices.NFloat System.Runtime.InteropServices.NFloat::op_Implicit(System.Single)"
$hStrEq       = Find-Ref $hMod "System.Boolean System.String::op_Equality(System.String,System.String)"
$hMathMax     = Find-Ref $hMod "System.Double System.Math::Max(System.Double,System.Double)"
if (!$hReadAllText) {
    $hReadAllText = New-Object Mono.Cecil.MethodReference("ReadAllText", $hMod.TypeSystem.String, $hFileExists.DeclaringType)
    $hReadAllText.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition($hMod.TypeSystem.String)))
}
$hStrContains = Find-Ref $hMod "System.Boolean System.String::Contains(System.String)"
if (!$hStrContains) {
    $hStrContains = New-Object Mono.Cecil.MethodReference("Contains", $hMod.TypeSystem.Boolean, $hMod.TypeSystem.String)
    $hStrContains.HasThis = $true
    $hStrContains.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition($hMod.TypeSystem.String)))
}
$hMathRound = New-Object Mono.Cecil.MethodReference("Round", $hMod.TypeSystem.Double, $hMathMax.DeclaringType)
$hMathRound.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition($hMod.TypeSystem.Double)))

$nfloatType = $hNFloatFromF.ReturnType
$hNFloatToF = New-Object Mono.Cecil.MethodReference("op_Explicit", $hMod.TypeSystem.Single, $nfloatType)
$hNFloatToF.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition($nfloatType)))

$fTrimLogs = New-Object Mono.Cecil.FieldDefinition("_v6TrimLogs", [Mono.Cecil.FieldAttributes]"Public, Static", $hMod.TypeSystem.Int32)
$ghType.Fields.Add($fTrimLogs)

# GameHost.ApplyPerfRenderScale(NFloat nativeScale):
#   1. Reads Documents/sdv_perf.cfg for trimLogs flag (default ON; ",0" -> OFF)
#   2. ALWAYS caps nativeScale to 2.0f max (v6 locked 2.0x!)
$mApplyScale = New-Object Mono.Cecil.MethodDefinition("ApplyPerfRenderScale", [Mono.Cecil.MethodAttributes]"Public, Static", $nfloatType)
$mApplyScale.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("nativeScale", [Mono.Cecil.ParameterAttributes]::None, $nfloatType)))
$mApplyScale.Body.InitLocals = $true
$vCfgP = New-Object Mono.Cecil.Cil.VariableDefinition($hMod.TypeSystem.String)
$vRaw  = New-Object Mono.Cecil.Cil.VariableDefinition($hMod.TypeSystem.String)
$vNatF = New-Object Mono.Cecil.Cil.VariableDefinition($hMod.TypeSystem.Single)
foreach ($v in @($vCfgP, $vRaw, $vNatF)) { $mApplyScale.Body.Variables.Add($v) }

$il = $mApplyScale.Body.GetILProcessor()
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_1))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Stsfld, $fTrimLogs))

$lAfterTry = $il.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0)
$lLeave    = $il.Create([Mono.Cecil.Cil.OpCodes]::Leave, $lAfterTry)
$lCatch    = $il.Create([Mono.Cecil.Cil.OpCodes]::Pop)
$lTryStart = $il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_5)

$il.Append($lTryStart)
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Call, $hGetFolder))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, "sdv_perf.cfg"))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Call, $hPathCombine))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vCfgP))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vCfgP))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Call, $hFileExists))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lLeave))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vCfgP))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Call, $hReadAllText))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vRaw))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vRaw))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, ",0"))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $hStrContains))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lLeave))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_2))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Stsfld, $fTrimLogs))
$il.Append($lLeave)
$il.Append($lCatch)
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Leave, $lAfterTry))

# v7: Always return native device scale untouched (3.0x on 3x devices, 2.0x on 2x devices)
$il.Append($lAfterTry)
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ret))

$ehS = New-Object Mono.Cecil.Cil.ExceptionHandler([Mono.Cecil.Cil.ExceptionHandlerType]::Catch)
$ehS.CatchType = $hMod.TypeSystem.Object
$ehS.TryStart = $lTryStart
$ehS.TryEnd = $lCatch
$ehS.HandlerStart = $lCatch
$ehS.HandlerEnd = $lAfterTry
$mApplyScale.Body.ExceptionHandlers.Add($ehS)
$ghType.Methods.Add($mApplyScale)

# Helper: GameHost.ReapplyViewScale(UIView view, NFloat scale)
$mReapply = New-Object Mono.Cecil.MethodDefinition("ReapplyViewScale", [Mono.Cecil.MethodAttributes]"Public, Static", $hMod.TypeSystem.Void)
$mReapply.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("view", [Mono.Cecil.ParameterAttributes]::None, $fActiveView.FieldType)))
$mReapply.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("scale", [Mono.Cecil.ParameterAttributes]::None, $nfloatType)))
$il2 = $mReapply.Body.GetILProcessor()
$lDoneR  = $il2.Create([Mono.Cecil.Cil.OpCodes]::Ret)
$lLeaveR = $il2.Create([Mono.Cecil.Cil.OpCodes]::Leave, $lDoneR)
$lCatchR = $il2.Create([Mono.Cecil.Cil.OpCodes]::Pop)
$lTryR   = $il2.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0)
$il2.Append($lTryR)
$il2.Append($il2.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lLeaveR))
$il2.Append($il2.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$il2.Append($il2.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_1))
$il2.Append($il2.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $hSetCSF))
$il2.Append($il2.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$il2.Append($il2.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $hGetLayer))
$il2.Append($il2.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lLeaveR))
$il2.Append($il2.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$il2.Append($il2.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $hGetLayer))
$il2.Append($il2.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_1))
$il2.Append($il2.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $hSetLayerSc))
$il2.Append($lLeaveR)
$il2.Append($lCatchR)
$il2.Append($il2.Create([Mono.Cecil.Cil.OpCodes]::Leave, $lDoneR))
$il2.Append($lDoneR)

$ehR = New-Object Mono.Cecil.Cil.ExceptionHandler([Mono.Cecil.Cil.ExceptionHandlerType]::Catch)
$ehR.CatchType = $hMod.TypeSystem.Object
$ehR.TryStart = $lTryR
$ehR.TryEnd = $lCatchR
$ehR.HandlerStart = $lCatchR
$ehR.HandlerEnd = $lDoneR
$mReapply.Body.ExceptionHandlers.Add($ehR)
$ghType.Methods.Add($mReapply)

# Hook LinkGameWindowToScene
Expand-ShortBranches $linkM
$lp = $linkM.Body.GetILProcessor()
$lIns = $linkM.Body.Instructions
$site1 = $null; $vScaleVar = $null
for ($i = 2; $i -lt $lIns.Count; $i++) {
    if ($lIns[$i].OpCode.Name -eq "callvirt" -and "$($lIns[$i].Operand)" -eq $hSetCSF.FullName) {
        $site1 = $lIns[$i - 2]; $vScaleVar = $lIns[$i - 1].Operand; break
    }
}
$i1a = $lp.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vScaleVar)
$i1b = $lp.Create([Mono.Cecil.Cil.OpCodes]::Call, $mApplyScale)
$i1c = $lp.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vScaleVar)
$lp.InsertBefore($site1, $i1a); $lp.InsertBefore($site1, $i1b); $lp.InsertBefore($site1, $i1c)
Retarget-Branches $linkM $site1 $i1a

$site2 = $null
for ($i = 2; $i -lt $lIns.Count; $i++) {
    if ($lIns[$i].OpCode.Name -eq "ldstr" -and $lIns[$i].Operand -eq "DestroyFramebuffer") {
        $site2 = $lIns[$i - 2]; break
    }
}
$i2a = $lp.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fActiveView)
$i2b = $lp.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vScaleVar)
$i2c = $lp.Create([Mono.Cecil.Cil.OpCodes]::Call, $mReapply)
$lp.InsertBefore($site2, $i2a); $lp.InsertBefore($site2, $i2b); $lp.InsertBefore($site2, $i2c)
Retarget-Branches $linkM $site2 $i2a

# Hook GetDisplayPixelWidth & GetDisplayPixelHeight
foreach ($dpName in @("GetDisplayPixelWidth", "GetDisplayPixelHeight")) {
    $dpM = $ghType.Methods | Where-Object { $_.Name -eq $dpName }
    Expand-ShortBranches $dpM
    $dpIL = $dpM.Body.GetILProcessor()
    for ($i = 0; $i -lt $dpM.Body.Instructions.Count; $i++) {
        if ($dpM.Body.Instructions[$i].OpCode.Name -eq "callvirt" -and "$($dpM.Body.Instructions[$i].Operand)" -like "*UIScreen::get_Scale*") {
            $dpIL.InsertBefore($dpM.Body.Instructions[$i + 1], $dpIL.Create([Mono.Cecil.Cil.OpCodes]::Call, $mApplyScale))
            break
        }
    }
    for ($i = 0; $i -lt $dpM.Body.Instructions.Count; $i++) {
        if ($dpM.Body.Instructions[$i].OpCode.Name -eq "conv.i4") {
            $dpIL.InsertBefore($dpM.Body.Instructions[$i], $dpIL.Create([Mono.Cecil.Cil.OpCodes]::Call, $hMathRound))
            break
        }
    }
}

# 2.5 iOS 26/27 Launch Scene-Create Deferral: Use a clean 0.35s NSTimer in AppDelegate.FinishedLaunching
# instead of immediate BeginInvokeOnMainThread, so UIScene (willConnectToSession -> sceneDidBecomeActive)
# completes its FrontBoard handshake in <50ms before GameHost.Launch() starts!
$adType = $hMod.Types | Where-Object { $_.Name -eq "AppDelegate" }
$mFl    = $adType.Methods | Where-Object { $_.Name -eq "FinishedLaunching" }
$mB46   = $adType.Methods | Where-Object { $_.Name -eq "<FinishedLaunching>b__4_6" }
$mCreateTimer = Find-Ref $hMod 'Foundation.NSTimer Foundation.NSTimer::CreateScheduledTimer(System.Double,System.Boolean,System.Action`1<Foundation.NSTimer>)'
$mActTimerCtor = $null
foreach ($t in $hMod.Types) {
    foreach ($m in $t.Methods) {
        if (-not $m.HasBody) { continue }
        foreach ($ins in $m.Body.Instructions) {
            if ($ins.OpCode.Name -eq "newobj" -and "$($ins.Operand)" -like "*Action*<Foundation.NSTimer>::.ctor*") {
                $mActTimerCtor = $ins.Operand; break
            }
        }
        if ($mActTimerCtor) { break }
    }
    if ($mActTimerCtor) { break }
}

if ($adType -and $mFl -and $mB46 -and $mCreateTimer -and $mActTimerCtor) {
    $tNSTimer = $mCreateTimer.ReturnType
    $mTimerCb = New-Object Mono.Cecil.MethodDefinition("DeferredLaunchTimerCallback", [Mono.Cecil.MethodAttributes]"Private", $hMod.TypeSystem.Void)
    $mTimerCb.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("timer", [Mono.Cecil.ParameterAttributes]::None, $tNSTimer)))
    $ilTC = $mTimerCb.Body.GetILProcessor()
    $ilTC.Append($ilTC.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
    $ilTC.Append($ilTC.Create([Mono.Cecil.Cil.OpCodes]::Call, $mB46))
    $ilTC.Append($ilTC.Create([Mono.Cecil.Cil.OpCodes]::Ret))
    $adType.Methods.Add($mTimerCb)

    Expand-ShortBranches $mFl
    $flIL = $mFl.Body.GetILProcessor()
    $flIns = $mFl.Body.Instructions
    for ($i = 0; $i -lt ($flIns.Count - 4); $i++) {
        if ($flIns[$i].OpCode.Name -eq "call" -and "$($flIns[$i].Operand)" -like "*UIApplication::get_SharedApplication*" -and
            $flIns[$i+4].OpCode.Name -eq "callvirt" -and "$($flIns[$i+4].Operand)" -like "*BeginInvokeOnMainThread*") {
            $flIns[$i].OpCode   = [Mono.Cecil.Cil.OpCodes]::Ldc_R8
            $flIns[$i].Operand  = [double]0.35
            $flIL.InsertAfter($flIns[$i], $flIL.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_0))
            # $flIns[$i+2] is now ldarg.0
            $flIns[$i+3].Operand = $mTimerCb
            $flIns[$i+4].Operand = $mActTimerCtor
            $flIns[$i+5].OpCode  = [Mono.Cecil.Cil.OpCodes]::Call
            $flIns[$i+5].Operand = $mCreateTimer
            $flIL.InsertAfter($flIns[$i+5], $flIL.Create([Mono.Cecil.Cil.OpCodes]::Pop))
            Write-Host "  iOS 26/27 Scene Launch Fix (SDViOS): Deferred GameHost.Launch via 0.35s NSTimer after sceneDidBecomeActive!"
            break
        }
    }
}

# EngineLogger.Log TRACE filter + 10s flush timer
$mDropTrace = New-Object Mono.Cecil.MethodDefinition("ShouldDropTrace", [Mono.Cecil.MethodAttributes]"Private, Static", $hMod.TypeSystem.Boolean)
$mDropTrace.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("msg", [Mono.Cecil.ParameterAttributes]::None, $hMod.TypeSystem.String)))
$mDropTrace.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("lvl", [Mono.Cecil.ParameterAttributes]::None, $hMod.TypeSystem.String)))
$il3 = $mDropTrace.Body.GetILProcessor()
$lRetFalse = $il3.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_0)
$il3.Append($il3.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fTrimLogs))
$il3.Append($il3.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_2))
$il3.Append($il3.Create([Mono.Cecil.Cil.OpCodes]::Beq, $lRetFalse))
$il3.Append($il3.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$il3.Append($il3.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lRetFalse))
$il3.Append($il3.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_1))
$il3.Append($il3.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, "INFO"))
$il3.Append($il3.Create([Mono.Cecil.Cil.OpCodes]::Call, $hStrEq))
$il3.Append($il3.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lRetFalse))
$il3.Append($il3.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$il3.Append($il3.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, " TRACE "))
$il3.Append($il3.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $hStrContains))
$il3.Append($il3.Create([Mono.Cecil.Cil.OpCodes]::Ret))
$il3.Append($lRetFalse)
$il3.Append($il3.Create([Mono.Cecil.Cil.OpCodes]::Ret))
$elType.Methods.Add($mDropTrace)

Expand-ShortBranches $logM
$ep = $logM.Body.GetILProcessor()
$logFirst = $logM.Body.Instructions[0]
$g0 = $ep.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0)
$g1 = $ep.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_1)
$g2 = $ep.Create([Mono.Cecil.Cil.OpCodes]::Call, $mDropTrace)
$g3 = $ep.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $logFirst)
$g4 = $ep.Create([Mono.Cecil.Cil.OpCodes]::Ret)
$ep.InsertBefore($logFirst, $g0); $ep.InsertBefore($logFirst, $g1); $ep.InsertBefore($logFirst, $g2); $ep.InsertBefore($logFirst, $g3); $ep.InsertBefore($logFirst, $g4)
Retarget-Branches $logM $logFirst $g0
$g3.Operand = $logFirst

foreach ($ins in $logM.Body.Instructions) {
    if ($ins.OpCode.Name -eq "ldc.i4" -and [int]$ins.Operand -eq 1500) {
        $ins.Operand = [int]10000
    }
}

$hAsm.Write($outHost)
$hAsm.Dispose()
Verify-Assembly $outHost

# ==============================================================================
# PART 3: PATCH MonoGame.Framework.dll (v7-monogame.dll)
# ==============================================================================
Write-Host "Patching MonoGame.Framework.dll -> $outMG ..."
$rp = New-Object Mono.Cecil.ReaderParameters
$res = New-Object Mono.Cecil.DefaultAssemblyResolver
$res.AddSearchDirectory("$env:TEMP\sdv-clean-ui")
if (Test-Path "$env:TEMP\tmpf4sv1zm1\Payload\SDViOS.app") {
    $res.AddSearchDirectory("$env:TEMP\tmpf4sv1zm1\Payload\SDViOS.app")
}
$rp.AssemblyResolver = $res
$mgAsm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($srcMG, $rp)
$mgMod = $mgAsm.MainModule
$mgWin = $mgMod.Types | Where-Object { $_.Name -eq "iOSGameWindow" }
$cbM   = $mgWin.Methods | Where-Object { $_.Name -eq "get_ClientBounds" }
$mgNFloatToD = Find-Ref $mgMod "System.Double System.Runtime.InteropServices.NFloat::op_Implicit(System.Runtime.InteropServices.NFloat)"
$mgMathRound = Find-Ref $mgMod "System.Double System.Math::Round(System.Double)"
Expand-ShortBranches $cbM
$mp = $cbM.Body.GetILProcessor()
$mIns = $cbM.Body.Instructions
$patchedMul = 0
for ($i = 1; $i -lt ($mIns.Count - 2); $i++) {
    if ($mIns[$i].OpCode.Name -eq "ldloc.1" -and
        $mIns[$i+1].OpCode.Name -eq "call" -and "$($mIns[$i+1].Operand)" -eq "System.Int32 System.Runtime.InteropServices.NFloat::op_Explicit(System.Runtime.InteropServices.NFloat)" -and
        $mIns[$i+2].OpCode.Name -eq "mul") {
        $iLdloc1 = $mIns[$i]; $iExpl = $mIns[$i+1]; $iMul = $mIns[$i+2]
        $mp.InsertBefore($iLdloc1, $mp.Create([Mono.Cecil.Cil.OpCodes]::Conv_R8))
        $iExpl.Operand = $mgNFloatToD
        $mp.InsertAfter($iMul, $mp.Create([Mono.Cecil.Cil.OpCodes]::Conv_I4))
        $mp.InsertAfter($iMul, $mp.Create([Mono.Cecil.Cil.OpCodes]::Call, $mgMathRound))
        $patchedMul++
        $i += 4
    }
}

# 3.2 (REMOVED WaveBank LoadSoundEffect RAM Eviction hook — it injected cross-class private field access
#      on OALSoundBuffer.dataSize which caused Mono Interpreter to throw FieldAccessException on every sound!)

# 3.3 CRITICAL iOS AUDIO FIX in OpenALSoundController.OpenSoundController():
#   a) Pass NULL (not "") to alcOpenDevice(NULL)
#   b) NOP out invalid AVAudioSession.SharedInstance().Init() which resets/breaks AVAudioSession on modern iOS!
#   c) Pass NULL (not unterminated int[0]) to alcCreateContext(_device, NULL)
#   d) Explicitly call Alc.ProcessContext(_context) after MakeContextCurrent(_context)
$tOalCtrl = $mgMod.Types | Where-Object { $_.Name -eq "OpenALSoundController" }
$mOpenSC  = $tOalCtrl.Methods | Where-Object { $_.Name -eq "OpenSoundController" }
$tAlc     = $mgMod.Types | Where-Object { $_.Name -eq "Alc" }
$fProcCtx = $tAlc.Fields | Where-Object { $_.Name -eq "ProcessContext" }
$fCtxFld  = $tOalCtrl.Fields | Where-Object { $_.Name -eq "_context" }
$tProcDel = $fProcCtx.FieldType.Resolve()
$mProcInv = $tProcDel.Methods | Where-Object { $_.Name -eq "Invoke" }
if (-not $mOpenSC -or -not $fProcCtx -or -not $fCtxFld -or -not $mProcInv) {
    throw "Missing OpenALSoundController / Alc audio targets!"
}
$tOalCtrl.IsPublic = $true
$tAlc.IsPublic = $true
if ($tProcDel.IsNested) { $tProcDel.IsNestedPublic = $true } else { $tProcDel.IsPublic = $true }
$fProcCtx.IsPublic = $true
$fCtxFld.IsPublic = $true
$mProcInv.IsPublic = $true
Expand-ShortBranches $mOpenSC
$oscP = $mOpenSC.Body.GetILProcessor()
$oIns = $mOpenSC.Body.Instructions
$fixOpenDev = 0; $fixAvInit = 0; $fixNullAttr = 0; $fixProcCtx = 0
for ($i = 0; $i -lt $oIns.Count; $i++) {
    # a) ldsfld string.Empty -> ldnull right before d_alcopendevice::Invoke
    if ($oIns[$i].OpCode.Name -eq "ldsfld" -and "$($oIns[$i].Operand)" -like "*String::Empty*" -and ($i + 1) -lt $oIns.Count -and "$($oIns[$i+1].Operand)" -like "*d_alcopendevice::Invoke*") {
        $oIns[$i].OpCode = [Mono.Cecil.Cil.OpCodes]::Ldnull
        $oIns[$i].Operand = $null
        $fixOpenDev++
    }
    # b) NOP out AVAudioSession.SharedInstance().Init()
    if ($oIns[$i].OpCode.Name -eq "call" -and "$($oIns[$i].Operand)" -like "*AVAudioSession::SharedInstance*" -and ($i + 1) -lt $oIns.Count -and $oIns[$i+1].OpCode.Name -eq "callvirt" -and "$($oIns[$i+1].Operand)" -like "*NSObject::Init*") {
        $oIns[$i].OpCode = [Mono.Cecil.Cil.OpCodes]::Nop; $oIns[$i].Operand = $null
        $oIns[$i+1].OpCode = [Mono.Cecil.Cil.OpCodes]::Nop; $oIns[$i+1].Operand = $null
        $fixAvInit++
    }
    # c) ldc.i4.0; newarr System.Int32 -> ldnull; nop right before stloc.1 for Alc.CreateContext
    if ($oIns[$i].OpCode.Name -eq "ldc.i4.0" -and ($i + 1) -lt $oIns.Count -and $oIns[$i+1].OpCode.Name -eq "newarr" -and "$($oIns[$i+1].Operand)" -eq "System.Int32") {
        $oIns[$i].OpCode = [Mono.Cecil.Cil.OpCodes]::Ldnull; $oIns[$i].Operand = $null
        $oIns[$i+1].OpCode = [Mono.Cecil.Cil.OpCodes]::Nop; $oIns[$i+1].Operand = $null
        $fixNullAttr++
    }
    # d) Call Alc.ProcessContext(_context) right after Alc.MakeContextCurrent(_context)
    if ($fixProcCtx -eq 0 -and $oIns[$i].OpCode.Name -eq "callvirt" -and "$($oIns[$i].Operand)" -like "*d_alcmakecontextcurrent::Invoke*") {
        $afterMake = $oIns[$i + 1]
        $oscP.InsertBefore($afterMake, $oscP.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fProcCtx))
        $oscP.InsertBefore($afterMake, $oscP.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
        $oscP.InsertBefore($afterMake, $oscP.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $fCtxFld))
        $oscP.InsertBefore($afterMake, $oscP.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mProcInv))
        $fixProcCtx++
        $i += 4
    }
}
if ($fixOpenDev -ne 1 -or $fixAvInit -ne 1 -or $fixNullAttr -ne 1 -or $fixProcCtx -ne 1) {
    throw "OpenALSoundController audio fix mismatch: openDev=$fixOpenDev avInit=$fixAvInit nullAttr=$fixNullAttr procCtx=$fixProcCtx"
}
Write-Host "  iOS Audio Fix (MonoGame): OpenDevice(NULL) + Removed AVAudioSession.Init() + CreateContext(NULL) + ProcessContext()!"

# 3.4 Home Indicator / Edge Gesture Deferral: ALWAYS return UIRectEdge.All (15) in iOSGameViewController
$tGameVC = $mgMod.Types | Where-Object { $_.Name -eq "iOSGameViewController" }
$mDeferEdges = $tGameVC.Methods | Where-Object { $_.Name -eq "get_PreferredScreenEdgesDeferringSystemGestures" }
if ($mDeferEdges) {
    $mDeferEdges.Body.ExceptionHandlers.Clear()
    $mDeferEdges.Body.Instructions.Clear()
    $deIL = $mDeferEdges.Body.GetILProcessor()
    $deIL.Append($deIL.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_S, [sbyte]15))
    $deIL.Append($deIL.Create([Mono.Cecil.Cil.OpCodes]::Conv_I8))
    $deIL.Append($deIL.Create([Mono.Cecil.Cil.OpCodes]::Ret))
    Write-Host "  iOS Home Bar Fix (MonoGame): iOSGameViewController.PreferredScreenEdgesDeferringSystemGestures -> ALWAYS UIRectEdge.All (15)!"
}

# 3.5 iOS Shader Mod Compatibility Fix (Dual MGFX v9 + v10 Binary Reader + Desktop GLSL -> GLSL ES 1.00 auto-translator):
$tEffect    = $mgMod.Types | Where-Object { $_.Name -eq "Effect" }
$tMgfxHdr   = $tEffect.NestedTypes | Where-Object { $_.Name -eq "MGFXHeader" }
$fHdrVer    = $tMgfxHdr.Fields | Where-Object { $_.Name -eq "Version" }
$fMgfxSig   = $tMgfxHdr.Fields | Where-Object { $_.Name -eq "MGFXSignature" }
$mBrReadByte= Find-Ref $mgMod "System.Byte System.IO.BinaryReader::ReadByte()"
$mBrReadI32 = Find-Ref $mgMod "System.Int32 System.IO.BinaryReader::ReadInt32()"
$tBinReader = $mBrReadI32.DeclaringType

if ($tEffect -and $fHdrVer -and $fMgfxSig -and $mBrReadByte -and $mBrReadI32) {
    $fCurVer = New-Object Mono.Cecil.FieldDefinition("CurrentMgfxVersion", [Mono.Cecil.FieldAttributes]"Public, Static", $mgMod.TypeSystem.Int32)
    $tEffect.Fields.Add($fCurVer)

    # 3.5a Effect.ReadHeader: accept MGFX Version 8..10 and store Effect.CurrentMgfxVersion = header.Version
    $mReadHdr = $tEffect.Methods | Where-Object { $_.Name -eq "ReadHeader" }
    Expand-ShortBranches $mReadHdr
    $rhIL = $mReadHdr.Body.GetILProcessor()
    for ($i = 0; $i -lt $mReadHdr.Body.Instructions.Count; $i++) {
        $ins = $mReadHdr.Body.Instructions[$i]
        if ($ins.OpCode.Name -eq "ldc.i4.s" -and [int]$ins.Operand -eq 10) {
            $ins.Operand = [sbyte]8
            break
        }
    }
    $rhLastRet = $mReadHdr.Body.Instructions[$mReadHdr.Body.Instructions.Count - 1]
    $rhIL.InsertBefore($rhLastRet, $rhIL.Create([Mono.Cecil.Cil.OpCodes]::Dup))
    $rhIL.InsertBefore($rhLastRet, $rhIL.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $fHdrVer))
    $rhIL.InsertBefore($rhLastRet, $rhIL.Create([Mono.Cecil.Cil.OpCodes]::Stsfld, $fCurVer))

    # 3.5b Build Effect.ReadMgfxCount(BinaryReader r) -> (CurrentMgfxVersion < 10) ? (int)r.ReadByte() : r.ReadInt32()
    $mReadCnt = New-Object Mono.Cecil.MethodDefinition("ReadMgfxCount", [Mono.Cecil.MethodAttributes]"Public, Static", $mgMod.TypeSystem.Int32)
    $mReadCnt.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("r", [Mono.Cecil.ParameterAttributes]::None, $tBinReader)))
    $ilRC = $mReadCnt.Body.GetILProcessor()
    $lV10Cnt = $ilRC.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0)
    $ilRC.Append($ilRC.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fCurVer))
    $ilRC.Append($ilRC.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_S, [sbyte]10))
    $ilRC.Append($ilRC.Create([Mono.Cecil.Cil.OpCodes]::Bge, $lV10Cnt))
    $ilRC.Append($ilRC.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
    $ilRC.Append($ilRC.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mBrReadByte))
    $ilRC.Append($ilRC.Create([Mono.Cecil.Cil.OpCodes]::Ret))
    $ilRC.Append($lV10Cnt)
    $ilRC.Append($ilRC.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mBrReadI32))
    $ilRC.Append($ilRC.Create([Mono.Cecil.Cil.OpCodes]::Ret))
    $tEffect.Methods.Add($mReadCnt)

    # 3.5c Build Effect.ReadMgfxShaderIndex(BinaryReader r) -> (CurrentMgfxVersion < 10) ? (int)(sbyte)r.ReadByte() : r.ReadInt32()
    $mReadIdx = New-Object Mono.Cecil.MethodDefinition("ReadMgfxShaderIndex", [Mono.Cecil.MethodAttributes]"Public, Static", $mgMod.TypeSystem.Int32)
    $mReadIdx.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("r", [Mono.Cecil.ParameterAttributes]::None, $tBinReader)))
    $ilRI = $mReadIdx.Body.GetILProcessor()
    $lV10Idx = $ilRI.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0)
    $ilRI.Append($ilRI.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fCurVer))
    $ilRI.Append($ilRI.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_S, [sbyte]10))
    $ilRI.Append($ilRI.Create([Mono.Cecil.Cil.OpCodes]::Bge, $lV10Idx))
    $ilRI.Append($ilRI.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
    $ilRI.Append($ilRI.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mBrReadByte))
    $ilRI.Append($ilRI.Create([Mono.Cecil.Cil.OpCodes]::Conv_I1))
    $ilRI.Append($ilRI.Create([Mono.Cecil.Cil.OpCodes]::Ret))
    $ilRI.Append($lV10Idx)
    $ilRI.Append($ilRI.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mBrReadI32))
    $ilRI.Append($ilRI.Create([Mono.Cecil.Cil.OpCodes]::Ret))
    $tEffect.Methods.Add($mReadIdx)

    # 3.5d Build Effect.ReadMgfxTailSignature(BinaryReader r) -> (CurrentMgfxVersion < 10) ? MGFXHeader.MGFXSignature : r.ReadInt32()
    $mReadTail = New-Object Mono.Cecil.MethodDefinition("ReadMgfxTailSignature", [Mono.Cecil.MethodAttributes]"Public, Static", $mgMod.TypeSystem.Int32)
    $mReadTail.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("r", [Mono.Cecil.ParameterAttributes]::None, $tBinReader)))
    $ilRT = $mReadTail.Body.GetILProcessor()
    $lV10Tail = $ilRT.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0)
    $ilRT.Append($ilRT.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fCurVer))
    $ilRT.Append($ilRT.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_S, [sbyte]10))
    $ilRT.Append($ilRT.Create([Mono.Cecil.Cil.OpCodes]::Bge, $lV10Tail))
    $ilRT.Append($ilRT.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fMgfxSig))
    $ilRT.Append($ilRT.Create([Mono.Cecil.Cil.OpCodes]::Ret))
    $ilRT.Append($lV10Tail)
    $ilRT.Append($ilRT.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mBrReadI32))
    $ilRT.Append($ilRT.Create([Mono.Cecil.Cil.OpCodes]::Ret))
    $tEffect.Methods.Add($mReadTail)

    # 3.5e Redirect ReadInt32() in ReadEffect (5 calls), ReadParameters (1 call), ReadAnnotations (1 call)
    foreach ($mName in @("ReadEffect", "ReadParameters", "ReadAnnotations")) {
        $em = $tEffect.Methods | Where-Object { $_.Name -eq $mName }
        foreach ($ins in $em.Body.Instructions) {
            if ($ins.OpCode.Name -eq "callvirt" -and "$($ins.Operand)" -eq "System.Int32 System.IO.BinaryReader::ReadInt32()") {
                $ins.OpCode  = [Mono.Cecil.Cil.OpCodes]::Call
                $ins.Operand = $mReadCnt
            }
        }
    }

    # 3.5f Redirect first 3 ReadInt32() in ReadPasses (passCount -> ReadMgfxCount, vsIdx & psIdx -> ReadMgfxShaderIndex)
    $mReadPasses = $tEffect.Methods | Where-Object { $_.Name -eq "ReadPasses" }
    $rpIdx = 0
    foreach ($ins in $mReadPasses.Body.Instructions) {
        if ($ins.OpCode.Name -eq "callvirt" -and "$($ins.Operand)" -eq "System.Int32 System.IO.BinaryReader::ReadInt32()") {
            $rpIdx++
            if ($rpIdx -eq 1) {
                $ins.OpCode  = [Mono.Cecil.Cil.OpCodes]::Call
                $ins.Operand = $mReadCnt
            } elseif ($rpIdx -eq 2 -or $rpIdx -eq 3) {
                $ins.OpCode  = [Mono.Cecil.Cil.OpCodes]::Call
                $ins.Operand = $mReadIdx
            }
        }
    }

    # 3.5g Redirect trailing ReadInt32() in Effect..ctor(GraphicsDevice, byte[], int, int)
    foreach ($em in ($tEffect.Methods | Where-Object { $_.Name -eq ".ctor" -and $_.Parameters.Count -eq 4 })) {
        for ($i = 0; $i -lt ($em.Body.Instructions.Count - 1); $i++) {
            $ins = $em.Body.Instructions[$i]
            if ($ins.OpCode.Name -eq "callvirt" -and "$($ins.Operand)" -eq "System.Int32 System.IO.BinaryReader::ReadInt32()" -and
                "$($em.Body.Instructions[$i+1].Operand)" -like "*MGFXSignature*") {
                $ins.OpCode  = [Mono.Cecil.Cil.OpCodes]::Call
                $ins.Operand = $mReadTail
                break
            }
        }
    }
    Write-Host "  iOS Shader Fix (MonoGame): Full dual MGFX v9 (ReadByte/ReadSByte) + MGFX v10 (ReadInt32) parser active!"
}

$tShader = $mgMod.Types | Where-Object { $_.Name -eq "Shader" }
$mStrReplace  = Find-Ref $mgMod "System.String System.String::Replace(System.String,System.String)"
$mStrContains = Find-Ref $mgMod "System.Boolean System.String::Contains(System.String)"
$mStrConcat2  = Find-Ref $mgMod "System.String System.String::Concat(System.String,System.String)"
$mConWrite    = Find-Ref $mgMod "System.Void System.Console::WriteLine(System.String)"

if ($tShader -and $mStrReplace -and $mStrContains -and $mStrConcat2) {
    $mPatchGlsl = New-Object Mono.Cecil.MethodDefinition("PatchGlslForIosEs", [Mono.Cecil.MethodAttributes]"Public, Static", $mgMod.TypeSystem.String)
    $mPatchGlsl.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("src", [Mono.Cecil.ParameterAttributes]::None, $mgMod.TypeSystem.String)))
    $ilG = $mPatchGlsl.Body.GetILProcessor()
    $lRetG = $ilG.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0)
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lRetG))
    foreach ($ver in @("#version 110", "#version 120", "#version 130", "#version 140", "#version 150", "#version 330")) {
        $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
        $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, $ver))
        $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, ""))
        $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mStrReplace))
        $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Starg, $mPatchGlsl.Parameters[0]))
    }
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, "precision mediump float;"))
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, "precision highp float;"))
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mStrReplace))
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Starg, $mPatchGlsl.Parameters[0]))
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, "precision "))
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mStrContains))
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Brtrue, $lRetG))
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, "#ifdef GL_ES`nprecision highp float;`nprecision mediump int;`n#endif`n"))
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Call, $mStrConcat2))
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Starg, $mPatchGlsl.Parameters[0]))
    $ilG.Append($lRetG)
    $ilG.Append($ilG.Create([Mono.Cecil.Cil.OpCodes]::Ret))
    $tShader.Methods.Add($mPatchGlsl)

    $mPlatConst = $tShader.Methods | Where-Object { $_.Name -eq "PlatformConstruct" }
    $pcIL = $mPlatConst.Body.GetILProcessor()
    for ($i = 0; $i -lt $mPlatConst.Body.Instructions.Count; $i++) {
        $ins = $mPlatConst.Body.Instructions[$i]
        if ($ins.OpCode.Name -eq "stfld" -and "$($ins.Operand)" -like "*_glslCode*") {
            $pcIL.InsertBefore($ins, $pcIL.Create([Mono.Cecil.Cil.OpCodes]::Call, $mPatchGlsl))
            break
        }
    }

    $mGetSH = $tShader.Methods | Where-Object { $_.Name -eq "GetShaderHandle" }
    if ($mGetSH -and $mConWrite) {
        for ($i = 1; $i -lt $mGetSH.Body.Instructions.Count; $i++) {
            if ($mGetSH.Body.Instructions[$i].OpCode.Name -eq "pop" -and "$($mGetSH.Body.Instructions[$i-1].Operand)" -like "*GetShaderInfoLog*") {
                $mGetSH.Body.Instructions[$i].OpCode = [Mono.Cecil.Cil.OpCodes]::Call
                $mGetSH.Body.Instructions[$i].Operand = $mConWrite
                break
            }
        }
    }
    Write-Host "  iOS Shader Fix (MonoGame): MGFX v8..10 enabled + Auto Desktop GLSL -> GLSL ES precision header + ShaderInfoLog output!"
}

$mgAsm.Write($outMG)
$mgAsm.Dispose()
Verify-Assembly $outMG
Write-Host "ALL V7.8 (AUDIO + HOME BAR + MENU OPT + RAM SAVER) PATCHES COMPLETE!"
