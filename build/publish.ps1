# publish.ps1 builds the one release package of an operating system: a self-contained portable
# folder holding both executables, the runtime they share, the native ONNX Runtime library, the
# models the lock ships, the pinned ffmpeg and the notices, archived as a zip (win-x64) or a
# tar.gz (linux-x64). Nothing is installed and nothing is downloaded when the package runs.
#
#   pwsh build/publish.ps1 [-Rid win-x64|linux-x64] [-Version 1.2.3] [-Output artifacts]
#                          [-ModelsDir <dir>] [-FfmpegDir <dir>] [-SkipModels] [-RecordBudgets]
#                          [-SmokeClip <file.wav>] [-SmokeLanguage en|fr|de]
#
# The Linux package is built on Linux only, because a tar.gz must carry the executable bits and
# only a Linux tar writes them. The Windows package may be built on either system (a zip needs no
# executable bits), which is useful for a local check; the smoke test runs only when the build
# machine is the package's own operating system, because the executables cannot run elsewhere.
#
# The release workflow runs this script on the hosted runner of each operating system. A build
# with -SkipModels proves the pipeline without 800 MB of models, and its name says so
# ("-nomodels"), so it can never be mistaken for a release.
[CmdletBinding()]
param(
    # The platform to package; the build machine's own operating system when absent.
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Rid = $(if ($IsWindows) { 'win-x64' } else { 'linux-x64' }),

    # Stamped on both executables as their version. The release workflow passes the tag's number;
    # the pattern also keeps anything but a version out of the build property and the file names.
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$')]
    [string]$Version = '0.0.0-dev',

    [string]$Configuration = 'Release',

    # Where the staging folder, the cache, the archive and sizes.json go.
    [string]$Output = 'artifacts',

    # A models folder that already holds the lock's default set. Absent: the fetch tool fills
    # <Output>/cache/models, which a second run finds complete and leaves alone.
    [string]$ModelsDir,

    # An ffmpeg folder laid out as <dir>/<rid>/ffmpeg[.exe] plus LICENSE.txt, as the fetch tool
    # writes it. Absent: the fetch tool fills <Output>/cache/ffmpeg.
    [string]$FfmpegDir,

    # Another models lock, for a test that ships a different set. The release uses the repository's.
    [string]$ModelsLock,

    # Leave the models out for a quick structural test; the package name carries "-nomodels".
    [switch]$SkipModels,

    # Rewrite build/size-budgets.json from this run instead of checking against it.
    [switch]$RecordBudgets,

    # A short speech recording (a test set clip) the smoke test transcribes from inside the staged
    # package, through the bundled ffmpeg and, on Windows, through Media Foundation as well, which
    # proves the shipped layout: models and ffmpeg found beside the executable. A missing file
    # skips that part with a notice; it is ignored with -SkipModels, which ships no model.
    [string]$SmokeClip,

    # The language spoken in the smoke clip.
    [ValidateSet('en', 'fr', 'de')]
    [string]$SmokeLanguage = 'en'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$buildDir = $PSScriptRoot
if (-not [System.IO.Path]::IsPathRooted($Output)) { $Output = Join-Path $root $Output }
if (-not $ModelsLock) { $ModelsLock = Join-Path $buildDir 'models.lock' }
$budgetsPath = Join-Path $buildDir 'size-budgets.json'

# The package may be built for another system than the build machine's, so the host is read once.
$hostIsWindows = [bool]$IsWindows
$ridIsWindows = $Rid -eq 'win-x64'
$exe = if ($ridIsWindows) { '.exe' } else { '' }
$sameOs = ($ridIsWindows -eq $hostIsWindows) -and ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'X64')
$packageName = "meeting-audio-transcriptor-$Version-$Rid" + $(if ($SkipModels) { '-nomodels' } else { '' })
$archiveLimit = 2000000000   # a GitHub release asset may not exceed 2 GB

function Step([string]$text) { Write-Host "==> $text" }

# Runs a native command and refuses on a non-zero exit code, naming the command and what to do.
function Invoke-Native([string]$what, [string]$hint, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE; $hint" }
}

function Write-Json([string]$path, $value) {
    $text = ($value | ConvertTo-Json -Depth 6) + "`n"
    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-Sha256([string]$path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant() }

function Get-FolderBytes([string]$path) {
    $total = 0L
    foreach ($file in Get-ChildItem -LiteralPath $path -Recurse -File -Force) { $total += $file.Length }
    return $total
}

# Fails unless the file exists with the size and SHA-256 the lock pins, saying how to fix it.
function Assert-Pinned([string]$path, [long]$bytes, [string]$sha256, [string]$fixHint) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "$path is missing; $fixHint" }
    $actualBytes = (Get-Item -LiteralPath $path).Length
    if ($actualBytes -ne $bytes) { throw "$path has $actualBytes bytes, wanted $bytes; delete it and $fixHint" }
    $actual = Get-Sha256 $path
    if ($actual -ne $sha256) { throw "$path has SHA-256 $actual, wanted $sha256; delete it and $fixHint" }
}

if (-not $ridIsWindows -and $hostIsWindows) {
    throw "the linux-x64 package is a tar.gz that must carry executable bits, and a Windows build machine cannot write them; build it on Linux (the release workflow does), or build -Rid win-x64 here"
}
if ($RecordBudgets -and $SkipModels) {
    throw "-RecordBudgets with -SkipModels would record the size of a package that is not a release; run it without -SkipModels"
}
foreach ($tool in 'dotnet', 'node') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool was not found on the PATH; install it (the SDK in global.json, Node 24) and run this again" }
}

# ---------------------------------------------------------------------------------------------
Step "publishing both executables for $Rid (version $Version)"
# Both executables are published self-contained into one folder so they share one runtime. The
# runtime is deliberately NOT trimmed and NOT single-file: per-application trimming would cut each
# executable's copy of the framework differently, and two executables in one folder must share
# identical framework files. Debug symbols are not wanted by a user and are not produced.
# ReadyToRun is not enabled: its cross-compiler packs differ per operating system and the Windows
# package may be built from Linux, so one setting that provably works for both is not available.
$work = Join-Path $Output "work/$Rid"
$staging = Join-Path $Output "staging/$Rid"
$package = Join-Path $staging $packageName
foreach ($folder in $work, $staging) {
    if (Test-Path $folder) { Remove-Item -Recurse -Force $folder }
}
New-Item -ItemType Directory -Force -Path $work, $package | Out-Null

$projects = [ordered]@{
    shell = Join-Path $root 'src/MeetingAudioTranscriptor.Shell/MeetingAudioTranscriptor.Shell.csproj'
    cli   = Join-Path $root 'src/MeetingAudioTranscriptor.Cli/MeetingAudioTranscriptor.Cli.csproj'
}
foreach ($name in $projects.Keys) {
    $project = $projects[$name]
    $into = Join-Path $work $name
    Invoke-Native "dotnet restore of $name" 'read its message above; the usual cause is a package source that cannot be reached' {
        # The property is passed by name: "restore -r" sets RuntimeIdentifiers (plural), which the
        # Shell project's host selection does not read, so a cross restore would drop the web view package.
        dotnet restore $project "-p:RuntimeIdentifier=$Rid" --nologo -v quiet
    }
    Invoke-Native "dotnet publish of $name" 'read the compiler message above and fix it first' {
        dotnet publish $project -c $Configuration -r $Rid --self-contained true --no-restore `
            "-p:Version=$Version" -p:DebugType=none -p:DebugSymbols=false -p:PublishTrimmed=false -p:PublishSingleFile=false `
            -m:1 -o $into --nologo -v quiet
    }
}

# ---------------------------------------------------------------------------------------------
Step 'checking that both executables exist and share identical framework files'
$shellExe = "meeting-audio-transcriptor$exe"
$cliExe = "meeting-audio-transcriptor-cli$exe"
foreach ($pair in @(@('shell', $shellExe), @('cli', $cliExe))) {
    $path = Join-Path (Join-Path $work $pair[0]) $pair[1]
    if (-not (Test-Path -LiteralPath $path)) { throw "the $($pair[0]) publish has no $($pair[1]); the project's AssemblyName or output type changed, so update this script and the README it writes" }
}
if ($ridIsWindows) {
    # The Windows host needs the web view's assembly and loader beside the executable; a restore
    # for the wrong host would still compile and publish, and the window would fail on a user's machine.
    foreach ($needed in 'Microsoft.Web.WebView2.Core.dll', 'WebView2Loader.dll') {
        if (-not (Test-Path -LiteralPath (Join-Path $work "shell/$needed"))) {
            throw "the desktop publish for $Rid has no $needed; the web view package was not restored for the Windows host. Delete src/MeetingAudioTranscriptor.Shell/obj and publish again"
        }
    }
}
# A file both publishes hold must be the same bytes; only each executable's own manifests differ,
# because they name the executable's own assembly and its own dependencies.
$shellRoot = (Resolve-Path (Join-Path $work 'shell')).Path
$cliRoot = (Resolve-Path (Join-Path $work 'cli')).Path
$compared = 0
foreach ($file in Get-ChildItem -LiteralPath $shellRoot -Recurse -File -Force) {
    $relative = [System.IO.Path]::GetRelativePath($shellRoot, $file.FullName)
    if ($relative -like '*.deps.json' -or $relative -like '*.runtimeconfig.json') { continue }
    $other = Join-Path $cliRoot $relative
    if (-not (Test-Path -LiteralPath $other)) { continue }
    $compared++
    if ($file.Length -ne (Get-Item -LiteralPath $other).Length -or (Get-Sha256 $file.FullName) -ne (Get-Sha256 $other)) {
        throw "$relative differs between the desktop and the command-line publish; the two share one folder, so both must publish the same framework files. Publish both with identical properties and the same SDK, and check that no project sets a per-project property such as trimming"
    }
}
if ($compared -lt 100) { throw "only $compared files are shared by the two publishes, wanted the whole runtime (over 100); the publish did not produce a self-contained folder" }
Write-Host "    $compared shared files are identical"

Copy-Item -Path (Join-Path $shellRoot '*') -Destination $package -Recurse -Force
Copy-Item -Path (Join-Path $cliRoot '*') -Destination $package -Recurse -Force
# The two single-executable publishes are no longer needed; a runner's disk is better kept free.
Remove-Item -Recurse -Force $work

# ---------------------------------------------------------------------------------------------
Step 'removing the files nobody loads'
# Each pattern is a file a self-contained publish copies that no run of this application loads.
# Matching is by file name anywhere in the folder.
$removals = @(
    @{ Pattern = '*.pdb';                              Why = 'debug symbols are for a developer, and a user pays for them in every download' },
    @{ Pattern = '*.xml';                              Why = 'documentation files of packages are for a reader of the package source' },
    @{ Pattern = '*.lib';                              Why = 'import libraries are linked against at build time and never loaded' },
    @{ Pattern = 'createdump*';                        Why = 'the crash-dump writer runs only when a developer switches dumps on' },
    @{ Pattern = '*mscordaccore*';                     Why = 'the debugger data-access library is loaded by a debugger only' },
    @{ Pattern = '*mscordbi*';                         Why = 'the debugging interface is loaded by a debugger only' },
    @{ Pattern = '*clrgc.*';                           Why = 'the alternative standalone garbage collector is loaded only when a setting names it' },
    @{ Pattern = '*clrgcexp.*';                        Why = 'the experimental standalone garbage collector is loaded only when a setting names it' },
    @{ Pattern = 'libcoreclrtraceptprovider.so';       Why = 'the LTTng trace provider is loaded only when tracing is switched on' },
    @{ Pattern = 'msquic.dll';                         Why = 'HTTP/3 transport; the application opens no network connection' },
    @{ Pattern = 'Microsoft.DiaSymReader.Native.*.dll'; Why = 'portable-PDB conversion for compilers, never used at run time' }
)
foreach ($rule in $removals) {
    foreach ($file in Get-ChildItem -LiteralPath $package -Recurse -File -Force -Filter $rule.Pattern) {
        Remove-Item -LiteralPath $file.FullName -Force
    }
}
# runtimes/ holds the web view loader's second copy; the loader beside the executable is the one used.
$runtimesFolder = Join-Path $package 'runtimes'
if (Test-Path $runtimesFolder) { Remove-Item -Recurse -Force $runtimesFolder }

# Refuse a folder that holds anything else, so new junk (a readme, a test file, a symbol file a
# package starts copying) is noticed here rather than shipped to every user.
$allowedExtensions = @('.dll', '.so', '.exe', '.json')
$allowedNames = @('meeting-audio-transcriptor', 'meeting-audio-transcriptor-cli')
$unexpected = @()
foreach ($entry in Get-ChildItem -LiteralPath $package -Recurse -Force) {
    $relative = [System.IO.Path]::GetRelativePath($package, $entry.FullName)
    if ($entry.PSIsContainer) { $unexpected += "$relative (a folder)"; continue }
    if ($allowedExtensions -notcontains $entry.Extension -and $allowedNames -notcontains $entry.Name) { $unexpected += $relative }
}
if ($unexpected.Count -gt 0) {
    throw "the published folder holds files nobody is known to load: $($unexpected -join ', '). If the application needs one, allow its type in build/publish.ps1; if not, add it to the removal list there with the reason"
}
Write-Host "    runtime folder: $((Get-ChildItem -LiteralPath $package -File).Count) files"

# ---------------------------------------------------------------------------------------------
Step 'adding the models'
$modelsInto = Join-Path $package 'models'
New-Item -ItemType Directory -Force -Path $modelsInto | Out-Null
$modelsBytes = 0L
if ($SkipModels) {
    # An empty models folder keeps the program resolving its models beside itself, so a smoke test
    # of the structure cannot wander up into a development checkout's models.
    Write-Host '    skipped (-SkipModels)'
} else {
    if (-not (Test-Path -LiteralPath $ModelsLock)) { throw "the models lock $ModelsLock does not exist; run from a checkout of the repository" }
    if (-not $ModelsDir) {
        $ModelsDir = Join-Path $Output 'cache/models'
        Invoke-Native 'node build/fetch-models.mjs' 'read its message above; it names the file that could not be fetched or verified' {
            node (Join-Path $buildDir 'fetch-models.mjs') --into $ModelsDir --lock $ModelsLock
        }
    }
    $lock = Get-Content -LiteralPath $ModelsLock -Raw | ConvertFrom-Json
    # The default set is what a release ships; a file of role "source" is an input the fetch tool
    # derives a shipped file from, and the application never loads it.
    $shipped = @($lock.models | Where-Object { $_.set -eq 'default' } | ForEach-Object { $model = $_; $model.files | Where-Object { $_.role -ne 'source' } | ForEach-Object { [pscustomobject]@{ Model = $model.name; File = $_ } } })
    if ($shipped.Count -eq 0) { throw "$ModelsLock lists no file of the default set, so the package would carry no model; check the 'set' fields of the lock" }
    foreach ($item in $shipped) {
        $file = $item.File
        $from = Join-Path $ModelsDir $file.path
        Assert-Pinned $from $file.bytes $file.sha256 "run: node build/fetch-models.mjs --into $ModelsDir"
        $to = Join-Path $modelsInto $file.path
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $to) | Out-Null
        Copy-Item -LiteralPath $from -Destination $to
        # The copy is verified again: a package built from a half-copied file would only fail on a user's machine.
        Assert-Pinned $to $file.bytes $file.sha256 "build again; the copy into the package was damaged"
        $modelsBytes += $file.bytes
        Write-Host "    $($file.path)"
    }
}
# The lock rides beside the executable so that "models --verify" in a package checks its own files.
Copy-Item -LiteralPath $ModelsLock -Destination (Join-Path $package 'models.lock')

# ---------------------------------------------------------------------------------------------
Step 'adding ffmpeg'
$ffmpegLock = (Get-Content -LiteralPath (Join-Path $buildDir 'ffmpeg.lock') -Raw | ConvertFrom-Json).builds | Where-Object { $_.rid -eq $Rid }
if (-not $ffmpegLock) { throw "build/ffmpeg.lock has no build for $Rid; add one, or package a platform the lock lists" }
if (-not $FfmpegDir) {
    $FfmpegDir = Join-Path $Output 'cache/ffmpeg'
    Invoke-Native 'node build/fetch-ffmpeg.mjs' 'read its message above; it names what could not be fetched or verified' {
        node (Join-Path $buildDir 'fetch-ffmpeg.mjs') --rid $Rid --into $FfmpegDir
    }
}
$ffmpegFrom = Join-Path $FfmpegDir $Rid
$ffmpegInto = Join-Path $package 'ffmpeg'
New-Item -ItemType Directory -Force -Path $ffmpegInto | Out-Null
$ffmpegHint = "run: node build/fetch-ffmpeg.mjs --rid $Rid --into $FfmpegDir"
$binary = $ffmpegLock.binary
Assert-Pinned (Join-Path $ffmpegFrom $binary.name) $binary.bytes $binary.sha256 $ffmpegHint
Copy-Item -LiteralPath (Join-Path $ffmpegFrom $binary.name) -Destination (Join-Path $ffmpegInto $binary.name)
foreach ($licence in $ffmpegLock.licenceFiles) {
    Assert-Pinned (Join-Path $ffmpegFrom $licence.name) $licence.bytes $licence.sha256 $ffmpegHint
    Copy-Item -LiteralPath (Join-Path $ffmpegFrom $licence.name) -Destination (Join-Path $ffmpegInto $licence.name)
}

# ---------------------------------------------------------------------------------------------
Step 'adding the notices and the readme'
foreach ($name in 'NOTICE', 'LICENSE') {
    $source = Join-Path $root $name
    if (-not (Test-Path -LiteralPath $source)) { throw "$name is missing from the repository root; every package carries it, so restore it" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $package $name)
}
$start = if ($ridIsWindows) { "Double-click meeting-audio-transcriptor.exe, or run it from a terminal." } else { "Run ./meeting-audio-transcriptor from a terminal or a file manager." }
$cli = if ($ridIsWindows) { '.\meeting-audio-transcriptor-cli.exe' } else { './meeting-audio-transcriptor-cli' }
$requirements = if ($ridIsWindows) {
    "Windows 10 or 11, 64-bit, with the Microsoft Edge WebView2 runtime (Evergreen).`nWindows 11 has it already; Windows 10 may need it from Microsoft's download page."
} else {
    "Linux, 64-bit, glibc 2.31 or newer, with the packages libgtk-3-0 and libwebkit2gtk-4.1-0.`nOn Debian or Ubuntu: sudo apt install libgtk-3-0 libwebkit2gtk-4.1-0"
}
$readme = @"
meeting-audio-transcriptor $Version ($Rid)

Transcribes a recorded meeting in English, French or German into a transcript file
(.vtt, .json or .txt), speaker by speaker, entirely on this computer. It opens no network
connection and needs no installation: unpack this folder anywhere and run it from there.

Window:        $start
Command line:  $cli --help
               $cli transcribe meeting.m4a --language en --formats vtt,txt
Check files:   $cli models --verify

System requirements
$requirements
No other software, no account and no network connection is needed.

What is in this folder
  meeting-audio-transcriptor$exe       the window
  meeting-audio-transcriptor-cli$exe   the command line
  models/                              the speech models, found beside the program
  ffmpeg/                              the audio decoder (LGPL, unmodified; see ffmpeg/LICENSE.txt)
  NOTICE, LICENSE                      the licences and credits of everything in this folder

Keep the folder together; the programs find their models and ffmpeg beside themselves.
"@
if (-not $ridIsWindows) { $readme = $readme -replace "`r`n", "`n" }
[System.IO.File]::WriteAllText((Join-Path $package 'README.txt'), $readme, (New-Object System.Text.UTF8Encoding($false)))

if (-not $ridIsWindows) {
    # Executable bits, set explicitly so a copy that lost them cannot ship.
    foreach ($name in 'meeting-audio-transcriptor', 'meeting-audio-transcriptor-cli', 'ffmpeg/ffmpeg') {
        Invoke-Native "chmod of $name" 'the file must be executable in the archive' { chmod +x (Join-Path $package $name) }
    }
}

# Transcribes the smoke clip with the staged command line: the clip is first encoded to AAC by the
# bundled ffmpeg, so the run must decode a compressed container, and the decoder variables are
# cleared so the program can only use what the package holds. Every run must end with exit code 0,
# name the expected decoder and recognise words.
function Invoke-SmokeTranscription([string]$CliPath, [string]$FfmpegPath) {
    $smokeDir = Join-Path $Output "smoke/$Rid"
    if (Test-Path $smokeDir) { Remove-Item -Recurse -Force $smokeDir }
    New-Item -ItemType Directory -Force -Path $smokeDir | Out-Null
    $m4a = Join-Path $smokeDir 'smoke.m4a'
    Invoke-Native 'encoding the smoke clip with the bundled ffmpeg' 'the bundled ffmpeg could not write AAC; run it by hand on the clip to read its message' {
        & $FfmpegPath -hide_banner -v error -nostdin -y -i $SmokeClip -c:a aac -b:a 64k $m4a
    }
    $saved = @{ MAT_FFMPEG = $env:MAT_FFMPEG; MAT_PREFER_FFMPEG = $env:MAT_PREFER_FFMPEG }
    $runs = @(@{ Name = 'ffmpeg'; Prefer = '1'; Decoder = 'ffmpeg (bundled)' })
    if ($ridIsWindows) { $runs += @{ Name = 'media-foundation'; Prefer = $null; Decoder = 'media-foundation' } }
    try {
        $env:MAT_FFMPEG = $null
        foreach ($run in $runs) {
            $env:MAT_PREFER_FFMPEG = $run.Prefer
            $into = Join-Path $smokeDir $run.Name
            New-Item -ItemType Directory -Force -Path $into | Out-Null
            $output = (& $CliPath transcribe $m4a --language $SmokeLanguage --formats json,txt --output $into --threads 4 2>&1 | Out-String)
            $exit = $LASTEXITCODE
            Write-Host ($output.Trim() -replace '(?m)^', '    ')
            if ($exit -ne 0) { throw "the staged command line exited with $exit transcribing the smoke clip through $($run.Name), wanted 0; its output above names the cause" }
            if ($output -notmatch "Decoder:\s+$([regex]::Escape($run.Decoder))") {
                throw "the staged command line did not decode the smoke clip with $($run.Decoder); the package must find its decoder beside the executable, so check the ffmpeg folder and the decoder order"
            }
            $words = if ($output -match 'Words:\s+(\d+) recognised') { [int]$Matches[1] } else { 0 }
            if ($words -lt 20) { throw "the staged command line recognised $words words in the smoke clip through $($run.Name), wanted at least 20; the models in the package may be damaged, run 'models --verify' from the package" }
            Write-Host "    smoke transcription through $($run.Name): $words words"
        }
    } finally {
        $env:MAT_FFMPEG = $saved.MAT_FFMPEG
        $env:MAT_PREFER_FFMPEG = $saved.MAT_PREFER_FFMPEG
        Remove-Item -Recurse -Force $smokeDir -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------------------------------------
if ($sameOs) {
    Step 'smoke test of the staged package'
    # Run with the models variable cleared, so the program must find its models beside itself.
    $savedModels = $env:MAT_MODELS_DIR
    $env:MAT_MODELS_DIR = $null
    try {
        $cliPath = Join-Path $package $cliExe
        $reported = (& $cliPath --version 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $reported -notmatch [regex]::Escape($Version)) {
            throw "the command-line executable printed '$reported' (exit code $LASTEXITCODE) for --version, wanted a line holding $Version; the version is stamped from -p:Version, so check that the publish received it"
        }
        Write-Host "    --version: $reported"

        $models = (& $cliPath models 2>&1 | Out-String).Trim()
        $modelsExit = $LASTEXITCODE
        if ($SkipModels) {
            Write-Host "    models (-SkipModels, exit $modelsExit): the models folder is empty on purpose"
        } elseif ($modelsExit -ne 0) {
            throw "'models' exited with $modelsExit for the staged package, wanted 0 (every model file present):`n$models`nThe package lacks a file the application requires; compare the default set of build/models.lock with the files the application's RecognizerFactory and LoadedModels name"
        } else {
            Write-Host "    models: $($models -replace "`r?`n", ' | ')"
        }

        # All of the output is read before the exit code is, because stopping a pipe early changes it.
        $ffmpegOutput = @(& (Join-Path $ffmpegInto $binary.name) -version 2>&1)
        $ffmpegLine = "$($ffmpegOutput | Select-Object -First 1)"
        if ($LASTEXITCODE -ne 0 -or $ffmpegLine -notmatch 'ffmpeg version') {
            throw "the bundled ffmpeg printed '$ffmpegLine' for -version, wanted a line starting 'ffmpeg version'; the binary may not run on this system"
        }
        Write-Host "    ffmpeg: $ffmpegLine"

        if ($SmokeClip -and -not $SkipModels) {
            if (-not (Test-Path -LiteralPath $SmokeClip -PathType Leaf)) {
                Write-Host "::notice::the smoke clip $SmokeClip does not exist, so the package's transcription is not tested; build it with node bench/testset/fetch-testset.mjs --subset ci"
            } else {
                Invoke-SmokeTranscription -CliPath $cliPath -FfmpegPath (Join-Path $ffmpegInto $binary.name)
            }
        }
    } finally {
        $env:MAT_MODELS_DIR = $savedModels
    }
} else {
    Step 'smoke test skipped (the build machine is not the package''s operating system)'
}

# ---------------------------------------------------------------------------------------------
Step 'archiving'
$archiveName = if ($ridIsWindows) { "$packageName.zip" } else { "$packageName.tar.gz" }
$archive = Join-Path $Output $archiveName
if (Test-Path $archive) { Remove-Item -Force $archive }
$folderBytes = Get-FolderBytes $package
if ($ridIsWindows) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    # CreateFromDirectory handles the multi-hundred-megabyte model files that Compress-Archive may not.
    [System.IO.Compression.ZipFile]::CreateFromDirectory($package, $archive, [System.IO.Compression.CompressionLevel]::Optimal, $true)
} else {
    if (-not (Get-Command tar -ErrorAction SilentlyContinue)) { throw 'tar was not found on the PATH; install it (the tar package) to build the Linux archive' }
    Invoke-Native 'tar' 'the archive could not be written; check the free disk space in the output folder' {
        tar -C $staging --sort=name --owner=0 --group=0 --numeric-owner -czf $archive $packageName
    }
}
$archiveBytes = (Get-Item -LiteralPath $archive).Length
Write-Host "    $archiveName : $archiveBytes bytes ($folderBytes bytes unpacked)"
if (-not $SkipModels -and $archiveBytes -gt $archiveLimit) {
    throw "$archiveName is $archiveBytes bytes, over the $archiveLimit a release asset may hold; remove or shrink a model, or ship the models as a separate asset (decision 0007)"
}

# ---------------------------------------------------------------------------------------------
Step 'measuring'
$sizes = [ordered]@{
    rid          = $Rid
    version      = $Version
    skipModels   = [bool]$SkipModels
    archive      = $archiveName
    archiveBytes = $archiveBytes
    folderBytes  = $folderBytes
    modelsBytes  = $modelsBytes
    ffmpegBytes  = (Get-FolderBytes $ffmpegInto)
}
Write-Json (Join-Path $Output 'sizes.json') $sizes

# Budgets: the sizes a release is expected to have, to within ten percent either way. Growth is a
# dependency that doubled the download; a shrink is a file that went missing.
$tolerance = 0.10
$measured = [ordered]@{ archiveBytes = $archiveBytes; folderBytes = $folderBytes }
$budgets = if (Test-Path $budgetsPath) { Get-Content -LiteralPath $budgetsPath -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
if (-not $budgets.ContainsKey('budgets')) { $budgets['budgets'] = @{} }
if ($RecordBudgets) {
    $budgets['budgets'][$Rid] = $measured
    $budgets['comment'] = 'Expected sizes in bytes of each platform''s full package, checked by build/publish.ps1 with a tolerance of ten percent. Rewritten only by a run with -RecordBudgets (the release workflow''s record_budgets input) in a pull request that shows the numbers moving.'
    $sorted = [ordered]@{}
    foreach ($key in ($budgets['budgets'].Keys | Sort-Object)) { $sorted[$key] = $budgets['budgets'][$key] }
    Write-Json $budgetsPath ([ordered]@{ comment = $budgets['comment']; budgets = $sorted })
    Step "recorded the budgets for $Rid in build/size-budgets.json"
} elseif ($SkipModels) {
    Write-Host '    sizes are not checked for a -SkipModels package'
} elseif (-not $budgets['budgets'].ContainsKey($Rid)) {
    Write-Host "::notice::build/size-budgets.json has no budget for $Rid, so its size is not checked; record one with -RecordBudgets"
} else {
    $failures = @()
    foreach ($key in $measured.Keys) {
        $budget = [double]$budgets['budgets'][$Rid][$key]
        $actual = [double]$measured[$key]
        if ($actual -gt $budget * (1 + $tolerance)) {
            $failures += "$key is $actual bytes, over the budget of $budget by more than ten percent; find what grew, or record the new size with -RecordBudgets in a pull request"
        } elseif ($actual -lt $budget * (1 - $tolerance)) {
            $failures += "$key is $actual bytes, under the budget of $budget by more than ten percent; a file may be missing from the package, or record the new size with -RecordBudgets"
        }
    }
    if ($failures.Count -gt 0) { throw "the package size is outside its budget:`n  $($failures -join "`n  ")" }
    Write-Host '    within budget'
}

Step "done: $archive"
