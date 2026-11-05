Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Security.Principal
Imports System.Text
Imports System.Threading.Tasks

' Explicit, per-INF import into an already-mounted read/write Windows image.
' Import never mounts, unmounts, commits, discards, recurses, or forces unsigned drivers.
Public NotInheritable Class DriverStudioInstaller
    Private Sub New()
    End Sub

    Public NotInheritable Class InstallResult
        Public Property InfName As String = ""
        Public Property ExitCode As Integer
        Public Property Succeeded As Boolean
        Public Property RebootRequired As Boolean
        Public Property Details As String = ""
    End Class

    Public Shared Function IsAllowed(d As DriverStudioEngine.Decision) As Boolean
        Return d IsNot Nothing AndAlso d.Candidate IsNot Nothing AndAlso
            String.IsNullOrWhiteSpace(d.Candidate.ValidationError) AndAlso
            (String.Equals(d.Status, "New", StringComparison.Ordinal) OrElse
             String.Equals(d.Status, "Newer", StringComparison.Ordinal))
    End Function

    ' Pure preflight: the candidate list and inventory are freshly re-read by
    ' the caller. Source INF SHA-256, identity, date, version and the decision
    ' must still match the previous user-reviewed selection.
    Public Shared Function ValidatePlan(
        reviewed As IEnumerable(Of DriverStudioEngine.Decision),
        freshSource As IEnumerable(Of DriverStudioEngine.DriverPackage),
        freshInstalled As IEnumerable(Of DriverStudioEngine.DriverPackage)
    ) As List(Of DriverStudioEngine.DriverPackage)
        If reviewed Is Nothing OrElse freshSource Is Nothing OrElse
           freshInstalled Is Nothing Then
            Throw New ArgumentNullException(NameOf(reviewed))
        End If
        Dim selected = reviewed.Where(Function(d) d.Selected).ToList()
        If selected.Count = 0 Then Throw New InvalidOperationException(
            "Choose at least one New or Newer driver before importing.")
        If selected.Count > 200 Then Throw New InvalidOperationException(
            "A single import is limited to 200 INF packages; use smaller batches.")
        If selected.Any(Function(d) Not IsAllowed(d)) Then
            Throw New InvalidOperationException(
                "Only verified New / Newer drivers are eligible. Deselect Same, Older and Review entries.")
        End If

        Dim current = DriverStudioEngine.CompareCandidates(freshSource,
                                                            freshInstalled, True)
        Dim approved As New List(Of DriverStudioEngine.DriverPackage)()
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each reviewedDecision In selected
            Dim oldPkg = reviewedDecision.Candidate
            If String.IsNullOrWhiteSpace(oldPkg.FilePath) OrElse
               String.IsNullOrWhiteSpace(oldPkg.Sha256) Then
                Throw New InvalidOperationException("Unverified INF metadata; run comparison again.")
            End If
            Dim full = Path.GetFullPath(oldPkg.FilePath)
            If Not full.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) OrElse
               Not seen.Add(full) Then
                Throw New InvalidOperationException("Duplicate or invalid INF path in selected plan.")
            End If
            Dim matching = current.Where(Function(d)
                 String.Equals(Path.GetFullPath(d.Candidate.FilePath), full,
                               StringComparison.OrdinalIgnoreCase)).ToList()
            If matching.Count <> 1 OrElse Not IsAllowed(matching(0)) OrElse
               Not String.Equals(matching(0).Status, reviewedDecision.Status,
                                 StringComparison.Ordinal) Then
                Throw New InvalidOperationException(
                    "Image inventory or driver decision changed for " &
                    oldPkg.InfName & ". Compare again before importing.")
            End If
            Dim freshPkg = matching(0).Candidate
            If Not String.Equals(oldPkg.Sha256, freshPkg.Sha256,
                                 StringComparison.OrdinalIgnoreCase) OrElse
               Not String.Equals(oldPkg.Provider, freshPkg.Provider,
                                 StringComparison.OrdinalIgnoreCase) OrElse
               Not String.Equals(oldPkg.ClassName, freshPkg.ClassName,
                                 StringComparison.OrdinalIgnoreCase) OrElse
               Not String.Equals(oldPkg.Version, freshPkg.Version,
                                 StringComparison.OrdinalIgnoreCase) OrElse
               Not String.Equals(oldPkg.DriverDate, freshPkg.DriverDate,
                                 StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidOperationException(
                    "INF source changed after comparison: " &
                    oldPkg.InfName & ". No drivers were imported.")
            End If
            approved.Add(freshPkg)
        Next
        Return approved
    End Function

    Public Shared Function RequireWritableTarget(wimFile As String,
                    index As Integer, mountFolder As String) As String
        Dim root = DriverStudioInventory.ValidateTarget(wimFile, index, mountFolder)
        Dim report = Wim360Reader.Inspect(wimFile)
        Dim chosen = report.Images.FirstOrDefault(Function(x) x.Index = index)
        If chosen Is Nothing OrElse
           Not String.Equals(chosen.MountStatus, "Mounted (R/W)",
                             StringComparison.OrdinalIgnoreCase) OrElse
           Not String.Equals(Path.GetFullPath(chosen.MountFolder).TrimEnd(
                 Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                 root, StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException(
                "The selected WIM index must be mounted in read/write mode. " &
                "Read-only or invalid mounts cannot receive driver packages.")
        End If
        Return root
    End Function

    Public Shared Function IsAdministrator() As Boolean
        Try
            Using identity = WindowsIdentity.GetCurrent()
                Return New WindowsPrincipal(identity).IsInRole(
                    WindowsBuiltInRole.Administrator)
            End Using
        Catch
            Return False
        End Try
    End Function

    Public Shared Function BuildArguments(mountFolder As String,
                                          driverInf As String) As List(Of String)
        If String.IsNullOrWhiteSpace(mountFolder) OrElse
           String.IsNullOrWhiteSpace(driverInf) Then
            Throw New ArgumentException("Mounted Windows folder and INF path are required.")
        End If
        Dim root = Path.GetFullPath(mountFolder).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        Dim inf = Path.GetFullPath(driverInf)
        If Not inf.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) Then
            Throw New ArgumentException("Only individual .inf files can be imported.")
        End If
        Return New List(Of String) From {
            "/Image:" & root,
            "/Add-Driver",
            "/Driver:" & inf
        }
    End Function

    Private Shared Function DismExecutable() As String
        Dim systemName = If(Environment.Is64BitOperatingSystem AndAlso
                            Not Environment.Is64BitProcess, "Sysnative", "System32")
        Dim osDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        Dim filename = Path.Combine(osDirectory, systemName, "dism.exe")
        If Not File.Exists(filename) Then Throw New FileNotFoundException(
            "System DISM.exe was not found.", filename)
        Return filename
    End Function

    Public Shared Async Function AddOneAsync(mountFolder As String,
                  package As DriverStudioEngine.DriverPackage) As Task(Of InstallResult)
        If package Is Nothing OrElse String.IsNullOrWhiteSpace(package.FilePath) Then
            Throw New ArgumentException("Missing INF source.")
        End If
        ' Re-hash the actual INF immediately before starting each DISM action.
        ' This does not certify all accompanying driver payloads; DISM enforces
        ' package validity and signature checks.
        Dim fresh = Await Task.Run(Function() DriverStudioEngine.ReadInf(package.FilePath))
        If Not String.Equals(fresh.Sha256, package.Sha256,
                             StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException(
                "Source INF changed during import: " & package.InfName &
                ". Stop, re-scan and compare again.")
        End If
        Dim psi As New ProcessStartInfo With {
            .FileName = DismExecutable(),
            .UseShellExecute = False, .CreateNoWindow = True,
            .RedirectStandardOutput = True, .RedirectStandardError = True
        }
        For Each argument As String In BuildArguments(mountFolder, fresh.FilePath)
            psi.ArgumentList.Add(argument)
        Next

        Using command As New Process With {.StartInfo = psi}
            If Not command.Start() Then Throw New Win32Exception(
                "Could not start DISM for the selected INF.")
            ' No kill/timeout mid-operation: interrupting an active DISM write
            ' can leave the mounted WIM in an inconsistent state.
            Dim outputTask = command.StandardOutput.ReadToEndAsync()
            Dim errorTask = command.StandardError.ReadToEndAsync()
            Await command.WaitForExitAsync()
            Dim combined = (Await outputTask) & Environment.NewLine & (Await errorTask)
            Dim shortOutput = If(combined.Length <= 3600,
                                 combined, combined.Substring(combined.Length - 3600))
            Return New InstallResult With {
                .InfName = fresh.InfName,
                .ExitCode = command.ExitCode,
                .Succeeded = command.ExitCode = 0 OrElse command.ExitCode = 3010,
                .RebootRequired = command.ExitCode = 3010,
                .Details = shortOutput.Trim()
            }
        End Using
    End Function
End Class
