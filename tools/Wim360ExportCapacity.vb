Imports System
Imports System.ComponentModel
Imports System.Globalization
Imports System.IO
Imports System.Runtime.InteropServices

' Conservative destination free-space gate for SWM -> NEW WIM export.
' This is not a space reservation and not a promise that DISM cannot run
' out of space; other processes and scratch usage can change at any time.
Public NotInheritable Class Wim360ExportCapacity
    Private Sub New()
    End Sub

    Private Const OneMiB As Long = 1048576L
    Private Const OneGiB As Long = 1073741824L

    <DllImport("kernel32.dll", EntryPoint:="GetDiskFreeSpaceExW",
               CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function GetDiskFreeSpaceEx(directoryName As String,
        ByRef availableToCaller As ULong, ByRef totalBytes As ULong,
        ByRef freeBytes As ULong) As Boolean
    End Function

    Public NotInheritable Class CapacityResult
        Public Property DestinationDirectory As String = ""
        Public Property AvailableBytes As Long
        Public Property RequiredBytes As Long
        Public Property SourcePartBytes As Long
        Public Property SelectedUncompressedBytes As Long

        Public ReadOnly Property Summary As String
            Get
                Return "SWM export destination capacity: " &
                    (AvailableBytes \ OneMiB).ToString("N0", CultureInfo.InvariantCulture) &
                    " MiB available; conservative minimum " &
                    (RequiredBytes \ OneMiB).ToString("N0", CultureInfo.InvariantCulture) &
                    " MiB required. No capacity is reserved."
            End Get
        End Property
    End Class

    Public Shared Function RequiredBytes(sourcePartsBytes As Long,
                                         selectedUncompressedBytes As Long) As Long
        If sourcePartsBytes <= 0 OrElse selectedUncompressedBytes < 0 Then
            Throw New ArgumentOutOfRangeException(NameOf(sourcePartsBytes),
                "Verified source size is required before estimating export space.")
        End If
        ' Part data can be compressed differently in the exported image.
        ' Require >=2x source bytes or >=60% of uncompressed image size,
        ' plus 512 MiB of scratch allowance and a 1 GiB minimum payload.
        ' This errs on the side of blocking an export on low-space media.
        Dim payload As Decimal = Math.Max(CDec(OneGiB),
            Math.Max(CDec(sourcePartsBytes) * 2D,
                     CDec(selectedUncompressedBytes) * 0.6D))
        Dim estimate = Decimal.Ceiling(payload + CDec(512L * OneMiB))
        If estimate > CDec(Long.MaxValue) Then
            Throw New IOException("SWM export required size is too large to represent.")
        End If
        Return CLng(estimate)
    End Function

    ' Pure gate for unit tests and known external free-space telemetry.
    Public Shared Sub RequireAvailable(availableBytes As Long, requiredBytes As Long)
        If availableBytes < 0 OrElse requiredBytes <= 0 OrElse
           availableBytes < requiredBytes Then
            Throw New IOException(
                "Not enough verified free space for SWM export. Need at least " &
                (requiredBytes \ OneMiB).ToString("N0", CultureInfo.InvariantCulture) &
                " MiB available to the current account; found " &
                (Math.Max(0L, availableBytes) \ OneMiB).
                    ToString("N0", CultureInfo.InvariantCulture) & " MiB. " &
                "No DISM export was started. Choose a larger destination.")
        End If
    End Sub

    Public Shared Function CheckDestination(destinationDirectory As String,
                                            sourcePartsBytes As Long,
                                            selectedUncompressedBytes As Long
                                            ) As CapacityResult
        If String.IsNullOrWhiteSpace(destinationDirectory) Then
            Throw New ArgumentException("Choose a destination directory for export.")
        End If
        Dim folder = Path.GetFullPath(destinationDirectory)
        If Not Directory.Exists(folder) Then
            Throw New DirectoryNotFoundException(folder)
        End If
        If (File.GetAttributes(folder) And FileAttributes.ReparsePoint) =
           FileAttributes.ReparsePoint Then
            Throw New IOException(
                "Destination folder is a reparse point. Choose a direct local or network path.")
        End If
        Dim available As ULong
        Dim total As ULong
        Dim unused As ULong
        If Not GetDiskFreeSpaceEx(folder, available, total, unused) Then
            Throw New Win32Exception(Marshal.GetLastWin32Error(),
                "Unable to verify destination free space; no export was started.")
        End If
        Dim required = RequiredBytes(sourcePartsBytes, selectedUncompressedBytes)
        Dim availableSigned = If(available > CULng(Long.MaxValue),
                                 Long.MaxValue, CLng(available))
        RequireAvailable(availableSigned, required)
        Return New CapacityResult With {
            .DestinationDirectory = folder,
            .AvailableBytes = availableSigned,
            .RequiredBytes = required,
            .SourcePartBytes = sourcePartsBytes,
            .SelectedUncompressedBytes = selectedUncompressedBytes
        }
    End Function
End Class
