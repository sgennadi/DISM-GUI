Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
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
                Next
            Catch ex As Exception
                lines.Add("Image inspection unavailable: " & ex.Message)
            End Try
            lines.Add("")
        End If
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
