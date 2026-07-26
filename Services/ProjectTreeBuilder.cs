using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Glue.Models;

namespace Glue.Services;

/// <summary>
/// Builds a FileTreeNode tree from a folder on disk, for the Explorer sidebar.
/// Skips the usual noise (bin/obj/.git/.vs/node_modules) so opening your own
/// project folder doesn't dump hundreds of build-output files into the tree.
/// </summary>
public static class ProjectTreeBuilder
{
    private static readonly string[] ExcludedDirectoryNames =
        { "bin", "obj", ".git", ".vs", ".vscode", "node_modules" };

    public static FileTreeNode Build(string rootPath)
    {
        var root = new FileTreeNode
        {
            Name = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar)),
            FullPath = rootPath,
            IsDirectory = true
        };

        PopulateChildren(root);
        return root;
    }

    private static void PopulateChildren(FileTreeNode node)
    {
        IEnumerable<string> directories;
        IEnumerable<string> files;

        try
        {
            directories = Directory.EnumerateDirectories(node.FullPath)
                .Where(d => !ExcludedDirectoryNames.Contains(Path.GetFileName(d)))
                .OrderBy(d => Path.GetFileName(d));

            files = Directory.EnumerateFiles(node.FullPath)
                .OrderBy(f => Path.GetFileName(f));
        }
        catch (UnauthorizedAccessException)
        {
            // A folder we can't read (permissions) — just show it empty
            // rather than crashing the whole tree build.
            return;
        }

        foreach (var dir in directories)
        {
            var childNode = new FileTreeNode
            {
                Name = Path.GetFileName(dir),
                FullPath = dir,
                IsDirectory = true
            };
            PopulateChildren(childNode);
            node.Children.Add(childNode);
        }

        foreach (var file in files)
        {
            node.Children.Add(new FileTreeNode
            {
                Name = Path.GetFileName(file),
                FullPath = file,
                IsDirectory = false
            });
        }
    }
}
