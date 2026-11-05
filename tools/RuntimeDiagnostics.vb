Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Local, redacted runtime diagnostics for real installed copies of DISM GUI.
' Never uploads telemetry, reads private DISM logs, or embeds GitHub credentials.
' User must explicitly review and submit reports via Support/GitHub.
Public NotInheritable Class RuntimeDiagnostics
    Private Sub New()
    End Sub

    Private Const MaximumLogBytes As Long = 256L * 1024L
    Private Const MaximumDescription As Integer = 700
    Private Shared ReadOnly Sync As New Object()
    Private Shared initialized As Integer
    Private Shared showingPrompt As Integer

    Public Shared ReadOnly Property Folder As String
        Get
            ' Only the test executable can opt into a disposable directory.
            If Environment.GetEnvironmentVariable("DISMGUI_TEST_DIAGNOSTICS") = "1" Then
                Dim testFolder = Environment.GetEnvironmentVariable("DISMGUI_TEST_LOG_DIRECTORY")
                If Not String.IsNullOrWhiteSpace(testFolder) Then
                    Return Path.GetFullPath(testFolder)
                End If
            End If
            Return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DISMGUI", "Diagnostics")
        End Get
    End Property

    Private Shared ReadOnly Property CurrentLog As String
        Get
            Return Path.Combine(Folder, "events.log")
        End Get
    End Property

    Private Shared ReadOnly Property OlderLog As String
        Get
            Return Path.Combine(Folder, "events.previous.log")
        End Get
    End Property

    Private Shared Function OneLine(value As String, maxChars As Integer) As String
        Dim safe = ErrorDiagnostics.Redact(If(value, "")).
            Replace(vbCr, " ").Replace(vbLf, " ").Replace(vbTab, " ").Trim()
        If safe.Length > maxChars Then safe = safe.Substring(0, maxChars)
        Return safe
    End Function

    Private Shared Function Fingerprint(area As String, errorType As String,
                                         code As Integer) As String
        ' No machine names or error messages are hashed. Deduplication is
        ' deliberately limited to a coarse category, type and error code.
        Dim generic = area & "|" & errorType & "|" &
            code.ToString(Globalization.CultureInfo.InvariantCulture)
        Dim hash = SHA256.HashData(Encoding.UTF8.GetBytes(generic))
        Return Convert.ToHexString(hash).Substring(0, 12)
    End Function

    Private Shared Sub WriteEntry(area As String, kind As String,
                                  detail As String, code As Integer)
        Try
            Dim safeArea = OneLine(area, 70)
            Dim safeKind = OneLine(kind, 70)
            Dim safeDetail = OneLine(detail, MaximumDescription)
            Dim version = GetType(RuntimeDiagnostics).Assembly.GetName().
                Version.ToString(3)
            Dim line = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ",
                Globalization.CultureInfo.InvariantCulture) & " | v" & version &
                " | " & safeArea & " | " & safeKind &
                " | code=" & code.ToString(Globalization.CultureInfo.InvariantCulture) &
                " | sig=" & Fingerprint(safeArea, safeKind, code) &
                " | " & safeDetail & Environment.NewLine
            SyncLock Sync
                Directory.CreateDirectory(Folder)
                Dim log = CurrentLog
                If File.Exists(log) AndAlso
                   New FileInfo(log).Length +
                       Encoding.UTF8.GetByteCount(line) >= MaximumLogBytes Then
                    If File.Exists(OlderLog) Then File.Delete(OlderLog)
                    File.Move(log, OlderLog)
                End If
                File.AppendAllText(log, line, New UTF8Encoding(False))
            End SyncLock
        Catch
            ' Diagnostics are never permitted to crash DISM GUI or interrupt
            ' imaging, even if the profile is read-only or disk is full.
        End Try
    End Sub

    Public Shared Sub RecordException(area As String, failure As Exception)
        If failure Is Nothing Then Return
        WriteEntry(area, failure.GetType().Name, failure.Message, failure.HResult)
    End Sub

    Public Shared Sub RecordDismExit(area As String, exitCode As String)
        Dim parsed As Integer
        If String.IsNullOrWhiteSpace(exitCode) OrElse exitCode.Trim() = "0" OrElse
           exitCode.Trim() = "3010" Then Return
        If Not Integer.TryParse(exitCode, parsed) Then
            parsed = -1
        End If
        WriteEntry(area, "DISM exit", "Nonzero DISM process exit. " &
                   "The full DISM console output is not recorded automatically.", parsed)
    End Sub

    Public Shared Function ReadRecent(Optional count As Integer = 15) As String
        Dim limit = Math.Max(1, Math.Min(50, count))
        Try
            SyncLock Sync
                Dim entries As New List(Of String)()
                For Each fileName As String In {OlderLog, CurrentLog}
                    If File.Exists(fileName) Then
                        ' Journal is bounded; read only two local sanitized files.
                        entries.AddRange(File.ReadAllLines(fileName, Encoding.UTF8).
                            Where(Function(s) Not String.IsNullOrWhiteSpace(s)))
                    End If
                Next
                Return If(entries.Count = 0, "(no recorded runtime errors)",
                          ErrorDiagnostics.Redact(String.Join(Environment.NewLine,
                              entries.Skip(Math.Max(0, entries.Count - limit)))))
            End SyncLock
        Catch
            Return "(local diagnostic history unavailable)"
        End Try
    End Function

    Public Shared Sub ClearHistory()
        Try
            SyncLock Sync
                If File.Exists(CurrentLog) Then File.Delete(CurrentLog)
                If File.Exists(OlderLog) Then File.Delete(OlderLog)
            End SyncLock
        Catch
            Throw New IOException("Could not clear local diagnostics. Check file access.")
        End Try
    End Sub

    Private Shared Sub OnUnhandled(sender As Object, e As UnhandledExceptionEventArgs)
        Try
            Dim failure = TryCast(e.ExceptionObject, Exception)
            If failure IsNot Nothing Then
                ErrorDiagnostics.RememberFailure("Unhandled process exception", failure)
            Else
                WriteEntry("Application", "Unhandled process error",
                           "Unspecified failure object.", -1)
            End If
        Catch
        End Try
        ' Never claim to recover from a fatal .NET exception.
    End Sub

    Private Shared Sub OnTaskUnobserved(sender As Object, e As UnobservedTaskExceptionEventArgs)
        Try
            If e.Exception IsNot Nothing Then
                ErrorDiagnostics.RememberFailure("Unobserved asynchronous task", e.Exception)
            End If
        Catch
        End Try
        ' Do not modify default task-exception policy.
    End Sub

    Private Shared Sub OnUiThreadException(sender As Object, e As ThreadExceptionEventArgs)
        If e Is Nothing OrElse e.Exception Is Nothing Then Return
        Try
            ErrorDiagnostics.RememberFailure("Unhandled UI event", e.Exception)
        Catch
        End Try
        If Interlocked.CompareExchange(showingPrompt, 1, 0) <> 0 Then Return
        Try
            Dim answer = MessageBox.Show(
                "An unexpected DISM GUI error was recorded locally." &
                Environment.NewLine & Environment.NewLine &
                "Open the sanitized support report for review?" &
                Environment.NewLine &
                "Nothing will be uploaded automatically.",
                "DISM GUI - Error Diagnostics",
                MessageBoxButtons.YesNo, MessageBoxIcon.Error)
            If answer = DialogResult.Yes Then
                Using viewer As New ErrorReportForm("", "Unexpected UI event")
                    viewer.ShowDialog()
                End Using
            End If
        Catch
            ' Diagnostics UI itself must never trigger a repeated modal loop.
        Finally
            Interlocked.Exchange(showingPrompt, 0)
        End Try
    End Sub

    Public Shared Sub Initialize()
        If Interlocked.CompareExchange(initialized, 1, 0) <> 0 Then Return
        Try
            AddHandler Application.ThreadException, AddressOf OnUiThreadException
            AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandled
            AddHandler TaskScheduler.UnobservedTaskException, AddressOf OnTaskUnobserved
        Catch
            Interlocked.Exchange(initialized, 0)
        End Try
    End Sub
End Class
