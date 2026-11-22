# Runs every P0a/P4a check in order and writes one summary (Windows PowerShell 5.1 or PowerShell 7).
#   powershell -ExecutionPolicy Bypass -File tools\island-lab\run-checks.ps1
#   powershell -ExecutionPolicy Bypass -File tools\island-lab\run-checks.ps1 -Count 20      # a quick run on 20 seeds
#   ... -SkipTests -SkipValidate -SkipSites                                                   # leave parts out
# Run from anywhere inside the repo. Everything goes to reports\checks-<time>\ (ignored by git);
# the summary is reports\checks-<time>\summary.md and is printed at the end.
param(
    [int]$Count = 200,
    [switch]$SkipTests,
    [switch]$SkipValidate,
    [switch]$SkipSites
)

# ---------- find the repo root ----------
$root = $PSScriptRoot
while ($root -and -not (Test-Path (Join-Path $root 'BlueHarvest.sln'))) { $root = Split-Path $root -Parent }
if (-not $root) { Write-Host 'Could not find BlueHarvest.sln above this script.' -ForegroundColor Red; exit 2 }
Set-Location $root
$lab = Join-Path $root 'tools\island-lab'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outDir = Join-Path $root "reports\checks-$stamp"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$name, [bool]$ok, [string]$detail) {
    $results.Add([pscustomobject]@{ Check = $name; Ok = $ok; Detail = $detail })
    $colour = 'Red'; if ($ok) { $colour = 'Green' }
    $mark = 'FAIL'; if ($ok) { $mark = 'ok' }
    Write-Host ("[{0}] {1}: {2}" -f $mark, $name, $detail) -ForegroundColor $colour
}
function Section([string]$title) { Write-Host ''; Write-Host "==== $title ====" -ForegroundColor Cyan }
$clock = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "Blue Harvest checks: $Count seeds; output in $outDir"
Write-Host 'With 200 seeds this takes roughly 25-40 minutes (the C# validations and the site report are the long parts).'

# ---------- 0. tools ----------
$nodeVersion = (& node --version) 2>$null
$commit = (& git log --oneline -1) 2>$null
if (-not $nodeVersion) { Write-Host 'node was not found on PATH.' -ForegroundColor Red; exit 2 }
foreach ($f in 'gen.js', 'checks.js', 'validate.js', 'compare-validate.js', 'sites-report.js') {
    if (-not (Test-Path (Join-Path $lab $f))) { Write-Host "tools\island-lab\$f is missing: unzip the delivery first." -ForegroundColor Red; exit 2 }
}

# ---------- 1. C# tests ----------
if (-not $SkipTests) {
    Section 'dotnet test'
    & dotnet test 2>&1 | Tee-Object -Variable testLog
    $testExit = $LASTEXITCODE
    # dotnet test prints one of two summary formats, depending on whether its output goes to a pipe:
    #   new:    Test summary: total: 1190, failed: 0, succeeded: 1190, skipped: 0, duration: 12s
    #   VSTest: Passed!  - Failed:     0, Passed:  1190, Skipped:     0, Total:  1190, Duration: 12 s
    # Each test project prints its own line, so the counts are added up.
    $total = 0; $failed = 0; $found = 0
    foreach ($t in $testLog) {
        $s = [string]$t
        if ($s -match 'Test summary:\s*total:\s*(\d+),\s*failed:\s*(\d+)') {
            $total += [int]$Matches[1]; $failed += [int]$Matches[2]; $found++
        } elseif ($s -match 'Failed:\s*(\d+),\s*Passed:\s*\d+,\s*Skipped:\s*\d+,\s*Total:\s*(\d+)') {
            $failed += [int]$Matches[1]; $total += [int]$Matches[2]; $found++
        }
    }
    if ($found -eq 0) { $line = 'no test summary line found in the output' }
    else { $line = "total $total, failed $failed (from $found summary line(s)), exit code $testExit" }
    $ok = ($testExit -eq 0) -and ($found -gt 0) -and ($total -eq 1190) -and ($failed -eq 0)
    Add-Result 'C# tests (expect 1190 passed)' $ok $line
    $testLog | Out-File -Encoding utf8 (Join-Path $outDir 'dotnet-test.log')
}

# ---------- 2. the lab's seed checks against the C# pins ----------
Section 'node checks.js'
$checksLog = & node (Join-Path $lab 'checks.js') 2>&1
$checksLog | ForEach-Object { Write-Host $_ }
$last = ($checksLog | Select-Object -Last 1) -as [string]
Add-Result 'Lab seed checks against the C# pins' (($LASTEXITCODE -eq 0) -and ($last -match 'All four test islands match')) $last
$checksLog | Out-File -Encoding utf8 (Join-Path $outDir 'checks.log')

# ---------- 3. golden scripts: every hash they print must be pinned in the C# tests ----------
Section 'golden scripts'
$testText = (Get-ChildItem (Join-Path $root 'src\BlueHarvest.Sim.Tests') -Filter *.cs | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
$goldens = 'golden', 'golden-shape', 'golden-climate', 'golden-sites', 'golden-roads', 'golden-transit', 'golden-wild', 'golden-chunks', 'golden-rooms', 'golden-knowledge', 'golden-areas'
$allHashes = 0; $missing = 0; $goldenDir = Join-Path $outDir 'golden'
New-Item -ItemType Directory -Force -Path $goldenDir | Out-Null
foreach ($g in $goldens) {
    $text = (& node (Join-Path $lab "$g.js") 2>&1 | Out-String)
    $text | Out-File -Encoding utf8 (Join-Path $goldenDir "$g.txt")
    $tokens = [regex]::Matches($text, '0x[0-9A-F]{16}UL') | ForEach-Object { $_.Value } | Sort-Object -Unique
    $miss = @($tokens | Where-Object { -not $testText.Contains($_) })
    $allHashes += @($tokens).Count; $missing += $miss.Count
    Write-Host ("  {0,-18} {1,3} hashes, {2} not in the tests" -f $g, @($tokens).Count, $miss.Count)
    foreach ($m in $miss) { Write-Host "    missing: $m" -ForegroundColor Red }
}
Add-Result 'Golden hashes pinned in the C# tests (expect 273)' (($missing -eq 0) -and ($allHashes -eq 273)) "$allHashes hashes, $missing missing"

# ---------- 3b. the content packs (content/packs, as the game loads them) ----------
Section 'content validate content\packs'
& dotnet run --project src\BlueHarvest.Runner -- content validate --path content\packs 2>&1 | Tee-Object -Variable contentLog
$contentExit = $LASTEXITCODE
$contentLog | Out-File -Encoding utf8 (Join-Path $outDir 'content-validate.log')
$contentLine = $contentLog | Select-String -Pattern '^Result:.*' | Select-Object -Last 1
$contentResult = 'no Result line'; if ($contentLine) { $contentResult = $contentLine.Matches[0].Value }
$contentPrint = $contentLog | Select-String -Pattern '^Fingerprint:\s+(\S+)' | Select-Object -Last 1
if ($contentPrint) { $contentResult = "$contentResult; $($contentPrint.Matches[0].Groups[1].Value)" }
Add-Result 'Content packs validate (authoring mode)' (($contentExit -eq 0) -and ($contentResult -match '^Result: OK')) $contentResult

# ---------- 4. island validate: C# and lab, at 75% and 100%, compared column by column ----------
$timings = @{}
if (-not $SkipValidate) {
    Section 'building the runner (Release)'
    & dotnet build -c Release src\BlueHarvest.Runner | Out-File -Encoding utf8 (Join-Path $outDir 'runner-build.log')
    if ($LASTEXITCODE -ne 0) { Add-Result 'Runner build (Release)' $false 'see runner-build.log' }
    else {
        foreach ($scale in 75, 100) {
            Section "island validate 1 $Count $scale (C#)"
            & dotnet run -c Release --no-build --project src\BlueHarvest.Runner -- island validate 1 $Count $scale 2>&1 | Tee-Object -Variable csLog
            $csLog | Out-File -Encoding utf8 (Join-Path $outDir "csharp-validate-$scale.log")
            $done = $csLog | Select-String -Pattern 'Done in (\d+)s' | Select-Object -Last 1
            $seeds = $csLog | Select-String -Pattern '^Seeds:\s+(.+)$' | Select-Object -Last 1
            $checksLine = $csLog | Select-String -Pattern '^Checks:.*' | Select-Object -Last 1
            $csSeconds = ''; if ($done) { $csSeconds = $done.Matches[0].Groups[1].Value }
            $timings["csharp-$scale"] = $csSeconds
            $csChecks = 'no Checks line'; if ($checksLine) { $csChecks = $checksLine.Matches[0].Value }
            Add-Result "C# island validate at $scale% ($Count seeds)" ($csChecks -match "Checks: $Count of $Count seeds pass") "$csChecks Done in ${csSeconds}s"

            Section "island validate 1 $Count $scale (lab)"
            $labOut = Join-Path $outDir "lab-$scale"
            & node (Join-Path $lab 'validate.js') 1 $Count $scale --out $labOut 2>&1 | Tee-Object -Variable labLog
            $labLog | Out-File -Encoding utf8 (Join-Path $outDir "lab-validate-$scale.log")
            $labDone = $labLog | Select-String -Pattern 'Done in (\d+)s' | Select-Object -Last 1
            if ($labDone) { $timings["lab-$scale"] = $labDone.Matches[0].Groups[1].Value }

            Section "compare C# and lab at $scale%"
            if (-not $seeds) { Add-Result "C# and lab agree at $scale%" $false 'the C# run printed no Seeds: path' }
            else {
                $csCsv = $seeds.Matches[0].Groups[1].Value.Trim()
                $cmp = & node (Join-Path $lab 'compare-validate.js') $csCsv (Join-Path $labOut 'seeds.csv') 2>&1
                $cmp | ForEach-Object { Write-Host $_ }
                $cmp | Out-File -Encoding utf8 (Join-Path $outDir "compare-$scale.log")
                $cmpLast = ($cmp | Select-Object -Last 1) -as [string]
                Add-Result "C# and lab agree at $scale%" ($LASTEXITCODE -eq 0) $cmpLast
            }
        }
    }
}

# ---------- 5. the P4a site report ----------
if (-not $SkipSites) {
    Section "sites-report 1 $Count (program 2 at 100%)"
    $sitesOut = Join-Path $outDir 'sites'
    & node (Join-Path $lab 'sites-report.js') 1 $Count --out $sitesOut 2>&1 | Tee-Object -Variable siteLog
    $siteLog | Out-File -Encoding utf8 (Join-Path $outDir 'sites-report.log')
    $reportPath = Join-Path $sitesOut 'report.md'
    if (Test-Path $reportPath) {
        $passLine = (Get-Content $reportPath | Select-Object -Index 2)
        Write-Host $passLine
        $m = [regex]::Match($passLine, '(\d+) of (\d+) seeds \(([\d.]+)%\) pass every Fail check')
        $ok = $m.Success -and ([double]$m.Groups[3].Value -ge 97)
        Add-Result 'P4a site program passes the 97% gate' $ok $passLine
    } else { Add-Result 'P4a site program passes the 97% gate' $false 'no report.md written; see sites-report.log' }
}

# ---------- summary ----------
$clock.Stop()
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Blue Harvest checks, $stamp")
$lines.Add('')
$lines.Add("Commit: $commit; node $nodeVersion; $Count seeds; took $([int]$clock.Elapsed.TotalMinutes) min.")
$lines.Add('')
$lines.Add('| Check | Result | Detail |')
$lines.Add('|---|---|---|')
foreach ($r in $results) {
    $res = 'FAIL'; if ($r.Ok) { $res = 'ok' }
    $lines.Add("| $($r.Check) | $res | $(($r.Detail -replace '\|', '/').Trim()) |")
}
$lines.Add('')
$lines.Add('Timings, seconds for the whole run:')
foreach ($k in ($timings.Keys | Sort-Object)) { $lines.Add("- ${k}: $($timings[$k])") }
$lines.Add('')
$lines.Add("Logs and reports: $outDir")
$summaryPath = Join-Path $outDir 'summary.md'
$lines | Out-File -Encoding utf8 $summaryPath

Section 'summary'
$lines | ForEach-Object { Write-Host $_ }
$failed = @($results | Where-Object { -not $_.Ok }).Count
if ($failed -eq 0) { Write-Host "`nAll checks passed. Send summary.md to Claude: $summaryPath" -ForegroundColor Green; exit 0 }
Write-Host "`n$failed check(s) failed. Send summary.md and the logs beside it to Claude: $outDir" -ForegroundColor Red
exit 1