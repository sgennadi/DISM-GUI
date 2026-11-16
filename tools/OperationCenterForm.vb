Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Unified operator-facing safe plan and history. Writes to WIM, recovery
' actions, driver installs and arbitrary shell commands are NOT batch types.
Public NotInheritable Class OperationCenterForm
    Inherits Form

    Private ReadOnly profileName As TextBox
    Private ReadOnly saved As ComboBox
    Private ReadOnly kindSelector As ComboBox
    Private ReadOnly imagePath As TextBox
    Private ReadOnly indexBox As NumericUpDown
    Private ReadOnly planGrid As DataGridView
    Private ReadOnly historyGrid As DataGridView
    Private ReadOnly historyDetails As TextBox
    Private ReadOnly status As Label
    Private ReadOnly runButton As Button
    Private ReadOnly preflightButton As Button
    Private ReadOnly cancelButton As Button
    Private ReadOnly addStepButton As Button
    Private ReadOnly saveButton As Button
    Private ReadOnly loadButton As Button
    Private ReadOnly tabs As TabControl
    Private active As CancellationTokenSource
    Private busy As Boolean
    Private nextResultIndex As Integer

    Public Sub New(Optional initialImage As String = "")
        Me.Name = "OperationCenterForm"
        Me.Text = "DISM GUI - Operation Center"
        Me.Size = New Size(1080, 790)
        Me.MinimumSize = New Size(840, 600)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ShowInTaskbar = False
        Me.BackColor = Color.White
        Me.Font = ModernTheme.UiFont(9.5F)
        Me.AutoScaleMode = AutoScaleMode.Font
        Try
            Me.Icon = ModernTheme.MakeAppIcon()
        Catch
        End Try

        Dim root As New TableLayoutPanel With {
            .Name = "OperationCenterLayout", .Dock = DockStyle.Fill,
            .ColumnCount = 1, .RowCount = 3,
            .Padding = New Padding(14, 10, 14, 12)
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 80))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 44))
        Controls.Add(root)
        Dim heading As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 2, .ColumnCount = 1
        }
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 55))
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 45))
        heading.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        heading.Controls.Add(New Label With {
            .Text = "OPERATION CENTER",
            .Font = ModernTheme.UiFont(13.2F, True),
            .ForeColor = ModernTheme.TextInk,
            .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0)
        heading.Controls.Add(New Label With {
            .Text = "Preflight, sequential read-only operations, saved profiles and recovery history",
            .Dock = DockStyle.Fill, .AutoEllipsis = True,
            .ForeColor = ModernTheme.Muted,
            .TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1)
        root.Controls.Add(heading, 0, 0)

        tabs = New TabControl With {
            .Name = "tabsOperationCenter", .Dock = DockStyle.Fill
        }
        Dim planTab As New TabPage("Plan & run")
        Dim historyTab As New TabPage("Local history")
        tabs.TabPages.Add(planTab)
        tabs.TabPages.Add(historyTab)
        root.Controls.Add(tabs, 0, 1)

        Dim layout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 4, .ColumnCount = 1,
            .Padding = New Padding(8, 10, 8, 8)
        }
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 54))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 64))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 60))
        planTab.Controls.Add(layout)

        Dim profiles As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1
        }
        For Each width As Single In {30.0F, 34.0F, 18.0F, 18.0F}
            profiles.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, width))
        Next
        profiles.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.Controls.Add(profiles, 0, 0)
        profileName = New TextBox With {
            .Name = "txtOperationProfileName",
            .PlaceholderText = "Profile name", .Text = "New read-only plan",
            .Dock = DockStyle.Fill, .Margin = New Padding(3, 12, 7, 8)
        }
        profiles.Controls.Add(profileName, 0, 0)
        saved = New ComboBox With {
            .Name = "cmbOperationSavedProfile", .DropDownStyle = ComboBoxStyle.DropDownList,
            .Dock = DockStyle.Fill, .Margin = New Padding(3, 12, 7, 8)
        }
        profiles.Controls.Add(saved, 1, 0)
        loadButton = AddButton(profiles, "btnOperationLoad", "Load profile", 2,
                               "secondary", AddressOf LoadClicked)
        saveButton = AddButton(profiles, "btnOperationSave", "Save profile", 3,
                               "secondary", AddressOf SaveClicked)

        Dim composer As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 1, .ColumnCount = 4
        }
        For Each width As Single In {22.0F, 54.0F, 9.0F, 15.0F}
            composer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, width))
        Next
        composer.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.Controls.Add(composer, 0, 1)
        kindSelector = New ComboBox With {
            .Name = "cmbOperationKind", .Dock = DockStyle.Fill,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Margin = New Padding(3, 12, 7, 10)
        }
        kindSelector.Items.AddRange(OperationWorkflow.AllowedKinds())
        kindSelector.SelectedIndex = 1
        composer.Controls.Add(kindSelector, 0, 0)
        imagePath = New TextBox With {
            .Name = "txtOperationImage", .Dock = DockStyle.Fill,
            .Text = If(initialImage, "").Trim(),
            .PlaceholderText = "Absolute path to WIM/ESD/SWM (when required)",
            .Margin = New Padding(3, 12, 7, 10)
        }
        composer.Controls.Add(imagePath, 1, 0)
        indexBox = New NumericUpDown With {
            .Name = "numOperationIndex", .Dock = DockStyle.Fill,
            .Minimum = 0, .Maximum = 255, .Value = 0,
            .Margin = New Padding(3, 12, 7, 10)
        }
        composer.Controls.Add(indexBox, 2, 0)
        addStepButton = AddButton(composer, "btnOperationAdd", "Add step", 3,
                                  "primary", AddressOf AddClicked)

        planGrid = MakeGrid("gridOperationPlan",
             {"Order", "Operation", "Index", "WIM / ESD / SWM", "Result"})
        planGrid.Columns(0).FillWeight = 9
        planGrid.Columns(1).FillWeight = 22
        planGrid.Columns(2).FillWeight = 9
        planGrid.Columns(3).FillWeight = 42
        planGrid.Columns(4).FillWeight = 18
        layout.Controls.Add(planGrid, 0, 2)

        Dim actions As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 5, .RowCount = 1
        }
        For i As Integer = 1 To 5
            actions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 20.0F))
        Next
        actions.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.Controls.Add(actions, 0, 3)
        AddButton(actions, "btnOperationRemove", "Remove step", 0,
                  "secondary", AddressOf RemoveClicked)
        AddButton(actions, "btnOperationClear", "Clear plan", 1,
                  "neutral", AddressOf ClearClicked)
        preflightButton = AddButton(actions, "btnOperationPreflight", "Preflight", 2,
                                    "browse", AddressOf PreflightClicked)
        runButton = AddButton(actions, "btnOperationRun", "Run queue", 3,
                              "primary", AddressOf RunClicked)
        cancelButton = AddButton(actions, "btnOperationCancel", "Stop after step", 4,
                                 "neutral", AddressOf CancelClicked)
        cancelButton.Enabled = False

        Dim historyLayout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3,
            .Padding = New Padding(8, 10, 8, 8)
        }
        historyLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        historyLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 62))
        historyLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 30))
        historyLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 8))
        historyTab.Controls.Add(historyLayout)
        historyGrid = MakeGrid("gridOperationHistory",
                              {"UTC", "Profile / action", "Result", "Steps"})
        historyGrid.Columns(0).FillWeight = 28
        historyGrid.Columns(1).FillWeight = 44
        historyGrid.Columns(2).FillWeight = 16
        historyGrid.Columns(3).FillWeight = 12
        historyLayout.Controls.Add(historyGrid, 0, 0)
        historyDetails = New TextBox With {
            .Name = "txtOperationHistoryDetails", .Dock = DockStyle.Fill,
            .Multiline = True, .ReadOnly = True,
            .ScrollBars = ScrollBars.Vertical,
            .BackColor = Color.FromArgb(249, 251, 254)
        }
        historyLayout.Controls.Add(historyDetails, 0, 1)
        Dim historyRefresh = New Button With {
            .Name = "btnOperationHistoryRefresh",
            .Text = "Refresh local history", .Dock = DockStyle.Fill
        }
        ModernTheme.StyleButton(historyRefresh, "secondary")
        AddHandler historyRefresh.Click, Sub(sender, e) ReloadHistory()
        historyLayout.Controls.Add(historyRefresh, 0, 2)
        AddHandler historyGrid.SelectionChanged, Sub(sender, e)
            If historyGrid.SelectedRows.Count = 0 Then Return
            historyDetails.Text = Convert.ToString(historyGrid.SelectedRows(0).Tag)
        End Sub

        status = New Label With {
            .Name = "lblOperationStatus", .Dock = DockStyle.Fill,
            .Text = "Read-only queue. No DISM Commit, Discard, mount or import operations are scheduled.",
            .ForeColor = ModernTheme.Muted, .TextAlign = ContentAlignment.MiddleLeft,
            .AutoEllipsis = True
        }
        root.Controls.Add(status, 0, 2)
        Dim fontScale As Single = Math.Max(1.0F, Me.Font.Height / 17.0F)
        ModernTheme.MakeDialogScrollable(Me, root,
            CInt(Math.Ceiling(820.0F / fontScale)), 700)
        WindowGeometry.Install(Me, "Software\DISMGUI\OperationCenter")
        AddHandler Me.Shown, Sub(sender, e)
            ReloadProfiles()
            ReloadHistory()
        End Sub
        AddHandler Me.FormClosing, Sub(sender, e)
            If busy Then
                active?.Cancel()
                status.Text = "Finishing the current read-only step before closing..."
                e.Cancel = True
            End If
        End Sub
    End Sub

    Private Shared Function MakeGrid(name As String, titles As String()) As DataGridView
        Dim grid As New DataGridView With {
            .Name = name, .Dock = DockStyle.Fill, .ReadOnly = True,
            .AllowUserToAddRows = False, .AllowUserToDeleteRows = False,
            .AllowUserToResizeRows = False, .RowHeadersVisible = False,
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .MultiSelect = False, .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            .BackgroundColor = Color.White, .BorderStyle = BorderStyle.FixedSingle,
            .ColumnHeadersHeight = 36,
            .ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        }
        grid.RowTemplate.Height = 30
        For Each title In titles
            grid.Columns.Add("col" & name & grid.Columns.Count.ToString(), title)
        Next
        AddHandler grid.CellToolTipTextNeeded, Sub(sender, e)
            If e.RowIndex >= 0 AndAlso e.ColumnIndex >= 0 Then
                e.ToolTipText = Convert.ToString(
                    grid.Rows(e.RowIndex).Cells(e.ColumnIndex).Value)
            End If
        End Sub
        Return grid
    End Function

    Private Shared Function AddButton(parent As TableLayoutPanel, name As String,
                                      caption As String, col As Integer,
                                      role As String, handler As EventHandler) As Button
        Dim result As New Button With {
            .Name = name, .Text = caption, .Dock = DockStyle.Fill,
            .Margin = New Padding(3, 7, 5, 7)
        }
        ModernTheme.StyleButton(result, role)
        AddHandler result.Click, handler
        parent.Controls.Add(result, col, 0)
        Return result
    End Function

    Private Sub SetBusy(value As Boolean)
        busy = value
        runButton.Enabled = Not value
        preflightButton.Enabled = Not value
        saveButton.Enabled = Not value
        loadButton.Enabled = Not value
        addStepButton.Enabled = Not value
        cancelButton.Enabled = value
    End Sub

    Private Sub AddClicked(sender As Object, e As EventArgs)
        If busy Then Return
        Try
            Dim item As New OperationWorkflow.StepPlan With {
                .Kind = Convert.ToString(kindSelector.SelectedItem),
                .ImagePath = If(imagePath.Text, "").Trim(),
                .ImageIndex = CInt(indexBox.Value)
            }
            If item.Kind = "EnvironmentPreflight" Then
                item.ImagePath = ""
                item.ImageIndex = 0
            ElseIf item.Kind <> "MountedInventory" Then
                item.ImageIndex = 0
            End If
            If planGrid.Rows.Count >= OperationWorkflow.MaxSteps Then
                Throw New InvalidOperationException("Maximum 16 sequential steps.")
            End If
            Dim temporary As New OperationWorkflow.Profile With {
                .Name = "Preview", .Steps = New List(Of OperationWorkflow.StepPlan) From {item}
            }
            OperationWorkflow.Validate(temporary)
            Dim pos = planGrid.Rows.Add(planGrid.Rows.Count + 1, item.Kind,
                If(item.ImageIndex = 0, "-", item.ImageIndex.ToString()),
                item.ImagePath, "Queued")
            planGrid.Rows(pos).Tag = item
            status.Text = "Step added; Preflight checks every selected source before execution."
        Catch ex As Exception
            status.Text = "Cannot add step: " & ex.Message
        End Try
    End Sub

    Private Function CurrentProfile() As OperationWorkflow.Profile
        Dim items = planGrid.Rows.Cast(Of DataGridViewRow)().
            Select(Function(r) DirectCast(r.Tag, OperationWorkflow.StepPlan)).ToList()
        Dim profile As New OperationWorkflow.Profile With {
            .Name = profileName.Text.Trim(), .Steps = items
        }
        Return OperationWorkflow.Validate(profile)
    End Function

    Private Sub RemoveClicked(sender As Object, e As EventArgs)
        If busy OrElse planGrid.SelectedRows.Count = 0 Then Return
        planGrid.Rows.Remove(planGrid.SelectedRows(0))
        Renumber()
    End Sub

    Private Sub ClearClicked(sender As Object, e As EventArgs)
        If busy Then Return
        planGrid.Rows.Clear()
        status.Text = "Queue cleared; no WIM files changed."
    End Sub

    Private Sub Renumber()
        For i As Integer = 0 To planGrid.Rows.Count - 1
            planGrid.Rows(i).Cells(0).Value = (i + 1).ToString()
        Next
    End Sub

    Private Sub ReloadProfiles()
        saved.Items.Clear()
        Try
            For Each p In OperationWorkflow.LoadProfiles()
                saved.Items.Add(p.Name)
            Next
            If saved.Items.Count > 0 Then saved.SelectedIndex = 0
        Catch ex As Exception
            status.Text = "Saved profiles unavailable: " & ex.Message
        End Try
    End Sub

    Private Sub SaveClicked(sender As Object, e As EventArgs)
        If busy Then Return
        Try
            OperationWorkflow.SaveProfile(CurrentProfile())
            ReloadProfiles()
            status.Text = "Profile saved locally. No image write actions were saved."
        Catch ex As Exception
            status.Text = "Save profile failed: " & ex.Message
        End Try
    End Sub

    Private Sub LoadClicked(sender As Object, e As EventArgs)
        If busy OrElse saved.SelectedItem Is Nothing Then Return
        Try
            Dim match = OperationWorkflow.LoadProfiles().First(Function(x)
                Return String.Equals(x.Name, Convert.ToString(saved.SelectedItem),
                                     StringComparison.OrdinalIgnoreCase)
            End Function)
            profileName.Text = match.Name
            planGrid.Rows.Clear()
            For Each stepItem In match.Steps
                Dim pos = planGrid.Rows.Add(planGrid.Rows.Count + 1, stepItem.Kind,
                    If(stepItem.ImageIndex = 0, "-", stepItem.ImageIndex.ToString()),
                    stepItem.ImagePath, "Queued")
                planGrid.Rows(pos).Tag = stepItem
            Next
            status.Text = "Profile loaded. Run Preflight before execution."
        Catch ex As Exception
            status.Text = "Load profile failed: " & ex.Message
        End Try
    End Sub

    Private Async Sub PreflightClicked(sender As Object, e As EventArgs)
        If busy Then Return
        Try
            Dim profile = CurrentProfile()
            SetBusy(True)
            active = New CancellationTokenSource()
            status.Text = "Checking selected image sources (read-only)..."
            Dim checks = Await OperationWorkflow.PreflightAsync(profile, active.Token)
            For i As Integer = 0 To checks.Count - 1
                planGrid.Rows(i).Cells(4).Value = checks(i).Status
            Next
            status.Text = "Preflight: " &
                checks.Where(Function(x) x.Status = "READY").Count().ToString() &
                " ready, " & checks.Where(Function(x) x.Status = "BLOCKED").Count().ToString() &
                " blocked. No servicing commands were executed."
        Catch ex As OperationCanceledException
            status.Text = "Preflight canceled."
        Catch ex As Exception
            status.Text = "Preflight failed: " & ex.Message
        Finally
            active?.Dispose()
            active = Nothing
            SetBusy(False)
        End Try
    End Sub

    Private Async Sub RunClicked(sender As Object, e As EventArgs)
        If busy Then Return
        Try
            Dim profile = CurrentProfile()
            SetBusy(True)
            active = New CancellationTokenSource()
            nextResultIndex = 0
            For Each row As DataGridViewRow In planGrid.Rows
                row.Cells(4).Value = "Pending"
            Next
            Dim observer As IProgress(Of OperationWorkflow.StepResult) =
                New Progress(Of OperationWorkflow.StepResult)(Sub(item)
                    If IsDisposed Then Return
                    If nextResultIndex < planGrid.Rows.Count Then
                        planGrid.Rows(nextResultIndex).Cells(4).Value = item.Status
                        nextResultIndex += 1
                    End If
                    status.Text = item.Kind & " | " & item.Status & " | " & item.Detail
                End Sub)
            Dim run = Await OperationWorkflow.RunAsync(profile, observer, active.Token)
            status.Text = "Queue " & run.Status & " - " & run.Results.Count.ToString() &
                          " steps recorded locally. No WIM writes."
            ReloadHistory()
        Catch ex As Exception
            status.Text = "Queue unavailable: " & ex.Message
        Finally
            active?.Dispose()
            active = Nothing
            SetBusy(False)
        End Try
    End Sub

    Private Sub CancelClicked(sender As Object, e As EventArgs)
        If Not busy Then Return
        active?.Cancel()
        status.Text = "Cancel requested; current read-only operation may finish first."
    End Sub

    Private Sub ReloadHistory()
        historyGrid.Rows.Clear()
        Try
            For Each entry In OperationWorkflow.LoadHistory()
                Dim pos = historyGrid.Rows.Add(
                    entry.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    entry.Profile, entry.Status, entry.Results.Count)
                historyGrid.Rows(pos).Tag = String.Join(Environment.NewLine,
                    entry.Results.Select(Function(x)
                        Return x.Kind & " | " & x.Status & " | " & x.Detail
                    End Function))
            Next
            For Each entry In WimMountSafety.RecentOperations(25)
                Dim pos = historyGrid.Rows.Add(
                    entry.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    "Mount " & entry.Action, entry.State, 1)
                historyGrid.Rows(pos).Tag = entry.Detail
            Next
            If historyGrid.Rows.Count = 0 Then
                historyDetails.Text = "No local operation history."
            End If
        Catch ex As Exception
            historyDetails.Text = "Local history unavailable: " & ex.Message
        End Try
    End Sub
End Class
