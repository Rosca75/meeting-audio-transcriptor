# release-version.ps1 holds the version ordering release.ps1 uses: semantic-version precedence, so a
# pre-release of X.Y.Z sorts below X.Y.Z and a number is compared as a number. Dot-source it:
#
#   . (Join-Path $PSScriptRoot 'release-version.ps1')
#   Compare-ReleaseVersion '1.0.0-rc.2' '1.0.0'    # -1
#
# build/release-version.test.ps1 asserts a table of orderings.

# Compares two pre-release identifiers: numbers as numbers, below any identifier with a letter or a
# hyphen, and those in ASCII order (semantic versioning 2.0.0, item 11).
function Compare-PreReleaseIdentifier([string]$left, [string]$right) {
    $leftNumeric = $left -match '^\d+$'
    $rightNumeric = $right -match '^\d+$'
    if ($leftNumeric -and $rightNumeric) {
        # Compared without converting, so an identifier longer than any integer type still orders:
        # leading zeros are dropped, then the longer number is the larger.
        $a = $left.TrimStart('0'); $b = $right.TrimStart('0')
        if ($a.Length -ne $b.Length) { return [Math]::Sign($a.Length - $b.Length) }
        return [Math]::Sign([string]::CompareOrdinal($a, $b))
    }
    if ($leftNumeric) { return -1 }
    if ($rightNumeric) { return 1 }
    return [Math]::Sign([string]::CompareOrdinal($left, $right))
}

# Returns -1, 0 or 1 as version $left is below, equal to or above $right. Each is 1.2.3 or
# 1.2.3-label, with or without a leading v; anything else is refused with the shape expected.
function Compare-ReleaseVersion([string]$left, [string]$right) {
    $pattern = '^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$'
    foreach ($text in @($left, $right)) {
        if ($text -notmatch $pattern) { throw "'$text' is not a version of the form 1.2.3 or 1.2.3-label" }
    }
    $null = $left -match $pattern; $l = $Matches.Clone()
    $null = $right -match $pattern; $r = $Matches.Clone()

    foreach ($part in 1, 2, 3) {
        $order = Compare-PreReleaseIdentifier $l[$part] $r[$part]
        if ($order -ne 0) { return $order }
    }

    # The same X.Y.Z: a version without a label is above every pre-release of it.
    $leftLabel = $l[4]; $rightLabel = $r[4]
    if (-not $leftLabel -and -not $rightLabel) { return 0 }
    if (-not $leftLabel) { return 1 }
    if (-not $rightLabel) { return -1 }

    $a = $leftLabel.Split('.'); $b = $rightLabel.Split('.')
    for ($i = 0; $i -lt [Math]::Min($a.Count, $b.Count); $i++) {
        $order = Compare-PreReleaseIdentifier $a[$i] $b[$i]
        if ($order -ne 0) { return $order }
    }

    # Every shared identifier is equal: the longer label is the later pre-release.
    return [Math]::Sign($a.Count - $b.Count)
}
