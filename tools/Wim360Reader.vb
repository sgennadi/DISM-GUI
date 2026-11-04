Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Xml
Imports System.Xml.Linq

' Read-only WIM catalog and mount-state inventory using the Windows imaging API.
' No WIMLoadImage, mount, append, delete, commit, or temporary-image extraction.
Public NotInheritable Class Wim360Reader
    Private Sub New()
    End Sub

    Public NotInheritable Class ImageInfo
        Public Property Index As Integer
        Public Property Name As String = ""
        Public Property Description As String = ""
        Public Property Edition As String = ""
        Public Property Flags As String = ""
        Public Property Architecture As String = ""
        Public Property Product As String = ""
        Public Property Version As String = ""
        Public Property Languages As String = ""
        Public Property InstallationType As String = ""
        Public Property UncompressedBytes As Long
        Public Property MountStatus As String = "Not checked"
        Public Property MountFolder As String = ""

        Public Overrides Function ToString() As String
            Return Index.ToString() & " - " & If(Name.Length > 0, Name, "Unnamed image")
        End Function
    End Class

    Public NotInheritable Class WimReport
        Public Property ImageFile As String = ""
        Public Property FileSizeBytes As Long
        Public Property ModifiedUtc As DateTime
        Public Property Images As New List(Of ImageInfo)()
        Public Property MountScanWarning As String = ""
        Public ReadOnly Property ImageCount As Integer
            Get
                Return Images.Count
            End Get
        End Property
    End Class

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure MountInfoNative
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=260)>
        Public ImagePath As String
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=260)>
        Public MountPath As String
        Public ImageIndex As UInteger
        Public MountFlags As UInteger
    End Structure

    <DllImport("wimgapi.dll", EntryPoint:="WIMCreateFile",
               CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function WIMCreateFile(path As String, access As UInteger,
            disposition As UInteger, flags As UInteger, compression As UInteger,
            ByRef creationResult As UInteger) As IntPtr
    End Function

    <DllImport("wimgapi.dll", EntryPoint:="WIMGetImageInformation",
               SetLastError:=True)>
    <return: MarshalAs(UnmanagedType.Bool)>
    Private Shared Function WIMGetImageInformation(handle As IntPtr,
            ByRef xmlBuffer As IntPtr, ByRef byteCount As UInteger) As Boolean
    End Function

    <DllImport("wimgapi.dll", EntryPoint:="WIMGetImageCount", SetLastError:=True)>
    Private Shared Function WIMGetImageCount(handle As IntPtr) As UInteger
    End Function

    <DllImport("wimgapi.dll", EntryPoint:="WIMCloseHandle", SetLastError:=True)>
    <return: MarshalAs(UnmanagedType.Bool)>
    Private Shared Function WIMCloseHandle(handle As IntPtr) As Boolean
    End Function

    <DllImport("wimgapi.dll", EntryPoint:="WIMGetMountedImageInfo", SetLastError:=True)>
    <return: MarshalAs(UnmanagedType.Bool)>
    Private Shared Function WIMGetMountedImageInfo(level As Integer,
            ByRef count As UInteger, buffer As IntPtr, bufferLength As UInteger,
            ByRef required As UInteger) As Boolean
    End Function

    <DllImport("kernel32.dll", EntryPoint:="LocalFree", SetLastError:=True)>
    Private Shared Function LocalFree(pointer As IntPtr) As IntPtr
    End Function

    Private Const WIM_OPEN_EXISTING As UInteger = 3UI
    Private Const WIM_MOUNTED As UInteger = 1UI
    Private Const WIM_MOUNTING As UInteger = 2UI
    Private Const WIM_REMOUNTABLE As UInteger = 4UI
    Private Const WIM_INVALID As UInteger = 8UI
    Private Const WIM_MISSING_IMAGE As UInteger = 16UI
    Private Const WIM_MISSING_FOLDER As UInteger = 32UI
    Private Const WIM_REPLACED_FOLDER As UInteger = 64UI
    Private Const WIM_READ_WRITE As UInteger = 256UI

    Public Shared Function HumanSize(bytes As Long) As String
        If bytes <= 0 Then Return "Not listed"
        Dim units As String() = {"B", "KB", "MB", "GB", "TB"}
        Dim n As Double = bytes
        Dim unit As Integer = 0
        While n >= 1024 AndAlso unit < units.Length - 1
            n /= 1024.0
            unit += 1
        End While
        Return n.ToString(If(unit = 0, "0", "0.##"),
                          CultureInfo.InvariantCulture) & " " & units(unit)
    End Function

    Private Shared Function Child(parent As XElement, name As String) As XElement
        If parent Is Nothing Then Return Nothing
        Return parent.Elements().FirstOrDefault(
            Function(x) String.Equals(x.Name.LocalName, name,
                                      StringComparison.OrdinalIgnoreCase))
    End Function

    Private Shared Function TextAt(parent As XElement, ParamArray nodes As String()) As String
        Dim cur = parent
        For Each nodeName As String In nodes
            cur = Child(cur, nodeName)
            If cur Is Nothing Then Return ""
        Next
        Return cur.Value.Trim()
    End Function

    Private Shared Function ArchitectureName(arch As String) As String
        Select Case arch.Trim()
            Case "0" : Return "x86"
            Case "5" : Return "ARM"
            Case "6" : Return "IA64"
            Case "9" : Return "x64"
            Case "12" : Return "ARM64"
            Case Else : Return If(String.IsNullOrWhiteSpace(arch), "Not listed", arch)
        End Select
    End Function

    ' Public so metadata format can be validated in CI without a 4GB WIM fixture.
    ' The XML is bounded and never resolves external entities.
    Public Shared Function ParseMetadataXml(xml As String, fullPath As String,
                                             fileSize As Long) As WimReport
        If String.IsNullOrWhiteSpace(xml) OrElse xml.Length > 8 * 1024 * 1024 Then
            Throw New InvalidDataException("WIM metadata XML is empty or too large.")
        End If
        Dim settings As New XmlReaderSettings With {
            .DtdProcessing = DtdProcessing.Prohibit,
            .XmlResolver = Nothing,
            .MaxCharactersInDocument = 8L * 1024L * 1024L
        }
        Dim doc As XDocument
        Using input As New StringReader(xml)
            Using reader = XmlReader.Create(input, settings)
                doc = XDocument.Load(reader)
            End Using
        End Using
        If doc.Root Is Nothing OrElse
           Not String.Equals(doc.Root.Name.LocalName, "WIM",
                             StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidDataException("Expected a WIM XML metadata catalog.")
        End If

        Dim report As New WimReport With {.ImageFile = fullPath,
                                          .FileSizeBytes = fileSize}
        For Each imageElement As XElement In doc.Root.Elements().Where(
            Function(x) String.Equals(x.Name.LocalName, "IMAGE",
                                      StringComparison.OrdinalIgnoreCase))
            Dim attribute = imageElement.Attributes().FirstOrDefault(
                Function(a) String.Equals(a.Name.LocalName, "INDEX",
                                          StringComparison.OrdinalIgnoreCase))
            Dim imageIndex As Integer
            If attribute Is Nothing OrElse
               Not Integer.TryParse(attribute.Value, NumberStyles.None,
                                    CultureInfo.InvariantCulture, imageIndex) OrElse
               imageIndex < 1 OrElse imageIndex > 1024 Then
                Throw New InvalidDataException("Invalid image index in WIM catalog.")
            End If
            If report.Images.Any(Function(x) x.Index = imageIndex) Then
                Throw New InvalidDataException("Duplicate image index in WIM catalog.")
            End If

            Dim versionParts As String() = {
                TextAt(imageElement, "WINDOWS", "VERSION", "MAJOR"),
                TextAt(imageElement, "WINDOWS", "VERSION", "MINOR"),
                TextAt(imageElement, "WINDOWS", "VERSION", "BUILD")
            }
            Dim versionValue As String = "Not listed"
            If versionParts.All(Function(x) x.Length > 0) Then
                versionValue = String.Join(".", versionParts)
            End If
            Dim size As Long = 0
            Long.TryParse(TextAt(imageElement, "TOTALBYTES"), NumberStyles.None,
                          CultureInfo.InvariantCulture, size)
            Dim languageNode = Child(Child(imageElement, "WINDOWS"), "LANGUAGES")
            Dim langs = If(languageNode Is Nothing, "",
                           String.Join(", ", languageNode.Elements().
                               Where(Function(x) String.Equals(x.Name.LocalName,
                                   "LANGUAGE", StringComparison.OrdinalIgnoreCase)).
                               Select(Function(x) x.Value.Trim()).Where(
                                   Function(x) x.Length > 0)))
            report.Images.Add(New ImageInfo With {
                .Index = imageIndex,
                .Name = TextAt(imageElement, "NAME"),
                .Description = TextAt(imageElement, "DESCRIPTION"),
                .Edition = TextAt(imageElement, "WINDOWS", "EDITIONID"),
                .Flags = TextAt(imageElement, "FLAGS"),
                .Architecture = ArchitectureName(TextAt(imageElement, "WINDOWS", "ARCH")),
                .Product = TextAt(imageElement, "WINDOWS", "PRODUCTNAME"),
                .Version = versionValue,
                .Languages = langs,
                .InstallationType = TextAt(imageElement, "WINDOWS", "INSTALLATIONTYPE"),
                .UncompressedBytes = Math.Max(0, size)
            })
        Next

        report.Images = report.Images.OrderBy(Function(x) x.Index).ToList()
        If report.Images.Count = 0 Then
            Throw New InvalidDataException("The image contains no readable indexes.")
        End If
        Return report
    End Function

    Private Shared Function GetMounts() As List(Of MountInfoNative)
        Dim count As UInteger = 0UI
        Dim required As UInteger = 0UI
        ' First call computes the buffer size; ERROR_INSUFFICIENT_BUFFER is normal.
        Dim found = WIMGetMountedImageInfo(1, count, IntPtr.Zero, 0UI, required)
        If required = 0UI Then
            If found OrElse count = 0UI Then Return New List(Of MountInfoNative)()
            Throw New Win32Exception(Marshal.GetLastWin32Error(),
                                     "Cannot query mounted-image registry.")
        End If
        Dim stride = Marshal.SizeOf(Of MountInfoNative)()
        If required > 1024UI * 1024UI Then
            Throw New InvalidDataException("Mount list is larger than the safety limit.")
        End If
        Dim ptr = Marshal.AllocHGlobal(CInt(required))
        Try
            If Not WIMGetMountedImageInfo(1, count, ptr, required, required) Then
                Throw New Win32Exception(Marshal.GetLastWin32Error(),
                                         "Cannot read the WIM mount list.")
            End If
            If count > 512UI OrElse CLng(count) * CLng(stride) > required Then
                Throw New InvalidDataException("Unexpected mount-list dimensions.")
            End If
            Dim result As New List(Of MountInfoNative)()
            For i As Integer = 0 To CInt(count) - 1
                result.Add(Marshal.PtrToStructure(Of MountInfoNative)(
                    IntPtr.Add(ptr, i * stride)))
            Next
            Return result
        Finally
            Marshal.FreeHGlobal(ptr)
        End Try
    End Function

    Private Shared Function MountStatus(flags As UInteger) As String
        If (flags And WIM_INVALID) <> 0 OrElse
           (flags And WIM_MISSING_IMAGE) <> 0 OrElse
           (flags And WIM_MISSING_FOLDER) <> 0 OrElse
           (flags And WIM_REPLACED_FOLDER) <> 0 Then Return "Invalid / check mount"
        If (flags And WIM_REMOUNTABLE) <> 0 Then Return "Needs remount"
        If (flags And WIM_MOUNTING) <> 0 Then Return "Mounting"
        If (flags And WIM_MOUNTED) <> 0 Then
            Return If((flags And WIM_READ_WRITE) <> 0, "Mounted (R/W)", "Mounted (read-only)")
        End If
        Return "Unknown mount state"
    End Function

    Private Shared Sub FillMountState(report As WimReport)
        Try
            Dim mounts = GetMounts()
            For Each image As ImageInfo In report.Images
                image.MountStatus = "Not mounted"
            Next
            Dim normalized = Path.GetFullPath(report.ImageFile).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            For Each item In mounts
                If String.IsNullOrWhiteSpace(item.ImagePath) Then Continue For
                Dim candidate As String
                Try
                    candidate = Path.GetFullPath(item.ImagePath).TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                Catch
                    Continue For
                End Try
                If Not String.Equals(normalized, candidate,
                                     StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim matched = report.Images.FirstOrDefault(
                    Function(x) x.Index = CInt(item.ImageIndex))
                If matched IsNot Nothing Then
                    matched.MountStatus = MountStatus(item.MountFlags)
                    matched.MountFolder = item.MountPath
                End If
            Next
        Catch ex As Exception
            report.MountScanWarning = "Mount state unavailable: " & ex.Message
            For Each image As ImageInfo In report.Images
                image.MountStatus = "Not checked"
            Next
        End Try
    End Sub

    Public Shared Function ReadImage(path As String) As WimReport
        If String.IsNullOrWhiteSpace(path) Then
            Throw New ArgumentException("Choose a .wim or .esd image.")
        End If
        Dim full = Path.GetFullPath(path)
        Dim ext = Path.GetExtension(full)
        If Not String.Equals(ext, ".wim", StringComparison.OrdinalIgnoreCase) AndAlso
           Not String.Equals(ext, ".esd", StringComparison.OrdinalIgnoreCase) Then
            Throw New NotSupportedException(
                "This read-only preview accepts .wim and .esd files. Split .swm is not supported yet.")
        End If
        Dim source As New FileInfo(full)
        If Not source.Exists Then Throw New FileNotFoundException("Image not found.", full)
        Dim opened As UInteger = 0UI
        ' dwDesiredAccess=0 is WIMGAPI's metadata-query-only mode.
        ' OPEN_EXISTING explicitly prohibits creation of any source image.
        Dim hWim = WIMCreateFile(full, 0UI, WIM_OPEN_EXISTING, 0UI, 0UI, opened)
        If hWim = IntPtr.Zero OrElse hWim = New IntPtr(-1) Then
            Throw New Win32Exception(Marshal.GetLastWin32Error(),
                                     "WIMGAPI could not open the image for metadata inspection.")
        End If
        Dim xmlBuffer As IntPtr = IntPtr.Zero
        Try
            Dim bytes As UInteger = 0UI
            If Not WIMGetImageInformation(hWim, xmlBuffer, bytes) Then
                Throw New Win32Exception(Marshal.GetLastWin32Error(),
                                         "WIMGAPI could not read image metadata.")
            End If
            If xmlBuffer = IntPtr.Zero OrElse bytes = 0UI OrElse bytes > 16UI * 1024UI * 1024UI OrElse
               bytes Mod 2UI <> 0UI Then
                Throw New InvalidDataException("WIM metadata length is invalid.")
            End If
            Dim xml = Marshal.PtrToStringUni(xmlBuffer, CInt(bytes \ 2UI)).
                TrimStart(ChrW(&HFEFF)).TrimEnd(ChrW(0))
            Dim report = ParseMetadataXml(xml, full, source.Length)
            Dim nativeCount = WIMGetImageCount(hWim)
            If nativeCount = 0 OrElse nativeCount <> CUInt(report.Images.Count) Then
                Throw New InvalidDataException("Image count disagrees with WIMGAPI metadata.")
            End If
            report.ModifiedUtc = source.LastWriteTimeUtc
            ' Query all mounts only after WIM file handles have been released.
            Return report
        Finally
            If xmlBuffer <> IntPtr.Zero Then LocalFree(xmlBuffer)
            WIMCloseHandle(hWim)
        End Try
    End Function

    Public Shared Function Inspect(path As String) As WimReport
        Dim report = ReadImage(path)
        FillMountState(report)
        Return report
    End Function

    Public Shared Function FormatReport(report As WimReport) As String
        Dim lines As New List(Of String) From {
            "DISM GUI WIM 360 (read-only)",
            "Image: " & report.ImageFile,
            "File size: " & HumanSize(report.FileSizeBytes),
            "Indexes: " & report.ImageCount.ToString(),
            "Modified (UTC): " & report.ModifiedUtc.ToString("u"),
            ""
        }
        For Each item As ImageInfo In report.Images
            lines.Add("Index " & item.Index.ToString() & ": " & item.Name)
            lines.Add("  Edition: " & item.Edition)
            lines.Add("  Architecture: " & item.Architecture)
            lines.Add("  OS build: " & item.Version)
            lines.Add("  Image bytes: " & HumanSize(item.UncompressedBytes))
            lines.Add("  Languages: " & item.Languages)
            lines.Add("  Install type: " & item.InstallationType)
            lines.Add("  Mount: " & item.MountStatus)
            If item.MountFolder.Length > 0 Then lines.Add("  Mount dir: " & item.MountFolder)
            lines.Add("")
        Next
        If report.MountScanWarning.Length > 0 Then lines.Add(report.MountScanWarning)
        Return String.Join(Environment.NewLine, lines)
    End Function
End Class
