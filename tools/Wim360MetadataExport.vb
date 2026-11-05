Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text.Json

' Export WIM/ESD/SWM index metadata without mounting or servicing an image.
' This format is deliberately separate from the servicing inventory snapshot.
Public NotInheritable Class Wim360MetadataExport
    Private Sub New()
    End Sub

    Private Shared ReadOnly Headers As String() = {
        "Image file", "Container type", "Index", "Name", "Description",
        "Edition", "Flags", "Architecture", "Product", "OS build",
        "Languages", "Installation type", "Image bytes", "Mount state",
        "Mount folder", "File bytes", "Modified UTC"
    }

    Private Shared Function SafeCsv(value As String) As String
        Dim safe = If(value, "")
        ' Metadata can originate in untrusted WIM files. Never let spreadsheet
        ' applications evaluate image names/paths as formulas.
        Dim trimmed = safe.TrimStart({" "c, ChrW(9), ChrW(13), ChrW(10)})
        If trimmed.Length > 0 AndAlso "=+-@".Contains(trimmed(0)) Then
            safe = "'" & safe
        End If
        Dim quote = ChrW(34).ToString()
        Return quote & safe.Replace(quote, quote & quote) & quote
    End Function

    Private Shared Function ContainerType(report As Wim360Reader.WimReport) As String
        If report.IsSplitSet Then Return "Split WIM (SWM)"
        If String.Equals(IO.Path.GetExtension(report.ImageFile), ".esd",
                         StringComparison.OrdinalIgnoreCase) Then Return "ESD"
        Return "WIM"
    End Function

    Public Shared Function ToCsv(report As Wim360Reader.WimReport) As String
        If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
        Dim lines As New List(Of String) From {
            String.Join(",", Headers.Select(Function(v) SafeCsv(v)))
        }
        For Each item As Wim360Reader.ImageInfo In report.Images.OrderBy(Function(x) x.Index)
            Dim fields As String() = {
                report.ImageFile,
                ContainerType(report),
                item.Index.ToString(CultureInfo.InvariantCulture),
                item.Name, item.Description, item.Edition, item.Flags,
                item.Architecture, item.Product, item.Version, item.Languages,
                item.InstallationType,
                item.UncompressedBytes.ToString(CultureInfo.InvariantCulture),
                item.MountStatus, item.MountFolder,
                report.FileSizeBytes.ToString(CultureInfo.InvariantCulture),
                report.ModifiedUtc.ToString("u", CultureInfo.InvariantCulture)
            }
            lines.Add(String.Join(",", fields.Select(Function(v) SafeCsv(v))))
        Next
        Return String.Join(vbCrLf, lines) & vbCrLf
    End Function

    Public Shared Function ToJson(report As Wim360Reader.WimReport) As String
        If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
        Dim snapshot = New With {
            .ExportKind = "Wim360IndexMetadata",
            .SchemaVersion = 1,
            .Source = "DISM GUI WIM 360 (read-only)",
            .Note = "Index metadata only. Not a servicing-inventory snapshot.",
            .ImageFile = report.ImageFile,
            .ContainerType = ContainerType(report),
            .FileSizeBytes = report.FileSizeBytes,
            .ModifiedUtc = report.ModifiedUtc,
            .MountScanWarning = report.MountScanWarning,
            .ImageCount = report.ImageCount,
            .SplitPartCount = If(report.IsSplitSet, report.SplitSet.PartCount, 0),
            .Indexes = report.Images.OrderBy(Function(x) x.Index).
                Select(Function(item) New With {
                    .Index = item.Index,
                    .Name = item.Name,
                    .Description = item.Description,
                    .Edition = item.Edition,
                    .Flags = item.Flags,
                    .Architecture = item.Architecture,
                    .Product = item.Product,
                    .OsBuild = item.Version,
                    .Languages = item.Languages,
                    .InstallationType = item.InstallationType,
                    .ImageBytes = item.UncompressedBytes,
                    .MountState = item.MountStatus,
                    .MountFolder = item.MountFolder
                }).ToArray()
        }
        Return JsonSerializer.Serialize(snapshot, New JsonSerializerOptions With {
            .WriteIndented = True
        })
    End Function
End Class
