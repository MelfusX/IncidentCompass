using System.Xml.Linq;

namespace IncidentCompass.UnitTests;

public sealed class ArchitectureTests
{
    private static readonly HashSet<string> ExactReferenceProjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "IncidentCompass.Domain",
        "IncidentCompass.Application"
    };

    private static readonly Dictionary<string, string[]> AllowedReferences = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IncidentCompass.Domain"] = [],
        ["IncidentCompass.Application"] = ["IncidentCompass.Domain"],
        ["IncidentCompass.Infrastructure"] =
            [
                "IncidentCompass.Domain",
                "IncidentCompass.Application"
            ],
        ["IncidentCompass.Api"] =
            [
                "IncidentCompass.Application",
                "IncidentCompass.Infrastructure"
            ],
        ["IncidentCompass.Worker"] =
            [
                "IncidentCompass.Application",
                "IncidentCompass.Infrastructure"
            ],
        ["IncidentCompass.Tester"] = []
    };

    [Fact]
    public void SourceProjects_UseOnlyAllowedProjectReferences()
    {
        var projects = LoadSourceProjects();
        var failures = new List<string>();

        foreach (var project in projects.Values.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!AllowedReferences.TryGetValue(project.Name, out var allowedReferences))
            {
                failures.Add($"{project.Name} is not part of the approved source project matrix.");
                continue;
            }

            var allowed = allowedReferences.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in project.ProjectReferences.OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase))
            {
                if (!allowed.Contains(reference))
                {
                    failures.Add($"{project.Name} must not reference {reference}.");
                }
            }

            if (ExactReferenceProjects.Contains(project.Name))
            {
                var expected = allowedReferences.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var actual = project.ProjectReferences.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                if (!expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase))
                {
                    failures.Add(
                        $"{project.Name} references [{string.Join(", ", actual)}], expected [{string.Join(", ", expected)}].");
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void ApplicationModuleNamespaces_MatchTargetModuleOwnership()
    {
        var projects = LoadSourceProjects();
        var rules = ModuleMembershipRules();
        var failures = new List<string>();

        foreach (var rule in rules)
        {
            if (!projects.TryGetValue(rule.ProjectName, out var project))
            {
                continue;
            }

            foreach (var filePath in EnumerateSourceFiles(project.Directory))
            {
                var relativePath = Path.GetRelativePath(project.Directory, filePath).Replace('\\', '/');
                if (relativePath.Equals("AssemblyInfo.cs", StringComparison.Ordinal))
                {
                    continue;
                }

                var declaredNamespace = ReadDeclaredNamespace(filePath);
                if (declaredNamespace is null)
                {
                    failures.Add($"{rule.ProjectName}/{relativePath} does not declare a namespace.");
                    continue;
                }

                if (!declaredNamespace.StartsWith(rule.NamespacePrefix, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{rule.ProjectName}/{relativePath} declares {declaredNamespace}; expected {rule.NamespacePrefix}.");
                }

                if (rule.AllowedTopLevelFolders.Count > 0 &&
                    !IsAllowedTopLevelFolder(relativePath, rule.AllowedTopLevelFolders))
                {
                    failures.Add(
                        $"{rule.ProjectName}/{relativePath} is outside allowed folders [{string.Join(", ", rule.AllowedTopLevelFolders)}].");
                }

                if (rule.AllowedTopLevelFolders.Count > 0 &&
                    TryGetExpectedFolderNamespace(rule, relativePath, out var expectedFolderNamespace) &&
                    !declaredNamespace.StartsWith(expectedFolderNamespace, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{rule.ProjectName}/{relativePath} declares {declaredNamespace}; expected {expectedFolderNamespace}.");
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void DomainProject_DoesNotDeclareApplicationPorts()
    {
        var domainDirectory = Path.Combine(RepositoryRoot(), "src", "IncidentCompass.Domain");
        var filesWithInterfaces = EnumerateSourceFiles(domainDirectory)
            .Where(filePath => File.ReadAllText(filePath).Contains("interface ", StringComparison.Ordinal))
            .Select(filePath => Path.GetRelativePath(domainDirectory, filePath).Replace('\\', '/'))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(filesWithInterfaces);
    }

    [Fact]
    public void ApplicationProject_DoesNotReferenceExternalMcpSdkOrProcessIo()
    {
        var applicationDirectory = Path.Combine(RepositoryRoot(), "src", "IncidentCompass.Application");
        var forbiddenMarkers = new[]
        {
            "ModelContextProtocol",
            "StdioClientTransport",
            "ProcessStartInfo",
            "System.Diagnostics.Process"
        };
        var failures = new List<string>();

        foreach (var projectPath in Directory.EnumerateFiles(applicationDirectory, "*.csproj"))
        {
            AddForbiddenMarkers(
                failures,
                Path.GetRelativePath(RepositoryRoot(), projectPath),
                File.ReadAllText(projectPath),
                forbiddenMarkers);
        }

        foreach (var sourcePath in EnumerateSourceFiles(applicationDirectory))
        {
            AddForbiddenMarkers(
                failures,
                Path.GetRelativePath(RepositoryRoot(), sourcePath),
                File.ReadAllText(sourcePath),
                forbiddenMarkers);
        }

        Assert.Empty(failures);
    }

    private static Dictionary<string, SourceProject> LoadSourceProjects()
    {
        var sourceDirectory = Path.Combine(RepositoryRoot(), "src");
        return Directory.EnumerateFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories)
            .Select(LoadSourceProject)
            .ToDictionary(project => project.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static SourceProject LoadSourceProject(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var references = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            // ProjectReference Include paths use Windows '\' separators; normalize to '/'
            // so Path APIs resolve them on Linux too (CI runs on ubuntu).
            .Select(include => include!.Replace('\\', '/'))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include)))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SourceProject(Path.GetFileNameWithoutExtension(projectPath), projectDirectory, references);
    }

    private static IReadOnlyList<ModuleMembershipRule> ModuleMembershipRules() =>
        [
            new(
                "IncidentCompass.Application",
                "IncidentCompass.Application",
                ["Core", "Governance", "Intake", "Investigation", "Memory", "SourceContext", "Tickets"])
        ];

    private static IEnumerable<string> EnumerateSourceFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(filePath => !filePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(filePath => !filePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static string? ReadDeclaredNamespace(string filePath)
    {
        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("namespace ", StringComparison.Ordinal))
            {
                continue;
            }

            return trimmed["namespace ".Length..]
                .Trim()
                .TrimEnd(';', '{')
                .Trim();
        }

        return null;
    }

    private static bool IsAllowedTopLevelFolder(string relativePath, IReadOnlyCollection<string> allowedFolders)
    {
        var firstSegment = relativePath.Split('/')[0];
        return firstSegment.Equals("Setup.cs", StringComparison.Ordinal) ||
            firstSegment.Equals("AssemblyInfo.cs", StringComparison.Ordinal) ||
            allowedFolders.Contains(firstSegment);
    }

    private static bool TryGetExpectedFolderNamespace(
        ModuleMembershipRule rule,
        string relativePath,
        out string expectedNamespace)
    {
        var firstSegment = relativePath.Split('/')[0];
        if (firstSegment.EndsWith(".cs", StringComparison.Ordinal) ||
            !rule.AllowedTopLevelFolders.Contains(firstSegment))
        {
            expectedNamespace = string.Empty;
            return false;
        }

        expectedNamespace = $"{rule.NamespacePrefix}.{firstSegment}";
        return true;
    }

    private static void AddForbiddenMarkers(
        List<string> failures,
        string relativePath,
        string content,
        IReadOnlyCollection<string> markers)
    {
        foreach (var marker in markers)
        {
            if (content.Contains(marker, StringComparison.Ordinal))
            {
                failures.Add($"{relativePath} contains forbidden external MCP/I/O marker {marker}.");
            }
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IncidentCompass.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IncidentCompass.slnx.");
    }

    private sealed record SourceProject(string Name, string Directory, string[] ProjectReferences);

    private sealed record ModuleMembershipRule(
        string ProjectName,
        string NamespacePrefix,
        IReadOnlyCollection<string> AllowedTopLevelFolders);
}
