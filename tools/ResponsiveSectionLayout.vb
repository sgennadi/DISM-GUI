Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

' Resizes legacy sections while preserving their original DISM event handlers.
' The Mount workspace uses a real grid: WIM path, image index, and buttons
' can no longer overlap because each occupies a separate layout cell.
Public NotInheritable Class ResponsiveSectionLayout
    Private Sub New()
    End Sub

    Private NotInheritable Class PageLayout
        Public ReadOnly Page As TabPage
        Public ReadOnly Original As New Dictionary(Of Control, Rectangle)()
        Public ReadOnly Groups As New Dictionary(Of Control, Dictionary(Of Control, Rectangle))()
        Public IsUpdating As Boolean
        Public LastWidth As Integer = -1

        Public Sub New(value As TabPage)
            Page = value
            For Each child As Control In Page.Controls
                Original(child) = child.Bounds
                If TypeOf child Is GroupBox Then
                    Dim positions As New Dictionary(Of Control, Rectangle)()
                    For Each nested As Control In child.Controls
                        positions(nested) = nested.Bounds
                    Next
                    Groups(child) = positions
                End If
            Next
        End Sub
    End Class

    Private Shared ReadOnly Pages As New Dictionary(Of TabPage, PageLayout)()

    Private Shared Function Take(page As TabPage, name As String) As Control
        Dim c = page.Controls(name)
        If c Is Nothing Then Throw New InvalidOperationException("Missing original DISM control: " & name)
        page.Controls.Remove(c)
        c.Dock = DockStyle.Fill
        c.Margin = New Padding(0, 3, 5, 4)
        Return c
    End Function

    Private Shared Sub AddLabel(parent As TableLayoutPanel, text As String,
                                column As Integer, row As Integer)
        parent.Controls.Add(New Label With {
            .Text = text, .Font = ModernTheme.UiFont(10, True),
            .ForeColor = ModernTheme.TextInk,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Dock = DockStyle.Fill, .Margin = Padding.Empty
        }, column, row)
    End Sub

    Private Shared Sub BuildMountPage(page As TabPage)
        page.SuspendLayout()
        Dim wim = Take(page, "txtWIM")
        Dim mount = Take(page, "txtMount")
        Dim chooseWim = Take(page, "btnOpenWIM")
        Dim chooseMount = Take(page, "btnOpenMount")
        Dim imageIndex = DirectCast(Take(page, "cmbIndex"), ComboBox)
        Dim info = Take(page, "btnDisplayWIMInfo")
        Dim readOnly = Take(page, "chkMountReadOnly")
        Dim mountButton = DirectCast(Take(page, "btnMount"), Button)
        Dim dismountButton = DirectCast(Take(page, "btnDismount"), Button)
        Dim openMounted = DirectCast(Take(page, "btnOpenFolder"), Button)

        ' Remove original fixed-position labels, replacing them with measured
        ' grid labels. Names are not used by the existing DISM command handlers.
        For Each name In {"Label1", "Label2", "Label3"}
            Dim old = page.Controls(name)
            If old IsNot Nothing Then
                page.Controls.Remove(old)
                old.Dispose()
            End If
        Next

        page.AutoScroll = False
        Dim root As New TableLayoutPanel With {
            .Name = "MountResponsiveRoot",
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
            .Padding = New Padding(17, 18, 14, 13),
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 208))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        page.Controls.Add(root)

        Dim left As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 7,
            .Margin = New Padding(0, 0, 22, 0), .Padding = Padding.Empty,
            .BackColor = Color.White
        }
        left.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each height In {29, 48, 61, 29, 48, 45}
            left.RowStyles.Add(New RowStyle(SizeType.Absolute, height))
        Next
        left.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.Controls.Add(left, 0, 0)

        AddLabel(left, "WIM image", 0, 0)
        Dim fileRow As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        fileRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        fileRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        fileRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 157))
        left.Controls.Add(fileRow, 0, 1)
        fileRow.Controls.Add(wim, 0, 0)
        chooseWim.Margin = New Padding(7, 3, 0, 4)
        fileRow.Controls.Add(chooseWim, 1, 0)

        Dim indexRow As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1,
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        indexRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 116))
        indexRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 100))
        indexRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        indexRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 185))
        indexRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        left.Controls.Add(indexRow, 0, 2)
        AddLabel(indexRow, "Image index", 0, 0)
        imageIndex.Margin = New Padding(0, 10, 8, 13)
        imageIndex.DropDownWidth = 112
        imageIndex.Font = ModernTheme.UiFont(10, True)
        imageIndex.FlatStyle = FlatStyle.Flat
        indexRow.Controls.Add(imageIndex, 1, 0)
        info.Margin = New Padding(6, 9, 0, 12)
        indexRow.Controls.Add(info, 3, 0)

        AddLabel(left, "Mount folder", 0, 3)
        Dim mountRow As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        mountRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        mountRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        mountRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 157))
        left.Controls.Add(mountRow, 0, 4)
        mountRow.Controls.Add(mount, 0, 0)
        chooseMount.Margin = New Padding(7, 3, 0, 4)
        mountRow.Controls.Add(chooseMount, 1, 0)

        readOnly.Margin = New Padding(3, 12, 0, 6)
        left.Controls.Add(readOnly, 0, 5)

        Dim actions As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 4,
            .Margin = New Padding(0, 25, 0, 0), .Padding = Padding.Empty,
            .BackColor = Color.White
        }
        actions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each size In {61, 61, 61}
            actions.RowStyles.Add(New RowStyle(SizeType.Absolute, size))
        Next
        actions.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.Controls.Add(actions, 1, 0)

        mountButton.Text = "Mount WIM"
        ModernTheme.StyleButton(mountButton, "primary")
        mountButton.Dock = DockStyle.Fill
        mountButton.Margin = New Padding(0, 4, 0, 8)
        actions.Controls.Add(mountButton, 0, 0)
        dismountButton.Text = "Dismount WIM"
        ModernTheme.StyleButton(dismountButton, "secondary")
        dismountButton.Dock = DockStyle.Fill
        dismountButton.Margin = New Padding(0, 4, 0, 8)
        actions.Controls.Add(dismountButton, 0, 1)
        openMounted.Text = "Open mounted folder"
        ModernTheme.StyleButton(openMounted, "neutral")
        openMounted.Dock = DockStyle.Fill
        openMounted.Margin = New Padding(0, 4, 0, 8)
        actions.Controls.Add(openMounted, 0, 2)

        page.ResumeLayout(True)
    End Sub

    Public Shared Sub Install(tabs As TabControl)
        Pages.Clear()
        For Each page As TabPage In tabs.TabPages
            If page.Name = "TabPage1" Then
                BuildMountPage(page)
            Else
                Dim layout As New PageLayout(page)
                Pages(page) = layout
                page.AutoScroll = True
                AddHandler page.SizeChanged, Sub(sender, e) UpdateLayout(layout)
            End If
        Next
        AddHandler tabs.SizeChanged, Sub(sender, e) UpdateVisible(tabs)
        AddHandler tabs.SelectedIndexChanged, Sub(sender, e) UpdateVisible(tabs)
        tabs.BeginInvoke(New MethodInvoker(Sub() UpdateVisible(tabs)))
    End Sub

    Private Shared Sub UpdateVisible(tabs As TabControl)
        If tabs.IsDisposed OrElse tabs.SelectedTab Is Nothing Then Return
        Dim layout As PageLayout = Nothing
        If Pages.TryGetValue(tabs.SelectedTab, layout) Then UpdateLayout(layout)
    End Sub

    Private Shared Sub MoveResize(control As Control, x As Integer, y As Integer,
                                  width As Integer, height As Integer)
        If control.IsDisposed Then Return
        Dim target As New Rectangle(Math.Max(0, x), Math.Max(0, y),
                                    Math.Max(30, width), Math.Max(15, height))
        If control.Bounds <> target Then control.Bounds = target
    End Sub

    Private Shared Function IsActionButton(control As Control, baseRect As Rectangle) As Boolean
        Return TypeOf control Is Button AndAlso baseRect.X >= 510
    End Function

    Private Shared Function IsBrowse(control As Control) As Boolean
        If Not TypeOf control Is Button Then Return False
        Dim name = control.Name.ToLowerInvariant()
        Return name.Contains("browse") OrElse name.Contains("choose") OrElse
               name.Contains("openmount") OrElse name.Contains("openwim") OrElse
               name.Contains("opndriver") OrElse name.Contains("openpackage") OrElse
               name.Contains("capturesrc") OrElse name.Contains("capturedest")
    End Function

    Private Shared Sub UpdateLayout(layout As PageLayout)
        If layout.IsUpdating OrElse layout.Page.IsDisposed Then Return
        Dim width = layout.Page.ClientSize.Width
        If width < 320 OrElse Math.Abs(width - layout.LastWidth) < 2 Then Return
        layout.IsUpdating = True
        Try
            Dim page = layout.Page
            page.SuspendLayout()
            Dim compact = width < 725
            Dim rightX = If(compact, 10, Math.Max(525, width - 195))
            Dim leftWidth = If(compact, width - 20, rightX - 24)
            Dim browseX = If(compact, Math.Max(295, width - 186),
                              Math.Max(350, leftWidth - 181))
            Dim compactStart = 395
            If compact Then
                For Each originalControl In layout.Original
                    If Not IsActionButton(originalControl.Key, originalControl.Value) Then
                        compactStart = Math.Max(compactStart, originalControl.Value.Bottom + 27)
                    End If
                Next
            End If

            For Each kv In layout.Original
                Dim control = kv.Key
                Dim rect = kv.Value
                Dim x = rect.X
                Dim y = rect.Y
                Dim w = rect.Width
                Dim h = rect.Height
                If IsActionButton(control, rect) Then
                    If compact Then
                        Dim sequence As Integer = 0
                        For Each other In layout.Original
                            If IsActionButton(other.Key, other.Value) AndAlso
                               (other.Value.Y < rect.Y OrElse
                               (other.Value.Y = rect.Y AndAlso
                                String.CompareOrdinal(other.Key.Name, control.Name) < 0)) Then
                                sequence += 1
                            End If
                        Next
                        Dim columnWidth = Math.Max(135, (width - 42) \ 2)
                        x = 12 + (sequence Mod 2) * (columnWidth + 12)
                        y = compactStart + (sequence \ 2) * 61
                        w = columnWidth
                        h = 53
                    Else
                        ' Full-width action column with room for long labels.
                        x = rightX
                        w = Math.Min(175, Math.Max(144, width - rightX - 12))
                        h = Math.Max(54, h)
                    End If
                ElseIf TypeOf control Is GroupBox Then
                    w = Math.Max(rect.Width, leftWidth - rect.X)
                ElseIf IsBrowse(control) AndAlso rect.X >= 320 Then
                    x = browseX
                    w = Math.Min(176, Math.Max(142, leftWidth - browseX - 7))
                    h = Math.Max(28, h)
                ElseIf TypeOf control Is TextBox Then
                    If rect.X <= 110 Then
                        w = Math.Max(rect.Width, browseX - rect.X - 13)
                    End If
                    h = Math.Max(23, rect.Height)
                ElseIf TypeOf control Is ComboBox Then
                    w = Math.Max(rect.Width, 63)
                    h = Math.Max(25, rect.Height)
                ElseIf TypeOf control Is Label Then
                    Dim label = DirectCast(control, Label)
                    label.AutoEllipsis = True
                    If label.AutoSize Then Continue For
                End If
                MoveResize(control, x, y, w, h)
            Next

            For Each pair In layout.Groups
                Dim parent = pair.Key
                Dim source = pair.Value
                Dim actualInnerWidth = parent.ClientSize.Width
                If actualInnerWidth < 100 Then Continue For
                Dim nestedButtonX = actualInnerWidth - 191
                For Each kv In source
                    Dim child = kv.Key
                    Dim r = kv.Value
                    Dim x = r.X
                    Dim y = r.Y
                    Dim w = r.Width
                    Dim h = r.Height
                    If TypeOf child Is Button AndAlso r.X >= 300 Then
                        x = nestedButtonX
                        w = Math.Max(150, actualInnerWidth - x - 12)
                        h = Math.Max(28, h)
                    ElseIf TypeOf child Is TextBox Then
                        w = Math.Max(r.Width, nestedButtonX - r.X - 12)
                        h = Math.Max(23, h)
                    ElseIf TypeOf child Is Label Then
                        DirectCast(child, Label).AutoEllipsis = True
                    End If
                    If Not TypeOf child Is Label OrElse Not DirectCast(child, Label).AutoSize Then
                        MoveResize(child, x, y, w, h)
                    End If
                Next
            Next

            layout.LastWidth = width
            page.ResumeLayout(True)
        Finally
            layout.IsUpdating = False
        End Try
    End Sub
End Class
