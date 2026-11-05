Imports System
Imports System.Collections.Generic
Imports System.Security.Cryptography
Imports System.Text

' In-process duplicate protection. No report contents are retained.
Public NotInheritable Class DirectGitHubDedup
    Private Sub New()
    End Sub

    Private Shared ReadOnly Guard As New Object()
    Private Shared ReadOnly Submissions As New Dictionary(Of String, DateTimeOffset)()

    Private Shared Function Digest(title As String, report As String) As String
        Dim cleaned = ErrorDiagnostics.BuildTitle(title) & vbLf &
                      ErrorDiagnostics.Redact(report)
        Return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cleaned)))
    End Function

    Public Shared Function AlreadySubmitted(title As String, report As String) As Boolean
        Dim signature = Digest(title, report)
        SyncLock Guard
            Dim whenSent As DateTimeOffset
            Return Submissions.TryGetValue(signature, whenSent) AndAlso
                   whenSent > DateTimeOffset.UtcNow.AddHours(-24)
        End SyncLock
    End Function

    Public Shared Sub Remember(title As String, report As String, issueUrl As String)
        Dim signature = Digest(title, report)
        SyncLock Guard
            Submissions(signature) = DateTimeOffset.UtcNow
        End SyncLock
    End Sub
End Class
