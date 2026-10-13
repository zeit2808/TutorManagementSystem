using System.Xml.Linq;

namespace TutorManagement.ArchitectureTests;

public sealed class ProjectDependencyTests
{
    private static readonly string BackendRoot = FindBackendRoot();

    [Fact]
    public void Domain_projects_must_not_reference_upper_layers()
    {
        var violations = FindProjects("*.Domain.csproj")
            .SelectMany(project => ReadProjectReferences(project)
                .Where(reference =>
                    reference.Contains(".Application", StringComparison.OrdinalIgnoreCase) ||
                    reference.Contains(".Infrastructure", StringComparison.OrdinalIgnoreCase) ||
                    reference.Contains(".Api", StringComparison.OrdinalIgnoreCase))
                .Select(reference => $"{Path.GetFileName(project)} -> {reference}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Application_projects_must_not_reference_infrastructure_or_api()
    {
        var violations = FindProjects("*.Application.csproj")
            .SelectMany(project => ReadProjectReferences(project)
                .Where(reference =>
                    reference.Contains(".Infrastructure", StringComparison.OrdinalIgnoreCase) ||
                    reference.Contains(".Api", StringComparison.OrdinalIgnoreCase))
                .Select(reference => $"{Path.GetFileName(project)} -> {reference}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Modules_must_not_reference_another_modules_internals()
    {
        var violations = new List<string>();

        foreach (var project in FindProjects("TutorManagement.Modules.*.csproj"))
        {
            var owner = GetModuleName(project);

            foreach (var reference in ReadProjectReferences(project))
            {
                var referencedModule = GetModuleName(reference);
                var referencesInternalLayer =
                    reference.Contains(".Domain", StringComparison.OrdinalIgnoreCase) ||
                    reference.Contains(".Infrastructure", StringComparison.OrdinalIgnoreCase);

                if (referencedModule is not null &&
                    !string.Equals(owner, referencedModule, StringComparison.OrdinalIgnoreCase) &&
                    referencesInternalLayer)
                {
                    violations.Add($"{Path.GetFileName(project)} -> {reference}");
                }
            }
        }

        Assert.Empty(violations);
    }

    private static IEnumerable<string> FindProjects(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(BackendRoot, "src"), pattern, SearchOption.AllDirectories);

    private static IEnumerable<string> ReadProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        return document.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!);
    }

    private static string? GetModuleName(string path)
    {
        const string marker = "TutorManagement.Modules.";
        var fileName = Path.GetFileNameWithoutExtension(path);
        var markerIndex = fileName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (markerIndex < 0)
        {
            return null;
        }

        var remainder = fileName[(markerIndex + marker.Length)..];
        var separatorIndex = remainder.IndexOf('.');
        return separatorIndex < 0 ? remainder : remainder[..separatorIndex];
    }

    private static string FindBackendRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TutorManagementSystem.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the backend root directory.");
    }
}
