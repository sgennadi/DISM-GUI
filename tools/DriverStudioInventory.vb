Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

' Inventory bridge for a specific, already-mounted WIM image index.
' Uses structured Get-WindowsDriver output; never mounts or changes the image.
Public NotInheritable Class DriverStudioInventory
    Private Sub New()
    End Sub

    Public Shared Function ValidateTarget(wimFile As String, index As Integer,
                                          mountFolder As String) As String
        If String.IsNullOrWhiteSpace(wimFile) OrElse index < 1 Then
            Throw New InvalidOperationException("Select a WIM file and image index in Mount & Inspect.")
        End If
        If String.IsNullOrWhiteSpace(mountFolder) Then
            Throw New InvalidOperationException("Mount the selected index first. Driver Studio never mounts an image automatically.")
        End If
        Dim mountRoot = Path.GetFullPath(mountFolder).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        If Not Directory.Exists(Path.Combine(mountRoot, "Windows")) Then
            Throw New DirectoryNotFoundException(
                "The mounted Windows directory is not available: " & mountRoot)
        End If
        Dim report = Wim360Reader.Inspect(wimFile)
        Dim item = report.Images.FirstOrDefault(Function(x) x.Index = index)
        If item Is Nothing Then
            Throw New InvalidOperationException("Image index " & index.ToString() &
                                                " is not present in the selected WIM.")
        End If
        Dim mapped = If(item.MountFolder, "").TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        If Not item.MountStatus.StartsWith("Mounted", StringComparison.OrdinalIgnoreCase) OrElse
           mapped.Length = 0 OrElse
           Not String.Equals(Path.GetFullPath(mapped), mountRoot,
                             StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException(
                "Selected WIM index is not mounted at this folder. " &
                "No driver version comparison was performed. " &
                "Use Mount & Inspect to mount exactly this index, then retry.")
        End If
        Return mountRoot
    End Function

    Private Shared Function PropertyText(parent As JsonElement, name As String) As String
        Dim p As JsonElement
        If parent.ValueKind <> JsonValueKind.Object OrElse
           Not parent.TryGetProperty(name, p) Then Return ""
        Select Case p.ValueKind
            Case JsonValueKind.String : Return If(p.GetString(), "")
            Case JsonValueKind.Number : Return p.ToString()
            Case JsonValueKind.True : Return "True"
            Case JsonValueKind.False : Return "False"
            Case Else : Return ""
        End Select
    End Function

    Public Shared Function ParseInventoryJson(json As String) As List(Of DriverStudioEngine.DriverPackage)
        If String.IsNullOrWhiteSpace(json) OrElse json.Length > 8 * 1024 * 1024 Then
            Throw New InvalidDataException("Driver inventory is empty or exceeds 8 MB.")
        End If
        Dim results As New List(Of DriverStudioEngine.DriverPackage)()
        Using parsed = JsonDocument.Parse(json, New JsonDocumentOptions With {
            .MaxDepth = 12, .AllowTrailingCommas = False,
            .CommentHandling = JsonCommentHandling.Disallow
        })
            Dim items As New List(Of JsonElement)()
            If parsed.RootElement.ValueKind = JsonValueKind.Array Then
                items.AddRange(parsed.RootElement.EnumerateArray())
            ElseIf parsed.RootElement.ValueKind = JsonValueKind.Object Then
                items.Add(parsed.RootElement)
            Else
                Throw New InvalidDataException("Expected a JSON driver-inventory array.")
            End If
            If items.Count > 5000 Then
                Throw New InvalidDataException("Too many driver inventory entries.")
            End If
            For Each entry In items
                If entry.ValueKind <> JsonValueKind.Object Then
                    Throw New InvalidDataException("Invalid driver-inventory item.")
                End If
                Dim infName = PropertyText(entry, "InfName")
                If infName.Length > 0 Then infName = Path.GetFileName(infName)
                results.Add(New DriverStudioEngine.DriverPackage With {
                    .InfName = infName,
                    .PublishedName = PropertyText(entry, "PublishedName"),
                    .Provider = PropertyText(entry, "Provider"),
                    .ClassName = PropertyText(entry, "ClassName"),
                    .Version = PropertyText(entry, "Version"),
                    .DriverDate = PropertyText(entry, "Date"),
                    .IsInbox = String.Equals(PropertyText(entry, "Inbox"), "True",
                                             StringComparison.OrdinalIgnoreCase)
                })
            Next
        End Using
        Return results
    End Function

    Private Shared Function PowershellPath() As String
        ' Prefer installed PowerShell 7 when available: some Windows Server
        ' hosts have a broken/disabled Windows PowerShell 5.1 module loader.
        ' Windows PowerShell remains the no-extra-dependency fallback.
        Dim programFiles = Environment.GetEnvironmentVariable("ProgramW6432")
        If String.IsNullOrWhiteSpace(programFiles) Then
            programFiles = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles)
        End If
        Dim core = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe")
        If File.Exists(core) Then Return core

        Dim windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        Dim systemDirectory = If(Environment.Is64BitOperatingSystem AndAlso
                                 Not Environment.Is64BitProcess, "Sysnative", "System32")
        Dim legacy = Path.Combine(windowsDir, systemDirectory,
                                  "WindowsPowerShell", "v1.0", "powershell.exe")
        If Not File.Exists(legacy) Then
            Throw New FileNotFoundException(
                "PowerShell 7 or Windows PowerShell with DISM module required.", legacy)
        End If
        Return legacy
    End Function

    Private Shared Function MakePowershellScript(mountFolder As String) As String
        ' Double single quotes to ensure a path cannot inject PowerShell commands.
        Dim literalPath = "'" & mountFolder.Replace("'", "''") & "'"
        Return String.Join(vbLf, {
            "$ErrorActionPreference = 'Stop'",
            "$ProgressPreference = 'SilentlyContinue'",
            "Import-Module Dism -ErrorAction Stop",
            "$drivers = @(Get-WindowsDriver -Path " & literalPath &
               " -All -ErrorAction Stop | ForEach-Object {",
            "  [PSCustomObject]@{",
            "    InfName = [string]$_.OriginalFileName",
            "    PublishedName = [string]$_.Driver",
            "    Provider = [string]$_.ProviderName",
            "    ClassName = [string]$_.ClassName",
            "    Version = [string]$_.Version",
            "    Date = $(if ($_.Date -is [datetime]) { $_.Date.ToString('yyyy-MM-dd') } " &
               "else { [string]$_.Date })",
            "    Inbox = [bool]$_.Inbox",
            "  }",
            "})",
            "ConvertTo-Json -InputObject $drivers -Depth 3 -Compress"
        })
    End Function

    Public Shared Async Function ReadMountedAsync(wimFile As String,
             index As Integer, mountFolder As String,
             cancellationToken As CancellationToken) As Task(Of List(Of DriverStudioEngine.DriverPackage))
        ' First verify the image/index/mount triplet. A Windows directory alone
        ' is insufficient: it could belong to another image or mounted index.
        Dim root = Await Task.Run(Function() ValidateTarget(wimFile, index, mountFolder),
                                  cancellationToken)
        cancellationToken.ThrowIfCancellationRequested()

        Dim script = MakePowershellScript(root)
        Dim encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script))
        Dim psi As New ProcessStartInfo With {
            .FileName = PowershellPath(),
            .UseShellExecute = False,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .CreateNoWindow = True
        }
        For Each arg As String In {"-NoProfile", "-NonInteractive", "-EncodedCommand", encoded}
            psi.ArgumentList.Add(arg)
        Next
        Using process As New Process With {.StartInfo = psi}
            If Not process.Start() Then
                Throw New Win32Exception("Cannot launch Windows PowerShell for driver inventory.")
            End If
            Using timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                timeout.CancelAfter(TimeSpan.FromMinutes(3))
                Try
                    Dim outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token)
                    Dim errorTask = process.StandardError.ReadToEndAsync(timeout.Token)
                    Await process.WaitForExitAsync(timeout.Token)
                    Dim output = Await outputTask
                    Dim errorText = Await errorTask
                    If process.ExitCode <> 0 Then
                        Throw New InvalidOperationException(
                            "Get-WindowsDriver failed (exit " & process.ExitCode.ToString() &
                            "): " & errorText.Substring(0, Math.Min(1600, errorText.Length)))
                    End If
                    Return ParseInventoryJson(output)
                Catch ex As OperationCanceledException
                    If Not process.HasExited Then process.Kill(True)
                    If cancellationToken.IsCancellationRequested Then Throw
                    Throw New TimeoutException(
                        "Driver inventory timed out after 3 minutes. No WIM changes were made.")
                Finally
                    If Not process.HasExited Then process.Kill(True)
                End Try
            End Using
        End Using
    End Function
End Class
