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
    DriverCountBefore = $null
    DriverCountAfterStage = $null
    DriverCountAfterCommit = $null
    StagedConfirmed = $false
    CommittedConfirmed = $false
    OriginalWimSha256Unchanged = $false
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

    # Diagnostic comparison: production Driver Studio uses WindowsPowerShell
    # 5.1 with UTF-16 EncodedCommand, while the CI runner uses PowerShell 7.
    function Inspect-WindowsPSSignature([string]$catalogPath) {
        $windowsPS = Join-Path $env:windir 'System32\WindowsPowerShell\v1.0\powershell.exe'
        if (-not (Test-Path -LiteralPath $windowsPS)) {
            return @{ ExitCode = -1; Stdout = ''; Stderr = 'Windows PowerShell not found' }
        }
        $literal = "'" + $catalogPath.Replace("'", "''") + "'"
        $command = '$ErrorActionPreference=''Stop''; (Get-AuthenticodeSignature -LiteralPath ' + $literal + ').Status.ToString()'
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        $psi = [Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = $windowsPS
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        foreach ($arg in @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded)) {
            [void]$psi.ArgumentList.Add($arg)
        }
        $p = [Diagnostics.Process]::new()
        $p.StartInfo = $psi
        try {
            if (-not $p.Start()) { throw 'Could not launch Windows PowerShell.' }
            $outTask = $p.StandardOutput.ReadToEndAsync()
            $errTask = $p.StandardError.ReadToEndAsync()
            if (-not $p.WaitForExit(20000)) {
                $p.Kill($true)
                return @{ ExitCode = -2; Stdout = ''; Stderr = 'Timeout' }
            }
            return @{ ExitCode = $p.ExitCode; Stdout = $outTask.GetAwaiter().GetResult().Trim();
                      Stderr = $errTask.GetAwaiter().GetResult().Trim() }
        } finally {
            $p.Dispose()
        }
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
                    $ps5 = Inspect-WindowsPSSignature $cat.FullName
                    Write-Host ("CAT signature comparison: pwsh7=Valid WinPSExit={0} WinPSStatus={1}" -f $ps5.ExitCode, $ps5.Stdout)
                    if ($ps5.ExitCode -ne 0) {
                        $shortError = $ps5.Stderr
                        if ($shortError.Length -gt 2000) {
                            $shortError = $shortError.Substring($shortError.Length - 2000)
                        }
                        Write-Host ("Windows PowerShell signature error: {0}" -f $shortError)
                        $report.Notes += ('Windows PowerShell CAT check error (exit ' + $ps5.ExitCode + ').')
                    }
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
                # Only report success when the actual integration report proves
                # signed driver staging, commit and unmodified original WIM.
                # Upload aggregate checks, never full source paths/manifests.
                $files = @(Get-ChildItem -LiteralPath $work -Filter 'oem-validation-report.json' -File -Recurse)
                if ($files.Count -ne 1) {
                    throw 'Expected precisely one signed-OEM integration report from the VM.'
                }
                $detail = Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
                $sourceMatch = ($detail.SourceSha256Before -match '^[0-9A-Fa-f]{64}$') -and ($detail.SourceSha256Before -eq $detail.SourceSha256After)
                if (-not $detail.StagedConfirmed -or
                    -not $detail.CommittedConfirmed -or
                    -not $sourceMatch -or
                    $detail.Result -notlike 'PASS*') {
                    throw 'The OEM report did not confirm staging, commit and unchanged source.'
                }
                $report.DriverCountBefore = [int]$detail.DriverCountBefore
                $report.DriverCountAfterStage = [int]$detail.DriverCountAfterStage
                $report.DriverCountAfterCommit = [int]$detail.DriverCountAfterCommit
                $report.StagedConfirmed = [bool]$detail.StagedConfirmed
                $report.CommittedConfirmed = [bool]$detail.CommittedConfirmed
                $report.OriginalWimSha256Unchanged = [bool]$sourceMatch
                $report.CopyOnlyImport = 'PASS'
                $report.Result = 'PASS_REAL_WINDOWS_SERVICING'
                Write-Host ("VM evidence: staged={0}, committed={1}, original WIM unchanged={2}" -f $report.StagedConfirmed, $report.CommittedConfirmed, $report.OriginalWimSha256Unchanged)
                Write-Host ("VM drivers: before={0}, staged={1}, committed={2}" -f $report.DriverCountBefore, $report.DriverCountAfterStage, $report.DriverCountAfterCommit)
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
