Imports System
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Self-contained updater: no PowerShell, cmd.exe, external installer or scheduled task.
' Only official GitHub Releases with SHA-256 asset digests can be installed automatically.
Public NotInheritable Class UpdateManager
    Private Sub New()
    End Sub

    Private Const ProjectUrl As String = "https://github.com/sgennadi/DISM-GUI"
    Private Const ReleaseApi As String = "https://api.github.com/repos/sgennadi/DISM-GUI/releases/latest"
    Private Shared ReadOnly UpdatesRoot As String =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "DISMGUI", "Updates")
    Private Shared ReadOnly Web As New HttpClient()

    Shared Sub New()
        Web.Timeout = TimeSpan.FromSeconds(60)
        Web.DefaultRequestHeaders.UserAgent.ParseAdd("DISMGUI-Modern-Updater/4.1")
        Web.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json")
    End Sub

    Private Shared Function CurrentVersion() As Version
        Return Assembly.GetEntryAssembly().GetName().Version
    End Function

    Private Shared Function CurrentArchitecture() As String
        Select Case RuntimeInformation.ProcessArchitecture
            Case Architecture.Arm64 : Return "win-arm64"
            Case Architecture.X64 : Return "win-x64"
            Case Architecture.X86 : Return "win-x86"
            Case Else : Throw New NotSupportedException("Unsupported process architecture")
        End Select
    End Function

    Public Shared Sub ShowUpdateDialog(owner As IWin32Window)
        Using form As New UpdateWindow()
            form.ShowDialog(owner)
        End Using
    End Sub

    Public Shared Function HandleSelfUpdateMode() As Boolean
        Dim args = Environment.GetCommandLineArgs()
        If args.Length < 2 OrElse Not String.Equals(args(1), "--finish-update",
                                                      StringComparison.OrdinalIgnoreCase) Then
            Return False
        End If
        Try
            If args.Length <> 5 Then Throw New InvalidOperationException("Invalid update request")
            Dim destination = Path.GetFullPath(args(2))
            Dim parentPid As Integer
            If Not Integer.TryParse(args(3), parentPid) Then
                Throw New InvalidOperationException("Invalid parent process")
            End If
            Dim token = args(4)
            Dim stagedExe = Path.GetFullPath(Environment.ProcessPath)
            Dim stagedDirectory = Path.GetDirectoryName(stagedExe)
            Dim authorizedRoot = Path.GetFullPath(UpdatesRoot).TrimEnd(Path.DirectorySeparatorChar) &
                                 Path.DirectorySeparatorChar
            If Not stagedDirectory.StartsWith(authorizedRoot, StringComparison.OrdinalIgnoreCase) OrElse
               Not String.Equals(Path.GetFileName(stagedExe), "DISM GUI.exe",
                                 StringComparison.OrdinalIgnoreCase) OrElse
               Not String.Equals(Path.GetFileName(destination), "DISM GUI.exe",
                                 StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidOperationException("Unexpected update destination")
            End If
            Dim authorization = Path.Combine(stagedDirectory, "update-request.txt")
            Dim lines = File.ReadAllLines(authorization)
            If lines.Length <> 3 OrElse
               Not String.Equals(lines(0), destination, StringComparison.OrdinalIgnoreCase) OrElse
               lines(1) <> parentPid.ToString() OrElse
               Not String.Equals(lines(2), token, StringComparison.Ordinal) Then
                Throw New InvalidOperationException("Update authorization failed")
            End If
            If Not File.Exists(destination) Then Throw New FileNotFoundException("Installed DISM GUI was not found")

            ' The original EXE must exit before Windows permits replacement.
            Try
                Using parent = Process.GetProcessById(parentPid)
                    If Not parent.WaitForExit(45000) Then
                        Throw New IOException("The previous DISM GUI instance is still running")
                    End If
                End Using
            Catch ex As ArgumentException
                ' Already exited.
            End Try

            Dim backupDir = Path.Combine(UpdatesRoot, "Backups")
            Directory.CreateDirectory(backupDir)
            Dim backup = Path.Combine(backupDir, "DISM GUI-before-update.exe")
            File.Copy(destination, backup, True)
            Try
                File.Copy(stagedExe, destination, True)
            Catch
                If File.Exists(backup) Then File.Copy(backup, destination, True)
                Throw
            End Try

            File.Delete(authorization)
            Process.Start(New ProcessStartInfo(destination) With {.UseShellExecute = True})
        Catch ex As Exception
            MessageBox.Show("The DISM GUI update could not be installed:" &
                            Environment.NewLine & ex.Message &
                            Environment.NewLine & "The original EXE was left intact or restored from backup.",
                            "DISM GUI Update", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Environment.Exit(0)
        Return True
    End Function

    Private Shared Async Function StageAndLaunchAsync(assetUrl As String, expectedDigest As String,
                                                      expectedSize As Long,
                                                      report As IProgress(Of Integer)) As Task
        If Not expectedDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidDataException("GitHub has not provided an SHA-256 digest for this release asset")
        End If
        If expectedSize < 1024 OrElse expectedSize > 250L * 1024L * 1024L Then
            Throw New InvalidDataException("Unexpected release size")
        End If
        Dim link As Uri = Nothing
        If Not Uri.TryCreate(assetUrl, UriKind.Absolute, link) OrElse
           link.Scheme <> Uri.UriSchemeHttps OrElse
           link.Host <> "github.com" Then
            Throw New InvalidDataException("Release asset URL must be on github.com")
        End If
        Dim existingExe = Path.GetFullPath(Environment.ProcessPath)
        If Not String.Equals(Path.GetFileName(existingExe), "DISM GUI.exe",
                             StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException("Only installed DISM GUI.exe can be updated automatically")
        End If

        Dim stageDir = Path.Combine(UpdatesRoot, Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(stageDir)
        Dim zipPath = Path.Combine(stageDir, "update.zip")
        Dim stagedExe = Path.Combine(stageDir, "DISM GUI.exe")
        Try
            Using response = Await Web.GetAsync(link, HttpCompletionOption.ResponseHeadersRead)
                response.EnsureSuccessStatusCode()
                If response.Content.Headers.ContentLength.HasValue AndAlso
                   response.Content.Headers.ContentLength.Value > 250L * 1024L * 1024L Then
                    Throw New InvalidDataException("Release exceeds the download limit")
                End If
                Using source = Await response.Content.ReadAsStreamAsync()
                    Using output As New FileStream(zipPath, FileMode.CreateNew, FileAccess.Write,
                                                   FileShare.None, 81920, True)
                        Dim buf(81919) As Byte
                        Dim received As Long = 0
                        While True
                            Dim n = Await source.ReadAsync(buf, 0, buf.Length)
                            If n = 0 Then Exit While
                            received += n
                            If received > 250L * 1024L * 1024L Then
                                Throw New InvalidDataException("Release exceeds the download limit")
                            End If
                            Await output.WriteAsync(buf, 0, n)
                            report.Report(Math.Min(95, CInt((received * 95) \ expectedSize)))
                        End While
                    End Using
                End Using
            End Using
            If New FileInfo(zipPath).Length <> expectedSize Then
                Throw New InvalidDataException("The downloaded asset is incomplete")
            End If
            Dim actualHash As String
            Using f = File.OpenRead(zipPath)
                actualHash = Convert.ToHexString(SHA256.HashData(f))
            End Using
            Dim expectedHash = expectedDigest.Substring(7).Trim()
            If expectedHash.Length <> 64 OrElse
               Not String.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidDataException("SHA-256 verification failed; the update was not installed")
            End If

            Using archive = ZipFile.OpenRead(zipPath)
                Dim exeEntry = archive.Entries.SingleOrDefault(
                    Function(entry) String.Equals(entry.FullName, "DISM GUI.exe",
                                                  StringComparison.OrdinalIgnoreCase))
                If exeEntry Is Nothing Then
                    Throw New InvalidDataException("Release ZIP does not contain DISM GUI.exe at its root")
                End If
                If exeEntry.Length < 10 * 1024 OrElse exeEntry.Length > 300L * 1024L * 1024L Then
                    Throw New InvalidDataException("Invalid EXE size inside release")
                End If
                exeEntry.ExtractToFile(stagedExe)
            End Using
            report.Report(100)

            Dim token = Guid.NewGuid().ToString("N")
            Dim pid = Environment.ProcessId
            File.WriteAllLines(Path.Combine(stageDir, "update-request.txt"),
                               {existingExe, pid.ToString(), token})
            Dim psi As New ProcessStartInfo(stagedExe) With {.UseShellExecute = True}
            psi.ArgumentList.Add("--finish-update")
            psi.ArgumentList.Add(existingExe)
            psi.ArgumentList.Add(pid.ToString())
            psi.ArgumentList.Add(token)
            Process.Start(psi)
        Catch
            Try
                Directory.Delete(stageDir, True)
            Catch
            End Try
            Throw
        End Try
    End Function

    Private NotInheritable Class UpdateWindow
        Inherits Form

        Private ReadOnly status As Label
        Private ReadOnly detail As Label
        Private ReadOnly install As Button
        Private ReadOnly check As Button
        Private ReadOnly closeButton As Button
        Private ReadOnly progressBar As ProgressBar
        Private assetDownload As String
        Private assetDigest As String
        Private assetSize As Long
        Private busy As Boolean

        Public Sub New()
            Me.Text = "DISM GUI - Update Center"
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.ShowInTaskbar = False
            Me.BackColor = ModernTheme.Backdrop
            Me.ClientSize = New Size(690, 405)
            Me.AutoScaleMode = AutoScaleMode.Font
            Me.Font = ModernTheme.UiFont(10)
            Try
                Me.Icon = ModernTheme.MakeAppIcon()
            Catch
            End Try

            Dim header As New Panel With {.Dock = DockStyle.Top, .Height = 102,
                                          .BackColor = ModernTheme.Blue}
            Me.Controls.Add(header)
            Dim name As New Label With {.Text = "UPDATE CENTER",
                                        .ForeColor = Color.White, .BackColor = Color.Transparent,
                                        .Font = ModernTheme.UiFont(20, True),
                                        .Location = New Point(26, 16), .Size = New Size(610, 48)}
            header.Controls.Add(name)
            Dim caption As New Label With {
                .Text = "Official GitHub Releases · Secure SHA-256 verification",
                .ForeColor = Color.LightCyan, .BackColor = Color.Transparent,
                .Font = ModernTheme.UiFont(9),
                .Location = New Point(26, 65), .Size = New Size(635, 28)
            }
            header.Controls.Add(caption)

            Dim installed As New Label With {
                .Text = "Installed: " & CurrentVersion().ToString() & "  |  " & CurrentArchitecture(),
                .Location = New Point(26, 123), .Size = New Size(632, 31),
                .Font = ModernTheme.UiFont(10, True), .ForeColor = ModernTheme.TextInk
            }
            Me.Controls.Add(installed)
            status = New Label With {.Text = "Select Check Updates to query published releases.",
                                     .Location = New Point(26, 167), .Size = New Size(633, 33),
                                     .ForeColor = ModernTheme.Blue, .Font = ModernTheme.UiFont(10, True)}
            Me.Controls.Add(status)
            detail = New Label With {.Text = "No background download. Nothing is installed without approval.",
                                     .Location = New Point(26, 213), .Size = New Size(630, 71),
                                     .ForeColor = ModernTheme.Muted, .Font = ModernTheme.UiFont(10.0F)}
            Me.Controls.Add(detail)
            progressBar = New ProgressBar With {.Location = New Point(26, 298),
                                                 .Size = New Size(636, 15), .Visible = False}
            Me.Controls.Add(progressBar)
            check = New Button With {.Text = "Check Updates", .Location = New Point(26, 335),
                                     .Size = New Size(162, 46)}
            ModernTheme.StyleButton(check, "browse")
            AddHandler check.Click, AddressOf CheckClicked
            Me.Controls.Add(check)
            install = New Button With {.Text = "Download & Install", .Location = New Point(204, 335),
                                       .Size = New Size(190, 46), .Enabled = False}
            ModernTheme.StyleButton(install, "primary")
            AddHandler install.Click, AddressOf InstallClicked
            Me.Controls.Add(install)
            Dim releases As New Button With {.Text = "Releases", .Location = New Point(410, 335),
                                             .Size = New Size(110, 46)}
            ModernTheme.StyleButton(releases)
            AddHandler releases.Click, Sub(sender, e)
                Process.Start(New ProcessStartInfo(ProjectUrl & "/releases") With
                              {.UseShellExecute = True})
            End Sub
            Me.Controls.Add(releases)
            closeButton = New Button With {.Text = "Close", .Location = New Point(538, 335),
                                     .Size = New Size(124, 46)}
            ModernTheme.StyleButton(closeButton)
            AddHandler closeButton.Click, Sub(sender, e) Me.Close()
            Me.Controls.Add(closeButton)
            Me.CancelButton = closeButton
        End Sub

        Private Async Sub CheckClicked(sender As Object, e As EventArgs)
            If busy Then Return
            busy = True
            check.Enabled = False
            install.Enabled = False
            status.Text = "Checking official GitHub Releases..."
            detail.Text = ""
            assetDownload = Nothing
            assetDigest = Nothing
            assetSize = 0
            Try
                Using response = Await Web.GetAsync(ReleaseApi)
                    If response.StatusCode = HttpStatusCode.NotFound Then
                        status.Text = "No published stable release yet"
                        detail.Text = "The Actions build is not a release. Publish a GitHub Release with architecture ZIP assets to enable one-click updates."
                        Return
                    End If
                    response.EnsureSuccessStatusCode()
                    Using document = JsonDocument.Parse(Await response.Content.ReadAsStringAsync())
                        Dim root = document.RootElement
                        Dim tag = root.GetProperty("tag_name").GetString()
                        Dim remote As Version = Nothing
                        If String.IsNullOrWhiteSpace(tag) OrElse
                           Not Version.TryParse(tag.TrimStart("v"c, "V"c), remote) Then
                            Throw New InvalidDataException("Release tag does not contain a valid version")
                        End If
                        Dim wantedName = "DISMGUI-VS2026-" & CurrentArchitecture() & ".zip"
                        Dim selected As JsonElement
                        Dim found As Boolean = False
                        For Each asset In root.GetProperty("assets").EnumerateArray()
                            Dim name = asset.GetProperty("name").GetString()
                            If String.Equals(name, wantedName, StringComparison.OrdinalIgnoreCase) Then
                                selected = asset
                                found = True
                                Exit For
                            End If
                        Next
                        status.Text = "Latest release: " & tag
                        If remote < CurrentVersion() Then
                            detail.Text = "Your installed version is newer than the latest stable release."
                            Return
                        End If
                        If Not found Then
                            detail.Text = "Missing " & wantedName & ". Open Releases to download manually."
                            Return
                        End If
                        assetDownload = selected.GetProperty("browser_download_url").GetString()
                        assetSize = selected.GetProperty("size").GetInt64()
                        Dim digestElement As JsonElement
                        If selected.TryGetProperty("digest", digestElement) AndAlso
                           digestElement.ValueKind = JsonValueKind.String Then
                            assetDigest = digestElement.GetString()
                        End If
                        If String.IsNullOrWhiteSpace(assetDigest) OrElse
                           Not assetDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) Then
                            detail.Text = "Release has no verified SHA-256 digest; automatic installation is disabled."
                            Return
                        End If
                        install.Text = If(remote = CurrentVersion(), "Reinstall", "Download & Install")
                        detail.Text = If(remote = CurrentVersion(),
                                         "Same version available; you can reinstall the verified release.",
                                         "Update available. The installed EXE will be backed up and replaced after this process exits.")
                        install.Enabled = True
                    End Using
                End Using
            Catch ex As Exception
                status.Text = "Update check failed"
                detail.Text = ex.Message
            Finally
                busy = False
                check.Enabled = True
            End Try
        End Sub

        Private Async Sub InstallClicked(sender As Object, e As EventArgs)
            If busy OrElse String.IsNullOrWhiteSpace(assetDownload) Then Return
            For Each openForm As Form In Application.OpenForms
                Dim main = TryCast(openForm, frmMain)
                If main IsNot Nothing AndAlso main.HasMountedImage Then
                    MessageBox.Show(Me, "A WIM is mounted or DISM is still working. Finish and dismount the image before installing an update.",
                                    "Update unavailable during DISM operations",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
            Next
            If MessageBox.Show(Me, "Download and install the verified DISM GUI release?" &
                               Environment.NewLine &
                               "Close any mounted WIM images before proceeding.",
                               "Install update", MessageBoxButtons.YesNo,
                               MessageBoxIcon.Question) <> DialogResult.Yes Then Return
            busy = True
            check.Enabled = False
            install.Enabled = False
            closeButton.Enabled = False
            progressBar.Value = 0
            progressBar.Visible = True
            status.Text = "Downloading and verifying..."
            detail.Text = "The new EXE is staged before the current one is closed."
            Try
                Dim report = New Progress(Of Integer)(Sub(value)
                    If Not Me.IsDisposed Then progressBar.Value = Math.Max(0, Math.Min(100, value))
                End Sub)
                Await StageAndLaunchAsync(assetDownload, assetDigest, assetSize, report)
                status.Text = "Installing update..."
                ' The staged copy replaces the installed EXE after this process exits.
                Environment.Exit(0)
            Catch ex As Exception
                status.Text = "Could not install update"
                detail.Text = ex.Message
                install.Enabled = True
            Finally
                busy = False
                check.Enabled = True
                closeButton.Enabled = True
            End Try
        End Sub

        Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
            If busy Then
                e.Cancel = True
                Return
            End If
            MyBase.OnFormClosing(e)
        End Sub
    End Class
End Class
