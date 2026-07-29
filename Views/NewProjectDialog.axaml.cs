using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Glue.Services;

namespace Glue.Views;

/// <summary>UI-friendly wrapper around a ProjectTemplate for the picker's data template.</summary>
public record TemplateDisplayItem(string Name, string Description, ProjectTemplate Template);

/// <summary>Result of a confirmed New Project wizard run.</summary>
public record NewProjectResult(ProjectTemplate Template, string ProjectName, string ParentFolder);

/// <summary>
/// The New Project wizard — pick a template (Services/ProjectScaffoldingService.cs),
/// name the project, choose a parent folder. Returns a NewProjectResult via
/// ShowDialog&lt;NewProjectResult?&gt;, or null if cancelled. The actual file
/// creation happens in MainWindow after this dialog closes, not here.
/// </summary>
public partial class NewProjectDialog : Window
{
    private readonly List<TemplateDisplayItem> _items;

    public NewProjectDialog()
    {
        InitializeComponent();
        _items = new List<TemplateDisplayItem>();
    }

    public NewProjectDialog(IReadOnlyList<ProjectTemplate> templates) : this()
    {
        _items = templates
            .Select(t => new TemplateDisplayItem(t.Name, t.Description, t))
            .ToList();

        TemplatesList.ItemsSource = _items;
        if (_items.Count > 0) TemplatesList.SelectedIndex = 0;

        UpdatePreviewPath();
    }

    private void OnTemplateSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdatePreviewPath();

    private void OnProjectNameOrLocationChanged(object? sender, TextChangedEventArgs e) => UpdatePreviewPath();

    private async void OnBrowseClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a location for the new project",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        LocationBox.Text = folders[0].Path.LocalPath;
        UpdatePreviewPath();
    }

    private void UpdatePreviewPath()
    {
        var location = LocationBox.Text;
        var name = ProjectNameBox.Text;

        PreviewPathText.Text = string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(name)
            ? ""
            : $"Will create: {Path.Combine(location, name)}";
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);

    private void OnCreateClicked(object? sender, RoutedEventArgs e)
    {
        if (TemplatesList.SelectedItem is not TemplateDisplayItem selected)
        {
            return; // Create button should really be disabled without a selection — good enough for a first pass.
        }

        var projectName = ProjectNameBox.Text;
        var parentFolder = LocationBox.Text;

        if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(parentFolder)) return;

        Close(new NewProjectResult(selected.Template, projectName, parentFolder));
    }
}
