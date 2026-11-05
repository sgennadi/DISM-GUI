Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq

' Validates a complete, read-only DISM split WIM set without extracting,
' mounting, applying or editing any image. Header checks are not a full
' content/integrity verification; they prevent wrong/missing part selection.
Public NotInheritable Class Wim360SplitSet
    Private Sub New()
    End Sub

    Public NotInheritable Class PartHeader
        Public Property FilePath As String = ""
        Public Property PartNumber As Integer
        Public Property TotalParts As Integer
        Public Property Identity As Guid
        Public Property IsSpanned As Boolean
        Public Property Bytes As Long
    End Class

    Public NotInheritable Class Manifest
        Public Property MainFile As String = ""
        Public Property Parts As New List(Of String)()
        Public Property Identity As Guid
        Public Property TotalBytes As Long
        Public ReadOnly Property PartCount As Integer
            Get
                Return Parts.Count
            End Get
        End Property
    End Class

    Public Shared Function ReadHeader(path As String) As PartHeader
        Dim full = IO.Path.GetFullPath(path)
        Dim info As New FileInfo(full)
        If Not info.Exists Then Throw New FileNotFoundException("SWM part is missing.", full)
        If info.Length < 208 Then
            Throw New InvalidDataException("Invalid WIM/SWM part: header is shorter than 208 bytes.")
        End If
        Using input As New FileStream(full, FileMode.Open, FileAccess.Read,
                                      FileShare.ReadWrite, 4096, FileOptions.SequentialScan)
            Using reader As New BinaryReader(input)
                Dim magic = reader.ReadBytes(8)
                Dim expected As Byte() = {&H4D, &H53, &H57, &H49, &H4D, 0, 0, 0}
                If Not magic.SequenceEqual(expected) Then
                    Throw New InvalidDataException("Selected file does not have a Microsoft WIM header.")
                End If
                Dim headerSize = reader.ReadUInt32()
                If headerSize < 208UI OrElse headerSize > 4096UI Then
                    Throw New InvalidDataException("Unsupported WIM header length.")
                End If
                reader.ReadUInt32() ' version
                Dim flags = reader.ReadUInt32()
                reader.ReadUInt32() ' compression block size
                Dim guidBytes = reader.ReadBytes(16)
                Dim number = reader.ReadUInt16()
                Dim total = reader.ReadUInt16()
                If number < 1 OrElse total < 2 OrElse total > 256 OrElse number > total Then
                    Throw New InvalidDataException("Invalid split image part numbers (1-256).")
                End If
                If (flags And &H8UI) = 0UI Then
                    Throw New InvalidDataException("WIM header does not identify a split image.")
                End If
                Return New PartHeader With {
                    .FilePath = full,
                    .PartNumber = CInt(number),
                    .TotalParts = CInt(total),
                    .Identity = New Guid(guidBytes),
                    .IsSpanned = True,
                    .Bytes = info.Length
                }
            End Using
        End Using
    End Function

    Public Shared Function Discover(path As String) As Manifest
        If String.IsNullOrWhiteSpace(path) Then
            Throw New ArgumentException("Select a .swm image part.")
        End If
        Dim selected = IO.Path.GetFullPath(path)
        If Not String.Equals(IO.Path.GetExtension(selected), ".swm",
                             StringComparison.OrdinalIgnoreCase) Then
            Throw New NotSupportedException("Split WIM inspection requires a .swm part.")
        End If
        Dim current = ReadHeader(selected)
        Dim selectedStem = IO.Path.GetFileNameWithoutExtension(selected)
        Dim rootName = selectedStem
        If current.PartNumber > 1 Then
            Dim suffix = current.PartNumber.ToString(Globalization.CultureInfo.InvariantCulture)
            If Not selectedStem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidDataException("Selected part filename is inconsistent with its header.")
            End If
            rootName = selectedStem.Substring(0, selectedStem.Length - suffix.Length)
            If rootName.Length = 0 Then
                Throw New InvalidDataException("Cannot determine the split-image base filename.")
            End If
        End If

        Dim directory = IO.Path.GetDirectoryName(selected)
        Dim first = IO.Path.Combine(directory, rootName & ".swm")
        Dim manifest As New Manifest With {
            .MainFile = first,
            .Identity = current.Identity
        }
        For partNumber As Integer = 1 To current.TotalParts
            Dim filename = If(partNumber = 1, rootName,
                              rootName & partNumber.ToString(Globalization.CultureInfo.InvariantCulture)) &
                           ".swm"
            Dim full = IO.Path.Combine(directory, filename)
            If Not File.Exists(full) Then
                Throw New FileNotFoundException("Missing SWM part " &
                    partNumber.ToString() & "/" & current.TotalParts.ToString() & ".", full)
            End If
            Dim item = ReadHeader(full)
            If item.PartNumber <> partNumber OrElse
               item.TotalParts <> current.TotalParts OrElse
               item.Identity <> current.Identity Then
                Throw New InvalidDataException("SWM part " & partNumber.ToString() &
                                               " belongs to a different or damaged split image.")
            End If
            manifest.Parts.Add(full)
            manifest.TotalBytes = Math.AddExact(manifest.TotalBytes, item.Bytes)
        Next
        Return manifest
    End Function

    Public Shared Function Describe(value As Manifest) As String
        If value Is Nothing Then Return "No split WIM set."
        Return value.PartCount.ToString() & " parts  |  " &
            Wim360Reader.HumanSize(value.TotalBytes) &
            "  |  Header identity checked; content integrity not verified."
    End Function
End Class
