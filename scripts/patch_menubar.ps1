param(
    [string]$WorkDir  = (Join-Path $env:TEMP 'sdv-clean-ui'),
    [string]$CecilDll = (Join-Path $env:TEMP 'sdv-viewport\Mono.Cecil.dll')
)
$ErrorActionPreference = 'Stop'
$d = $WorkDir
[void][Reflection.Assembly]::LoadFrom($CecilDll)
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
    if (-not $m) { throw "Missing member ref in $($module.Name): $fullName" }
    return $m
}

function Find-TypeRef($module, [string]$fullName) {
    $t = $module.GetTypeReferences() | Where-Object { $_.FullName -eq $fullName } | Select-Object -First 1
    if (-not $t) { throw "Missing type ref in $($module.Name): $fullName" }
    return $t
}

function Get-IntLiteralValue($ins) {
    switch ($ins.OpCode.Code.ToString()) {
        'Ldc_I4_M1' { return -1 }
        'Ldc_I4_0'  { return 0 }
        'Ldc_I4_1'  { return 1 }
        'Ldc_I4_2'  { return 2 }
        'Ldc_I4_3'  { return 3 }
        'Ldc_I4_4'  { return 4 }
        'Ldc_I4_5'  { return 5 }
        'Ldc_I4_6'  { return 6 }
        'Ldc_I4_7'  { return 7 }
        'Ldc_I4_8'  { return 8 }
        'Ldc_I4_S'  { return [int][sbyte]$ins.Operand }
        'Ldc_I4'    { return [int]$ins.Operand }
        default     { return $null }
    }
}

function Add-FullUiCustomization($asm, [bool]$isMod) {
    $mod = $asm.MainModule
    $ts = $mod.TypeSystem
    $tp = $mod.Types | Where-Object Name -eq 'TouchVirtualPad'
    if (-not $tp) { throw "TouchVirtualPad not found in $($asm.Name.Name)" }
    if ($tp.Fields | Where-Object Name -eq '_mbScale') { throw "$($asm.Name.Name) already patched" }

    $refGetFolderPath = Find-MemberRef $mod 'System.String System.Environment::GetFolderPath(System.Environment/SpecialFolder)'
    $refPathCombine   = Find-MemberRef $mod 'System.String System.IO.Path::Combine(System.String,System.String)'
    $refFileExists    = Find-MemberRef $mod 'System.Boolean System.IO.File::Exists(System.String)'
    $refReadAllText   = Find-MemberRef $mod 'System.String System.IO.File::ReadAllText(System.String)'
    $refStrEquals     = Find-MemberRef $mod 'System.Boolean System.String::Equals(System.String,System.String,System.StringComparison)'
    $refStrSplit      = Find-MemberRef $mod 'System.String[] System.String::Split(System.Char,System.StringSplitOptions)'
    $refStrTrim       = Find-MemberRef $mod 'System.String System.String::Trim()'
    $refInvCulture    = Find-MemberRef $mod 'System.Globalization.CultureInfo System.Globalization.CultureInfo::get_InvariantCulture()'
    $refTryParse      = Find-MemberRef $mod 'System.Boolean System.Single::TryParse(System.String,System.Globalization.NumberStyles,System.IFormatProvider,System.Single&)'
    $refClamp         = Find-MemberRef $mod 'System.Single System.Math::Clamp(System.Single,System.Single,System.Single)'
    $refMaxSingle     = Find-MemberRef $mod 'System.Single System.Math::Max(System.Single,System.Single)'
    $refColorWhite    = Find-MemberRef $mod 'Microsoft.Xna.Framework.Color Microsoft.Xna.Framework.Color::get_White()'
    $refColorMul      = Find-MemberRef $mod 'Microsoft.Xna.Framework.Color Microsoft.Xna.Framework.Color::op_Multiply(Microsoft.Xna.Framework.Color,System.Single)'
    $refException     = Find-TypeRef   $mod 'System.Exception'

    $mGetSettings = $tp.Methods | Where-Object Name -eq 'get_Settings'
    $tSettings    = $mod.Types  | Where-Object Name -eq 'TouchOverlaySettings'
    $mSettingsLoad= $tSettings.Methods | Where-Object Name -eq 'Load'
    $mUpdateLayout= $tp.Methods | Where-Object Name -eq 'UpdateLayout'
    $mUpdate      = $tp.Methods | Where-Object Name -eq 'Update'
    $mDraw        = $tp.Methods | Where-Object Name -eq 'Draw'
    $fVpW         = $tp.Fields  | Where-Object Name -eq '_viewportWidth'
    $fVpH         = $tp.Fields  | Where-Object Name -eq '_viewportHeight'

    # 1. Add static config fields (14 live parameters + init/poll state)
    $fAttr = [Mono.Cecil.FieldAttributes]'Private,Static'
    $fMbScale     = [Mono.Cecil.FieldDefinition]::new('_mbScale',     $fAttr, $ts.Single)
    $fMbTopY      = [Mono.Cecil.FieldDefinition]::new('_mbTopY',      $fAttr, $ts.Single)
    $fMbLeftX     = [Mono.Cecil.FieldDefinition]::new('_mbLeftX',     $fAttr, $ts.Single)
    $fMbGap       = [Mono.Cecil.FieldDefinition]::new('_mbGap',       $fAttr, $ts.Single)
    $fMbOpacity   = [Mono.Cecil.FieldDefinition]::new('_mbOpacity',   $fAttr, $ts.Single)
    $fAbxyScale   = [Mono.Cecil.FieldDefinition]::new('_abxyScale',   $fAttr, $ts.Single)
    $fAbxySpacing = [Mono.Cecil.FieldDefinition]::new('_abxySpacing', $fAttr, $ts.Single)
    $fAbxyOpacity = [Mono.Cecil.FieldDefinition]::new('_abxyOpacity', $fAttr, $ts.Single)
    $fTrigScale   = [Mono.Cecil.FieldDefinition]::new('_trigScale',   $fAttr, $ts.Single)
    $fTrigOpacity = [Mono.Cecil.FieldDefinition]::new('_trigOpacity', $fAttr, $ts.Single)
    $fJoyScale    = [Mono.Cecil.FieldDefinition]::new('_joyScale',    $fAttr, $ts.Single)
    $fJoyOpacity  = [Mono.Cecil.FieldDefinition]::new('_joyOpacity',  $fAttr, $ts.Single)
    $fModalScale  = [Mono.Cecil.FieldDefinition]::new('_modalScale',  $fAttr, $ts.Single)
    $fDrawerScale = [Mono.Cecil.FieldDefinition]::new('_drawerScale', $fAttr, $ts.Single)
    $fInit        = [Mono.Cecil.FieldDefinition]::new('_mbInit',      $fAttr, $ts.Boolean)
    $fTick        = [Mono.Cecil.FieldDefinition]::new('_mbPollTick',  $fAttr, $ts.Int32)
    $fLastRaw     = [Mono.Cecil.FieldDefinition]::new('_mbLastRaw',   $fAttr, $ts.String)
    foreach ($f in @($fMbScale,$fMbTopY,$fMbLeftX,$fMbGap,$fMbOpacity,$fAbxyScale,$fAbxySpacing,$fAbxyOpacity,$fTrigScale,$fTrigOpacity,$fJoyScale,$fJoyOpacity,$fModalScale,$fDrawerScale,$fInit,$fTick,$fLastRaw)) {
        $tp.Fields.Add($f)
    }

    # 2. Helper: ParseMbFloat(string[] parts, int idx, float defVal, float minVal, float maxVal)
    $mParse = [Mono.Cecil.MethodDefinition]::new('ParseMbFloat', [Mono.Cecil.MethodAttributes]'Private,Static,HideBySig', $ts.Single)
    $strArrType = [Mono.Cecil.ArrayType]::new($ts.String)
    $mParse.Parameters.Add([Mono.Cecil.ParameterDefinition]::new('parts',  [Mono.Cecil.ParameterAttributes]::None, $strArrType))
    $mParse.Parameters.Add([Mono.Cecil.ParameterDefinition]::new('idx',    [Mono.Cecil.ParameterAttributes]::None, $ts.Int32))
    $mParse.Parameters.Add([Mono.Cecil.ParameterDefinition]::new('defVal', [Mono.Cecil.ParameterAttributes]::None, $ts.Single))
    $mParse.Parameters.Add([Mono.Cecil.ParameterDefinition]::new('minVal', [Mono.Cecil.ParameterAttributes]::None, $ts.Single))
    $mParse.Parameters.Add([Mono.Cecil.ParameterDefinition]::new('maxVal', [Mono.Cecil.ParameterAttributes]::None, $ts.Single))
    $mParse.Body.InitLocals = $true
    $vParsed = [Mono.Cecil.Cil.VariableDefinition]::new($ts.Single)
    $mParse.Body.Variables.Add($vParsed)
    $tp.Methods.Add($mParse)
    $il = $mParse.Body.GetILProcessor()
    $retDef = $il.Create($ops::Ldarg_2)
    $il.Emit($ops::Ldarg_0); $il.Emit($ops::Brfalse, $retDef)
    $il.Emit($ops::Ldarg_1); $il.Emit($ops::Ldc_I4_0); $il.Emit($ops::Blt, $retDef)
    $il.Emit($ops::Ldarg_1); $il.Emit($ops::Ldarg_0); $il.Emit($ops::Ldlen); $il.Emit($ops::Conv_I4); $il.Emit($ops::Bge, $retDef)
    $il.Emit($ops::Ldarg_0); $il.Emit($ops::Ldarg_1); $il.Emit($ops::Ldelem_Ref); $il.Emit($ops::Dup)
    $lblNonNull = $il.Create($ops::Callvirt, $refStrTrim)
    $il.Emit($ops::Brtrue, $lblNonNull); $il.Emit($ops::Pop); $il.Emit($ops::Br, $retDef)
    $il.Append($lblNonNull)
    $il.Emit($ops::Ldc_I4, [int]167); $il.Emit($ops::Call, $refInvCulture); $il.Emit($ops::Ldloca, $vParsed); $il.Emit($ops::Call, $refTryParse)
    $il.Emit($ops::Brfalse, $retDef)
    $il.Emit($ops::Ldloc, $vParsed); $il.Emit($ops::Ldarg_3); $il.Emit($ops::Ldarg_S, $mParse.Parameters[4]); $il.Emit($ops::Call, $refClamp); $il.Emit($ops::Ret)
    $il.Append($retDef); $il.Emit($ops::Ret)

    # Helper builder: private static int ScaleIntByField(int px) -> (int)(px * field)
    function Add-ScaleIntHelper([string]$name, $scaleField) {
        $m = [Mono.Cecil.MethodDefinition]::new($name, [Mono.Cecil.MethodAttributes]'Private,Static,HideBySig', $ts.Int32)
        $m.Parameters.Add([Mono.Cecil.ParameterDefinition]::new('px', [Mono.Cecil.ParameterAttributes]::None, $ts.Int32))
        $tp.Methods.Add($m)
        $pil = $m.Body.GetILProcessor()
        $pil.Emit($ops::Ldarg_0)
        $pil.Emit($ops::Conv_R4)
        $pil.Emit($ops::Ldsfld, $scaleField)
        $pil.Emit($ops::Mul)
        $pil.Emit($ops::Conv_I4)
        $pil.Emit($ops::Ret)
        return $m
    }

    # Helper builder: adaptive font size (1..4) from a float scale field
    function Add-FontSizeHelper([string]$name, $scaleField, [single]$t2, [single]$t3, [single]$t4) {
        $m = [Mono.Cecil.MethodDefinition]::new($name, [Mono.Cecil.MethodAttributes]'Private,Static,HideBySig', $ts.Int32)
        $tp.Methods.Add($m)
        $fil = $m.Body.GetILProcessor()
        $c2 = $fil.Create($ops::Ldsfld, $scaleField)
        $c3 = $fil.Create($ops::Ldsfld, $scaleField)
        $r4 = $fil.Create($ops::Ldc_I4_4)
        $fil.Emit($ops::Ldsfld, $scaleField); $fil.Emit($ops::Ldc_R4, $t2); $fil.Emit($ops::Bge_Un, $c2); $fil.Emit($ops::Ldc_I4_1); $fil.Emit($ops::Ret)
        $fil.Append($c2); $fil.Emit($ops::Ldc_R4, $t3); $fil.Emit($ops::Bge_Un, $c3); $fil.Emit($ops::Ldc_I4_2); $fil.Emit($ops::Ret)
        $fil.Append($c3); $fil.Emit($ops::Ldc_R4, $t4); $fil.Emit($ops::Bge_Un, $r4); $fil.Emit($ops::Ldc_I4_3); $fil.Emit($ops::Ret)
        $fil.Append($r4); $fil.Emit($ops::Ret)
        return $m
    }

    # Helper builder: text color = Color.White * Math.Max(minAlpha, opacityField)
    function Add-TextTintHelper([string]$name, $opField, [single]$minA) {
        $m = [Mono.Cecil.MethodDefinition]::new($name, [Mono.Cecil.MethodAttributes]'Private,Static,HideBySig', $refColorWhite.ReturnType)
        $tp.Methods.Add($m)
        $til = $m.Body.GetILProcessor()
        $til.Emit($ops::Call, $refColorWhite)
        $til.Emit($ops::Ldsfld, $opField)
        $til.Emit($ops::Ldc_R4, $minA)
        $til.Emit($ops::Call, $refMaxSingle)
        $til.Emit($ops::Call, $refColorMul)
        $til.Emit($ops::Ret)
        return $m
    }

    $mScaleModal   = Add-ScaleIntHelper 'ScaleModalInt'   $fModalScale
    $mScaleDrawer  = Add-ScaleIntHelper 'ScaleDrawerInt'  $fDrawerScale
    $mScaleJoyInt  = Add-ScaleIntHelper 'ScaleJoyInt'     $fJoyScale
    $mMbFont       = Add-FontSizeHelper 'GetMenuBarFontSize' $fMbScale     0.88 1.30 1.75
    $mModalFont    = Add-FontSizeHelper 'GetModalFontSize'   $fModalScale  0.80 1.28 1.78
    $mDrawerFont   = Add-FontSizeHelper 'GetDrawerFontSize'  $fDrawerScale 0.85 1.30 1.78
    $mAbxyFont     = Add-FontSizeHelper 'GetAbxyFontSize'    $fAbxyScale   0.55 0.85 1.45
    $mTrigFont     = Add-FontSizeHelper 'GetTrigFontSize'    $fTrigScale   0.75 1.30 1.75
    $mMbTextTint   = Add-TextTintHelper 'GetMbTextColor'     $fMbOpacity   0.22
    $mAbxyTextTint = Add-TextTintHelper 'GetAbxyTextColor'   $fAbxyOpacity 0.14
    $mTrigTextTint = Add-TextTintHelper 'GetTrigTextColor'   $fTrigOpacity 0.14

    # 3. SyncMenuBarConfig(bool force)
    $mSync = [Mono.Cecil.MethodDefinition]::new('SyncMenuBarConfig', [Mono.Cecil.MethodAttributes]'Private,HideBySig', $ts.Void)
    $mSync.Parameters.Add([Mono.Cecil.ParameterDefinition]::new('force', [Mono.Cecil.ParameterAttributes]::None, $ts.Boolean))
    $mSync.Body.InitLocals = $true
    $vPath  = [Mono.Cecil.Cil.VariableDefinition]::new($ts.String)
    $vRaw   = [Mono.Cecil.Cil.VariableDefinition]::new($ts.String)
    $vParts = [Mono.Cecil.Cil.VariableDefinition]::new($strArrType)
    $mSync.Body.Variables.Add($vPath); $mSync.Body.Variables.Add($vRaw); $mSync.Body.Variables.Add($vParts)
    $tp.Methods.Add($mSync)

    $il = $mSync.Body.GetILProcessor()
    $lblAfterInit = $il.Create($ops::Ldarg_1)
    $lblDoPoll    = $il.Create($ops::Ldc_I4_5)
    $lblCatch     = $il.Create($ops::Pop)
    $lblEnd       = $il.Create($ops::Ret)

    $defaults = @(
        @($fMbScale,     1.00, 0.55, 2.20),
        @($fMbTopY,      24.0,  4.0, 160.0),
        @($fMbLeftX,     56.0,  8.0, 240.0),
        @($fMbGap,        8.0,  2.0,  22.0),
        @($fMbOpacity,   0.75, 0.10,  1.00),
        @($fAbxyScale,   1.00, 0.50,  2.20),
        @($fAbxySpacing, 1.00, 0.60,  1.80),
        @($fAbxyOpacity, 0.70, 0.05,  1.00),
        @($fTrigScale,   1.00, 0.50,  2.20),
        @($fTrigOpacity, 0.70, 0.05,  1.00),
        @($fJoyScale,    1.00, 0.50,  2.20),
        @($fJoyOpacity,  0.65, 0.05,  1.00),
        @($fModalScale,  1.55, 1.00,  2.00),
        @($fDrawerScale, 1.35, 0.80,  2.00)
    )

    $lblTryStart = $il.Create($ops::Ldsfld, $fInit)
    $il.Append($lblTryStart)
    $il.Emit($ops::Brtrue, $lblAfterInit)
    foreach ($entry in $defaults) {
        $il.Emit($ops::Ldc_R4, [single]$entry[1])
        $il.Emit($ops::Stsfld, $entry[0])
    }
    $il.Emit($ops::Ldc_I4_1); $il.Emit($ops::Stsfld, $fInit)

    # Poll every 8 frames when !force
    $il.Append($lblAfterInit)
    $il.Emit($ops::Brtrue, $lblDoPoll)
    $il.Emit($ops::Ldsfld, $fTick); $il.Emit($ops::Ldc_I4_1); $il.Emit($ops::Add); $il.Emit($ops::Dup); $il.Emit($ops::Stsfld, $fTick)
    $lblResetTick = $il.Create($ops::Ldc_I4_0)
    $il.Emit($ops::Ldc_I4_8); $il.Emit($ops::Bge, $lblResetTick)
    $il.Emit($ops::Leave, $lblEnd)
    $il.Append($lblResetTick); $il.Emit($ops::Stsfld, $fTick)

    # Read Documents/sdv_menubar.cfg
    $il.Append($lblDoPoll)
    $il.Emit($ops::Call, $refGetFolderPath); $il.Emit($ops::Ldstr, 'sdv_menubar.cfg'); $il.Emit($ops::Call, $refPathCombine); $il.Emit($ops::Stloc, $vPath)
    $lblRead = $il.Create($ops::Ldloc, $vPath)
    $il.Emit($ops::Ldloc, $vPath); $il.Emit($ops::Call, $refFileExists); $il.Emit($ops::Brtrue, $lblRead)
    $il.Emit($ops::Leave, $lblEnd)

    $il.Append($lblRead)
    $il.Emit($ops::Call, $refReadAllText); $il.Emit($ops::Stloc, $vRaw)
    $lblChanged = $il.Create($ops::Ldloc, $vRaw)
    $il.Emit($ops::Ldloc, $vRaw); $il.Emit($ops::Ldsfld, $fLastRaw); $il.Emit($ops::Ldc_I4_4); $il.Emit($ops::Call, $refStrEquals)
    $il.Emit($ops::Brfalse, $lblChanged); $il.Emit($ops::Leave, $lblEnd)

    $il.Append($lblChanged)
    $il.Emit($ops::Stsfld, $fLastRaw)
    $il.Emit($ops::Ldloc, $vRaw); $il.Emit($ops::Ldc_I4_S, [sbyte]44); $il.Emit($ops::Ldc_I4_0); $il.Emit($ops::Callvirt, $refStrSplit); $il.Emit($ops::Stloc, $vParts)

    for ($idx = 0; $idx -lt $defaults.Count; $idx++) {
        $entry = $defaults[$idx]
        $il.Emit($ops::Ldloc, $vParts)
        $il.Emit($ops::Ldc_I4, [int]$idx)
        $il.Emit($ops::Ldc_R4, [single]$entry[1])
        $il.Emit($ops::Ldc_R4, [single]$entry[2])
        $il.Emit($ops::Ldc_R4, [single]$entry[3])
        $il.Emit($ops::Call, $mParse)
        $il.Emit($ops::Stsfld, $entry[0])
    }

    $lblLeaveTry = $il.Create($ops::Leave, $lblEnd)
    $il.Emit($ops::Ldarg_1); $il.Emit($ops::Brtrue, $lblLeaveTry)
    $il.Emit($ops::Ldarg_0); $il.Emit($ops::Call, $mGetSettings); $il.Emit($ops::Callvirt, $mSettingsLoad)
    $il.Emit($ops::Ldarg_0); $il.Emit($ops::Ldarg_0); $il.Emit($ops::Ldfld, $fVpW); $il.Emit($ops::Ldarg_0); $il.Emit($ops::Ldfld, $fVpH); $il.Emit($ops::Call, $mUpdateLayout)
    $il.Append($lblLeaveTry)
    $il.Append($lblCatch); $il.Emit($ops::Leave, $lblEnd)
    $il.Append($lblEnd)

    $eh = [Mono.Cecil.Cil.ExceptionHandler]::new([Mono.Cecil.Cil.ExceptionHandlerType]::Catch)
    $eh.CatchType = $refException; $eh.TryStart = $lblTryStart; $eh.TryEnd = $lblCatch; $eh.HandlerStart = $lblCatch; $eh.HandlerEnd = $lblEnd
    $mSync.Body.ExceptionHandlers.Add($eh)

    # 4. Patch Update(GameTime): call SyncMenuBarConfig(false)
    Expand-ShortBranches $mUpdate.Body
    $upIl = $mUpdate.Body.GetILProcessor()
    $firstUp = $mUpdate.Body.Instructions[0]
    $upIl.InsertBefore($firstUp, $upIl.Create($ops::Ldarg_0))
    $upIl.InsertBefore($firstUp, $upIl.Create($ops::Ldc_I4_0))
    $upIl.InsertBefore($firstUp, $upIl.Create($ops::Call, $mSync))

    # 5. Patch UpdateLayout(int width, int height)
    Expand-ShortBranches $mUpdateLayout.Body
    $ulIl = $mUpdateLayout.Body.GetILProcessor()
    $firstUl = $mUpdateLayout.Body.Instructions[0]
    $ulIl.InsertBefore($firstUl, $ulIl.Create($ops::Ldsfld, $fInit))
    $ulIl.InsertBefore($firstUl, $ulIl.Create($ops::Brtrue, $firstUl))
    $ulIl.InsertBefore($firstUl, $ulIl.Create($ops::Ldarg_0))
    $ulIl.InsertBefore($firstUl, $ulIl.Create($ops::Ldc_I4_1))
    $ulIl.InsertBefore($firstUl, $ulIl.Create($ops::Call, $mSync))

    function Mul-After($ilProc, $insNode, $scaleFld) {
        $m1 = $ilProc.Create($ops::Ldsfld, $scaleFld)
        $m2 = $ilProc.Create($ops::Mul)
        $ilProc.InsertAfter($insNode, $m1)
        $ilProc.InsertAfter($m1, $m2)
        return $m2
    }

    function Scale-FloatLoad($ilProc, $ldcIns, [single]$newBase, $scaleFld) {
        $ldcIns.Operand = $newBase
        return (Mul-After $ilProc $ldcIns $scaleFld)
    }

    $ins = $mUpdateLayout.Body.Instructions

    # 5a. Scale btnSize (58f -> *_abxyScale), _joystickRadius (75f -> *_joyScale), spacing (58f -> *_abxyScale * _abxySpacing)
    $iBtnSize = $null; $iJoyRad = $null; $iSpacing = $null
    for ($k = 15; $k -lt 80; $k++) {
        if ($ins[$k].OpCode.Code -eq 'Ldc_R4' -and $ins[$k].Operand -eq [single]58 -and -not $iBtnSize) { $iBtnSize = $ins[$k] }
        elseif ($ins[$k].OpCode.Code -eq 'Ldc_R4' -and $ins[$k].Operand -eq [single]75 -and -not $iJoyRad) { $iJoyRad = $ins[$k] }
        elseif ($ins[$k].OpCode.Code -eq 'Ldc_R4' -and $ins[$k].Operand -eq [single]58 -and $iJoyRad -and -not $iSpacing) { $iSpacing = $ins[$k] }
    }
    if (-not $iBtnSize -or -not $iJoyRad -or -not $iSpacing) { throw "Missing joystick/ABXY size anchors" }
    [void](Mul-After $ulIl $iBtnSize $fAbxyScale)
    [void](Mul-After $ulIl $iJoyRad  $fJoyScale)
    $afterSp = Mul-After $ulIl $iSpacing $fAbxyScale
    [void](Mul-After $ulIl $afterSp  $fAbxySpacing)

    # 5b. Scale LT/RT trigW (86f) & trigH (44f) in mod
    if ($isMod) {
        $iTrigW = $null; $iTrigH = $null
        for ($k = 240; $k -lt 340; $k++) {
            if ($ins[$k].OpCode.Code -eq 'Ldc_R4' -and $ins[$k].Operand -eq [single]86 -and -not $iTrigW) { $iTrigW = $ins[$k] }
            elseif ($ins[$k].OpCode.Code -eq 'Ldc_R4' -and $ins[$k].Operand -eq [single]44 -and $iTrigW -and -not $iTrigH) { $iTrigH = $ins[$k]; break }
        }
        if (-not $iTrigW -or -not $iTrigH) { throw "Missing LT/RT size anchors" }
        [void](Mul-After $ulIl $iTrigW $fTrigScale)
        [void](Mul-After $ulIl $iTrigH $fTrigScale)
    }

    # 5c. Top Menu Bar + Inventory Bar + Hotkey Drawer + Key Picker + Touch Settings Modal
    $anchorField = if ($isMod) { '_btnRTRect' } else { '_btnBRect' }
    $anchorIdx = -1
    for ($k = 0; $k -lt $ins.Count; $k++) {
        if ($ins[$k].OpCode.Code -eq 'Stfld' -and $ins[$k].Operand.Name -eq $anchorField) { $anchorIdx = $k; break }
    }
    if ($anchorIdx -lt 0) { throw "Missing $anchorField" }

    $iTopY = $ins[$anchorIdx + 1]; $iTopH = $ins[$anchorIdx + 3]; $iTopW = $ins[$anchorIdx + 5]
    if ($iTopY.OpCode.Code -ne 'Ldc_R4' -or $iTopY.Operand -ne [single]24) { throw "Unexpected topY: $iTopY" }
    if ($iTopH.OpCode.Code -ne 'Ldc_R4' -or $iTopH.Operand -ne [single]42) { throw "Unexpected topBtnH: $iTopH" }
    if ($iTopW.OpCode.Code -ne 'Ldc_R4' -or $iTopW.Operand -ne [single]64) { throw "Unexpected topBtnW: $iTopW" }
    $iTopY.OpCode = $ops::Ldsfld; $iTopY.Operand = $fMbTopY
    [void](Scale-FloatLoad $ulIl $iTopH ([single]42.0) $fMbScale)
    [void](Scale-FloatLoad $ulIl $iTopW ([single]64.0) $fMbScale)

    if ($isMod) {
        $curXIns = $null
        for ($k = $anchorIdx; $k -lt $anchorIdx + 20; $k++) {
            if ($ins[$k].OpCode.Code -eq 'Ldc_R4' -and $ins[$k].Operand -eq [single]56) { $curXIns = $ins[$k]; break }
        }
        $curXIns.OpCode = $ops::Ldsfld; $curXIns.Operand = $fMbLeftX

        $invEndIdx = -1
        for ($k = $anchorIdx; $k -lt $ins.Count; $k++) {
            if ($ins[$k].OpCode.Code -eq 'Stfld' -and $ins[$k].Operand.Name -eq '_btnInventoryXferRect') { $invEndIdx = $k; break }
        }

        $menuWIns=$null; $modsWIns=$null; $drawerXIns=$null; $invHIns=$null; $invWIns=$null; $invXIns=$null; $invXferWIns=$null
        $gapInsList = @()
        for ($k = $anchorIdx + 1; $k -lt $invEndIdx; $k++) {
            $cur = $ins[$k]
            if ($cur.OpCode.Code -eq 'Ldc_R4' -and $cur.Operand -eq [single]8) { $gapInsList += $cur }
            elseif ($cur.OpCode.Code -eq 'Ldc_R4' -and $cur.Operand -eq [single]64 -and -not $menuWIns) {
                for ($j = $k; $j -lt [Math]::Min($k + 25, $ins.Count); $j++) { if ($ins[$j].OpCode.Code -eq 'Stfld' -and $ins[$j].Operand.Name -eq '_btnMenuRect') { $menuWIns = $cur; break } }
            } elseif ($cur.OpCode.Code -eq 'Ldc_R4' -and $cur.Operand -eq [single]68 -and -not $modsWIns) {
                for ($j = $k; $j -lt [Math]::Min($k + 25, $ins.Count); $j++) { if ($ins[$j].OpCode.Code -eq 'Stfld' -and $ins[$j].Operand.Name -eq '_btnHotkeyDrawerToggleRect') { $modsWIns = $cur; break } }
            } elseif ($cur.OpCode.Code -eq 'Ldc_I4_S' -and [int]$cur.Operand -eq 40 -and -not $drawerXIns) {
                if ($k -gt 0 -and "$($ins[$k-1].Operand)" -match 'set_IsHotkeyDrawerOpen') { $drawerXIns = $cur }
            } elseif ($cur.OpCode.Code -eq 'Ldc_R4' -and $cur.Operand -eq [single]32 -and -not $invHIns) {
                for ($j = $k; $j -lt [Math]::Min($k + 30, $ins.Count); $j++) { if ($ins[$j].OpCode.Code -eq 'Stfld' -and $ins[$j].Operand.Name -eq '_btnInventoryOneRect') { $invHIns = $cur; break } }
            } elseif ($cur.OpCode.Code -eq 'Ldc_R4' -and $cur.Operand -eq [single]54 -and $invHIns -and -not $invWIns) { $invWIns = $cur }
            elseif ($cur.OpCode.Code -eq 'Ldc_R4' -and $cur.Operand -eq [single]40 -and $invWIns -and -not $invXIns) { $invXIns = $cur }
            elseif ($cur.OpCode.Code -eq 'Ldc_R4' -and $cur.Operand -eq [single]74 -and $invXIns -and -not $invXferWIns) { $invXferWIns = $cur }
        }
        foreach ($g in $gapInsList) { $g.OpCode = $ops::Ldsfld; $g.Operand = $fMbGap }
        [void](Scale-FloatLoad $ulIl $menuWIns    ([single]68.0) $fMbScale)
        [void](Scale-FloatLoad $ulIl $modsWIns    ([single]72.0) $fMbScale)
        $drawerXIns.OpCode = $ops::Ldsfld; $drawerXIns.Operand = $fMbLeftX
        $ulIl.InsertAfter($drawerXIns, $ulIl.Create($ops::Conv_I4))
        [void](Scale-FloatLoad $ulIl $invHIns     ([single]32.0) $fMbScale)
        [void](Scale-FloatLoad $ulIl $invWIns     ([single]54.0) $fMbScale)
        $invXIns.OpCode = $ops::Ldsfld; $invXIns.Operand = $fMbLeftX
        [void](Scale-FloatLoad $ulIl $invXferWIns ([single]74.0) $fMbScale)

        # 5d. Scale Hotkey Drawer metrics (between drawerXIns and _btnHotkeyCloseDrawerRect) using ScaleDrawerInt!
        $ins = $mUpdateLayout.Body.Instructions
        $dStart = $ins.IndexOf($drawerXIns) + 1
        $dEnd = -1
        for ($k = $dStart; $k -lt $ins.Count; $k++) {
            if ($ins[$k].OpCode.Code -eq 'Stfld' -and $ins[$k].Operand.Name -eq '_btnHotkeyCloseDrawerRect') { $dEnd = $k; break }
        }
        # Only scale pixel size constants (skip loop counters 0,1,2,3,4,8 where used for slot index % 4 / < 8!)
        # Safe pixel literals in HotkeyDrawer section: 92(slotW), 44(slotH), 12(pad), 34(qsH), 30(footerH), 138, 146, 148, 302
        $drawerToScale = @()
        for ($k = $dStart; $k -lt $dEnd; $k++) {
            $val = Get-IntLiteralValue $ins[$k]
            if ($null -ne $val -and $val -in @(92, 44, 12, 34, 30, 138, 146, 148, 302)) {
                $drawerToScale += $ins[$k]
            }
        }
        foreach ($node in $drawerToScale) { $ulIl.InsertAfter($node, $ulIl.Create($ops::Call, $mScaleDrawer)) }
        Write-Output "  Mod HotkeyDrawer metrics scaled: $($drawerToScale.Count)"

        # 5e. Scale KeyPicker Modal (720,480,38,30,44,34) + DoneEdit (60,120,44) + SettingsModal (680,530,38,30,48,37,260,40,28,110,170,80,88,176,56,32,70,44,34,42) via ScaleModalInt!
        $ins = $mUpdateLayout.Body.Instructions
        $sStart = $dEnd + 1
        $modalToScale = @()
        for ($k = $sStart; $k -lt $ins.Count; $k++) {
            $val = Get-IntLiteralValue $ins[$k]
            # Exclude small row indices (0..7) and screen edge clamps (40/30 right before sub+Min) — wait:
            # 40 before `sub; Math.Min` is screen margin (don't scale!), whereas 40 before rect ctor is button width (DO scale!)
            if ($null -eq $val) { continue }
            $nextOp = if ($k + 1 -lt $ins.Count) { $ins[$k+1].OpCode.Code.ToString() } else { '' }
            $prevOp = if ($k -gt 0) { $ins[$k-1].OpCode.Code.ToString() } else { '' }
            # Skip screen margin subtractions `ldarg.1; ldc 40; sub` or `ldarg.2; ldc 30; sub`
            if (($val -eq 40 -or $val -eq 30) -and $prevOp -in @('Ldarg_1','Ldarg_2') -and $nextOp -eq 'Sub') { continue }
            if ($val -in @(720,480,680,530,260,176,170,120,110,88,80,70,60,56,48,44,42,40,38,37,34,32,30,28)) {
                $modalToScale += $ins[$k]
            }
        }
        foreach ($node in $modalToScale) { $ulIl.InsertAfter($node, $ulIl.Create($ops::Call, $mScaleModal)) }
        Write-Output "  Mod SettingsModal & KeyPicker metrics scaled: $($modalToScale.Count)"
    } else {
        # Fallback host pad
        $rightIns = $null
        for ($k = $anchorIdx; $k -lt $anchorIdx + 20; $k++) {
            if ($ins[$k].OpCode.Code -eq 'Ldc_R4' -and $ins[$k].Operand -eq [single]56) { $rightIns = $ins[$k]; break }
        }
        $rightIns.OpCode = $ops::Ldsfld; $rightIns.Operand = $fMbLeftX
        $endIdx = -1
        for ($k = $anchorIdx; $k -lt $ins.Count; $k++) {
            if ($ins[$k].OpCode.Code -eq 'Stfld' -and $ins[$k].Operand.Name -eq '_btnDoneEditRect') { $endIdx = $k; break }
        }
        $menuWIns = $null; $gapInsList = @()
        for ($k = $anchorIdx + 1; $k -lt $endIdx; $k++) {
            $cur = $ins[$k]
            if ($cur.OpCode.Code -eq 'Ldc_R4' -and $cur.Operand -eq [single]8) { $gapInsList += $cur }
            elseif ($cur.OpCode.Code -eq 'Ldc_R4' -and $cur.Operand -eq [single]64 -and -not $menuWIns) {
                for ($j = $k; $j -lt [Math]::Min($k + 25, $ins.Count); $j++) { if ($ins[$j].OpCode.Code -eq 'Stfld' -and $ins[$j].Operand.Name -eq '_btnMenuRect') { $menuWIns = $cur; break } }
            }
        }
        foreach ($g in $gapInsList) { $g.OpCode = $ops::Ldsfld; $g.Operand = $fMbGap }
        [void](Scale-FloatLoad $ulIl $menuWIns ([single]68.0) $fMbScale)
    }

    # 6. Patch Draw(SpriteBatch): independent opacity (NO 0.50/0.75/0.85 floor!), adaptive font sizes, scaled joystick thumb!
    Expand-ShortBranches $mDraw.Body
    $drIl = $mDraw.Body.GetILProcessor()
    $dins = $mDraw.Body.Instructions

    # Helper: replace `ldc.r4 <floor>; ldloc.0; call Math.Max(float,float)` with `ldsfld <opField>`
    function Patch-AlphaPattern($dinsArr, [int]$start, [int]$end, [single]$floorVal, $opField) {
        $hits = 0
        for ($k = $start; $k -lt [Math]::Min($end, $dinsArr.Count - 2); $k++) {
            if ($dinsArr[$k].OpCode.Code -eq 'Ldc_R4' -and $dinsArr[$k].Operand -eq $floorVal -and
                $dinsArr[$k+1].OpCode.Code -eq 'Ldloc_0' -and "$($dinsArr[$k+2].Operand)" -match 'Math::Max\(System\.Single,System\.Single\)') {
                $dinsArr[$k].OpCode = $ops::Ldsfld; $dinsArr[$k].Operand = $opField
                $dinsArr[$k+1].OpCode = $ops::Nop;  $dinsArr[$k+1].Operand = $null
                $dinsArr[$k+2].OpCode = $ops::Nop;  $dinsArr[$k+2].Operand = $null
                $hits++
            }
        }
        return $hits
    }

    if ($isMod) {
        # Locate boundaries in Draw
        $idxIsVis = -1; $idxJoyBase = -1; $idxBtnA = -1
        for ($k = 0; $k -lt $dins.Count; $k++) {
            if ($idxIsVis -lt 0 -and "$($dins[$k].Operand)" -match '_btnLTRect') { $idxIsVis = $k - 35 }
            if ($idxJoyBase -lt 0 -and $dins[$k].OpCode.Code -eq 'Ldfld' -and $dins[$k].Operand.Name -eq '_joystickBaseRect') { $idxJoyBase = $k }
            if ($idxBtnA -lt 0 -and $dins[$k].OpCode.Code -eq 'Ldfld' -and $dins[$k].Operand.Name -eq '_btnARect') { $idxBtnA = $k }
        }
        if ($idxIsVis -lt 0 -or $idxJoyBase -lt 0 -or $idxBtnA -lt 0) { throw "Missing Draw section anchors" }

        # Menu Bar opacity (0.5f and 0.6f floors -> _mbOpacity)
        $hMb1 = Patch-AlphaPattern $dins 0 $idxIsVis ([single]0.5) $fMbOpacity
        $hMb2 = Patch-AlphaPattern $dins 0 $idxIsVis ([single]0.6) $fMbOpacity
        # LT/RT opacity (0.75f floor -> _trigOpacity)
        $hTrig = Patch-AlphaPattern $dins $idxIsVis $idxJoyBase ([single]0.75) $fTrigOpacity
        # Joystick base (`ldloc.0` -> `_joyOpacity`) & thumb (`0.85, ldloc.0, Max` -> `_joyOpacity`)
        $hJoyThumb = Patch-AlphaPattern $dins $idxJoyBase $idxBtnA ([single]0.85) $fJoyOpacity
        $hJoyBase = 0
        for ($k = $idxJoyBase; $k -lt $idxBtnA; $k++) {
            if ($dins[$k].OpCode.Code -eq 'Ldloc_0') {
                $dins[$k].OpCode = $ops::Ldsfld; $dins[$k].Operand = $fJoyOpacity; $hJoyBase++
            }
        }
        # Scale joystick thumb 28f / 56 by _joyScale!
        $thumb28 = @(); $thumb56 = @()
        for ($k = $idxJoyBase; $k -lt $idxBtnA; $k++) {
            if ($dins[$k].OpCode.Code -eq 'Ldc_R4' -and $dins[$k].Operand -eq [single]28) { $thumb28 += $dins[$k] }
            elseif ($dins[$k].OpCode.Code -eq 'Ldc_I4_S' -and [int]$dins[$k].Operand -eq 56) { $thumb56 += $dins[$k] }
        }
        foreach ($n in $thumb28) { [void](Mul-After $drIl $n $fJoyScale) }
        foreach ($n in $thumb56) { $drIl.InsertAfter($n, $drIl.Create($ops::Call, $mScaleJoyInt)) }

        # Re-read indices after thumb inserts & patch ABXY opacity (0.75f floor -> _abxyOpacity)
        $dins = $mDraw.Body.Instructions
        $idxBtnA = -1
        for ($k = 0; $k -lt $dins.Count; $k++) { if ($dins[$k].OpCode.Code -eq 'Ldfld' -and $dins[$k].Operand.Name -eq '_btnARect') { $idxBtnA = $k; break } }
        $hAbxy = Patch-AlphaPattern $dins $idxBtnA $dins.Count ([single]0.75) $fAbxyOpacity

        # Adaptive font size + tinted text color for Menu Bar, Inventory Bar, LT/RT, and A/B/X/Y!
        $mbRects   = @('_btnToggleRect','_btnSettingsRect','_btnKeyboardRect','_btnMenuRect','_btnHotkeyDrawerToggleRect','_btnInventoryOneRect','_btnInventoryHalfRect','_btnInventoryXferRect')
        $trigRects = @('_btnLTRect','_btnRTRect')
        $abxyRects = @('_btnARect','_btnXRect','_btnYRect','_btnBRect')
        $fPatched = 0
        for ($k = 2; $k -lt $dins.Count; $k++) {
            if ("$($dins[$k].Operand)" -notmatch '::DrawButton\(') { continue }
            $fld = ''
            for ($j = $k - 1; $j -ge [Math]::Max(0, $k - 24); $j--) {
                if ($dins[$j].OpCode.Code -eq 'Ldfld' -and "$($dins[$j].Operand)" -match '_btn') { $fld = $dins[$j].Operand.Name; break }
            }
            if ($fld -in $mbRects) {
                $dins[$k-1].OpCode = $ops::Call; $dins[$k-1].Operand = $mMbFont
                if ($dins[$k-2].Operand -eq $refColorWhite) { $dins[$k-2].Operand = $mMbTextTint }
                $fPatched++
            } elseif ($fld -in $trigRects) {
                $dins[$k-1].OpCode = $ops::Call; $dins[$k-1].Operand = $mTrigFont
                if ($dins[$k-2].Operand -eq $refColorWhite) { $dins[$k-2].Operand = $mTrigTextTint }
                $fPatched++
            } elseif ($fld -in $abxyRects) {
                $dins[$k-1].OpCode = $ops::Call; $dins[$k-1].Operand = $mAbxyFont
                if ($dins[$k-2].Operand -eq $refColorWhite) { $dins[$k-2].Operand = $mAbxyTextTint }
                $fPatched++
            }
        }
        Write-Output "  Mod Draw patched: mbOp=$($hMb1+$hMb2) trigOp=$hTrig joyOp=$hJoyBase/$hJoyThumb abxyOp=$hAbxy font+tint=$fPatched"

        # 7. Patch DrawSettingsModal (scale offsets/heights via ScaleModalInt + font via GetModalFontSize!)
        $mDrawSet = $tp.Methods | Where-Object Name -eq 'DrawSettingsModal'
        Expand-ShortBranches $mDrawSet.Body
        $dsIl = $mDrawSet.Body.GetILProcessor()
        $dsIns = $mDrawSet.Body.Instructions
        $dsScaled = 0; $dsFonts = 0
        $dsNodesToScale = @()
        for ($k = 0; $k -lt $dsIns.Count; $k++) {
            $cur = $dsIns[$k]
            # Font scale 2 before DrawString / DrawCenteredString / DrawButton
            if ($cur.OpCode.Code -eq 'Ldc_I4_2' -and $k + 1 -lt $dsIns.Count -and
                "$($dsIns[$k+1].Operand)" -match '::DrawString\(|::DrawCenteredString\(|::DrawButton\(') {
                $cur.OpCode = $ops::Call; $cur.Operand = $mModalFont; $dsFonts++
                continue
            }
            $val = Get-IntLiteralValue $cur
            if ($null -eq $val) { continue }
            $next = if ($k + 1 -lt $dsIns.Count) { $dsIns[$k+1] } else { $null }
            $prev = if ($k -gt 0) { $dsIns[$k-1] } else { $null }
            # Scale modal layout constants: 20(titleX), 14(titleY), 24(titleH), 28(labelX & valueH), 48(startY), 37(rowSpacing), 6(labelYOffset)
            if (($val -eq 20 -and $prev -and "$($prev.Operand)" -match 'get_Left') -or
                ($val -eq 14 -and $prev -and "$($prev.Operand)" -match 'get_Top') -or
                ($val -eq 24 -and $next -and "$($next.Operand)" -match 'Rectangle::\.ctor') -or
                ($val -eq 28 -and $prev -and "$($prev.Operand)" -match 'get_Left') -or
                ($val -eq 48 -and $prev -and "$($prev.Operand)" -match 'get_Top') -or
                ($val -eq 37 -and $next -and $next.OpCode.Code -eq 'Stloc_3') -or
                ($val -eq 28 -and $next -and "$($next.Operand)" -match 'Rectangle::\.ctor') -or
                ($val -eq 6  -and $next -and $next.OpCode.Code -eq 'Add' -and $k + 2 -lt $dsIns.Count -and $dsIns[$k+2].OpCode.Code -eq 'Conv_R4')) {
                $dsNodesToScale += $cur
            }
        }
        foreach ($node in $dsNodesToScale) { $dsIl.InsertAfter($node, $dsIl.Create($ops::Call, $mScaleModal)); $dsScaled++ }
        Write-Output "  Mod DrawSettingsModal patched: offsets=$dsScaled fonts=$dsFonts"

        # 8. Patch DrawHotkeyDrawer (scale slot top/bot half heights via ScaleDrawerInt + font via GetDrawerFontSize!)
        $mDrawHot = $tp.Methods | Where-Object Name -eq 'DrawHotkeyDrawer'
        Expand-ShortBranches $mDrawHot.Body
        $dhIl = $mDrawHot.Body.GetILProcessor()
        $dhIns = $mDrawHot.Body.Instructions
        $dhFonts = 0; $dhScaled = 0
        $dhNodes = @()
        for ($k = 0; $k -lt $dhIns.Count; $k++) {
            $cur = $dhIns[$k]
            if ($cur.OpCode.Code -eq 'Ldc_I4_2' -and $k + 1 -lt $dhIns.Count -and
                "$($dhIns[$k+1].Operand)" -match '::DrawCenteredString\(|::DrawButton\(') {
                $cur.OpCode = $ops::Call; $cur.Operand = $mDrawerFont; $dhFonts++
                continue
            }
            $val = Get-IntLiteralValue $cur
            if ($null -eq $val) { continue }
            $next = if ($k + 1 -lt $dhIns.Count) { $dhIns[$k+1] } else { $null }
            $prev = if ($k -gt 0) { $dhIns[$k-1] } else { $null }
            if (($val -in @(3, 22) -and $prev -and "$($prev.Operand)" -match 'Rectangle::Y' -and $next -and $next.OpCode.Code -eq 'Add') -or
                ($val -in @(20, 18) -and $next -and "$($next.Operand)" -match 'Rectangle::\.ctor')) {
                $dhNodes += $cur
            }
        }
        foreach ($node in $dhNodes) { $dhIl.InsertAfter($node, $dhIl.Create($ops::Call, $mScaleDrawer)); $dhScaled++ }
        Write-Output "  Mod DrawHotkeyDrawer patched: slotHalves=$dhScaled fonts=$dhFonts"
    } else {
        # Fallback host pad Draw: adaptive menu bar font + opacity
        $hMb = Patch-AlphaPattern $dins 0 130 ([single]0.5) $fMbOpacity
        $mbRects = @('_btnToggleRect','_btnSettingsRect','_btnKeyboardRect','_btnMenuRect')
        $fPatched = 0
        for ($k = 2; $k -lt $dins.Count; $k++) {
            if ("$($dins[$k].Operand)" -notmatch '::DrawButton\(') { continue }
            $fld = ''
            for ($j = $k - 1; $j -ge [Math]::Max(0, $k - 22); $j--) {
                if ($dins[$j].OpCode.Code -eq 'Ldfld' -and "$($dins[$j].Operand)" -match '_btn') { $fld = $dins[$j].Operand.Name; break }
            }
            if ($fld -in $mbRects) {
                $dins[$k-1].OpCode = $ops::Call; $dins[$k-1].Operand = $mMbFont
                if ($dins[$k-2].Operand -eq $refColorWhite) { $dins[$k-2].Operand = $mMbTextTint }
                $fPatched++
            }
        }
        Write-Output "  Host Draw patched: mbOp=$hMb fonts=$fPatched"
    }
}

function Verify-Assembly($path) {
    $a = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
    try {
        $tp = $a.MainModule.Types | Where-Object Name -eq 'TouchVirtualPad'
        foreach ($m in $tp.Methods | Where-Object HasBody) {
            $set = [System.Collections.Generic.HashSet[object]]::new()
            foreach ($i in $m.Body.Instructions) { [void]$set.Add($i) }
            foreach ($i in $m.Body.Instructions) {
                if ($i.OpCode.OperandType -in @('InlineBrTarget','ShortInlineBrTarget')) {
                    if (-not $i.Operand -or -not $set.Contains($i.Operand)) { throw "Broken branch in $($a.Name.Name)::$($m.Name) at $i" }
                }
            }
            foreach ($eh in $m.Body.ExceptionHandlers) {
                foreach ($pt in @($eh.TryStart, $eh.TryEnd, $eh.HandlerStart, $eh.HandlerEnd)) {
                    if ($pt -and -not $set.Contains($pt)) { throw "Broken EH in $($a.Name.Name)::$($m.Name)" }
                }
            }
        }
        Write-Output "  Verified IL integrity of $(Split-Path -Leaf $path) OK"
    } finally { $a.Dispose() }
}

$outTouch = Join-Path $d 'menubar-touch.dll'
$outHost  = Join-Path $d 'menubar-host.dll'

$touch = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $d 'clean-touch.dll'))
try { Add-FullUiCustomization $touch $true; $touch.Write($outTouch) } finally { $touch.Dispose() }

$hostAsm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $d 'clean-host.dll'))
try {
    Add-FullUiCustomization $hostAsm $false
    $resources = $hostAsm.MainModule.Resources
    $old = $resources | Where-Object Name -eq 'SDViOS.Resources.Mods.SDViOSTouchControls.SDViOSTouchControls.dll'
    if (-not $old) { throw 'Missing embedded SDViOSTouchControls.dll resource' }
    $idx = $resources.IndexOf($old)
    $resources[$idx] = [Mono.Cecil.EmbeddedResource]::new($old.Name, $old.Attributes, [IO.File]::ReadAllBytes($outTouch))
    $hostAsm.Write($outHost)
} finally { $hostAsm.Dispose() }

Verify-Assembly $outTouch
Verify-Assembly $outHost
Get-Item -LiteralPath $outTouch, $outHost | Select-Object Name, Length | Format-Table -AutoSize
