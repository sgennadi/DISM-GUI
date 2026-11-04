Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

' Modern, responsive forms for the eight servicing pages besides Mount.
' NO fixed legacy coordinates, and NO changes to DISM handlers or control identities.
Public NotInheritable Class ResponsiveForms
    Private Sub New()
    End Sub

    Private NotInheritable Class PageBuilder
        Private ReadOnly page As TabPage
        Private ReadOnly root As TableLayoutPanel
        Private ReadOnly leftPanel As FlowLayoutPanel
        Private ReadOnly rightPanel As FlowLayoutPanel
        Private ReadOnly originalButtons As New List(Of Button)()
        Private ReadOnly leftItems As New List(Of Control)()
        Private ReadOnly rightItems As New List(Of Button)()
        Private isResizing As Boolean

        Public Sub New(target As TabPage)
            page = target
            page.SuspendLayout()
            page.AutoScroll = False
            page.BackColor = Color.White
            page.UseVisualStyleBackColor = False

            root = New TableLayoutPanel With {
                .Name = "SectionResponsiveRoot",
                .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
                .Margin = Padding.Empty, .Padding = New Padding(14, 14, 13, 10),
                .BackColor = Color.White
            }
            root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            root.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 210))
            root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

            leftPanel = New FlowLayoutPanel With {
                .Name = "SectionFields",
                .FlowDirection = FlowDirection.TopDown,
                .WrapContents = False, .AutoScroll = True,
                .Dock = DockStyle.Fill, .BackColor = Color.White,
                .Padding = New Padding(0, 0, 9, 12),
                .Margin = New Padding(0, 0, 15, 0)
            }
            rightPanel = New FlowLayoutPanel With {
                .Name = "SectionActions",
                .FlowDirection = FlowDirection.TopDown,
                .WrapContents = False, .AutoScroll = True,
                .Dock = DockStyle.Fill, .BackColor = Color.White,
                .Padding = New Padding(9, 5, 6, 10),
                .Margin = Padding.Empty
            }
            root.Controls.Add(leftPanel, 0, 0)
            root.Controls.Add(rightPanel, 1, 0)

            AddHandler leftPanel.ClientSizeChanged, AddressOf LeftResized
            AddHandler rightPanel.ClientSizeChanged, AddressOf RightResized
            AddHandler root.ClientSizeChanged, AddressOf RootResized
        End Sub

        Private Function Extract(name As String) As Control
            Dim matches = page.Controls.Find(name, True)
            If matches.Length <> 1 Then
                Throw New InvalidOperationException(
                    "DISM GUI " & page.Name & ": expected 1 control named " &
                    name & ", found " & matches.Length.ToString())
            End If
            Dim control = matches(0)
            If control.Parent Is Nothing Then
                Throw New InvalidOperationException("Control has no parent: " & name)
            End If
            control.Parent.Controls.Remove(control)
            control.Dock = DockStyle.Fill
            control.Margin = New Padding(0, 4, 4, 5)
            Return control
        End Function

        Private Shared Function NewSectionTitle(caption As String) As Label
            Dim font = ModernTheme.UiFont(10.7F, True)
            Return New Label With {
                .Text = caption.ToUpperInvariant(), .Font = font,
                .ForeColor = ModernTheme.Blue, .BackColor = Color.White,
                .Height = Math.Max(38, font.Height + 19), .Width = 350,
                .Margin = New Padding(0, 11, 0, 1),
                .TextAlign = ContentAlignment.MiddleLeft, .AutoEllipsis = False
            }
        End Function

        Private Sub PutLeft(item As Control)
            leftItems.Add(item)
            leftPanel.Controls.Add(item)
        End Sub

        Public Sub Section(caption As String)
            PutLeft(NewSectionTitle(caption))
        End Sub

        Public Sub Field(caption As String, fieldName As String,
                         Optional browseName As String = Nothing,
                         Optional browseCaption As String = Nothing)
            Dim input = Extract(fieldName)
            Dim browse As Button = Nothing
            If Not String.IsNullOrWhiteSpace(browseName) Then
                browse = DirectCast(Extract(browseName), Button)
                Dim label = If(String.IsNullOrWhiteSpace(browseCaption),
                               browse.Text.Replace("&", ""), browseCaption)
                browse.Text = label
                browse.AutoEllipsis = False
                ModernTheme.StyleButton(browse, "browse")
                browse.Font = ModernTheme.UiFont(9.3F, True)
                browse.TextAlign = ContentAlignment.MiddleCenter
            End If

            Dim font = ModernTheme.UiFont(10.2F)
            Dim labelFont = ModernTheme.UiFont(10, True)
            Dim headingHeight = Math.Max(26, labelFont.Height + 7)
            Dim inputHeight = Math.Max(48, font.Height + 24)
            If browse IsNot Nothing Then
                Dim bounds = TextRenderer.MeasureText(browse.Text, browse.Font,
                    New Size(138, Integer.MaxValue),
                    TextFormatFlags.WordBreak Or TextFormatFlags.TextBoxControl)
                inputHeight = Math.Max(inputHeight, bounds.Height + 18)
            End If

            Dim item As New TableLayoutPanel With {
                .Name = "field_" & fieldName, .Width = 420,
                .Height = headingHeight + inputHeight + 8,
                .RowCount = 2, .ColumnCount = 1,
                .BackColor = Color.White, .Margin = New Padding(0, 0, 0, 7),
                .Padding = Padding.Empty
            }
            item.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            item.RowStyles.Add(New RowStyle(SizeType.Absolute, headingHeight))
            item.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            Dim label As New Label With {
                .Text = caption, .Font = labelFont, .ForeColor = ModernTheme.TextInk,
                .BackColor = Color.White, .Dock = DockStyle.Fill,
                .Margin = Padding.Empty, .TextAlign = ContentAlignment.MiddleLeft,
                .AutoEllipsis = False
            }
            item.Controls.Add(label, 0, 0)
            Dim inputRow As New TableLayoutPanel With {
                .Dock = DockStyle.Fill, .ColumnCount = If(browse Is Nothing, 1, 2),
                .RowCount = 1, .BackColor = Color.White, .Margin = Padding.Empty,
                .Padding = Padding.Empty
            }
            inputRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            inputRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            If browse IsNot Nothing Then
                inputRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 158))
            End If

            input.Font = font
            input.ForeColor = ModernTheme.TextInk
            input.BackColor = Color.White
            input.Margin = New Padding(0, 5, If(browse Is Nothing, 0, 5), 6)
            If TypeOf input Is TextBox Then
                DirectCast(input, TextBox).BorderStyle = BorderStyle.FixedSingle
            ElseIf TypeOf input Is MaskedTextBox Then
                DirectCast(input, MaskedTextBox).BorderStyle = BorderStyle.FixedSingle
            ElseIf TypeOf input Is ComboBox Then
                DirectCast(input, ComboBox).FlatStyle = FlatStyle.Flat
            End If
            inputRow.Controls.Add(input, 0, 0)
            If browse IsNot Nothing Then
                browse.Margin = New Padding(5, 4, 0, 5)
                inputRow.Controls.Add(browse, 1, 0)
            End If
            item.Controls.Add(inputRow, 0, 1)
            PutLeft(item)
        End Sub

        Public Sub Check(name As String, Optional caption As String = Nothing)
            Dim box = DirectCast(Extract(name), CheckBox)
            If Not String.IsNullOrWhiteSpace(caption) Then box.Text = caption
            box.Dock = DockStyle.None
            box.Width = 370
            box.Height = Math.Max(36, box.Font.Height + 17)
            box.AutoSize = False
            box.Font = ModernTheme.UiFont(10.2F)
            box.ForeColor = ModernTheme.TextInk
            box.BackColor = Color.White
            box.Margin = New Padding(2, 2, 0, 5)
            PutLeft(box)
        End Sub

        Public Sub Action(name As String, Optional caption As String = Nothing,
                          Optional role As String = "secondary")
            Dim button = DirectCast(Extract(name), Button)
            If Not String.IsNullOrWhiteSpace(caption) Then button.Text = caption
            ModernTheme.StyleButton(button, role)
            button.Font = ModernTheme.UiFont(10, True)
            button.TextAlign = ContentAlignment.MiddleCenter
            button.AutoEllipsis = False
            button.Dock = DockStyle.None
            button.Margin = New Padding(3, 0, 0, 12)
            button.Width = 186
            Dim m = TextRenderer.MeasureText(button.Text, button.Font,
                        New Size(163, Integer.MaxValue),
                        TextFormatFlags.WordBreak Or TextFormatFlags.TextBoxControl)
            button.Height = Math.Max(56, m.Height + 20)
            rightItems.Add(button)
            rightPanel.Controls.Add(button)
        End Sub

        Private Sub LeftResized(sender As Object, e As EventArgs)
            If isResizing Then Return
            isResizing = True
            Try
                Dim width = Math.Max(220, leftPanel.ClientSize.Width -
                                 leftPanel.Padding.Horizontal -
                                 SystemInformation.VerticalScrollBarWidth - 5)
                For Each item In leftItems
                    If item.Width <> width Then item.Width = width
                Next
            Finally
                isResizing = False
            End Try
        End Sub

        Private Sub RightResized(sender As Object, e As EventArgs)
            Dim width = Math.Max(155, rightPanel.ClientSize.Width -
                                     rightPanel.Padding.Horizontal -
                                     SystemInformation.VerticalScrollBarWidth - 2)
            For Each button In rightItems
                If button.Width <> width Then button.Width = width
                Dim measured = TextRenderer.MeasureText(button.Text, button.Font,
                    New Size(Math.Max(90, width - 16), Integer.MaxValue),
                    TextFormatFlags.WordBreak Or TextFormatFlags.TextBoxControl)
                Dim target = Math.Max(55, measured.Height + 21)
                If button.Height <> target Then button.Height = target
            Next
        End Sub

        Private Sub RootResized(sender As Object, e As EventArgs)
            ' At narrow window sizes stack actions *under* fields. The fields
            ' keep full width and both panes remain vertically scrollable.
            Dim narrow = root.ClientSize.Width < 690
            If narrow AndAlso root.RowCount <> 2 Then
                root.SuspendLayout()
                root.ColumnStyles(1).SizeType = SizeType.Absolute
                root.ColumnStyles(1).Width = 0
                root.RowCount = 2
                root.RowStyles.Clear()
                root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
                root.RowStyles.Add(New RowStyle(SizeType.Absolute, 165))
                root.SetCellPosition(rightPanel, New TableLayoutPanelCellPosition(0, 1))
                rightPanel.FlowDirection = FlowDirection.LeftToRight
                rightPanel.WrapContents = True
                root.ResumeLayout(True)
            ElseIf Not narrow AndAlso root.RowCount <> 1 Then
                root.SuspendLayout()
                root.SetCellPosition(rightPanel, New TableLayoutPanelCellPosition(1, 0))
                root.RowCount = 1
                root.RowStyles.Clear()
                root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
                root.ColumnStyles(1).SizeType = SizeType.Absolute
                root.ColumnStyles(1).Width = 210
                rightPanel.FlowDirection = FlowDirection.TopDown
                rightPanel.WrapContents = False
                root.ResumeLayout(True)
            End If
            LeftResized(sender, e)
            RightResized(sender, e)
        End Sub

        Public Sub Finish()
            ' All original controls must have been moved before disposing their
            ' legacy GroupBoxes/labels. Missing controls fail the build smoke test.
            For Each original As Control In page.Controls.Cast(Of Control)().ToArray()
                page.Controls.Remove(original)
                original.Dispose()
            Next
            page.Controls.Add(root)
            page.ResumeLayout(True)
            RootResized(Me, EventArgs.Empty)
        End Sub
    End Class

    Public Shared Sub Install(page As TabPage)
        Dim p As New PageBuilder(page)
        Select Case page.Name
            Case "TabPage2"  ' Drivers
                p.Section("Add drivers")
                p.Field("Driver folder", "txtDriverLocation", "btnOpnDriverFolder",
                        "Choose driver folder")
                p.Check("chkForceUnsigned")
                p.Check("chkRecurse")
                p.Section("Delete drivers")
                p.Field("Driver name", "txtDelDriverLocation")
                p.Action("btnAddDriver", "Add drivers", "primary")
                p.Action("btnGetDrivers", "Get 3rd-party drivers")
                p.Action("btnGetAllDriverInfo", "Get all driver info")
                p.Action("btnDelDriver", "Delete driver")
            Case "TabPage3"  ' Packages
                p.Section("Add packages")
                p.Field("Package folder", "txtPackageFile", "btnOpenPackageFile",
                        "Choose package folder")
                p.Check("chkIgnoreCheck")
                p.Section("Remove packages")
                p.Field("Package name", "txtPackageName")
                p.Field("Package path", "txtPackagePath")
                p.Action("btnAddPackages", "Add packages", "primary")
                p.Action("btnGetPackages", "Get packages from WIM")
                p.Action("btnRemovePackageName", "Remove by name")
                p.Action("btnRemovePackagePath", "Remove by path")
            Case "TabPage4"  ' Windows features
                p.Section("Windows features")
                p.Field("Feature name", "txtFeatureName")
                p.Check("chkEnablePkgName", "Use package name")
                p.Field("Package name", "txtFeatPackageName")
                p.Check("chkEnablePkgPath", "Use package path")
                p.Field("Package path", "txtFeatPackagePath")
                p.Action("btnGetFeatures", "Get Windows features")
                p.Action("btnEnableFeature", "Enable feature", "primary")
                p.Action("btnDisableFeature", "Disable feature")
            Case "TabPage5"  ' Windows editions
                p.Section("Product key")
                p.Field("Product key", "txtProdKey")
                p.Section("Windows edition")
                p.Field("Target edition", "txtEdition")
                p.Action("btGetCurrentEdition", "Get current edition")
                p.Action("btnGetTargetEditions", "Get target editions")
                p.Action("btnSetProdKey", "Set product key", "primary")
                p.Action("btnSetEdition", "Set edition", "primary")
            Case "TabPage6"  ' Unattend
                p.Section("Unattended servicing")
                p.Field("Unattend XML file", "txtUnattend", "btnChooseUnAttend",
                        "Choose XML")
                p.Action("btnApplyUnattend", "Apply unattend.xml", "primary")
            Case "TabPage7"  ' Applications
                p.Section("Application servicing")
                p.Field("Product code", "txtProductCode")
                p.Field("Patch code", "txtPatchCode")
                p.Field("MSP patch file", "txtPatchLocation", "btnChooseMSP",
                        "Choose MSP")
                p.Action("btnGetApps", "Get applications")
                p.Action("btnGetAppInfo", "Get application info")
                p.Action("btnGetAppPatches", "Get application patches")
                p.Action("btnGetAppPatchInfo", "Get patch info")
                p.Action("btnCheckAppPatch", "Check app patch")
            Case "TabPage8"  ' Capture
                p.Section("Capture WIM image")
                p.Field("Source folder", "txtCaptureSource", "btnCaptureSrc",
                        "Browse source")
                p.Field("WIM destination", "txtCaptureDest", "btnCaptureDest",
                        "Browse destination")
                p.Field("Image name", "txtName")
                p.Field("Compression", "cmbCompression")
                p.Check("chkCaptureVerify", "Verify image")
                p.Action("btnCreate", "Create image", "primary")
                p.Action("btnAppend", "Append image", "primary")
            Case "TabPage9"  ' Apply
                p.Section("Apply WIM image")
                p.Field("Source WIM", "txtxApplySource", "btnBrowseSource",
                        "Browse WIM")
                p.Field("Destination folder", "txtApplyDest", "btnBrowseDest",
                        "Browse destination")
                p.Field("Image index", "cmbApplyIndex")
                p.Check("chkApplyVerify", "Verify image")
                p.Action("btnApply", "Apply WIM", "primary")
            Case Else
                Throw New InvalidOperationException("Unsupported DISM section: " & page.Name)
        End Select
        p.Finish()
    End Sub
End Class
