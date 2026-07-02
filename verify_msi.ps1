<#
.SYNOPSIS
    Verifies a built Jimmy MSI without installing it: version info, required
    files (including clublog_key.txt), Rule Definitions/companion lists, and
    MajorUpgrade configuration.

.DESCRIPTION
    Reads the MSI database directly via the Windows Installer COM API
    (WindowsInstaller.Installer). No install/uninstall/repair is performed.
    Intended to run after `wix build` and before copying the MSI out for
    release, as part of the Release Versioning checklist in CLAUDE.md.

.PARAMETER MsiPath
    Path to the MSI to verify. Defaults to C:\claude\Jimmy\Jimmy.msi.

.PARAMETER ReleaseDir
    Path to the Release build output folder, used only as an optional
    cross-check of file counts against what's actually on disk. Defaults to
    C:\claude\Jimmy\WSJTX_Controller\bin\Release. Skipped if not found.

.EXAMPLE
    .\verify_msi.ps1
    .\verify_msi.ps1 -MsiPath "C:\claude\Jimmy\Setup_WiX\Release\Jimmy.msi"

.NOTES
    MSI SQL strings must be single-quoted in PowerShell -- a double-quoted
    string containing a backtick gets backtick-escaped by PowerShell itself
    before it ever reaches OpenView, silently corrupting the query.
#>
param(
    [string]$MsiPath    = "C:\claude\Jimmy\Jimmy.msi",
    [string]$ReleaseDir = "C:\claude\Jimmy\WSJTX_Controller\bin\Release"
)

$ErrorActionPreference = "Stop"
$failures = @()
$warnings = @()

function Write-Check($ok, $label, $detail) {
    $mark = if ($ok) { "PASS" } else { "FAIL" }
    Write-Output ("  [{0}] {1}{2}" -f $mark, $label, $(if ($detail) { " -- $detail" } else { "" }))
}

function Query($db, $sql) {
    # COM method calls whose return value isn't captured get written to the
    # output stream and silently merge into this function's return value --
    # [void]/| Out-Null everything except the data we actually want.
    $view = $db.OpenView($sql)
    [void]$view.Execute()
    $rows = New-Object System.Collections.ArrayList
    while ($true) {
        $rec = $view.Fetch()
        if ($null -eq $rec) { break }
        $cols = New-Object System.Collections.ArrayList
        for ($i = 1; $i -le 8; $i++) {
            [void]$cols.Add([string]$rec.StringData($i))
        }
        [void]$rows.Add($cols.ToArray())
    }
    [void]$view.Close()
    return ,$rows.ToArray()
}

function GetProperty($db, $name) {
    $rows = Query $db ("SELECT Value FROM Property WHERE Property = '{0}'" -f $name)
    if ($rows.Count -eq 0) { return $null }
    return $rows[0][0]
}

# Matches the MSI File table's "shortname|longname" FileName format (long
# name only present when it differs from the 8.3 short name).
function LongFileName($fileNameField) {
    $parts = $fileNameField -split '\|'
    return $parts[$parts.Count - 1]
}

Write-Output "=== Jimmy MSI Verification ==="
Write-Output "MSI: $MsiPath"
Write-Output ""

if (-not (Test-Path $MsiPath)) {
    Write-Output "  [FAIL] MSI file not found."
    exit 1
}
$msiSize = (Get-Item $MsiPath).Length
Write-Output ("MSI size: {0:N0} bytes" -f $msiSize)
Write-Output ""

$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.OpenDatabase($MsiPath, 0)

# ── Product identity ─────────────────────────────────────────────────────
Write-Output "--- Product identity ---"
$productName    = GetProperty $db "ProductName"
$productVersion = GetProperty $db "ProductVersion"
$productCode    = GetProperty $db "ProductCode"
$upgradeCode    = GetProperty $db "UpgradeCode"
$manufacturer   = GetProperty $db "Manufacturer"

Write-Check ($productName -eq "Jimmy") "ProductName" $productName
Write-Check ($productVersion -match '^\d+\.\d+\.\d+$') "ProductVersion" $productVersion
Write-Check ($null -ne $productCode) "ProductCode" $productCode
Write-Check ($null -ne $upgradeCode) "UpgradeCode" $upgradeCode
Write-Check ($manufacturer -eq "KB0UZT") "Manufacturer" $manufacturer
Write-Output ""

# ── Jimmy.exe file version ───────────────────────────────────────────────
Write-Output "--- Jimmy.exe File table entry ---"
$exeRows = Query $db "SELECT File, FileName, Version FROM File WHERE File = 'JimmyExeFile'"
if ($exeRows.Count -eq 0) {
    Write-Check $false "JimmyExeFile present in File table" "not found"
    $failures += "JimmyExeFile missing from MSI File table"
}
else {
    $exeVersion = $exeRows[0][2]
    Write-Check $true "JimmyExeFile present" (LongFileName $exeRows[0][1])
    Write-Check ($exeVersion -eq $productVersion + ".0" -or $exeVersion -eq $productVersion) `
        "Jimmy.exe Version matches ProductVersion" "$exeVersion vs $productVersion"
    if ($exeVersion -ne $productVersion + ".0" -and $exeVersion -ne $productVersion) {
        $warnings += "Jimmy.exe file version ($exeVersion) doesn't match ProductVersion ($productVersion)"
    }
}
Write-Output ""

# ── Required files by name ───────────────────────────────────────────────
Write-Output "--- Required files present (by name) ---"
$allFiles = Query $db "SELECT File, FileName FROM File"
$allLongNames = $allFiles | ForEach-Object { LongFileName $_[1] }

$required = @(
    "Jimmy.exe", "Jimmy.exe.config", "System.Data.SQLite.dll",
    "SQLite.Interop.dll", "clublog_key.txt"
)
foreach ($name in $required) {
    $found = $allLongNames | Where-Object { $_ -ieq $name }
    $ok = ($found -ne $null) -and ($found.Count -gt 0)
    Write-Check $ok $name $(if ($ok) { "$($found.Count) copy/copies" } else { "MISSING" })
    if (-not $ok) { $failures += "$name not found in MSI" }
}
Write-Output ""

# ── Rule Definitions / companion lists ───────────────────────────────────
Write-Output "--- Rule Definitions and companion lists ---"
$iniFiles = $allLongNames | Where-Object { $_ -like "*.ini" }
$txtFiles = $allLongNames | Where-Object { $_ -like "*.txt" -and $_ -ine "clublog_key.txt" }
Write-Check ($iniFiles.Count -gt 0) "Rule Definition .ini files packaged" "$($iniFiles.Count) file(s)"
Write-Check ($txtFiles.Count -gt 0) "Companion list .txt files packaged" "$($txtFiles.Count) file(s)"
if ($iniFiles.Count -eq 0) { $failures += "No Rule Definition .ini files found in MSI" }

if (Test-Path $ReleaseDir) {
    $diskIniCount = (Get-ChildItem (Join-Path $ReleaseDir "RuleDefinitions") -Filter "*.ini" -ErrorAction SilentlyContinue).Count
    $diskTxtCount = (Get-ChildItem (Join-Path $ReleaseDir "RuleDefinitions\Lists") -Filter "*.txt" -ErrorAction SilentlyContinue).Count
    Write-Check ($iniFiles.Count -eq $diskIniCount) "ini count matches Release build output" "MSI=$($iniFiles.Count) disk=$diskIniCount"
    Write-Check ($txtFiles.Count -eq $diskTxtCount) "txt count matches Release build output" "MSI=$($txtFiles.Count) disk=$diskTxtCount"
    if ($iniFiles.Count -ne $diskIniCount) { $warnings += "MSI .ini count ($($iniFiles.Count)) != Release build output ($diskIniCount) -- rebuild the MSI after the last Release build?" }
    if ($txtFiles.Count -ne $diskTxtCount) { $warnings += "MSI .txt count ($($txtFiles.Count)) != Release build output ($diskTxtCount) -- rebuild the MSI after the last Release build?" }
}
else {
    Write-Output "  (skipped disk cross-check -- $ReleaseDir not found)"
}
Write-Output ""

# ── MajorUpgrade / Upgrade table ─────────────────────────────────────────
Write-Output "--- MajorUpgrade configuration ---"
$upgradeRows = Query $db "SELECT UpgradeCode, VersionMin, VersionMax, Attributes, ActionProperty FROM Upgrade"
Write-Check ($upgradeRows.Count -ge 2) "Upgrade table has upgrade+downgrade rows" "$($upgradeRows.Count) row(s)"
foreach ($row in $upgradeRows) {
    $sameCode = ($row[0] -eq $upgradeCode)
    Write-Check $sameCode "Upgrade row UpgradeCode matches Property" ("min={0} max={1} action={2}" -f $row[1], $row[2], $row[4])
    if (-not $sameCode) { $failures += "Upgrade table row UpgradeCode ($($row[0])) doesn't match Property UpgradeCode ($upgradeCode)" }
}
if ($upgradeRows.Count -lt 2) { $failures += "Upgrade table missing expected rows -- MajorUpgrade may not be configured correctly" }
Write-Output ""

$db = $null
[System.GC]::Collect()
[System.GC]::WaitForPendingFinalizers()

# ── Summary ───────────────────────────────────────────────────────────────
Write-Output "=== Summary ==="
Write-Output "ProductVersion: $productVersion   ProductCode: $productCode"
Write-Output "Total files in MSI: $($allFiles.Count)"
if ($warnings.Count -gt 0) {
    Write-Output ""
    Write-Output "Warnings:"
    foreach ($w in $warnings) { Write-Output "  - $w" }
}
if ($failures.Count -gt 0) {
    Write-Output ""
    Write-Output "FAILURES:"
    foreach ($f in $failures) { Write-Output "  - $f" }
    Write-Output ""
    Write-Output "RESULT: FAIL"
    exit 1
}
else {
    Write-Output ""
    Write-Output "RESULT: PASS"
    exit 0
}
