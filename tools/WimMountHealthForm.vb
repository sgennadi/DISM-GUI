Imports System
Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Local, read-only mount state and incomplete-operation viewer.
' No repair/cleanup/commit/discard commands are offered by this dialog.
Public NotInheritable Class WimMountHealthForm
    Inherits Form

    Private ReadOnly sourceBox As TextBox
    Private ReadOnly reportBox As TextBox
    Private ReadOnly statusLabel As Label
    Private ReadOnly refreshButton As Button
    Private ReadOnly findLocksButton As Button
    Private ReadOnly recoveryButton As Button
    Private ReadOnly copyButton As Button
    Private ReadOnly closeButton As Button
    Private checking As Boolean

    Public Sub New(Optional sourcePath As String = "")
        Me.Name = "WimMountHealthForm"
        Me.Text = "DISM GUI - WIM 360 - Mount health"
        Me.MinimumSize = New Size(760, 480)
        Me.Size = New Size(1060, 700)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ShowInTaskbar = False
        Me.BackColor = Color.White
        Me.Font = ModernTheme.UiFont(9.5F)
        Me.AutoScaleMode = AutoScaleMode.Font
        Dim root As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .Padding = New Padding(14, 10, 14, 10),
            .ColumnCount = 1, .RowCount = 5, .BackColor = Color.White
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        ' Fixed height clipped the subtitle at simulated 175% DPI.
        ' Measure both font lines and reserve spacing between them.
        Dim headingFont = ModernTheme.UiFont(12.3F, True)
        Dim subtitleFont = ModernTheme.UiFont(9.1F)
        Dim headingHeight = Math.Max(78,
            headingFont.Height + subtitleFont.Height + 26)
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, headingHeight))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 47))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 53))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 57))
        Controls.Add(root)

        Dim header As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2
        }
        header.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        header.RowStyles.Add(New RowStyle(SizeType.Percent, 55))
        header.RowStyles.Add(New RowStyle(SizeType.Percent, 45))
        Dim title = New Label With {
            .Text = "WIM 360  |  MOUNT HEALTH",
            .Font = headingFont,
            .ForeColor = ModernTheme.TextInk,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Dock = DockStyle.Fill
        }
        Dim subheading = New Label With {
            .Name = "lblMountHealthSubtitle",
            .Text = "Registered mounts and recovery history - read-only",
            .Font = subtitleFont,
            .ForeColor = ModernTheme.Muted,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Dock = DockStyle.Fill
        }
        header.Controls.Add(title, 0, 0)
        header.Controls.Add(subheading, 0, 1)
        root.Controls.Add(header, 0, 0)

        sourceBox = New TextBox With {
            .Name = "txtMountHealthSource",
            .Dock = DockStyle.Fill,
            .Text = If(sourcePath, "").Trim(),
            .PlaceholderText = "Optional WIM/ESD path - leave empty for journal only",
            .Margin = New Padding(0, 10, 0, 7)
        }
        root.Controls.Add(sourceBox, 0, 1)

        reportBox = New TextBox With {
            .Name = "txtMountHealthReport",
            .Dock = DockStyle.Fill,
            .ReadOnly = True,
            .Multiline = True,
            .WordWrap = False,
            .ScrollBars = ScrollBars.Both,
            .Font = New Font("Consolas", 9.3F),
            .BackColor = Color.FromArgb(249, 251, 254),
            .ForeColor = ModernTheme.TextInk,
            .BorderStyle = BorderStyle.FixedSingle,
            .Text = "Select Refresh to check the current local mount registration."
        }
        root.Controls.Add(reportBox, 0, 2)

        statusLabel = New Label With {
            .Name = "lblMountHealthStatus",
            .Dock = DockStyle.Fill,
            .ForeColor = ModernTheme.Muted,
            .Text = "This window never runs DISM repair, Commit or Discard.",
            .TextAlign = ContentAlignment.MiddleLeft,
            .AutoEllipsis = False
        }
        root.Controls.Add(statusLabel, 0, 3)

        Dim buttons As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 5, .RowCount = 1
        }
        For Each col As Single In {20.0F, 21.0F, 21.0F, 21.0F, 17.0F}
            buttons.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, col))
        Next
        buttons.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        refreshButton = New Button With {
            .Name = "btnMountHealthRefresh", .Text = "Refresh mount health",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 10, 4)
        }
        ModernTheme.StyleButton(refreshButton, "primary")
        findLocksButton = New Button With {
            .Name = "btnMountHealthFindLocks", .Text = "Find file locks",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 10, 4)
        }
        ModernTheme.StyleButton(findLocksButton, "secondary")
        recoveryButton = New Button With {
            .Name = "btnMountHealthRecovery", .Text = "Recovery assistant",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 8, 4)
        }
        ModernTheme.StyleButton(recoveryButton, "secondary")
        copyButton = New Button With {
            .Name = "btnMountHealthCopy", .Text = "Copy report",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 10, 4),
            .Enabled = False
        }
        ModernTheme.StyleButton(copyButton, "secondary")
        closeButton = New Button With {
            .Name = "btnMountHealthClose", .Text = "Close",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 0, 4)
        }
        ModernTheme.StyleButton(closeButton, "neutral")
        buttons.Controls.Add(refreshButton, 0, 0)
        buttons.Controls.Add(findLocksButton, 1, 0)
        buttons.Controls.Add(recoveryButton, 2, 0)
        buttons.Controls.Add(copyButton, 3, 0)
        buttons.Controls.Add(closeButton, 4, 0)
        root.Controls.Add(buttons, 0, 4)

        AddHandler refreshButton.Click, AddressOf RefreshClicked
        AddHandler findLocksButton.Click, AddressOf FindLocksClicked
        AddHandler recoveryButton.Click, Sub(sender, e)
            If checking Then Return
            Using wizard As New MountRecoveryWizardForm(sourceBox.Text.Trim())
                wizard.ShowDialog(Me)
            End Using
        End Sub
        AddHandler copyButton.Click, Sub(sender, e)
            Try
                Clipboard.SetText(reportBox.Text)
                statusLabel.Text = "Local report copied. No image operations were performed."
            Catch ex As Exception
                statusLabel.Text = "Copy unavailable: " & ex.Message
            End Try
        End Sub
        AddHandler closeButton.Click, Sub(sender, e) Close()
        WindowGeometry.Install(Me, "Software\DISMGUI\WimMountHealth")
        AddHandler Me.Shown, Sub(sender, e) RefreshClicked(sender, e)
    End Sub

    ' Read-only best-effort Restart Manager query against actual WIMGAPI mount
    ' registration; no arbitrary folders, force-unlocks or process termination.
    Private Async Sub FindLocksClicked(sender As Object, e As EventArgs)
        If checking Then Return
        Dim source = sourceBox.Text.Trim()
        If String.IsNullOrWhiteSpace(source) Then
            statusLabel.Text = "Choose the WIM/ESD source path before scanning file locks."
            Return
        End If
        checking = True
        refreshButton.Enabled = False
        findLocksButton.Enabled = False
        copyButton.Enabled = False
        statusLabel.Text = "Sampling mounted files with Windows Restart Manager (read-only)..."
        Try
            Dim result = Await Task.Run(Function()
                Dim inspected = Wim360Reader.Inspect(source)
                If Not String.IsNullOrWhiteSpace(inspected.MountScanWarning) Then
                    Throw New InvalidOperationException(inspected.MountScanWarning)
                End If
                Dim mounted = inspected.Images.FirstOrDefault(Function(x)
                    Return Not String.IsNullOrWhiteSpace(x.MountFolder)
                End Function)
                If mounted Is Nothing Then
                    Return "No registered mount folder was found for the selected image." &
                        Environment.NewLine &
                        "The absence of registration is not permission to delete a folder."
                End If
                Return "Index " & mounted.Index.ToString() & " | " &
                    mounted.MountStatus & Environment.NewLine &
                    WimMountLocks.FormatResult(WimMountLocks.Scan(
                        mounted.MountFolder, inspected.ImageFile))
            End Function)
            If IsDisposed Then Return
            reportBox.Text &= Environment.NewLine & Environment.NewLine & result
            statusLabel.Text = "Best-effort file-lock scan. Missing handles are possible."
            copyButton.Enabled = True
        Catch ex As Exception
            RuntimeDiagnostics.RecordException("WIM mount lock inspection", ex)
            If Not IsDisposed Then
                statusLabel.Text = "File-lock scan unavailable: " & ex.Message
            End If
        Finally
            checking = False
            If Not IsDisposed Then
                refreshButton.Enabled = True
                findLocksButton.Enabled = True
            End If
        End Try
    End Sub

    Private Async Sub RefreshClicked(sender As Object, e As EventArgs)
        If checking Then Return
        checking = True
        refreshButton.Enabled = False
        findLocksButton.Enabled = False
        copyButton.Enabled = False
        statusLabel.Text = "Refreshing image mount status and local journal (read-only)..."
        Dim source = sourceBox.Text.Trim()
        Try
            Dim result = Await Task.Run(Function() WimMountSafety.BuildHealthReport(source))
            If IsDisposed Then Return
            reportBox.Text = result
            reportBox.SelectionStart = 0
            reportBox.SelectionLength = 0
            statusLabel.Text = "Report refreshed. Interrupted Commit requires manual review."
            copyButton.Enabled = True
        Catch ex As Exception
            RuntimeDiagnostics.RecordException("WIM Mount health report", ex)
            If Not IsDisposed Then
                statusLabel.Text = "Mount health unavailable: " & ex.Message
            End If
        Finally
            checking = False
            If Not IsDisposed Then
                refreshButton.Enabled = True
                findLocksButton.Enabled = True
            End If
        End Try
    End Sub
End Class
