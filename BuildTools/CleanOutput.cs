using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Xml;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>Preserves Patcher runtime data while cleaning centralized build output.</summary>
public sealed class FishMMOCleanOutputTask : Task
{
    [Required] public string Mode { get; set; }
    [Required] public string OutputDirectory { get; set; }
    public string StateFile { get; set; }
    public string[] TemplateFiles { get; set; }
    public string[] AdditionalDirectories { get; set; }
    [Output] public ITaskItem[] PreservedDirectories { get; set; }

    private readonly StringComparison comparison = Path.DirectorySeparatorChar == '\\'
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private string root;
    private HashSet<string> preserved;

    public override bool Execute()
    {
        try
        {
            root = Path.GetFullPath(OutputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            preserved = new HashSet<string>(Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            if (Mode == "Discover") Discover();
            else if (Mode == "Clean")
            {
                foreach (var item in PreservedDirectories ?? new ITaskItem[0]) Protect(item.ItemSpec);
                if (Directory.Exists(root) && !IsLink(root)) CleanDirectory(root.TrimEnd(Path.DirectorySeparatorChar));
            }
            else throw new InvalidOperationException("Unknown cleanup mode: " + Mode);
            PreservedDirectories = preserved.OrderBy(p => p).Select(p => (ITaskItem)new TaskItem(p)).ToArray();
            return true;
        }
        catch (Exception error)
        {
            Log.LogError("Patcher output cleanup stopped: " + error.Message);
            return false;
        }
    }

    private void Protect(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(root, comparison)) preserved.Add(fullPath);
    }

    private bool IsProtected(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return preserved.Any(p => fullPath.StartsWith(p, comparison));
    }

    private static bool IsLink(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static string JsonName(XmlNode node)
    {
        var mappedName = node.Attributes["item"];
        return mappedName == null ? node.LocalName : mappedName.Value;
    }

    private IEnumerable<string> ReadDirectoryNames(string file)
    {
        if (!File.Exists(file)) return new string[0];
        var names = new List<string>();
        try
        {
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(
                File.ReadAllBytes(file), XmlDictionaryReaderQuotas.Max))
            {
                var document = new XmlDocument();
                document.Load(reader);
                foreach (XmlNode section in document.DocumentElement.ChildNodes)
                {
                    if (JsonName(section).Equals("Patches:DirectoryName", StringComparison.OrdinalIgnoreCase))
                        names.Add(section.InnerText);
                    if (!JsonName(section).Equals("Patches", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (XmlNode setting in section.ChildNodes)
                        if (JsonName(setting).Equals("DirectoryName", StringComparison.OrdinalIgnoreCase) &&
                            setting.Attributes["type"].Value != "null") names.Add(setting.InnerText);
                }
            }
        }
        catch (Exception error)
        {
            throw new InvalidOperationException("Cannot read patch-directory settings in '" + file + "'. " + error.Message, error);
        }
        return names;
    }

    private void Discover()
    {
        // Keep locations beside surviving data, independent of obj/ or deleted appsettings.
        if (!string.IsNullOrEmpty(StateFile) && File.Exists(StateFile))
        {
            var state = new XmlDocument();
            state.Load(StateFile);
            foreach (XmlNode entry in state.SelectNodes("/PatchDirectories/Directory"))
                Protect(Path.Combine(root, entry.InnerText));
        }

        var templateNames = (TemplateFiles ?? new string[0]).SelectMany(ReadDirectoryNames).ToArray();
        var pending = new Stack<string>();
        if (Directory.Exists(root)) pending.Push(root.TrimEnd(Path.DirectorySeparatorChar));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (IsProtected(directory) || IsLink(directory)) continue;
            var configurationFiles = Directory.GetFiles(directory, "appsettings*.json");
            var isApplicationDirectory = directory + Path.DirectorySeparatorChar == root ||
                configurationFiles.Length > 0 || Directory.GetFiles(directory, "*.runtimeconfig.json").Length > 0;
            if (isApplicationDirectory)
            {
                Protect(Path.Combine(directory, "Patches"));
                foreach (var name in templateNames.Concat(configurationFiles.SelectMany(ReadDirectoryNames))
                    .Concat(AdditionalDirectories ?? new string[0])
                    .Concat(new[] { Environment.GetEnvironmentVariable("Patches__DirectoryName"),
                                    Environment.GetEnvironmentVariable("Patches:DirectoryName") }))
                    if (!string.IsNullOrWhiteSpace(name)) Protect(Path.Combine(directory, name));
            }
            foreach (var child in Directory.GetDirectories(directory))
            {
                // The default location stays safe even after obj/ and config files are removed.
                if (Path.GetFileName(child).Equals("Patches", comparison)) Protect(child);
                if (!IsProtected(child) && !IsLink(child)) pending.Push(child);
            }
        }
        var existingData = preserved.Where(p => Directory.Exists(p)).ToArray();
        if (!string.IsNullOrEmpty(StateFile) && existingData.Length > 0)
        {
            var state = new XmlDocument();
            var element = state.CreateElement("PatchDirectories");
            state.AppendChild(element);
            foreach (var path in existingData.OrderBy(p => p))
            {
                var entry = state.CreateElement("Directory");
                entry.InnerText = path.Substring(root.Length);
                element.AppendChild(entry);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(StateFile)));
            state.Save(StateFile);
            Protect(StateFile);
        }
        else if (!string.IsNullOrEmpty(StateFile) && File.Exists(StateFile)) File.Delete(StateFile);
    }

    private void CleanDirectory(string directory)
    {
        if (IsProtected(directory)) return;
        foreach (var entry in Directory.GetFileSystemEntries(directory))
        {
            if (IsProtected(entry)) continue;
            if ((File.GetAttributes(entry) & FileAttributes.Directory) == 0) File.Delete(entry);
            else if (IsLink(entry)) Directory.Delete(entry);
            else CleanDirectory(entry);
        }
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
}
