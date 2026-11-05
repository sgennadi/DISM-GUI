Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

' UniversalDiagnostics 0.1.0-rc.1 "capture-json" adapter.
' Local queue only: NEVER submits to GitHub or reads/discloses raw DISM logs.
' The shared collector is independently installed for all applications.
Public NotInheritable Class UniversalDiagnosticsBridge
    Private Sub New()
    End Sub

    Public Const Repository As String = "sgennadi/DISM-GUI"
    Public Const CollectorFileName As String = "UniversalDiagnostics.exe"

    Public NotInheritable Class CaptureResult
        Public Property Success As Boolean
        Public Property ReportId As String = ""
        Public Property Message As String = ""
    End Class

    Public Shared Function CollectorPath() As String
        Dim locations As New List(Of String)()
        ' Optional administrator-specified path; never search PATH or download
        ' and execute remote code without a user action.
        Dim custom = Environment.GetEnvironmentVariable("UNIVERSAL_DIAGNOSTICS_EXE")
        If Not String.IsNullOrWhiteSpace(custom) AndAlso
           Path.IsPathFullyQualified(custom) Then
            locations.Add(custom)
        End If
        Dim localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData)
        If Not String.IsNullOrWhiteSpace(localAppData) Then
            locations.Add(Path.Combine(localAppData, "UniversalDiagnostics",
                                       CollectorFileName))
        End If
        locations.Add(Path.Combine(AppContext.BaseDirectory, CollectorFileName))
        For Each candidate In locations
            Try
                Dim fullPath = Path.GetFullPath(candidate)
                If File.Exists(fullPath) AndAlso
                   String.Equals(Path.GetExtension(fullPath), ".exe",
                                 StringComparison.OrdinalIgnoreCase) Then
                    Return fullPath
                End If
            Catch ex As ArgumentException
            Catch ex As NotSupportedException
            Catch ex As PathTooLongException
            End Try
        Next
        Return ""
    End Function

    Private Shared Function SafeField(value As String, maximum As Integer) As String
        Dim text = ErrorDiagnostics.Redact(If(value, "")).
            Replace(vbCr, " ").Replace(vbLf, " ").Replace(vbTab, " ").Trim()
        If text.Length > maximum Then text = text.Substring(0, maximum)
        Return text
    End Function

    Public Shared Function BuildCapturePayload(component As String,
                 appVersion As String, title As String,
                 description As String) As String
        Dim area = SafeField(component, 90)
        If area.Length = 0 Then area = "DISM GUI"
        Dim heading = SafeField(title, 180)
        If heading.Length = 0 Then heading = "DISM GUI error report"
        Dim details = SafeField(description, 240)
        Dim summary = heading & If(details.Length > 0, " - " & details, "")
        Dim values As New Dictionary(Of String, Object) From {
            {"project", Repository},
            {"component", area},
            {"version", SafeField(appVersion, 70)},
            {"error_type", "USER_REPORT"},
            {"code", "MANUAL"},
            {"summary", SafeField(summary, 450)},
            {"level", "error"}
        }
        ' The contract deliberately excludes excerpt, local paths, raw logs,
        ' usernames, environment variables, machine identifiers and tokens.
        Return JsonSerializer.Serialize(values)
    End Function

    Public Shared Async Function CaptureAsync(payload As String,
                 Optional cancellationToken As CancellationToken = Nothing
                 ) As Task(Of CaptureResult)
        Dim collector = CollectorPath()
        If collector.Length = 0 Then
            Return New CaptureResult With {
                .Message = "Shared collector not installed. Install " &
                           "UniversalDiagnostics.exe from the universal module."
            }
        End If
        If String.IsNullOrWhiteSpace(payload) Then
            Return New CaptureResult With {.Message = "Nothing to save."}
        End If
        Dim info As New ProcessStartInfo(collector) With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardInput = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .StandardInputEncoding = New UTF8Encoding(False),
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }
        info.ArgumentList.Add("capture-json")
        ' A capture-only child does not need or inherit GitHub credentials.
        info.Environment.Remove("GH_TOKEN")
        info.Environment.Remove("GITHUB_TOKEN")
        Using process As New Process With {.StartInfo = info}
            Try
                If Not process.Start() Then
                    Return New CaptureResult With {
                        .Message = "Collector could not start."
                    }
                End If
                Dim outputTask = process.StandardOutput.ReadToEndAsync()
                Dim errorTask = process.StandardError.ReadToEndAsync()
                Await process.StandardInput.WriteAsync(payload).ConfigureAwait(False)
                process.StandardInput.Close()
                Using deadline = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken)
                    deadline.CancelAfter(TimeSpan.FromSeconds(25))
                    Await process.WaitForExitAsync(deadline.Token).ConfigureAwait(False)
                End Using
                Dim output = Await outputTask.ConfigureAwait(False)
                ' Drain stderr without displaying raw process diagnostic data.
                Await errorTask.ConfigureAwait(False)
                If process.ExitCode <> 0 Then
                    Return New CaptureResult With {
                        .Message = "Local collector exited with code " &
                                   process.ExitCode.ToString() & ". No upload was attempted."
                    }
                End If
                Using response = JsonDocument.Parse(output)
                    Dim id As JsonElement
                    Dim reportId As Guid
                    If Not response.RootElement.TryGetProperty("id", id) OrElse
                       id.ValueKind <> JsonValueKind.String OrElse
                       Not Guid.TryParse(id.GetString(), reportId) Then
                        Return New CaptureResult With {
                            .Message = "Collector response is missing a valid report ID."
                        }
                    End If
                    Return New CaptureResult With {
                        .Success = True,
                        .ReportId = reportId.ToString("D"),
                        .Message = "Saved to shared local queue. Nothing was sent to GitHub."
                    }
                End Using
            Catch ex As OperationCanceledException
                Try
                    If Not process.HasExited Then process.Kill(True)
                Catch
                End Try
                Return New CaptureResult With {
                    .Message = "Collector timed out or was cancelled. Verify the local queue."
                }
            Catch ex As Exception When TypeOf ex Is IOException OrElse
                                       TypeOf ex Is Win32Exception OrElse
                                       TypeOf ex Is InvalidOperationException OrElse
                                       TypeOf ex Is UnauthorizedAccessException OrElse
                                       TypeOf ex Is JsonException
                Try
                    If Not process.HasExited Then process.Kill(True)
                Catch
                End Try
                Return New CaptureResult With {
                    .Message = "Local collector failed (" & ex.GetType().Name &
                               "). No GitHub upload was requested."
                }
            End Try
        End Using
    End Function
End Class
