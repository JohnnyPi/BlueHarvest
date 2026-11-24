<#
.SYNOPSIS
  Collects a *real, local* Otherwild Studio VA.0 verification log.
.DESCRIPTION
  From any location inside the BlueHarvest checkout, discovers the repo root,
  records branch and tools, then runs native build/test/content validation.
  -IncludeIsland also runs a three-seed island check (writes ignored reports/).
  Does NOT run narrative-studio/make-test-fixture.js and DOES NOT change game sources.
  Writes logs under a timestamped directory in the OS temp folder.
#>
param([switch]$IncludeIsland)

$ErrorActionPreference = 'Continue'
$root = (& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $root) {
    throw 'Run this script from inside a BlueHarvest Git checkout.'
}
$root = $root.Trim()
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logs = Join-Path $env:TEMP "Otherwild-VA0-$stamp"
New-Item -ItemType Directory -Force -Path $logs | Out-Null
Push-Location $root
try {
    $results = [System.Collections.Generic.List[object]]::new()
    function Invoke-BaselineStep([string]$Name, [string]$Exe, [string[]]$Arguments) {
        $file = Join-Path $logs ("$Name.log")
        $cmd = Get-Command $Exe -ErrorAction SilentlyContinue
        if (-not $cmd) {
            "SKIPPED: $Exe not installed" | Set-Content -Path $file
            $results.Add([pscustomobject]@{ Step=$Name; Outcome='SKIPPED'; ExitCode='' })
            Write-Warning "$Name skipped: $Exe unavailable"
            return
        }
        "COMMAND: $Exe $($Arguments -join ' ')" | Set-Content -Path $file
        & $Exe @Arguments *>> $file
        $code = $LASTEXITCODE
        if ($null -eq $code) { $code = 0 }
        $results.Add([pscustomobject]@{ Step=$Name; Outcome=$(if($code -eq 0){'PASS'}else{'FAIL'}); ExitCode=$code })
        Write-Host "$Name : exit $code"
    }

    Invoke-BaselineStep '01-git-status' 'git' @('status','--short')
    Invoke-BaselineStep '02-git-branch' 'git' @('branch','--show-current')
    Invoke-BaselineStep '03-git-head' 'git' @('rev-parse','HEAD')
    Invoke-BaselineStep '04-dotnet-info' 'dotnet' @('--info')
    Invoke-BaselineStep '05-dotnet-build' 'dotnet' @('build','BlueHarvest.sln')
    Invoke-BaselineStep '06-dotnet-test' 'dotnet' @('test','BlueHarvest.sln')
    Invoke-BaselineStep '07-content-authoring' 'dotnet' @('run','--project','src/BlueHarvest.Runner','--','content','validate','--path','src/BlueHarvest.Sim.Tests/Content/Fixtures/packs')
    if ($IncludeIsland) {
        Invoke-BaselineStep '08-island-validate' 'dotnet' @('run','--project','src/BlueHarvest.Runner','--','island','validate','1','3')
    }
    Invoke-BaselineStep '09-node-version' 'node' @('--version')
    Invoke-BaselineStep '10-npm-version' 'npm' @('--version')
    $results | Format-Table -AutoSize | Out-String | Set-Content (Join-Path $logs 'SUMMARY.txt')
    Write-Host "Baseline logs: $logs"
    $results | Format-Table -AutoSize
} finally {
    Pop-Location
}
