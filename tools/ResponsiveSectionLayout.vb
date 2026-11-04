Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

' Responsive layout for the nine legacy DISM GUI pages.
' The upstream designer coordinates target a 655px page. Adapt widths and
' group internals without changing any existing event handler or command.
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

    Public Shared Sub Install(tabs As TabControl)
        Pages.Clear()
        For Each page As TabPage In tabs.TabPages
            Dim layout As New PageLayout(page)
            Pages(page) = layout
            page.AutoScroll = True
            AddHandler page.SizeChanged, Sub(sender, e) UpdateLayout(layout)
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
            Dim rightX = Math.Max(525, width - 195)
            Dim leftWidth = rightX - 24
            Dim browseX = Math.Max(356, leftWidth - 146)

            For Each kv In layout.Original
                Dim control = kv.Key
                Dim rect = kv.Value
                Dim x = rect.X
                Dim y = rect.Y
                Dim w = rect.Width
                Dim h = rect.Height
                If IsActionButton(control, rect) Then
                    ' Full-width action column with room for long labels.
                    x = rightX
                    w = Math.Min(175, Math.Max(144, width - rightX - 12))
                    h = Math.Max(54, h)
                ElseIf TypeOf control Is GroupBox Then
                    w = Math.Max(rect.Width, leftWidth - rect.X)
                ElseIf IsBrowse(control) AndAlso rect.X >= 320 Then
                    x = browseX
                    w = Math.Min(140, Math.Max(118, leftWidth - browseX - 7))
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
                    If label.AutoSize Then
                        ' Keep labels at their designer origin; their natural height
                        ' is already auto-scaled by the parent.
                        Continue For
                    End If
                End If
                MoveResize(control, x, y, w, h)
            Next

            For Each pair In layout.Groups
                Dim parent = pair.Key
                Dim source = pair.Value
                Dim actualInnerWidth = parent.ClientSize.Width
                If actualInnerWidth < 100 Then Continue For
                Dim nestedButtonX = actualInnerWidth - 169
                For Each kv In source
                    Dim child = kv.Key
                    Dim r = kv.Value
                    Dim x = r.X
                    Dim y = r.Y
                    Dim w = r.Width
                    Dim h = r.Height
                    If TypeOf child Is Button AndAlso r.X >= 300 Then
                        x = nestedButtonX
                        w = Math.Max(125, actualInnerWidth - x - 12)
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

            ' Mount and apply pages need an index selector next to WIM actions;
            ' keep the index in a dedicated 66px slot before the main buttons.
            If page.Name = "TabPage1" Then
                Dim indexLabel = page.Controls("Label3")
                Dim indexCombo = page.Controls("cmbIndex")
                If indexLabel IsNot Nothing Then
                    MoveResize(indexLabel, rightX - 77, 5, 70, 19)
                End If
                If indexCombo IsNot Nothing Then
                    MoveResize(indexCombo, rightX - 77, 25, 62, 27)
                End If
            End If

            layout.LastWidth = width
            page.ResumeLayout(True)
        Finally
            layout.IsUpdating = False
        End Try
    End Sub
End Class
