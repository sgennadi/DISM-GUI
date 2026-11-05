Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Text.RegularExpressions

' Pure diagnostic formatting: no file writing or automatic transmission.
Public NotInheritable Class ErrorDiagnostics
    Private Sub New()
    End Sub

    Public Const IssuesUrl As String = "https://github.com/sgennadi/DISM-GUI/issues"
    Private Const MaxLogCharacters As Integer = 6000
    Private Const MaxReportCharacters As Integer = 14000
    Private Const MaxGitHubBodyCharacters As Integer = 1800
    Private Shared ReadOnly Gate As New Object()
    Private Shared latestFailure As String = ""

    Private Shared Function ReplaceSensitive(value As String, pattern As String,
                                              replacement As String) As String
        Return Regex.Replace(value, pattern, replacement,
            RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(350))
    End Function

    ' Conservative redaction; the operator must still inspect the preview.
    Public Shared Function Redact(input As String) As String
        If String.IsNullOrEmpty(input) Then Return ""
        Dim source = input.Replace(ChrW(0), " "c)
        ' Sensitive key/value text: suppress the entire line.
        source = ReplaceSensitive(source,
            "(?m)^.*\b(?:password|passwd|passphrase|pwd|recovery[\s_-]*(?:key|password)|bitlocker[\s_-]*key|laps[\s_-]*password|access[\s_-]*token|refresh[\s_-]*token|client[\s_-]*secret|api[\s_-]*key|authorization|bearer|private[\s_-]*key|secret[\s_-]*key)\b.*$",
            "[REDACTED: credential or recovery information]")
        source = ReplaceSensitive(source,
            "\b(?:[0-9]{6}-){7}[0-9]{6}\b", "[REDACTED: recovery password]")
        source = ReplaceSensitive(source,
            "\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", "[EMAIL]")
        source = ReplaceSensitive(source,
            "\b(?:[0-9A-F]{2}[:-]){5}[0-9A-F]{2}\b", "[MAC]")
        source = ReplaceSensitive(source,
            "\b[0-9A-F]{8}-(?:[0-9A-F]{4}-){3}[0-9A-F]{12}\b", "[GUID]")
        source = ReplaceSensitive(source,
            "\b(?:\d{1,3}\.){3}\d{1,3}\b", "[IPv4]")
        source = ReplaceSensitive(source,
            "(?<![A-F0-9:])(?:[A-F0-9]{1,4}:){2,7}[A-F0-9]{0,4}(?![A-F0-9:])", "[IPv6]")
        source = ReplaceSensitive(source,
            "https?://[^\s<>""']+", "[URL]")
        ' Conservative path rule avoids leaking usernames, domain servers, shares.
        source = ReplaceSensitive(source,
            "(?<![A-Z0-9])(?:[A-Z]:\\|\\\\)[^\r\n]*", "[LOCAL OR NETWORK PATH]")
        source = ReplaceSensitive(source,
            "\b[A-Z0-9._-]{2,80}\\[A-Z0-9._-]{2,80}\b", "[DOMAIN USER]")
        source = ReplaceSensitive(source,
            "\b(?:eyJ[A-Z0-9_-]{15,}|[A-Z0-9_+/=-]{64,})\b", "[LONG IDENTIFIER]")
        Return source
    End Function

    Public Shared Function RecentOutput(logText As String) As String
        If String.IsNullOrWhiteSpace(logText) Then Return "(no DISM output available)"
        Dim lines = logText.Replace(vbCr, "").Split(ChrW(10))
        Dim tail = String.Join(vbLf, lines.Skip(Math.Max(0, lines.Length - 60)))
        If tail.Length > MaxLogCharacters Then
            tail = tail.Substring(tail.Length - MaxLogCharacters)
        End If
        Return Redact(tail)
    End Function

    Public Shared Sub RememberFailure(operation As String, failure As Exception)
        If failure Is Nothing Then Return
        Dim summary = Redact(If(operation, "Operation") & ": " &
            failure.GetType().Name & ": " & failure.Message)
        If summary.Length > 900 Then summary = summary.Substring(0, 900)
        SyncLock Gate
            latestFailure = summary
        End SyncLock
    End Sub

    Public Shared ReadOnly Property LastFailure As String
        Get
            SyncLock Gate
                Return latestFailure
            End SyncLock
        End Get
    End Property

    Public Shared Function BuildTitle(title As String) As String
        Dim subject = Redact(If(title, "")).Replace(vbCr, " ").Replace(vbLf, " ").Trim()
        If subject.Length = 0 Then subject = "Unexpected DISM GUI error"
        If subject.Length > 105 Then subject = subject.Substring(0, 105)
        Return "[Bug] " & subject
    End Function

    Public Shared Function BuildReport(title As String, description As String,
               includeLog As Boolean, logText As String,
               Optional context As String = "",
               Optional dpi As Integer = 96) As String
        Dim descriptionSafe = Redact(If(description, "").Trim())
        If descriptionSafe.Length > 2500 Then
            descriptionSafe = descriptionSafe.Substring(0, 2500) & vbLf & "(description shortened)"
        End If
        Dim version = GetType(ErrorDiagnostics).Assembly.GetName().Version.ToString(3)
        Dim lines As New List(Of String) From {
            "## Summary",
            BuildTitle(title),
            "",
            "## Steps to reproduce / expected / actual",
            If(descriptionSafe.Length > 0, descriptionSafe,
               "(Please describe the steps and what happened.)"),
            "",
            "## Diagnostic context (sanitized)",
            "- DISM GUI: " & version,
            "- OS version: " & Environment.OSVersion.Version.ToString(),
            "- Process architecture: " & RuntimeInformation.ProcessArchitecture.ToString(),
            "- .NET runtime: " & Environment.Version.ToString(),
            "- Display DPI: " & Math.Max(72, Math.Min(960, dpi)).ToString(),
            "- Area: " & Redact(If(context, "Not specified")),
            "- Last recorded failure: " &
                If(LastFailure.Length > 0, LastFailure, "(none recorded)"),
            "- User-identifying paths, addresses and credentials are redacted.",
            ""
        }
        If includeLog Then
            lines.Add("## DISM output (last 60 lines; sanitized)")
            lines.Add(RecentOutput(logText))
            lines.Add("")
        Else
            lines.Add("DISM output: not included (opt-in only).")
        End If
        lines.Add("## Checklist")
        lines.Add("- [ ] I reviewed this report for sensitive information.")
        Dim result = Redact(String.Join(vbLf, lines))
        If result.Length > MaxReportCharacters Then
            result = result.Substring(0, MaxReportCharacters) & vbLf & "(report shortened)"
        End If
        Return result
    End Function

    ' Prefilled browser issue, not an automatic GitHub API POST.
    ' Submission requires clicking Submit on GitHub in the logged-in browser.
    Public Shared Function BuildGitHubIssueUrl(title As String, report As String) As String
        Dim subject = BuildTitle(title)
        Dim safeBody = Redact(If(report, ""))
        If safeBody.Length > MaxGitHubBodyCharacters Then
            safeBody = safeBody.Substring(0, MaxGitHubBodyCharacters) & vbLf & vbLf &
                "(Report truncated for URL. The complete sanitized report is in the clipboard; " &
                "paste it before submitting.)"
        End If
        Return IssuesUrl & "/new?title=" & Uri.EscapeDataString(subject) &
               "&body=" & Uri.EscapeDataString(safeBody)
    End Function

    Public Shared Sub OpenGitHubIssue(title As String, report As String)
        Process.Start(New ProcessStartInfo(BuildGitHubIssueUrl(title, report)) With {
            .UseShellExecute = True
        })
    End Sub
End Class
