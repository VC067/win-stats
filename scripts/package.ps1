param(
    [string]$Version = '0.1.0',
    [switch]$SkipInstaller,
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') {
    throw "Version '$Version' is not a numeric semantic version with an optional prerelease."
}

$installerVersion = ($Version -split '-', 2)[0]
$projectPath = Join-Path $PSScriptRoot '..\src\StatsBar\StatsBar.csproj'
$installerScript = Join-Path $PSScriptRoot '..\installer\WinStats.iss'
$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot 'artifacts'))
$publishPath = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'publish\win-x64'))
$releasePath = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'release'))

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null

function Clear-ArtifactDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedRoot = [System.IO.Path]::GetFullPath($artifactsRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $rootPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar

    if (-not $resolvedPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear a path outside the artifacts directory: $resolvedPath"
    }

    $currentPath = $resolvedPath
    while ($currentPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or $currentPath.Equals($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath $currentPath) {
            $attributes = [System.IO.File]::GetAttributes($currentPath)
            if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to clear artifacts through a linked path: $currentPath"
            }
        }
        if ($currentPath.Equals($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $currentPath = [System.IO.Directory]::GetParent($currentPath).FullName
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        $item = Get-Item -LiteralPath $resolvedPath -Force
        if (-not $item.PSIsContainer) {
            throw "Refusing to recursively delete a non-directory artifact path: $resolvedPath"
        }
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

Clear-ArtifactDirectory -Path $publishPath
Clear-ArtifactDirectory -Path $releasePath
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null

& dotnet publish $projectPath --configuration Release --runtime win-x64 --self-contained true --output $publishPath -p:Version=$Version -p:PublishTrimmed=false -p:PublishAot=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$licensePath = Join-Path $workspaceRoot 'LICENSE'
$noticesPath = Join-Path $workspaceRoot 'THIRD_PARTY_NOTICES.md'
if (Test-Path -LiteralPath $licensePath -PathType Leaf) {
    Copy-Item -LiteralPath $licensePath -Destination $publishPath
}
if (Test-Path -LiteralPath $noticesPath -PathType Leaf) {
    Copy-Item -LiteralPath $noticesPath -Destination $publishPath
}

$runtimeConfigPath = Join-Path $publishPath 'WinStats.runtimeconfig.json'
$assetsPath = Join-Path $workspaceRoot 'src\StatsBar\obj\project.assets.json'
if (-not (Test-Path -LiteralPath $runtimeConfigPath -PathType Leaf)) {
    throw "Published runtime configuration was not found: $runtimeConfigPath"
}
if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
    throw ".NET restore assets were not found: $assetsPath"
}

$runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$includedFrameworks = $runtimeConfig.runtimeOptions.includedFrameworks
if ($null -eq $includedFrameworks) {
    throw "Published runtime configuration has no includedFrameworks list: $runtimeConfigPath"
}

$noticePackages = @(
    @{
        Framework = 'Microsoft.NETCore.App'
        Package = 'microsoft.netcore.app.runtime.win-x64'
        Files = @(
            @{ Source = 'LICENSE.TXT'; Destination = 'Microsoft.NETCore.App-LICENSE.txt' }
            @{ Source = 'THIRD-PARTY-NOTICES.TXT'; Destination = 'Microsoft.NETCore.App-THIRD-PARTY-NOTICES.txt' }
        )
        Directory = 'dotnet'
    }
    @{
        Framework = 'Microsoft.WindowsDesktop.App'
        Package = 'microsoft.windowsdesktop.app.runtime.win-x64'
        Files = @(
            @{ Source = 'LICENSE'; Destination = 'Microsoft.WindowsDesktop.App-LICENSE.txt' }
        )
        Directory = 'windowsdesktop'
    }
)

$packageFolders = @($assets.packageFolders.PSObject.Properties.Name)
foreach ($noticePackage in $noticePackages) {
    $framework = $includedFrameworks | Where-Object { $_.name -eq $noticePackage.Framework } | Select-Object -First 1
    if ($null -eq $framework) {
        throw "Published runtime configuration does not include the expected framework $($noticePackage.Framework)."
    }

    $packageDirectories = foreach ($packageFolder in $packageFolders) {
        [System.IO.Path]::GetFullPath((Join-Path (Join-Path $packageFolder $noticePackage.Package) $framework.version))
    }
    $packageDirectory = $packageDirectories | Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($packageDirectory)) {
        throw "Runtime package $($noticePackage.Package) version $($framework.version) was not found in the restored NuGet package folders."
    }

    $noticeDirectory = Join-Path (Join-Path $publishPath 'notices') $noticePackage.Directory
    New-Item -ItemType Directory -Path $noticeDirectory -Force | Out-Null
    foreach ($noticeFile in $noticePackage.Files) {
        $sourcePath = Join-Path $packageDirectory $noticeFile.Source
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Required runtime notice file was not found: $sourcePath"
        }
        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $noticeDirectory $noticeFile.Destination)
    }
}

$portableZip = Join-Path $releasePath 'WinStats-portable-x64.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $publishPath,
    $portableZip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)

$artifactPaths = [System.Collections.Generic.List[string]]::new()
$artifactPaths.Add($portableZip)

if (-not $SkipInstaller) {
    if ([string]::IsNullOrWhiteSpace($IsccPath)) {
        $programFilesPaths = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
        $isccCandidates = foreach ($programFilesPath in $programFilesPaths) {
            Join-Path $programFilesPath 'Inno Setup 7\ISCC.exe'
            Join-Path $programFilesPath 'Inno Setup 6\ISCC.exe'
        }
        $IsccPath = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }

    if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
        throw 'Inno Setup compiler was not found. Pass -IsccPath or install Inno Setup 6 or 7.'
    }

    $resolvedIsccPath = (Resolve-Path -LiteralPath $IsccPath).Path
    $installerOutput = Join-Path $releasePath 'WinStats-Setup-x64.exe'
    & $resolvedIsccPath /Q "/DAppVersion=$installerVersion" "/DPublishDir=$publishPath" "/DOutputDir=$releasePath" $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
    }
    if (-not (Test-Path -LiteralPath $installerOutput -PathType Leaf)) {
        throw "Inno Setup did not produce the expected installer: $installerOutput"
    }
    $artifactPaths.Add($installerOutput)
}

$checksumLines = foreach ($artifactPath in ($artifactPaths | Sort-Object { [System.IO.Path]::GetFileName($_) })) {
    $hash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
    '{0}  {1}' -f $hash, [System.IO.Path]::GetFileName($artifactPath)
}
Set-Content -LiteralPath (Join-Path $releasePath 'SHA256SUMS.txt') -Value $checksumLines -Encoding ascii
