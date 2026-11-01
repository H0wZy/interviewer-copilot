<#
.SYNOPSIS
  Phase 1 audio diagnostics for interviewer-copilot. Read-only; never opens a microphone.
.DESCRIPTION
  1) Lists registered audio endpoints from the registry (with the corrected property GUIDs:
     PKEY_Device_DeviceDesc = {a45c254e-df1c-4efd-8020-67d146a850e0},2 and the adapter name
     {b3f8fa53-0004-438e-9003-51a46e139bfc},6 -- the original brief used a mistyped GUID,
     which is why every name came back as "Unknown").
  2) Uses the Core Audio COM API (IMMDeviceEnumerator) to list endpoints with their real state
     and default roles, then tests WASAPI shared-mode LOOPBACK on every ACTIVE render endpoint,
     reporting mix format, packet stats and captured levels. Play some audio while it runs to
     see non-silent levels.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\diagnostics\audio-probe.ps1 -Seconds 3
#>
param([double]$Seconds = 2.5)

$base = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio'
$devices = foreach ($type in 'Render', 'Capture') {
    Get-ChildItem (Join-Path $base $type) -ErrorAction SilentlyContinue | ForEach-Object {
        $props = Get-ItemProperty (Join-Path $_.PSPath 'Properties') -ErrorAction SilentlyContinue
        $desc = $props.'{a45c254e-df1c-4efd-8020-67d146a850e0},2'
        $adapter = $props.'{b3f8fa53-0004-438e-9003-51a46e139bfc},6'
        [pscustomobject]@{
            Type       = if ($type -eq 'Render') { 'Output (Speakers/Headphones)' } else { 'Input (Microphone)' }
            Name       = if ($desc) { "$desc ($adapter)" } else { 'Unknown' }
            EndpointId = $_.PSChildName
        }
    }
}
'== Registry endpoints (real state is in the Core Audio section below) =='
$devices | Sort-Object Type, Name | Format-Table -AutoSize | Out-String -Width 250

Add-Type -TypeDefinition (Get-Content -Raw (Join-Path $PSScriptRoot 'WasapiProbe.cs')) -Language CSharp
[WasapiProbe.Probe]::Run([int]($Seconds * 1000))
