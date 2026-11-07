# Copy-only OEM verification on disposable GitHub-hosted Windows VM.
# Uses no production VM, user credentials, or licensed installation downloads.
param([string]$ReportDirectory = (Join-Path $env:RUNNER_TEMP 'dismgui-oem-vm-report'))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$reportFile = Join-Path $ReportDirectory 'oem-vm-result.json'
$work = Join-Path $env:RUNNER_TEMP ('DismOemVm-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null

$report = [ordered]@{
    VM = 'GitHub Actions windows-2025 hosted virtual machine'
    OS = [Environment]::OSVersion.VersionString
    TestMode = 'Copy-only; input WIM and host OS remain untouched'
    SelfTest = 'NOT_RUN'
    RealWindowsWim = 'NOT_FOUND'
    RealWindowsWimType = 'NOT_FOUND'
    ThirdPartyDriverEntries = 0
    SignedThirdPartyInf = 'NOT_FOUND'
    SignedCat = 'NOT_FOUND'
    Plan = 'NOT_RUN'
    CopyOnlyImport = 'NOT_RUN'
    Result = 'INCOMPLETE'
    Notes = @()
}
$exitCode = 0
try {
    Write-Host 'Running safety tests on ephemeral hosted Windows VM.'
    dotnet run --project 'tests/RealOemIntegration/RealOemIntegration.csproj' --configuration Release -- --self-test
    if ($LASTEXITCODE -ne 0) { throw 'OEM runner safety self-test failed.' }
    $report.SelfTest = 'PASS'
    Write-Host 'PASS: copy-only OEM runner argument, path and guard tests.'

    # Look for an actual WinRE/WinPE servicing WIM already provided by the VM.
    # A synthetic captured WIM is NOT a substitute for a real Windows image.
    $winKit = [Environment]::GetFolderPath('ProgramFilesX86')
    $candidates = @(
        (Join-Path $env:windir 'System32\Recovery\Winre.wim'),
        'C:\Recovery\WindowsRE\Winre.wim',
        (Join-Path $winKit 'Windows Kits\10\Assessment and Deployment Kit\Windows Preinstallation Environment\amd64\en-us\winpe.wim'),
        (Join-Path $winKit 'Windows Kits\10\Assessment and Deployment Kit\Windows Preinstallation Environment\amd64\winpe.wim')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -Unique

    $realWim = $null
    foreach ($path in $candidates) {
        try {
            $null = & dism.exe /English /Get-WimInfo "/WimFile:$path" 2>&1
            if ($LASTEXITCODE -eq 0) {
                $realWim = $path
                $report.RealWindowsWim = Split-Path -Leaf $path
                $report.RealWindowsWimType = if ($path -match '(?i)winre') { 'Windows RE' } else { 'Windows PE' }
                Write-Host "Found genuine Windows servicing image: $($report.RealWindowsWimType)."
                break
            }
        } catch {
            $report.Notes += ('WIM inspection exception: ' + $_.Exception.GetType().Name)
        }
    }
    if (-not $realWim) {
        $report.Notes += 'No real WinRE/WinPE image found on hosted VM; real OEM import cannot run.'
    }

    # Only a real third-party package with a valid signed CAT qualifies.
    $drivers = @()
    try {
        Import-Module Dism -ErrorAction Stop
        $drivers = @(Get-WindowsDriver -Online -ErrorAction Stop | Where-Object { $_.Inbox -eq $false })
        $report.ThirdPartyDriverEntries = $drivers.Count
        Write-Host ("Third-party packages installed on VM: {0}" -f $drivers.Count)
    } catch {
        $report.Notes += ('Third-party inventory unavailable: ' + $_.Exception.Message)
    }

    $signedInf = $null
    if ($realWim -and $drivers.Count -gt 0) {
        foreach ($entry in ($drivers | Select-Object -First 100)) {
            $inf = [string]$entry.OriginalFileName
            if (-not $inf -or -not (Test-Path -LiteralPath $inf -PathType Leaf)) { continue }
            if ([IO.Path]::GetExtension($inf) -ne '.inf') { continue }
            $root = Split-Path -Parent $inf
            $cats = @(Get-ChildItem -LiteralPath $root -Filter '*.cat' -File -ErrorAction SilentlyContinue)
            foreach ($cat in $cats) {
                $signature = Get-AuthenticodeSignature -LiteralPath $cat.FullName -ErrorAction SilentlyContinue
                if ($null -ne $signature -and "$($signature.Status)" -eq 'Valid') {
                    $signedInf = $inf
                    $report.SignedThirdPartyInf = [IO.Path]::GetFileName($inf)
                    $report.SignedCat = $cat.Name
                    break
                }
            }
            if ($signedInf) { break }
        }
    }
    if (-not $signedInf) {
        $report.Notes += 'No signed third-party INF/CAT fixture found for servicing test.'
    }

    if ($realWim -and $signedInf) {
        $inputArgs = @('--wim', $realWim, '--index', '1', '--inf', $signedInf, '--work-root', $work)
        Write-Host 'Running genuine Windows image + signed driver read-only preflight.'
        dotnet run --project 'tests/RealOemIntegration/RealOemIntegration.csproj' --configuration Release --no-build -- --plan @inputArgs
        if ($LASTEXITCODE -ne 0) {
            $report.Plan = 'FAIL'
            $report.Result = 'FAIL_REAL_MEDIA_PREFLIGHT'
            $exitCode = 1
        } else {
            $report.Plan = 'PASS'
            Write-Host 'Testing import, commit and read-only remount on disposable WIM copy.'
            dotnet run --project 'tests/RealOemIntegration/RealOemIntegration.csproj' --configuration Release --no-build -- --execute --confirm TEST_COPY_ONLY @inputArgs
            if ($LASTEXITCODE -ne 0) {
                $report.CopyOnlyImport = 'FAIL'
                $report.Result = 'FAIL_COPY_ONLY_IMPORT'
                $exitCode = 1
            } else {
                $report.CopyOnlyImport = 'PASS'
                $report.Result = 'PASS_REAL_WINDOWS_SERVICING'
            }
        }
    } else {
        $report.Plan = 'SKIP_MISSING_VM_FIXTURES'
        $report.CopyOnlyImport = 'SKIP_MISSING_VM_FIXTURES'
        $report.Result = 'PASS_SAFETY_CHECKS_REAL_MEDIA_UNAVAILABLE'
    }
} catch {
    $report.Result = 'FAIL_RUNNER_OR_INFRASTRUCTURE'
    $report.Notes += $_.Exception.Message
    Write-Error $_.Exception.Message -ErrorAction Continue
    $exitCode = 1
} finally {
    try {
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportFile -Encoding UTF8
        Write-Host "VM validation result: $($report.Result)"
        Write-Host "Diagnostic report: $reportFile"
    } catch {
        Write-Error "Unable to save VM report: $($_.Exception.Message)" -ErrorAction Continue
        $exitCode = 1
    }
}
exit $exitCode
