Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Security.Principal
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

' Read-only integration checks. No mount/apply/commit/discard, no image writes,
' no network requests and no automatic telemetry.
Public NotInheritable Class IntegratedTestChecks
    Private Sub New()
    End Sub

    Public NotInheritable Class Result
        Public Property Status As String = "INFO"
        Public Property Name As String = ""
        Public Property Details As String = ""
        Public Overrides Function ToString() As String
            Return "[" & Status & "] " & Name & ": " & Details
        End Function
    End Class

    Private Shared Function Item(status As String, name As String, details As String) As Result
        Return New Result With {.Status = status, .Name = name, .Details = details}
    End Function

    Private Shared Function DismExecutable() As String
        Dim system = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        Dim folder = If(Environment.Is64BitOperatingSystem AndAlso
                         Not Environment.Is64BitProcess, "Sysnative", "System32")
        Return IO.Path.Combine(system, folder, "dism.exe")
    End Function

    Private Shared Async Function TestDismAsync(token As CancellationToken) As Task(Of Result)
        Dim executable = DismExecutable()
        If Not File.Exists(executable) Then
            Return Item("FAIL", "DISM binary", "Windows DISM executable is missing.")
        End If
        Dim startInfo As New ProcessStartInfo With {
            .FileName = executable,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True
        }
        For Each arg As String In {"/English", "/Get-MountedImageInfo"}
            startInfo.ArgumentList.Add(arg)
        Next
        Using process As New Process With {.StartInfo = startInfo}
            If Not process.Start() Then
                Return Item("FAIL", "DISM mounted image query", "Could not start DISM.")
            End If
            Using budget = CancellationTokenSource.CreateLinkedTokenSource(token)
                budget.CancelAfter(TimeSpan.FromSeconds(60))
                Try
                    Dim stdout = process.StandardOutput.ReadToEndAsync(budget.Token)
                    Dim stderr = process.StandardError.ReadToEndAsync(budget.Token)
                    Await process.WaitForExitAsync(budget.Token)
                    Dim output = Await stdout
                    Dim err = Await stderr
                    If process.ExitCode <> 0 Then
                        Return Item("WARN", "DISM mounted image query",
                            "Exit " & process.ExitCode.ToString() & ". " &
                            ErrorDiagnostics.Redact(If(err.Length > 0, err, output)).
                                Substring(0, Math.Min(200,
                                    ErrorDiagnostics.Redact(If(err.Length > 0, err, output)).Length)))
                    End If
                    Return Item("PASS", "DISM mounted image query",
                        "DISM is available; /Get-MountedImageInfo completed successfully (read-only).")
                Catch ex As OperationCanceledException
                    If token.IsCancellationRequested Then Throw
                    Return Item("WARN", "DISM mounted image query",
                                "Read-only DISM command timed out after 60 seconds.")
                Catch ex As Exception
                    Return Item("WARN", "DISM mounted image query",
                                ErrorDiagnostics.Redact(ex.GetType().Name & ": " & ex.Message))
                Finally
                    If Not process.HasExited Then
                        Try
                            process.Kill(True)
                        Catch
                        End Try
                    End If
                End Try
            End Using
        End Using
    End Function

    Public Shared Function InspectDisplay(dpi As Integer, clientWidth As Integer,
                                          clientHeight As Integer) As Result
        Dim percent = dpi * 100.0 / 96.0
        If clientWidth < 800 OrElse clientHeight < 530 Then
            Return Item("WARN", "DPI and layout",
                percent.ToString("0") & "% scale, window " &
                clientWidth.ToString() & "x" & clientHeight.ToString() &
                "; window below recommended test size.")
        End If
        Return Item("PASS", "DPI and layout",
            percent.ToString("0") & "% (" & dpi.ToString() &
            " DPI), window " & clientWidth.ToString() & "x" &
            clientHeight.ToString() & ". Check for clipped controls visually.")
    End Function

    Public Shared Function FormatReport(results As IEnumerable(Of Result)) As String
        If results Is Nothing Then Throw New ArgumentNullException(NameOf(results))
        Dim items = results.ToList()
        Dim text As New StringBuilder()
        text.AppendLine("DISM GUI " &
                        GetType(IntegratedTestChecks).Assembly.GetName().Version.ToString(3) &
                        " | Integrated Read-only Test Center")
        text.AppendLine("Timestamp (UTC): " & DateTime.UtcNow.ToString("u"))
        text.AppendLine("No mounted images, drivers, packages or files were modified by these checks.")
        text.AppendLine("")
        For Each statusValue As String In {"PASS", "WARN", "FAIL", "SKIP"}
            text.AppendLine(statusValue & ": " &
                items.Where(Function(x) x.Status = statusValue).Count().ToString())
        Next
        text.AppendLine("")
        For Each entry As Result In items
            text.AppendLine(entry.ToString())
        Next
        text.AppendLine("")
        text.AppendLine("Note: SWM headers/part identity and WIM metadata checks do not prove all file data is intact.")
        Return ErrorDiagnostics.Redact(text.ToString())
    End Function

    Private Shared Sub Publish(results As List(Of Result), observer As IProgress(Of Result),
                               item As Result)
        results.Add(item)
        If observer IsNot Nothing Then observer.Report(item)
    End Sub

    Public Shared Async Function RunAllAsync(imagePath As String,
        includeMountedAudit As Boolean, dpi As Integer, width As Integer, height As Integer,
        observer As IProgress(Of Result), token As CancellationToken) As Task(Of List(Of Result))
        Dim entries As New List(Of Result)()
        token.ThrowIfCancellationRequested()
        Publish(entries, observer, Item("PASS", "Environment",
            "Windows " & Environment.OSVersion.Version.ToString() &
            ", .NET " & Environment.Version.ToString() &
            ", " & Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()))
        Dim elevated As Boolean = False
        Try
            Dim identity = WindowsIdentity.GetCurrent()
            elevated = New WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
        Catch
        End Try
        Publish(entries, observer, Item(If(elevated, "PASS", "WARN"),
            "Administrator rights",
            If(elevated, "Elevated; offline WIM servicing is available.",
               "Not elevated; DISM and mounted-image scans may have restricted access.")))
        Publish(entries, observer, InspectDisplay(dpi, width, height))
        Dim dismResult = Await TestDismAsync(token)
        Publish(entries, observer, dismResult)

        token.ThrowIfCancellationRequested()
        If String.IsNullOrWhiteSpace(imagePath) Then
            Publish(entries, observer, Item("SKIP", "WIM/ESD/SWM metadata",
                "No image selected. Choose an image for the full read-only test."))
            Publish(entries, observer, Item("SKIP", "Mounted Windows servicing",
                "No image selected. This optional test is never run automatically."))
            Return entries
        End If

        Dim inspected As Wim360Reader.WimReport = Nothing
        Try
            inspected = Await Task.Run(Function() Wim360Reader.Inspect(imagePath), token)
            token.ThrowIfCancellationRequested()
            Publish(entries, observer, Item("PASS", "WIM/ESD/SWM metadata",
                inspected.ImageCount.ToString() & " indexes, " &
                Wim360Reader.HumanSize(inspected.FileSizeBytes) &
                If(inspected.IsSplitSet, ", split set: " &
                   inspected.SplitSet.PartCount.ToString() & " matching parts.", ".")))
            If inspected.IsSplitSet Then
                Publish(entries, observer, Item("PASS", "SWM part manifest",
                    "All parts present and header IDs match. The set is read-only."))
            Else
                Publish(entries, observer, Item("SKIP", "SWM part manifest",
                    "This is a standard WIM/ESD file."))
            End If
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            ErrorDiagnostics.RememberFailure("Integrated Test Center image inventory", ex)
            Publish(entries, observer, Item("FAIL", "WIM/ESD/SWM metadata",
                ErrorDiagnostics.Redact(ex.GetType().Name & ": " & ex.Message)))
        End Try

        token.ThrowIfCancellationRequested()
        If Not includeMountedAudit Then
            Publish(entries, observer, Item("SKIP", "Mounted Windows servicing",
                "Optional audit is off. Enable it to scan an already-mounted Windows index."))
        ElseIf inspected Is Nothing OrElse inspected.IsSplitSet Then
            Publish(entries, observer, Item("SKIP", "Mounted Windows servicing",
                "Split/unavailable image cannot be mounted by Test Center."))
        Else
            Dim mounted = inspected.Images.FirstOrDefault(Function(x)
                Return x.MountStatus.StartsWith("Mounted", StringComparison.OrdinalIgnoreCase) AndAlso
                       Not String.IsNullOrWhiteSpace(x.MountFolder)
            End Function)
            If mounted Is Nothing Then
                Publish(entries, observer, Item("SKIP", "Mounted Windows servicing",
                    "No already-mounted image index was found. Test Center never mounts images."))
            Else
                Try
                    Dim audit = Await Wim360Servicing.InspectMountedAsync(
                        inspected.ImageFile, mounted.Index, mounted.MountFolder, token)
                    Publish(entries, observer, Item(If(audit.HasWarnings, "WARN", "PASS"),
                        "Mounted Windows servicing",
                        "Index " & mounted.Index.ToString() & ": " &
                        audit.DriverCount.ToString() & " drivers, " &
                        audit.PackageCount.ToString() & " packages, " &
                        audit.Components.Count.ToString() & " components. " &
                        If(audit.HasWarnings, audit.WarningSummary, "Complete read-only audit.")))
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As Exception
                    ErrorDiagnostics.RememberFailure("Integrated Test Center mounted audit", ex)
                    Publish(entries, observer, Item("FAIL", "Mounted Windows servicing",
                        ErrorDiagnostics.Redact(ex.GetType().Name & ": " & ex.Message)))
                End Try
            End If
        End If
        Return entries
    End Function
End Class
