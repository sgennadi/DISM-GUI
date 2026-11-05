Imports System
Imports System.Diagnostics
Imports System.Drawing
Imports System.Security
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms
Imports Microsoft.Win32

' Explicit, user-authorized direct submission. Never opens, uploads or
' creates Issues without a user click. No server or GitHub secret is needed.
Public NotInheritable Class DirectGitHubSendForm
    Inherits Form

    Private ReadOnly issueTitle As String
    Private ReadOnly reviewedReport As String
    Private ReadOnly client As New DirectGitHubClient()
    Private ReadOnly clientIdBox As TextBox
    Private ReadOnly authStatus As Label
    Private ReadOnly codeBox As TextBox
    Private ReadOnly reportBox As TextBox
    Private ReadOnly reviewBox As CheckBox
    Private ReadOnly connectButton As Button
    Private ReadOnly sendButton As Button
    Private ReadOnly disconnectButton As Button
    Private ReadOnly copyCodeButton As Button
    Private ReadOnly browserButton As Button
    Private ReadOnly resultLabel As Label
    Private ReadOnly cancelButton As Button
    Private session As CancellationTokenSource
    Private connectedLogin As String = ""
    Private submitted As Boolean

    Public Sub New(title As String, report As String)
        issueTitle = If(title, "")
        reviewedReport = ErrorDiagnostics.Redact(If(report, ""))
        If String.IsNullOrWhiteSpace(reviewedReport) Then
            Throw New ArgumentException("A reviewed diagnostic report is required.")
        End If
        Me.Name = "DirectGitHubSendForm"
        Me.Text = "DISM GUI - Send reviewed error directly to GitHub"
        Me.Size = New Size(1000, 750)
        Me.MinimumSize = New Size(800, 620)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.Font = ModernTheme.UiFont(9.2F)
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.BackColor = Color.White
        Me.ShowInTaskbar = False
        Try
            Me.Icon = ModernTheme.MakeAppIcon()
        Catch
        End Try

        Dim root As New TableLayoutPanel With {
            .Name = "DirectGitHubLayout", .Dock = DockStyle.Fill,
            .ColumnCount = 1, .RowCount = 7,
            .Padding = New Padding(17, 13, 17, 13)
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each height As Integer In {56, 49, 49, 44, 44}
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, height))
        Next
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 105))
        Controls.Add(root)

        Dim headline As New Label With {
            .Name = "lblDirectGitHubTitle",
            .Dock = DockStyle.Fill,
            .Text = "DIRECT GITHUB ISSUE  |  USER-REVIEWED REPORT",
            .Font = ModernTheme.UiFont(13.0F, True),
            .ForeColor = ModernTheme.TextInk,
            .TextAlign = ContentAlignment.MiddleLeft,
            .AutoEllipsis = True
        }
        root.Controls.Add(headline, 0, 0)

        Dim clientRow As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1
        }
        clientRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        clientRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 153))
        clientRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        clientIdBox = New TextBox With {
            .Name = "txtDirectClientId", .Dock = DockStyle.Fill,
            .Text = DirectGitHubAccount.ReadClientId(),
            .PlaceholderText = "GitHub App public Client ID (set by DISM GUI owner)",
            .Margin = New Padding(0, 4, 9, 7)
        }
        clientRow.Controls.Add(clientIdBox, 0, 0)
        connectButton = New Button With {
            .Name = "btnDirectConnect", .Text = "Connect GitHub",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 3, 0, 6)
        }
        ModernTheme.StyleButton(connectButton, "primary")
        AddHandler connectButton.Click, AddressOf ConnectClicked
        clientRow.Controls.Add(connectButton, 1, 0)
        root.Controls.Add(clientRow, 0, 1)

        authStatus = New Label With {
            .Name = "lblDirectAuthStatus",
            .Text = "Not connected. Use a GitHub account; no password is shared with DISM GUI.",
            .Dock = DockStyle.Fill, .AutoEllipsis = True,
            .ForeColor = ModernTheme.Muted,
            .TextAlign = ContentAlignment.MiddleLeft
        }
        root.Controls.Add(authStatus, 0, 2)

        Dim verifyRow As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1
        }
        verifyRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        verifyRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130))
        verifyRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 155))
        verifyRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        codeBox = New TextBox With {
            .Name = "txtDirectUserCode",
            .Dock = DockStyle.Fill, .ReadOnly = True,
            .PlaceholderText = "Device verification code appears here",
            .Font = ModernTheme.UiFont(11.5F, True),
            .Margin = New Padding(0, 4, 9, 5)
        }
        verifyRow.Controls.Add(codeBox, 0, 0)
        copyCodeButton = New Button With {
            .Name = "btnDirectCopyCode", .Text = "Copy code",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(0, 3, 7, 5)
        }
        ModernTheme.StyleButton(copyCodeButton, "browse")
        AddHandler copyCodeButton.Click, Sub(sender, e)
            Try
                If codeBox.TextLength > 0 Then Clipboard.SetText(codeBox.Text)
            Catch ex As Exception
                resultLabel.Text = "Could not copy code: " & ex.GetType().Name
            End Try
        End Sub
        verifyRow.Controls.Add(copyCodeButton, 1, 0)
        browserButton = New Button With {
            .Name = "btnDirectOpenBrowser", .Text = "Open GitHub login",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(0, 3, 0, 5)
        }
        ModernTheme.StyleButton(browserButton, "browse")
        AddHandler browserButton.Click, Sub(sender, e)
            Try
                Process.Start(New ProcessStartInfo(
                    "https://github.com/login/device") With {.UseShellExecute = True})
            Catch ex As Exception
                resultLabel.Text = "Browser could not be opened: " & ex.GetType().Name
            End Try
        End Sub
        verifyRow.Controls.Add(browserButton, 2, 0)
        root.Controls.Add(verifyRow, 0, 3)

        Dim previewHeading As New Label With {
            .Name = "lblDirectPreviewHeading",
            .Text = "REPORT TO BE SENT  •  " & DirectGitHubAccount.TargetRepository &
                    "  •  GitHub Issue will be public",
            .Dock = DockStyle.Fill, .ForeColor = ModernTheme.TextInk,
            .TextAlign = ContentAlignment.MiddleLeft, .AutoEllipsis = True
        }
        root.Controls.Add(previewHeading, 0, 4)

        reportBox = New TextBox With {
            .Name = "txtDirectReviewedReport",
            .Dock = DockStyle.Fill, .Multiline = True,
            .ReadOnly = True, .WordWrap = False,
            .ScrollBars = ScrollBars.Both,
            .Text = reviewedReport,
            .Font = New Font("Consolas", 9.1F),
            .Margin = New Padding(0, 2, 0, 7)
        }
        root.Controls.Add(reportBox, 0, 5)

        Dim footer As New TableLayoutPanel With {
            .Name = "DirectGitHubFooter", .Dock = DockStyle.Fill,
            .ColumnCount = 1, .RowCount = 3,
            .Margin = Padding.Empty
        }
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        footer.RowStyles.Add(New RowStyle(SizeType.Absolute, 28))
        footer.RowStyles.Add(New RowStyle(SizeType.Absolute, 28))
        footer.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.Controls.Add(footer, 0, 6)

        reviewBox = New CheckBox With {
            .Name = "chkDirectReviewed",
            .Text = "I reviewed this exact report; publish it as a public Issue in DISM-GUI.",
            .Dock = DockStyle.Fill, .AutoSize = False,
            .Checked = False, .Margin = Padding.Empty
        }
        footer.Controls.Add(reviewBox, 0, 0)
        resultLabel = New Label With {
            .Name = "lblDirectResult",
            .Text = "No report was sent. GitHub authentication is required.",
            .Dock = DockStyle.Fill, .AutoEllipsis = True,
            .TextAlign = ContentAlignment.MiddleLeft, .ForeColor = ModernTheme.Muted
        }
        footer.Controls.Add(resultLabel, 0, 1)

        Dim actions As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1,
            .Margin = Padding.Empty
        }
        actions.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        actions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        actions.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 178))
        actions.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 214))
        actions.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 112))
        footer.Controls.Add(actions, 0, 2)

        disconnectButton = New Button With {
            .Name = "btnDirectDisconnect", .Text = "Remove saved login",
            .Dock = DockStyle.Fill, .Margin = New Padding(2, 3, 7, 0)
        }
        ModernTheme.StyleButton(disconnectButton, "browse")
        AddHandler disconnectButton.Click, AddressOf DisconnectClicked
        actions.Controls.Add(disconnectButton, 1, 0)

        sendButton = New Button With {
            .Name = "btnDirectSubmitIssue", .Text = "Send report directly",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(2, 3, 7, 0)
        }
        ModernTheme.StyleButton(sendButton, "primary")
        AddHandler sendButton.Click, AddressOf SendClicked
        actions.Controls.Add(sendButton, 2, 0)

        cancelButton = New Button With {
            .Name = "btnDirectClose", .Text = "Close",
            .Dock = DockStyle.Fill, .Margin = New Padding(2, 3, 0, 0)
        }
        ModernTheme.StyleButton(cancelButton, "neutral")
        AddHandler cancelButton.Click, Sub(sender, e)
            If session IsNot Nothing Then
                session.Cancel()
            Else
                Me.Close()
            End If
        End Sub
        actions.Controls.Add(cancelButton, 3, 0)
        Me.CancelButton = cancelButton

        AddHandler reviewBox.CheckedChanged, Sub(sender, e) RefreshSendAvailability()
        AddHandler Me.FormClosing, Sub(sender, e)
            If session IsNot Nothing Then session.Cancel()
        End Sub
        WindowGeometry.Install(Me, "Software\DISMGUI\DirectGitHubReport")
        RefreshSendAvailability()
    End Sub

    Private Sub RefreshSendAvailability()
        sendButton.Enabled =
            session Is Nothing AndAlso Not submitted AndAlso
            reviewBox.Checked AndAlso connectedLogin.Length > 0
    End Sub

    Private Sub SetBusy(active As Boolean)
        connectButton.Enabled = Not active
        disconnectButton.Enabled = Not active
        clientIdBox.Enabled = Not active
        sendButton.Enabled = False
        cancelButton.Text = If(active, "Cancel", "Close")
        If Not active Then RefreshSendAvailability()
    End Sub

    Private Async Sub ConnectClicked(sender As Object, e As EventArgs)
        If session IsNot Nothing Then Return
        Dim publicId = clientIdBox.Text.Trim()
        If Not DirectGitHubAccount.ValidClientId(publicId) Then
            resultLabel.Text = "Enter the PUBLIC GitHub App Client ID configured by the repository owner."
            Return
        End If
        session = New CancellationTokenSource()
        SetBusy(True)
        Dim token = session.Token
        Try
            DirectGitHubAccount.SaveClientId(publicId)
            authStatus.Text = "Requesting a GitHub device code..."
            Dim codes = Await client.StartDeviceFlowAsync(publicId, token)
            If Me.IsDisposed OrElse token.IsCancellationRequested Then Return
            codeBox.Text = codes.UserCode
            copyCodeButton.Enabled = True
            browserButton.Enabled = True
            authStatus.Text = "Go to github.com/login/device, enter code " &
                              codes.UserCode & " and approve DISM GUI."
            resultLabel.Text = "Awaiting GitHub approval; cancel to stop. No report has been sent."
            Try
                Process.Start(New ProcessStartInfo(
                    "https://github.com/login/device") With {.UseShellExecute = True})
            Catch
                ' Copy code and Open GitHub login remain available.
            End Try
            Dim accessToken = Await client.FinishDeviceFlowAsync(publicId, codes, token)
            Dim login = Await client.WhoAmIAsync(accessToken, token)
            If Me.IsDisposed OrElse token.IsCancellationRequested Then Return
            DirectGitHubAccount.StoreToken(accessToken)
            connectedLogin = login
            authStatus.Text = "Connected to GitHub as " & login &
                              ". Token saved in Windows Credential Manager."
            codeBox.Clear()
            copyCodeButton.Enabled = False
            browserButton.Enabled = False
            resultLabel.Text = "Connected. Review the report and opt in before sending."
        Catch ex As OperationCanceledException
            If Not Me.IsDisposed Then resultLabel.Text = "GitHub authorization canceled."
        Catch ex As Exception
            If Not Me.IsDisposed Then
                resultLabel.Text = "Connection failed: " &
                    ErrorDiagnostics.Redact(ex.Message)
            End If
        Finally
            If session IsNot Nothing Then
                session.Dispose()
                session = Nothing
            End If
            If Not Me.IsDisposed Then SetBusy(False)
        End Try
    End Sub

    Private Sub DisconnectClicked(sender As Object, e As EventArgs)
        If session IsNot Nothing Then Return
        Try
            DirectGitHubAccount.Disconnect()
            connectedLogin = ""
            authStatus.Text = "Saved GitHub authorization removed from this Windows profile."
            codeBox.Clear()
            browserButton.Enabled = False
            copyCodeButton.Enabled = False
            resultLabel.Text = "No report was sent."
            RefreshSendAvailability()
        Catch ex As Exception
            resultLabel.Text = "Disconnect failed: " &
                ErrorDiagnostics.Redact(ex.GetType().Name)
        End Try
    End Sub

    Private Async Sub SendClicked(sender As Object, e As EventArgs)
        If session IsNot Nothing OrElse submitted OrElse Not reviewBox.Checked OrElse
           connectedLogin.Length = 0 Then Return

        If DirectGitHubDedup.AlreadySubmitted(issueTitle, reviewedReport) Then
            resultLabel.Text = "This report was already sent recently. Duplicate blocked."
            Return
        End If
        If MessageBox.Show(Me,
                "Publish this sanitized report as a PUBLIC Issue in " &
                DirectGitHubAccount.TargetRepository & "?" & Environment.NewLine &
                "You cannot retract information already published.",
                "Confirm GitHub Issue publication",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        session = New CancellationTokenSource()
        SetBusy(True)
        Try
            Dim storedToken = DirectGitHubAccount.ReadToken()
            If storedToken.Length = 0 Then
                Throw New SecurityException("GitHub login is missing. Connect again.")
            End If
            resultLabel.Text = "Sending sanitized report directly to GitHub..."
            Dim answer = Await client.CreateIssueAsync(
                storedToken, issueTitle, reviewedReport, session.Token)
            DirectGitHubDedup.Remember(issueTitle, reviewedReport,
                                                  answer.Url)
            submitted = True
            resultLabel.Text = "Published GitHub Issue #" & answer.Number.ToString() &
                               ". Report sent successfully."
            authStatus.Text = "Issue #" & answer.Number.ToString() &
                              " published in " & DirectGitHubAccount.TargetRepository
            If MessageBox.Show(Me,
                    "Open GitHub Issue #" & answer.Number.ToString() & " in the browser?",
                    "Report submitted", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) = DialogResult.Yes Then
                Process.Start(New ProcessStartInfo(answer.Url) With {
                    .UseShellExecute = True
                })
            End If
        Catch ex As OperationCanceledException
            resultLabel.Text = "Request canceled; check GitHub Issues before retrying."
        Catch ex As Exception
            resultLabel.Text = "Direct send failed: " & ErrorDiagnostics.Redact(ex.Message)
        Finally
            If session IsNot Nothing Then
                session.Dispose()
                session = Nothing
            End If
            If Not Me.IsDisposed Then SetBusy(False)
        End Try
    End Sub

    Public Sub PreviewConnectedForTests()
        ' UI smoke only: does not authenticate or call the API.
        connectedLogin = "test-account"
        RefreshSendAvailability()
    End Sub
End Class
