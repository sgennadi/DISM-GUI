Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports Microsoft.Win32
Imports System.Security.Principal
Imports System.Text
Imports System.Text.Json

' Fail-closed preflight and LOCAL recovery journal for DISM Mount / Commit / Discard.
' Does not run DISM, clean stale mountpoints, remount, delete, or write WIM content.
' An interrupted commit may have reached disk: never auto retry or auto discard.
Public NotInheritable Class WimMountSafety
    Private Sub New()
    End Sub

    Public NotInheritable Class Target
        Public Property ImageFile As String = ""
        Public Property ImageIndex As Integer
        Public Property MountFolder As String = ""
        Public Property MountStatus As String = ""
        Public Property Commit As Boolean
    End Class

    Public NotInheritable Class OperationRecord
        Public Property Id As String = ""
        Public Property StartedUtc As DateTime
        Public Property FinishedUtc As DateTime?
        Public Property ImageFile As String = ""
        Public Property ImageIndex As Integer
        Public Property MountFolder As String = ""
        Public Property Action As String = ""
        Public Property State As String = "Pending"
        Public Property ExitCode As String = ""
        Public Property Detail As String = ""
    End Class

    Private Shared Function NormalizeFile(value As String) As String
        If String.IsNullOrWhiteSpace(value) Then
            Throw New ArgumentException("Choose a WIM/ESD source before dismount.")
        End If
        Return Path.GetFullPath(value.Trim().Trim(""""c)).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
    End Function

    Private Shared Function NormalizeFolder(value As String) As String
        If String.IsNullOrWhiteSpace(value) Then
            Throw New ArgumentException("Choose the existing mount folder.")
        End If
        Dim full = Path.GetFullPath(value.Trim().Trim(""""c))
        Dim root = Path.GetPathRoot(full)
        If String.Equals(full.TrimEnd("\"c, "/"c),
                         If(root, "").TrimEnd("\"c, "/"c),
                         StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException(
                "A volume root cannot be used as the WIM mount folder.")
        End If
        Return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
    End Function

    ' Injectable pure validator, used by CI without a real mounted WIM.
    ' A stale selection, R/O commit, or unknown mount state always blocks work.
    Public Shared Function ValidateRegisteredTarget(report As Wim360Reader.WimReport,
                                    index As Integer, mountFolder As String,
                                    commit As Boolean) As Target
        If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
        If report.IsSplitSet Then Throw New InvalidOperationException(
            "Split SWM sets cannot be committed or discarded as a mounted WIM.")
        If index < 1 Then Throw New ArgumentOutOfRangeException(NameOf(index))
        Dim selected = report.Images.FirstOrDefault(Function(x) x.Index = index)
        If selected Is Nothing Then Throw New InvalidOperationException(
            "The selected image index no longer exists. Run Inspect again.")
        Dim folder = NormalizeFolder(mountFolder)
        If Not Directory.Exists(folder) Then Throw New DirectoryNotFoundException(
            "Selected mount folder no longer exists: " & folder)
        If (File.GetAttributes(folder) And FileAttributes.ReparsePoint) =
           FileAttributes.ReparsePoint Then
            Throw New InvalidOperationException("A linked or redirected mount folder is not allowed.")
        End If
        Dim registered = If(selected.MountFolder, "")
        If String.IsNullOrWhiteSpace(registered) Then Throw New InvalidOperationException(
            "The selected image is not registered as mounted. Refresh WIM 360 / Mount health.")
        registered = NormalizeFolder(registered)
        If Not String.Equals(folder, registered, StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException(
                "Mount folder differs from the WIM image/index registration. " &
                "No DISM command was started; refresh the selection.")
        End If
        If commit Then
            If selected.MountStatus <> "Mounted (R/W)" Then
                Throw New InvalidOperationException(
                    "Commit requires an active READ/WRITE mount. Current: " &
                    selected.MountStatus & ". Use Discard only after inspecting the registration.")
            End If
        ElseIf selected.MountStatus <> "Mounted (R/W)" AndAlso
               selected.MountStatus <> "Mounted (read-only)" Then
            Throw New InvalidOperationException(
                "Discard requires an active, valid mounted index. Current: " &
                selected.MountStatus & ". No automatic mount repair was attempted.")
        End If
        Return New Target With {
            .ImageFile = NormalizeFile(report.ImageFile),
            .ImageIndex = index,
            .MountFolder = folder,
            .MountStatus = selected.MountStatus,
            .Commit = commit
        }
    End Function

    ' Prevent repeat /Mount-WIM before launching DISM. The folder may be
    ' empty even while an Invalid WIMMount registration still exists.
    ' No registry mutations, WIM writes, cleanup or recovery are performed.
    ' Injectable inventory permits regression tests without mounting a WIM.
    Public Shared Function ValidateNewMountTarget(
        report As Wim360Reader.WimReport, index As Integer, mountFolder As String,
        mountReadOnly As Boolean,
        registrations As IEnumerable(Of Wim360Reader.MountedImageRegistration)
    ) As String
        If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
        If registrations Is Nothing Then Throw New ArgumentNullException(NameOf(registrations))
        If report.IsSplitSet Then Throw New InvalidOperationException(
            "A split SWM set cannot be mounted with this action.")
        If Not String.IsNullOrWhiteSpace(report.MountScanWarning) Then
            Throw New InvalidOperationException(
                "Cannot verify existing mounts: " & report.MountScanWarning &
                ". DISM Mount was NOT started.")
        End If
        Dim selected = report.Images.SingleOrDefault(Function(x) x.Index = index)
        If index < 1 OrElse selected Is Nothing Then
            Throw New InvalidOperationException(
                "The selected WIM index is missing. Refresh WIM 360 before mounting.")
        End If
        Dim wimPath = NormalizeFile(report.ImageFile)
        Dim folder = NormalizeFolder(mountFolder)
        If Not String.Equals(selected.MountStatus, "Not mounted",
                             StringComparison.Ordinal) Then
            Throw New InvalidOperationException(
                "This WIM index is already registered: " & selected.MountStatus &
                If(String.IsNullOrWhiteSpace(selected.MountFolder), "",
                   " | Mount directory: " & selected.MountFolder) &
                ". DISM Mount was NOT started (0xC1420127 guard). " &
                "Open WIM 360 > Mount health / Recovery; an Invalid registration " &
                "must not be deleted or discarded automatically.")
        End If

        ' Inspect all WIM registrations: another image can reserve this folder,
        ' or the same WIM can have a conflicting R/W or invalid index.
        For Each entry As Wim360Reader.MountedImageRegistration In registrations
            If entry Is Nothing OrElse String.IsNullOrWhiteSpace(entry.ImageFile) OrElse
               String.IsNullOrWhiteSpace(entry.MountFolder) Then
                Throw New InvalidOperationException(
                    "Incomplete system WIMMount registration. Mount blocked; review Mount health.")
            End If
            Dim registeredWim = NormalizeFile(entry.ImageFile)
            Dim registeredFolder = NormalizeFolder(entry.MountFolder)
            Dim sameWim = String.Equals(wimPath, registeredWim,
                                        StringComparison.OrdinalIgnoreCase)
            If String.Equals(folder, registeredFolder,
                             StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidOperationException(
                    "The selected mount folder is reserved by a WIMMount registration " &
                    "(index " & entry.ImageIndex.ToString(CultureInfo.InvariantCulture) &
                    ", " & entry.Status & "). DISM Mount was NOT started. " &
                    "Open WIM 360 > Mount health.")
            End If
            If sameWim AndAlso entry.ImageIndex = index Then
                Throw New InvalidOperationException(
                    "The same WIM/index already has a registered mount at " &
                    registeredFolder & " (" & entry.Status & "). " &
                    "Do not mount again; inspect WIM 360 > Mount health.")
            End If
            If sameWim AndAlso (entry.IsReadWrite OrElse Not mountReadOnly OrElse
                                Not String.Equals(entry.Status, "Mounted (read-only)",
                                                  StringComparison.Ordinal)) Then
                Throw New InvalidOperationException(
                    "The same WIM has another R/W or non-healthy registered index " &
                    "(" & entry.Status & "). Verify its state in Mount health " &
                    "before starting another mount.")
            End If
        Next
        If Not Directory.Exists(folder) Then
            Throw New DirectoryNotFoundException(
                "Mount folder does not exist: " & folder)
        End If
        If (File.GetAttributes(folder) And FileAttributes.ReparsePoint) =
           FileAttributes.ReparsePoint Then
            Throw New InvalidOperationException(
                "Mount directory is a symlink/junction. Select an ordinary empty folder.")
        End If
        If Directory.EnumerateFileSystemEntries(folder).Any() Then
            Throw New InvalidOperationException(
                "Mount folder is not empty. Do not erase its contents; " &
                "select a new empty folder after verifying mount registrations.")
        End If
        Return folder
    End Function

    Public Shared Function ValidateNewMount(wimPath As String, indexText As String,
                                            mountFolder As String,
                                            mountReadOnly As Boolean) As String
        Dim index As Integer
        If Not Integer.TryParse(If(indexText, "").Trim(), NumberStyles.None,
                                CultureInfo.InvariantCulture, index) OrElse index < 1 Then
            Throw New ArgumentException("Select a valid numeric WIM index.")
        End If
        Dim source = NormalizeFile(wimPath)
        If Not File.Exists(source) Then
            Throw New FileNotFoundException("WIM file was not found.", source)
        End If
        Dim report = Wim360Reader.Inspect(source)
        Return ValidateNewMountTarget(report, index, mountFolder, mountReadOnly,
                                      Wim360Reader.MountedImageRegistrations())
    End Function

    Public Shared Function ValidateDismount(wimPath As String, indexText As String,
                                            mountFolder As String,
                                            commit As Boolean) As Target
        Dim index As Integer
        If Not Integer.TryParse(If(indexText, "").Trim(),
                                NumberStyles.None, CultureInfo.InvariantCulture, index) OrElse
           index < 1 Then
            Throw New ArgumentException("Select a numeric WIM image index before dismount.")
        End If
        Dim filename = NormalizeFile(wimPath)
        If Not File.Exists(filename) Then Throw New FileNotFoundException(
            "Selected WIM/ESD file no longer exists.", filename)
        Dim report = Wim360Reader.Inspect(filename)
        Return ValidateRegisteredTarget(report, index, mountFolder, commit)
    End Function

    Private Shared Function JournalFolder() As String
        Dim root = Environment.GetEnvironmentVariable("DISMGUI_TEST_MOUNT_JOURNAL")
        If String.IsNullOrWhiteSpace(root) Then
            root = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
                "DISMGUI", "MountSafety")
        End If
        Return Path.GetFullPath(root)
    End Function

    Private Shared Function RecordFile(operationId As String) As String
        If operationId Is Nothing OrElse operationId.Length <> 32 OrElse
           operationId.Any(Function(c) Not Uri.IsHexDigit(c)) Then
            Throw New InvalidDataException("Invalid mount journal identifier.")
        End If
        Return Path.Combine(JournalFolder(), operationId & ".json")
    End Function

    ' Write locally BEFORE starting DISM. The journal never stores raw DISM
    ' stdout/stderr, credentials or package payloads. A failed write blocks
    ' the potentially destructive operation, rather than losing recovery state.
    Private Shared Sub SaveRecord(record As OperationRecord)
        Dim folder = JournalFolder()
        Directory.CreateDirectory(folder)
        Dim destination = RecordFile(record.Id)
        Dim temp = destination & "." & Guid.NewGuid().ToString("N") & ".tmp"
        Try
            Dim bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record))
            Using output As New FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                                          FileShare.None, 4096, FileOptions.WriteThrough)
                output.Write(bytes, 0, bytes.Length)
                output.Flush(True)
            End Using
            File.Move(temp, destination, True)
        Finally
            If File.Exists(temp) Then
                Try
                    File.Delete(temp)
                Catch
                End Try
            End If
        End Try
    End Sub

    Public Shared Function BeginDismount(target As Target) As OperationRecord
        If target Is Nothing Then Throw New ArgumentNullException(NameOf(target))
        Dim record As New OperationRecord With {
            .Id = Guid.NewGuid().ToString("N"),
            .StartedUtc = DateTime.UtcNow,
            .ImageFile = target.ImageFile,
            .ImageIndex = target.ImageIndex,
            .MountFolder = target.MountFolder,
            .Action = If(target.Commit, "Commit", "Discard"),
            .State = "Pending",
            .Detail = "DISM action started. Outcome not yet verified."
        }
        SaveRecord(record)
        Return record
    End Function

    Public Shared Sub FinishDismount(record As OperationRecord, verified As Boolean,
                                     exitCode As String, detail As String)
        If record Is Nothing Then Return
        record.FinishedUtc = DateTime.UtcNow
        record.State = If(verified, "Verified", "Review required")
        record.ExitCode = If(exitCode, "")
        record.Detail = If(verified, "DISM success and the registered mount is absent.",
                           If(String.IsNullOrWhiteSpace(detail),
                              "DISM completion is uncertain. Inspect the mount manually.",
                              detail))
        SaveRecord(record)
    End Sub

    Public Shared Function VerifyNoLongerMounted(target As Target) As Boolean
        If target Is Nothing Then Throw New ArgumentNullException(NameOf(target))
        Dim report = Wim360Reader.Inspect(target.ImageFile)
        If Not String.IsNullOrWhiteSpace(report.MountScanWarning) Then
            Throw New InvalidOperationException(
                "Mount registration status is unavailable after DISM.")
        End If
        Dim image = report.Images.FirstOrDefault(Function(x) x.Index = target.ImageIndex)
        Return image IsNot Nothing AndAlso image.MountStatus = "Not mounted"
    End Function

    Public Shared Function RecentOperations(Optional limit As Integer = 25
                                            ) As List(Of OperationRecord)
        Dim list As New List(Of OperationRecord)()
        Dim folder = JournalFolder()
        If Not Directory.Exists(folder) Then Return list
        For Each journalPath As String In Directory.EnumerateFiles(folder, "*.json").
                         OrderByDescending(Function(x) IO.File.GetLastWriteTimeUtc(x)).
                         Take(Math.Max(1, Math.Min(limit, 100)))
            Try
                Dim info As New FileInfo(journalPath)
                If info.Length <= 0 OrElse info.Length > 32768 Then Continue For
                Dim loaded = JsonSerializer.Deserialize(Of OperationRecord)(
                    IO.File.ReadAllText(journalPath, Encoding.UTF8))
                If loaded IsNot Nothing AndAlso
                   Not String.IsNullOrWhiteSpace(loaded.Id) Then list.Add(loaded)
            Catch
                ' One corrupt journal record must never hide other mount history.
            End Try
        Next
        Return list
    End Function

    ' DISM returns HRESULT 0xC1420117 after it has reached 100% when
    ' Windows still has handles open underneath the mount directory.
    ' A failed unmount never authorizes automatic cleanup or another Commit.
    Public Shared Function IsIncompleteUnmount(exitCode As String) As Boolean
        Dim raw = If(exitCode, "").Trim()
        If raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) Then
            Dim unsignedCode As ULong
            Return ULong.TryParse(raw.Substring(2), NumberStyles.HexNumber,
                                  CultureInfo.InvariantCulture, unsignedCode) AndAlso
                   unsignedCode = &HC1420117UL
        End If
        Dim numericCode As Long
        Return Long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture,
                             numericCode) AndAlso
               (numericCode = -1052638953L OrElse numericCode = 3242328343L)
    End Function

    Public Shared Function FailureGuidance(exitCode As String, wasCommit As Boolean,
                                           Optional registration As String = "") As String
        Dim state = If(String.IsNullOrWhiteSpace(registration),
                       "Mount registration has not been verified.", registration)
        If IsIncompleteUnmount(exitCode) Then
            Return "DISM could not completely release the mount folder (0xC1420117)." &
                Environment.NewLine & state & Environment.NewLine &
                "A process may still have files or a current directory open inside the mount: " &
                "Explorer, Total Commander, a terminal, an editor, a file scan or antivirus." &
                Environment.NewLine &
                "Close windows using the folder and check Resource Monitor > CPU > " &
                "Associated Handles for its path. Refresh WIM 360 Mount health before " &
                "choosing any further action." & Environment.NewLine &
                If(wasCommit,
                   "A failed Commit may already have written changes. Do not retry Commit " &
                   "or switch to Discard automatically.",
                   "Do not automatically repeat Discard; check whether DISM still " &
                   "registers the image as mounted.") &
                Environment.NewLine &
                "Do not delete or take ownership of the mount folder or run " &
                "Cleanup-Mountpoints until its state has been reviewed."
        End If
        Return "DISM did not verify the dismount (exit " & If(exitCode, "") & "). " &
            state & " Review WIM 360 Mount health and the DISM log. " &
            "Do not automatically retry a Commit or Discard."
    End Function

    ' Mount HEALTH has no write or repair capability. A raw registry status
    ' value is internal WIMMount metadata, NOT the DISM Status enumeration.
    Public Shared Function FormatRegistryMountEntry(keyName As String,
                                                     mountPath As String,
                                                     imagePath As String,
                                                     imageIndex As String,
                                                     statusRaw As Integer?) As String
        Dim statusText = If(statusRaw.HasValue,
                            "0x" & statusRaw.GetValueOrDefault().ToString(
                                "X8", CultureInfo.InvariantCulture),
                            "unavailable")
        Return "WIMMount entry " & If(keyName, "") & Environment.NewLine &
            "  Mount: " & If(mountPath, "") & Environment.NewLine &
            "  Image: " & If(imagePath, "") & " | index " & If(imageIndex, "") &
            Environment.NewLine &
            "  Raw registry Status: " & statusText &
            " (not a DISM mount-state code)"
    End Function

    Public Shared Function InvalidMountGuidance(mountStatus As String) As String
        Dim value = If(mountStatus, "").Trim()
        If value.StartsWith("Invalid", StringComparison.OrdinalIgnoreCase) Then
            Return "REVIEW REQUIRED: WIMGAPI reports an invalid mount. " &
                "A successful /Cleanup-Mountpoints exit does not prove that " &
                "the registration was removed." & Environment.NewLine &
                "Close Explorer, Total Commander, editors and terminals using the " &
                "mount directory. If not already done, reboot Windows and check " &
                "DISM /Get-MountedImageInfo again." & Environment.NewLine &
                "If /Remount-Image and /Cleanup-Mountpoints already failed, " &
                "do not repeat them blindly. Preserve DISM logs and use a " &
                "manual administrator review." & Environment.NewLine &
                "Do NOT delete WIMMount registry keys, force-delete the mount " &
                "folder, stop the WIMMount driver or retry Commit automatically."
        End If
        If String.Equals(value, "Needs remount", StringComparison.OrdinalIgnoreCase) Then
            Return "REVIEW REQUIRED: DISM indicates Needs Remount. " &
                "Review DISM /Get-MountedImageInfo and use the documented " &
                "/Remount-Image procedure only when appropriate. No auto repair."
        End If
        Return ""
    End Function

    ' Read the SYSTEM-WIDE WIMMount registry only; never write keys, touch
    ' open handles, run DISM cleanup or infer that an absent entry permits deletion.
    Private Shared Function ReadRegistryMountReport(chosenWim As String) As List(Of String)
        Dim lines As New List(Of String) From {
            "WIMMount registry registrations (read-only)",
            "Registry raw Status is NOT a DISM Invalid/OK status code."
        }
        Dim wanted As String = ""
        Try
            If Not String.IsNullOrWhiteSpace(chosenWim) Then
                wanted = Path.GetFullPath(chosenWim.Trim().Trim(""""c)).
                    TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            End If
            Dim view = If(Environment.Is64BitOperatingSystem,
                          RegistryView.Registry64, RegistryView.Registry32)
            Using machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view)
                Using root = machine.OpenSubKey("SOFTWARE\Microsoft\WIMMount\Mounted Images", False)
                    If root Is Nothing Then
                        lines.Add("Registration key not found; this is not evidence " &
                                  "that the mount folder can be removed.")
                        Return lines
                    End If
                    Dim entries = root.GetSubKeyNames().OrderBy(
                        Function(x) x, StringComparer.OrdinalIgnoreCase).Take(128)
                    Dim matched As Integer = 0
                    For Each subkey As String In entries
                        Using record = root.OpenSubKey(subkey, False)
                            If record Is Nothing Then Continue For
                            Dim mountPath = TryCast(record.GetValue("Mount Path"), String)
                            Dim imagePath = TryCast(record.GetValue("WIM Path"), String)
                            If wanted.Length > 0 Then
                                Dim candidate As String = ""
                                Try
                                    candidate = Path.GetFullPath(If(imagePath, "")).
                                        TrimEnd(Path.DirectorySeparatorChar,
                                                Path.AltDirectorySeparatorChar)
                                Catch
                                    Continue For
                                End Try
                                If Not String.Equals(wanted, candidate,
                                            StringComparison.OrdinalIgnoreCase) Then Continue For
                            End If
                            Dim raw As Object = record.GetValue("Status")
                            Dim rawValue As Integer? = Nothing
                            If TypeOf raw Is Integer Then rawValue = DirectCast(raw, Integer)
                            Dim indexText = Convert.ToString(record.GetValue("Image Index"),
                                                              CultureInfo.InvariantCulture)
                            lines.Add(FormatRegistryMountEntry(subkey, mountPath,
                                      imagePath, indexText, rawValue))
                            matched += 1
                        End Using
                    Next
                    If matched = 0 Then lines.Add(
                        "No matching registry entries found; this alone does NOT " &
                        "confirm that the mount folder is safe to delete.")
                End Using
            End Using
        Catch ex As Exception When TypeOf ex Is UnauthorizedAccessException OrElse
                                  TypeOf ex Is System.Security.SecurityException OrElse
                                  TypeOf ex Is IOException OrElse
                                  TypeOf ex Is ArgumentException
            lines.Add("Registry inspection unavailable: " & ex.Message)
        End Try
        Return lines
    End Function

    Public Shared Function BuildHealthReport(Optional chosenWim As String = ""
                                             ) As String
        Dim lines As New List(Of String) From {
            "WIM 360 | MOUNT HEALTH (read-only)",
            "No automatic cleanup, remount, commit or discard will be performed.",
            ""
        }
        If Not String.IsNullOrWhiteSpace(chosenWim) Then
            Try
                Dim report = Wim360Reader.Inspect(chosenWim)
                lines.Add("Source: " & report.ImageFile)
                If Not String.IsNullOrWhiteSpace(report.MountScanWarning) Then
                    lines.Add("WARNING: " & report.MountScanWarning)
                End If
                For Each image In report.Images
                    lines.Add("Index " & image.Index.ToString() & " | " &
                              image.MountStatus & " | " & image.MountFolder)
                    Dim advice = InvalidMountGuidance(image.MountStatus)
                    If advice.Length > 0 Then lines.Add(advice)
                Next
            Catch ex As Exception
                lines.Add("Image inspection unavailable: " & ex.Message)
            End Try
            lines.Add("")
        End If
        lines.AddRange(ReadRegistryMountReport(chosenWim))
        lines.Add("")
        Dim recent = RecentOperations()
        lines.Add("Local DISM dismount journal (" & recent.Count.ToString() & " entries):")
        For Each record In recent
            lines.Add(record.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) &
                      " UTC | " & record.Action & " | " & record.State &
                      " | index " & record.ImageIndex.ToString() &
                      " | exit " & record.ExitCode)
            lines.Add("  " & record.ImageFile & " | " & record.MountFolder)
            lines.Add("  " & record.Detail)
        Next
        lines.Add("")
        lines.Add("If a mount is still active, review its index and mode before any action.")
        lines.Add("If Commit was interrupted, the image may contain partly persisted changes.")
        lines.Add("Use DISM /Get-MountedImageInfo to verify state before manual recovery.")
        lines.Add("Do not run /Cleanup-Mountpoints or /Discard on an uncertain Commit.")
        lines.Add("Do not remove a mount folder until DISM confirms it is dismounted.")
        Return String.Join(Environment.NewLine, lines)
    End Function
End Class
