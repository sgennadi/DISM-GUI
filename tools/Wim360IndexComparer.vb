Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading

' Compare 2..8 indexes without changing source WIM files or mount state.
' Metadata works on closed images. Files require *existing* mounted folders.
' Servicing inventory requires a complete per-index audit from this session.
Public NotInheritable Class Wim360IndexComparer
    Private Sub New()
    End Sub

    Public NotInheritable Class Item
        Public Property Index As Integer
        Public Property Kind As String = ""
        Public Property Key As String = ""
        Public Property Value As String = ""
    End Class

    Public NotInheritable Class Row
        Public Property Kind As String = ""
        Public Property Name As String = ""
        Public Property State As String = ""
        Public Property Values As New Dictionary(Of Integer, String)()
        Public Property PresentCount As Integer
        Public Property Notes As String = ""

        Public Function At(index As Integer) As String
            Dim value As String = Nothing
            Return If(Values.TryGetValue(index, value), value, "—")
        End Function
    End Class

    Public NotInheritable Class Result
        Public Property ImageFile As String = ""
        Public Property Mode As String = ""
        Public Property Indexes As New List(Of Integer)()
        Public Property Rows As New List(Of Row)()
        Public Property Notes As String = ""
    End Class

    Public Shared Function SelectedIndexes(report As Wim360Reader.WimReport,
                           indexes As IEnumerable(Of Integer)) As List(Of Wim360Reader.ImageInfo)
        If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
        If indexes Is Nothing Then Throw New ArgumentNullException(NameOf(indexes))
        Dim selected = indexes.Distinct().OrderBy(Function(x) x).ToList()
        If selected.Count < 2 OrElse selected.Count > 8 Then
            Throw New ArgumentException("Select between two and eight image indexes.")
        End If
        Dim found = report.Images.Where(Function(x) selected.Contains(x.Index)).
                                  OrderBy(Function(x) x.Index).ToList()
        If found.Count <> selected.Count Then
            Throw New ArgumentException("One or more selected image indexes do not exist.")
        End If
        Return found
    End Function

    Public Shared Function Metadata(report As Wim360Reader.WimReport,
                                    indexes As IEnumerable(Of Integer)) As Result
        Dim chosen = SelectedIndexes(report, indexes)
        Dim records As New List(Of Item)()
        For Each item In chosen
            Dim fields As New Dictionary(Of String, String) From {
                {"Image name", item.Name},
                {"Description", item.Description},
                {"Edition", item.Edition},
                {"Flags", item.Flags},
                {"Architecture", item.Architecture},
                {"Product", item.Product},
                {"OS build", item.Version},
                {"Languages", item.Languages},
                {"Installation type", item.InstallationType},
                {"Uncompressed bytes", item.UncompressedBytes.ToString(CultureInfo.InvariantCulture)}
            }
            For Each field In fields
                records.Add(New Item With {
                    .Index = item.Index, .Kind = "Metadata",
                    .Key = field.Key, .Value = If(field.Value, "")
                })
            Next
        Next
        Return CompareRecords(report.ImageFile, "Index metadata",
                              chosen.Select(Function(x) x.Index), records)
    End Function

    Public Shared Function AuditedInventory(report As Wim360Reader.WimReport,
                                             indexes As IEnumerable(Of Integer)) As Result
        Dim chosen = SelectedIndexes(report, indexes)
        Dim records As New List(Of Item)()
        For Each item In chosen
            Dim audit = item.ServicingAudit
            If audit Is Nothing Then
                Throw New InvalidOperationException(
                    "Index " & item.Index.ToString() &
                    " has no mounted inventory. Mount it and run Scan mounted index first.")
            End If
            If audit.HasWarnings Then
                Throw New InvalidOperationException(
                    "Index " & item.Index.ToString() & " has partial inventory: " &
                    audit.WarningSummary & ". Missing-item results would be unreliable.")
            End If
            For Each entry In Wim360Catalog.BuildRows(audit)
                Dim identity = entry.Name
                If String.Equals(entry.Kind, "Drivers",
                                 StringComparison.OrdinalIgnoreCase) Then
                    identity &= "  |  " & entry.DriverClass & "  |  " & entry.ProviderOrType
                End If
                Dim value = If(String.Equals(entry.Kind, "Drivers",
                                             StringComparison.OrdinalIgnoreCase),
                               entry.Version & " / " & entry.DriverDate & " / " & entry.State,
                               entry.State & If(entry.ProviderOrType.Length > 0,
                                                " / " & entry.ProviderOrType, ""))
                records.Add(New Item With {
                    .Index = item.Index, .Kind = entry.Kind,
                    .Key = identity, .Value = value.Trim()
                })
            Next
        Next
        Return CompareRecords(report.ImageFile, "Audited servicing inventory",
                              chosen.Select(Function(x) x.Index), records)
    End Function

    Public Shared Function MountedFiles(report As Wim360Reader.WimReport,
                                        indexes As IEnumerable(Of Integer),
                                        token As CancellationToken) As Result
        Dim chosen = SelectedIndexes(report, indexes)
        If report.IsSplitSet Then
            Throw New InvalidOperationException(
                "Split SWM can be compared by metadata only. Export or apply it to a test image first.")
        End If
        ' Refresh mount registrations immediately before reading files; an old
        ' dashboard snapshot must never be treated as a still-mounted directory.
        Dim fresh = Wim360Reader.Inspect(report.ImageFile)
        Dim verified = SelectedIndexes(fresh, chosen.Select(Function(x) x.Index))
        Dim records As New List(Of Item)()
        For Each item In verified
            token.ThrowIfCancellationRequested()
            If Not item.MountStatus.StartsWith("Mounted", StringComparison.OrdinalIgnoreCase) OrElse
               String.IsNullOrWhiteSpace(item.MountFolder) OrElse
               Not Directory.Exists(item.MountFolder) Then
                Throw New InvalidOperationException(
                    "Index " & item.Index.ToString() & " is not mounted. " &
                    "Mount every selected index in a separate folder, then compare.")
            End If
            Dim root = Path.GetFullPath(item.MountFolder)
            Dim options As New EnumerationOptions With {
                .RecurseSubdirectories = True,
                .IgnoreInaccessible = False,
                .AttributesToSkip = FileAttributes.ReparsePoint
            }
            Dim count As Integer = 0
            For Each fullName In Directory.EnumerateFiles(root, "*", options)
                token.ThrowIfCancellationRequested()
                count += 1
                If count > 250000 Then
                    Throw New InvalidOperationException(
                        "Index " & item.Index.ToString() &
                        " exceeded the 250,000-file safety limit. No incomplete diff was shown.")
                End If
                Dim relative = Path.GetRelativePath(root, fullName).Replace("/"c, "\"c)
                Dim bytes = New FileInfo(fullName).Length
                records.Add(New Item With {
                    .Index = item.Index, .Kind = "Files",
                    .Key = relative,
                    .Value = bytes.ToString(CultureInfo.InvariantCulture) & " bytes"
                })
            Next
            ' Empty directories and directory-only differences matter too.
            ' Use the same recursion and junction-skip policy as file listing.
            For Each fullName In Directory.EnumerateDirectories(root, "*", options)
                token.ThrowIfCancellationRequested()
                count += 1
                If count > 250000 Then
                    Throw New InvalidOperationException(
                        "Index " & item.Index.ToString() &
                        " exceeded the 250,000-item safety limit. No incomplete diff was shown.")
                End If
                Dim relative = Path.GetRelativePath(root, fullName).Replace("/"c, "\"c)
                records.Add(New Item With {
                    .Index = item.Index, .Kind = "Folders",
                    .Key = relative, .Value = "Present"
                })
            Next
        Next
        Dim result = CompareRecords(report.ImageFile, "Mounted file paths / sizes",
                                    chosen.Select(Function(x) x.Index), records)
        result.Notes = "Read-only file and folder listing. Reparse points skipped. " &
            "Matching file lengths are NOT proof of identical bytes; " &
            "SHA-256 verification is available for a selected file."
        Return result
    End Function

    ' Hash one selected file across every mounted index where it exists.
    ' This is opt-in because full hashing of a Windows image can be expensive.
    Public Shared Function VerifyFileHashes(report As Wim360Reader.WimReport,
                                           indexes As IEnumerable(Of Integer),
                                           original As Row,
                                           token As CancellationToken) As Row
        If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
        If original Is Nothing OrElse
           Not String.Equals(original.Kind, "Files", StringComparison.OrdinalIgnoreCase) Then
            Throw New ArgumentException("Choose a file row to verify by SHA-256.")
        End If
        If Path.IsPathRooted(original.Name) OrElse
           original.Name.IndexOf(ChrW(0)) >= 0 Then
            Throw New InvalidOperationException("Invalid relative file path.")
        End If
        Dim chosen = SelectedIndexes(report, indexes)
        Dim fresh = Wim360Reader.Inspect(report.ImageFile)
        Dim current = SelectedIndexes(fresh, chosen.Select(Function(x) x.Index))
        Dim records As New List(Of Item)()
        For Each image In current
            token.ThrowIfCancellationRequested()
            If Not original.Values.ContainsKey(image.Index) Then Continue For
            If Not image.MountStatus.StartsWith("Mounted", StringComparison.OrdinalIgnoreCase) OrElse
               String.IsNullOrWhiteSpace(image.MountFolder) Then
                Throw New InvalidOperationException(
                    "Index " & image.Index.ToString() &
                    " is no longer mounted. Refresh the comparison.")
            End If
            Dim basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(image.MountFolder))
            Dim rootPrefix = basePath & Path.DirectorySeparatorChar
            Dim fullName = Path.GetFullPath(Path.Combine(basePath, original.Name))
            If Not fullName.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) OrElse
               Not File.Exists(fullName) Then
                Throw New InvalidOperationException(
                    "A file has moved or is outside the mounted image. Recompare the indexes.")
            End If
            If (File.GetAttributes(fullName) And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then
                Throw New InvalidOperationException(
                    "SHA-256 verification intentionally excludes reparse points.")
            End If
            Dim hashBytes As Byte()
            Using stream As New FileStream(fullName, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite Or FileShare.Delete)
                Using hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                    Dim buffer(131071) As Byte
                    While True
                        token.ThrowIfCancellationRequested()
                        Dim read = stream.Read(buffer, 0, buffer.Length)
                        If read <= 0 Then Exit While
                        hash.AppendData(buffer, 0, read)
                    End While
                    hashBytes = hash.GetHashAndReset()
                End Using
            End Using
            records.Add(New Item With {
                .Index = image.Index, .Kind = "Files", .Key = original.Name,
                .Value = Convert.ToHexString(hashBytes)
            })
        Next
        Dim compared = CompareRecords(report.ImageFile, "SHA-256 verification",
            chosen.Select(Function(x) x.Index), records)
        Dim checkedRow = compared.Rows.Single()
        checkedRow.Notes = "SHA-256 verified for the available copies at scan time. " &
            "Absent indexes remain absent. Large files may take time to hash."
        Return checkedRow
    End Function

    Public Shared Function CompareRecords(imageFile As String, mode As String,
                       indexes As IEnumerable(Of Integer),
                       items As IEnumerable(Of Item)) As Result
        If items Is Nothing Then Throw New ArgumentNullException(NameOf(items))
        Dim chosen = indexes.Distinct().OrderBy(Function(x) x).ToList()
        If chosen.Count < 2 OrElse chosen.Count > 8 Then
            Throw New ArgumentException("Select between two and eight image indexes.")
        End If
        Dim result As New Result With {
            .ImageFile = If(imageFile, ""), .Mode = If(mode, ""),
            .Indexes = chosen
        }
        Dim all = items.Where(Function(x) chosen.Contains(x.Index)).
                        GroupBy(Function(x) x.Kind & ChrW(0) & x.Key,
                                StringComparer.OrdinalIgnoreCase)
        For Each group In all
            Dim first = group.First()
            Dim row As New Row With {.Kind = first.Kind, .Name = first.Key}
            Dim ambiguous As Boolean = False
            For Each index In chosen
                Dim sources = group.Where(Function(x) x.Index = index).ToList()
                If sources.Count > 0 Then
                    If sources.Count > 1 Then ambiguous = True
                    row.Values.Add(index, String.Join(" | ",
                        sources.Select(Function(x) If(x.Value, "")).
                            Distinct(StringComparer.OrdinalIgnoreCase)))
                End If
            Next
            row.PresentCount = row.Values.Count
            If ambiguous Then
                row.State = "Ambiguous duplicates"
                row.Notes = "More than one item has the same identity in an index."
            ElseIf row.PresentCount = 1 Then
                row.State = "Only in Index " & row.Values.Keys.First().ToString()
            ElseIf row.PresentCount < chosen.Count Then
                row.State = "Missing in some"
            ElseIf row.Values.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1 Then
                row.State = "Different"
            Else
                row.State = "Present in all"
            End If
            result.Rows.Add(row)
        Next
        result.Rows = result.Rows.OrderBy(Function(x) x.Kind,
                                          StringComparer.OrdinalIgnoreCase).
                                  ThenBy(Function(x) x.Name,
                                         StringComparer.OrdinalIgnoreCase).ToList()
        Return result
    End Function

    Public Shared Function Filter(rows As IEnumerable(Of Row), category As String,
                                  state As String, query As String,
                                  Optional totalIndexes As Integer = 0) As List(Of Row)
        If rows Is Nothing Then Throw New ArgumentNullException(NameOf(rows))
        Dim typeText = If(category, "")
        Dim stateText = If(state, "")
        Dim needle = If(query, "").Trim()
        Return rows.Where(Function(x)
            If typeText.Length > 0 AndAlso typeText <> "All types" AndAlso
               Not String.Equals(x.Kind, typeText, StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If
            If stateText = "Missing / only in some" Then
                If Not (x.State.StartsWith("Only in Index ", StringComparison.Ordinal) OrElse
                        x.State = "Missing in some") Then Return False
            ElseIf stateText.StartsWith("Missing from Index ", StringComparison.Ordinal) Then
                Dim value As Integer
                If Not Integer.TryParse(stateText.Substring("Missing from Index ".Length),
                                        value) OrElse x.Values.ContainsKey(value) Then Return False
            ElseIf stateText.StartsWith("Present in Index ", StringComparison.Ordinal) Then
                Dim value As Integer
                If Not Integer.TryParse(stateText.Substring("Present in Index ".Length),
                                        value) OrElse Not x.Values.ContainsKey(value) Then Return False
            ElseIf stateText = "Exists in all" Then
                ' Present in every selected image, even when version/size differs.
                If totalIndexes < 2 OrElse x.PresentCount <> totalIndexes Then Return False
            ElseIf stateText = "Present in all" Then
                ' Identical reported values in every selected image.
                If x.State <> "Present in all" Then Return False
            ElseIf stateText <> "" AndAlso stateText <> "All results" AndAlso
                   Not String.Equals(x.State, stateText, StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If
            If needle.Length > 0 Then
                If x.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   x.Kind.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   x.State.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
                Return x.Values.Values.Any(Function(v) v.IndexOf(
                    needle, StringComparison.OrdinalIgnoreCase) >= 0)
            End If
            Return True
        End Function).ToList()
    End Function

    Private Shared Function CsvCell(value As String) As String
        Dim raw = If(value, "")
        Dim trimmed = raw.TrimStart({" "c, ChrW(9), ChrW(13), ChrW(10)})
        If trimmed.Length > 0 AndAlso "=+-@".Contains(trimmed(0)) Then
            raw = "'" & raw
        End If
        Dim quote = ChrW(34).ToString()
        Return quote & raw.Replace(quote, quote & quote) & quote
    End Function

    Public Shared Sub WriteCsv(writer As TextWriter, result As Result,
                               visibleRows As IEnumerable(Of Row))
        If writer Is Nothing Then Throw New ArgumentNullException(NameOf(writer))
        If result Is Nothing Then Throw New ArgumentNullException(NameOf(result))
        Dim columns = {"Kind", "Item", "Result", "Notes"}.
                      Concat(result.Indexes.Select(Function(x) "Index " & x.ToString())).ToList()
        writer.WriteLine(String.Join(",", columns.Select(Function(x) CsvCell(x))))
        For Each item In visibleRows
            Dim cells = New String() {item.Kind, item.Name, item.State, item.Notes}.
                Concat(result.Indexes.Select(Function(x) item.At(x)))
            writer.WriteLine(String.Join(",", cells.Select(Function(x) CsvCell(x))))
        Next
    End Sub

    Public Shared Function ToCsv(result As Result, visibleRows As IEnumerable(Of Row)) As String
        Using writer As New StringWriter(CultureInfo.InvariantCulture)
            WriteCsv(writer, result, visibleRows)
            Return writer.ToString()
        End Using
    End Function
End Class
