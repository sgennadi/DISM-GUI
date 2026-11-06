Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports System.Text.Json

' Local status bookkeeping ONLY. It never calls the GitHub API, launches a
' browser or treats opening an Issue draft as successful submission.
Public NotInheritable Class UniversalDiagnosticsDelivery
    Private Sub New()
    End Sub

    Private Const Project As String = "sgennadi/DISM-GUI"
    Private Shared ReadOnly Gate As New Object()

    Public Shared Function NormalizeIssueUrl(raw As String) As String
        Dim link As Uri = Nothing
        If Not Uri.TryCreate(If(raw, "").Trim(), UriKind.Absolute, link) OrElse
           Not String.Equals(link.Scheme, "https", StringComparison.OrdinalIgnoreCase) OrElse
           Not String.Equals(link.Host, "github.com", StringComparison.OrdinalIgnoreCase) OrElse
           Not link.IsDefaultPort OrElse
           Not String.IsNullOrEmpty(link.UserInfo) OrElse
           Not String.IsNullOrEmpty(link.Query) OrElse
           Not String.IsNullOrEmpty(link.Fragment) Then
            Throw New ArgumentException("Enter the published DISM GUI GitHub Issue URL.")
        End If
        Dim parts = link.AbsolutePath.Split("/"c, StringSplitOptions.RemoveEmptyEntries)
        Dim number As Long
        If parts.Length <> 4 OrElse
           Not String.Equals(parts(0), "sgennadi", StringComparison.Ordinal) OrElse
           Not String.Equals(parts(1), "DISM-GUI", StringComparison.Ordinal) OrElse
           Not String.Equals(parts(2), "issues", StringComparison.Ordinal) OrElse
           Not Long.TryParse(parts(3), number) OrElse number <= 0 Then
            Throw New ArgumentException("Only https://github.com/sgennadi/DISM-GUI/issues/<number> is accepted.")
        End If
        Return "https://github.com/sgennadi/DISM-GUI/issues/" &
            number.ToString(Globalization.CultureInfo.InvariantCulture)
    End Function

    Public Shared Function SafeIssueUrl(raw As String) As String
        Try
            Return NormalizeIssueUrl(raw)
        Catch ex As ArgumentException
            Return ""
        End Try
    End Function

    ' A user-supplied Issue link records a manual declaration, not proof of a
    ' successful GitHub upload. Resetting to pending removes the local link.
    Public Shared Sub SetStatus(reportId As String, state As String,
                                Optional submittedUrl As String = "")
        Dim id As Guid
        If Not Guid.TryParseExact(If(reportId, ""), "D", id) Then
            Throw New ArgumentException("Invalid local report ID.")
        End If
        If state <> "pending" AndAlso state <> "submitted" Then
            Throw New ArgumentException("Invalid local delivery state.")
        End If
        Dim link = If(state = "submitted", NormalizeIssueUrl(submittedUrl), "")
        Dim folder = UniversalDiagnosticsEmbedded.QueueDirectory(Project)
        Dim filename = Path.Combine(folder, id.ToString("D") & ".json")
        SyncLock Gate
            Dim details As New FileInfo(filename)
            If Not details.Exists OrElse details.Length < 40 OrElse
               details.Length > 65536 OrElse
               (details.Attributes And FileAttributes.ReparsePoint) <> 0 Then
                Throw New InvalidDataException("The local report is missing or unsafe.")
            End If
            Dim updated As Byte()
            Using source = JsonDocument.Parse(File.ReadAllText(filename, Encoding.UTF8))
                Dim ev As JsonElement
                Dim prior As JsonElement
                If source.RootElement.ValueKind <> JsonValueKind.Object OrElse
                   Not source.RootElement.TryGetProperty("event", ev) OrElse
                   ev.ValueKind <> JsonValueKind.Object OrElse
                   Not source.RootElement.TryGetProperty("delivery", prior) OrElse
                   prior.ValueKind <> JsonValueKind.Object Then
                    Throw New InvalidDataException("Invalid local diagnostic record.")
                End If
                Dim storedId As JsonElement
                Dim storedProject As JsonElement
                Dim storedSchema As JsonElement
                If Not ev.TryGetProperty("id", storedId) OrElse
                   storedId.ValueKind <> JsonValueKind.String OrElse
                   Not String.Equals(storedId.GetString(), id.ToString("D"),
                                     StringComparison.OrdinalIgnoreCase) OrElse
                   Not ev.TryGetProperty("project", storedProject) OrElse
                   storedProject.GetString() <> Project OrElse
                   Not ev.TryGetProperty("schema", storedSchema) OrElse
                   storedSchema.GetString() <> UniversalDiagnosticsEmbedded.Schema Then
                    Throw New InvalidDataException("The report identity does not match its filename.")
                End If
                Dim document As New Dictionary(Of String, Object) From {
                    {"event", ev.Clone()},
                    {"delivery", New Dictionary(Of String, String) From {
                        {"status", state}, {"url", link}
                    }}
                }
                updated = JsonSerializer.SerializeToUtf8Bytes(document,
                    New JsonSerializerOptions With {.WriteIndented = True})
            End Using
            If updated.Length > 65536 Then
                Throw New InvalidDataException("The report exceeds the allowed size.")
            End If
            Dim temp = Path.Combine(folder, ".delivery-" &
                Guid.NewGuid().ToString("N") & ".tmp")
            Try
                Using output As New FileStream(temp, FileMode.CreateNew,
                                               FileAccess.Write, FileShare.None)
                    output.Write(updated)
                    output.Flush(True)
                End Using
                File.Move(temp, filename, True)
            Finally
                If File.Exists(temp) Then File.Delete(temp)
            End Try
        End SyncLock
    End Sub
End Class
