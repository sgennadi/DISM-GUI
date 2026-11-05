Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Multi-column comparison of 2..8 indexes in a single WIM/ESD.
' No DISM mount, unmount, install, commit or file modification is performed.
Public NotInheritable Class Wim360CompareIndexesForm
    Inherits Form

    Private ReadOnly source As Wim360Reader.WimReport
    Private ReadOnly selectedIndexes As CheckedListBox
    Private ReadOnly modeBox As ComboBox
    Private ReadOnly categoryBox As ComboBox
    Private ReadOnly stateBox As ComboBox
    Private ReadOnly searchBox As TextBox
    Private ReadOnly compareButton As Button
    Private ReadOnly exportButton As Button
    Private ReadOnly hashButton As Button
    Private ReadOnly closeButton As Button
    Private ReadOnly summaryLabel As Label
    Private ReadOnly hintLabel As Label
    Private ReadOnly grid As DataGridView
    Private ReadOnly detailsTip As ToolTip
    Private ReadOnly searchDelay As System.Windows.Forms.Timer
    Private comparison As Wim360IndexComparer.Result
    Private visibleRows As New List(Of Wim360IndexComparer.Row)()
    Private job As CancellationTokenSource
    Private working As Boolean

    Public Sub New(report As Wim360Reader.WimReport)
        If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
        If report.Images.Count < 2 Then
            Throw New ArgumentException("Compare Indexes requires at least two indexes.")
        End If
        source = report
        Me.Name = "Wim360CompareIndexesForm"
        Me.Text = "DISM GUI " & Reflection.Assembly.GetExecutingAssembly().
            GetName().Version.ToString(3) & " - WIM 360 Compare Indexes"
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ShowInTaskbar = False
        Me.MinimumSize = New Size(940, 620)
        Me.Size = New Size(1230, 790)
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.Font = ModernTheme.UiFont(9.6F)
        Me.BackColor = Color.White
        Try
            Dim main = Application.OpenForms.Cast(Of Form)().
                FirstOrDefault(Function(f) f.Name = "frmMain")
            If main IsNot Nothing Then Me.Icon = main.Icon
        Catch
            ' Cosmetic only.
        End Try

        ' A single scrollable viewport protects every action/heading at high
        ' DPI and on small screens; the grid retains its own scrollbars.
        Dim uiScale As Single = Math.Max(1.0F, Me.Font.Height / 17.0F)
        Dim stackedLayout As Boolean = uiScale > 1.15F
        Dim viewport As New Panel With {
            .Name = "Wim360CompareIndexesViewport",
            .Dock = DockStyle.Fill, .AutoScroll = True,
            .BackColor = Color.White
        }
        Controls.Add(viewport)
        Dim root As New TableLayoutPanel With {
            .Name = "Wim360CompareIndexesLayout",
            .Dock = DockStyle.None, .Location = Point.Empty,
            .ColumnCount = 1, .RowCount = 6,
            .Padding = New Padding(15, 10, 15, 12), .BackColor = Color.White
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        ' Compact DPI mode keeps index selection and comparison mode side by
        ' side and reserves a visible grid, rather than stacking controls so
        ' deeply that the comparison table starts below the window.
        Dim headerRows As Integer() = If(stackedLayout,
            New Integer() {
                CInt(Math.Ceiling(70 + 13 * (uiScale - 1))),
                CInt(Math.Ceiling(133 + 42 * (uiScale - 1))),
                CInt(Math.Ceiling(85 + 40 * (uiScale - 1)))
            },
            New Integer() {CInt(Math.Ceiling(76 * uiScale)),
                           CInt(Math.Ceiling(156 * uiScale)),
                           CInt(Math.Ceiling(62 * uiScale))})
        For Each h As Integer In headerRows
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, h))
        Next
        Dim statusHeight As Integer = If(stackedLayout,
            CInt(Math.Ceiling(52 + 17 * (uiScale - 1))),
            CInt(Math.Ceiling(80 * uiScale)))
        Dim actionsHeight As Integer = If(stackedLayout,
            CInt(Math.Ceiling(51 + 15 * (uiScale - 1))),
            CInt(Math.Ceiling(68 * uiScale)))
        Dim minimumGridHeight As Integer = If(stackedLayout, 147,
                                             CInt(Math.Ceiling(235 * uiScale)))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, statusHeight))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, actionsHeight))
        viewport.Controls.Add(root)
        Dim adjustViewport As Action = Sub()
            If root.IsDisposed OrElse viewport.IsDisposed Then Return
            Dim requiredWidth = If(stackedLayout, 780,
                                   CInt(Math.Ceiling(930 * uiScale)))
            Dim requiredHeight = headerRows.Sum() + statusHeight +
                                 actionsHeight + minimumGridHeight +
                                 root.Padding.Vertical
            Dim wantedWidth = Math.Max(requiredWidth, viewport.ClientSize.Width - 1)
            Dim wantedHeight = Math.Max(requiredHeight, viewport.ClientSize.Height - 1)
            If root.Width <> wantedWidth OrElse root.Height <> wantedHeight Then
                root.Size = New Size(wantedWidth, wantedHeight)
            End If
        End Sub
        AddHandler viewport.ClientSizeChanged, Sub(sender, e) adjustViewport()
        AddHandler Me.Shown, Sub(sender, e)
            adjustViewport()
            Me.BeginInvoke(New MethodInvoker(Sub()
                If Not viewport.IsDisposed Then viewport.AutoScrollPosition = Point.Empty
            End Sub))
        End Sub
        adjustViewport()

        Dim heading As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 2, .ColumnCount = 1
        }
        heading.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        heading.RowStyles.Add(New RowStyle(SizeType.Absolute,
            If(stackedLayout, CInt(Math.Ceiling(31 + 11 * (uiScale - 1))),
               CInt(Math.Ceiling(40 * uiScale)))))
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        Dim title = MakeLabel(If(stackedLayout,
                                 "COMPARE INDEXES  |  WHAT IS MISSING",
                                 "COMPARE IMAGE INDEXES  |  WHAT EXISTS / WHAT IS MISSING"),
                              13, True)
        title.Name = "lblIndexCompareTitle"
        heading.Controls.Add(title, 0, 0)
        Dim fileName = MakeLabel(Path.GetFileName(source.ImageFile) & "  |  " &
                                 source.ImageCount.ToString() &
                                 " indexes  |  Read-only comparison", 9, False)
        fileName.ForeColor = ModernTheme.Muted
        heading.Controls.Add(fileName, 0, 1)
        detailsTip = New ToolTip With {.AutoPopDelay = 18000}
        searchDelay = New System.Windows.Forms.Timer With {.Interval = 260}
        AddHandler searchDelay.Tick, Sub(sender, e)
            searchDelay.Stop()
            If Not Me.IsDisposed AndAlso comparison IsNot Nothing AndAlso Not working Then
                ApplyFilter()
            End If
        End Sub
        detailsTip.SetToolTip(fileName, source.ImageFile)
        root.Controls.Add(heading, 0, 0)

        Dim settings As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 2, .RowCount = 1,
            .Margin = New Padding(0, 3, 0, 7)
        }
        settings.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 54))
        settings.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 46))
        settings.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.Controls.Add(settings, 0, 1)

        Dim selectArea As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .Margin = New Padding(0, 0, 12, 0)
        }
        selectArea.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        selectArea.RowStyles.Add(New RowStyle(SizeType.Absolute,
            If(stackedLayout, CInt(Math.Ceiling(25 + 10 * (uiScale - 1))),
               CInt(Math.Ceiling(32 * uiScale)))))
        selectArea.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        selectArea.Controls.Add(MakeLabel("INDEXES TO COMPARE (CHOOSE 2–8)", 9.5F, True), 0, 0)
        selectedIndexes = New CheckedListBox With {
            .Name = "lst360CompareIndexes",
            .Dock = DockStyle.Fill, .CheckOnClick = True,
            .BorderStyle = BorderStyle.FixedSingle,
            .HorizontalScrollbar = True
        }
        Dim count As Integer = 0
        For Each image In report.Images.OrderBy(Function(x) x.Index)
            selectedIndexes.Items.Add(image, count < 3)
            count += 1
        Next
        selectArea.Controls.Add(selectedIndexes, 0, 1)
        settings.Controls.Add(selectArea, 0, 0)

        Dim options As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3
        }
        options.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        options.RowStyles.Add(New RowStyle(SizeType.Absolute,
            If(stackedLayout, CInt(Math.Ceiling(25 + 10 * (uiScale - 1))),
               CInt(Math.Ceiling(32 * uiScale)))))
        options.RowStyles.Add(New RowStyle(SizeType.Absolute,
            If(stackedLayout, CInt(Math.Ceiling(32 + 16 * (uiScale - 1))),
               CInt(Math.Ceiling(52 * uiScale)))))
        options.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        options.Controls.Add(MakeLabel("WHAT TO COMPARE", 9.5F, True), 0, 0)
        modeBox = New ComboBox With {
            .Name = "cmb360IndexCompareMode", .Dock = DockStyle.Fill,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Margin = New Padding(0, 2, 0, 8)
        }
        modeBox.Items.AddRange(New Object() {
            "Index metadata (no mounting)",
            "Files + folders (no mounting)",
            "Files + sizes (already mounted)",
            "Drivers / packages / features (audited)"
        })
        modeBox.SelectedIndex = 0
        options.Controls.Add(modeBox, 0, 1)
        Dim explanation = MakeLabel(
            "Paths: no mount. Sizes / SHA-256: mount. " &
            "Drivers / packages: scan.", 9, False)
        explanation.ForeColor = ModernTheme.Muted
        explanation.AutoEllipsis = False
        options.Controls.Add(explanation, 0, 2)
        settings.Controls.Add(options, 1, 0)

        Dim filters As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = If(stackedLayout, 2, 4),
            .RowCount = If(stackedLayout, 2, 1),
            .Margin = New Padding(0, 3, 0, 5)
        }
        If stackedLayout Then
            filters.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
            filters.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
            filters.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            filters.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        Else
            filters.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            filters.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute,
                CInt(Math.Ceiling(155 * uiScale))))
            filters.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute,
                CInt(Math.Ceiling(252 * uiScale))))
            filters.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            filters.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute,
                CInt(Math.Ceiling(112 * uiScale))))
        End If
        root.Controls.Add(filters, 0, 2)
        categoryBox = New ComboBox With {
            .Name = "cmb360IndexCompareType", .Dock = DockStyle.Fill,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Margin = New Padding(0, 2, 9, 2)
        }
        categoryBox.Items.Add("All types")
        categoryBox.SelectedIndex = 0
        filters.Controls.Add(categoryBox, 0, 0)
        stateBox = New ComboBox With {
            .Name = "cmb360IndexCompareState", .Dock = DockStyle.Fill,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Margin = New Padding(0, 2, 9, 2)
        }
        stateBox.Items.AddRange(New Object() {"All results", "Missing / only in some",
                                                 "Different", "Present in all",
                                                 "Ambiguous duplicates"})
        stateBox.SelectedIndex = 0
        filters.Controls.Add(stateBox, 1, 0)
        searchBox = New TextBox With {
            .Name = "txt360IndexCompareSearch", .Dock = DockStyle.Fill,
            .PlaceholderText = "Search paths, names, versions and values",
            .Margin = New Padding(0, 2, 8, 2)
        }
        filters.Controls.Add(searchBox, If(stackedLayout, 0, 2),
                             If(stackedLayout, 1, 0))
        Dim reset As New Button With {
            .Name = "btn360IndexCompareReset", .Text = "Reset",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 1, 0, 1)
        }
        ModernTheme.StyleButton(reset, "neutral")
        filters.Controls.Add(reset, If(stackedLayout, 1, 3),
                             If(stackedLayout, 1, 0))

        grid = New DataGridView With {
            .Name = "grid360IndexComparison", .Dock = DockStyle.Fill,
            .ReadOnly = True, .VirtualMode = True,
            .RowHeadersVisible = False, .AllowUserToAddRows = False,
            .AllowUserToDeleteRows = False, .MultiSelect = False,
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            .BackgroundColor = Color.White,
            .BorderStyle = BorderStyle.FixedSingle,
            .EnableHeadersVisualStyles = False,
            .ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            .ColumnHeadersHeight = Math.Max(36, Me.Font.Height + 17),
            .RowTemplate = New DataGridViewRow With {.Height = Math.Max(29, Me.Font.Height + 12)},
            .ShowCellToolTips = True
        }
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 242, 252)
        grid.ColumnHeadersDefaultCellStyle.ForeColor = ModernTheme.TextInk
        grid.ColumnHeadersDefaultCellStyle.Font = ModernTheme.UiFont(9.2F, True)
        grid.DefaultCellStyle.Font = ModernTheme.UiFont(9.2F)
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(216, 234, 253)
        grid.DefaultCellStyle.SelectionForeColor = ModernTheme.TextInk
        AddHandler grid.CellValueNeeded, AddressOf GridValueNeeded
        AddHandler grid.CellToolTipTextNeeded, AddressOf GridTooltipNeeded
        AddHandler grid.ClientSizeChanged, Sub(sender, e) FitIndexColumns()
        root.Controls.Add(grid, 0, 3)

        Dim statusPanel As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .Margin = New Padding(0, 4, 0, 4)
        }
        statusPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        statusPanel.RowStyles.Add(New RowStyle(SizeType.Absolute,
            If(stackedLayout, CInt(Math.Ceiling(28 + 11 * (uiScale - 1))),
               CInt(Math.Ceiling(31 * uiScale)))))
        statusPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        summaryLabel = MakeLabel("Select at least two indexes.", 9.4F, True)
        summaryLabel.Name = "lbl360IndexCompareSummary"
        statusPanel.Controls.Add(summaryLabel, 0, 0)
        hintLabel = MakeLabel("No image files will be modified.", 9, False)
        hintLabel.Name = "lbl360IndexCompareHint"
        hintLabel.ForeColor = ModernTheme.Muted
        hintLabel.AutoEllipsis = True
        statusPanel.Controls.Add(hintLabel, 0, 1)
        root.Controls.Add(statusPanel, 0, 4)

        Dim actions As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = If(stackedLayout, 4, 5),
            .RowCount = 1,
            .Margin = Padding.Empty
        }
        If stackedLayout Then
            actions.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            For Each pct As Single In {33.0F, 23.0F, 27.0F, 17.0F}
                actions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, pct))
            Next
        Else
            actions.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            actions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            For Each columnWidth As Integer In {220, 165, 180, 114}
                actions.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute,
                    CInt(Math.Ceiling(columnWidth * uiScale))))
            Next
        End If
        root.Controls.Add(actions, 0, 5)
        compareButton = New Button With {
            .Name = "btn360RunIndexCompare", .Text = If(stackedLayout, "Compare indexes", "Compare selected indexes"),
            .Dock = DockStyle.Fill, .Margin = New Padding(4, 7, 6, 4)
        }
        ModernTheme.StyleButton(compareButton, "primary")
        actions.Controls.Add(compareButton, If(stackedLayout, 0, 1), 0)
        hashButton = New Button With {
            .Name = "btn360IndexCompareHash", .Text = "Verify SHA-256",
            .Dock = DockStyle.Fill, .Margin = New Padding(4, 7, 6, 4),
            .Enabled = False
        }
        ModernTheme.StyleButton(hashButton, "secondary")
        actions.Controls.Add(hashButton, If(stackedLayout, 1, 2), 0)
        exportButton = New Button With {
            .Name = "btn360IndexCompareCsv", .Text = If(stackedLayout, "Export CSV", "Export filtered CSV"),
            .Dock = DockStyle.Fill, .Margin = New Padding(4, 7, 6, 4),
            .Enabled = False
        }
        ModernTheme.StyleButton(exportButton, "browse")
        actions.Controls.Add(exportButton, If(stackedLayout, 2, 3), 0)
        closeButton = New Button With {
            .Name = "btn360IndexCompareClose", .Text = "Close",
            .Dock = DockStyle.Fill, .Margin = New Padding(4, 7, 0, 4)
        }
        ModernTheme.StyleButton(closeButton, "neutral")
        actions.Controls.Add(closeButton, If(stackedLayout, 3, 4), 0)
        Me.CancelButton = closeButton

        AddHandler compareButton.Click, AddressOf CompareClicked
        AddHandler hashButton.Click, AddressOf HashClicked
        AddHandler exportButton.Click, AddressOf ExportClicked
        AddHandler closeButton.Click, Sub(sender, e)
            If job IsNot Nothing Then job.Cancel()
            Me.Close()
        End Sub
        AddHandler selectedIndexes.ItemCheck, Sub(sender, e)
            ' Comparisons are not silently recomputed when index selection changes.
            comparison = Nothing
            visibleRows.Clear()
            grid.RowCount = 0
            exportButton.Enabled = False
            hashButton.Enabled = False
            summaryLabel.Text = "Selection changed. Click Compare selected indexes."
        End Sub
        AddHandler modeBox.SelectedIndexChanged, Sub(sender, e)
            comparison = Nothing
            visibleRows.Clear()
            grid.RowCount = 0
            exportButton.Enabled = False
            hashButton.Enabled = False
            summaryLabel.Text = "Mode changed. Click Compare selected indexes."
        End Sub
        AddHandler categoryBox.SelectedIndexChanged, Sub(sender, e) ApplyFilter()
        AddHandler stateBox.SelectedIndexChanged, Sub(sender, e) ApplyFilter()
        AddHandler searchBox.TextChanged, Sub(sender, e)
            searchDelay.Stop()
            If Not working AndAlso comparison IsNot Nothing Then searchDelay.Start()
        End Sub
        AddHandler grid.SelectionChanged, Sub(sender, e) UpdateHashButton()
        AddHandler reset.Click, Sub(sender, e)
            categoryBox.SelectedIndex = 0
            stateBox.SelectedIndex = 0
            searchBox.Clear()
        End Sub
        AddHandler Me.FormClosing, Sub(sender, e)
            If job IsNot Nothing Then job.Cancel()
            searchDelay.Stop()
        End Sub
        AddHandler Me.FormClosed, Sub(sender, e)
            searchDelay.Stop()
            searchDelay.Dispose()
        End Sub
        WindowGeometry.Install(Me, "Software\DISMGUI\Wim360IndexComparison")

        ' A metadata diff needs no mount and is available as soon as the window opens.
        Dim defaults = report.Images.OrderBy(Function(x) x.Index).
            Take(Math.Min(3, report.ImageCount)).Select(Function(x) x.Index).ToArray()
        ShowComparison(Wim360IndexComparer.Metadata(report, defaults))
    End Sub

    Private Shared Function MakeLabel(caption As String, size As Single,
                                       bold As Boolean) As Label
        Return New Label With {
            .Text = caption, .Font = ModernTheme.UiFont(size, bold),
            .ForeColor = ModernTheme.TextInk,
            .Dock = DockStyle.Fill, .Margin = Padding.Empty,
            .TextAlign = ContentAlignment.MiddleLeft, .AutoEllipsis = True
        }
    End Function

    Private Function CurrentSelection() As Integer()
        Return selectedIndexes.CheckedItems.Cast(Of Wim360Reader.ImageInfo)().
            Select(Function(x) x.Index).OrderBy(Function(x) x).ToArray()
    End Function

    Private Async Sub CompareClicked(sender As Object, e As EventArgs)
        If working Then
            Try
                If job IsNot Nothing Then job.Cancel()
            Catch ex As ObjectDisposedException
            End Try
            compareButton.Enabled = False
            summaryLabel.Text = "Canceling the current comparison..."
            Return
        End If
        Dim indexes = CurrentSelection()
        If indexes.Length < 2 OrElse indexes.Length > 8 Then
            MessageBox.Show(Me, "Choose between 2 and 8 indexes to compare.",
                            "WIM 360 - Compare Indexes", MessageBoxButtons.OK,
                            MessageBoxIcon.Information)
            Return
        End If
        Dim mode = modeBox.SelectedIndex
        working = True
        compareButton.Text = "Cancel scan"
        compareButton.Enabled = True
        exportButton.Enabled = False
        hashButton.Enabled = False
        comparison = Nothing
        visibleRows.Clear()
        grid.RowCount = 0
        selectedIndexes.Enabled = False
        modeBox.Enabled = False
        summaryLabel.Text = "Comparing indexes " & String.Join(", ", indexes) & "..."
        hintLabel.Text = If(mode = 1,
            "Reading file/folder paths directly from WIM (Unicode-safe). No mounting or sizes.",
            "Read-only. Mounted file trees can take longer on large images.")
        Dim tokenSource As New CancellationTokenSource()
        job = tokenSource
        Dim progress As IProgress(Of String) = New Progress(Of String)(Sub(message)
            If Not Me.IsDisposed AndAlso Not tokenSource.IsCancellationRequested Then
                summaryLabel.Text = message
            End If
        End Sub)
        Try
            Dim result = Await Task.Run(Function()
                Select Case mode
                    Case 0
                        Return Wim360IndexComparer.Metadata(source, indexes)
                    Case 1
                        Return Wim360OfflineList.Compare(source, indexes,
                                                         tokenSource.Token, progress)
                    Case 2
                        Return Wim360IndexComparer.MountedFiles(source, indexes, tokenSource.Token)
                    Case 3
                        Return Wim360IndexComparer.AuditedInventory(source, indexes)
                    Case Else
                        Throw New InvalidOperationException("Unknown comparison mode.")
                End Select
            End Function)
            If Not Me.IsDisposed AndAlso Not tokenSource.IsCancellationRequested Then
                ShowComparison(result)
            End If
        Catch ex As OperationCanceledException
            If Not Me.IsDisposed Then summaryLabel.Text = "Comparison canceled."
        Catch ex As Exception
            ErrorDiagnostics.RememberFailure("WIM 360 multi-index compare", ex)
            If Not Me.IsDisposed Then
                summaryLabel.Text = "Comparison unavailable. No images were modified."
                hintLabel.Text = ex.Message
                detailsTip.SetToolTip(hintLabel, ex.Message)
            End If
        Finally
            If Object.ReferenceEquals(job, tokenSource) Then job = Nothing
            tokenSource.Dispose()
            working = False
            If Not Me.IsDisposed Then
                compareButton.Text = If(Me.Font.Height / 17.0F > 1.15F,
                                        "Compare indexes", "Compare selected indexes")
                compareButton.Enabled = True
                selectedIndexes.Enabled = True
                modeBox.Enabled = True
            End If
        End Try
    End Sub

    Private Sub UpdateHashButton()
        hashButton.Enabled = Not working AndAlso
            comparison IsNot Nothing AndAlso
            comparison.Mode = "Mounted file paths / sizes" AndAlso
            grid.SelectedRows.Count > 0 AndAlso grid.SelectedRows(0).Index >= 0 AndAlso
            grid.SelectedRows(0).Index < visibleRows.Count AndAlso
            visibleRows(grid.SelectedRows(0).Index).Kind = "Files"
    End Sub

    Private Async Sub HashClicked(sender As Object, e As EventArgs)
        If working OrElse comparison Is Nothing OrElse
           comparison.Mode <> "Mounted file paths / sizes" OrElse
           grid.SelectedRows.Count = 0 Then Return
        Dim rowNumber = grid.SelectedRows(0).Index
        If rowNumber < 0 OrElse rowNumber >= visibleRows.Count Then Return
        Dim original = visibleRows(rowNumber)
        Dim selected = comparison.Indexes.ToArray()
        working = True
        compareButton.Text = "Cancel scan"
        compareButton.Enabled = True
        hashButton.Enabled = False
        exportButton.Enabled = False
        selectedIndexes.Enabled = False
        modeBox.Enabled = False
        summaryLabel.Text = "Computing SHA-256 for " & original.Name & "..."
        Dim tokenSource As New CancellationTokenSource()
        job = tokenSource
        Try
            Dim verified = Await Task.Run(Function()
                Return Wim360IndexComparer.VerifyFileHashes(
                    source, selected, original, tokenSource.Token)
            End Function)
            If Me.IsDisposed OrElse tokenSource.IsCancellationRequested Then Return
            Dim position = comparison.Rows.FindIndex(Function(x)
                Return x.Kind = original.Kind AndAlso x.Name = original.Name
            End Function)
            If position >= 0 Then comparison.Rows(position) = verified
            ApplyFilter()
            hintLabel.Text = "SHA-256 verified for selected path. " &
                "Other paths were compared by presence and size only."
        Catch ex As OperationCanceledException
            If Not Me.IsDisposed Then summaryLabel.Text = "Hash verification canceled."
        Catch ex As Exception
            ErrorDiagnostics.RememberFailure("WIM 360 SHA-256 verification", ex)
            If Not Me.IsDisposed Then
                summaryLabel.Text = "Unable to verify SHA-256."
                hintLabel.Text = ex.Message
                detailsTip.SetToolTip(hintLabel, ex.Message)
            End If
        Finally
            If Object.ReferenceEquals(job, tokenSource) Then job = Nothing
            tokenSource.Dispose()
            working = False
            If Not Me.IsDisposed Then
                compareButton.Text = If(Me.Font.Height / 17.0F > 1.15F,
                                        "Compare indexes", "Compare selected indexes")
                compareButton.Enabled = True
                exportButton.Enabled = comparison IsNot Nothing
                selectedIndexes.Enabled = True
                modeBox.Enabled = True
                UpdateHashButton()
            End If
        End Try
    End Sub

    ' Exposed only so the Windows LayoutSmoke suite can test the real grid.
    Public Sub PreviewForTest(result As Wim360IndexComparer.Result)
        ShowComparison(result)
    End Sub

    Private Sub ShowComparison(result As Wim360IndexComparer.Result)
        searchDelay.Stop()
        comparison = result
        grid.SuspendLayout()
        Try
            grid.RowCount = 0
            grid.Columns.Clear()
            AddColumn("Kind", 115)
            AddColumn("Item / relative path", 350)
            AddColumn("Comparison", 168)
            For Each index In result.Indexes
                AddColumn("Index " & index.ToString(), 178)
            Next
        Finally
            grid.ResumeLayout(True)
        End Try
        FitIndexColumns()
        categoryBox.Items.Clear()
        categoryBox.Items.Add("All types")
        For Each kind In result.Rows.Select(Function(x) x.Kind).
                         Distinct(StringComparer.OrdinalIgnoreCase).
                         OrderBy(Function(x) x, StringComparer.OrdinalIgnoreCase)
            categoryBox.Items.Add(kind)
        Next
        categoryBox.SelectedIndex = 0
        stateBox.Items.Clear()
        stateBox.Items.AddRange(New Object() {
            "All results", "Missing / only in some",
            "Different", "Exists in all", "Present in all",
            "Ambiguous duplicates"
        })
        For Each index In result.Indexes
            stateBox.Items.Add("Only in Index " & index.ToString())
            stateBox.Items.Add("Missing from Index " & index.ToString())
            stateBox.Items.Add("Present in Index " & index.ToString())
        Next
        stateBox.SelectedIndex = 0
        searchBox.Clear()
        exportButton.Enabled = True
        ApplyFilter()
        UpdateHashButton()
        hintLabel.Text = If(String.IsNullOrWhiteSpace(result.Notes),
            "Read-only. Missing means an item is absent from a complete comparison.",
            result.Notes)
        detailsTip.SetToolTip(hintLabel, hintLabel.Text)
    End Sub

    ' Keep all three compared indexes on screen at ordinary dialog widths.
    ' Four to eight indexes retain horizontal scrolling, as necessary.
    Private Sub FitIndexColumns()
        If grid.IsDisposed OrElse grid.Columns.Count < 4 OrElse
           comparison Is Nothing OrElse comparison.Indexes.Count > 3 Then Return
        Dim available As Integer = grid.ClientSize.Width -
                                   SystemInformation.VerticalScrollBarWidth - 8
        If available <= 540 Then Return
        Dim kindWidth As Integer = Math.Max(86, grid.Font.Height * 4)
        Dim pathWidth As Integer = Math.Max(214, grid.Font.Height * 8)
        Dim stateWidth As Integer = Math.Max(144, grid.Font.Height * 6)
        Dim count As Integer = comparison.Indexes.Count
        Dim perIndex As Integer = (available - kindWidth - pathWidth -
                                   stateWidth) \ count
        If perIndex < 105 Then
            pathWidth = Math.Max(160, pathWidth - (105 - perIndex) * count)
            perIndex = (available - kindWidth - pathWidth - stateWidth) \ count
        End If
        If perIndex < 92 Then Return
        grid.Columns(0).Width = kindWidth
        grid.Columns(1).Width = pathWidth
        grid.Columns(2).Width = stateWidth
        For i As Integer = 0 To count - 1
            grid.Columns(3 + i).Width = perIndex
        Next
    End Sub

    Private Sub AddColumn(caption As String, width As Integer)
        Dim column As New DataGridViewTextBoxColumn With {
            .HeaderText = caption, .Name = "col360IdxCompare" & grid.Columns.Count.ToString(),
            .Width = width, .MinimumWidth = 82, .SortMode = DataGridViewColumnSortMode.NotSortable
        }
        grid.Columns.Add(column)
    End Sub

    Private Sub ApplyFilter()
        If comparison Is Nothing Then Return
        visibleRows = Wim360IndexComparer.Filter(comparison.Rows,
            Convert.ToString(categoryBox.SelectedItem),
            Convert.ToString(stateBox.SelectedItem), searchBox.Text,
            comparison.Indexes.Count)
        grid.RowCount = visibleRows.Count
        UpdateHashButton()
        Dim missing = visibleRows.Where(Function(x)
            Return x.State = "Missing in some" OrElse
                   x.State.StartsWith("Only in Index ", StringComparison.Ordinal)
        End Function).Count()
        Dim changed = visibleRows.Where(Function(x) x.State = "Different").Count()
        summaryLabel.Text = visibleRows.Count.ToString("N0") & " of " &
            comparison.Rows.Count.ToString("N0") & " rows  |  " &
            missing.ToString("N0") & " missing/unique  |  " &
            changed.ToString("N0") & " different  |  Mode: " & comparison.Mode
    End Sub

    Private Sub GridValueNeeded(sender As Object, e As DataGridViewCellValueEventArgs)
        If e.RowIndex < 0 OrElse e.RowIndex >= visibleRows.Count Then Return
        Dim item = visibleRows(e.RowIndex)
        Select Case e.ColumnIndex
            Case 0 : e.Value = item.Kind
            Case 1 : e.Value = item.Name
            Case 2 : e.Value = item.State
            Case Else
                If comparison IsNot Nothing AndAlso e.ColumnIndex - 3 < comparison.Indexes.Count Then
                    e.Value = item.At(comparison.Indexes(e.ColumnIndex - 3))
                End If
        End Select
    End Sub

    Private Sub GridTooltipNeeded(sender As Object,
                                  e As DataGridViewCellToolTipTextNeededEventArgs)
        If e.RowIndex < 0 OrElse e.RowIndex >= visibleRows.Count Then Return
        Dim item = visibleRows(e.RowIndex)
        Select Case e.ColumnIndex
            Case 0 : e.ToolTipText = item.Kind
            Case 1 : e.ToolTipText = item.Name
            Case 2 : e.ToolTipText = item.State & If(item.Notes.Length > 0,
                                                      Environment.NewLine & item.Notes, "")
            Case Else
                If comparison IsNot Nothing AndAlso e.ColumnIndex - 3 < comparison.Indexes.Count Then
                    e.ToolTipText = item.At(comparison.Indexes(e.ColumnIndex - 3))
                End If
        End Select
    End Sub

    Private Sub ExportClicked(sender As Object, e As EventArgs)
        If comparison Is Nothing Then Return
        Using dialog As New SaveFileDialog With {
            .Title = "Export the filtered multi-index comparison",
            .Filter = "CSV comparison (*.csv)|*.csv",
            .DefaultExt = "csv", .AddExtension = True,
            .OverwritePrompt = True, .RestoreDirectory = True,
            .FileName = Path.GetFileNameWithoutExtension(source.ImageFile) &
                        "-compare-indexes.csv"
        }
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Using writer As New StreamWriter(dialog.FileName, False, New UTF8Encoding(True))
                    Wim360IndexComparer.WriteCsv(writer, comparison, visibleRows)
                End Using
                hintLabel.Text = "Saved " & visibleRows.Count.ToString("N0") &
                    " filtered rows. Source WIM not changed."
            Catch ex As Exception
                ErrorDiagnostics.RememberFailure("WIM 360 compare export", ex)
                MessageBox.Show(Me, "Could not export comparison: " & ex.Message,
                    "WIM 360", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Using
    End Sub
End Class
