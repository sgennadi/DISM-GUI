Imports System
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json

' Guided recovery state is LOCAL and entirely read-only with respect to WIM,
' the mount directory, mounted-image registry and the WIMMount driver.
' A checkpoint is not proof that a cleanup or reboot fixed the mount.
Public NotInheritable Class MountRecoverySession
    Private Sub New()
    End Sub

    Public NotInheritable Class Checkpoint
        Public Property Schema As Integer = 1
        Public Property ImagePath As String = ""
        Public Property Index As Integer
        Public Property MountFolder As String = ""
        Public Property ObservedStatus As String = ""
        Public Property StartedUtc As DateTime
        Public Property BootEpochUtc As DateTime
        Public Property LastReviewedUtc As DateTime?
        Public Property LastOutcome As String = "Awaiting Windows restart"
    End Class

    Public NotInheritable Class Assessment
        Public Property ImageFile As String = ""
        Public Property Index As Integer
        Public Property MountFolder As String = ""
        Public Property Status As String = "Unavailable"
        Public Property Warning As String = ""
        Public Property RegistrationChecked As Boolean
        Public Property RequiresManualReview As Boolean = True
        Public Property Explanation As String = ""
    End Class

    Private Shared Function StoreFile() As String
        Dim testRoot = Environment.GetEnvironmentVariable("DISMGUI_TEST_RECOVERY_ROOT")
        If Environment.GetEnvironmentVariable("DISMGUI_TEST_DIAGNOSTICS") = "1" AndAlso
           Not String.IsNullOrWhiteSpace(testRoot) Then
            Return Path.Combine(Path.GetFullPath(testRoot), "checkpoint.json")
        End If
        Return Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
            "DISMGUI", "MountRecovery", "checkpoint.json")
    End Function

    Private Shared Function NormalizeSource(value As String) As String
        If String.IsNullOrWhiteSpace(value) OrElse
           Not Path.IsPathFullyQualified(value) Then
            Throw New ArgumentException("Choose an absolute WIM/ESD source path.")
        End If
        Dim source = Path.GetFullPath(value.Trim().Trim(""""c))
        If Not {".wim", ".esd"}.Contains(Path.GetExtension(source),
                                        StringComparer.OrdinalIgnoreCase) Then
            Throw New ArgumentException("Recovery assessment needs a WIM or ESD source.")
        End If
        Return source
    End Function

    ' Approximate Windows kernel startup time. This is a hint only; clock
    ' adjustment or a Fast Startup shutdown can affect the estimate.
    Public Shared Function BootEpochUtc() As DateTime
        Return DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64)
    End Function

    Public Shared Function RestartAppearsToHaveOccurred(
            beforeBootUtc As DateTime, afterBootUtc As DateTime) As Boolean
        Return Math.Abs((afterBootUtc - beforeBootUtc).TotalMinutes) >= 3.0
    End Function

    Public Shared Function Inspect(sourcePath As String, index As Integer) As Assessment
        Dim normalized = NormalizeSource(sourcePath)
        If index < 1 Then Throw New ArgumentOutOfRangeException(NameOf(index))
        Dim answer As New Assessment With {
            .ImageFile = normalized, .Index = index,
            .Explanation = "No remediation has been performed."
        }
        Try
            Dim report = Wim360Reader.Inspect(normalized)
            If Not String.IsNullOrWhiteSpace(report.MountScanWarning) Then
                Throw New InvalidOperationException(report.MountScanWarning)
            End If
            Dim image = report.Images.FirstOrDefault(Function(x) x.Index = index)
            If image Is Nothing Then
                answer.Warning = "The selected index is not present in the image metadata."
                Return answer
            End If
            answer.Status = image.MountStatus
            answer.MountFolder = image.MountFolder
            answer.RegistrationChecked = True
            answer.RequiresManualReview = Not String.Equals(
                image.MountStatus, "Not mounted", StringComparison.Ordinal)
            Select Case image.MountStatus
                Case "Invalid / check mount"
                    answer.Explanation =
                        "INVALID: WIMGAPI still registers a damaged mount. " &
                        "Do not delete registry entries, take ownership or force-delete folders. " &
                        "Check file locks and consider a full Windows Restart, then inspect again."
                Case "Needs remount"
                    answer.Explanation =
                        "NEEDS REMOUNT: review the DISM log and the existing remount procedure. " &
                        "This assistant does not run Remount or Cleanup."
                Case "Not mounted"
                    answer.Explanation =
                        "The image index is no longer registered as mounted. " &
                        "This alone does NOT prove the old mount folder is safe to delete. " &
                        "Review its content and WIMMount state separately."
                Case Else
                    answer.Explanation =
                        "Registered mount status: " & image.MountStatus &
                        ". Review the existing mount before any Commit, Discard or cleanup."
            End Select
        Catch ex As Exception
            answer.Warning = ex.GetType().Name & ": " & ex.Message
            answer.Explanation =
                "Mount state could not be verified; do not make any destructive changes."
        End Try
        Return answer
    End Function

    Public Shared Function LoadCheckpoint() As Checkpoint
        Dim target = StoreFile()
        If Not File.Exists(target) Then Return Nothing
        Dim file As New FileInfo(target)
        If file.Length < 2 OrElse file.Length > 32768 Then
            Throw New InvalidDataException("Unexpected recovery checkpoint size.")
        End If
        Dim record = JsonSerializer.Deserialize(Of Checkpoint)(File.ReadAllText(target))
        If record Is Nothing OrElse record.Schema <> 1 OrElse
           record.Index < 1 OrElse
           String.IsNullOrWhiteSpace(record.ImagePath) Then
            Throw New InvalidDataException("Invalid recovery checkpoint schema.")
        End If
        Return record
    End Function

    Private Shared Sub WriteCheckpoint(record As Checkpoint)
        Dim destination = StoreFile()
        Directory.CreateDirectory(Path.GetDirectoryName(destination))
        Dim temp = destination & "." & Guid.NewGuid().ToString("N") & ".tmp"
        Try
            File.WriteAllText(temp, JsonSerializer.Serialize(record,
                New JsonSerializerOptions With {.WriteIndented = True}),
                New UTF8Encoding(False))
            File.Move(temp, destination, True)
        Finally
            If File.Exists(temp) Then File.Delete(temp)
        End Try
    End Sub

    Public Shared Function BeginRestartReview(assessment As Assessment) As Checkpoint
        If assessment Is Nothing OrElse Not assessment.RegistrationChecked OrElse
           Not String.Equals(assessment.Status, "Invalid / check mount",
                             StringComparison.Ordinal) Then
            Throw New InvalidOperationException(
                "A restart checkpoint requires verified WIMGAPI Invalid status.")
        End If
        Dim record As New Checkpoint With {
            .ImagePath = assessment.ImageFile,
            .Index = assessment.Index,
            .MountFolder = assessment.MountFolder,
            .ObservedStatus = assessment.Status,
            .StartedUtc = DateTime.UtcNow,
            .BootEpochUtc = BootEpochUtc()
        }
        WriteCheckpoint(record)
        Return record
    End Function

    Public Shared Function ReviewAfterRestart(sourcePath As String,
                                              index As Integer
                                              ) As String
        Dim checkpoint = LoadCheckpoint()
        If checkpoint Is Nothing Then
            Return "No saved recovery checkpoint. First inspect a registered invalid mount."
        End If
        Dim normalized = NormalizeSource(sourcePath)
        If index <> checkpoint.Index OrElse
           Not String.Equals(normalized, checkpoint.ImagePath,
                             StringComparison.OrdinalIgnoreCase) Then
            Return "The stored checkpoint targets a different WIM/index. " &
                   "No state has been changed."
        End If
        If Not RestartAppearsToHaveOccurred(checkpoint.BootEpochUtc, BootEpochUtc()) Then
            Return "A new kernel boot has not been confirmed. Restart Windows " &
                   "(not Fast Startup shutdown) and review the mount afterward."
        End If
        Dim now = Inspect(normalized, index)
        checkpoint.LastReviewedUtc = DateTime.UtcNow
        checkpoint.LastOutcome = If(now.RegistrationChecked,
            now.Status, "Mount registration unavailable; manual review required")
        WriteCheckpoint(checkpoint)
        Return "Post-restart inspection: " & checkpoint.LastOutcome &
            Environment.NewLine & now.Explanation &
            If(String.IsNullOrWhiteSpace(now.Warning), "",
               Environment.NewLine & now.Warning)
    End Function
End Class
