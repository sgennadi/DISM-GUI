Imports System
Imports System.Diagnostics
Imports System.Drawing
Imports System.Windows.Forms

' User-controlled support workflow. No automatic upload or GitHub API token.
Public NotInheritable Class ErrorReportForm
    Inherits Form

    Private ReadOnly rawOutput As String
    Private ReadOnly area As String
    Private ReadOnly titleBox As TextBox
    Private ReadOnly descriptionBox As TextBox
    Private ReadOnly includeLog As CheckBox
    Private ReadOnly includeHistory As CheckBox
    Private ReadOnly previewBox As TextBox
    Private ReadOnly reviewed As CheckBox
    Private ReadOnly sendButton As Button
    Private ReadOnly status As Label

    Public Sub New(Optional dismOutput As String = "",
                   Optional sourceArea As String = "DISM GUI")
        rawOutput = If(dismOutput, "")
        area = If(sourceArea, "DISM GUI")
        Me.Name = "ErrorReportForm"
        Me.Text = "DISM GUI - Diagnostics and GitHub Support"
        Me.StartPosition = FormStartPosition.CenterParent
        Me.Size = New Size(1050, 780)
        Me.MinimumSize = New Size(810, 620)
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.Font = ModernTheme.UiFont(9.4F)
        Me.BackColor = Color.White
        Me.ShowInTaskbar = False
        Try
            Me.Icon = ModernTheme.MakeAppIcon()
        Catch
        End Try

        Dim layout As New TableLayoutPanel With {
            .Name = "SupportReportLayout",
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 6,
            .Padding = New Padding(17, 13, 17, 14),
            .BackColor = Color.White
        }
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each h As Integer In {52, 55, 112, 47}
            layout.RowStyles.Add(New RowStyle(SizeType.Absolute, h))
        Next
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 97))
        Me.Controls.Add(layout)

        Dim heading As New Label With {
            .Name = "lblSupportHeading",
            .Text = "ERROR DIAGNOSTICS  |  GITHUB SUPPORT",
            .Dock = DockStyle.Fill, .Font = ModernTheme.UiFont(13.5F, True),
            .TextAlign = ContentAlignment.MiddleLeft,
            .ForeColor = ModernTheme.TextInk, .AutoEllipsis = True
        }
        layout.Controls.Add(heading, 0, 0)

        titleBox = New TextBox With {
            .Name = "txtSupportTitle", .Dock = DockStyle.Fill,
            .Margin = New Padding(0, 5, 0, 14),
            .PlaceholderText = "Short error title (without passwords or host names)",
            .Text = If(ErrorDiagnostics.LastFailure.Length > 0,
                       "DISM GUI operation failed", "Problem with DISM GUI")
        }
        layout.Controls.Add(titleBox, 0, 1)

        descriptionBox = New TextBox With {
            .Name = "txtSupportDescription", .Dock = DockStyle.Fill,
            .Multiline = True, .ScrollBars = ScrollBars.Vertical,
            .Margin = New Padding(0, 3, 0, 10),
            .PlaceholderText = "Steps to reproduce, expected result and actual result..."
        }
        layout.Controls.Add(descriptionBox, 0, 2)

        Dim choices As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2,
            .RowCount = 1, .Margin = Padding.Empty
        }
        choices.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 44))
        choices.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 32))
        choices.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 24))
        choices.ColumnCount = 3
        choices.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.Controls.Add(choices, 0, 3)

        includeLog = New CheckBox With {
            .Name = "chkSupportIncludeLog",
            .Text = "Include sanitized last 60 lines of DISM output",
            .Dock = DockStyle.Fill, .Checked = False,
            .AutoSize = False, .Margin = New Padding(0, 5, 5, 7)
        }
        choices.Controls.Add(includeLog, 0, 0)
        includeHistory = New CheckBox With {
            .Name = "chkSupportIncludeHistory",
            .Text = "Include last 15 local errors",
            .Checked = False, .Dock = DockStyle.Fill,
            .AutoSize = False, .Margin = New Padding(2, 5, 5, 7)
        }
        choices.Controls.Add(includeHistory, 1, 0)
        Dim refresh As New Button With {
            .Name = "btnSupportGenerate",
            .Text = "Generate sanitized preview",
            .Dock = DockStyle.Fill, .Margin = New Padding(8, 3, 0, 5)
        }
        ModernTheme.StyleButton(refresh, "browse")
        AddHandler refresh.Click, Sub(sender, e) GeneratePreview()
        choices.Controls.Add(refresh, 2, 0)

        previewBox = New TextBox With {
            .Name = "txtSupportPreview",
            .Dock = DockStyle.Fill, .Multiline = True,
            .ScrollBars = ScrollBars.Both, .WordWrap = False,
            .Font = New Font("Consolas", 9.2F),
            .Margin = New Padding(0, 3, 0, 6)
        }
        layout.Controls.Add(previewBox, 0, 4)

        Dim footer As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3,
            .Margin = Padding.Empty
        }
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        footer.RowStyles.Add(New RowStyle(SizeType.Absolute, 27))
        footer.RowStyles.Add(New RowStyle(SizeType.Absolute, 27))
        footer.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.Controls.Add(footer, 0, 5)

        status = New Label With {
            .Name = "lblSupportWarning",
            .Text = "Local preview only. Review for sensitive information before sharing.",
            .Dock = DockStyle.Fill, .AutoEllipsis = True,
            .ForeColor = ModernTheme.Muted, .TextAlign = ContentAlignment.MiddleLeft
        }
        footer.Controls.Add(status, 0, 0)

        reviewed = New CheckBox With {
            .Name = "chkSupportReviewed",
            .Text = "I reviewed the full report and agree to open a GitHub Issue draft.",
            .Dock = DockStyle.Fill, .AutoSize = False, .Checked = False,
            .Margin = Padding.Empty
        }
        footer.Controls.Add(reviewed, 0, 1)

        Dim actions As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 6, .RowCount = 1,
            .Margin = Padding.Empty
        }
        actions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each width As Integer In {135, 125, 135, 215, 95}
            actions.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, width))
        Next
        actions.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        footer.Controls.Add(actions, 0, 2)

        Dim openHistory As New Button With {
            .Name = "btnSupportOpenHistory",
            .Text = "Open log folder",
            .Dock = DockStyle.Fill, .Margin = New Padding(3, 4, 6, 0)
        }
        ModernTheme.StyleButton(openHistory, "browse")
        AddHandler openHistory.Click, Sub(sender, e)
            Try
                System.IO.Directory.CreateDirectory(RuntimeDiagnostics.Folder)
                Process.Start(New ProcessStartInfo(RuntimeDiagnostics.Folder) With {
                    .UseShellExecute = True
                })
            Catch ex As Exception
                status.Text = "Cannot open local diagnostic folder: " & ex.GetType().Name
            End Try
        End Sub
        actions.Controls.Add(openHistory, 1, 0)

        Dim clearHistory As New Button With {
            .Name = "btnSupportClearHistory",
            .Text = "Clear history",
            .Dock = DockStyle.Fill, .Margin = New Padding(3, 4, 6, 0)
        }
        ModernTheme.StyleButton(clearHistory, "neutral")
        AddHandler clearHistory.Click, Sub(sender, e)
            If MessageBox.Show(Me, "Delete all locally recorded diagnostic errors?",
                "Clear local diagnostics", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) <> DialogResult.Yes Then Return
            Try
                RuntimeDiagnostics.ClearHistory()
                GeneratePreview()
                status.Text = "Local diagnostic history cleared."
            Catch ex As IOException
                status.Text = ex.Message
            End Try
        End Sub
        actions.Controls.Add(clearHistory, 2, 0)

        Dim copy As New Button With {
            .Name = "btnSupportCopy", .Text = "Copy report",
            .Dock = DockStyle.Fill, .Margin = New Padding(3, 4, 6, 0)
        }
        ModernTheme.StyleButton(copy, "browse")
        AddHandler copy.Click, Sub(sender, e) CopyReport()
        actions.Controls.Add(copy, 3, 0)

        sendButton = New Button With {
            .Name = "btnSupportGitHub", .Text = "Open GitHub Issue draft",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(3, 4, 6, 0)
        }
        ModernTheme.StyleButton(sendButton, "primary")
        AddHandler sendButton.Click, AddressOf OpenIssueClicked
        actions.Controls.Add(sendButton, 4, 0)

        Dim close As New Button With {
            .Name = "btnSupportClose", .Text = "Close",
            .Dock = DockStyle.Fill, .Margin = New Padding(3, 4, 0, 0)
        }
        ModernTheme.StyleButton(close, "neutral")
        AddHandler close.Click, Sub(sender, e) Me.Close()
        actions.Controls.Add(close, 5, 0)
        Me.CancelButton = close

        AddHandler reviewed.CheckedChanged, Sub(sender, e)
            sendButton.Enabled = reviewed.Checked AndAlso previewBox.TextLength > 0
        End Sub
        AddHandler previewBox.TextChanged, Sub(sender, e)
            reviewed.Checked = False
        End Sub
        AddHandler includeLog.CheckedChanged, Sub(sender, e)
            GeneratePreview()
        End Sub
        AddHandler includeHistory.CheckedChanged, Sub(sender, e)
            GeneratePreview()
        End Sub
        AddHandler titleBox.TextChanged, Sub(sender, e)
            reviewed.Checked = False
        End Sub
        AddHandler descriptionBox.TextChanged, Sub(sender, e)
            reviewed.Checked = False
        End Sub
        GeneratePreview()
        WindowGeometry.Install(Me, "Software\DISMGUI\ErrorReport")
    End Sub

    Private Sub GeneratePreview()
        Try
            previewBox.Text = ErrorDiagnostics.BuildReport(
                titleBox.Text, descriptionBox.Text, includeLog.Checked,
                If(includeLog.Checked, rawOutput, ""), area, Me.DeviceDpi,
                includeHistory.Checked)
            reviewed.Checked = False
            status.Text = "Report redacted locally. Check every line before opening GitHub."
        Catch ex As Exception
            ErrorDiagnostics.RememberFailure("Support preview", ex)
            status.Text = "Could not create safe report: " & ex.GetType().Name
            previewBox.Clear()
        End Try
    End Sub

    Private Sub CopyReport()
        Try
            Dim safeReport = ErrorDiagnostics.Redact(previewBox.Text)
            If String.IsNullOrWhiteSpace(safeReport) Then Return
            Clipboard.SetText(safeReport)
            status.Text = "Sanitized report copied. It has NOT been sent."
        Catch ex As Exception
            status.Text = "Clipboard unavailable: " & ex.GetType().Name
        End Try
    End Sub

    Private Sub OpenIssueClicked(sender As Object, e As EventArgs)
        If Not reviewed.Checked Then Return
        Try
            ' Re-sanitize even if the preview was manually edited.
            Dim sanitized = ErrorDiagnostics.Redact(previewBox.Text)
            Clipboard.SetText(sanitized)
            ErrorDiagnostics.OpenGitHubIssue(titleBox.Text, sanitized)
            status.Text = "GitHub draft opened. You must sign in and click Submit yourself."
        Catch ex As Exception
            status.Text = "GitHub browser/clipboard unavailable: " & ex.Message
        End Try
    End Sub
End Class
