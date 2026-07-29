using System.Collections.Generic;
using System.IO;

namespace Glue.Services;

/// <summary>
/// A single file within a template — a relative path (which may itself
/// contain a {{ProjectName}} token, e.g. "{{ProjectName}}.csproj") and its
/// content (which may also contain tokens).
/// </summary>
public record TemplateFile(string RelativePath, string Content);

/// <summary>
/// A project template — name/description for the wizard's picker, and the
/// set of files it creates. This is the "manifest" the roadmap describes
/// (docs/ROADMAP.md section 2.3), just expressed as a C# record for this
/// first pass rather than a separate JSON file format — the data shape is
/// the same either way, and this avoids building JSON-manifest loading/
/// seeding machinery before there's a real need for user-authored custom
/// templates (a natural follow-up once this foundation is proven).
/// </summary>
public record ProjectTemplate(string Name, string Description, string Language, IReadOnlyList<TemplateFile> Files);

/// <summary>
/// Built-in templates and the logic to actually create a project from one.
/// Only token substituted for now: {{ProjectName}}. House-style principles
/// (no Bootstrap/jQuery, no legacy defaults) apply once web-facing templates
/// are added — the two here (Console App, Class Library) don't have any
/// front-end dependencies to avoid in the first place, so they're a proof
/// of the mechanism, not yet a demonstration of the house-style opinion
/// itself.
/// </summary>
public static class ProjectScaffoldingService
{
    public static IReadOnlyList<ProjectTemplate> GetBuiltInTemplates() => new List<ProjectTemplate>
    {
        new ProjectTemplate(
            "C# Console App",
            "A minimal console application — Program.cs, .csproj, and a correct .gitignore.",
            "csharp",
            new List<TemplateFile>
            {
                new(".gitignore", GitIgnoreContent),
                new("{{ProjectName}}.csproj", ConsoleCsprojContent),
                new("Program.cs", ProgramCsContent)
            }),

        new ProjectTemplate(
            "C# Class Library",
            "A minimal class library — Class1.cs, .csproj, and a correct .gitignore.",
            "csharp",
            new List<TemplateFile>
            {
                new(".gitignore", GitIgnoreContent),
                new("{{ProjectName}}.csproj", ClassLibCsprojContent),
                new("Class1.cs", ClassLibClass1Content)
            }),
    };

    /// <summary>
    /// Creates targetFolder (if it doesn't exist) and writes out every file
    /// in the template, substituting {{ProjectName}} in both file paths and
    /// content.
    /// </summary>
    public static void CreateProject(ProjectTemplate template, string targetFolder, string projectName)
    {
        Directory.CreateDirectory(targetFolder);

        foreach (var file in template.Files)
        {
            var relativePath = file.RelativePath.Replace("{{ProjectName}}", projectName);
            var content = file.Content.Replace("{{ProjectName}}", projectName);

            var fullPath = Path.Combine(targetFolder, relativePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, content);
        }
    }

    private const string GitIgnoreContent =
        "bin/\nobj/\n.vs/\n.vscode/\n*.user\n.DS_Store\nThumbs.db\n";

    private const string ConsoleCsprojContent =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n\n" +
        "  <PropertyGroup>\n" +
        "    <OutputType>Exe</OutputType>\n" +
        "    <TargetFramework>net8.0</TargetFramework>\n" +
        "    <ImplicitUsings>enable</ImplicitUsings>\n" +
        "    <Nullable>enable</Nullable>\n" +
        "  </PropertyGroup>\n\n" +
        "</Project>\n";

    private const string ProgramCsContent =
        "Console.WriteLine(\"Hello from {{ProjectName}}!\");\n";

    private const string ClassLibCsprojContent =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFramework>net8.0</TargetFramework>\n" +
        "    <ImplicitUsings>enable</ImplicitUsings>\n" +
        "    <Nullable>enable</Nullable>\n" +
        "  </PropertyGroup>\n\n" +
        "</Project>\n";

    private const string ClassLibClass1Content =
        "namespace {{ProjectName}};\n\n" +
        "public class Class1\n" +
        "{\n\n" +
        "}\n";
}
