param(
    [string]$ProjectDir = $PSScriptRoot,
    [string]$AltiumInstallDir = 'C:\Program Files\Altium\AD25',
    [string]$AltiumDataRoot = '',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Prebuilt,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$ProjectDir = (Resolve-Path -LiteralPath $ProjectDir).Path
$AltiumInstallDir = [System.IO.Path]::GetFullPath($AltiumInstallDir)
$project = Join-Path $ProjectDir 'Doomium.csproj'
$vendor = Join-Path $ProjectDir 'vendor\managed-doom\ManagedDoom\src'
$resourceSource = Join-Path $ProjectDir 'Doomium.rcs'
$pcbResources = Join-Path $AltiumInstallDir 'System\AdvPcb.rcs'
$runtimeConfig = Join-Path $AltiumInstallDir 'X2.runtimeconfig.json'
if (-not $AltiumDataRoot) {
    $dataParent = Join-Path $env:ProgramData 'Altium'
    $candidates = @(Get-ChildItem -LiteralPath $dataParent -Directory -ErrorAction Stop |
        Where-Object { $_.Name -like 'Altium Designer {*}' -and
            (Test-Path -LiteralPath (Join-Path $_.FullName 'Extensions\ExtensionsRegistry.xml')) })
    if ($candidates.Count -ne 1) {
        throw "Found $($candidates.Count) Altium data directories. Pass -AltiumDataRoot with the intended Altium Designer directory."
    }
    $AltiumDataRoot = $candidates[0].FullName
}
$AltiumDataRoot = [System.IO.Path]::GetFullPath($AltiumDataRoot)
$extensions = Join-Path $AltiumDataRoot 'Extensions'
$registry = Join-Path $extensions 'ExtensionsRegistry.xml'
$destination = Join-Path $extensions 'Doomium'

if (-not $Prebuilt) {
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Project not found: $project" }
    if (-not (Test-Path -LiteralPath $vendor -PathType Container)) { throw "ManagedDoom sources not found: $vendor" }
}
elseif (-not (Test-Path -LiteralPath (Join-Path $ProjectDir 'Doomium.dll') -PathType Leaf)) {
    throw "Prebuilt Doomium.dll not found in $ProjectDir"
}
if (-not (Test-Path -LiteralPath $registry)) { throw "Altium registry not found: $registry" }
if (-not (Test-Path -LiteralPath $resourceSource)) { throw "Doomium resources not found: $resourceSource" }
if (-not (Test-Path -LiteralPath (Join-Path $ProjectDir 'Doomium.ins') -PathType Leaf)) {
    throw "Doomium.ins not found in $ProjectDir"
}
if (-not (Test-Path -LiteralPath $pcbResources)) { throw "PCB resources not found: $pcbResources" }
if (-not (Test-Path -LiteralPath $runtimeConfig)) { throw "Altium runtime config not found: $runtimeConfig" }
$hostFramework = (Get-Content -LiteralPath $runtimeConfig -Raw | ConvertFrom-Json).runtimeOptions.tfm

if (-not $Prebuilt) {
    [xml]$projectXml = Get-Content -LiteralPath $project -Raw
    $targetFramework = $projectXml.Project.PropertyGroup.TargetFramework
    if ($targetFramework -ne "$hostFramework-windows") {
        throw "Doomium targets $targetFramework, but this Altium installation hosts $hostFramework."
    }
}
elseif ($hostFramework -ne 'net6.0') {
    throw "This prebuilt Doomium targets net6.0, but this Altium installation hosts $hostFramework."
}

function Assert-DoomiumResources {
    param([string]$ResourcePath, [string]$PcbResourcePath)

    $lines = @(Get-Content -LiteralPath $ResourcePath |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -ne '' -and -not $_.StartsWith('//') })
    $pcb = Get-Content -LiteralPath $PcbResourcePath -Raw
    $launchers = @{}
    $usedLaunchers = @{}
    $resourceIds = @{}
    $insertions = 0

    foreach ($line in $lines) {
        if ($line -eq 'PL') {
            throw 'Invalid RCS: a PL name must be on the same line as PL.'
        }
        if ($line -match '^PL\s+(\S+)$') {
            if ($launchers.ContainsKey($Matches[1])) { throw "Duplicate PLID: $($Matches[1])" }
            $launchers[$Matches[1]] = $true
        }
    }

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch '^Insertion\b') { continue }
        if ($lines[$i] -notmatch "^Insertion\s+(\S+)\s+TargetID='([^']+)'\s+RefID0='([^']+)'\s+InsertType='(After|Before)'$") {
            throw "Invalid Insertion header: $($lines[$i])"
        }
        $insertionId = $Matches[1]
        $targetId = $Matches[2]
        $referenceId = $Matches[3]
        if ($resourceIds.ContainsKey($insertionId)) { throw "Duplicate resource ID: $insertionId" }
        $resourceIds[$insertionId] = $true

        if ($i + 2 -ge $lines.Count -or
            $lines[$i + 1] -notmatch "^Link\s+(\S+)\s+PLID='([^']+)'\s+End$" -or
            $lines[$i + 2] -ne 'End') {
            throw "Invalid Insertion $insertionId`: exactly one Link is required."
        }
        $linkId = $Matches[1]
        $launcherId = $Matches[2]
        if ($resourceIds.ContainsKey($linkId)) { throw "Duplicate resource ID: $linkId" }
        $resourceIds[$linkId] = $true
        if (-not $launchers.ContainsKey($launcherId)) { throw "Undefined PLID: $launcherId" }
        if ($usedLaunchers.ContainsKey($launcherId)) { throw "PLID used more than once: $launcherId" }
        $usedLaunchers[$launcherId] = $true

        $escapedTarget = [regex]::Escape($targetId)
        $escapedReference = [regex]::Escape($referenceId)
        if ($pcb -notmatch "(?m)^\s*Tree\s+$escapedTarget(?=\s)") {
            throw "TargetID is absent from AdvPcb.rcs: $targetId"
        }
        if ($pcb -notmatch "(?m)^\s*(?:Tree|Link|Separator)\s+$escapedReference(?=\s)") {
            throw "RefID0 is absent from AdvPcb.rcs: $referenceId"
        }
        $insertions++
        $i += 2
    }

    if ($launchers.Count -lt 1 -or $launchers.Count -ne $insertions) {
        throw "Every PL needs one Insertion; found $($launchers.Count) PL definitions and $insertions Insertions."
    }
}

Assert-DoomiumResources -ResourcePath $resourceSource -PcbResourcePath $pcbResources

[xml]$xml = Get-Content -LiteralPath $registry -Raw
if (-not $xml.DocumentElement -or $xml.DocumentElement.Name -ne 'Extensions') {
    throw "Unexpected Altium extension registry format: $registry"
}
$existing = $xml.SelectSingleNode("/Extensions/Item[@HRID='Doomium']")
if ($existing) {
    $pathNode = $existing.SelectSingleNode('Path')
    if (-not $pathNode -or -not $pathNode.InnerText) { throw 'Registered Doomium path is empty.' }
    $registeredPath = [System.IO.Path]::GetFullPath($pathNode.InnerText).TrimEnd('\')
    if (-not [string]::Equals($registeredPath, $destination.TrimEnd('\'),
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Registered Doomium path differs from the target: $registeredPath"
    }
}

if ($ValidateOnly) {
    Write-Host "Doomium validation passed for $AltiumInstallDir and $AltiumDataRoot"
    return
}

if (Get-Process -Name X2 -ErrorAction SilentlyContinue) {
    throw 'Close Altium Designer and save open documents before installing Doomium.'
}

if (-not $Prebuilt) {
    Push-Location $ProjectDir
    try {
        & dotnet build $project -c $Configuration --nologo -clp:ErrorsOnly "-p:AltiumInstallDir=$AltiumInstallDir"
        if ($LASTEXITCODE -ne 0) { throw 'Doomium build failed; nothing was installed.' }
    }
    finally { Pop-Location }
}

$dll = if ($Prebuilt) { Join-Path $ProjectDir 'Doomium.dll' }
    else { Join-Path $ProjectDir "bin\$Configuration\net6.0-windows\Doomium.dll" }
$files = @('Doomium.dll', 'Doomium.ins', 'Doomium.rcs')
$sources = @(
    $dll,
    (Join-Path $ProjectDir 'Doomium.ins'),
    (Join-Path $ProjectDir 'Doomium.rcs')
)
foreach ($source in $sources) {
    if (-not (Test-Path -LiteralPath $source)) { throw "Required file not found: $source" }
}
$version = ([System.Reflection.AssemblyName]::GetAssemblyName($dll)).Version.ToString(3)

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $ProjectDir ".doomium-install-backup-$stamp"
if (Test-Path -LiteralPath $backup) { $backup += "-$([guid]::NewGuid().ToString('N').Substring(0, 8))" }
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item -LiteralPath $registry -Destination (Join-Path $backup 'ExtensionsRegistry.xml')
$destinationExisted = Test-Path -LiteralPath $destination -PathType Container

foreach ($name in $files) {
    $installed = Join-Path $destination $name
    if (Test-Path -LiteralPath $installed) {
        Copy-Item -LiteralPath $installed -Destination (Join-Path $backup $name)
    }
}

$registryChanged = $false
try {
    if (-not $destinationExisted) { New-Item -ItemType Directory -Path $destination | Out-Null }
    for ($i = 0; $i -lt $files.Count; $i++) {
        Copy-Item -LiteralPath $sources[$i] -Destination (Join-Path $destination $files[$i]) -Force
    }

    for ($i = 0; $i -lt $files.Count; $i++) {
        $sourceHash = (Get-FileHash -LiteralPath $sources[$i] -Algorithm SHA256).Hash
        $targetHash = (Get-FileHash -LiteralPath (Join-Path $destination $files[$i]) -Algorithm SHA256).Hash
        if ($sourceHash -ne $targetHash) { throw "Hash mismatch: $($files[$i])" }
    }

    if (-not $existing) {
        $item = $xml.CreateElement('Item')
        $item.SetAttribute('HRID', 'Doomium')
        $guid = [guid]::NewGuid().ToString().ToUpperInvariant()
        $item.SetAttribute('Guid', $guid)
        $now = ([datetime]::UtcNow.ToOADate()).ToString([cultureinfo]::InvariantCulture)
        $properties = [ordered]@{
            Path = $destination; Status = '0'; VaultGuid = ''; CreatedBy = 'Doomium'
            CategoryGuid = '793A1F67-0B22-4E01-A5DE-3176A1E8C60D'; CategoryName = ''
            ReadMe = ''; Help = ''; Requirements = ''; Title = 'Doomium'
            ShortDescription = 'DOOM inside Altium Designer'
            LongDescription = 'DOOM rendered on the PCB canvas'
            SmallImage = ''; LargeImage = ''; Version = $version; VersionGuid = $guid
            ReleasedDate = $now; ReleaseNotes = ''; DateInstalled = $now
        }
        foreach ($key in $properties.Keys) {
            $node = $xml.CreateElement($key)
            $node.InnerText = [string]$properties[$key]
            [void]$item.AppendChild($node)
        }
        $platform = $xml.CreateElement('PlatformVersions')
        foreach ($name in @('DXP', 'EDP', 'MaxDXP', 'MaxEDP')) {
            $node = $xml.CreateElement($name)
            $build = if ($name.StartsWith('Max')) { '0.0.0.0' } else { '1.0.16.54' }
            $node.SetAttribute('BuildNumber', $build)
            [void]$platform.AppendChild($node)
        }
        [void]$item.AppendChild($platform)
        [void]$xml.DocumentElement.AppendChild($item)
        $registryChanged = $true
    }
    else {
        $versionNode = $existing.SelectSingleNode('Version')
        if (-not $versionNode) {
            $versionNode = $xml.CreateElement('Version')
            [void]$existing.AppendChild($versionNode)
        }
        if ($versionNode.InnerText -ne $version) {
            $versionNode.InnerText = $version
            $registryChanged = $true
        }
    }
    if ($registryChanged) {
        $temporaryRegistry = Join-Path $backup 'ExtensionsRegistry.new.xml'
        $xml.Save($temporaryRegistry)
        Copy-Item -LiteralPath $temporaryRegistry -Destination $registry -Force
        [xml]$check = Get-Content -LiteralPath $registry -Raw
        $checkItem = $check.SelectSingleNode("/Extensions/Item[@HRID='Doomium']")
        if (-not $checkItem -or $checkItem.SelectSingleNode('Version').InnerText -ne $version) {
            throw 'Doomium registration could not be verified.'
        }
    }
}
catch {
    if ($registryChanged) {
        Copy-Item -LiteralPath (Join-Path $backup 'ExtensionsRegistry.xml') -Destination $registry -Force
    }
    foreach ($name in $files) {
        $saved = Join-Path $backup $name
        $installed = Join-Path $destination $name
        if (Test-Path -LiteralPath $saved) {
            Copy-Item -LiteralPath $saved -Destination $installed -Force
        }
        elseif (Test-Path -LiteralPath $installed) {
            Remove-Item -LiteralPath $installed -Force
        }
    }
    if (-not $destinationExisted -and (Test-Path -LiteralPath $destination)) {
        if (@(Get-ChildItem -LiteralPath $destination -Force).Count -eq 0) {
            Remove-Item -LiteralPath $destination
        }
    }
    throw
}

Write-Host "Doomium $version installed: $destination"
Write-Host "Backup: $backup"
Write-Host 'Start Altium Designer, open a PcbDoc, select a rectangular fill or four tracks forming a rectangle, and use Tools > Convert > Rect to Doomium.'
if (Test-Path -LiteralPath (Join-Path $destination 'freedoom2.wad')) {
    Write-Host 'Freedoom 2 IWAD found; playback can start without choosing a WAD.'
}
else {
    Write-Host 'A DOOM or Freedoom IWAD (.wad) is required when playback starts.'
}
