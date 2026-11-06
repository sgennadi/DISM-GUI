#!/usr/bin/env python3
"""Apply the DISM GUI modern picker and foreground unmount UX patch.

Fail closed on unrecognized upstream changes. No changes to the original GitHub repository.
"""
from pathlib import Path
import sys


def replace_once(data, old, new, label):
    count = data.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one source match, found {count}")
    return data.replace(old, new, 1)


def replace_block(data, begin, end, replacement, label):
    start = data.find(begin)
    stop = data.find(end, start + len(begin))
    if start < 0 or stop < 0 or data.find(begin, start + 1) != -1:
        raise RuntimeError(f"{label}: upstream markers missing or ambiguous")
    return data[:start] + replacement.rstrip() + "\n\n" + data[stop:]


def main():
    if len(sys.argv) != 2:
        raise SystemExit("Usage: apply_fix.py REPOSITORY_PATH")
    file = Path(sys.argv[1]).resolve() / "DISM GUI" / "frmMain.vb"
    if not file.is_file():
        raise RuntimeError(f"Original source missing: {file}")
    original = file.read_text(encoding="utf-8-sig")
    if "Private Function ChooseFolderModern(" in original:
        print("Modern dialogs already applied.")
        return
    newline = "\r\n" if "\r\n" in original else "\n"
    data = original.replace("\r\n", "\n")
    data = replace_once(data, "Imports System.ComponentModel\n",
                        "Imports System.ComponentModel\nImports System.Linq\n", "Linq import")

    helpers = '''    ' Persist last locations separately for each kind of picker.
    Private Const RecentKey As String = "Software\\DISMGUI\\RecentPaths"
    Private closeAfterUnmount As Boolean = False
    Private activeDismountTarget As WimMountSafety.Target = Nothing
    Private activeDismountJournal As WimMountSafety.OperationRecord = Nothing

    Private Function RecentDirectory(ByVal category As String) As String
        Try
            Using k = Registry.CurrentUser.OpenSubKey(RecentKey, False)
                If k IsNot Nothing Then
                    Dim dir = TryCast(k.GetValue(category), String)
                    If Not String.IsNullOrWhiteSpace(dir) AndAlso System.IO.Directory.Exists(dir) Then
                        Return dir
                    End If
                End If
            End Using
        Catch
            ' Settings are optional.
        End Try
        Return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
    End Function

    Private Sub RememberDirectory(ByVal category As String, ByVal dir As String)
        If String.IsNullOrWhiteSpace(dir) Then Return
        Try
            Using k = Registry.CurrentUser.CreateSubKey(RecentKey)
                k.SetValue(category, dir, RegistryValueKind.String)
            End Using
        Catch
            ' Lack of registry write access must not block DISM.
        End Try
    End Sub

    Private Function ChooseFolderModern(ByVal category As String, ByVal caption As String) As String
        Using picker As New FolderBrowserDialog()
            picker.Description = caption
            picker.UseDescriptionForTitle = True
            picker.AutoUpgradeEnabled = True
            picker.ShowNewFolderButton = True
            picker.SelectedPath = RecentDirectory(category)
            If picker.ShowDialog(Me) <> DialogResult.OK Then Return Nothing
            If Not System.IO.Directory.Exists(picker.SelectedPath) Then Return Nothing
            RememberDirectory(category, picker.SelectedPath)
            Return picker.SelectedPath
        End Using
    End Function

    Private Function ChooseWIM(ByVal category As String) As String
        Using picker As New OpenFileDialog()
            picker.Title = "Choose a WIM file"
            picker.Filter = "WIM files (*.wim)|*.wim|All files (*.*)|*.*"
            picker.InitialDirectory = RecentDirectory(category)
            picker.AutoUpgradeEnabled = True
            picker.CheckFileExists = True
            picker.Multiselect = False
            If picker.ShowDialog(Me) <> DialogResult.OK Then Return Nothing
            RememberDirectory(category, System.IO.Path.GetDirectoryName(picker.FileName))
            Return picker.FileName
        End Using
    End Function

    Private Function IsEmptyFolder(ByVal path As String) As Boolean
        Return Not System.IO.Directory.EnumerateFileSystemEntries(path).Any()
    End Function

    Private Function AskUnmountAction() As Boolean?
        Dim response = MessageBox.Show(Me,
            "Save the changes to the WIM before dismounting?" & Environment.NewLine &
            "Yes - Commit" & Environment.NewLine &
            "No - Discard" & Environment.NewLine &
            "Cancel - Keep the image mounted",
            "Dismount WIM", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)
        If response = DialogResult.Cancel Then Return Nothing
        Return response = DialogResult.Yes
    End Function

    Private Sub StartUnmount()
        If BackgroundWorkerDisMount.IsBusy OrElse BackgroundWorkerMount.IsBusy OrElse
           BackgroundWorkerDISMCommand.IsBusy Then Return
        Dim commit = AskUnmountAction()
        If Not commit.HasValue Then Return
        Dim chosen As WimMountSafety.Target
        Try
            ' WIMGAPI registration is the source of truth, NOT old UI fields.
            chosen = WimMountSafety.ValidateDismount(
                txtWIM.Text, cmbIndex.Text, txtMount.Text, commit.Value)
        Catch ex As Exception
            RuntimeDiagnostics.RecordException("DISM dismount preflight", ex)
            MessageBox.Show(Me,
                "DISM was NOT started. Refresh WIM 360 / inspect the selected index." &
                Environment.NewLine & ex.Message,
                "Dismount safety check", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        Try
            ' Failure to persist the recovery journal prevents starting DISM.
            activeDismountJournal = WimMountSafety.BeginDismount(chosen)
        Catch ex As Exception
            RuntimeDiagnostics.RecordException("DISM dismount journal", ex)
            MessageBox.Show(Me, "DISM was NOT started because the local safety " &
                "journal could not be written." & Environment.NewLine & ex.Message,
                "Dismount safety check", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        activeDismountTarget = chosen
        blnDISMCommit = chosen.Commit
        strFolderName = chosen.MountFolder
        strIndex = chosen.ImageIndex.ToString(
            Globalization.CultureInfo.InvariantCulture)
        strWIM = chosen.ImageFile
        txtOutput.Text = ""
        strOutput = ""
        strDISMExitCode = ""
        Try
            BackgroundWorkerDisMount.RunWorkerAsync()
        Catch ex As Exception
            RuntimeDiagnostics.RecordException("DISM dismount start", ex)
            Try
                WimMountSafety.FinishDismount(activeDismountJournal, False, "",
                    "DISM worker failed to start: " & ex.GetType().Name)
            Catch
            End Try
            activeDismountTarget = Nothing
            activeDismountJournal = Nothing
            MessageBox.Show(Me, "DISM was NOT started: " & ex.Message,
                            "Dismount failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        frmProgress.ShowDialog(Me)
        txtOutput.Text = strOutput
    End Sub

    Private Sub ShowUnmountResult(ByVal dismounted As Boolean,
                                  ByVal journalSaved As Boolean, ByVal failure As String)
        If Me.IsDisposed OrElse Me.Disposing Then Return
        Dim verified = dismounted AndAlso journalSaved
        Dim caption = "DISM GUI - Dismount WIM"
        Dim message = If(verified, "WIM image successfully dismounted.",
                         If(dismounted,
                            "WIM was successfully dismounted, but the local safety journal " &
                            "could not be updated. " & failure & Environment.NewLine &
                            "Mount selection has been refreshed. Inspect Mount health.",
                            "Dismount was not verified. " & failure & Environment.NewLine &
                            "Review WIM 360 > Mount health before any manual recovery. " &
                            "Never automatically retry an interrupted Commit." &
                            Environment.NewLine & "See the DISM output for details."))
        Me.Activate()
        MessageBox.Show(Me, message, caption, MessageBoxButtons.OK,
                        If(verified, MessageBoxIcon.Information,
                           If(dismounted, MessageBoxIcon.Warning, MessageBoxIcon.Error)))
    End Sub

'''
    data = replace_once(data, "    Private Sub btnOpenWIM_Click",
                        helpers + "    Private Sub btnOpenWIM_Click", "helper insertion")

    data = replace_block(data, "    Private Sub btnOpenWIM_Click",
                         "    Private Sub btnOpenMount_Click", '''    Private Sub btnOpenWIM_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles btnOpenWIM.Click
        Dim fileName = ChooseWIM("WIM")
        If fileName IsNot Nothing Then txtWIM.Text = fileName
    End Sub''', "WIM browser")
    data = replace_block(data, "    Private Sub btnOpenMount_Click",
                         "    Private Sub btnMount_Click", '''    Private Sub btnOpenMount_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles btnOpenMount.Click
        Dim folder = ChooseFolderModern("Mount", "Choose an empty WIM mount folder")
        If folder Is Nothing Then Return
        Try
            If Not IsEmptyFolder(folder) Then
                MessageBox.Show(Me, "Please select an empty mount folder.", "DISM GUI",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            txtMount.Text = folder
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Cannot access folder",
                MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub''', "mount folder browser")

    data = replace_block(data, "    Private Sub frmMain_FormClosing",
                         "    Private Sub btnDismount_Click", '''    Private Sub frmMain_FormClosing(ByVal sender As Object, ByVal e As System.Windows.Forms.FormClosingEventArgs) Handles Me.FormClosing
        If closeAfterUnmount Then Return
        If MessageBox.Show(Me, "Are you sure you want to exit?", "Exit?",
                           MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
            e.Cancel = True
            Return
        End If
        If WIMMounted Then
            Dim response = MessageBox.Show(Me, "A WIM is mounted. Dismount it before exiting?",
                                           "WIM Mounted", MessageBoxButtons.YesNoCancel,
                                           MessageBoxIcon.Warning)
            If response = DialogResult.Cancel Then
                e.Cancel = True
            ElseIf response = DialogResult.Yes Then
                e.Cancel = True
                closeAfterUnmount = True
                StartUnmount()
                If WIMMounted AndAlso Not BackgroundWorkerDisMount.IsBusy Then closeAfterUnmount = False
            End If
        End If
    End Sub''', "form exit")
    data = replace_block(data, "    Private Sub btnDismount_Click",
                         "    Private Sub btnOpenFolder_Click", '''    Private Sub btnDismount_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles btnDismount.Click
        StartUnmount()
    End Sub''', "unmount button")

    data = replace_once(data, '''        If MessageBox.Show("Do you want to commit changes?", "WIM Mounted", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
            blnDISMCommit = True
        Else
            blnDISMCommit = False
        End If
''', "", "worker thread prompt")
    # Always consume both redirected streams: a full stderr pipe must not deadlock DISM.
    data = replace_once(data, '''        strOutput = strOutput & vbCr & vbCr & DISM.StandardOutput.ReadToEnd()
        DISM.WaitForExit()
        strDISMExitCode = DISM.ExitCode
    End Sub

    Private Sub BackgroundWorkerDisMount_RunWorkerCompleted''',
'''        Dim stdoutTask = DISM.StandardOutput.ReadToEndAsync()
        Dim stderrTask = DISM.StandardError.ReadToEndAsync()
        DISM.WaitForExit()
        strOutput &= Environment.NewLine & stdoutTask.GetAwaiter().GetResult()
        Dim stderr = stderrTask.GetAwaiter().GetResult()
        If Not String.IsNullOrWhiteSpace(stderr) Then strOutput &= Environment.NewLine & stderr
        strDISMExitCode = DISM.ExitCode.ToString()
    End Sub

    Private Sub BackgroundWorkerDisMount_RunWorkerCompleted''', "unmount streams")
    # Validate once on the foreground UI thread and once immediately
    # before DISM.Start() in the background worker. External mount changes
    # between the confirmation and actual process launch must fail closed.
    worker_begin = data.find("    Private Sub BackgroundWorkerDisMount_DoWork")
    worker_end = data.find("    Private Sub BackgroundWorkerDisMount_RunWorkerCompleted",
                           worker_begin + 1)
    if worker_begin < 0 or worker_end < 0:
        raise RuntimeError("Cannot locate DISM dismount worker for final safety guard")
    dismount_worker = data[worker_begin:worker_end]
    # The pinned upstream has one DISM.Start in each Commit/Discard branch.
    # Validate once BEFORE selecting either command, not only one branch.
    branch_start = "        If blnDISMCommit = True Then\n"
    if (dismount_worker.count(branch_start) != 1 or
            dismount_worker.count("            DISM.Start()\n") != 2):
        raise RuntimeError(
            "Expected one Commit/Discard decision with two DISM process launches")
    dismount_worker = dismount_worker.replace(branch_start,
        "        ' Immediately revalidate the mount, index and access mode.\n"
        "        WimMountSafety.ValidateDismount(strWIM, strIndex, strFolderName,\n"
        "                                         blnDISMCommit)\n"
        + branch_start)
    data = data[:worker_begin] + dismount_worker + data[worker_end:]

    data = replace_block(data, "    Private Sub BackgroundWorkerDisMount_RunWorkerCompleted",
                         "    Private Sub btnOpnDriverFolder_Click", '''    Private Sub BackgroundWorkerDisMount_RunWorkerCompleted(ByVal sender As Object, ByVal e As System.ComponentModel.RunWorkerCompletedEventArgs) Handles BackgroundWorkerDisMount.RunWorkerCompleted
        Dim dismounted = (e.Error Is Nothing AndAlso Not e.Cancelled AndAlso
                          strDISMExitCode = "0")
        Dim details As String = If(e.Error IsNot Nothing, e.Error.Message,
                                   "DISM exit code: " & strDISMExitCode)
        If dismounted Then
            Try
                dismounted = activeDismountTarget IsNot Nothing AndAlso
                             WimMountSafety.VerifyNoLongerMounted(activeDismountTarget)
                If Not dismounted Then
                    details &= " | Image still registered as mounted or cannot be verified."
                End If
            Catch ex As Exception
                RuntimeDiagnostics.RecordException("DISM dismount postflight", ex)
                dismounted = False
                details &= " | Mount registration check failed: " & ex.Message
            End Try
        End If
        Dim journalSaved As Boolean = True
        Try
            If activeDismountJournal Is Nothing Then Throw New InvalidOperationException(
                "No pending dismount journal entry exists.")
            WimMountSafety.FinishDismount(activeDismountJournal, dismounted,
                strDISMExitCode, If(dismounted, "", details))
        Catch ex As Exception
            RuntimeDiagnostics.RecordException("DISM dismount journal completion", ex)
            journalSaved = False
            details &= " | Local safety journal could not be updated: " & ex.Message
        End Try
        activeDismountTarget = Nothing
        activeDismountJournal = Nothing
        ' The UI must reflect the ACTUAL WIMGAPI state even when journaling
        ' fails after a verified dismount. Never leave a false mounted flag.
        If dismounted Then
            btnMount.Enabled = True
            btnDismount.Enabled = False
            WIMMounted = False
            strMountedImageLocation = ""
        End If
        frmProgress.Close()
        ' Post the result AFTER the progress window is closed.
        Me.BeginInvoke(New MethodInvoker(Sub()
            ShowUnmountResult(dismounted, journalSaved, details)
            If closeAfterUnmount Then
                If dismounted AndAlso journalSaved Then
                    Me.Close()
                Else
                    closeAfterUnmount = False
                End If
            End If
        End Sub))
    End Sub''', "unmount result")

    data = replace_once(data, "        txtDriverLocation.Text = OpenFolder()",
                        '''        Dim selected = OpenFolder("Drivers")
        If selected IsNot Nothing Then txtDriverLocation.Text = selected''', "driver folder cancel")
    data = replace_once(data, "        txtPackageFile.Text = OpenFolder()",
                        '''        Dim selected = OpenFolder("Packages")
        If selected IsNot Nothing Then txtPackageFile.Text = selected''', "package folder cancel")
    data = replace_block(data, "    Private Function OpenFolder()",
                         "    Private Sub btnGetAllDriverInfo_Click", '''    Private Function OpenFolder(ByVal category As String) As String
        Return ChooseFolderModern(category, "Choose " & category & " folder")
    End Function''', "generic folder browser")
    data = replace_block(data, "    Private Sub btnCaptureSrc_Click",
                         "    Private Sub btnCaptureDest_Click", '''    Private Sub btnCaptureSrc_Click(sender As System.Object, e As System.EventArgs) Handles btnCaptureSrc.Click
        Dim folder = ChooseFolderModern("CaptureSource", "Choose a source folder to capture")
        If folder Is Nothing Then Return
        Try
            If IsEmptyFolder(folder) Then
                MessageBox.Show(Me, "You must choose a non-empty capture source folder.",
                    "DISM GUI", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            txtCaptureSource.Text = folder
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Cannot access folder",
                MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub''', "capture source")
    data = replace_block(data, "    Private Sub btnCaptureDest_Click",
                         "    Private Sub btnAppend_Click", '''    Private Sub btnCaptureDest_Click(sender As System.Object, e As System.EventArgs) Handles btnCaptureDest.Click
        Dim fileName = ChooseWIM("CaptureWIM")
        If fileName IsNot Nothing Then txtCaptureDest.Text = fileName
    End Sub''', "capture WIM")
    data = replace_block(data, "    Private Sub btnBrowseSource_Click",
                         "    Private Sub btnBrowseDest_Click", '''    Private Sub btnBrowseSource_Click(sender As System.Object, e As System.EventArgs) Handles btnBrowseSource.Click
        Dim fileName = ChooseWIM("ApplyWIM")
        If fileName IsNot Nothing Then txtxApplySource.Text = fileName
    End Sub''', "apply source WIM")
    data = replace_block(data, "    Private Sub btnBrowseDest_Click",
                         "    Private Sub btnApply_Click", '''    Private Sub btnBrowseDest_Click(sender As System.Object, e As System.EventArgs) Handles btnBrowseDest.Click
        Dim folder = ChooseFolderModern("ApplyDestination", "Choose an empty apply destination folder")
        If folder Is Nothing Then Return
        Try
            If Not IsEmptyFolder(folder) Then
                MessageBox.Show(Me, "Please select an empty apply destination folder.",
                    "DISM GUI", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            txtApplyDest.Text = folder
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Cannot access folder",
                MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub''', "apply destination")

    backup = file.with_suffix(".vb.before-modern-dialogs.bak")
    if not backup.exists():
        backup.write_text(original, encoding="utf-8-sig")
    file.write_text(data.replace("\n", newline), encoding="utf-8-sig")
    print("PATCHED:", file)


if __name__ == "__main__":
    main()
