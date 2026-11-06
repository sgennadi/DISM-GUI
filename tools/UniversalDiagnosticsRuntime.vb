Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json

' App-specific, local-only bridge from existing runtime failures to the
' canonical universal-diagnostics/v1 embedded capture. Never sends network
' requests; never copies exception messages, raw DISM output or file paths.
Public NotInheritable Class UniversalDiagnosticsRuntime
    Private Sub New()
    End Sub

    Private Const Project As String = "sgennadi/DISM-GUI"
    Private Const MaxQueueRecords As Integer = 1500
    Private Const MaxRecentScan As Integer = MaxQueueRecords
    Private Shared ReadOnly Gate As New Object()
    Private Shared ReadOnly Seen As New Dictionary(Of String, DateTime)(
        StringComparer.Ordinal)
    Private Shared ReadOnly RepeatWindow As TimeSpan = TimeSpan.FromHours(24)

    Private Shared Function Category(area As String) As String
        Dim text = If(area, "")
        If text.IndexOf("Driver Studio", StringComparison.OrdinalIgnoreCase) >= 0 Then
            Return "Driver Studio"
        End If
        If text.IndexOf("WIM 360", StringComparison.OrdinalIgnoreCase) >= 0 Then
            If text.IndexOf("driver", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return "WIM 360 Drivers"
            End If
            If text.IndexOf("package", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return "WIM 360 Packages"
            End If
            If text.IndexOf("component", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return "WIM 360 Components"
            End If
            If text.IndexOf("metadata", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return "WIM 360 Metadata"
            End If
            Return "WIM 360"
        End If
        If text.IndexOf("Mounted WIM", StringComparison.OrdinalIgnoreCase) >= 0 Then
            Return "WIM 360"
        End If
        If text.IndexOf("DISM", StringComparison.OrdinalIgnoreCase) >= 0 Then
            Return "DISM"
        End If
        Return "Application"
    End Function

    Private Shared Function ErrorClass(kind As String) As String
        Dim value = If(kind, "").Trim()
        If value.Equals("DISM exit", StringComparison.OrdinalIgnoreCase) Then
            Return "DismExit"
        End If
        If value.Length = 0 OrElse value.Length > 64 OrElse
           Not value.All(Function(c) (c >= "A"c AndAlso c <= "Z"c) OrElse
                                   (c >= "a"c AndAlso c <= "z"c) OrElse
                                   (c >= "0"c AndAlso c <= "9"c) OrElse
                                   c = "_"c) Then
            Return "Exception"
        End If
        Return value
    End Function

    Private Shared Function Signature(component As String,
                                      kind As String, code As String) As String
        Dim normalized = String.Join("|", {
            Project.ToLowerInvariant(), component.ToLowerInvariant(),
            kind.ToLowerInvariant(), code.ToLowerInvariant()
        })
        Return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).
            ToLowerInvariant()
    End Function

    Private Shared Function AlreadyStored(folder As String, fingerprint As String,
                                         cutoff As DateTime) As Boolean
        If Not Directory.Exists(folder) Then Return False
        Dim scanned As Integer = 0
        For Each item In New DirectoryInfo(folder).EnumerateFiles("*.json").
                         OrderByDescending(Function(x) x.LastWriteTimeUtc)
            If item.LastWriteTimeUtc < cutoff Then Exit For
            If scanned >= MaxRecentScan Then Exit For
            scanned += 1
            If item.Length < 40 OrElse item.Length > 65536 Then Continue For
            Try
                Using doc = JsonDocument.Parse(File.ReadAllText(item.FullName, Encoding.UTF8))
                    Dim eventNode As JsonElement
                    Dim hash As JsonElement
                    If doc.RootElement.TryGetProperty("event", eventNode) AndAlso
                       eventNode.ValueKind = JsonValueKind.Object AndAlso
                       eventNode.TryGetProperty("fingerprint", hash) AndAlso
                       hash.ValueKind = JsonValueKind.String AndAlso
                       String.Equals(hash.GetString(), fingerprint,
                                     StringComparison.OrdinalIgnoreCase) Then
                        Return True
                    End If
                End Using
            Catch ex As Exception When TypeOf ex Is IOException OrElse
                                       TypeOf ex Is UnauthorizedAccessException OrElse
                                       TypeOf ex Is JsonException
                ' A corrupt saved record must not break DISM or other captures.
            End Try
        Next
        Return False
    End Function

    ' True only when a NEW local record was captured. Repeated signatures
    ' within 24 hours are ignored, across application restarts as well.
    ' Unhandled failures of diagnostics NEVER affect image servicing.
    Public Shared Function RecordFailure(area As String, kind As String,
                                         code As Integer) As Boolean
        Try
            Dim component = Category(area)
            Dim errorType = ErrorClass(kind)
            Dim errorCode = code.ToString(CultureInfo.InvariantCulture)
            Dim fingerprint = Signature(component, errorType, errorCode)
            Dim utcNow = DateTime.UtcNow
            SyncLock Gate
                Dim previous As DateTime
                If Seen.TryGetValue(fingerprint, previous) AndAlso
                   utcNow - previous < RepeatWindow Then Return False

                Dim folder = UniversalDiagnosticsEmbedded.QueueDirectory(Project)
                If AlreadyStored(folder, fingerprint, utcNow.Subtract(RepeatWindow)) Then
                    Seen(fingerprint) = utcNow
                    Return False
                End If
                If Directory.Exists(folder) AndAlso
                   Directory.EnumerateFiles(folder, "*.json").Take(
                       MaxQueueRecords + 1).Count() >= MaxQueueRecords Then
                    Return False
                End If

                Dim payload As New Dictionary(Of String, String) From {
                    {"project", Project},
                    {"component", component},
                    {"error_type", errorType},
                    {"code", errorCode},
                    {"version", GetType(UniversalDiagnosticsRuntime).Assembly.
                        GetName().Version.ToString(3)},
                    {"level", "error"},
                    {"summary", component & " failure (" & errorType &
                        ", code " & errorCode & "). Local review required."}
                }
                UniversalDiagnosticsEmbedded.Capture(JsonSerializer.Serialize(payload))
                Seen(fingerprint) = utcNow
                If Seen.Count > 1024 Then
                    For Each oldKey In Seen.Where(Function(x) utcNow - x.Value >= RepeatWindow).
                                            Select(Function(x) x.Key).ToArray()
                        Seen.Remove(oldKey)
                    Next
                End If
                Return True
            End SyncLock
        Catch
            Return False
        End Try
    End Function
End Class
