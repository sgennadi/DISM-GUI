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

    Public Shared Function Summarize(wimFile As String, index As Integer,
        mountFolder As String, drivers As IEnumerable(Of DriverStudioEngine.DriverPackage),
        packages As IEnumerable(Of PackageEntry)) As AuditResult
        If drivers Is Nothing Then Throw New ArgumentNullException(NameOf(drivers))
        If packages Is Nothing Then Throw New ArgumentNullException(NameOf(packages))
        Dim drv = drivers.ToList()
        Dim pkgs = packages.ToList()
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
            .ThirdPartyDriverCount = drv.Count(Function(d) Not d.IsInbox),
            .InboxDriverCount = drv.Count(Function(d) d.IsInbox),
            .PackageCount = pkgs.Count,
            .InstalledPackageCount = pkgs.Count(Function(p)
                Return String.Equals(p.State, "Installed", StringComparison.OrdinalIgnoreCase)
            End Function),
            .PendingPackageCount = pending.Count,
            .StagedPackageCount = pkgs.Count(Function(p)
                Return String.Equals(p.State, "Staged", StringComparison.OrdinalIgnoreCase)
            End Function),
            .DriverClasses = If(classes.Any(), String.Join(", ", classes), "Not listed"),
            .DriverProviders = If(providers.Any(), String.Join(", ", providers), "Not listed"),
            .PendingPackages = If(pending.Count = 0, "None",
                String.Join(", ", pending.Take(4).Select(Function(p) p.Name)))
        }
    End Function

    Public Shared Function FormatAudit(value As AuditResult) As String
        If value Is Nothing Then Return "Mounted servicing inventory not scanned."
        Return String.Join(Environment.NewLine, {
            "MOUNTED SERVICING AUDIT (read-only) | Index " & value.Index.ToString(),
            "Checked (UTC): " & value.ScannedUtc.ToString("u"),
            "Driver packages: " & value.DriverCount.ToString() &
                " (" & value.ThirdPartyDriverCount.ToString() & " third-party, " &
                value.InboxDriverCount.ToString() & " inbox)",
            "Common classes: " & value.DriverClasses,
            "Top providers: " & value.DriverProviders,
            "Windows packages: " & value.PackageCount.ToString() &
                " (" & value.InstalledPackageCount.ToString() & " installed, " &
                value.PendingPackageCount.ToString() & " pending, " &
                value.StagedPackageCount.ToString() & " staged)",
            "Pending packages (up to 4): " & value.PendingPackages
        })
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

    Public Shared Async Function InspectMountedAsync(wimFile As String,
        index As Integer, mountFolder As String,
        cancellation As CancellationToken) As Task(Of AuditResult)
        ' Reject an unrelated mount before or after inventory collection.
        Dim root = Await Task.Run(
            Function() DriverStudioInventory.ValidateTarget(wimFile, index, mountFolder),
            cancellation)
        cancellation.ThrowIfCancellationRequested()
        Dim drivers = Await DriverStudioInventory.ReadMountedAsync(wimFile, index, root, cancellation)
        cancellation.ThrowIfCancellationRequested()
        Dim packages = Await ReadPackagesAsync(root, cancellation)
        cancellation.ThrowIfCancellationRequested()
        Await Task.Run(Function()
            Return DriverStudioInventory.ValidateTarget(wimFile, index, root)
        End Function, cancellation)
        Return Summarize(wimFile, index, root, drivers, packages)
    End Function
End Class
