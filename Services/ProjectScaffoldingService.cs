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
/// Only token substituted for now: {{ProjectName}}.
///
/// Content shared across web templates (layout, site.css, scripts.js,
/// .csproj, appsettings.json, the example model/service pair) uses "Web*"
/// names rather than being duplicated per template — Razor Pages and MVC
/// both reference the exact same WebLayoutContent/WebSiteCssContent/etc.
/// Only the genuinely template-specific pieces (Program.cs, the actual
/// pages/views/controllers) get their own constants.
///
/// House-style principles: no Bootstrap/jQuery ever, BEM naming for all
/// CSS with an in-file guideline comment (see docs/ROADMAP.md section 2.3).
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
                new("{{ProjectName}}.csproj", WebCsprojContent),
                new("Program.cs", RazorPagesProgramCsContent),
                new("appsettings.json", WebAppSettingsContent),
                new("Pages/_ViewImports.cshtml", RazorPagesViewImportsContent),
                new("Pages/_ViewStart.cshtml", WebViewStartContent),
                new("Pages/Shared/_Layout.cshtml", WebLayoutContent),
                new("Pages/Index.cshtml", RazorPagesIndexCshtmlContent),
                new("Pages/Index.cshtml.cs", RazorPagesIndexCsContent),
                new("Pages/Error.cshtml", RazorPagesErrorCshtmlContent),
                new("Pages/Error.cshtml.cs", RazorPagesErrorCsContent),
                new("Models/ExampleModel.cs", WebExampleModelContent),
                new("Services/IExampleService.cs", WebIExampleServiceContent),
                new("Services/ExampleService.cs", WebExampleServiceContent),
                new("wwwroot/css/site.css", WebSiteCssContent),
                new("wwwroot/js/scripts.js", WebScriptsJsContent)
            }),

        new ProjectTemplate(
            "ASP.NET Core MVC",
            "An MVC web app — controllers + views, no Bootstrap, no jQuery, same house style as the " +
            "Razor Pages template.",
            "csharp",
            new List<TemplateFile>
            {
                new(".gitignore", GitIgnoreContent),
                new("README.md", MvcReadmeContent),
                new("{{ProjectName}}.csproj", WebCsprojContent),
                new("Program.cs", MvcProgramCsContent),
                new("appsettings.json", WebAppSettingsContent),
                new("Controllers/HomeController.cs", MvcHomeControllerContent),
                new("Views/_ViewImports.cshtml", MvcViewImportsContent),
                new("Views/_ViewStart.cshtml", WebViewStartContent),
                new("Views/Shared/_Layout.cshtml", WebLayoutContent),
                new("Views/Home/Index.cshtml", MvcIndexCshtmlContent),
                new("Views/Shared/Error.cshtml", MvcErrorCshtmlContent),
                new("Models/ErrorViewModel.cs", MvcErrorViewModelContent),
                new("Models/ExampleModel.cs", WebExampleModelContent),
                new("Services/IExampleService.cs", WebIExampleServiceContent),
                new("Services/ExampleService.cs", WebExampleServiceContent),
                new("wwwroot/css/site.css", WebSiteCssContent),
                new("wwwroot/js/scripts.js", WebScriptsJsContent)
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

    // ---- Shared across every ASP.NET Core web template ----

    private const string WebCsprojContent = """
        <Project Sdk="Microsoft.NET.Sdk.Web">

          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>

        </Project>
        """;

    private const string WebAppSettingsContent = """
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

    private const string WebViewStartContent = """
        @{
            Layout = "_Layout";
        }
        """;

    private const string WebLayoutContent = """
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
                <nav class="site-header__nav">
                    <a class="site-header__brand" href="/">{{ProjectName}}</a>
                </nav>
            </header>

            <main class="site-main">
                @RenderBody()
            </main>

            <footer class="site-footer">
                <p class="site-footer__text">&copy; @DateTime.Now.Year - {{ProjectName}}</p>
            </footer>

            @await RenderSectionAsync("Scripts", required: false)
            <script src="~/js/scripts.js"></script>
        </body>
        </html>
        """;

    private const string WebSiteCssContent = """
        /* =============================================
           TABLE OF CONTENTS
           1. Design Tokens
           2. Base / Reset
           3. Layout
           4. Navigation
           5. Site Footer
           ============================================= */

        /* -----------------------------------------------
           BEM NAMING CONVENTION
           block__element--modifier

           - Block: a standalone, reusable component (e.g. .site-header)
           - Element: a part of a block, tied to it, never used alone
             (e.g. .site-header__brand)
           - Modifier: a variant/state of a block or element
             (e.g. .site-header--transparent)

           All elements use their block's name, regardless of how deeply
           they're nested in the actual HTML — never chain more than one
           __element (no block__element__subelement). This convention
           applies across every Glue template that ships CSS, not just
           this one.
           ----------------------------------------------- */

        /* #region 1. Design Tokens */
        :root {
          --color-bg: #ffffff;
          --color-text: #1a1a1a;
          --color-text-muted: #666;
          --color-border: #e0e0e0;
          --font-sans: system-ui, -apple-system, "Segoe UI", sans-serif;
        }
        /* #endregion */

        /* #region 2. Base / Reset */
        * {
          box-sizing: border-box;
        }

        body {
          margin: 0; /* Removes the browser's default body margin */
          font-family: var(--font-sans);
          line-height: 1.5;
          color: var(--color-text);
          background: var(--color-bg);
        }
        /* #endregion */

        /* #region 3. Layout */
        /* Flexbox sticky footer: body becomes a full-height column flex
           container, main grows to fill whatever space is left, footer
           stays pinned to the bottom even on short pages. */
        html, body {
          height: 100%;
        }

        body {
          display: flex;
          flex-direction: column;
          min-height: 100vh;
        }

        .site-main {
          flex: 1 0 auto;
          width: 100%;
          padding: 1.5rem;
          max-width: 960px;
          margin: 0 auto;
        }
        /* #endregion */

        /* #region 4. Navigation */
        .site-header {
          flex-shrink: 0;
          padding: 1rem 1.5rem;
          border-bottom: 1px solid var(--color-border);
        }

        .site-header__brand {
          font-weight: 600;
          text-decoration: none;
          color: inherit;
        }
        /* #endregion */

        /* #region 5. Site Footer */
        .site-footer {
          flex-shrink: 0;
          padding: 1rem 1.5rem;
          border-top: 1px solid var(--color-border);
          font-size: 0.875rem;
          color: var(--color-text-muted);
        }

        .site-footer__text {
          margin: 0;
        }
        /* #endregion */
        """;

    private const string WebScriptsJsContent = """
        // {{ProjectName}} — base vanilla JS file. No jQuery, no framework —
        // add your own code here as needed.

        document.addEventListener("DOMContentLoaded", () => {
            // Your code here.
        });
        """;

    private const string WebExampleModelContent = """
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

    private const string WebIExampleServiceContent = """
        namespace {{ProjectName}}.Services;

        public interface IExampleService
        {
            string GetGreeting(string name);
        }
        """;

    private const string WebExampleServiceContent = """
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

    // ---- Razor Pages specific ----

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

    private const string RazorPagesViewImportsContent = """
        @namespace {{ProjectName}}.Pages
        @using {{ProjectName}}
        @addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
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

    // ---- MVC specific ----

    private const string MvcProgramCsContent = """
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllersWithViews();

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthorization();

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        app.Run();
        """;

    private const string MvcViewImportsContent = """
        @using {{ProjectName}}
        @using {{ProjectName}}.Models
        @addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
        """;

    private const string MvcHomeControllerContent = """
        using Microsoft.AspNetCore.Mvc;
        using {{ProjectName}}.Models;

        namespace {{ProjectName}}.Controllers;

        public class HomeController : Controller
        {
            public IActionResult Index()
            {
                return View();
            }

            public IActionResult Error()
            {
                return View(new ErrorViewModel());
            }
        }
        """;

    private const string MvcIndexCshtmlContent = """
        @{
            ViewData["Title"] = "Home";
        }

        <h1>Welcome to {{ProjectName}}</h1>
        <p>This is your new MVC project — no Bootstrap, no jQuery, just your own styles and vanilla JS when you need it.</p>
        """;

    private const string MvcErrorCshtmlContent = """
        @model {{ProjectName}}.Models.ErrorViewModel
        @{
            ViewData["Title"] = "Error";
        }

        <h1>Error</h1>
        <p>An error occurred while processing your request.</p>
        """;

    private const string MvcErrorViewModelContent = """
        namespace {{ProjectName}}.Models;

        public class ErrorViewModel
        {
            public string? RequestId { get; set; }
        }
        """;

    private const string MvcReadmeContent = """
        # {{ProjectName}}

        An ASP.NET Core MVC project, scaffolded with Glue.

        ## Getting started

        ```bash
        dotnet restore
        dotnet run
        ```

        ## Project structure

        - `Controllers/` — MVC controllers
        - `Views/` — Razor views
        - `Models/` — data/domain models
        - `Services/` — application services
        - `wwwroot/` — static files (CSS, JS)

        No Bootstrap, no jQuery — `wwwroot/css/site.css` and `wwwroot/js/scripts.js` are yours to build on.
        """;
}
