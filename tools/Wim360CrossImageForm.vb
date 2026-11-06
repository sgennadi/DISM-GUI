Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Two different source files, exactly one selected index in each.
' Metadata or offline paths only: no DISM mount, write, export-image, commit.
Public NotInheritable Class Wim360CrossImageForm
    Inherits Form

    Private ReadOnly paths(1) As TextBox
    Private ReadOnly indexSelectors(1) As ComboBox
    Private ReadOnly loadButtons(1) As Button
    Private ReadOnly browseButtons(1) As Button
    Private ReadOnly reports(1) As Wim360Reader.WimReport
    Private ReadOnly generation(1) As Integer
    Private ReadOnly modeBox As ComboBox
    Private ReadOnly typeBox As ComboBox
    Private ReadOnly stateBox As ComboBox
    Private ReadOnly searchBox As TextBox
    Private ReadOnly runButton As Button
    Private ReadOnly exportButton As Button
    Private ReadOnly statusLabel As Label
    Private ReadOnly hintLabel As Label
    Private ReadOnly table As DataGridView
    Private ReadOnly delaySearch As System.Windows.Forms.Timer
    Private ReadOnly tips As ToolTip
    Private comparison As Wim360IndexComparer.Result
    Private visibleRows As New List(Of Wim360IndexComparer.Row)()
    Private job As CancellationTokenSource
    Private busy As Boolean
    Private loading As Boolean

    Public Sub New(Optional baseline As Wim360Reader.WimReport = Nothing,
                   Optional baselineIndex As Integer = 0)
        Me.Name = "Wim360CrossImageForm"
        Me.Text = "DISM GUI - WIM 360 - Compare two WIM/ESD files"
        Me.MinimumSize = New Size(880, 620)
        Me.Size = New Size(1220, 800)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ShowInTaskbar = False
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.Font = ModernTheme.UiFont(9.5F)
        Me.BackColor = Color.White
        Try
            Dim main = Application.OpenForms.Cast(Of Form)().
                FirstOrDefault(Function(f) f.Name = "frmMain")
            If main IsNot Nothing Then Me.Icon = main.Icon
        Catch
            ' Non-critical UI decoration.
        End Try
        tips = New ToolTip With {.AutoPopDelay = 20000}
        delaySearch = New System.Windows.Forms.Timer With {.Interval = 260}
        AddHandler delaySearch.Tick, Sub(sender, e)
            delaySearch.Stop()
            If Not IsDisposed AndAlso Not busy Then ApplyFilter()
        End Sub
        Dim scale As Single = Math.Max(1.0F, Me.Font.Height / 16.0F)
        Dim stack As Boolean = scale > 1.15F
        Dim viewport As New Panel With {
            .Name = "Wim360CrossViewport", .Dock = DockStyle.Fill,
            .BackColor = Color.White, .AutoScroll = True
        }
        Controls.Add(viewport)
        Dim layout As New TableLayoutPanel With {
            .Name = "Wim360CrossLayout", .ColumnCount = 1, .RowCount = 7,
            .Padding = New Padding(14, 9, 14, 12),
            .Margin = Padding.Empty, .BackColor = Color.White,
            .Location = Point.Empty, .Dock = DockStyle.None
        }
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        Dim heights = If(stack, New Integer() {75, 150, 150, 132, 0, 84, 68},
                                New Integer() {76, 94, 94, 94, 0, 86, 65})
        For i = 0 To heights.Length - 1
            If i = 4 Then
                layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            Else
                layout.RowStyles.Add(New RowStyle(SizeType.Absolute,
                    CInt(Math.Ceiling(heights(i) * If(stack, 1.0F, scale)))))
            End If
        Next
        viewport.Controls.Add(layout)
        Dim resizeLayout As Action = Sub()
            If layout.IsDisposed OrElse viewport.IsDisposed Then Return
            Dim requiredWidth = If(stack, 800,
                                   CInt(Math.Ceiling(950 * scale)))
            Dim requiredHeight = heights.Sum() + If(stack, 170, 235) +
                                 layout.Padding.Vertical
            Dim width = Math.Max(requiredWidth, viewport.ClientSize.Width - 1)
            Dim height = Math.Max(requiredHeight, viewport.ClientSize.Height - 1)
            If layout.Size <> New Size(width, height) Then
                layout.Size = New Size(width, height)
            End If
        End Sub
        AddHandler viewport.ClientSizeChanged, Sub(sender, e) resizeLayout()
        AddHandler Me.Shown, Sub(sender, e)
            resizeLayout()
            BeginInvoke(New MethodInvoker(Sub()
                If Not viewport.IsDisposed Then viewport.AutoScrollPosition = Point.Empty
            End Sub))
        End Sub
        resizeLayout()

        Dim heading As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 2, .ColumnCount = 1
        }
        heading.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 55))
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 45))
        heading.Controls.Add(MakeText("WIM 360  |  COMPARE TWO IMAGES",
                                      12.8F, True, ModernTheme.TextInk), 0, 0)
        heading.Controls.Add(MakeText("One index per WIM/ESD. Read-only; no mounting or changes.",
                                      9.0F, False, ModernTheme.Muted), 0, 1)
        layout.Controls.Add(heading, 0, 0)

        For i = 0 To 1
            CreateSourceRow(layout, i, stack, scale)
        Next

        Dim options As New TableLayoutPanel With {
            .Name = "Wim360CrossFilters",
            .ColumnCount = 4, .RowCount = 2, .Dock = DockStyle.Fill,
            .Margin = New Padding(0, 3, 0, 5)
        }
        options.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 20))
        options.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 26))
        options.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 27))
        options.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 27))
        options.RowStyles.Add(New RowStyle(SizeType.Percent, 52))
        options.RowStyles.Add(New RowStyle(SizeType.Percent, 48))
        layout.Controls.Add(options, 0, 3)
        modeBox = New ComboBox With {
            .Name = "cmb360CrossMode", .Dock = DockStyle.Fill,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Margin = New Padding(0, 5, 7, 6)
        }
        modeBox.Items.AddRange(New Object() {
            "Index metadata (no mounting)",
            "Files + folders (no mounting)"
        })
        modeBox.SelectedIndex = 0
        options.Controls.Add(modeBox, 0, 0)
        options.SetColumnSpan(modeBox, 2)
        Dim modeHint = MakeText("Paths are compared by presence only, not by bytes.",
                                9.0F, False, ModernTheme.Muted)
        options.Controls.Add(modeHint, 2, 0)
        options.SetColumnSpan(modeHint, 2)
        typeBox = New ComboBox With {
            .Name = "cmb360CrossType", .Dock = DockStyle.Fill,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Margin = New Padding(0, 2, 7, 2)
        }
        typeBox.Items.Add("All types")
        typeBox.SelectedIndex = 0
        options.Controls.Add(typeBox, 0, 1)
        stateBox = New ComboBox With {
            .Name = "cmb360CrossState", .Dock = DockStyle.Fill,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Margin = New Padding(0, 2, 7, 2)
        }
        stateBox.Items.AddRange(New Object() {
            "All results", "Differences only", "Only in A", "Only in B",
            "Different", "Exists in both", "Present in both"
        })
        stateBox.SelectedIndex = 0
        options.Controls.Add(stateBox, 1, 1)
        searchBox = New TextBox With {
            .Name = "txt360CrossSearch", .Dock = DockStyle.Fill,
            .PlaceholderText = "Search paths, differences, metadata",
            .Margin = New Padding(0, 5, 0, 2)
        }
        options.Controls.Add(searchBox, 2, 1)
        options.SetColumnSpan(searchBox, 2)
        AddHandler typeBox.SelectedIndexChanged, Sub(sender, e) ApplyFilter()
        AddHandler stateBox.SelectedIndexChanged, Sub(sender, e) ApplyFilter()
        AddHandler searchBox.TextChanged, Sub(sender, e)
            delaySearch.Stop()
            delaySearch.Start()
        End Sub
        AddHandler modeBox.SelectedIndexChanged, Sub(sender, e)
            InvalidateResult()
        End Sub

        table = New DataGridView With {
            .Name = "grid360CrossComparison", .Dock = DockStyle.Fill,
            .ReadOnly = True, .VirtualMode = True,
            .AllowUserToAddRows = False, .AllowUserToDeleteRows = False,
            .RowHeadersVisible = False, .MultiSelect = False,
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            .BackgroundColor = Color.White,
            .BorderStyle = BorderStyle.FixedSingle,
            .EnableHeadersVisualStyles = False,
            .ColumnHeadersHeight = Math.Max(36, Me.Font.Height + 17),
            .RowTemplate = New DataGridViewRow With {
                .Height = Math.Max(29, Me.Font.Height + 11)
            }
        }
        table.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 242, 252)
        table.ColumnHeadersDefaultCellStyle.ForeColor = ModernTheme.TextInk
        table.ColumnHeadersDefaultCellStyle.Font = ModernTheme.UiFont(9.0F, True)
        table.DefaultCellStyle.Font = ModernTheme.UiFont(9.0F)
        table.DefaultCellStyle.SelectionBackColor = Color.FromArgb(216, 234, 253)
        table.DefaultCellStyle.SelectionForeColor = ModernTheme.TextInk
        table.Columns.Add(MakeColumn("Type", 105))
        table.Columns.Add(MakeColumn("Item / relative path", 365))
        table.Columns.Add(MakeColumn("Result", 160))
        table.Columns.Add(MakeColumn("A: source", 185))
        table.Columns.Add(MakeColumn("B: source", 185))
        AddHandler table.CellValueNeeded, AddressOf GridValueNeeded
        AddHandler table.CellToolTipTextNeeded, AddressOf GridTooltipNeeded
        layout.Controls.Add(table, 0, 4)

        Dim statusPanel As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .Margin = New Padding(0, 4, 0, 4)
        }
        statusPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        statusPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 48))
        statusPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 52))
        statusLabel = MakeText("Choose A and B WIM/ESD files, then compare.",
                               9.1F, True, ModernTheme.TextInk)
        statusLabel.Name = "lbl360CrossStatus"
        statusPanel.Controls.Add(statusLabel, 0, 0)
        hintLabel = MakeText("Metadata mode: no file scan. Paths mode: both WIM/ESD SHA-256 checked.",
                              9.0F, False, ModernTheme.Muted)
        hintLabel.Name = "lbl360CrossHint"
        statusPanel.Controls.Add(hintLabel, 0, 1)
        layout.Controls.Add(statusPanel, 0, 5)

        Dim actions As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1
        }
        For Each percentage In {44.0F, 32.0F, 24.0F}
            actions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, percentage))
        Next
        actions.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        runButton = New Button With {
            .Name = "btn360CrossCompare", .Text = "Compare images",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 5, 10, 5)
        }
        ModernTheme.StyleButton(runButton, "primary")
        AddHandler runButton.Click, AddressOf CompareClicked
        actions.Controls.Add(runButton, 0, 0)
        exportButton = New Button With {
            .Name = "btn360CrossExport", .Text = "Export filtered CSV",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 5, 10, 5),
            .Enabled = False
        }
        ModernTheme.StyleButton(exportButton, "secondary")
        AddHandler exportButton.Click, AddressOf ExportClicked
        actions.Controls.Add(exportButton, 1, 0)
        Dim closeButton As New Button With {
            .Name = "btn360CrossClose", .Text = "Close",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 5, 0, 5)
        }
        ModernTheme.StyleButton(closeButton, "neutral")
        AddHandler closeButton.Click, Sub(sender, e)
            If busy Then
                job?.Cancel()
            Else
                Close()
            End If
        End Sub
        actions.Controls.Add(closeButton, 2, 0)
        layout.Controls.Add(actions, 0, 6)
        AddHandler Me.FormClosing, Sub(sender, e)
            If busy Then
                job?.Cancel()
                e.Cancel = True
            End If
        End Sub
        AddHandler Me.Disposed, Sub(sender, e)
            delaySearch.Stop()
            delaySearch.Dispose()
            tips.Dispose()
        End Sub
        If baseline IsNot Nothing AndAlso Not baseline.IsSplitSet Then
            paths(0).Text = baseline.ImageFile
            DisplaySource(0, baseline, baselineIndex)
        End If
        UpdateControls()
    End Sub

    Private Shared Function MakeText(value As String, size As Single,
                                     bold As Boolean, foreground As Color) As Label
        Return New Label With {
            .Text = value, .Font = ModernTheme.UiFont(size, bold),
            .ForeColor = foreground, .BackColor = Color.Transparent,
            .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .AutoEllipsis = False
        }
    End Function

    Private Shared Function MakeColumn(caption As String,
                                       width As Integer) As DataGridViewTextBoxColumn
        Return New DataGridViewTextBoxColumn With {
            .HeaderText = caption, .Width = width, .MinimumWidth = 95,
            .SortMode = DataGridViewColumnSortMode.NotSortable
        }
    End Function

    Private Sub CreateSourceRow(parent As TableLayoutPanel,
                                side As Integer, stacked As Boolean,
                                scale As Single)
        Dim row As New TableLayoutPanel With {
            .Name = If(side = 0, "Wim360CrossBaselineRow", "Wim360CrossCandidateRow"),
            .Dock = DockStyle.Fill, .RowCount = If(stacked, 3, 2),
            .ColumnCount = If(stacked, 3, 4),
            .Margin = New Padding(0, 2, 0, 4)
        }
        If stacked Then
            row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 46))
            row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 27))
            row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 27))
            row.RowStyles.Add(New RowStyle(SizeType.Percent, 27))
            row.RowStyles.Add(New RowStyle(SizeType.Percent, 36))
            row.RowStyles.Add(New RowStyle(SizeType.Percent, 37))
        Else
            row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            For Each width As Integer In {170, 96, 113}
                row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute,
                    CInt(Math.Ceiling(width * scale))))
            Next
            row.RowStyles.Add(New RowStyle(SizeType.Percent, 43))
            row.RowStyles.Add(New RowStyle(SizeType.Percent, 57))
        End If
        Dim tag = If(side = 0, "A  |  BASELINE", "B  |  COMPARISON")
        Dim title = MakeText(tag & "  -  choose a WIM/ESD file and index",
                             9.3F, True, ModernTheme.Blue)
        row.Controls.Add(title, 0, 0)
        row.SetColumnSpan(title, row.ColumnCount)
        paths(side) = New TextBox With {
            .Name = If(side = 0, "txt360CrossBaseline", "txt360CrossCandidate"),
            .Dock = DockStyle.Fill, .PlaceholderText = "Path to .wim or .esd",
            .Margin = New Padding(0, 4, 8, 5)
        }
        row.Controls.Add(paths(side), 0, 1)
        If stacked Then row.SetColumnSpan(paths(side), 3)
        indexSelectors(side) = New ComboBox With {
            .Name = If(side = 0, "cmb360CrossBaselineIndex", "cmb360CrossCandidateIndex"),
            .Dock = DockStyle.Fill, .DropDownStyle = ComboBoxStyle.DropDownList,
            .Margin = New Padding(0, 3, 8, 3)
        }
        row.Controls.Add(indexSelectors(side), If(stacked, 0, 1),
                         If(stacked, 2, 1))
        loadButtons(side) = New Button With {
            .Name = If(side = 0, "btn360CrossLoadBaseline", "btn360CrossLoadCandidate"),
            .Text = "Inspect", .Dock = DockStyle.Fill,
            .Margin = New Padding(0, 2, 8, 3)
        }
        ModernTheme.StyleButton(loadButtons(side), "secondary")
        row.Controls.Add(loadButtons(side), If(stacked, 1, 2),
                         If(stacked, 2, 1))
        browseButtons(side) = New Button With {
            .Name = If(side = 0, "btn360CrossBrowseBaseline", "btn360CrossBrowseCandidate"),
            .Text = "Browse...", .Dock = DockStyle.Fill,
            .Margin = New Padding(0, 2, 0, 3)
        }
        ModernTheme.StyleButton(browseButtons(side), "browse")
        row.Controls.Add(browseButtons(side), If(stacked, 2, 3), If(stacked, 2, 1))
        AddHandler browseButtons(side).Click, Sub(sender, e) BrowseSource(side)
        AddHandler loadButtons(side).Click, Sub(sender, e) LoadSource(side)
        AddHandler paths(side).TextChanged, Sub(sender, e)
            generation(side) += 1
            reports(side) = Nothing
            indexSelectors(side).Items.Clear()
            InvalidateResult()
        End Sub
        AddHandler indexSelectors(side).SelectedIndexChanged, Sub(sender, e)
            InvalidateResult()
        End Sub
        parent.Controls.Add(row, 0, side + 1)
        tips.SetToolTip(paths(side), "Complete path to WIM/ESD; no automatic mount.")
        tips.SetToolTip(indexSelectors(side), "Original index number inside this source.")
    End Sub

    Private Sub BrowseSource(side As Integer)
        If busy OrElse loading Then Return
        Using dialog As New OpenFileDialog With {
            .Title = If(side = 0, "Choose baseline image (A)", "Choose comparison image (B)"),
            .Filter = "Windows images (*.wim;*.esd)|*.wim;*.esd|All files (*.*)|*.*",
            .CheckFileExists = True, .Multiselect = False,
            .RestoreDirectory = True
        }
            If File.Exists(paths(side).Text.Trim()) Then
                dialog.FileName = paths(side).Text.Trim()
            End If
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            paths(side).Text = dialog.FileName
            LoadSource(side)
        End Using
    End Sub

    Private Async Sub LoadSource(side As Integer)
        If busy OrElse loading Then Return
        Dim filename = paths(side).Text.Trim()
        If filename.Length = 0 Then Return
        Dim requestedGeneration = generation(side)
        loading = True
        UpdateControls()
        statusLabel.Text = "Inspecting " & If(side = 0, "A", "B") & " metadata (read-only)..."
        Try
            Dim loaded = Await Task.Run(Function() Wim360Reader.Inspect(filename))
            If IsDisposed OrElse requestedGeneration <> generation(side) Then Return
            If loaded.IsSplitSet Then Throw New NotSupportedException(
                "Cross-image comparison needs two complete WIM/ESD files, not SWM parts.")
            If Not String.Equals(Path.GetFullPath(filename), loaded.ImageFile,
                                 StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidOperationException("Inspected image path changed.")
            End If
            DisplaySource(side, loaded, 1)
            statusLabel.Text = If(side = 0, "A", "B") & " loaded: " &
                loaded.ImageCount.ToString() & " indexes available."
        Catch ex As Exception
            ErrorDiagnostics.RememberFailure("WIM 360 cross-image Inspect", ex)
            If Not IsDisposed Then
                statusLabel.Text = If(side = 0, "A", "B") &
                    " unavailable: " & ex.Message
            End If
        Finally
            loading = False
            If Not IsDisposed Then UpdateControls()
        End Try
    End Sub

    Private Sub DisplaySource(side As Integer, report As Wim360Reader.WimReport,
                              preferredIndex As Integer)
        If report.IsSplitSet Then Return
        reports(side) = report
        indexSelectors(side).BeginUpdate()
        Try
            indexSelectors(side).Items.Clear()
            For Each image In report.Images.OrderBy(Function(x) x.Index)
                indexSelectors(side).Items.Add(image)
            Next
            Dim match = report.Images.FirstOrDefault(Function(x) x.Index = preferredIndex)
            If match IsNot Nothing Then
                indexSelectors(side).SelectedItem = match
            ElseIf indexSelectors(side).Items.Count > 0 Then
                indexSelectors(side).SelectedIndex = 0
            End If
        Finally
            indexSelectors(side).EndUpdate()
        End Try
        tips.SetToolTip(paths(side), report.ImageFile)
        InvalidateResult()
    End Sub

    Private Sub InvalidateResult()
        If table Is Nothing Then Return
        comparison = Nothing
        visibleRows.Clear()
        table.RowCount = 0
        exportButton.Enabled = False
        If Not busy Then UpdateControls()
    End Sub

    Private Sub UpdateControls()
        If runButton Is Nothing Then Return
        For i = 0 To 1
            paths(i).ReadOnly = busy OrElse loading
            loadButtons(i).Enabled = Not busy AndAlso Not loading
            browseButtons(i).Enabled = Not busy AndAlso Not loading
            indexSelectors(i).Enabled = Not busy AndAlso Not loading
        Next
        If busy Then
            runButton.Text = "Cancel scan"
            runButton.Enabled = True
        Else
            runButton.Text = "Compare images"
            runButton.Enabled = Not loading AndAlso
                reports(0) IsNot Nothing AndAlso reports(1) IsNot Nothing AndAlso
                indexSelectors(0).SelectedItem IsNot Nothing AndAlso
                indexSelectors(1).SelectedItem IsNot Nothing
        End If
        exportButton.Enabled = Not busy AndAlso comparison IsNot Nothing
        modeBox.Enabled = Not busy AndAlso Not loading
        typeBox.Enabled = Not busy AndAlso Not loading
        stateBox.Enabled = Not busy AndAlso Not loading
        searchBox.ReadOnly = busy OrElse loading
    End Sub

    Private Async Sub CompareClicked(sender As Object, e As EventArgs)
        If busy Then
            job?.Cancel()
            Return
        End If
        If Not runButton.Enabled OrElse loading Then Return
        Dim aReport = reports(0)
        Dim bReport = reports(1)
        Dim aInfo = TryCast(indexSelectors(0).SelectedItem, Wim360Reader.ImageInfo)
        Dim bInfo = TryCast(indexSelectors(1).SelectedItem, Wim360Reader.ImageInfo)
        If aReport Is Nothing OrElse bReport Is Nothing OrElse
           aInfo Is Nothing OrElse bInfo Is Nothing Then Return
        Dim pathsMode = modeBox.SelectedIndex = 1
        Dim source = New CancellationTokenSource()
        job = source
        busy = True
        UpdateControls()
        statusLabel.Text = "Starting read-only comparison..."
        Dim progress As IProgress(Of String) = New Progress(Of String)(Sub(message)
            If Not IsDisposed AndAlso Not source.IsCancellationRequested Then
                statusLabel.Text = message
            End If
        End Sub)
        Try
            Dim compared = Await Task.Run(Function()
                Return Wim360CrossImage.Compare(aReport, aInfo.Index, bReport, bInfo.Index,
                                                 pathsMode, source.Token, progress)
            End Function)
            If IsDisposed OrElse source.IsCancellationRequested Then Return
            ShowComparison(compared)
        Catch ex As OperationCanceledException
            If Not IsDisposed Then statusLabel.Text = "Comparison canceled. No partial results shown."
        Catch ex As Exception
            ErrorDiagnostics.RememberFailure("WIM 360 cross-image comparison", ex)
            If Not IsDisposed Then
                statusLabel.Text = "Comparison failed; no partial results shown."
                hintLabel.Text = ex.Message
                tips.SetToolTip(hintLabel, ex.Message)
            End If
        Finally
            If Object.ReferenceEquals(job, source) Then job = Nothing
            source.Dispose()
            busy = False
            If Not IsDisposed Then UpdateControls()
        End Try
    End Sub

    ' Exposed for synthetic read-only GUI regression tests.
    Public Sub PreviewForTest(compared As Wim360IndexComparer.Result)
        ShowComparison(compared)
    End Sub

    Private Sub ShowComparison(compared As Wim360IndexComparer.Result)
        Dim oldType = Convert.ToString(typeBox.SelectedItem)
        Dim oldState = Convert.ToString(stateBox.SelectedItem)
        comparison = compared
        typeBox.Items.Clear()
        typeBox.Items.Add("All types")
        For Each kind In compared.Rows.Select(Function(x) x.Kind).
                      Distinct(StringComparer.OrdinalIgnoreCase).
                      OrderBy(Function(x) x, StringComparer.OrdinalIgnoreCase)
            typeBox.Items.Add(kind)
        Next
        typeBox.SelectedItem = If(typeBox.Items.Contains(oldType), oldType, "All types")
        stateBox.SelectedItem = If(stateBox.Items.Contains(oldState), oldState, "All results")
        table.Columns(3).HeaderText = Wim360IndexComparer.ColumnLabel(compared, 1)
        table.Columns(4).HeaderText = Wim360IndexComparer.ColumnLabel(compared, 2)
        tips.SetToolTip(table, "A: " & reports(0)?.ImageFile & Environment.NewLine &
                           "B: " & reports(1)?.ImageFile)
        hintLabel.Text = If(compared.Mode.Contains("offline paths"),
            "Path presence only; matching names do not establish identical bytes.",
            "Index metadata only; no file content inspected.")
        tips.SetToolTip(hintLabel, compared.Notes)
        ApplyFilter()
    End Sub

    Private Sub ApplyFilter()
        If comparison Is Nothing Then Return
        Dim filter = Convert.ToString(stateBox.SelectedItem)
        Select Case filter
            Case "Only in A" : filter = "Only in Index 1"
            Case "Only in B" : filter = "Only in Index 2"
            Case "Exists in both" : filter = "Exists in all"
            Case "Present in both" : filter = "Present in all"
        End Select
        visibleRows = Wim360IndexComparer.Filter(
            comparison.Rows, Convert.ToString(typeBox.SelectedItem),
            filter, searchBox.Text, 2)
        table.RowCount = visibleRows.Count
        Dim diffs = visibleRows.Where(Function(x)
            Return x.State = "Different" OrElse
                x.State.StartsWith("Only in Index ", StringComparison.Ordinal) OrElse
                x.State = "Ambiguous duplicates"
        End Function).Count()
        statusLabel.Text = visibleRows.Count.ToString("N0") & "/" &
            comparison.Rows.Count.ToString("N0") & " rows  |  " &
            diffs.ToString("N0") & " differences  |  " & comparison.Mode
        exportButton.Enabled = True
    End Sub

    Private Sub GridValueNeeded(sender As Object, e As DataGridViewCellValueEventArgs)
        If e.RowIndex < 0 OrElse e.RowIndex >= visibleRows.Count Then Return
        Dim row = visibleRows(e.RowIndex)
        Select Case e.ColumnIndex
            Case 0 : e.Value = row.Kind
            Case 1 : e.Value = row.Name
            Case 2
                If row.State = "Only in Index 1" Then
                    e.Value = "Only in A"
                ElseIf row.State = "Only in Index 2" Then
                    e.Value = "Only in B"
                Else
                    e.Value = row.State
                End If
            Case 3 : e.Value = row.At(1)
            Case 4 : e.Value = row.At(2)
        End Select
    End Sub

    Private Sub GridTooltipNeeded(sender As Object,
                                  e As DataGridViewCellToolTipTextNeededEventArgs)
        If e.RowIndex < 0 OrElse e.RowIndex >= visibleRows.Count Then Return
        Dim row = visibleRows(e.RowIndex)
        Select Case e.ColumnIndex
            Case 1 : e.ToolTipText = row.Name
            Case 2 : e.ToolTipText = row.State & " " & row.Notes
            Case 3 : e.ToolTipText = row.At(1)
            Case 4 : e.ToolTipText = row.At(2)
        End Select
    End Sub

    Private Sub ExportClicked(sender As Object, e As EventArgs)
        If comparison Is Nothing OrElse busy Then Return
        Using picker As New SaveFileDialog With {
            .Title = "Save cross-image comparison (filtered CSV)",
            .Filter = "CSV comparison (*.csv)|*.csv",
            .DefaultExt = "csv", .AddExtension = True,
            .OverwritePrompt = True, .RestoreDirectory = True,
            .FileName = "WIM360-A-vs-B.csv"
        }
            If picker.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Using writer As New StreamWriter(picker.FileName, False, New UTF8Encoding(True))
                    Wim360IndexComparer.WriteCsv(writer, comparison, visibleRows)
                End Using
                statusLabel.Text = "Saved " & visibleRows.Count.ToString("N0") &
                    " filtered rows. Neither source image was modified."
            Catch ex As Exception
                ErrorDiagnostics.RememberFailure("WIM 360 cross-image CSV", ex)
                MessageBox.Show(Me, "CSV export failed: " & ex.Message,
                                "WIM 360", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Using
    End Sub
End Class
