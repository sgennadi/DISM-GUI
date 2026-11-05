Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Text

' Pure, read-only comparison of WIMGAPI index metadata.
' Never mounts, extracts, hashes files, or assumes matching metadata means
' matching contents. Can be used immediately after Inspect and on SWM metadata.
Public NotInheritable Class Wim360InspectOverview
    Private Sub New()
    End Sub

    Public NotInheritable Class SuggestedPair
        Public Property FirstIndex As Integer
        Public Property SecondIndex As Integer
        Public Property Reason As String = ""
        Public Property Score As Integer
    End Class

    Public NotInheritable Class OverviewResult
        Public Property Text As String = ""
        Public Property SuggestedPairs As New List(Of SuggestedPair)()
    End Class

    Private Shared Function Known(value As String) As Boolean
        Return Not String.IsNullOrWhiteSpace(value) AndAlso
               Not String.Equals(value.Trim(), "Not listed",
                                 StringComparison.OrdinalIgnoreCase) AndAlso
               Not String.Equals(value.Trim(), "Unknown",
                                 StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function EditionOf(image As Wim360Reader.ImageInfo) As String
        If Known(image.Edition) Then Return image.Edition.Trim()
        If Known(image.Flags) Then Return image.Flags.Trim()
        Return "Not listed"
    End Function

    Private Shared Function LanguagesOf(image As Wim360Reader.ImageInfo) As String
        If Not Known(image.Languages) Then Return "Not listed"
        Dim values = image.Languages.Split(","c).
            Select(Function(x) x.Trim()).
            Where(Function(x) x.Length > 0).
            Distinct(StringComparer.OrdinalIgnoreCase).
            OrderBy(Function(x) x, StringComparer.OrdinalIgnoreCase).ToArray()
        Return If(values.Length = 0, "Not listed", String.Join(", ", values))
    End Function

    Private Shared Function VersionOf(image As Wim360Reader.ImageInfo) As String
        Return If(Known(image.Version), image.Version.Trim(), "Not listed")
    End Function

    Private Shared Function ArchitectureOf(image As Wim360Reader.ImageInfo) As String
        Return If(Known(image.Architecture), image.Architecture.Trim(), "Not listed")
    End Function

    Private Shared Function CompactIndexes(indexes As IEnumerable(Of Integer)) As String
        Dim chosen = indexes.OrderBy(Function(n) n).ToList()
        Dim first = chosen.Take(8).Select(Function(n) n.ToString()).ToArray()
        Dim ending = If(chosen.Count > 8, ", +" & (chosen.Count - 8).ToString() & " more", "")
        Return String.Join(", ", first) & ending
    End Function

    Private Shared Function DescribeGroups(images As IList(Of Wim360Reader.ImageInfo),
                                            selector As Func(Of Wim360Reader.ImageInfo, String),
                                            heading As String) As String
        Dim groups = images.GroupBy(selector, StringComparer.OrdinalIgnoreCase).ToList()
        If groups.Count = 1 Then
            Dim onlyGroup = groups(0)
            Return heading & ": " & onlyGroup.Key & If(
                Known(onlyGroup.Key), " in all " & images.Count.ToString() & " indexes.",
                " (no reliable comparison possible).")
        End If
        Dim parts = groups.Take(6).
            Select(Function(g) g.Key & " [" &
                         CompactIndexes(g.Select(Function(item) item.Index)) & "]").ToArray()
        Dim overflow = If(groups.Count > 6,
                          "; +" & (groups.Count - 6).ToString() & " more groups", "")
        Return heading & ": " & String.Join("; ", parts) & overflow & "."
    End Function

    Private Shared Function Propose(first As Wim360Reader.ImageInfo,
                                    second As Wim360Reader.ImageInfo) As SuggestedPair
        Dim sameArchitecture = Known(first.Architecture) AndAlso
            Known(second.Architecture) AndAlso
            String.Equals(first.Architecture.Trim(), second.Architecture.Trim(),
                          StringComparison.OrdinalIgnoreCase)
        Dim sameBuild = Known(first.Version) AndAlso Known(second.Version) AndAlso
            String.Equals(first.Version.Trim(), second.Version.Trim(),
                          StringComparison.OrdinalIgnoreCase)
        Dim leftEdition = EditionOf(first)
        Dim rightEdition = EditionOf(second)
        Dim differentEdition = Known(leftEdition) AndAlso Known(rightEdition) AndAlso
            Not String.Equals(leftEdition, rightEdition, StringComparison.OrdinalIgnoreCase)
        Dim sameEdition = Known(leftEdition) AndAlso Known(rightEdition) AndAlso
            Not differentEdition
        Dim differentBuild = Known(first.Version) AndAlso Known(second.Version) AndAlso
            Not sameBuild
        Dim leftLanguage = LanguagesOf(first)
        Dim rightLanguage = LanguagesOf(second)
        Dim differentLanguages = Known(leftLanguage) AndAlso Known(rightLanguage) AndAlso
            Not String.Equals(leftLanguage, rightLanguage, StringComparison.OrdinalIgnoreCase)

        Dim reason As String
        Dim weight As Integer
        If sameArchitecture AndAlso sameBuild AndAlso differentEdition Then
            reason = "same build/architecture; different editions"
            weight = 100
        ElseIf sameArchitecture AndAlso sameEdition AndAlso differentBuild Then
            reason = "same edition/architecture; different builds"
            weight = 95
        ElseIf sameArchitecture AndAlso sameBuild AndAlso differentLanguages Then
            reason = "same build/architecture; different languages"
            weight = 90
        ElseIf sameArchitecture AndAlso sameBuild Then
            reason = "matching build/architecture; inspect file-level differences"
            weight = 80
        ElseIf sameArchitecture AndAlso differentBuild Then
            reason = "same architecture; different builds"
            weight = 70
        ElseIf differentEdition Then
            reason = "different editions; verify architecture/build compatibility"
            weight = 60
        ElseIf Known(first.Architecture) AndAlso Known(second.Architecture) AndAlso
               Not sameArchitecture Then
            reason = "different architectures; compare only if relevant"
            weight = 30
        Else
            reason = "metadata is incomplete; inspect differences manually"
            weight = 10
        End If
        Return New SuggestedPair With {
            .FirstIndex = first.Index, .SecondIndex = second.Index,
            .Reason = reason, .Score = weight
        }
    End Function

    Public Shared Function Analyze(report As Wim360Reader.WimReport) As OverviewResult
        If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
        Dim ordered = report.Images.OrderBy(Function(item) item.Index).ToList()
        Dim overview As New OverviewResult()
        If ordered.Count = 0 Then
            overview.Text = "No image indexes were found. Inspect a WIM/ESD/SWM file."
            Return overview
        End If

        Dim lines As New List(Of String) From {
            ordered.Count.ToString() & " index" & If(ordered.Count = 1, "", "es") &
            " | automatic read-only metadata overview",
            DescribeGroups(ordered, AddressOf EditionOf, "Editions"),
            DescribeGroups(ordered, AddressOf VersionOf, "OS builds"),
            DescribeGroups(ordered, AddressOf ArchitectureOf, "Architectures"),
            DescribeGroups(ordered, AddressOf LanguagesOf, "Languages")
        }
        If ordered.Count >= 2 Then
            Dim candidates As New List(Of SuggestedPair)()
            For first As Integer = 0 To ordered.Count - 2
                For second As Integer = first + 1 To ordered.Count - 1
                    candidates.Add(Propose(ordered(first), ordered(second)))
                Next
            Next
            ' Give priority to useful cross-edition, cross-build and language
            ' comparisons, then select up to three deterministic pairs.
            overview.SuggestedPairs = candidates.
                OrderByDescending(Function(p) p.Score).
                ThenBy(Function(p) p.FirstIndex).
                ThenBy(Function(p) p.SecondIndex).
                Take(Math.Min(3, candidates.Count)).ToList()
            For Each pair In overview.SuggestedPairs
                lines.Add("Compare " & pair.FirstIndex.ToString() & " vs " &
                          pair.SecondIndex.ToString() & ": " & pair.Reason & ".")
            Next
        Else
            lines.Add("Only one index: there is no other index to compare.")
        End If
        If ordered.Any(Function(i) Not Known(EditionOf(i)) OrElse
                                     Not Known(i.Version) OrElse
                                     Not Known(i.Architecture)) Then
            lines.Add("Some metadata is missing; unlisted values are not treated as matches.")
        End If
        lines.Add("Use Compare indexes for file/folder differences. " &
                  "Matching metadata does NOT prove identical files.")
        overview.Text = String.Join(Environment.NewLine, lines)
        Return overview
    End Function
End Class
