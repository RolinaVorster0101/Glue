using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Glue.Models;

/// <summary>
/// A single node in the Explorer sidebar's file tree — either a directory
/// (with children) or a leaf file. Built once when a folder is opened;
/// see Services/ProjectTreeBuilder.cs.
///
/// IsModified is set/cleared after the tree is already built (by
/// MainWindow's UpdateModifiedIndicators — see that method's comment), so
/// this needs real change notification for the UI to react to it live,
/// unlike the other properties which are fixed once at construction.
/// </summary>
public class FileTreeNode : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public ObservableCollection<FileTreeNode> Children { get; } = new();

    private bool _isModified;
    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (_isModified == value) return;
            _isModified = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotModified));
        }
    }

    // Avoids relying on "!PropertyName" binding-path negation syntax, whose
    // exact support isn't something we've verified — a plain property is safer.
    public bool IsNotModified => !IsModified;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
