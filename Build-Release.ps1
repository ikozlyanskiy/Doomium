param(
    [string]$AltiumInstallDir = 'C:\Program Files\Altium\AD25'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$projectDir = Join-Path $root 'Doomium\Doomium'
$project = Join-Path $projectDir 'Doomium.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
$name = "Doomium-v$version-ad25-win-x64"
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$stage = Join-Path $dist ".staging-$([guid]::NewGuid().ToString('N'))"
$stagePath = [System.IO.Path]::GetFullPath($stage)
$distPath = [System.IO.Path]::GetFullPath($dist).TrimEnd('\') + '\'
if (-not $stagePath.StartsWith($distPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release staging path is outside dist.'
}
$zip = Join-Path $dist "$name.zip"

Push-Location $projectDir
try {
    & dotnet build $project -c Release --nologo -clp:ErrorsOnly "-p:AltiumInstallDir=$AltiumInstallDir"
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}
finally { Pop-Location }

try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stage 'docs\assets') -Force | Out-Null
    $items = @{
        'Doomium.dll' = (Join-Path $projectDir 'bin\Release\net6.0-windows\Doomium.dll')
        'Doomium.ins' = (Join-Path $projectDir 'Doomium.ins')
        'Doomium.rcs' = (Join-Path $projectDir 'Doomium.rcs')
        'Install-Doomium.ps1' = (Join-Path $projectDir 'Install-Doomium.ps1')
        'README.md' = (Join-Path $root 'README.md')
        'LICENSE' = (Join-Path $root 'LICENSE')
        'THIRD_PARTY_NOTICES.md' = (Join-Path $root 'THIRD_PARTY_NOTICES.md')
        'docs\assets\doomium-in-altium.png' = (Join-Path $root 'docs\assets\doomium-in-altium.png')
    }
    foreach ($nameInZip in $items.Keys) {
        $source = $items[$nameInZip]
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Release file missing: $source" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $stage $nameInZip)
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $temporaryZip = Join-Path $dist ".staging-$([guid]::NewGuid().ToString('N')).zip"
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryZip)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($temporaryZip)
    try {
        $actual = @($archive.Entries | Where-Object { $_.Name } |
            ForEach-Object { $_.FullName.Replace('/', '\') } | Sort-Object)
        $expected = @($items.Keys | Sort-Object)
        if (($actual -join '|') -ne ($expected -join '|')) {
            throw "Unexpected ZIP contents: $($actual -join ', ')"
        }
    }
    finally { $archive.Dispose() }
    Move-Item -LiteralPath $temporaryZip -Destination $zip -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$zip.sha256" -Value "$hash  $([System.IO.Path]::GetFileName($zip))" -Encoding Ascii
    Write-Host "Release: $zip"
    Write-Host "SHA-256: $hash"
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
