param(
    [string]$WorkDir  = "$env:TEMP\sdv-clean-ui",
    [string]$CecilDll = "$env:TEMP\sdv-viewport\Mono.Cecil.dll"
)
$ErrorActionPreference = "Stop"
Add-Type -Path $CecilDll

$srcTouch = Join-Path $WorkDir "menubar-touch.dll"
$srcHost  = Join-Path $WorkDir "menubar-host.dll"
$srcMG    = Join-Path $WorkDir "MonoGame.Framework.dll"
$outTouch = Join-Path $WorkDir "v6-touch.dll"
$outHost  = Join-Path $WorkDir "v6-host.dll"
$outMG    = Join-Path $WorkDir "v6-monogame.dll"

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
# PART 1: PATCH SDViOSTouchControls.dll (v6-touch.dll) — LOCKED 2.0x + MENU OPT
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

# Only downscale 3x Retina displays (h <= 1400, w % 3 == 0, h % 3 == 0) -> crisp integer 2x!
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vHT))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4, 1400))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Bgt, $lRetR))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vWT))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_3))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Rem))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Brtrue, $lRetR))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vHT))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_3))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Rem))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Brtrue, $lRetR))

$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vWT))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_3))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Div))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_2))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Mul))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vWT))

$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vHT))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_3))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Div))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_2))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Mul))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vHT))

$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vWT))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Stfld, $fNatW))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vHT))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Stfld, $fNatH))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vWT))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Stsfld, $fSNatW))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vHT))
$ilR.Append($ilR.Create([Mono.Cecil.Cil.OpCodes]::Stsfld, $fSNatH))
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
Write-Host "  v6 TouchControls: Locked 2.0x + PrefixClientBounds + MenuLag(stackSkip, GC 140MB x$gmLowered) + Poll 45f + Watchdog $bumped"
$tAsm.Write($outTouch)
$tAsm.Dispose()
Verify-Assembly $outTouch

# ==============================================================================
# PART 2: PATCH SDViOS.dll (v6-host.dll) — LOCKED 2.0x + TRACE LOG FILTER
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

# Always cap scale > 2.0f -> 2.0f (crisp integer 2.0x!)
$lRetNative = $il.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0)
$il.Append($lAfterTry)
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Call, $hNFloatToF))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vNatF))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vNatF))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R4, [float]2.0))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ble_Un, $lRetNative))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R4, [float]2.0))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Call, $hNFloatFromF))
$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ret))
$il.Append($lRetNative)
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
# PART 3: PATCH MonoGame.Framework.dll (v6-monogame.dll)
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
# 3.2 WaveBank Smart Music LRU Eviction (frees 100–350 MB of idle OpenAL PCM music buffers!)
$tWB   = $mgMod.Types | Where-Object { $_.Name -eq "WaveBank" }
$tSE   = $mgMod.Types | Where-Object { $_.Name -eq "SoundEffect" }
$tOAL  = $mgMod.Types | Where-Object { $_.Name -eq "OALSoundBuffer" }
$tSEI  = $mgMod.Types | Where-Object { $_.Name -eq "SoundEffectInstance" }
$tPool = $mgMod.Types | Where-Object { $_.Name -eq "SoundEffectInstancePool" }

$fPoolLocker  = $tPool.Fields | Where-Object { $_.Name -eq "_locker" }
$fPoolPlaying = $tPool.Fields | Where-Object { $_.Name -eq "_playingInstances" }
$fSeiEffect   = $tSEI.Fields  | Where-Object { $_.Name -eq "_effect" }
$fWbSounds    = $tWB.Fields   | Where-Object { $_.Name -eq "_sounds" }
$fSeDisposed  = $tSE.Fields   | Where-Object { $_.Name -eq "_isDisposed" }
$fSeBuffer    = $tSE.Fields   | Where-Object { $_.Name -eq "SoundBuffer" }
$fOalSize     = $tOAL.Fields  | Where-Object { $_.Name -eq "dataSize" }
$mSeDispose   = $tSE.Methods  | Where-Object { $_.Name -eq "Dispose" -and $_.Parameters.Count -eq 0 }

$mStopPooled = $tPool.Methods | Where-Object { $_.Name -eq "StopPooledInstances" }
$mMonEnter   = $null; $mMonExit = $null; $mListGetItem = $null; $mListCount = $null
foreach ($ins in $mStopPooled.Body.Instructions) {
    if ($ins.Operand -is [Mono.Cecil.MethodReference]) {
        $fn = $ins.Operand.FullName
        if ($fn -like "*Monitor::Enter*") { $mMonEnter = $ins.Operand }
        elseif ($fn -like "*Monitor::Exit*") { $mMonExit = $ins.Operand }
        elseif ($fn -like "*::get_Item*") { $mListGetItem = $ins.Operand }
        elseif ($fn -like "*::get_Count*") { $mListCount = $ins.Operand }
    }
}
if (!$mMonEnter -or !$mMonExit -or !$mListGetItem -or !$mListCount -or !$mSeDispose) {
    throw "Missing MonoGame audio pool references!"
}

# Build SoundEffectInstancePool.IsEffectPlaying(SoundEffect effect)
$mIsPlaying = New-Object Mono.Cecil.MethodDefinition("IsEffectPlaying", [Mono.Cecil.MethodAttributes]"Assembly, Static", $mgMod.TypeSystem.Boolean)
$mIsPlaying.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("effect", [Mono.Cecil.ParameterAttributes]::None, $tSE)))
$mIsPlaying.Body.InitLocals = $true
$vLck   = New-Object Mono.Cecil.Cil.VariableDefinition($mgMod.TypeSystem.Object)
$vTaken = New-Object Mono.Cecil.Cil.VariableDefinition($mgMod.TypeSystem.Boolean)
$vFound = New-Object Mono.Cecil.Cil.VariableDefinition($mgMod.TypeSystem.Boolean)
$vIdx   = New-Object Mono.Cecil.Cil.VariableDefinition($mgMod.TypeSystem.Int32)
$vInst  = New-Object Mono.Cecil.Cil.VariableDefinition($tSEI)
foreach ($v in @($vLck, $vTaken, $vFound, $vIdx, $vInst)) { $mIsPlaying.Body.Variables.Add($v) }

$ilP = $mIsPlaying.Body.GetILProcessor()
$lRetBool  = $ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vFound)
$lFinStart = $ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vTaken)
$lFinEnd   = $ilP.Create([Mono.Cecil.Cil.OpCodes]::Endfinally)
$lLeaveFin = $ilP.Create([Mono.Cecil.Cil.OpCodes]::Leave, $lRetBool)
$lLoopCond = $ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vIdx)
$lLoopBody = $ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fPoolPlaying)
$lLoopNext = $ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vIdx)

$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lRetBool))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fPoolLocker))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vLck))
$lTryP = $ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vLck)
$ilP.Append($lTryP)
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldloca, $vTaken))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Call, $mMonEnter))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_0))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vIdx))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Br, $lLoopCond))

$ilP.Append($lLoopBody)
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vIdx))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mListGetItem))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vInst))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vInst))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lLoopNext))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vInst))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $fSeiEffect))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Bne_Un, $lLoopNext))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_1))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vFound))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Leave, $lRetBool))

$ilP.Append($lLoopNext)
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_1))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Add))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vIdx))

$ilP.Append($lLoopCond)
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldsfld, $fPoolPlaying))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mListCount))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Blt, $lLoopBody))
$ilP.Append($lLeaveFin)

$ilP.Append($lFinStart)
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lFinEnd))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vLck))
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Call, $mMonExit))
$ilP.Append($lFinEnd)

$ilP.Append($lRetBool)
$ilP.Append($ilP.Create([Mono.Cecil.Cil.OpCodes]::Ret))

$ehP = New-Object Mono.Cecil.Cil.ExceptionHandler([Mono.Cecil.Cil.ExceptionHandlerType]::Finally)
$ehP.TryStart = $lTryP
$ehP.TryEnd = $lFinStart
$ehP.HandlerStart = $lFinStart
$ehP.HandlerEnd = $lRetBool
$mIsPlaying.Body.ExceptionHandlers.Add($ehP)
$tPool.Methods.Add($mIsPlaying)

# Build WaveBank.EvictIdleLargeSounds(int keepIndex)
$mEvict = New-Object Mono.Cecil.MethodDefinition("EvictIdleLargeSounds", [Mono.Cecil.MethodAttributes]"Private", $mgMod.TypeSystem.Void)
$mEvict.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition("keepIndex", [Mono.Cecil.ParameterAttributes]::None, $mgMod.TypeSystem.Int32)))
$mEvict.Body.InitLocals = $true
$vArr = New-Object Mono.Cecil.Cil.VariableDefinition($fWbSounds.FieldType)
$vI   = New-Object Mono.Cecil.Cil.VariableDefinition($mgMod.TypeSystem.Int32)
$vSe  = New-Object Mono.Cecil.Cil.VariableDefinition($tSE)
$vSb  = New-Object Mono.Cecil.Cil.VariableDefinition($tOAL)
foreach ($v in @($vArr, $vI, $vSe, $vSb)) { $mEvict.Body.Variables.Add($v) }

$ilE = $mEvict.Body.GetILProcessor()
$lRetE   = $ilE.Create([Mono.Cecil.Cil.OpCodes]::Ret)
$lLeaveE = $ilE.Create([Mono.Cecil.Cil.OpCodes]::Leave, $lRetE)
$lCatchE = $ilE.Create([Mono.Cecil.Cil.OpCodes]::Pop)
$lCondE  = $ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vI)
$lBodyE  = $ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vI)
$lNextE  = $ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vI)
$lTryE   = $ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0)

$ilE.Append($lTryE)
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $fWbSounds))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vArr))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vArr))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lLeaveE))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_0))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vI))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Br, $lCondE))

$ilE.Append($lBodyE)
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_1))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Beq, $lNextE))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vArr))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vI))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldelem_Ref))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vSe))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vSe))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lNextE))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vSe))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $fSeDisposed))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Brtrue, $lNextE))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vSe))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $fSeBuffer))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vSb))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vSb))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $lNextE))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vSb))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $fOalSize))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4, 524288)) # >= 512 KB (music / long ambient tracks)
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Blt, $lNextE))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vSe))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Call, $mIsPlaying))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Brtrue, $lNextE))
# Safe to evict: clear slot & release OpenAL buffer (20–35 MB per song!)
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vArr))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vI))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldnull))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Stelem_Ref))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vSe))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $mSeDispose))

$ilE.Append($lNextE)
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_1))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Add))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $vI))

$ilE.Append($lCondE)
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vArr))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Ldlen))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Conv_I4))
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Blt, $lBodyE))
$ilE.Append($lLeaveE)

$ilE.Append($lCatchE)
$ilE.Append($ilE.Create([Mono.Cecil.Cil.OpCodes]::Leave, $lRetE))
$ilE.Append($lRetE)

$ehE = New-Object Mono.Cecil.Cil.ExceptionHandler([Mono.Cecil.Cil.ExceptionHandlerType]::Catch)
$ehE.CatchType = $mgMod.TypeSystem.Object
$ehE.TryStart = $lTryE
$ehE.TryEnd = $lCatchE
$ehE.HandlerStart = $lCatchE
$ehE.HandlerEnd = $lRetE
$mEvict.Body.ExceptionHandlers.Add($ehE)
$tWB.Methods.Add($mEvict)

# Hook WaveBank.LoadSoundEffect(int trackIndex) right after `stelem.ref`
$mLoadSE = $tWB.Methods | Where-Object { $_.Name -eq "LoadSoundEffect" }
Expand-ShortBranches $mLoadSE
$lpSE = $mLoadSE.Body.GetILProcessor()
$hookedWave = $false
for ($i = 0; $i -lt $mLoadSE.Body.Instructions.Count; $i++) {
    if ($mLoadSE.Body.Instructions[$i].OpCode.Name -eq "stelem.ref") {
        $vLoadedSe = $mLoadSE.Body.Instructions[$i - 1].Operand
        $afterStore = $mLoadSE.Body.Instructions[$i + 1]
        $lpSE.InsertBefore($afterStore, $lpSE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vLoadedSe))
        $lpSE.InsertBefore($afterStore, $lpSE.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $afterStore))
        $lpSE.InsertBefore($afterStore, $lpSE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vLoadedSe))
        $lpSE.InsertBefore($afterStore, $lpSE.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $fSeBuffer))
        $lpSE.InsertBefore($afterStore, $lpSE.Create([Mono.Cecil.Cil.OpCodes]::Brfalse, $afterStore))
        $lpSE.InsertBefore($afterStore, $lpSE.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $vLoadedSe))
        $lpSE.InsertBefore($afterStore, $lpSE.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $fSeBuffer))
        $lpSE.InsertBefore($afterStore, $lpSE.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $fOalSize))
        $lpSE.InsertBefore($afterStore, $lpSE.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4, 524288))
        $lpSE.InsertBefore($afterStore, $lpSE.Create([Mono.Cecil.Cil.OpCodes]::Blt, $afterStore))
        $lpSE.InsertBefore($afterStore, $lpSE.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
        $lpSE.InsertBefore($afterStore, $lpSE.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_1))
        $lpSE.InsertBefore($afterStore, $lpSE.Create([Mono.Cecil.Cil.OpCodes]::Call, $mEvict))
        $hookedWave = $true
        break
    }
}
if (-not $hookedWave) { throw "Could not hook WaveBank.LoadSoundEffect" }
Write-Host "  RAM Saver (MonoGame): Added WaveBank auto-eviction for idle >=512KB OpenAL music buffers!"

$mgAsm.Write($outMG)
$mgAsm.Dispose()
Verify-Assembly $outMG
Write-Host "ALL V6 (2.0x LOCKED + MENU OPT + RAM SAVER) PATCHES COMPLETE!"
