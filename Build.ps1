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
    } else {
        throw 'Python 3 is required to prepare the upstream source.'
    }
    if ($LASTEXITCODE -ne 0) { throw 'Source preparation failed.' }
}

$project = '.\DISM GUI\DISM GUI.vbproj'
if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}
$runtimes = if ($Runtime -eq 'all') {
    @('win-x86','win-x64','win-arm64')
} else {
    @($Runtime)
}

foreach ($rid in $runtimes) {
    Write-Host "Publishing $rid ..."
    $publishDir = Join-Path $PSScriptRoot "artifacts\$rid"
    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Force -Recurse
    }
    & dotnet publish $project -c Release -r $rid --self-contained true '-p:PublishSingleFile=true' '-p:PublishReadyToRun=false' '-p:Platform=AnyCPU' -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $rid" }

    $exe = Join-Path $publishDir 'DISM GUI.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "Portable DISM GUI.exe was not generated for $rid"
    }

    # Remove only debug symbols and the three legacy DISM GUI config variants.
    # Runtime dependencies, if any, must remain in the package.
    Get-ChildItem -LiteralPath $publishDir -File | Where-Object {
        $_.Extension -ieq '.pdb' -or
        $_.Name -ieq 'DISM GUI.dll.config' -or
        $_.Name -ieq 'DISM GUI.exe.config' -or
        $_.Name -ieq 'DISM GUI.config'
    } | Remove-Item -Force

    $extraFiles = @(Get-ChildItem -LiteralPath $publishDir -File | Where-Object {
        $_.Extension -ieq '.pdb' -or
        $_.Name -ieq 'DISM GUI.dll.config' -or
        $_.Name -ieq 'DISM GUI.exe.config' -or
        $_.Name -ieq 'DISM GUI.config'
    })
    if ($extraFiles.Count -ne 0) {
        throw "PDB or config files still exist in the $rid portable directory"
    }

    $zip = Join-Path $PSScriptRoot "artifacts\DISMGUI-VS2026-$rid.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip
    Write-Host "Created $zip"
}
