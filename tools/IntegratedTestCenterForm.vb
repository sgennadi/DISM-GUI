Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms

' One place to exercise every safe DISM GUI diagnostic without WIM writes.
' Support issue submission remains user-reviewed and opt-in.
Public NotInheritable Class IntegratedTestCenterForm
    Inherits Form

    Private ReadOnly pathBox As TextBox
    Private ReadOnly includeAudit As CheckBox
    Private ReadOnly startButton As Button
    Private ReadOnly cancelAction As Button
    Private ReadOnly copyButton As Button
    Private ReadOnly saveButton As Button
    Private ReadOnly supportButton As Button
    Private ReadOnly statusLabel As Label
    Private ReadOnly grid As DataGridView
    Private current As New List(Of IntegratedTestChecks.Result)()
    Private running As CancellationTokenSource

    Public Sub New(Optional initialPath As String = "")
        Me.Name = "IntegratedTestCenterForm"
        Me.Text = "DISM GUI " &
            GetType(IntegratedTestCenterForm).Assembly.GetName().Version.ToString(3) &
            " - Integrated Test Center"
        Me.StartPosition = FormStartPosition.CenterParent
        Me.Size = New Size(1040, 730)
        Me.MinimumSize = New Size(820, 590)
        Me.Font = ModernTheme.UiFont(9.5F)
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.BackColor = Color.White
        Me.ShowInTaskbar = False
        Try
            Me.Icon = ModernTheme.MakeAppIcon()
        Catch
        End Try

        Dim root As New TableLayoutPanel With {
            .Name = "TestCenterLayout",
            .ColumnCount = 1, .RowCount = 5, .Dock = DockStyle.Fill,
            .BackColor = Color.White, .Padding = New Padding(16, 12, 16, 14)
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 75))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 66))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 50))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 100))
        Controls.Add(root)

        Dim heading As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .Margin = Padding.Empty
        }
        heading.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        heading.RowStyles.Add(New RowStyle(SizeType.Absolute, 34))
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        Dim headline As New Label With {
            .Name = "lblTestCenterTitle", .Dock = DockStyle.Fill,
            .Text = "INTEGRATED TEST CENTER  |  READ ONLY",
            .Font = ModernTheme.UiFont(13.1F, True),
            .ForeColor = ModernTheme.TextInk,
            .TextAlign = ContentAlignment.MiddleLeft,
            .AutoEllipsis = True
        }
        heading.Controls.Add(headline, 0, 0)
        heading.Controls.Add(New Label With {
            .Dock = DockStyle.Fill,
            .Text = "Environment  •  DISM  •  WIM/SWM  •  Mount audit  •  DPI  •  Diagnostics",
            .TextAlign = ContentAlignment.MiddleLeft, .AutoEllipsis = True,
            .ForeColor = ModernTheme.Muted
        }, 0, 1)
        root.Controls.Add(heading, 0, 0)

        Dim imageRow As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 2,
            .Margin = New Padding(0, 3, 0, 5)
        }
        imageRow.RowStyles.Add(New RowStyle(SizeType.Absolute, 23))
        imageRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        imageRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        imageRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 156))
        imageRow.Controls.Add(New Label With {
            .Name = "lblTestCenterImage", .Text = "Image to inspect (optional)",
            .Dock = DockStyle.Fill, .ForeColor = ModernTheme.TextInk,
            .TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0)
        pathBox = New TextBox With {
            .Name = "txtTestCenterImage", .Text = If(initialPath, ""),
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 2, 9, 4),
            .PlaceholderText = "Select install.wim, boot.wim, install.esd or install.swm"
        }
        imageRow.Controls.Add(pathBox, 0, 1)
        Dim browse As New Button With {
            .Name = "btnTestCenterBrowse", .Text = "Choose image",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 0, 0, 2)
        }
        ModernTheme.StyleButton(browse, "browse")
        AddHandler browse.Click, AddressOf BrowseClicked
        imageRow.Controls.Add(browse, 1, 1)
        root.Controls.Add(imageRow, 0, 1)

        Dim optionsRow As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
            .Margin = New Padding(0, 0, 0, 7)
        }
        optionsRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        optionsRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 154))
        optionsRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        includeAudit = New CheckBox With {
            .Name = "chkTestCenterAuditMounted",
            .Text = "Scan servicing of an ALREADY mounted index (optional, can be slow)",
            .Checked = False, .AutoSize = False, .Dock = DockStyle.Fill,
            .Margin = New Padding(0, 2, 7, 1)
        }
        optionsRow.Controls.Add(includeAudit, 0, 0)
        startButton = New Button With {
            .Name = "btnTestCenterRun", .Text = "Run all safe checks",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 1, 0, 1)
        }
        ModernTheme.StyleButton(startButton, "primary")
        AddHandler startButton.Click, AddressOf StartClicked
        optionsRow.Controls.Add(startButton, 1, 0)
        root.Controls.Add(optionsRow, 0, 2)

        grid = New DataGridView With {
            .Name = "gridTestCenterResults", .Dock = DockStyle.Fill,
            .Margin = New Padding(0, 2, 0, 6),
            .ReadOnly = True, .AllowUserToAddRows = False,
            .AllowUserToDeleteRows = False, .RowHeadersVisible = False,
            .MultiSelect = False, .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            .BackgroundColor = Color.White, .BorderStyle = BorderStyle.FixedSingle,
            .ColumnHeadersHeight = 34,
            .ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        }
        grid.RowTemplate.Height = 30
        grid.Columns.Add("colTestStatus", "Result")
        grid.Columns.Add("colTestName", "Check")
        grid.Columns.Add("colTestDetails", "Details")
        grid.Columns(0).Width = 78
        grid.Columns(0).FillWeight = 12
        grid.Columns(1).FillWeight = 28
        grid.Columns(2).FillWeight = 60
        AddHandler grid.CellToolTipTextNeeded, Sub(sender, e)
            If e.RowIndex >= 0 AndAlso e.ColumnIndex >= 0 Then
                e.ToolTipText = Convert.ToString(grid.Rows(e.RowIndex).Cells(e.ColumnIndex).Value)
            End If
        End Sub
        root.Controls.Add(grid, 0, 3)

        Dim footer As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .Margin = Padding.Empty
        }
        footer.RowStyles.Add(New RowStyle(SizeType.Absolute, 37))
        footer.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.Controls.Add(footer, 0, 4)
        statusLabel = New Label With {
            .Name = "lblTestCenterStatus",
            .Text = "No checks run. All tests are local and read-only.",
            .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .AutoEllipsis = True, .ForeColor = ModernTheme.Muted
        }
        footer.Controls.Add(statusLabel, 0, 0)

        Dim actions As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 5, .RowCount = 1,
            .Margin = Padding.Empty
        }
        actions.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        actions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each w As Integer In {126, 132, 185, 105}
            actions.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, w))
        Next
        footer.Controls.Add(actions, 0, 1)

        copyButton = AddButton(actions, "btnTestCenterCopy", "Copy report", 1,
                               "browse", AddressOf CopyClicked)
        saveButton = AddButton(actions, "btnTestCenterSave", "Save report", 2,
                               "browse", AddressOf SaveClicked)
        supportButton = AddButton(actions, "btnTestCenterSupport", "Report issue...", 3,
                                  "secondary", AddressOf SupportClicked)
        cancelAction = AddButton(actions, "btnTestCenterCancel", "Cancel / close", 4,
                                 "neutral", AddressOf CancelClicked)
        copyButton.Enabled = False
        saveButton.Enabled = False
        supportButton.Enabled = False
        ModernTheme.MakeDialogScrollable(Me, root, 940, 680)
        WindowGeometry.Install(Me, "Software\DISMGUI\IntegratedTestCenter")
        AddHandler Me.FormClosing, Sub(sender, e)
            If running IsNot Nothing Then
                Try
                    running.Cancel()
                Catch ex As ObjectDisposedException
                End Try
            End If
        End Sub
    End Sub

    Private Shared Function AddButton(parent As TableLayoutPanel, name As String,
        caption As String, col As Integer, style As String, handler As EventHandler) As Button
        Dim result As New Button With {
            .Name = name, .Text = caption, .Dock = DockStyle.Fill,
            .Margin = New Padding(3, 4, 3, 0)
        }
        ModernTheme.StyleButton(result, style)
        AddHandler result.Click, handler
        parent.Controls.Add(result, col, 0)
        Return result
    End Function

    Private Sub BrowseClicked(sender As Object, e As EventArgs)
        Using chooser As New OpenFileDialog With {
            .Title = "Choose image for local read-only checks",
            .Filter = "WIM, ESD, SWM (*.wim;*.esd;*.swm)|*.wim;*.esd;*.swm|All files|*.*",
            .CheckFileExists = True, .Multiselect = False,
            .RestoreDirectory = True
        }
            If chooser.ShowDialog(Me) = DialogResult.OK Then
                pathBox.Text = chooser.FileName
            End If
        End Using
    End Sub

    Private Async Sub StartClicked(sender As Object, e As EventArgs)
        If running IsNot Nothing Then Return
        Dim tokenSource As New CancellationTokenSource()
        running = tokenSource
        current.Clear()
        grid.Rows.Clear()
        startButton.Enabled = False
        copyButton.Enabled = False
        saveButton.Enabled = False
        supportButton.Enabled = False
        cancelAction.Text = "Cancel"
        statusLabel.Text = "Running local read-only checks..."
        Dim progress As IProgress(Of IntegratedTestChecks.Result) =
            New Progress(Of IntegratedTestChecks.Result)(Sub(entry)
                If Not Me.IsDisposed AndAlso Not tokenSource.IsCancellationRequested Then
                    grid.Rows.Add(entry.Status, entry.Name, entry.Details)
                    statusLabel.Text = entry.ToString()
                End If
            End Sub)
        Try
            Dim results = Await IntegratedTestChecks.RunAllAsync(
                pathBox.Text.Trim(), includeAudit.Checked, Me.DeviceDpi,
                Me.ClientSize.Width, Me.ClientSize.Height, progress, tokenSource.Token)
            If Me.IsDisposed OrElse tokenSource.IsCancellationRequested Then Return
            current = results
            grid.Rows.Clear()
            For Each entry In current
                grid.Rows.Add(entry.Status, entry.Name, entry.Details)
            Next
            statusLabel.Text = "Completed: " &
                current.Where(Function(x) x.Status = "PASS").Count().ToString() & " PASS, " &
                current.Where(Function(x) x.Status = "WARN").Count().ToString() & " WARN, " &
                current.Where(Function(x) x.Status = "FAIL").Count().ToString() & " FAIL, " &
                current.Where(Function(x) x.Status = "SKIP").Count().ToString() & " SKIP."
        Catch ex As OperationCanceledException
            If Not Me.IsDisposed Then statusLabel.Text = "Test canceled; no image changes made."
        Catch ex As Exception
            ErrorDiagnostics.RememberFailure("Integrated Test Center", ex)
            If Not Me.IsDisposed Then
                statusLabel.Text = "Test stopped: " &
                    ErrorDiagnostics.Redact(ex.GetType().Name & ": " & ex.Message)
            End If
        Finally
            running = Nothing
            tokenSource.Dispose()
            If Not Me.IsDisposed Then
                startButton.Enabled = True
                copyButton.Enabled = current.Count > 0
                saveButton.Enabled = current.Count > 0
                supportButton.Enabled = current.Count > 0
                cancelAction.Text = "Close"
            End If
        End Try
    End Sub

    Public Sub PreviewForTest(entries As IEnumerable(Of IntegratedTestChecks.Result))
        current = entries.ToList()
        grid.Rows.Clear()
        For Each entry In current
            grid.Rows.Add(entry.Status, entry.Name, entry.Details)
        Next
        copyButton.Enabled = current.Count > 0
        saveButton.Enabled = current.Count > 0
        supportButton.Enabled = current.Count > 0
        statusLabel.Text = "Test preview loaded (no live commands)."
    End Sub

    Private Sub CopyClicked(sender As Object, e As EventArgs)
        If current.Count = 0 Then Return
        Try
            Clipboard.SetText(IntegratedTestChecks.FormatReport(current))
            statusLabel.Text = "Sanitized test report copied. No upload was performed."
        Catch ex As Exception
            statusLabel.Text = "Copy failed: " & ex.GetType().Name
        End Try
    End Sub

    Private Sub SaveClicked(sender As Object, e As EventArgs)
        If current.Count = 0 Then Return
        Using dialog As New SaveFileDialog With {
            .Title = "Save local test results (sanitized)",
            .Filter = "Text report (*.txt)|*.txt",
            .DefaultExt = "txt", .AddExtension = True,
            .OverwritePrompt = True, .RestoreDirectory = True,
            .FileName = "DISM-GUI-Test-Report.txt"
        }
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                File.WriteAllText(dialog.FileName,
                    IntegratedTestChecks.FormatReport(current), New UTF8Encoding(True))
                statusLabel.Text = "Saved local report. No data was transmitted."
            Catch ex As Exception
                MessageBox.Show(Me, "Cannot save report: " & ex.Message,
                    "Test Center", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Using
    End Sub

    Private Sub SupportClicked(sender As Object, e As EventArgs)
        If current.Count = 0 Then Return
        Using report As New ErrorReportForm(IntegratedTestChecks.FormatReport(current),
                                            "Integrated Test Center")
            report.ShowDialog(Me)
        End Using
    End Sub

    Private Sub CancelClicked(sender As Object, e As EventArgs)
        If running Is Nothing Then
            Me.Close()
        Else
            running.Cancel()
        End If
    End Sub
End Class
