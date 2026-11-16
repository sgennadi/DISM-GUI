Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

' Explicit, bounded and locally persisted READ-ONLY operation plans.
' No arbitrary command strings, scripts, DISM writers or unattended servicing.
Public NotInheritable Class OperationWorkflow
    Private Sub New()
    End Sub

    Public Const SchemaVersion As Integer = 1
    Public Const MaxSteps As Integer = 16

    Public NotInheritable Class StepPlan
        Public Property Kind As String = "WimMetadata"
        Public Property ImagePath As String = ""
        Public Property ImageIndex As Integer = 0
    End Class

    Public NotInheritable Class Profile
        Public Property Schema As Integer = SchemaVersion
        Public Property Name As String = ""
        Public Property Steps As New List(Of StepPlan)()
    End Class

    Public NotInheritable Class StepResult
        Public Property Kind As String = ""
        Public Property Status As String = "SKIP"
        Public Property Detail As String = ""
    End Class

    Public NotInheritable Class RunRecord
        Public Property Profile As String = ""
        Public Property StartedUtc As DateTime = DateTime.UtcNow
        Public Property FinishedUtc As DateTime
        Public Property Status As String = "PENDING"
        Public Property Results As New List(Of StepResult)()
    End Class

    Private Shared Function RootFolder() As String
        Dim unitRoot = Environment.GetEnvironmentVariable("DISMGUI_TEST_OPERATION_ROOT")
        ' Test-only path override. In real installations, profile files
        ' must stay under LocalAppData, never a mounted user-selected WIM.
        If Not String.IsNullOrWhiteSpace(unitRoot) AndAlso
           Environment.GetEnvironmentVariable("DISMGUI_TEST_DIAGNOSTICS") = "1" Then
            Return Path.GetFullPath(unitRoot)
        End If
        Return Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "DISMGUI", "OperationCenter")
    End Function

    Private Shared Function StoragePath(name As String) As String
        Return Path.Combine(RootFolder(), name)
    End Function

    Private Shared Sub AtomicSave(destinationPath As String, json As String)
        Dim folder = IO.Path.GetDirectoryName(destinationPath)
        Directory.CreateDirectory(folder)
        Dim temp = destinationPath & "." & Guid.NewGuid().ToString("N") & ".tmp"
        Try
            File.WriteAllText(temp, json, New UTF8Encoding(False))
            File.Move(temp, destinationPath, True)
        Finally
            If File.Exists(temp) Then File.Delete(temp)
        End Try
    End Sub

    Private Shared Function LoadJson(Of T)(name As String) As T
        Dim path = StoragePath(name)
        If Not File.Exists(path) Then Return Nothing
        If New FileInfo(path).Length > 524288 Then
            Throw New InvalidDataException("Local " & name & " exceeds the safety limit.")
        End If
        Return JsonSerializer.Deserialize(Of T)(File.ReadAllText(path),
            New JsonSerializerOptions With {.PropertyNameCaseInsensitive = False,
                                              .MaxDepth = 12})
    End Function

    Public Shared Function AllowedKinds() As String()
        Return {"EnvironmentPreflight", "WimMetadata", "MountHealth", "MountedInventory"}
    End Function

    Public Shared Function Validate(profile As Profile) As Profile
        If profile Is Nothing OrElse profile.Schema <> SchemaVersion Then
            Throw New InvalidDataException("Unsupported operation profile schema.")
        End If
        If String.IsNullOrWhiteSpace(profile.Name) OrElse
           profile.Name.Trim().Length > 80 OrElse
           profile.Name.IndexOfAny(New Char() {ChrW(13), ChrW(10), ChrW(0)}) >= 0 Then
            Throw New InvalidDataException("Profile name must be 1-80 single-line characters.")
        End If
        If profile.Steps Is Nothing OrElse profile.Steps.Count = 0 OrElse
           profile.Steps.Count > MaxSteps Then
            Throw New InvalidDataException("Choose 1-16 operations.")
        End If
        For Each stepItem In profile.Steps
            If stepItem Is Nothing OrElse
               Not AllowedKinds().Contains(stepItem.Kind, StringComparer.Ordinal) Then
                Throw New InvalidDataException("Only known read-only operation types are allowed.")
            End If
            If stepItem.Kind = "EnvironmentPreflight" Then
                If Not String.IsNullOrWhiteSpace(stepItem.ImagePath) OrElse
                   stepItem.ImageIndex <> 0 Then
                    Throw New InvalidDataException("Environment preflight takes no image arguments.")
                End If
            ElseIf stepItem.Kind = "MountHealth" AndAlso
                   String.IsNullOrWhiteSpace(stepItem.ImagePath) Then
                If stepItem.ImageIndex <> 0 Then
                    Throw New InvalidDataException("Mount health cannot accept an index without a WIM.")
                End If
            Else
                If String.IsNullOrWhiteSpace(stepItem.ImagePath) OrElse
                   Not Path.IsPathFullyQualified(stepItem.ImagePath) Then
                    Throw New InvalidDataException("This operation requires an absolute WIM path.")
                End If
                Dim extension = Path.GetExtension(stepItem.ImagePath)
                If Not {".wim", ".esd", ".swm"}.Contains(extension,
                          StringComparer.OrdinalIgnoreCase) Then
                    Throw New InvalidDataException("Only WIM, ESD or SWM image sources are accepted.")
                End If
                If stepItem.Kind = "MountedInventory" Then
                    If stepItem.ImageIndex < 1 Then
                        Throw New InvalidDataException("Mounted inventory requires a positive image index.")
                    End If
                ElseIf stepItem.ImageIndex <> 0 Then
                    Throw New InvalidDataException("Unexpected image index in metadata or health check.")
                End If
            End If
        Next
        Return profile
    End Function

    Public Shared Function LoadProfiles() As List(Of Profile)
        Dim records = LoadJson(Of List(Of Profile))("profiles.json")
        If records Is Nothing Then Return New List(Of Profile)()
        If records.Count > 24 Then Throw New InvalidDataException("Too many local profiles.")
        For Each profile In records
            Validate(profile)
        Next
        If records.Select(Function(x) x.Name).Distinct(
              StringComparer.OrdinalIgnoreCase).Count() <> records.Count Then
            Throw New InvalidDataException("Duplicate profile names in local store.")
        End If
        Return records.OrderBy(Function(x) x.Name, StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    Public Shared Sub SaveProfile(profile As Profile)
        Validate(profile)
        Dim profiles = LoadProfiles()
        Dim match = profiles.FindIndex(Function(x)
            Return String.Equals(x.Name, profile.Name, StringComparison.OrdinalIgnoreCase)
        End Function)
        If match >= 0 Then
            profiles(match) = profile
        Else
            If profiles.Count >= 24 Then
                Throw New InvalidDataException("24 saved profiles is the maximum.")
            End If
            profiles.Add(profile)
        End If
        AtomicSave(StoragePath("profiles.json"),
            JsonSerializer.Serialize(profiles,
                New JsonSerializerOptions With {.WriteIndented = True}))
    End Sub

    Public Shared Function LoadHistory() As List(Of RunRecord)
        Dim runs = LoadJson(Of List(Of RunRecord))("history.json")
        If runs Is Nothing Then Return New List(Of RunRecord)()
        If runs.Count > 100 OrElse runs.Any(Function(x)
              Return x Is Nothing OrElse x.Results Is Nothing OrElse
                     x.Results.Count > MaxSteps
           End Function) Then
            Throw New InvalidDataException("Invalid local operation history.")
        End If
        Return runs
    End Function

    Private Shared Sub SaveHistory(record As RunRecord)
        Dim recent = LoadHistory()
        recent.Insert(0, record)
        AtomicSave(StoragePath("history.json"),
            JsonSerializer.Serialize(recent.Take(100).ToList(),
                New JsonSerializerOptions With {.WriteIndented = True}))
    End Sub

    Public Shared Function ValidateMountedIndex(report As Wim360Reader.WimReport,
                                                 index As Integer) As String
        If report Is Nothing OrElse report.IsSplitSet Then
            Throw New InvalidOperationException("Split or unavailable image is not mounted.")
        End If
        If Not String.IsNullOrWhiteSpace(report.MountScanWarning) Then
            Throw New InvalidOperationException(report.MountScanWarning)
        End If
        Dim image = report.Images.FirstOrDefault(Function(x) x.Index = index)
        If image Is Nothing OrElse
           Not image.MountStatus.StartsWith("Mounted", StringComparison.OrdinalIgnoreCase) OrElse
           String.IsNullOrWhiteSpace(image.MountFolder) Then
            Throw New InvalidOperationException(
                "Image index is not registered as valid and already mounted.")
        End If
        Return image.MountFolder
    End Function

    Private Shared Function Entry(stepItem As StepPlan, status As String,
                                  details As String) As StepResult
        Return New StepResult With {.Kind = stepItem.Kind, .Status = status,
                                    .Detail = details}
    End Function

    Public Shared Async Function PreflightAsync(profile As Profile,
                                                token As CancellationToken
                                                ) As Task(Of List(Of StepResult))
        Validate(profile)
        Dim result As New List(Of StepResult)()
        For Each item In profile.Steps
            token.ThrowIfCancellationRequested()
            Try
                If item.Kind = "EnvironmentPreflight" Then
                    result.Add(Entry(item, "READY", "Windows environment check can run read-only."))
                ElseIf item.Kind = "MountHealth" AndAlso
                       String.IsNullOrWhiteSpace(item.ImagePath) Then
                    result.Add(Entry(item, "READY", "Local mount journal can be read."))
                Else
                    Dim inspected = Await Task.Run(
                        Function() Wim360Reader.Inspect(item.ImagePath), token)
                    If item.Kind = "MountedInventory" Then
                        ValidateMountedIndex(inspected, item.ImageIndex)
                    End If
                    result.Add(Entry(item, "READY",
                        inspected.ImageCount.ToString(CultureInfo.InvariantCulture) &
                        " readable image indexes; no write scheduled."))
                End If
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                result.Add(Entry(item, "BLOCKED",
                    ErrorDiagnostics.Redact(ex.GetType().Name & ": " & ex.Message)))
            End Try
        Next
        Return result
    End Function

    Private Shared Async Function ExecuteStepAsync(
            stepItem As StepPlan, token As CancellationToken) As Task(Of StepResult)
        token.ThrowIfCancellationRequested()
        If stepItem.Kind = "EnvironmentPreflight" Then
            Dim checks = Await IntegratedTestChecks.RunAllAsync("",
                False, 96, 1024, 680, Nothing, token)
            Dim failed = checks.Where(Function(x) x.Status = "FAIL").Count()
            Dim warning = checks.Where(Function(x) x.Status = "WARN").Count()
            Return Entry(stepItem, If(failed > 0, "FAIL",
                    If(warning > 0, "WARN", "PASS")),
                    checks.Count.ToString() & " checks; " &
                    warning.ToString() & " warnings, " & failed.ToString() & " failures.")
        End If
        If stepItem.Kind = "MountHealth" Then
            Dim report = Await Task.Run(
                Function() WimMountSafety.BuildHealthReport(stepItem.ImagePath), token)
            Dim unchecked = String.IsNullOrWhiteSpace(stepItem.ImagePath)
            Dim suspicious = report.Contains("Invalid / check mount",
                                              StringComparison.OrdinalIgnoreCase)
            Return Entry(stepItem, If(unchecked OrElse suspicious, "WARN", "PASS"),
                If(unchecked,
                   "Registry and local history only; choose a WIM to verify its WIMGAPI status.",
                   "Read-only WIMMount registration and local history inspected."))
        End If
        Dim inspectedImage = Await Task.Run(
            Function() Wim360Reader.Inspect(stepItem.ImagePath), token)
        If stepItem.Kind = "WimMetadata" Then
            Return Entry(stepItem, If(inspectedImage.MountScanWarning.Length > 0,
                                      "WARN", "PASS"),
                "Read " & inspectedImage.ImageCount.ToString() &
                " indexes without mounting or changing source files.")
        End If
        Dim folder = ValidateMountedIndex(inspectedImage, stepItem.ImageIndex)
        token.ThrowIfCancellationRequested()
        Dim scanned = Await Wim360Servicing.InspectMountedAsync(
            inspectedImage.ImageFile, stepItem.ImageIndex, folder, token)
        Return Entry(stepItem, If(scanned.HasWarnings, "WARN", "PASS"),
            "Index " & stepItem.ImageIndex.ToString() &
            ": " & scanned.DriverCount.ToString() & " drivers, " &
            scanned.PackageCount.ToString() & " packages. " &
            If(scanned.HasWarnings, "Partial read-only audit.", "Complete read-only audit."))
    End Function

    Public Shared Async Function RunAsync(profile As Profile,
                                          progress As IProgress(Of StepResult),
                                          token As CancellationToken
                                          ) As Task(Of RunRecord)
        Validate(profile)
        Dim run As New RunRecord With {
            .Profile = profile.Name,
            .StartedUtc = DateTime.UtcNow,
            .Status = "RUNNING"
        }
        Try
            Dim preview = Await PreflightAsync(profile, token)
            If preview.Any(Function(x) x.Status = "BLOCKED") Then
                run.Status = "BLOCKED"
                run.Results = preview
                For Each item In preview
                    If progress IsNot Nothing Then progress.Report(item)
                Next
            Else
                For Each stepItem In profile.Steps
                    token.ThrowIfCancellationRequested()
                    Dim report As StepResult
                    Try
                        report = Await ExecuteStepAsync(stepItem, token)
                    Catch ex As OperationCanceledException
                        Throw
                    Catch ex As Exception
                        report = Entry(stepItem, "FAIL",
                            ErrorDiagnostics.Redact(ex.GetType().Name & ": " & ex.Message))
                    End Try
                    run.Results.Add(report)
                    If progress IsNot Nothing Then progress.Report(report)
                    If report.Status = "FAIL" Then Exit For
                Next
                run.Status = If(run.Results.Any(Function(x) x.Status = "FAIL"),
                                "FAIL",
                                If(run.Results.Any(Function(x) x.Status = "WARN"),
                                   "WARN", "PASS"))
            End If
        Catch ex As OperationCanceledException
            run.Status = "CANCELED"
        Catch ex As Exception
            run.Status = "FAIL"
            run.Results.Add(New StepResult With {
                .Kind = "Operation Center", .Status = "FAIL",
                .Detail = ErrorDiagnostics.Redact(ex.GetType().Name & ": " & ex.Message)
            })
        Finally
            run.FinishedUtc = DateTime.UtcNow
            SaveHistory(run)
        End Try
        Return run
    End Function
End Class
