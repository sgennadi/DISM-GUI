Imports System

Public NotInheritable Class DirectGitHubDedup
    Private Sub New()
    End Sub

    Public Shared Function AlreadySubmitted(title As String, report As String) As Boolean
        Return False
    End Function

    Public Shared Sub Remember(title As String, report As String, issueUrl As String)
    End Sub
End Class
