Imports System
Imports System.Drawing
Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Guided, non-destructive invalid mount investigation. NO automation to
' delete registry keys, stop services, remount, clean, commit or discard.
Public NotInheritable Class MountRecoveryWizardForm
    Inherits Form

    Private ReadOnly source As TextBox
    Private ReadOnly indexBox As NumericUpDown
    Private ReadOnly report As TextBox
    Private ReadOnly status As Label
    Private ReadOnly inspectButton As Button
    Private ReadOnly locksButton As Button
    Private ReadOnly checkpointButton As Button
    Private ReadOnly recheckButton As Button
    Private busy As Boolean
    Private lastAssessment As MountRecoverySession.Assessment

    Public Sub New(Optional wimFile As String = "")
        Me.Name = "MountRecoveryWizardForm"
        Me.Text = "DISM GUI - WIM Mount Recovery"
        Me.Size = New Size(1050, 725)
        Me.MinimumSize = New Size(840, 570)
        Me.BackColor = Color.White
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ShowInTaskbar = False
        Me.Font = ModernTheme.UiFont(9.5F)
        Me.AutoScaleMode = AutoScaleMode.Font

        Dim previous As MountRecoverySession.Checkpoint = Nothing
        Try
            previous = MountRecoverySession.LoadCheckpoint()
        Catch
            ' A corrupt checkpoint is never used as authority for any action.
        End Try
        Dim selectedWim = If(wimFile, "").Trim()
        If selectedWim.Length = 0 AndAlso previous IsNot Nothing Then
            selectedWim = previous.ImagePath
        End If

        Dim root As New TableLayoutPanel With {
            .Name = "MountRecoveryLayout",
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 5,
            .Padding = New Padding(14, 10, 14, 10)
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 82))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 58))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 56))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 58))
        Controls.Add(root)

        Dim heading As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 2, .ColumnCount = 1
        }
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 60))
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 40))
        heading.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        heading.Controls.Add(New Label With {
            .Text = "WIM MOUNT RECOVERY ASSISTANT",
            .Font = ModernTheme.UiFont(13.0F, True),
            .Dock = DockStyle.Fill, .ForeColor = ModernTheme.TextInk,
            .TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0)
        heading.Controls.Add(New Label With {
            .Text = "Inspect → close file handles → restart Windows manually → verify. No destructive actions.",
            .Dock = DockStyle.Fill, .ForeColor = ModernTheme.Muted,
            .TextAlign = ContentAlignment.MiddleLeft, .AutoEllipsis = True
        }, 0, 1)
        root.Controls.Add(heading, 0, 0)

        Dim inputs As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1
        }
        For Each portion As Single In {13.0F, 57.0F, 12.0F, 18.0F}
            inputs.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, portion))
        Next
        inputs.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        inputs.Controls.Add(New Label With {
            .Text = "Source WIM", .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0)
        source = New TextBox With {
            .Name = "txtRecoveryWim",
            .Text = selectedWim, .Dock = DockStyle.Fill,
            .Margin = New Padding(0, 12, 8, 8),
            .PlaceholderText = "Choose WIM/ESD to inspect"
        }
        inputs.Controls.Add(source, 1, 0)
        inputs.Controls.Add(New Label With {
            .Text = "Index", .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleRight,
            .Margin = New Padding(4, 0, 8, 0)
        }, 2, 0)
        indexBox = New NumericUpDown With {
            .Name = "numRecoveryIndex", .Dock = DockStyle.Fill,
            .Minimum = 1, .Maximum = 255,
            .Value = If(previous IsNot Nothing, CDec(Math.Min(255, previous.Index)), 1D),
            .Margin = New Padding(0, 12, 0, 8)
        }
        inputs.Controls.Add(indexBox, 3, 0)
        root.Controls.Add(inputs, 0, 1)

        report = New TextBox With {
            .Name = "txtRecoveryReport", .Dock = DockStyle.Fill,
            .Multiline = True, .ReadOnly = True, .WordWrap = True,
            .ScrollBars = ScrollBars.Vertical,
            .Font = New Font("Consolas", 9.25F),
            .BackColor = Color.FromArgb(249, 251, 254),
            .Text = "This assistant does not perform Remount, Cleanup-Mountpoints, Commit, Discard or registry repair." &
                Environment.NewLine &
                "Choose a source and Inspect. Copy the report before any manual intervention."
        }
        root.Controls.Add(report, 0, 2)

        status = New Label With {
            .Name = "lblRecoveryStatus", .Dock = DockStyle.Fill,
            .ForeColor = ModernTheme.Muted,
            .Text = "Checkpoint storage is local; verifying WIMGAPI is required after restart.",
            .TextAlign = ContentAlignment.MiddleLeft
        }
        root.Controls.Add(status, 0, 3)

        Dim actions As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 6, .RowCount = 1
        }
        For Each width As Single In {17.0F, 17.0F, 20.0F, 20.0F, 14.0F, 12.0F}
            actions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, width))
        Next
        actions.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        inspectButton = New Button With {
            .Name = "btnRecoveryInspect", .Text = "Inspect status",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 5, 7, 5)
        }
        locksButton = New Button With {
            .Name = "btnRecoveryLocks", .Text = "Find file locks",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 5, 7, 5)
        }
        checkpointButton = New Button With {
            .Name = "btnRecoveryCheckpoint", .Text = "Record restart check",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 5, 7, 5),
            .Enabled = False
        }
        recheckButton = New Button With {
            .Name = "btnRecoveryRecheck", .Text = "Verify after restart",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 5, 7, 5)
        }
        Dim copyButton As New Button With {
            .Name = "btnRecoveryCopy", .Text = "Copy report",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 5, 7, 5)
        }
        Dim closeButton As New Button With {
            .Name = "btnRecoveryClose", .Text = "Close",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 5, 0, 5)
        }
        For Each button In {inspectButton, locksButton, checkpointButton, recheckButton, copyButton, closeButton}
            ModernTheme.StyleButton(button, If(button Is inspectButton, "primary", "secondary"))
            actions.Controls.Add(button, actions.Controls.Count, 0)
        Next
        root.Controls.Add(actions, 0, 4)

        AddHandler inspectButton.Click, AddressOf InspectClicked
        AddHandler locksButton.Click, AddressOf ScanClicked
        AddHandler checkpointButton.Click, AddressOf CheckpointClicked
        AddHandler recheckButton.Click, AddressOf RecheckClicked
        AddHandler copyButton.Click, Sub(sender, e)
            Try
                Clipboard.SetText(report.Text)
                status.Text = "Report copied. Review paths before sharing."
            Catch ex As Exception
                status.Text = "Clipboard unavailable: " & ex.Message
            End Try
        End Sub
        AddHandler closeButton.Click, Sub(sender, e) Close()
        ModernTheme.MakeDialogScrollable(Me, root, 800, 640)
        WindowGeometry.Install(Me, "Software\DISMGUI\MountRecoveryWizard")
        AddHandler Me.Shown, Sub(sender, e)
            If source.Text.Length > 0 Then InspectClicked(sender, e)
        End Sub
    End Sub

    Private Sub SetBusy(value As Boolean)
        busy = value
        inspectButton.Enabled = Not value
        locksButton.Enabled = Not value
        checkpointButton.Enabled = Not value AndAlso lastAssessment IsNot Nothing AndAlso
            lastAssessment.RegistrationChecked AndAlso
            String.Equals(lastAssessment.Status, "Invalid / check mount", StringComparison.Ordinal)
        recheckButton.Enabled = Not value
    End Sub

    Private Async Sub InspectClicked(sender As Object, e As EventArgs)
        If busy Then Return
        SetBusy(True)
        status.Text = "Reading WIMGAPI registration and local journal..."
        Dim chosen = source.Text.Trim()
        Dim imageIndex = CInt(indexBox.Value)
        Try
            Dim assessment = Await Task.Run(Function()
                Return MountRecoverySession.Inspect(chosen, imageIndex)
            End Function)
            If IsDisposed Then Return
            lastAssessment = assessment
            report.Text = "Index " & assessment.Index.ToString() & " | " &
                assessment.Status & Environment.NewLine &
                "Mounted folder: " & assessment.MountFolder & Environment.NewLine &
                If(assessment.Warning.Length > 0,
                   "Warning: " & assessment.Warning & Environment.NewLine, "") &
                assessment.Explanation & Environment.NewLine & Environment.NewLine &
                WimMountSafety.BuildHealthReport(chosen)
            status.Text = If(assessment.RegistrationChecked,
                "WIMGAPI observed; a damaged or active mount requires manual review.",
                "Unable to verify mount registration; no cleanup action is authorized.")
        Catch ex As Exception
            status.Text = "Cannot inspect WIM mount: " & ex.Message
        Finally
            If Not IsDisposed Then SetBusy(False)
        End Try
    End Sub

    Private Async Sub ScanClicked(sender As Object, e As EventArgs)
        If busy Then Return
        If lastAssessment Is Nothing OrElse
           String.IsNullOrWhiteSpace(lastAssessment.MountFolder) Then
            status.Text = "Inspect and select a registered mount folder first."
            Return
        End If
        SetBusy(True)
        Try
            Dim folder = lastAssessment.MountFolder
            Dim image = lastAssessment.ImageFile
            status.Text = "Querying sampled file handles (not directory handles)..."
            Dim result = Await Task.Run(Function()
                Return WimMountLocks.FormatResult(WimMountLocks.Scan(folder, image))
            End Function)
            If Not IsDisposed Then
                report.Text &= Environment.NewLine & Environment.NewLine & result
                status.Text = "Scan is partial; no processes were closed or restarted."
            End If
        Catch ex As Exception
            If Not IsDisposed Then status.Text = "File-lock scan unavailable: " & ex.Message
        Finally
            If Not IsDisposed Then SetBusy(False)
        End Try
    End Sub

    Private Sub CheckpointClicked(sender As Object, e As EventArgs)
        If busy Then Return
        Try
            Dim state = MountRecoverySession.BeginRestartReview(lastAssessment)
            status.Text = "Saved local checkpoint at " & state.StartedUtc.ToString("u") &
                ". Close applications and restart Windows manually."
            report.Text &= Environment.NewLine & Environment.NewLine &
                "RESTART CHECKPOINT RECORDED. No reboot has been initiated." &
                Environment.NewLine &
                "After restarting Windows, open this dialog and choose Verify after restart."
        Catch ex As Exception
            status.Text = "Checkpoint could not be recorded: " & ex.Message
        End Try
    End Sub

    Private Async Sub RecheckClicked(sender As Object, e As EventArgs)
        If busy Then Return
        SetBusy(True)
        status.Text = "Verifying the saved restart checkpoint and current mount status..."
        Try
            Dim src = source.Text.Trim()
            Dim index = CInt(indexBox.Value)
            Dim outcome = Await Task.Run(Function()
                Return MountRecoverySession.ReviewAfterRestart(src, index)
            End Function)
            If Not IsDisposed Then
                report.Text &= Environment.NewLine & Environment.NewLine & outcome
                status.Text = "Post-restart review recorded. No automatic cleanup performed."
            End If
        Catch ex As Exception
            If Not IsDisposed Then status.Text = "Review failed: " & ex.Message
        Finally
            If Not IsDisposed Then SetBusy(False)
        End Try
    End Sub
End Class
