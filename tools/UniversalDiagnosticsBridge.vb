Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

' App-specific adapter to the embedded, shared UniversalDiagnostics v1 source.
' Runs inside DISM GUI: no helper EXE, shell, subprocess or network required.
Public NotInheritable Class UniversalDiagnosticsBridge
    Private Sub New()
    End Sub

    Public Const Repository As String = "sgennadi/DISM-GUI"

    Public NotInheritable Class CaptureResult
        Public Property Success As Boolean
        Public Property ReportId As String = ""
        Public Property Message As String = ""
    End Class

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
        ' No raw DISM log, excerpt, machine identity, password or token.
        Return JsonSerializer.Serialize(values)
    End Function

    Public Shared Async Function CaptureAsync(payload As String,
                     Optional cancellationToken As CancellationToken = Nothing
                     ) As Task(Of CaptureResult)
        Try
            Dim captured = Await Task.Run(
                Function() UniversalDiagnosticsEmbedded.Capture(payload, cancellationToken),
                cancellationToken)
            Return New CaptureResult With {
                .Success = True,
                .ReportId = captured.Id,
                .Message = "Saved to the embedded local diagnostics queue. " &
                           "Nothing was sent to GitHub."
            }
        Catch ex As OperationCanceledException
            Return New CaptureResult With {
                .Message = "Local diagnostic capture was cancelled."
            }
        Catch ex As Exception When TypeOf ex Is IOException OrElse
                                   TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is InvalidDataException OrElse
                                   TypeOf ex Is JsonException OrElse
                                   TypeOf ex Is ArgumentException
            Return New CaptureResult With {
                .Message = "Cannot save a local diagnostic (" &
                           ex.GetType().Name & "). No GitHub upload was attempted."
            }
        End Try
    End Function
End Class
