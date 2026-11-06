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
        ' Optional descriptive labels for cross-container comparisons.
        ' Classic same-WIM comparisons continue to show Index N.
        Public Property ColumnLabels As New Dictionary(Of Integer, String)()
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

    ' Hashing is intentionally opt-in. The single-file action also supports
    ' missing-index rows; the bounded batch is for files of identical size
    ' in *every* index, where size-only comparison cannot establish equality.
    Public Shared Function VerifyFileHashes(report As Wim360Reader.WimReport,
                                           indexes As IEnumerable(Of Integer),
                                           original As Row,
                                           token As CancellationToken) As Row
        If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
        Dim chosen = SelectedIndexes(report, indexes)
        Dim fresh = Wim360Reader.Inspect(report.ImageFile)
        Dim current = SelectedIndexes(fresh, chosen.Select(Function(x) x.Index))
        Return VerifyAtMountRoots(report.ImageFile, current, original, token)
    End Function

    ' Refuse silently truncated work. The operator must narrow the Type,
    ' Result or Search filters if the visible set exceeds either limit.
    ' A 1 GiB cap measures total bytes read across all selected image indexes.
    Public Shared Function PlanHashBatch(comparison As Result,
                                         visible As IEnumerable(Of Row),
                                         Optional maxPaths As Integer = 50,
                                         Optional maxReadBytes As Long = 1073741824L) As List(Of Row)
        If comparison Is Nothing Then Throw New ArgumentNullException(NameOf(comparison))
        If visible Is Nothing Then Throw New ArgumentNullException(NameOf(visible))
        If comparison.Mode <> "Mounted file paths / sizes" OrElse
           comparison.Indexes.Count < 2 OrElse comparison.Indexes.Count > 8 Then
            Throw New InvalidOperationException(
                "Batch SHA-256 requires a completed mounted-file comparison.")
        End If
        If maxPaths < 1 OrElse maxReadBytes < 1 Then
            Throw New ArgumentOutOfRangeException(NameOf(maxPaths))
        End If
        Dim selected As New List(Of Row)()
        Dim totalBytes As Long = 0
        For Each item In visible
            If item Is Nothing OrElse item.Kind <> "Files" OrElse
               item.State <> "Present in all" OrElse
               item.PresentCount <> comparison.Indexes.Count Then Continue For
            Dim first As String = Nothing
            If Not item.Values.TryGetValue(comparison.Indexes(0), first) OrElse
               Not first.EndsWith(" bytes", StringComparison.Ordinal) Then Continue For
            Dim size As Long
            If Not Long.TryParse(first.Substring(0, first.Length - 6),
                                 NumberStyles.None, CultureInfo.InvariantCulture, size) OrElse
               size < 0 Then Continue For
            If comparison.Indexes.Any(Function(i)
                    Return Not item.Values.ContainsKey(i) OrElse
                           Not String.Equals(item.Values(i), first, StringComparison.Ordinal)
               End Function) Then Continue For
            If selected.Count >= maxPaths Then
                Throw New InvalidOperationException(
                    "More than " & maxPaths.ToString() &
                    " same-size files match the filters. Narrow Search or Result first.")
            End If
            If size > (maxReadBytes - totalBytes) \ comparison.Indexes.Count Then
                Throw New InvalidOperationException(
                    "Visible files exceed the 1 GiB SHA-256 read budget. " &
                    "Narrow Search or Result first.")
            End If
            totalBytes += size * comparison.Indexes.Count
            selected.Add(item)
        Next
        Return selected
    End Function

    ' Inspect mount registrations only once per batch, not once per file.
    ' Return an all-or-nothing list: cancellation and file changes must not
    ' replace half of the comparison with partially verified hash values.
    Public Shared Function VerifyFileHashesBatch(report As Wim360Reader.WimReport,
                            indexes As IEnumerable(Of Integer),
                            rows As IEnumerable(Of Row),
                            token As CancellationToken,
                            Optional progress As IProgress(Of String) = Nothing) As List(Of Row)
        If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
        Dim chosen = SelectedIndexes(report, indexes)
        Dim descriptor As New Result With {
            .Mode = "Mounted file paths / sizes",
            .Indexes = chosen.Select(Function(x) x.Index).ToList()
        }
        Dim candidates = PlanHashBatch(descriptor, rows)
        If candidates.Count = 0 Then
            Throw New InvalidOperationException(
                "No matching same-size files exist in every index. " &
                "Choose All results or Exists in all, then narrow Search.")
        End If
        token.ThrowIfCancellationRequested()
        Dim fresh = Wim360Reader.Inspect(report.ImageFile)
        Dim current = SelectedIndexes(fresh, chosen.Select(Function(x) x.Index))
        Dim verified As New List(Of Row)()
        For Each item In candidates
            token.ThrowIfCancellationRequested()
            If progress IsNot Nothing Then
                progress.Report("SHA-256 " & (verified.Count + 1).ToString() &
                    "/" & candidates.Count.ToString() & ": " & item.Name)
            End If
            verified.Add(VerifyAtMountRoots(report.ImageFile, current, item, token))
        Next
        Return verified
    End Function

    Private Shared Function VerifyAtMountRoots(imageFile As String,
                          current As List(Of Wim360Reader.ImageInfo),
                          original As Row, token As CancellationToken) As Row
        If original Is Nothing OrElse
           Not String.Equals(original.Kind, "Files", StringComparison.OrdinalIgnoreCase) Then
            Throw New ArgumentException("Choose a file row to verify by SHA-256.")
        End If
        If Path.IsPathRooted(original.Name) OrElse
           original.Name.IndexOf(ChrW(0)) >= 0 Then
            Throw New InvalidOperationException("Invalid relative file path.")
        End If
        Dim segments = original.Name.Split(
            New Char() {Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar},
            StringSplitOptions.RemoveEmptyEntries)
        If segments.Length = 0 OrElse segments.Any(Function(x) x = "." OrElse x = "..") Then
            Throw New InvalidOperationException("Invalid relative file path.")
        End If
        Dim records As New List(Of Item)()
        For Each image In current
            token.ThrowIfCancellationRequested()
            If Not original.Values.ContainsKey(image.Index) Then Continue For
            If Not image.MountStatus.StartsWith("Mounted", StringComparison.OrdinalIgnoreCase) OrElse
               String.IsNullOrWhiteSpace(image.MountFolder) OrElse
               Not Directory.Exists(image.MountFolder) Then
                Throw New InvalidOperationException(
                    "Index " & image.Index.ToString() &
                    " is no longer mounted. Refresh the comparison.")
            End If
            Dim basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(image.MountFolder))
            Dim rootPrefix = If(Path.EndsInDirectorySeparator(basePath),
                                basePath, basePath & Path.DirectorySeparatorChar)
            Dim fullName = Path.GetFullPath(Path.Combine(basePath, original.Name))
            If Not fullName.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) OrElse
               Not File.Exists(fullName) Then
                Throw New InvalidOperationException(
                    "A file has moved or is outside the mounted image. Recompare the indexes.")
            End If
            ' Reject junctions or symlinks in *parent* folders as well as the file.
            ' A folder swapped for a junction since listing must not let a
            ' read-only verification escape the selected mount directory.
            Dim parent = Path.GetDirectoryName(fullName)
            While Not String.Equals(parent, basePath, StringComparison.OrdinalIgnoreCase)
                If String.IsNullOrEmpty(parent) OrElse
                   Not parent.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) OrElse
                   (File.GetAttributes(parent) And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then
                    Throw New InvalidOperationException(
                        "SHA-256 verification excludes reparse points and paths outside the mount.")
                End If
                parent = Path.GetDirectoryName(parent)
            End While
            If (File.GetAttributes(fullName) And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then
                Throw New InvalidOperationException(
                    "SHA-256 verification intentionally excludes reparse points.")
            End If
            Dim fileInfo As New FileInfo(fullName)
            Dim initialSize As Long = fileInfo.Length
            Dim initialWriteTime = fileInfo.LastWriteTimeUtc
            Dim recordedValue = original.Values(image.Index)
            If recordedValue.EndsWith(" bytes", StringComparison.Ordinal) Then
                Dim recordedSize As Long
                If Not Long.TryParse(recordedValue.Substring(0, recordedValue.Length - 6),
                                     NumberStyles.None, CultureInfo.InvariantCulture,
                                     recordedSize) OrElse recordedSize <> initialSize Then
                    Throw New InvalidOperationException(
                        "A file size changed since the comparison. Run Compare indexes again.")
                End If
            End If
            Dim hashBytes As Byte()
            Dim totalRead As Long = 0
            Using stream As New FileStream(fullName, FileMode.Open, FileAccess.Read,
                                           FileShare.Read)
                Using hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                    Dim buffer(131071) As Byte
                    While True
                        token.ThrowIfCancellationRequested()
                        Dim read = stream.Read(buffer, 0, buffer.Length)
                        If read <= 0 Then Exit While
                        totalRead += read
                        hash.AppendData(buffer, 0, read)
                    End While
                    hashBytes = hash.GetHashAndReset()
                End Using
            End Using
            fileInfo.Refresh()
            If totalRead <> initialSize OrElse fileInfo.Length <> initialSize OrElse
               fileInfo.LastWriteTimeUtc <> initialWriteTime Then
                Throw New InvalidOperationException(
                    "A file changed while hashing. Refresh the comparison.")
            End If
            records.Add(New Item With {
                .Index = image.Index, .Kind = "Files", .Key = original.Name,
                .Value = Convert.ToHexString(hashBytes)
            })
        Next
        Dim compared = CompareRecords(imageFile, "SHA-256 verification",
            current.Select(Function(x) x.Index), records)
        Dim checkedRow = compared.Rows.Single()
        checkedRow.Notes = "SHA-256 verified for available mounted files. " &
            "Other paths still compare presence and size only."
        Return checkedRow
    End Function

    ' One-pass aggregation: a large install.wim may have hundreds of thousands
    ' of paths per index. Avoid LINQ GroupBy(), which retains a second full
    ' collection of all input items and causes large allocation spikes.
    Public Shared Function CompareRecords(imageFile As String, mode As String,
                       indexes As IEnumerable(Of Integer),
                       items As IEnumerable(Of Item)) As Result
        If items Is Nothing Then Throw New ArgumentNullException(NameOf(items))
        If indexes Is Nothing Then Throw New ArgumentNullException(NameOf(indexes))
        Dim chosen = indexes.Distinct().OrderBy(Function(x) x).ToList()
        If chosen.Count < 2 OrElse chosen.Count > 8 Then
            Throw New ArgumentException("Select between two and eight image indexes.")
        End If
        Dim result As New Result With {
            .ImageFile = If(imageFile, ""), .Mode = If(mode, ""),
            .Indexes = chosen
        }
        Dim allowed As New HashSet(Of Integer)(chosen)
        Dim rowsByPath As New Dictionary(Of String, Row)(
            StringComparer.OrdinalIgnoreCase)
        For Each entry In items
            If entry Is Nothing OrElse Not allowed.Contains(entry.Index) Then Continue For
            Dim identity = If(entry.Kind, "") & ChrW(0) & If(entry.Key, "")
            Dim row As Row = Nothing
            If Not rowsByPath.TryGetValue(identity, row) Then
                row = New Row With {
                    .Kind = If(entry.Kind, ""),
                    .Name = If(entry.Key, "")
                }
                rowsByPath.Add(identity, row)
            End If
            Dim value = If(entry.Value, "")
            Dim previous As String = Nothing
            If row.Values.TryGetValue(entry.Index, previous) Then
                ' Even a same-value duplicate has an ambiguous identity.
                row.Notes = "More than one item has the same identity in an index."
                If Not String.Equals(previous, value,
                                     StringComparison.OrdinalIgnoreCase) Then
                    row.Values(entry.Index) = previous & " | " & value
                End If
            Else
                row.Values.Add(entry.Index, value)
            End If
        Next
        For Each row In rowsByPath.Values
            row.PresentCount = row.Values.Count
            If row.Notes.Length > 0 Then
                row.State = "Ambiguous duplicates"
            ElseIf row.PresentCount = 1 Then
                row.State = "Only in Index " & row.Values.Keys.First().ToString()
            ElseIf row.PresentCount < chosen.Count Then
                row.State = "Missing in some"
            ElseIf row.Values.Values.Distinct(
                   StringComparer.OrdinalIgnoreCase).Count() > 1 Then
                row.State = "Different"
            Else
                row.State = "Present in all"
            End If
        Next
        result.Rows = rowsByPath.Values.
            OrderBy(Function(x) x.Kind, StringComparer.OrdinalIgnoreCase).
            ThenBy(Function(x) x.Name, StringComparer.OrdinalIgnoreCase).ToList()
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
            If stateText = "Differences only" Then
                ' Include changed values, missing/unique rows, and ambiguous
                ' duplicates that need attention; never label matches as changes.
                If Not (x.State = "Different" OrElse
                        x.State = "Missing in some" OrElse
                        x.State = "Ambiguous duplicates" OrElse
                        x.State.StartsWith("Only in Index ", StringComparison.Ordinal)) Then
                    Return False
                End If
            ElseIf stateText = "Missing / only in some" Then
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

    Public Shared Function ColumnLabel(result As Result, index As Integer) As String
        If result Is Nothing Then Throw New ArgumentNullException(NameOf(result))
        Dim label As String = Nothing
        If result.ColumnLabels IsNot Nothing AndAlso
           result.ColumnLabels.TryGetValue(index, label) AndAlso
           Not String.IsNullOrWhiteSpace(label) Then Return label
        Return "Index " & index.ToString(CultureInfo.InvariantCulture)
    End Function

    Public Shared Sub WriteCsv(writer As TextWriter, result As Result,
                               visibleRows As IEnumerable(Of Row))
        If writer Is Nothing Then Throw New ArgumentNullException(NameOf(writer))
        If result Is Nothing Then Throw New ArgumentNullException(NameOf(result))
        Dim columns = {"Kind", "Item", "Result", "Notes"}.
                      Concat(result.Indexes.Select(Function(x) ColumnLabel(result, x))).ToList()
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
