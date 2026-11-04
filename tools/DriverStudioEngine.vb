Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions

' Driver Studio comparison engine: metadata only, never installs or alters images.
' Same INF + provider + class + date/version is a conservative "same version",
' not proof of identical catalog/signature or Windows device-driver ranking.
Public NotInheritable Class DriverStudioEngine
    Private Sub New()
    End Sub

    Public NotInheritable Class DriverPackage
        Public Property FilePath As String = ""
        Public Property InfName As String = ""
        Public Property PublishedName As String = ""
        Public Property Provider As String = ""
        Public Property ClassName As String = ""
        Public Property Version As String = ""
        Public Property DriverDate As String = ""
        Public Property IsInbox As Boolean
        Public Property Sha256 As String = ""
        Public Property ValidationError As String = ""
        Public Property HardwareIds As New List(Of String)()
    End Class

    Public NotInheritable Class Decision
        Public Property Candidate As DriverPackage
        Public Property Existing As DriverPackage
        Public Property Status As String = "Not checked"
        Public Property Detail As String = ""
        Public Property Selected As Boolean
        Public ReadOnly Property InstalledVersion As String
            Get
                Return If(Existing Is Nothing, "—", If(Existing.Version, "—"))
            End Get
        End Property
    End Class

    Private Shared Function Simplify(input As String) As String
        Return If(input, "").Trim().Trim(""""c).ToUpperInvariant()
    End Function

    Private Shared Function PackageKey(p As DriverPackage) As String
        Return Simplify(p.InfName) & "|" & Simplify(p.Provider) & "|" &
               Simplify(p.ClassName)
    End Function

    Private Shared Function Unquote(raw As String) As String
        Dim trimmed = If(raw, "").Trim()
        If trimmed.StartsWith("""", StringComparison.Ordinal) AndAlso
           trimmed.EndsWith("""", StringComparison.Ordinal) AndAlso
           trimmed.Length > 1 Then
            trimmed = trimmed.Substring(1, trimmed.Length - 2)
        End If
        Return trimmed
    End Function

    Private Shared Function ResolveStrings(value As String,
                                            strings As Dictionary(Of String, String)) As String
        Dim resolved = Unquote(value)
        For i As Integer = 1 To 4
            Dim expanded = Regex.Replace(resolved, "%([^%]+)%",
                Function(m As Match)
                    Dim replacement As String = Nothing
                    If strings.TryGetValue(m.Groups(1).Value, replacement) Then
                        Return replacement
                    End If
                    Return m.Value
                End Function)
            If String.Equals(expanded, resolved, StringComparison.Ordinal) Then Exit For
            resolved = expanded
        Next
        Return Unquote(resolved)
    End Function

    Private Shared Function DateForComparison(input As String) As DateTime?
        Dim dateValue As DateTime
        Dim patterns As String() = {"M/d/yyyy", "MM/dd/yyyy",
            "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ"}
        If DateTime.TryParseExact(If(input, "").Trim(), patterns,
                                  CultureInfo.InvariantCulture,
                                  DateTimeStyles.AllowWhiteSpaces Or
                                  DateTimeStyles.AssumeUniversal,
                                  dateValue) Then Return dateValue.Date
        If DateTime.TryParse(input, CultureInfo.InvariantCulture,
                             DateTimeStyles.AllowWhiteSpaces, dateValue) Then
            Return dateValue.Date
        End If
        Return Nothing
    End Function

    Private Shared Function Same(a As String, b As String) As Boolean
        Return String.Equals(Simplify(a), Simplify(b), StringComparison.Ordinal)
    End Function

    Public Shared Function ReadInf(file As String) As DriverPackage
        Dim item As New DriverPackage With {
            .FilePath = Path.GetFullPath(file),
            .InfName = Path.GetFileName(file)
        }
        If Not File.Exists(item.FilePath) Then
            Throw New FileNotFoundException("Driver INF not found.", item.FilePath)
        End If
        If Not item.InfName.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidDataException("Expected a .inf driver file.")
        End If
        Dim versionEntries As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim stringEntries As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim bytes = File.ReadAllBytes(item.FilePath)
        If bytes.Length > 4 * 1024 * 1024 Then
            Throw New InvalidDataException("INF exceeds the 4 MB parsing limit.")
        End If
        Using sha = SHA256.Create()
            item.Sha256 = Convert.ToHexString(sha.ComputeHash(bytes))
        End Using
        Dim infText As String
        Using mem As New MemoryStream(bytes)
            Using reader As New StreamReader(mem, Encoding.UTF8, True)
                infText = reader.ReadToEnd()
            End Using
        End Using
        Dim section As String = ""
        Dim ids As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each raw As String In infText.Split({vbCrLf, vbLf},
                                               StringSplitOptions.None)
            Dim line = raw.Trim()
            If line.Length = 0 OrElse line.StartsWith(";") Then Continue For
            If line.StartsWith("[") AndAlso line.EndsWith("]") Then
                section = line.Substring(1, line.Length - 2).Trim()
                Continue For
            End If
            Dim commentPosition = line.IndexOf(";"c)
            If commentPosition >= 0 Then line = line.Substring(0, commentPosition).Trim()
            If line.Length = 0 Then Continue For

            Dim equalPosition = line.IndexOf("="c)
            If equalPosition > 0 Then
                Dim key = line.Substring(0, equalPosition).Trim()
                Dim value = line.Substring(equalPosition + 1).Trim()
                If section.Equals("Version", StringComparison.OrdinalIgnoreCase) Then
                    versionEntries(key) = value
                ElseIf section.Equals("Strings", StringComparison.OrdinalIgnoreCase) OrElse
                       section.StartsWith("Strings.", StringComparison.OrdinalIgnoreCase) Then
                    ' Prefer base [Strings] when multiple locale tables exist.
                    If section.Equals("Strings", StringComparison.OrdinalIgnoreCase) OrElse
                       Not stringEntries.ContainsKey(key) Then
                        stringEntries(key) = Unquote(value)
                    End If
                End If
            End If

            If section.IndexOf(".NT", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               section.Equals("Manufacturer", StringComparison.OrdinalIgnoreCase) Then
                For Each m As Match In Regex.Matches(line,
                        "(?:PCI|USB|ACPI|HID|HDAUDIO|ROOT|BTH|SCSI|SWD|DISPLAY|SD|I2C)\\[A-Za-z0-9_&+\.\-\\]+",
                        RegexOptions.IgnoreCase)
                    If ids.Count < 200 Then ids.Add(m.Value.ToUpperInvariant())
                Next
            End If
        Next
        item.HardwareIds = ids.OrderBy(Function(x) x, StringComparer.Ordinal).ToList()
        Dim providerValue As String = Nothing
        If versionEntries.TryGetValue("Provider", providerValue) Then
            item.Provider = ResolveStrings(providerValue, stringEntries)
        End If
        Dim classValue As String = Nothing
        If versionEntries.TryGetValue("Class", classValue) Then
            item.ClassName = ResolveStrings(classValue, stringEntries)
        End If
        Dim driverVer As String = Nothing
        If versionEntries.TryGetValue("DriverVer", driverVer) Then
            Dim parts = driverVer.Split(","c)
            If parts.Length >= 2 Then
                item.DriverDate = Unquote(parts(0))
                item.Version = Unquote(parts(1))
            End If
        End If
        Dim errors As New List(Of String)()
        If item.Provider.Length = 0 Then errors.Add("Provider missing")
        If item.ClassName.Length = 0 Then errors.Add("Class missing")
        If item.Version.Length = 0 Then errors.Add("DriverVer version missing")
        Dim parsedVersion As System.Version = Nothing
        If item.Version.Length > 0 AndAlso
           Not System.Version.TryParse(item.Version, parsedVersion) Then
            errors.Add("Invalid DriverVer version")
        End If
        If item.DriverDate.Length = 0 OrElse
           Not DateForComparison(item.DriverDate).HasValue Then
            errors.Add("Invalid or missing DriverVer date")
        End If
        item.ValidationError = String.Join("; ", errors)
        Return item
    End Function

    Public Shared Function ScanSource(path As String, recurse As Boolean) As List(Of DriverPackage)
        If String.IsNullOrWhiteSpace(path) Then
            Throw New ArgumentException("Choose an INF file or driver directory.")
        End If
        Dim full = Path.GetFullPath(path)
        Dim files As IEnumerable(Of String)
        If File.Exists(full) Then
            files = {full}
        ElseIf Directory.Exists(full) Then
            Dim options As New EnumerationOptions With {
                .RecurseSubdirectories = recurse,
                .IgnoreInaccessible = False,
                .AttributesToSkip = FileAttributes.ReparsePoint,
                .ReturnSpecialDirectories = False
            }
            files = Directory.EnumerateFiles(full, "*.inf", options)
        Else
            Throw New FileNotFoundException("Driver source path does not exist.", full)
        End If

        Dim results As New List(Of DriverPackage)()
        For Each file In files
            If results.Count >= 2000 Then
                Throw New InvalidDataException("Driver Studio stops at 2000 INF files. Select a smaller folder.")
            End If
            Try
                results.Add(ReadInf(file))
            Catch ex As Exception
                results.Add(New DriverPackage With {
                    .FilePath = file, .InfName = Path.GetFileName(file),
                    .ValidationError = ex.Message
                })
            End Try
        Next
        Return results.OrderBy(Function(x) x.InfName, StringComparer.OrdinalIgnoreCase).
                       ThenBy(Function(x) x.FilePath, StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    Private Shared Function IsExactIdentity(a As DriverPackage, b As DriverPackage) As Boolean
        Return Same(a.InfName, b.InfName) AndAlso
               a.Provider.Length > 0 AndAlso b.Provider.Length > 0 AndAlso
               Same(a.Provider, b.Provider) AndAlso
               a.ClassName.Length > 0 AndAlso b.ClassName.Length > 0 AndAlso
               Same(a.ClassName, b.ClassName)
    End Function

    Private Shared Function CompareOne(candidate As DriverPackage,
                                       existing As DriverPackage) As Decision
        Dim d As New Decision With {.Candidate = candidate, .Existing = existing}
        Dim cVersion As System.Version = Nothing
        Dim eVersion As System.Version = Nothing
        If Not System.Version.TryParse(candidate.Version, cVersion) OrElse
           Not System.Version.TryParse(existing.Version, eVersion) Then
            d.Status = "Review"
            d.Detail = "Cannot compare driver versions reliably. Review the two INF packages."
            Return d
        End If
        Dim difference = cVersion.CompareTo(eVersion)
        If difference > 0 Then
            d.Status = "Newer"
            d.Selected = True
            d.Detail = "Incoming version is newer. Windows may still choose another package based on ranking."
        ElseIf difference < 0 Then
            d.Status = "Older"
            d.Detail = "Installed version is newer. Older package is not selected automatically."
        Else
            Dim sourceDate = DateForComparison(candidate.DriverDate)
            Dim inImageDate = DateForComparison(existing.DriverDate)
            If sourceDate.HasValue AndAlso inImageDate.HasValue AndAlso
               sourceDate.Value = inImageDate.Value Then
                d.Status = "Same version"
                d.Detail = "Same INF identity, version and date. Signature, catalog and file contents may differ."
            Else
                d.Status = "Review"
                d.Detail = "Driver version matches, but driver dates differ or cannot be compared."
            End If
        End If
        Return d
    End Function

    Public Shared Function CompareCandidates(source As IEnumerable(Of DriverPackage),
            installed As IEnumerable(Of DriverPackage),
            inventoryVerified As Boolean) As List(Of Decision)
        Dim list = source.ToList()
        Dim existing = installed.ToList()
        Dim decisions As New List(Of Decision)()
        Dim seen As New Dictionary(Of String, DriverPackage)(StringComparer.OrdinalIgnoreCase)
        For Each p In list
            Dim d As New Decision With {.Candidate = p}
            If p.ValidationError.Length > 0 Then
                d.Status = "Review"
                d.Detail = "Invalid or incomplete INF metadata: " & p.ValidationError
            Else
                Dim key = PackageKey(p)
                Dim earlier As DriverPackage = Nothing
                If seen.TryGetValue(key, earlier) AndAlso
                   Same(p.Version, earlier.Version) AndAlso
                   Same(p.DriverDate, earlier.DriverDate) Then
                    d.Status = If(Same(p.Sha256, earlier.Sha256),
                                  "Duplicate source", "Review")
                    d.Detail = If(d.Status = "Duplicate source",
                        "This exact INF file was already listed in the incoming batch.",
                        "Another source INF has the same identity and version but different contents.")
                Else
                    seen(key) = p
                    If Not inventoryVerified Then
                        d.Status = "Not checked"
                        d.Detail = "Image driver inventory not verified. Mount the selected WIM index before comparison."
                    Else
                        Dim matches = existing.Where(Function(x) IsExactIdentity(p, x)).ToList()
                        If matches.Count > 0 Then
                            Dim newest = matches.OrderByDescending(
                                Function(x)
                                    Dim v As System.Version = Nothing
                                    If System.Version.TryParse(x.Version, v) Then Return v
                                    Return New System.Version(0, 0)
                                End Function).First()
                            d = CompareOne(p, newest)
                        Else
                            Dim conflict = existing.FirstOrDefault(Function(x)
                                Return p.HardwareIds.Count > 0 AndAlso
                                       x.HardwareIds.Any(Function(h) p.HardwareIds.Contains(
                                           h, StringComparer.OrdinalIgnoreCase))
                            End Function)
                            If conflict IsNot Nothing Then
                                d.Status = "Review"
                                d.Existing = conflict
                                d.Detail = "Overlapping Hardware ID with a different driver identity. Check compatibility."
                            Else
                                d.Status = "New"
                                d.Selected = True
                                d.Detail = "No package with the same INF/provider/class identity found in this mounted index."
                            End If
                        End If
                    End If
                End If
            End If
            decisions.Add(d)
        Next
        Return decisions
    End Function

    Public Shared Function FormatReport(items As IEnumerable(Of Decision),
                                        target As String) As String
        Dim rows As New List(Of String) From {
            "DISM GUI Driver Studio - Smart Driver Detection (read-only)",
            "Target: " & target,
            "Note: same version is not proof of identical signed driver package.",
            ""
        }
        For Each d In items
            rows.Add(d.Status & " | " & d.Candidate.InfName & " | incoming " &
                     d.Candidate.Version & " | installed " & d.InstalledVersion &
                     " | provider " & d.Candidate.Provider)
            rows.Add("  " & d.Detail)
            rows.Add("  INF: " & d.Candidate.FilePath)
            rows.Add("")
        Next
        Return String.Join(Environment.NewLine, rows)
    End Function
End Class
