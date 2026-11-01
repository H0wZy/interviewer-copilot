param([int]$Seconds = 12, [long]$Hns = 10000000, [int]$UseEvent = 1)
$src = Join-Path $PSScriptRoot 'LoopProbe.cs'
Add-Type -TypeDefinition (Get-Content -Raw $src) -Language CSharp
[LoopProbe.P]::Run($Seconds, $Hns, [bool]$UseEvent)
