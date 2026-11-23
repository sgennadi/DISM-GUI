Imports System
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms

Namespace IndependentDismGui
    Public NotInheritable Class MainWindow
        Inherits Form

        Private ReadOnly imagePath As New TextBox() With {.Dock = DockStyle.Fill, .PlaceholderText = "Select a WIM, ESD or VHD image"}
        Private ReadOnly output As New TextBox() With {.Dock = DockStyle.Fill, .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Both, .WordWrap = False}
        Private ReadOnly inspectButton As New Button() With {.Text = "Inspect image", .AutoSize = True}
        Private ReadOnly mountsButton As New Button() With {.Text = "Mounted images", .AutoSize = True}
        Private ReadOnly browseButton As New Button() With {.Text = "Browse...", .AutoSize = True}
        Private ReadOnly status As New Label() With {.Text = "Ready. Read-only image inspection.", .Dock = DockStyle.Fill, .AutoEllipsis = True}

        Public Sub New()
            Text = "DISM GUI Independent - .NET 11"
            MinimumSize = New Size(720, 460)
            Size = New Size(1000, 680)
            StartPosition = FormStartPosition.CenterScreen
            AutoScaleMode = AutoScaleMode.Dpi

            Dim root As New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .Padding = New Padding(12)}
            root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            Dim controls As New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 4, .AutoSize = True}
            controls.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            controls.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            controls.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            controls.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            controls.Controls.Add(imagePath, 0, 0)
            controls.Controls.Add(browseButton, 1, 0)
            controls.Controls.Add(inspectButton, 2, 0)
            controls.Controls.Add(mountsButton, 3, 0)
            root.Controls.Add(controls, 0, 0)
            root.Controls.Add(output, 0, 1)
            root.Controls.Add(status, 0, 2)
            Controls.Add(root)
            AddHandler browseButton.Click, AddressOf BrowseImage
            AddHandler inspectButton.Click, AddressOf InspectImage
            AddHandler mountsButton.Click, AddressOf InspectMounts
        End Sub

        Private Sub BrowseImage(sender As Object, e As EventArgs)
            Using dialog As New OpenFileDialog() With {.Filter = "Windows images|*.wim;*.esd;*.vhd;*.vhdx|All files|*.*", .CheckFileExists = True}
                If dialog.ShowDialog(Me) = DialogResult.OK Then imagePath.Text = dialog.FileName
            End Using
        End Sub

        Private Async Sub InspectImage(sender As Object, e As EventArgs)
            Dim selected = imagePath.Text.Trim()
            If Not File.Exists(selected) Then
                MessageBox.Show(Me, "Select an existing image file.", "Image not found", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            inspectButton.Enabled = False
            status.Text = "Inspecting image..."
            Try
                Dim result = Await Task.Run(Function() RunDism("/Get-WimInfo", "/WimFile:" & selected))
                output.Text = result
                status.Text = "Inspection finished."
            Catch ex As Exception
                output.Text = ex.ToString()
                status.Text = "Inspection failed."
            Finally
                inspectButton.Enabled = True
            End Try
        End Sub

        Private Async Sub InspectMounts(sender As Object, e As EventArgs)
            mountsButton.Enabled = False
            status.Text = "Inspecting mounted images..."
            Try
                output.Text = Await Task.Run(Function() RunDism("/Get-MountedWimInfo"))
                status.Text = "Mounted-image inventory finished."
            Catch ex As Exception
                output.Text = ex.ToString()
                status.Text = "Mounted-image inventory failed."
            Finally
                mountsButton.Enabled = True
            End Try
        End Sub

        Private Shared Function RunDism(ParamArray arguments As String()) As String
            Dim start As New ProcessStartInfo() With {
                .FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "dism.exe"),
                .UseShellExecute = False,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .CreateNoWindow = True
            }
            start.ArgumentList.Add("/English")
            For Each argument In arguments
                start.ArgumentList.Add(argument)
            Next
            Using proc As New Process() With {.StartInfo = start}
                If Not proc.Start() Then Throw New InvalidOperationException("DISM failed to start.")
                Dim stdoutTask = proc.StandardOutput.ReadToEndAsync()
                Dim stderrTask = proc.StandardError.ReadToEndAsync()
                proc.WaitForExit()
                Dim result = stdoutTask.GetAwaiter().GetResult() & Environment.NewLine & stderrTask.GetAwaiter().GetResult()
                If proc.ExitCode <> 0 Then Throw New InvalidOperationException("DISM exit code " & proc.ExitCode.ToString() & Environment.NewLine & result)
                Return result
            End Using
        End Function
    End Class
End Namespace
