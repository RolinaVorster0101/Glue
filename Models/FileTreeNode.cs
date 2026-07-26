using System.Collections.ObjectModel;

namespace Glue.Models;

/// <summary>
/// A single node in the Explorer sidebar's file tree — either a directory
/// (with children) or a leaf file. Built once when a folder is opened;
/// see Services/ProjectTreeBuilder.cs.
/// </summary>
public class FileTreeNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public ObservableCollection<FileTreeNode> Children { get; } = new();
}
