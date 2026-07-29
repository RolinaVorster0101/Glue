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

        new ProjectTemplate(
            "ASP.NET Core Razor Pages",
            "A Razor Pages web app — no Bootstrap, no jQuery, ever. Your own minimal CSS reset instead, " +
            "vanilla JS only if/when you actually need it.",
            "csharp",
            new List<TemplateFile>
            {
                new(".gitignore", GitIgnoreContent),
                new("README.md", RazorPagesReadmeContent),
                new("{{ProjectName}}.csproj", RazorPagesCsprojContent),
                new("Program.cs", RazorPagesProgramCsContent),
                new("appsettings.json", RazorPagesAppSettingsContent),
                new("Pages/_ViewImports.cshtml", RazorPagesViewImportsContent),
                new("Pages/_ViewStart.cshtml", RazorPagesViewStartContent),
                new("Pages/Shared/_Layout.cshtml", RazorPagesLayoutContent),
                new("Pages/Index.cshtml", RazorPagesIndexCshtmlContent),
                new("Pages/Index.cshtml.cs", RazorPagesIndexCsContent),
                new("Pages/Error.cshtml", RazorPagesErrorCshtmlContent),
                new("Pages/Error.cshtml.cs", RazorPagesErrorCsContent),
                new("Models/ExampleModel.cs", RazorPagesExampleModelContent),
                new("Services/IExampleService.cs", RazorPagesIExampleServiceContent),
                new("Services/ExampleService.cs", RazorPagesExampleServiceContent),
                new("wwwroot/css/site.css", RazorPagesSiteCssContent),
                new("wwwroot/js/scripts.js", RazorPagesScriptsJsContent)
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

    // Raw string literals ("""...""") used throughout below rather than
    // manual "\n"-concatenated strings — much less error-prone for blocks
    // this size (HTML/CSS/C# templates), and the indentation of the closing
    // """ determines what gets stripped from each line automatically.

    private const string GitIgnoreContent = """
        bin/
        obj/
        .vs/
        .vscode/
        *.user
        .DS_Store
        Thumbs.db
        """;

    private const string ConsoleCsprojContent = """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net8.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>

        </Project>
        """;

    private const string ProgramCsContent = """
        Console.WriteLine("Hello from {{ProjectName}}!");
        """;

    private const string ClassLibCsprojContent = """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>

        </Project>
        """;

    private const string ClassLibClass1Content = """
        namespace {{ProjectName}};

        public class Class1
        {

        }
        """;

    private const string RazorPagesCsprojContent = """
        <Project Sdk="Microsoft.NET.Sdk.Web">

          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>

        </Project>
        """;

    private const string RazorPagesProgramCsContent = """
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorPages();

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthorization();

        app.MapRazorPages();

        app.Run();
        """;

    private const string RazorPagesAppSettingsContent = """
        {
          "Logging": {
            "LogLevel": {
              "Default": "Information",
              "Microsoft.AspNetCore": "Warning"
            }
          },
          "AllowedHosts": "*"
        }
        """;

    private const string RazorPagesViewImportsContent = """
        @namespace {{ProjectName}}.Pages
        @using {{ProjectName}}
        @addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
        """;

    private const string RazorPagesViewStartContent = """
        @{
            Layout = "_Layout";
        }
        """;

    private const string RazorPagesLayoutContent = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>@ViewData["Title"] - {{ProjectName}}</title>
            <link rel="stylesheet" href="~/css/site.css" />
        </head>
        <body>
            <header class="site-header">
                <nav class="site-nav">
                    <a class="site-nav-brand" href="/">{{ProjectName}}</a>
                </nav>
            </header>

            <main class="site-main">
                @RenderBody()
            </main>

            <footer class="site-footer">
                <p>&copy; @DateTime.Now.Year - {{ProjectName}}</p>
            </footer>

            @await RenderSectionAsync("Scripts", required: false)
            <script src="~/js/scripts.js"></script>
        </body>
        </html>
        """;

    private const string RazorPagesIndexCshtmlContent = """
        @page
        @model IndexModel
        @{
            ViewData["Title"] = "Home";
        }

        <h1>Welcome to {{ProjectName}}</h1>
        <p>This is your new Razor Pages project — no Bootstrap, no jQuery, just your own styles and vanilla JS when you need it.</p>
        """;

    private const string RazorPagesIndexCsContent = """
        using Microsoft.AspNetCore.Mvc.RazorPages;

        namespace {{ProjectName}}.Pages;

        public class IndexModel : PageModel
        {
            public void OnGet()
            {
            }
        }
        """;

    private const string RazorPagesErrorCshtmlContent = """
        @page
        @model ErrorModel
        @{
            ViewData["Title"] = "Error";
        }

        <h1>Error</h1>
        <p>An error occurred while processing your request.</p>
        """;

    private const string RazorPagesErrorCsContent = """
        using Microsoft.AspNetCore.Mvc.RazorPages;

        namespace {{ProjectName}}.Pages;

        public class ErrorModel : PageModel
        {
            public void OnGet()
            {
            }
        }
        """;

    private const string RazorPagesSiteCssContent = """
        /* {{ProjectName}} — house style starter stylesheet. No Bootstrap, ever. */

        * {
            box-sizing: border-box;
        }

        body {
            margin: 0;
            font-family: system-ui, -apple-system, "Segoe UI", sans-serif;
            line-height: 1.5;
            color: #1a1a1a;
            background: #ffffff;
        }

        .site-header {
            padding: 1rem 1.5rem;
            border-bottom: 1px solid #e0e0e0;
        }

        .site-nav-brand {
            font-weight: 600;
            text-decoration: none;
            color: inherit;
        }

        .site-main {
            padding: 1.5rem;
            max-width: 960px;
            margin: 0 auto;
        }

        .site-footer {
            padding: 1rem 1.5rem;
            border-top: 1px solid #e0e0e0;
            font-size: 0.875rem;
            color: #666;
        }
        """;

    private const string RazorPagesReadmeContent = """
        # {{ProjectName}}

        An ASP.NET Core Razor Pages project, scaffolded with Glue.

        ## Getting started

        ```bash
        dotnet restore
        dotnet run
        ```

        ## Project structure

        - `Pages/` — Razor Pages (views + page models)
        - `Models/` — data/domain models
        - `Services/` — application services
        - `wwwroot/` — static files (CSS, JS)

        No Bootstrap, no jQuery — `wwwroot/css/site.css` and `wwwroot/js/scripts.js` are yours to build on.
        """;

    private const string RazorPagesExampleModelContent = """
        namespace {{ProjectName}}.Models;

        /// <summary>
        /// Example model — replace or delete this once you add your own.
        /// </summary>
        public class ExampleModel
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }
        """;

    private const string RazorPagesIExampleServiceContent = """
        namespace {{ProjectName}}.Services;

        public interface IExampleService
        {
            string GetGreeting(string name);
        }
        """;

    private const string RazorPagesExampleServiceContent = """
        namespace {{ProjectName}}.Services;

        /// <summary>
        /// Example service — replace or delete this once you add your own.
        /// Register it in Program.cs, e.g.:
        ///   builder.Services.AddScoped&lt;IExampleService, ExampleService&gt;();
        /// </summary>
        public class ExampleService : IExampleService
        {
            public string GetGreeting(string name) => $"Hello, {name}!";
        }
        """;

    private const string RazorPagesScriptsJsContent = """
        // {{ProjectName}} — base vanilla JS file. No jQuery, no framework —
        // add your own code here as needed.

        document.addEventListener("DOMContentLoaded", () => {
            // Your code here.
        });
        """;
}
