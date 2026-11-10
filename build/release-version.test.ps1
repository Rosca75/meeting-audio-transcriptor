# release-version.test.ps1 asserts the version ordering of release-version.ps1 against a table written
# out by hand, and exits 1 on the first wrong answer. Run it with: pwsh build/release-version.test.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-version.ps1')

# Each row: left, right, and whether left is below (-1), equal to (0) or above (1) right.
$table = @(
    @('1.0.0', '1.0.0', 0),
    @('v1.0.0', '1.0.0', 0),
    @('1.0.0', '2.0.0', -1),
    @('2.0.0', '2.1.0', -1),
    @('2.1.0', '2.1.1', -1),
    @('0.9.0', '0.10.0', -1),
    @('0.10.0', '0.9.9', 1),
    @('1.0.0-rc.2', '1.0.0', -1),
    @('1.0.0', '1.0.0-rc.2', 1),
    @('1.0.0-rc.1', '1.0.0-rc.2', -1),
    @('1.0.0-rc.2', '1.0.0-rc.10', -1),
    @('1.0.0-alpha', '1.0.0-alpha.1', -1),
    @('1.0.0-alpha.1', '1.0.0-alpha.beta', -1),
    @('1.0.0-alpha.beta', '1.0.0-beta', -1),
    @('1.0.0-beta', '1.0.0-beta.2', -1),
    @('1.0.0-beta.2', '1.0.0-beta.11', -1),
    @('1.0.0-beta.11', '1.0.0-rc.1', -1),
    @('1.0.0-rc.1', '1.0.0-rc.1', 0),
    @('1.0.1-rc.1', '1.0.0', 1),
    @('0.2.0-rc.2', '0.2.0', -1),
    @('1.0.0-99999999999999999999', '1.0.0-100000000000000000000', -1)
)

$failures = 0
foreach ($row in $table) {
    $got = Compare-ReleaseVersion $row[0] $row[1]
    if ($got -ne $row[2]) {
        Write-Host "FAIL: Compare-ReleaseVersion '$($row[0])' '$($row[1])' gave $got, wanted $($row[2])"
        $failures++
    }
}

# A malformed version is refused, never ordered.
try {
    Compare-ReleaseVersion '1.0' '1.0.0' | Out-Null
    Write-Host "FAIL: '1.0' was ordered, wanted a refusal"
    $failures++
} catch {
    if ($_.Exception.Message -notmatch 'not a version') { Write-Host "FAIL: '1.0' was refused with '$($_.Exception.Message)'"; $failures++ }
}

if ($failures -gt 0) {
    Write-Host "$failures of $($table.Count + 1) checks failed"
    exit 1
}
Write-Host "all $($table.Count + 1) checks passed"
