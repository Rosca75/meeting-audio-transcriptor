# release.ps1 is the one way a version is released: it checks that main is ready, then creates the
# annotated tag vX.Y.Z and pushes it. The tag starts .github/workflows/release.yml, which builds
# both packages on the hosted runners and attaches them to a release named after the tag. Nothing
# is built or uploaded from this machine, so a package is always built the same way.
#
#   pwsh build/release.ps1 -Version 0.1.0 [-DryRun]
#
# It refuses, saying what to do, when the version is not of the form 1.2.3 or 1.2.3-label, when the
# checkout is not main, holds uncommitted changes or differs from the remote's main, when CHANGELOG.md
# has no entry for the version, when the tag exists here or on the remote, or when the version is
# not above the highest one already released (a pre-release sorts below its final version). A tag
# is a public promise that these bytes are that version, so it is cut once, from what everyone can
# see, and never moved or reused.
#
# -DryRun performs every check and prints the commands it would run, and creates and pushes nothing.
[CmdletBinding()]
param(
    # The same shape publish.ps1 and the workflow accept, checked here first so a malformed tag
    # never reaches the remote.
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$')]
    [string]$Version,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tag = "v$Version"

function Step([string]$text) { Write-Host "==> $text" }

# Runs git in the repository and returns its output, refusing on a failure with the command that
# failed. Git writes its progress to the error stream, which would otherwise be turned into a
# stopping error, so the exit code alone decides.
function Invoke-Git {
    $ErrorActionPreference = 'Continue'
    $output = & git -C $root @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed with exit code $LASTEXITCODE; read its message above" }
    return $output
}

Step 'checking the checkout'
$branch = Invoke-Git rev-parse --abbrev-ref HEAD
if ($branch -ne 'main') {
    throw "the checkout is on '$branch'; a version is tagged from main only, so run 'git switch main' and try again"
}
$changes = Invoke-Git status --porcelain
if ($changes) {
    throw "the checkout has uncommitted changes, which the tag would not carry; commit or stash them first:`n$($changes -join "`n")"
}

Step 'comparing with the remote'
Invoke-Git fetch origin main --tags --quiet | Out-Null
$local = Invoke-Git rev-parse HEAD
$remote = Invoke-Git rev-parse origin/main
if ($local -ne $remote) {
    throw "the local main ($($local.Substring(0, 7))) differs from origin/main ($($remote.Substring(0, 7))); push or pull until they match, so the tag names a commit everyone can see"
}

Step 'checking the changelog'
$changelog = Join-Path $root 'CHANGELOG.md'
if (-not (Test-Path -LiteralPath $changelog)) { throw 'CHANGELOG.md is missing; restore it, then add an entry for this version' }
if (-not ((Get-Content -LiteralPath $changelog -Raw) -match "(?m)^## \[$([regex]::Escape($Version))\]")) {
    throw "CHANGELOG.md has no '## [$Version]' heading; move the Unreleased entries under it, commit and push to main, then run this again"
}

Step "checking that $tag is new"
if (Invoke-Git tag --list $tag) {
    throw "the tag $tag already exists; a version is released once, so choose the next number"
}
if (Invoke-Git ls-remote --tags origin "refs/tags/$tag") {
    throw "the remote already has the tag $tag; a version is released once, so choose the next number"
}

# Every released version, ordered by semantic-version precedence (release-version.ps1): numbers as
# numbers, so 0.10.0 is above 0.9.0, and a pre-release below its final version, so 1.2.0 may follow
# 1.2.0-rc.1 while 1.2.0-rc.2 may not follow 1.2.0. A new version must be above every released one.
. (Join-Path $PSScriptRoot 'release-version.ps1')
$released = @(Invoke-Git tag --list 'v*' | Where-Object { $_ -match '^v\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$' })
if ($released.Count -gt 0) {
    $highest = $released[0]
    foreach ($candidate in $released) {
        if ((Compare-ReleaseVersion $candidate $highest) -gt 0) { $highest = $candidate }
    }
    if ((Compare-ReleaseVersion $tag $highest) -le 0) {
        throw "$tag is not above the highest released version, $highest (a pre-release sorts below its final version); choose a number above it, or cut a hot fix from a branch that tags a higher number"
    }
}

if ($DryRun) {
    Step 'dry run: every check passed; nothing was created or pushed. A release would run:'
    Write-Host "    git tag -a $tag -m `"meeting-audio-transcriptor $Version`""
    Write-Host "    git push origin $tag"
    return
}

Step "tagging $local as $tag and pushing it"
Invoke-Git tag -a $tag -m "meeting-audio-transcriptor $Version" | Out-Null
Invoke-Git push origin $tag | Out-Null

Write-Host ''
Write-Host "    $tag is pushed. The release workflow builds both packages and attaches them to the release of the tag."
Write-Host '    Follow the build with: gh run list --workflow release.yml --limit 1'
