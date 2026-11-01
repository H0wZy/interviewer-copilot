$src = Join-Path $PSScriptRoot 'FmtProbe.cs'
Add-Type -TypeDefinition (Get-Content -Raw $src) -Language CSharp
[FmtProbe.F]::Run()
