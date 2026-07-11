# ponytail: self-check for the fan/preset fix. Fails loudly if any of the
# logical invariants introduced by this change are violated. Run after every
# follow-up edit to this area. No framework, no fixtures — one runnable check.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$fs   = Get-Content (Join-Path $root 'Services\FanService.cs') -Raw
$fp   = Get-Content (Join-Path $root 'Pages\FanPage.xaml.cs') -Raw
$fx   = Get-Content (Join-Path $root 'Pages\FanPage.xaml') -Raw
$fails = New-Object System.Collections.Generic.List[string]

function Assert([bool]$cond, [string]$msg) { if (-not $cond) { $fails.Add($msg) } }

# ── FanService: built-in preset keys now map to the existing curve columns ──
Assert -cond ([bool]($fs -match '"LightUse"'))          -msg 'FanService: LightUse missing from GetDefaultPresetCurve'
Assert -cond ([bool]($fs -match '"Extreme"'))           -msg 'FanService: Extreme missing from GetDefaultPresetCurve'
Assert -cond (-not ([bool]($fs -match 'balanced\)')))    -msg 'FanService: "balanced" still used as a case label (no longer exists as a preset)'

# ── FanPage.cs: dead array removed, default key aligned with ConfigService.Preset ──
Assert -cond (-not ([bool]($fp -match 'static readonly string\[\] PresetKeys'))) -msg 'FanPage: PresetKeys dead array still present'
Assert -cond (-not ([bool]($fp -match '_currentPresetKey = "balanced"')))        -msg 'FanPage: default _currentPresetKey still "balanced"'
Assert -cond ([bool]($fp -match '_currentPresetKey = "GpuPriority"'))             -msg 'FanPage: default _currentPresetKey not "GpuPriority"'

# ── FanPage.cs/xaml: 3-button handlers removed ──
Assert -cond (-not ([bool]($fp -match 'btnFanSave_Click')))  -msg 'FanPage.cs: btnFanSave_Click handler still present'
Assert -cond (-not ([bool]($fp -match 'btnFanLoad_Click')))  -msg 'FanPage.cs: btnFanLoad_Click handler still present'
Assert -cond (-not ([bool]($fp -match 'btnFanUndo_Click')))  -msg 'FanPage.cs: btnFanUndo_Click handler still present'
Assert -cond (-not ([bool]($fx -match 'btnFanSave'))) -msg 'FanPage.xaml: save button still present'
Assert -cond (-not ([bool]($fx -match 'btnFanUndo'))) -msg 'FanPage.xaml: undo button still present'
Assert -cond (-not ([bool]($fx -match 'btnFanLoad'))) -msg 'FanPage.xaml: load button still present'

# ── FanMode_SelectionChanged: no BeginInvoke, has custom-preset writeback ──
$mi = $fp.IndexOf('void FanMode_SelectionChanged(')
if ($mi -lt 0) { $fails.Add('FanMode_SelectionChanged not found') } else {
  $oi = $fp.IndexOf('{', $mi)
  if ($oi -ge 0) {
    $d = 0; $ei = -1
    for ($i = $oi; $i -lt $fp.Length; $i++) {
      if ($fp[$i] -eq '{') { $d++ }
      elseif ($fp[$i] -eq '}') { $d--; if ($d -eq 0) { $ei = $i; break } }
    }
    if ($ei -gt $oi) {
      $body = $fp.Substring($mi, $ei - $mi + 1)
      Assert -cond (-not ([bool]($body -match 'Dispatcher\.BeginInvoke'))) -msg 'FanMode_SelectionChanged: still has BeginInvoke deferral'
      Assert -cond ([bool]($body -match 'SaveCustomPreset')) -msg 'FanMode_SelectionChanged: missing custom-preset writeback'
    } else { $fails.Add('FanMode_SelectionChanged brace mismatch') }
  } else { $fails.Add('FanMode_SelectionChanged opening brace not found') }
}

# ── FanRpm events: writeback present ──
Assert -cond ([bool]($fp -match 'SaveCustomPreset\(ConfigService\.Preset\)')) -msg 'FanPage: fan rpm events missing custom-preset writeback'

# ── OnPresetChanged: _currentPresetKey before guard, LoadConfigState present ──
$si = $fp.IndexOf('void OnPresetChanged(string preset)')
$bodyOk = $false; $keyOk = $false; $loadOk = $false
if ($si -ge 0) {
  $oi = $fp.IndexOf('{', $si)
  if ($oi -ge 0) {
    $d = 0; $ei = -1
    for ($i = $oi; $i -lt $fp.Length; $i++) {
      if ($fp[$i] -eq '{') { $d++ }
      elseif ($fp[$i] -eq '}') { $d--; if ($d -eq 0) { $ei = $i; break } }
    }
    if ($ei -gt $oi) {
      $body = $fp.Substring($si, $ei - $si + 1)
      $bodyOk = $true
      $keyIdx = $body.IndexOf('_currentPresetKey = preset')
      $guardIdx = $body.IndexOf('if (!IsLoaded')
      $keyOk  = ($keyIdx -ge 0) -and ($guardIdx -gt $keyIdx)
      $loadOk = [bool]($body -match 'LoadConfigState\(\)')
    }
  }
}
Assert -cond $bodyOk -msg 'OnPresetChanged body not found'
Assert -cond $keyOk  -msg 'OnPresetChanged: _currentPresetKey = preset must come before IsLoaded guard'
Assert -cond $loadOk -msg 'OnPresetChanged: missing LoadConfigState call (preset -> mode UI sync)'

if ($fails.Count -eq 0) {
  Write-Output 'OK: fan/preset invariants hold'
} else {
  for ($i = 0; $i -lt $fails.Count; $i++) {
    Write-Output ('FAIL#' + $i + ': ' + $fails[$i])
  }
  exit 1
}