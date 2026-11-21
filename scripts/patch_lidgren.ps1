$ErrorActionPreference = 'Stop'
$d = Join-Path $env:TEMP 'sdv-clean-ui'
[void][Reflection.Assembly]::LoadFrom((Join-Path $env:TEMP 'sdv-viewport\Mono.Cecil.dll'))
$ops = [Mono.Cecil.Cil.OpCodes]

function Expand-ShortBranches($body) {
    $map = @{
        'Br_S'     = $ops::Br
        'Brfalse_S'= $ops::Brfalse
        'Brtrue_S' = $ops::Brtrue
        'Beq_S'    = $ops::Beq
        'Bne_Un_S' = $ops::Bne_Un
        'Bge_S'    = $ops::Bge
        'Bgt_S'    = $ops::Bgt
        'Ble_S'    = $ops::Ble
        'Blt_S'    = $ops::Blt
        'Bge_Un_S' = $ops::Bge_Un
        'Bgt_Un_S' = $ops::Bgt_Un
        'Ble_Un_S' = $ops::Ble_Un
        'Blt_Un_S' = $ops::Blt_Un
        'Leave_S'  = $ops::Leave
    }
    foreach ($ins in $body.Instructions) {
        $code = $ins.OpCode.Code.ToString()
        if ($map.ContainsKey($code)) { $ins.OpCode = $map[$code] }
    }
}

function Find-MemberRef($module, [string]$fullName) {
    $m = $module.GetMemberReferences() | Where-Object { $_.FullName -eq $fullName } | Select-Object -First 1
    if (-not $m) { throw "Missing member ref: $fullName" }
    return $m
}

function Find-TypeRef($module, [string]$fullName) {
    $t = $module.GetTypeReferences() | Where-Object { $_.FullName -eq $fullName } | Select-Object -First 1
    if (-not $t) { throw "Missing type ref: $fullName" }
    return $t
}

# Helper to wrap a method returning a reference type in an outer try { ... } catch (Exception) { fallback }
function Wrap-MethodSafe($method, $refException, [scriptblock]$emitFallback) {
    $body = $method.Body
    Expand-ShortBranches $body
    $body.InitLocals = $true
    $vRet = [Mono.Cecil.Cil.VariableDefinition]::new($method.ReturnType)
    $body.Variables.Add($vRet)

    $il = $body.GetILProcessor()
    $firstIns = $body.Instructions[0]
    $lblEnd = $il.Create($ops::Ldloc, $vRet)

    # Convert every existing Ret to: stloc vRet; leave lblEnd
    $rets = @($body.Instructions | Where-Object { $_.OpCode.Code -eq 'Ret' })
    foreach ($r in $rets) {
        $r.OpCode = $ops::Stloc
        $r.Operand = $vRet
        $il.InsertAfter($r, $il.Create($ops::Leave, $lblEnd))
    }

    # Append catch (Exception) { pop; <fallback>; stloc vRet; leave lblEnd }
    $lblCatch = $il.Create($ops::Pop)
    $il.Append($lblCatch)
    & $emitFallback $il
    $il.Emit($ops::Stloc, $vRet)
    $il.Emit($ops::Leave, $lblEnd)
    $il.Append($lblEnd)
    $il.Emit($ops::Ret)

    $eh = [Mono.Cecil.Cil.ExceptionHandler]::new([Mono.Cecil.Cil.ExceptionHandlerType]::Catch)
    $eh.CatchType    = $refException
    $eh.TryStart     = $firstIns
    $eh.TryEnd       = $lblCatch
    $eh.HandlerStart = $lblCatch
    $eh.HandlerEnd   = $lblEnd
    $body.ExceptionHandlers.Add($eh)
}

$srcPath = Join-Path $env:TEMP 'lidgren_bundled.dll'
$outPath = Join-Path $d 'lidgren-ios.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($srcPath)
try {
    $mod = $asm.MainModule
    $ts  = $mod.TypeSystem
    $refException = Find-TypeRef   $mod 'System.Exception'
    $refBroadcast = Find-MemberRef $mod 'System.Net.IPAddress System.Net.IPAddress::Broadcast'
    $refRngCtor   = Find-MemberRef $mod 'System.Void System.Security.Cryptography.RNGCryptoServiceProvider::.ctor()'
    $refRngBytes  = Find-MemberRef $mod 'System.Void System.Security.Cryptography.RandomNumberGenerator::GetBytes(System.Byte[])'
    $refIpMask    = Find-MemberRef $mod 'System.Net.IPAddress System.Net.NetworkInformation.UnicastIPAddressInformation::get_IPv4Mask()'

    $nu = $mod.Types | Where-Object Name -eq 'NetUtility'
    $np = $mod.Types | Where-Object Name -eq 'NetPeer'

    # 1. NetUtility.GetNetworkInterface(): remove IPGlobalProperties.GetIPGlobalProperties() (throws PlatformNotSupportedException on iOS!)
    #    and wrap whole method in try/catch -> return null.
    $mGetNi = $nu.Methods | Where-Object Name -eq 'GetNetworkInterface'
    $ins = $mGetNi.Body.Instructions
    if ("$($ins[0].Operand)" -notmatch 'GetIPGlobalProperties') { throw "Unexpected GetNetworkInterface prologue: $($ins[0])" }
    for ($k = 0; $k -lt 4; $k++) {
        $ins[$k].OpCode = $ops::Nop
        $ins[$k].Operand = $null
    }
    Wrap-MethodSafe $mGetNi $refException { param($il) $il.Emit($ops::Ldnull) }
    Write-Output "  Patched NetUtility.GetNetworkInterface (removed IPGlobalProperties + try/catch)"

    # 2. NetUtility.GetBroadcastAddress(): add null-check on IPv4Mask (for iOS VPN utun), return IPAddress.Broadcast instead of null,
    #    and wrap whole method in try/catch -> return IPAddress.Broadcast.
    $mGetBc = $nu.Methods | Where-Object Name -eq 'GetBroadcastAddress'
    Expand-ShortBranches $mGetBc.Body
    $bcIl = $mGetBc.Body.GetILProcessor()
    $bcIns = $mGetBc.Body.Instructions
    # Change early `if (ni == null) return null;` to `return IPAddress.Broadcast;`
    if ($bcIns[4].OpCode.Code -ne 'Ldnull' -or $bcIns[5].OpCode.Code -ne 'Ret') { throw "Unexpected GetBroadcastAddress early return: $($bcIns[4]) / $($bcIns[5])" }
    $bcIns[4].OpCode = $ops::Ldsfld
    $bcIns[4].Operand = $refBroadcast
    # Find callvirt get_IPv4Mask() and ensure if null we jump to loop continue target (same target as bne.un right before it!)
    $maskCall = $null
    $loopNext = $null
    for ($k = 2; $k -lt $bcIns.Count; $k++) {
        if ($bcIns[$k].Operand -eq $refIpMask) {
            $maskCall = $bcIns[$k]
            # Find previous branch target (bne.un -> MoveNext)
            for ($j = $k - 1; $j -ge 0; $j--) {
                if ($bcIns[$j].OpCode.Code -eq 'Bne_Un') { $loopNext = $bcIns[$j].Operand; break }
            }
            break
        }
    }
    if (-not $maskCall -or -not $loopNext) { throw "Could not find IPv4Mask call or loop continuation" }
    # After callvirt get_IPv4Mask(), stack has [IPAddress mask].
    # Insert: dup; brtrue keepMask; pop; br loopNext; keepMask:
    $keepMask = $maskCall.Next
    $iDup = $bcIl.Create($ops::Dup)
    $iOk  = $bcIl.Create($ops::Brtrue, $keepMask)
    $iPop = $bcIl.Create($ops::Pop)
    $iJmp = $bcIl.Create($ops::Br, $loopNext)
    $bcIl.InsertAfter($maskCall, $iDup)
    $bcIl.InsertAfter($iDup, $iOk)
    $bcIl.InsertAfter($iOk, $iPop)
    $bcIl.InsertAfter($iPop, $iJmp)
    Wrap-MethodSafe $mGetBc $refException { param($il) $il.Emit($ops::Ldsfld, $refBroadcast) }
    Write-Output "  Patched NetUtility.GetBroadcastAddress (IPv4Mask null guard + IPAddress.Broadcast fallback + try/catch)"

    # 3. NetUtility.GetMacAddressBytes(): return 8 random bytes via RNGCryptoServiceProvider
    #    (iOS blocks MAC addresses and returns constant 02:00:00:00:00:00 which would collide across iPhones!)
    $mMac = $nu.Methods | Where-Object Name -eq 'GetMacAddressBytes'
    $mMac.Body.Instructions.Clear()
    $mMac.Body.Variables.Clear()
    $mMac.Body.ExceptionHandlers.Clear()
    $mMac.Body.InitLocals = $true
    $vBytes = [Mono.Cecil.Cil.VariableDefinition]::new([Mono.Cecil.ArrayType]::new($ts.Byte))
    $mMac.Body.Variables.Add($vBytes)
    $macIl = $mMac.Body.GetILProcessor()
    $macIl.Emit($ops::Ldc_I4_8)
    $macIl.Emit($ops::Newarr, $ts.Byte)
    $macIl.Emit($ops::Stloc, $vBytes)
    $macIl.Emit($ops::Newobj, $refRngCtor)
    $macIl.Emit($ops::Ldloc, $vBytes)
    $macIl.Emit($ops::Callvirt, $refRngBytes)
    $macIl.Emit($ops::Ldloc, $vBytes)
    $macIl.Emit($ops::Ret)
    Write-Output "  Patched NetUtility.GetMacAddressBytes (unique 8-byte cryptographic peer ID per iOS device)"

    # 4. NetUtility.GetMyAddress(out IPAddress mask): wrap in try/catch -> mask = null; return null;
    $mMy = $nu.Methods | Where-Object Name -eq 'GetMyAddress'
    Wrap-MethodSafe $mMy $refException {
        param($il)
        $il.Emit($ops::Ldarg_0)
        $il.Emit($ops::Ldnull)
        $il.Emit($ops::Stind_Ref)
        $il.Emit($ops::Ldnull)
    }
    Write-Output "  Patched NetUtility.GetMyAddress (try/catch)"

    # 5. NetPeer.BindSocket(bool reBind): always enable SO_REUSEADDR before Bind so quick re-host on iOS never hits AddressAlreadyInUse
    $mBind = $np.Methods | Where-Object Name -eq 'BindSocket'
    $bIns = $mBind.Body.Instructions
    $patchedReuse = $false
    for ($k = 0; $k -lt $bIns.Count - 5; $k++) {
        if ($bIns[$k].OpCode.Code -eq 'Ldarg_1' -and $bIns[$k+1].OpCode.Code -eq 'Brfalse_S' -and
            $bIns[$k+4].OpCode.Code -eq 'Ldc_I4' -and [int]$bIns[$k+4].Operand -eq 65535) {
            $bIns[$k].OpCode = $ops::Nop; $bIns[$k].Operand = $null
            $bIns[$k+1].OpCode = $ops::Nop; $bIns[$k+1].Operand = $null
            $patchedReuse = $true
            break
        }
    }
    if (-not $patchedReuse) { throw "Could not find SO_REUSEADDR guard in BindSocket" }
    Write-Output "  Patched NetPeer.BindSocket (unconditional SO_REUSEADDR for fast re-host)"

    $asm.Write($outPath)
} finally { $asm.Dispose() }

# Verify all branches and exception handlers
$v = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($outPath)
try {
    foreach ($tn in 'NetUtility','NetPeer') {
        $t = $v.MainModule.Types | Where-Object Name -eq $tn
        foreach ($m in $t.Methods | Where-Object HasBody) {
            $set = [System.Collections.Generic.HashSet[object]]::new()
            foreach ($i in $m.Body.Instructions) { [void]$set.Add($i) }
            foreach ($i in $m.Body.Instructions) {
                if ($i.OpCode.OperandType -in @('InlineBrTarget','ShortInlineBrTarget')) {
                    if (-not $i.Operand -or -not $set.Contains($i.Operand)) { throw "Broken branch in $tn::$($m.Name) at $i" }
                }
            }
            foreach ($eh in $m.Body.ExceptionHandlers) {
                foreach ($pt in @($eh.TryStart, $eh.TryEnd, $eh.HandlerStart, $eh.HandlerEnd)) {
                    if ($pt -and -not $set.Contains($pt)) { throw "Broken EH in $tn::$($m.Name)" }
                }
            }
        }
    }
    Write-Output "  Verified IL integrity of lidgren-ios.dll OK"
} finally { $v.Dispose() }

Get-Item -LiteralPath $outPath | Select-Object Name, Length | Format-Table -AutoSize
