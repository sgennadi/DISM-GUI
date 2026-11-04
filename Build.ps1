param(
    [ValidateSet('win-x86','win-x64','win-arm64','all')]
    [string]$Runtime = 'all',
    [switch]$SkipPrepare
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not $SkipPrepare) {
    if (Get-Command py -ErrorAction SilentlyContinue) {
        & py -3 '.\tools\prepare_source.py'
    } elseif (Get-Command python -ErrorAction SilentlyContinue) {
        & python '.\tools\prepare_source.py'
    } else { throw 'Python 3 is required to prepare upstream source.' }
    if ($LASTEXITCODE -ne 0) { throw 'Source preparation failed.' }
}
$project = '.\DISM GUI\DISM GUI.vbproj'
if (-not (Test-Path $project)) { throw "Project not found: $project" }
$runtimes = if ($Runtime -eq 'all') { @('win-x86','win-x64','win-arm64') } else { @($Runtime) }
foreach ($rid in $runtimes) {
    Write-Host "Publishing $rid ..."
    & dotnet publish $project -c Release -r $rid --self-contained true '-p:PublishSingleFile=true' '-p:PublishReadyToRun=false' '-p:Platform=AnyCPU' -o ".\artifacts\$rid"
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $rid" }
    $publishDir = Join-Path $PSScriptRoot "artifacts\$rid"
    $publishedExe = Join-Path $publishDir 'DISM GUI.exe'
    if (-not (Test-Path -LiteralPath $publishedExe)) { throw "DISM GUI.exe not found for $rid" }

    # Leave only runtime files in portable archives: remove legacy configuration and debug symbols.
    Get-ChildItem -LiteralPath $publishDir -File | Where-Object {
        $_.Name -match '(?i)\.pdb
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path ".\artifacts\$rid\*" -DestinationPath $zip
    Write-Host "Created $zip"
}
 -or $_.Name -match '(?i)^DISM GUI(\.(dll|exe))?\.config
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path ".\artifacts\$rid\*" -DestinationPath $zip
    Write-Host "Created $zip"
}

    } | Remove-Item -Force
    $unwanted = @(Get-ChildItem -LiteralPath $publishDir -File | Where-Object {
        $_.Name -match '(?i)\.pdb
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path ".\artifacts\$rid\*" -DestinationPath $zip
    Write-Host "Created $zip"
}
 -or $_.Name -match '(?i)^DISM GUI(\.(dll|exe))?\.config
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path ".\artifacts\$rid\*" -DestinationPath $zip
    Write-Host "Created $zip"
}

    })
    if ($unwanted.Count -gt 0) { throw "Unwanted portable output files remain for $rid" }
    $zip = Join-Path $PSScriptRoot "artifacts\DISMGUI-VS2026-$rid.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path ".\artifacts\$rid\*" -DestinationPath $zip
    Write-Host "Created $zip"
}
