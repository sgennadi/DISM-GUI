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
Imports System.Text.RegularExpressions
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
        Web.DefaultRequestHeaders.UserAgent.ParseAdd("DISMGUI-Modern-Updater/4.2.1")
        Web.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json")
    End Sub

    Private Shared Function CurrentVersion() As Version
        Return GetType(UpdateManager).Assembly.GetName().Version
    End Function

    Private Shared Function CurrentArchitecture() As String
        Select Case RuntimeInformation.ProcessArchitecture
            Case Architecture.Arm64 : Return "win-arm64"
            Case Architecture.X64 : Return "win-x64"
            Case Architecture.X86 : Return "win-x86"
            Case Else : Throw New NotSupportedException("Unsupported process architecture")
        End Select
    End Function

    ' One read-only, non-interrupting startup check for the Tools menu.
    ' Returns a newer stable version only if the current architecture asset and
    ' SHA-256 digest are present. Failure/offline simply yields no notification.
    Public Shared Async Function CheckForUpdatesAsync() As Task(Of String)
        Try
            Using timeout As New System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(9))
                Using response = Await Web.GetAsync(ReleaseApi, timeout.Token)
                    If Not response.IsSuccessStatusCode Then Return Nothing
                    Using document = JsonDocument.Parse(Await response.Content.ReadAsStringAsync())
                        Dim release = document.RootElement
                        Dim tag = release.GetProperty("tag_name").GetString()
                        If String.IsNullOrWhiteSpace(tag) Then Return Nothing
                        Dim remote As Version = Nothing
                        If Not Version.TryParse(tag.TrimStart("v"c, "V"c), remote) Then Return Nothing
                        If remote <= CurrentVersion() Then Return Nothing
                        Dim wanted = "DISMGUI-VS2026-" & CurrentArchitecture() & ".zip"
                        For Each asset In release.GetProperty("assets").EnumerateArray()
                            If Not String.Equals(asset.GetProperty("name").GetString(),
                                                 wanted, StringComparison.OrdinalIgnoreCase) Then Continue For
                            Dim digest As JsonElement
                            If asset.TryGetProperty("digest", digest) AndAlso
                               digest.ValueKind = JsonValueKind.String AndAlso
                               digest.GetString().StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) Then
                                Return remote.ToString(3)
                            End If
                        Next
                    End Using
                End Using
            End Using
        Catch
            ' Network errors, rate limits and proxy failures must not block startup.
        End Try
        Return Nothing
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

    ' Render GitHub release notes as readable plain text without HTML or script execution.
    ' Keep the original information and bullet order; the complete source stays linked.
    Private Shared Function FormatReleaseNotesMarkdown(markdown As String) As String
        If String.IsNullOrWhiteSpace(markdown) Then
            Return "No changelog was included in this release. Open GitHub Releases for details."
        End If
        Dim body = markdown.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
        If body.Length > 45000 Then body = body.Substring(0, 45000) & vbLf &
            "(Truncated here. See the full changelog on GitHub.)"
        Dim lines As New List(Of String)()
        For Each rawLine As String In body.Split(ControlChars.Lf)
            Dim line = rawLine.TrimEnd()
            line = Regex.Replace(line, "^\s{0,3}#{1,6}\s+", "")
            line = Regex.Replace(line, "^\s*[-*]\s+", "• ")
            line = Regex.Replace(line, "\[([^\]]+)\]\((https?://[^)]+)\)", "$1")
            line = line.Replace("**", "").Replace("`", "")
            lines.Add(line)
        Next
        Return String.Join(Environment.NewLine, lines).Trim()
    End Function

    ' Responsive, informative update center. No installation action until a verified
    ' stable GitHub Release for the current architecture has been found.
    Private NotInheritable Class UpdateWindow
        Inherits Form

        Private ReadOnly installedValue As Label
        Private ReadOnly latestValue As Label
        Private ReadOnly status As Label
        Private ReadOnly detail As Label
        Private ReadOnly changelogTitle As Label
        Private ReadOnly releaseNotes As TextBox
        Private ReadOnly releaseLink As LinkLabel
        Private ReadOnly install As Button
        Private ReadOnly check As Button
        Private ReadOnly closeButton As Button
        Private ReadOnly progressBar As ProgressBar
        Private ReadOnly progressText As Label
        Private assetDownload As String
        Private assetDigest As String
        Private assetSize As Long
        Private busy As Boolean

        Public Sub New()
            Me.Text = "DISM GUI " & CurrentVersion().ToString(3) & " - Updates"
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.Sizable
            Me.MaximizeBox = True
            Me.MinimizeBox = False
            Me.ShowInTaskbar = False
            Me.BackColor = ModernTheme.Backdrop
            Me.Font = ModernTheme.UiFont(10)
            Me.AutoScaleMode = AutoScaleMode.Font
            Me.ClientSize = New Size(835, 625)
            Me.MinimumSize = New Size(785, 535)
            Me.ShowIcon = True
            Try
                Me.Icon = ModernTheme.MakeAppIcon()
            Catch
            End Try

            Dim root As New TableLayoutPanel With {
                .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 5,
                .Margin = Padding.Empty, .Padding = New Padding(17, 16, 17, 13),
                .BackColor = ModernTheme.Backdrop
            }
            root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, 125))
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, 183))
            root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, 45))
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, 73))
            Me.Controls.Add(root)

            Dim header As New TableLayoutPanel With {
                .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
                .BackColor = ModernTheme.Blue, .Margin = New Padding(0, 0, 0, 10),
                .Padding = New Padding(18, 14, 17, 14)
            }
            header.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 76))
            header.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            root.Controls.Add(header, 0, 0)

            Dim logo As New PictureBox With {
                .Dock = DockStyle.Fill, .SizeMode = PictureBoxSizeMode.Zoom,
                .Margin = New Padding(0, 2, 14, 2), .BackColor = Color.Transparent
            }
            Try
                Using appIcon As Icon = ModernTheme.MakeAppIcon()
                    logo.Image = appIcon.ToBitmap()
                End Using
            Catch
            End Try
            header.Controls.Add(logo, 0, 0)
            Dim heroText As New TableLayoutPanel With {
                .Dock = DockStyle.Fill, .RowCount = 2, .ColumnCount = 1,
                .Margin = Padding.Empty, .BackColor = Color.Transparent
            }
            heroText.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            heroText.RowStyles.Add(New RowStyle(SizeType.Percent, 60))
            heroText.RowStyles.Add(New RowStyle(SizeType.Percent, 40))
            header.Controls.Add(heroText, 1, 0)
            heroText.Controls.Add(MakeLabel("UPDATES", 18, True, Color.White), 0, 0)
            heroText.Controls.Add(MakeLabel("Stable releases  ·  SHA-256 verified",
                                           10, False, Color.FromArgb(231, 246, 255)), 0, 1)

            Dim info As New TableLayoutPanel With {
                .Dock = DockStyle.Fill, .RowCount = 4, .ColumnCount = 1,
                .Margin = Padding.Empty, .Padding = New Padding(18, 12, 18, 8),
                .BackColor = Color.White
            }
            info.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            info.RowStyles.Add(New RowStyle(SizeType.Absolute, 27))
            info.RowStyles.Add(New RowStyle(SizeType.Absolute, 53))
            info.RowStyles.Add(New RowStyle(SizeType.Absolute, 43))
            info.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            root.Controls.Add(info, 0, 1)
            info.Controls.Add(MakeLabel("VERSION STATUS", 9.5F, True, ModernTheme.Muted), 0, 0)

            Dim versions As New TableLayoutPanel With {
                .Dock = DockStyle.Fill, .RowCount = 1, .ColumnCount = 2,
                .Margin = Padding.Empty, .BackColor = Color.White
            }
            versions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            versions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            info.Controls.Add(versions, 0, 1)
            installedValue = MakeLabel("Installed: " & CurrentVersion().ToString(3) &
                                       " (" & CurrentArchitecture() & ")", 11, True,
                                       ModernTheme.TextInk)
            latestValue = MakeLabel("Latest stable: Not checked", 11, True, ModernTheme.Muted)
            versions.Controls.Add(installedValue, 0, 0)
            versions.Controls.Add(latestValue, 1, 0)

            status = MakeLabel("Select Check for updates to look for a stable release.",
                               11, True, ModernTheme.Blue)
            info.Controls.Add(status, 0, 2)
            detail = MakeLabel("Updates are downloaded from GitHub Releases. The SHA-256 " &
                               "checksum is verified before installation. " &
                               "Your current EXE is backed up.",
                               10, False, ModernTheme.Muted)
            info.Controls.Add(detail, 0, 3)

            ' Inline release changelog (not a separate popup). It expands with
            ' the Updates window and stays scrollable for long release notes.
            Dim changesPanel As New TableLayoutPanel With {
                .Name = "ChangelogPanel", .Dock = DockStyle.Fill,
                .ColumnCount = 1, .RowCount = 2,
                .BackColor = Color.White,
                .Margin = New Padding(0, 9, 0, 0),
                .Padding = New Padding(18, 7, 18, 13)
            }
            changesPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            changesPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 36))
            changesPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            root.Controls.Add(changesPanel, 0, 2)

            Dim changesHeading As New TableLayoutPanel With {
                .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
                .Margin = Padding.Empty, .BackColor = Color.White
            }
            changesHeading.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            changesHeading.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 238))
            changesHeading.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            changesPanel.Controls.Add(changesHeading, 0, 0)

            changelogTitle = MakeLabel("WHAT'S NEW", 10.5F, True, ModernTheme.TextInk)
            changelogTitle.Name = "lblChangelogTitle"
            changesHeading.Controls.Add(changelogTitle, 0, 0)

            releaseLink = New LinkLabel With {
                .Name = "lnkFullChangelog",
                .Text = "Full changelog on GitHub",
                .Font = ModernTheme.UiFont(9.2F, True),
                .LinkColor = ModernTheme.Blue, .ActiveLinkColor = ModernTheme.Green,
                .Dock = DockStyle.Fill, .Margin = Padding.Empty,
                .TextAlign = ContentAlignment.MiddleRight,
                .Enabled = False, .TabStop = True
            }
            AddHandler releaseLink.LinkClicked, Sub(sender, e)
                Dim releaseTag = latestValue.Text.Replace("Latest stable: ", "").Trim()
                If releaseTag.Length > 1 AndAlso
                   releaseTag.StartsWith("v", StringComparison.OrdinalIgnoreCase) Then
                    Dim releaseVersion As System.Version = Nothing
                    If System.Version.TryParse(releaseTag.Substring(1), releaseVersion) Then
                        Process.Start(New ProcessStartInfo(
                            ProjectUrl & "/releases/tag/" & Uri.EscapeDataString(releaseTag)) With
                            {.UseShellExecute = True})
                    End If
                End If
            End Sub
            changesHeading.Controls.Add(releaseLink, 1, 0)

            releaseNotes = New TextBox With {
                .Name = "txtReleaseChangelog",
                .Multiline = True, .ReadOnly = True,
                .ScrollBars = ScrollBars.Vertical, .WordWrap = True,
                .Dock = DockStyle.Fill, .TabStop = False,
                .BackColor = Color.FromArgb(249, 251, 255),
                .ForeColor = ModernTheme.TextInk,
                .Font = ModernTheme.UiFont(9.5F),
                .BorderStyle = BorderStyle.FixedSingle,
                .Margin = New Padding(0, 0, 0, 1),
                .Text = "Checking the latest stable release..."
            }
            changesPanel.Controls.Add(releaseNotes, 0, 1)

            Dim progressPanel As New TableLayoutPanel With {
                .Dock = DockStyle.Fill, .RowCount = 2, .ColumnCount = 1,
                .Margin = Padding.Empty, .BackColor = ModernTheme.Backdrop
            }
            progressPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            progressPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 23))
            progressPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            progressText = MakeLabel("", 9, False, ModernTheme.TextInk)
            progressPanel.Controls.Add(progressText, 0, 0)
            progressBar = New ProgressBar With {
                .Dock = DockStyle.Fill, .Height = 14, .Margin = New Padding(2, 1, 2, 2),
                .Visible = False, .Style = ProgressBarStyle.Continuous
            }
            progressPanel.Controls.Add(progressBar, 0, 1)
            root.Controls.Add(progressPanel, 0, 3)

            Dim actions As New TableLayoutPanel With {
                .Dock = DockStyle.Fill, .ColumnCount = 5, .RowCount = 1,
                .Margin = Padding.Empty, .Padding = New Padding(0, 13, 0, 5),
                .BackColor = ModernTheme.Backdrop
            }
            actions.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 183))
            actions.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 182))
            actions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            actions.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 135))
            actions.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 107))
            root.Controls.Add(actions, 0, 4)

            check = New Button With {.Text = "Check", .Dock = DockStyle.Fill,
                                     .Margin = New Padding(0, 0, 12, 0)}
            ModernTheme.StyleButton(check, "browse")
            AddHandler check.Click, AddressOf CheckClicked
            actions.Controls.Add(check, 0, 0)

            install = New Button With {.Text = "Update", .Dock = DockStyle.Fill,
                                       .Margin = New Padding(0, 0, 12, 0), .Enabled = False}
            ModernTheme.StyleButton(install, "primary")
            AddHandler install.Click, AddressOf InstallClicked
            actions.Controls.Add(install, 1, 0)

            Dim releases As New Button With {.Text = "Releases", .Dock = DockStyle.Fill,
                                             .Margin = New Padding(0, 0, 12, 0)}
            ModernTheme.StyleButton(releases, "neutral")
            AddHandler releases.Click, Sub(sender, e)
                Process.Start(New ProcessStartInfo(ProjectUrl & "/releases") With
                              {.UseShellExecute = True})
            End Sub
            actions.Controls.Add(releases, 3, 0)

            closeButton = New Button With {.Text = "Close", .Dock = DockStyle.Fill,
                                           .Margin = Padding.Empty}
            ModernTheme.StyleButton(closeButton, "neutral")
            AddHandler closeButton.Click, Sub(sender, e) Me.Close()
            Me.CancelButton = closeButton
            actions.Controls.Add(closeButton, 4, 0)

            Dim tips As New ToolTip()
            tips.SetToolTip(check, "Query the latest published stable release on GitHub")
            tips.SetToolTip(install, "Available after a release and SHA-256 digest are verified")
            tips.SetToolTip(releases, "Open the download page in your browser")
            tips.SetToolTip(closeButton, "Close only the Update Center")
            ' Opening Update Center checks the latest release automatically;
            ' the manual Check for updates button remains available for refresh.
            AddHandler Me.Shown, Sub(sender, e) CheckClicked(check, EventArgs.Empty)
        End Sub

        Private Shared Function MakeLabel(value As String, size As Single, bold As Boolean,
                                          color As Color) As Label
            Return New Label With {
                .Text = value, .Font = ModernTheme.UiFont(size, bold),
                .ForeColor = color, .BackColor = Color.Transparent,
                .TextAlign = ContentAlignment.MiddleLeft, .AutoEllipsis = False,
                .Dock = DockStyle.Fill, .Margin = Padding.Empty
            }
        End Function

        Private Shared Function NormalizeVersion(value As Version) As Version
            Return New Version(value.Major, value.Minor,
                               Math.Max(0, value.Build), Math.Max(0, value.Revision))
        End Function

        Private Async Sub CheckClicked(sender As Object, e As EventArgs)
            If busy Then Return
            busy = True
            check.Enabled = False
            install.Enabled = False
            install.Text = "Update"
            latestValue.Text = "Latest stable: Checking..."
            status.Text = "Checking GitHub Releases..."
            detail.Text = "Checking release version and architecture-specific download..."
            status.ForeColor = ModernTheme.Blue
            assetDownload = Nothing
            assetDigest = Nothing
            assetSize = 0
            changelogTitle.Text = "WHAT'S NEW"
            releaseNotes.Text = "Loading release notes from GitHub..."
            releaseLink.Enabled = False

            Try
                Using response = Await Web.GetAsync(ReleaseApi)
                    If response.StatusCode = HttpStatusCode.NotFound Then
                        latestValue.Text = "Latest stable: Not published"
                        status.Text = "No stable release has been published."
                        status.ForeColor = ModernTheme.Muted
                        detail.Text = "The Actions page contains test builds. " &
                                      "Stable ZIP packages appear under GitHub Releases " &
                                      "once a version is published."
                        releaseNotes.Text = "No stable release or changelog is available yet."
                        Return
                    End If
                    response.EnsureSuccessStatusCode()
                    Using document = JsonDocument.Parse(Await response.Content.ReadAsStringAsync())
                        Dim release = document.RootElement
                        Dim tag = release.GetProperty("tag_name").GetString()
                        Dim remote As Version = Nothing
                        If String.IsNullOrWhiteSpace(tag) OrElse
                           Not Version.TryParse(tag.TrimStart("v"c, "V"c), remote) Then
                            Throw New InvalidDataException("The stable release has an invalid version tag")
                        End If
                        latestValue.Text = "Latest stable: " & tag
                        changelogTitle.Text = "WHAT'S NEW IN " & remote.ToString(3)
                        Dim notesJson As JsonElement
                        If release.TryGetProperty("body", notesJson) AndAlso
                           notesJson.ValueKind = JsonValueKind.String Then
                            releaseNotes.Text = FormatReleaseNotesMarkdown(notesJson.GetString())
                        Else
                            releaseNotes.Text = FormatReleaseNotesMarkdown(Nothing)
                        End If
                        releaseLink.Enabled = True
                        Dim normalizedRemote = NormalizeVersion(remote)
                        Dim normalizedCurrent = NormalizeVersion(CurrentVersion())

                        If normalizedRemote < normalizedCurrent Then
                            status.Text = "You have a newer version than the stable release."
                            status.ForeColor = ModernTheme.Muted
                            detail.Text = "No downgrade is recommended. Use View releases " &
                                          "to inspect older stable packages."
                            Return
                        End If

                        Dim wantedName = "DISMGUI-VS2026-" & CurrentArchitecture() & ".zip"
                        Dim selected As JsonElement
                        Dim found As Boolean = False
                        For Each asset In release.GetProperty("assets").EnumerateArray()
                            If String.Equals(asset.GetProperty("name").GetString(),
                                             wantedName, StringComparison.OrdinalIgnoreCase) Then
                                selected = asset
                                found = True
                                Exit For
                            End If
                        Next
                        If Not found Then
                            status.Text = "No download for " & CurrentArchitecture() & "."
                            status.ForeColor = ModernTheme.Red
                            detail.Text = "The stable release does not contain " & wantedName &
                                          ". Select View releases to inspect the files."
                            Return
                        End If

                        assetDownload = selected.GetProperty("browser_download_url").GetString()
                        assetSize = selected.GetProperty("size").GetInt64()
                        Dim digestJson As JsonElement
                        If selected.TryGetProperty("digest", digestJson) AndAlso
                           digestJson.ValueKind = JsonValueKind.String Then
                            assetDigest = digestJson.GetString()
                        End If
                        If String.IsNullOrWhiteSpace(assetDigest) OrElse
                           Not assetDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) Then
                            status.Text = "Download not verified"
                            status.ForeColor = ModernTheme.Red
                            detail.Text = "No SHA-256 digest is available for this ZIP. " &
                                          "Automatic installation is disabled for safety."
                            Return
                        End If

                        If normalizedRemote = normalizedCurrent Then
                            install.Text = "Reinstall"
                            status.Text = "You already have the latest stable version."
                            status.ForeColor = ModernTheme.Green
                            detail.Text = "The same verified version is available. " &
                                          "Use Reinstall only if you need a clean copy."
                        Else
                            install.Text = "Update"
                            status.Text = "A newer stable version is available."
                            status.ForeColor = ModernTheme.Green
                            detail.Text = "Select Update to download the verified " &
                                          "ZIP, back up your current EXE and install " &
                                          tag & ". DISM GUI will restart."
                        End If
                        install.Enabled = True
                    End Using
                End Using
            Catch ex As Exception
                latestValue.Text = "Latest stable: Unavailable"
                status.Text = "Could not check GitHub Releases"
                status.ForeColor = ModernTheme.Red
                detail.Text = ex.Message
                releaseNotes.Text = "Couldn't retrieve the release changelog. " &
                                    "Use Releases to view the published history on GitHub."
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
                    MessageBox.Show(Me,
                                    "A WIM is mounted or DISM is still working." &
                                    Environment.NewLine &
                                    "Dismount the image and complete DISM operations first.",
                                    "Update blocked while DISM is working",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
            Next

            Dim verb = If(install.Text = "Reinstall", "reinstall this version", "install this update")
            If MessageBox.Show(Me,
                               "Download the verified release and " & verb & "?" &
                               Environment.NewLine & Environment.NewLine &
                               "The current EXE will be backed up and DISM GUI will restart.",
                               "DISM GUI - Confirm update", MessageBoxButtons.YesNo,
                               MessageBoxIcon.Question) <> DialogResult.Yes Then Return

            busy = True
            check.Enabled = False
            install.Enabled = False
            closeButton.Enabled = False
            progressText.Text = "Download progress"
            progressBar.Value = 0
            progressBar.Visible = True
            status.Text = "Downloading and verifying the release..."
            status.ForeColor = ModernTheme.Blue
            detail.Text = "Your current EXE remains untouched until verification finishes."
            Try
                Dim report = New Progress(Of Integer)(Sub(value)
                    If Me.IsDisposed Then Return
                    progressBar.Value = Math.Max(0, Math.Min(100, value))
                    progressText.Text = "Download and verification: " & value.ToString() & "%"
                End Sub)
                Await StageAndLaunchAsync(assetDownload, assetDigest, assetSize, report)
                status.Text = "Applying the update..."
                detail.Text = "A verified copy was staged. The application will restart."
                Environment.Exit(0)
            Catch ex As Exception
                status.Text = "The update was not installed."
                status.ForeColor = ModernTheme.Red
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
