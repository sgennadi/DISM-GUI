Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

' Read-only driver/package inventory of one existing mounted WIM index.
' Never mount, unmount, commit, discard, add or remove any image content.
Public NotInheritable Class Wim360Servicing
    Private Sub New()
    End Sub

    Public NotInheritable Class PackageEntry
        Public Property Name As String = ""
        Public Property State As String = ""
        Public Property ReleaseType As String = ""
    End Class

    Public NotInheritable Class ComponentEntry
        Public Property Kind As String = ""
        Public Property Name As String = ""
        Public Property State As String = ""
    End Class

    Public NotInheritable Class ComponentInventory
        Public Property Items As New List(Of ComponentEntry)()
        Public Property FeatureWarning As String = ""
        Public Property CapabilityWarning As String = ""
    End Class

    Public NotInheritable Class AuditResult
        Public Property ImageFile As String = ""
        Public Property Index As Integer
        Public Property MountFolder As String = ""
        Public Property ScannedUtc As DateTime
        Public Property DriverCount As Integer
        Public Property ThirdPartyDriverCount As Integer
        Public Property InboxDriverCount As Integer
        Public Property PackageCount As Integer
        Public Property InstalledPackageCount As Integer
        Public Property PendingPackageCount As Integer
        Public Property StagedPackageCount As Integer
        Public Property DriverClasses As String = ""
        Public Property DriverProviders As String = ""
        Public Property PendingPackages As String = ""
        Public Property Drivers As New List(Of DriverStudioEngine.DriverPackage)()
        Public Property Packages As New List(Of PackageEntry)()
        Public Property Components As New List(Of ComponentEntry)()
        Public Property FeatureCount As Integer
        Public Property EnabledFeatureCount As Integer
        Public Property CapabilityCount As Integer
        Public Property InstalledCapabilityCount As Integer
        Public Property FeatureWarning As String = ""
        Public Property CapabilityWarning As String = ""
        Public Property DriverWarning As String = ""
        Public Property PackageWarning As String = ""
        Public Property DriverDurationSeconds As Double
        Public Property PackageDurationSeconds As Double
        Public Property ComponentDurationSeconds As Double

        Public ReadOnly Property HasWarnings As Boolean
            Get
                Return DriverWarning.Length > 0 OrElse PackageWarning.Length > 0 OrElse
                       FeatureWarning.Length > 0 OrElse CapabilityWarning.Length > 0
            End Get
        End Property

        Public ReadOnly Property WarningSummary As String
            Get
                Dim issues As New List(Of String)()
                If DriverWarning.Length > 0 Then issues.Add("Drivers: " & DriverWarning)
                If PackageWarning.Length > 0 Then issues.Add("Packages: " & PackageWarning)
                If FeatureWarning.Length > 0 Then issues.Add("Features: " & FeatureWarning)
                If CapabilityWarning.Length > 0 Then issues.Add("Capabilities: " & CapabilityWarning)
                Return String.Join("  |  ", issues)
            End Get
        End Property
    End Class

    Private Shared Function StringField(parent As JsonElement, field As String) As String
        Dim value As JsonElement
        If parent.ValueKind <> JsonValueKind.Object OrElse
           Not parent.TryGetProperty(field, value) Then Return ""
        If value.ValueKind = JsonValueKind.String Then Return If(value.GetString(), "")
        Return ""
    End Function

    ' Standalone parser so CI can check DISM output without a mounted Windows image.
    Public Shared Function ParsePackagesJson(json As String) As List(Of PackageEntry)
        If String.IsNullOrWhiteSpace(json) OrElse json.Length > 16 * 1024 * 1024 Then
            Throw New InvalidDataException("Package inventory is empty or larger than 16 MB.")
        End If
        Dim entries As New List(Of JsonElement)()
        Dim result As New List(Of PackageEntry)()
        Using parsed = JsonDocument.Parse(json, New JsonDocumentOptions With {
            .MaxDepth = 12,
            .AllowTrailingCommas = False,
            .CommentHandling = JsonCommentHandling.Disallow
        })
            If parsed.RootElement.ValueKind = JsonValueKind.Array Then
                entries.AddRange(parsed.RootElement.EnumerateArray())
            ElseIf parsed.RootElement.ValueKind = JsonValueKind.Object Then
                entries.Add(parsed.RootElement)
            Else
                Throw New InvalidDataException("Expected package-inventory JSON array.")
            End If
            If entries.Count > 20000 Then
                Throw New InvalidDataException("Package inventory exceeds the 20000-entry limit.")
            End If
            For Each entry As JsonElement In entries
                If entry.ValueKind <> JsonValueKind.Object Then
                    Throw New InvalidDataException("Invalid Windows package inventory item.")
                End If
                Dim name = StringField(entry, "PackageName")
                If String.IsNullOrWhiteSpace(name) Then
                    Throw New InvalidDataException("Windows package is missing its package name.")
                End If
                result.Add(New PackageEntry With {
                    .Name = name,
                    .State = StringField(entry, "PackageState"),
                    .ReleaseType = StringField(entry, "ReleaseType")
                })
            Next
        End Using
        Return result
    End Function

    ' Optional component inputs retain compatibility with 4.3.3 call sites.
    Public Shared Function Summarize(wimFile As String, index As Integer,
        mountFolder As String, drivers As IEnumerable(Of DriverStudioEngine.DriverPackage),
        packages As IEnumerable(Of PackageEntry),
        Optional components As IEnumerable(Of ComponentEntry) = Nothing,
        Optional featureWarning As String = "",
        Optional capabilityWarning As String = "",
        Optional driverWarning As String = "",
        Optional packageWarning As String = "") As AuditResult
        If drivers Is Nothing Then Throw New ArgumentNullException(NameOf(drivers))
        If packages Is Nothing Then Throw New ArgumentNullException(NameOf(packages))
        Dim drv = drivers.ToList()
        Dim pkgs = packages.ToList()
        Dim comps = If(components, Enumerable.Empty(Of ComponentEntry)()).ToList()
        Dim classes = drv.Where(Function(d) Not String.IsNullOrWhiteSpace(d.ClassName)).
            GroupBy(Function(d) d.ClassName, StringComparer.OrdinalIgnoreCase).
            OrderByDescending(Function(g) g.Count()).
            ThenBy(Function(g) g.Key, StringComparer.OrdinalIgnoreCase).
            Take(5).Select(Function(g) g.Key & " (" & g.Count().ToString() & ")")
        Dim providers = drv.Where(Function(d) Not String.IsNullOrWhiteSpace(d.Provider)).
            GroupBy(Function(d) d.Provider, StringComparer.OrdinalIgnoreCase).
            OrderByDescending(Function(g) g.Count()).
            ThenBy(Function(g) g.Key, StringComparer.OrdinalIgnoreCase).
            Take(5).Select(Function(g) g.Key & " (" & g.Count().ToString() & ")")
        Dim pending = pkgs.Where(Function(p)
            Return p.State.IndexOf("Pending", StringComparison.OrdinalIgnoreCase) >= 0
        End Function).ToList()
        Return New AuditResult With {
            .ImageFile = wimFile,
            .Index = index,
            .MountFolder = mountFolder,
            .ScannedUtc = DateTime.UtcNow,
            .DriverCount = drv.Count,
            .ThirdPartyDriverCount = drv.Where(Function(d) Not d.IsInbox).Count(),
            .InboxDriverCount = drv.Where(Function(d) d.IsInbox).Count(),
            .PackageCount = pkgs.Count,
            .InstalledPackageCount = pkgs.Where(Function(p)
                Return String.Equals(p.State, "Installed", StringComparison.OrdinalIgnoreCase)
            End Function).Count(),
            .PendingPackageCount = pending.Count,
            .StagedPackageCount = pkgs.Where(Function(p)
                Return String.Equals(p.State, "Staged", StringComparison.OrdinalIgnoreCase)
            End Function).Count(),
            .DriverClasses = If(classes.Any(), String.Join(", ", classes), "Not listed"),
            .DriverProviders = If(providers.Any(), String.Join(", ", providers), "Not listed"),
            .PendingPackages = If(pending.Count = 0, "None",
                String.Join(", ", pending.Take(4).Select(Function(p) p.Name))),
            .Drivers = drv,
            .Packages = pkgs,
            .Components = comps,
            .FeatureCount = comps.Where(Function(c) c.Kind = "Features").Count(),
            .EnabledFeatureCount = comps.Where(Function(c)
                Return c.Kind = "Features" AndAlso
                    String.Equals(c.State, "Enabled", StringComparison.OrdinalIgnoreCase)
            End Function).Count(),
            .CapabilityCount = comps.Where(Function(c) c.Kind = "Capabilities").Count(),
            .InstalledCapabilityCount = comps.Where(Function(c)
                Return c.Kind = "Capabilities" AndAlso
                    String.Equals(c.State, "Installed", StringComparison.OrdinalIgnoreCase)
            End Function).Count(),
            .FeatureWarning = If(featureWarning, ""),
            .CapabilityWarning = If(capabilityWarning, ""),
            .DriverWarning = If(driverWarning, ""),
            .PackageWarning = If(packageWarning, "")
        }
    End Function

    Public Shared Function FormatAudit(value As AuditResult) As String
        If value Is Nothing Then Return "Mounted servicing inventory not scanned."
        Return String.Join(Environment.NewLine, {
            "MOUNTED SERVICING AUDIT (read-only) | Index " & value.Index.ToString(),
            "Checked (UTC): " & value.ScannedUtc.ToString("u"),
            If(value.DriverWarning.Length > 0,
               "Driver packages: unavailable (no reliable count)",
               "Driver packages: " & value.DriverCount.ToString() &
                " (" & value.ThirdPartyDriverCount.ToString() & " third-party, " &
                value.InboxDriverCount.ToString() & " inbox)"),
            "Common classes: " & value.DriverClasses,
            "Top providers: " & value.DriverProviders,
            If(value.PackageWarning.Length > 0,
               "Windows packages: unavailable (no reliable count)",
               "Windows packages: " & value.PackageCount.ToString() &
                " (" & value.InstalledPackageCount.ToString() & " installed, " &
                value.PendingPackageCount.ToString() & " pending, " &
                value.StagedPackageCount.ToString() & " staged)"),
            If(value.PackageWarning.Length > 0,
               "Pending packages: unavailable",
               "Pending packages (up to 4): " & value.PendingPackages),
            If(value.FeatureWarning.Length > 0,
               "Optional features: unavailable (no reliable count)",
               "Optional features: " & value.FeatureCount.ToString() &
                " (" & value.EnabledFeatureCount.ToString() & " enabled)"),
            If(value.CapabilityWarning.Length > 0,
               "Capabilities: unavailable (no reliable count)",
               "Capabilities: " & value.CapabilityCount.ToString() &
                " (" & value.InstalledCapabilityCount.ToString() & " installed)"),
            If(value.DriverWarning.Length > 0,
               "Driver inventory failed: " & value.DriverWarning, ""),
            If(value.PackageWarning.Length > 0,
               "Windows package inventory failed: " & value.PackageWarning, ""),
            If(value.FeatureWarning.Length > 0,
               "Optional features not available: " & value.FeatureWarning, ""),
            If(value.CapabilityWarning.Length > 0,
               "Capabilities not available: " & value.CapabilityWarning, ""),
            "Read timings: drivers " & value.DriverDurationSeconds.ToString("0.0") &
                "s; packages " & value.PackageDurationSeconds.ToString("0.0") &
                "s; components " & value.ComponentDurationSeconds.ToString("0.0") & "s",
            If(value.HasWarnings,
               "PARTIAL AUDIT: warnings above mean some counts are unavailable.",
               "AUDIT COMPLETE: all four inventory categories returned successfully.")
        })
    End Function

    ' Component results use a single JSON envelope; each read has its own
    ' warning so WinPE images lacking one API still retain available data.
    Public Shared Function ParseComponentsJson(json As String) As ComponentInventory
        If String.IsNullOrWhiteSpace(json) OrElse json.Length > 16 * 1024 * 1024 Then
            Throw New InvalidDataException("Windows component inventory is missing or larger than 16 MB.")
        End If
        Dim result As New ComponentInventory()
        Using parsed = JsonDocument.Parse(json, New JsonDocumentOptions With {
            .MaxDepth = 12,
            .AllowTrailingCommas = False,
            .CommentHandling = JsonCommentHandling.Disallow
        })
            Dim root = parsed.RootElement
            If root.ValueKind <> JsonValueKind.Object Then
                Throw New InvalidDataException("Expected Windows component inventory JSON object.")
            End If
            result.FeatureWarning = StringField(root, "FeatureWarning")
            result.CapabilityWarning = StringField(root, "CapabilityWarning")
            For Each kind As String In {"Features", "Capabilities"}
                Dim children As JsonElement
                If Not root.TryGetProperty(kind, children) OrElse
                   children.ValueKind <> JsonValueKind.Array Then
                    Throw New InvalidDataException("Missing Windows component array: " & kind)
                End If
                For Each entry As JsonElement In children.EnumerateArray()
                    If entry.ValueKind <> JsonValueKind.Object Then
                        Throw New InvalidDataException("Malformed Windows component item.")
                    End If
                    Dim name = StringField(entry, "Name")
                    If String.IsNullOrWhiteSpace(name) Then
                        Throw New InvalidDataException("Windows component has no name.")
                    End If
                    result.Items.Add(New ComponentEntry With {
                        .Kind = kind,
                        .Name = name,
                        .State = StringField(entry, "State")
                    })
                    If result.Items.Count > 20000 Then
                        Throw New InvalidDataException("Windows component inventory exceeds 20000 entries.")
                    End If
                Next
            Next
        End Using
        Return result
    End Function

    Private Shared Async Function ReadComponentsAsync(mountFolder As String,
        cancellation As CancellationToken) As Task(Of ComponentInventory)
        Dim safeLiteral = "'" & mountFolder.Replace("'", "''") & "'"
        Dim script = String.Join(vbLf, {
            "$ErrorActionPreference = 'Stop'",
            "$ProgressPreference = 'SilentlyContinue'",
            "Import-Module Dism -ErrorAction Stop",
            "$features = @()",
            "$capabilities = @()",
            "$fWarning = ''",
            "$cWarning = ''",
            "try {",
            "  $features = @(Get-WindowsOptionalFeature -Path " & safeLiteral &
               " -ErrorAction Stop | ForEach-Object {",
            "    [PSCustomObject]@{ Name = [string]$_.FeatureName; State = [string]$_.State }",
            "  })",
            "} catch { $fWarning = [string]$_.Exception.Message }",
            "try {",
            "  $capabilities = @(Get-WindowsCapability -Path " & safeLiteral &
               " -ErrorAction Stop | ForEach-Object {",
            "    [PSCustomObject]@{ Name = [string]$_.Name; State = [string]$_.State }",
            "  })",
            "} catch { $cWarning = [string]$_.Exception.Message }",
            "$result = [PSCustomObject]@{",
            "  Features = @($features)",
            "  Capabilities = @($capabilities)",
            "  FeatureWarning = $fWarning",
            "  CapabilityWarning = $cWarning",
            "}",
            "ConvertTo-Json -InputObject $result -Depth 4 -Compress"
        })
        Dim startInfo As New ProcessStartInfo With {
            .FileName = PowerShellExe(), .UseShellExecute = False,
            .CreateNoWindow = True, .RedirectStandardOutput = True,
            .RedirectStandardError = True
        }
        For Each arg As String In {"-NoProfile", "-NonInteractive",
                                   "-EncodedCommand",
                                   Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}
            startInfo.ArgumentList.Add(arg)
        Next
        Using command As New Process With {.StartInfo = startInfo}
            If Not command.Start() Then
                Throw New Win32Exception("Unable to run mounted Windows component inventory.")
            End If
            Using budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation)
                budget.CancelAfter(TimeSpan.FromMinutes(5))
                Try
                    Dim stdoutTask = command.StandardOutput.ReadToEndAsync(budget.Token)
                    Dim stderrTask = command.StandardError.ReadToEndAsync(budget.Token)
                    Await command.WaitForExitAsync(budget.Token)
                    Dim stdout = Await stdoutTask
                    Dim stderr = Await stderrTask
                    If command.ExitCode <> 0 Then
                        Throw New InvalidOperationException("Get-WindowsOptionalFeature / " &
                            "Get-WindowsCapability failed (exit " & command.ExitCode.ToString() &
                            "): " & stderr.Substring(0, Math.Min(stderr.Length, 1600)))
                    End If
                    Return ParseComponentsJson(stdout)
                Catch ex As OperationCanceledException
                    If cancellation.IsCancellationRequested Then Throw
                    Throw New TimeoutException("Component inventory timed out after 5 minutes. No image changes.")
                Finally
                    If Not command.HasExited Then
                        Try
                            command.Kill(True)
                        Catch
                        End Try
                    End If
                End Try
            End Using
        End Using
    End Function

    Private Shared Function PowerShellExe() As String
        Dim windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        Dim systemFolder = If(Environment.Is64BitOperatingSystem AndAlso
                              Not Environment.Is64BitProcess, "Sysnative", "System32")
        Dim filename = Path.Combine(windowsDir, systemFolder,
                                   "WindowsPowerShell", "v1.0", "powershell.exe")
        If Not File.Exists(filename) Then
            Throw New FileNotFoundException("Windows PowerShell with DISM is required.", filename)
        End If
        Return filename
    End Function

    Private Shared Async Function ReadPackagesAsync(mountFolder As String,
        cancellation As CancellationToken) As Task(Of List(Of PackageEntry))
        Dim safeLiteral = "'" & mountFolder.Replace("'", "''") & "'"
        Dim script = String.Join(vbLf, {
            "$ErrorActionPreference = 'Stop'",
            "$ProgressPreference = 'SilentlyContinue'",
            "Import-Module Dism -ErrorAction Stop",
            "$items = @(Get-WindowsPackage -Path " & safeLiteral &
                " -ErrorAction Stop | ForEach-Object {",
            "  [PSCustomObject]@{",
            "    PackageName = [string]$_.PackageName",
            "    PackageState = [string]$_.PackageState",
            "    ReleaseType = [string]$_.ReleaseType",
            "  }",
            "})",
            "ConvertTo-Json -InputObject $items -Depth 3 -Compress"
        })
        Dim startInfo As New ProcessStartInfo With {
            .FileName = PowerShellExe(), .UseShellExecute = False,
            .CreateNoWindow = True, .RedirectStandardOutput = True,
            .RedirectStandardError = True
        }
        For Each arg As String In {"-NoProfile", "-NonInteractive",
                                   "-EncodedCommand",
                                   Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}
            startInfo.ArgumentList.Add(arg)
        Next
        Using command As New Process With {.StartInfo = startInfo}
            If Not command.Start() Then
                Throw New Win32Exception("Unable to run Windows package inventory.")
            End If
            Using budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation)
                budget.CancelAfter(TimeSpan.FromMinutes(3))
                Try
                    Dim stdoutTask = command.StandardOutput.ReadToEndAsync(budget.Token)
                    Dim stderrTask = command.StandardError.ReadToEndAsync(budget.Token)
                    Await command.WaitForExitAsync(budget.Token)
                    Dim stdout = Await stdoutTask
                    Dim stderr = Await stderrTask
                    If command.ExitCode <> 0 Then
                        Throw New InvalidOperationException("Get-WindowsPackage failed (exit " &
                            command.ExitCode.ToString() & "): " &
                            stderr.Substring(0, Math.Min(stderr.Length, 1600)))
                    End If
                    Return ParsePackagesJson(stdout)
                Catch ex As OperationCanceledException
                    If cancellation.IsCancellationRequested Then Throw
                    Throw New TimeoutException(
                        "Package inventory timed out after 3 minutes; the image was not modified.")
                Finally
                    If Not command.HasExited Then
                        Try
                            command.Kill(True)
                        Catch
                        End Try
                    End If
                End Try
            End Using
        End Using
    End Function

    Private Shared Function Diagnostic(ex As Exception) As String
        ' Failures are shown to the operator and exported with the snapshot.
        Dim description = ex.GetType().Name & ": " & ex.Message
        Return ErrorDiagnostics.Redact(description.Substring(0,
            Math.Min(description.Length, 480)))
    End Function

    Public Shared Async Function InspectMountedAsync(wimFile As String,
        index As Integer, mountFolder As String,
        cancellation As CancellationToken,
        Optional progress As IProgress(Of String) = Nothing) As Task(Of AuditResult)
        ' Mount verification failures remain fatal: never claim that an
        ' arbitrary Windows folder is the selected mounted image index.
        If progress IsNot Nothing Then progress.Report("WIM 360: validating source image / index / mount...")
        Dim root = Await Task.Run(
            Function() DriverStudioInventory.ValidateTarget(wimFile, index, mountFolder),
            cancellation)
        cancellation.ThrowIfCancellationRequested()

        Dim drivers As New List(Of DriverStudioEngine.DriverPackage)()
        Dim packages As New List(Of PackageEntry)()
        Dim components As New ComponentInventory()
        Dim driverWarning As String = ""
        Dim packageWarning As String = ""
        Dim featureWarning As String = ""
        Dim capabilityWarning As String = ""
        Dim timer As Stopwatch = Stopwatch.StartNew()

        If progress IsNot Nothing Then progress.Report("WIM 360 [1/3]: reading Windows driver packages...")
        Try
            drivers = Await DriverStudioInventory.ReadMountedAsync(wimFile, index, root, cancellation)
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            driverWarning = Diagnostic(ex)
        End Try
        cancellation.ThrowIfCancellationRequested()
        Dim driverSeconds = timer.Elapsed.TotalSeconds
        timer.Restart()

        If progress IsNot Nothing Then progress.Report("WIM 360 [2/3]: reading Windows package states...")
        Try
            packages = Await ReadPackagesAsync(root, cancellation)
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            packageWarning = Diagnostic(ex)
        End Try
        cancellation.ThrowIfCancellationRequested()
        Dim packageSeconds = timer.Elapsed.TotalSeconds
        timer.Restart()

        If progress IsNot Nothing Then progress.Report("WIM 360 [3/3]: reading optional features and capabilities...")
        Try
            components = Await ReadComponentsAsync(root, cancellation)
            featureWarning = ErrorDiagnostics.Redact(components.FeatureWarning)
            capabilityWarning = ErrorDiagnostics.Redact(components.CapabilityWarning)
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            featureWarning = Diagnostic(ex)
            capabilityWarning = featureWarning
        End Try
        cancellation.ThrowIfCancellationRequested()
        Dim componentSeconds = timer.Elapsed.TotalSeconds

        If progress IsNot Nothing Then progress.Report("WIM 360: revalidating mounted index (read-only)...")
        ' Abort the whole scan if the WIM/index/mount was externally replaced.
        Await Task.Run(Function()
            Return DriverStudioInventory.ValidateTarget(wimFile, index, root)
        End Function, cancellation)
        cancellation.ThrowIfCancellationRequested()

        Dim audit = Summarize(wimFile, index, root, drivers, packages,
                              components.Items, featureWarning, capabilityWarning,
                              driverWarning, packageWarning)
        audit.DriverDurationSeconds = driverSeconds
        audit.PackageDurationSeconds = packageSeconds
        audit.ComponentDurationSeconds = componentSeconds
        Return audit
    End Function
End Class
