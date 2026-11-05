Imports System
Imports System.ComponentModel
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Runtime.InteropServices
Imports System.Security
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Win32

' Direct-to-GitHub authorization. Requires a GitHub account and a GitHub App
' installed on sgennadi/DISM-GUI with Issues: Read & write, Device Flow enabled.
' A GitHub App Client ID is PUBLIC; no app/client secret is in the executable.
' User tokens are stored in the Windows Credential Manager for this user only.
Public NotInheritable Class DirectGitHubAccount
    Private Sub New()
    End Sub

    Public Const TargetRepository As String = "sgennadi/DISM-GUI"
    Private Const CredentialName As String = "DISMGUI.GitHub.DirectIssue.v1"
    Private Const RegistryKey As String = "Software\DISMGUI\DirectGitHub"

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure Credential
        Public Flags As UInteger
        Public Type As UInteger
        <MarshalAs(UnmanagedType.LPWStr)>
        Public TargetName As String
        <MarshalAs(UnmanagedType.LPWStr)>
        Public Comment As String
        Public LastWritten As Long
        Public CredentialBlobSize As UInteger
        Public CredentialBlob As IntPtr
        Public Persist As UInteger
        Public AttributeCount As UInteger
        Public Attributes As IntPtr
        <MarshalAs(UnmanagedType.LPWStr)>
        Public TargetAlias As String
        <MarshalAs(UnmanagedType.LPWStr)>
        Public UserName As String
    End Structure

    <DllImport("advapi32.dll", CharSet:=CharSet.Unicode, SetLastError:=True,
               EntryPoint:="CredWriteW")>
    Private Shared Function CredWrite(ByRef credential As Credential,
                                      flags As UInteger) As Boolean
    End Function

    <DllImport("advapi32.dll", CharSet:=CharSet.Unicode, SetLastError:=True,
               EntryPoint:="CredReadW")>
    Private Shared Function CredRead(target As String, kind As UInteger,
                                    flags As UInteger, ByRef ptr As IntPtr) As Boolean
    End Function

    <DllImport("advapi32.dll", CharSet:=CharSet.Unicode, SetLastError:=True,
               EntryPoint:="CredDeleteW")>
    Private Shared Function CredDelete(target As String, kind As UInteger,
                                      flags As UInteger) As Boolean
    End Function

    <DllImport("advapi32.dll", EntryPoint:="CredFree")>
    Private Shared Sub CredFree(ptr As IntPtr)
    End Sub

    Public Shared Function ValidClientId(value As String) As Boolean
        Return Not String.IsNullOrWhiteSpace(value) AndAlso
               Regex.IsMatch(value.Trim(), "^[A-Za-z0-9._-]{6,100}$",
                             RegexOptions.CultureInvariant)
    End Function

    Public Shared Function ReadClientId() As String
        Try
            Using key = Registry.CurrentUser.OpenSubKey(RegistryKey)
                Dim stored = TryCast(key?.GetValue("GitHubAppClientId"), String)
                Return If(ValidClientId(stored), stored.Trim(), "")
            End Using
        Catch
            Return ""
        End Try
    End Function

    Public Shared Sub SaveClientId(value As String)
        If Not ValidClientId(value) Then
            Throw New ArgumentException("Enter the public Client ID of the GitHub App.")
        End If
        Using key = Registry.CurrentUser.CreateSubKey(RegistryKey)
            key.SetValue("GitHubAppClientId", value.Trim(), RegistryValueKind.String)
        End Using
    End Sub

    Public Shared Function ReadToken() As String
        Dim ptr As IntPtr = IntPtr.Zero
        If Not CredRead(CredentialName, 1UI, 0UI, ptr) Then Return ""
        Try
            Dim record = Marshal.PtrToStructure(Of Credential)(ptr)
            If record.CredentialBlobSize = 0 OrElse
               record.CredentialBlobSize > 8192 OrElse
               record.CredentialBlob = IntPtr.Zero Then Return ""
            Dim buffer(CInt(record.CredentialBlobSize) - 1) As Byte
            Marshal.Copy(record.CredentialBlob, buffer, 0, buffer.Length)
            Try
                Return Encoding.UTF8.GetString(buffer)
            Finally
                Array.Clear(buffer, 0, buffer.Length)
            End Try
        Finally
            CredFree(ptr)
        End Try
    End Function

    Public Shared Sub StoreToken(token As String)
        If String.IsNullOrWhiteSpace(token) OrElse token.Length > 5000 Then
            Throw New SecurityException("GitHub returned an invalid access token.")
        End If
        Dim bytes = Encoding.UTF8.GetBytes(token)
        Dim ptr = Marshal.AllocHGlobal(bytes.Length)
        Try
            Marshal.Copy(bytes, 0, ptr, bytes.Length)
            Dim entry As New Credential With {
                .Type = 1UI,
                .TargetName = CredentialName,
                .UserName = "GitHub App user token",
                .CredentialBlobSize = CUInt(bytes.Length),
                .CredentialBlob = ptr,
                .Persist = 2UI
            }
            If Not CredWrite(entry, 0UI) Then
                Throw New Win32Exception(Marshal.GetLastWin32Error(),
                    "Windows Credential Manager could not save GitHub authorization.")
            End If
        Finally
            Array.Clear(bytes, 0, bytes.Length)
            Dim zeros(Math.Max(0, bytes.Length - 1)) As Byte
            Marshal.Copy(zeros, 0, ptr, bytes.Length)
            Marshal.FreeHGlobal(ptr)
        End Try
    End Sub

    Public Shared Sub Disconnect()
        ' Remove saved token; no attempts to revoke a token remotely.
        If Not CredDelete(CredentialName, 1UI, 0UI) Then
            Const NotFound As Integer = 1168
            Dim errorCode = Marshal.GetLastWin32Error()
            If errorCode <> NotFound Then
                Throw New Win32Exception(errorCode, "Could not clear saved GitHub credential.")
            End If
        End If
    End Sub
End Class

Public NotInheritable Class DirectGitHubClient
    Private ReadOnly webClient As HttpClient
    Private Const DeviceEndpoint As String = "https://github.com/login/device/code"
    Private Const TokenEndpoint As String = "https://github.com/login/oauth/access_token"
    Private Const UserEndpoint As String = "https://api.github.com/user"
    Private Const IssueEndpoint As String =
        "https://api.github.com/repos/sgennadi/DISM-GUI/issues"

    Public NotInheritable Class DeviceCode
        Public Property DeviceSecret As String = ""
        Public Property UserCode As String = ""
        Public Property VerificationUri As String = ""
        Public Property IntervalSeconds As Integer
        Public Property ExpiresSeconds As Integer
    End Class

    Public NotInheritable Class IssueResult
        Public Property Number As Integer
        Public Property Url As String = ""
    End Class

    Public Sub New(Optional client As HttpClient = Nothing)
        webClient = If(client, New HttpClient())
        webClient.Timeout = TimeSpan.FromSeconds(25)
    End Sub

    Private Shared Function TextField(root As JsonElement, field As String,
                                     Optional fallback As String = "") As String
        Dim value As JsonElement
        If root.ValueKind <> JsonValueKind.Object OrElse
           Not root.TryGetProperty(field, value) Then Return fallback
        Return If(value.ValueKind = JsonValueKind.String,
                  If(value.GetString(), fallback), fallback)
    End Function

    Private Shared Function IntegerField(root As JsonElement, field As String,
                                        fallback As Integer) As Integer
        Dim value As JsonElement
        Dim number As Integer
        If root.ValueKind = JsonValueKind.Object AndAlso
           root.TryGetProperty(field, value) AndAlso
           value.ValueKind = JsonValueKind.Number AndAlso
           value.TryGetInt32(number) Then Return number
        Return fallback
    End Function

    Private Async Function PostDeviceFormAsync(endpoint As String,
        values As IEnumerable(Of KeyValuePair(Of String, String)),
        cancellation As CancellationToken) As Task(Of JsonDocument)
        ' Authentication form is posted to a fixed github.com endpoint,
        ' never to a caller-supplied URL. No access token is logged.
        Using request As New HttpRequestMessage(HttpMethod.Post, endpoint)
            request.Headers.Accept.Add(
                New MediaTypeWithQualityHeaderValue("application/json"))
            request.Headers.UserAgent.ParseAdd("DISM-GUI-Direct-Report/4.4")
            request.Content = New FormUrlEncodedContent(values)
            Using response = Await webClient.SendAsync(request, cancellation)
                If Not response.IsSuccessStatusCode Then
                    Throw New InvalidOperationException("GitHub authorization returned HTTP " &
                        CInt(response.StatusCode).ToString() & ".")
                End If
                Dim body = Await response.Content.ReadAsStringAsync(cancellation)
                Return JsonDocument.Parse(body)
            End Using
        End Using
    End Function

    Public Async Function StartDeviceFlowAsync(clientId As String,
        cancellation As CancellationToken) As Task(Of DeviceCode)
        If Not DirectGitHubAccount.ValidClientId(clientId) Then
            Throw New ArgumentException("A valid public GitHub App Client ID is required.")
        End If
        Using doc = Await PostDeviceFormAsync(DeviceEndpoint,
                New Dictionary(Of String, String) From {
                    {"client_id", clientId.Trim()}
                }, cancellation)
            Dim root = doc.RootElement
            Dim errorName = TextField(root, "error")
            If errorName.Length > 0 Then
                Throw New InvalidOperationException("GitHub device flow failed: " &
                    ErrorDiagnostics.Redact(errorName))
            End If
            Dim result As New DeviceCode With {
                .DeviceSecret = TextField(root, "device_code"),
                .UserCode = TextField(root, "user_code"),
                .VerificationUri = TextField(root, "verification_uri"),
                .IntervalSeconds = Math.Max(5, Math.Min(60,
                    IntegerField(root, "interval", 5))),
                .ExpiresSeconds = Math.Max(60, Math.Min(900,
                    IntegerField(root, "expires_in", 900)))
            }
            Dim verification As Uri = Nothing
            If result.DeviceSecret.Length < 15 OrElse
               result.UserCode.Length < 5 OrElse
               Not Uri.TryCreate(result.VerificationUri, UriKind.Absolute, verification) OrElse
               verification.Scheme <> Uri.UriSchemeHttps OrElse
               Not String.Equals(verification.Host, "github.com",
                                 StringComparison.OrdinalIgnoreCase) OrElse
               verification.AbsolutePath <> "/login/device" Then
                Throw New SecurityException("GitHub returned an unexpected device login URL/code.")
            End If
            Return result
        End Using
    End Function

    Public Async Function FinishDeviceFlowAsync(clientId As String,
            code As DeviceCode, cancellation As CancellationToken) As Task(Of String)
        If code Is Nothing OrElse code.DeviceSecret.Length < 15 Then
            Throw New ArgumentException("Start a GitHub device authorization first.")
        End If
        Dim deadline = DateTimeOffset.UtcNow.AddSeconds(code.ExpiresSeconds)
        Dim interval = code.IntervalSeconds
        While DateTimeOffset.UtcNow < deadline
            Await Task.Delay(TimeSpan.FromSeconds(interval), cancellation)
            Using doc = Await PostDeviceFormAsync(TokenEndpoint,
                  New Dictionary(Of String, String) From {
                    {"client_id", clientId.Trim()},
                    {"device_code", code.DeviceSecret},
                    {"grant_type", "urn:ietf:params:oauth:grant-type:device_code"}
                  }, cancellation)
                Dim root = doc.RootElement
                Dim token = TextField(root, "access_token")
                If token.Length > 15 Then Return token
                Select Case TextField(root, "error")
                    Case "authorization_pending"
                        Continue While
                    Case "slow_down"
                        interval = Math.Min(90, Math.Max(
                            IntegerField(root, "interval", interval + 5), interval + 5))
                    Case "access_denied"
                        Throw New UnauthorizedAccessException(
                            "GitHub authorization was declined by the user.")
                    Case "expired_token", "token_expired"
                        Throw New TimeoutException("GitHub device code expired. Connect again.")
                    Case "device_flow_disabled"
                        Throw New InvalidOperationException(
                            "The repository owner must enable Device Flow for the GitHub App.")
                    Case Else
                        Throw New InvalidOperationException(
                            "GitHub device authentication was unsuccessful. " &
                            "Check GitHub App installation and permissions.")
                End Select
            End Using
        End While
        Throw New TimeoutException("GitHub device authorization expired.")
    End Function

    Private Shared Function AuthenticatedRequest(method As HttpMethod,
        endpoint As String, token As String) As HttpRequestMessage
        If String.IsNullOrWhiteSpace(token) OrElse token.Length > 5000 Then
            Throw New SecurityException("Connect GitHub before sending a report.")
        End If
        Dim request As New HttpRequestMessage(method, endpoint)
        request.Headers.Authorization =
            New AuthenticationHeaderValue("Bearer", token)
        request.Headers.UserAgent.ParseAdd("DISM-GUI-Direct-Report/4.4")
        request.Headers.Accept.Add(New MediaTypeWithQualityHeaderValue(
            "application/vnd.github+json"))
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28")
        Return request
    End Function

    Public Async Function WhoAmIAsync(token As String,
        cancellation As CancellationToken) As Task(Of String)
        Using request = AuthenticatedRequest(HttpMethod.Get, UserEndpoint, token)
            Using response = Await webClient.SendAsync(request, cancellation)
                If Not response.IsSuccessStatusCode Then
                    Throw New UnauthorizedAccessException("GitHub authorization is invalid (HTTP " &
                        CInt(response.StatusCode).ToString() & ").")
                End If
                Using doc = JsonDocument.Parse(Await response.Content.ReadAsStringAsync(cancellation))
                    Dim username = TextField(doc.RootElement, "login")
                    If String.IsNullOrWhiteSpace(username) OrElse username.Length > 100 Then
                        Throw New InvalidOperationException("GitHub user account response is invalid.")
                    End If
                    Return username
                End Using
            End Using
        End Using
    End Function

    Public Async Function CreateIssueAsync(token As String, title As String,
        reviewedReport As String, cancellation As CancellationToken) As Task(Of IssueResult)
        If String.IsNullOrWhiteSpace(reviewedReport) OrElse
           reviewedReport.Length > 16000 Then
            Throw New ArgumentException("Reviewed diagnostic report must be 1-16000 characters.")
        End If
        Dim safeTitle = ErrorDiagnostics.BuildTitle(title)
        Dim safeBody = ErrorDiagnostics.Redact(reviewedReport)
        ' The final body is sanitized once more; caller must have displayed
        ' the report and required explicit user consent before calling this.
        Using request = AuthenticatedRequest(HttpMethod.Post, IssueEndpoint, token)
            request.Content = New StringContent(
                JsonSerializer.Serialize(New With {
                    .title = safeTitle,
                    .body = safeBody
                }), Encoding.UTF8, "application/json")
            Using response = Await webClient.SendAsync(request, cancellation)
                If response.StatusCode <> HttpStatusCode.Created Then
                    If response.StatusCode = HttpStatusCode.Unauthorized Then
                        Throw New UnauthorizedAccessException(
                            "GitHub session expired. Connect again.")
                    End If
                    If response.StatusCode = HttpStatusCode.Forbidden Then
                        Throw New UnauthorizedAccessException(
                            "GitHub App is not installed on DISM-GUI with Issues: write, " &
                            "or your GitHub account does not have permission.")
                    End If
                    Throw New InvalidOperationException(
                        "GitHub could not create an Issue (HTTP " &
                        CInt(response.StatusCode).ToString() & ").")
                End If
                Using doc = JsonDocument.Parse(Await response.Content.ReadAsStringAsync(cancellation))
                    Dim issueUrl = TextField(doc.RootElement, "html_url")
                    Dim number = IntegerField(doc.RootElement, "number", 0)
                    Dim page As Uri = Nothing
                    If number < 1 OrElse
                       Not Uri.TryCreate(issueUrl, UriKind.Absolute, page) OrElse
                       page.Scheme <> Uri.UriSchemeHttps OrElse
                       Not String.Equals(page.Host, "github.com",
                          StringComparison.OrdinalIgnoreCase) OrElse
                       Not page.AbsolutePath.StartsWith(
                           "/sgennadi/DISM-GUI/issues/",
                           StringComparison.OrdinalIgnoreCase) Then
                        Throw New SecurityException("GitHub Issue response was not trusted.")
                    End If
                    Return New IssueResult With {.Number = number, .Url = issueUrl}
                End Using
            End Using
        End Using
    End Function
End Class
