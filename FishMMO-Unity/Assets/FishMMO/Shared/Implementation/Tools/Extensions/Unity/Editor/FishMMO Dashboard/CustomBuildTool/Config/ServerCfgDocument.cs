#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FishMMO.Shared.CustomBuildTool.Config
{
    /// <summary>Edits effective CFG settings without rewriting comments, order or line endings.</summary>
    public sealed class ServerCfgDocument
    {
        private readonly List<string> parts;
        private readonly bool bom;
        private byte[] savedBytes;
        private readonly Dictionary<string, int> indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public string PathName { get; }
        public bool IsDirty => !GetBytes().SequenceEqual(savedBytes);
        public IEnumerable<string> Keys => indices.OrderBy(p => p.Value).Select(p => p.Key);

        public ServerCfgDocument(string path)
        {
            PathName = Path.GetFullPath(path);
            savedBytes = File.ReadAllBytes(PathName);
            bom = savedBytes.Length >= 3 && savedBytes[0] == 0xef && savedBytes[1] == 0xbb && savedBytes[2] == 0xbf;
            string text = new UTF8Encoding(false, true).GetString(savedBytes, bom ? 3 : 0, savedBytes.Length - (bom ? 3 : 0));
            parts = Regex.Split(text, "(\\r\\n|\\r|\\n)").ToList();
            Reindex();
        }

        private void Reindex()
        {
            indices.Clear();
            for (int i = 0; i < parts.Count; i += 2)
            {
                string line = parts[i].TrimStart();
                int equals = line.IndexOf('=');
                if (line.Length == 0 || line[0] == '#' || line[0] == ';' || equals < 1) continue;
                string key = line.Substring(0, equals).Trim();
                if (key.Length > 0) indices[key] = i;
            }
        }

        public string Get(string key, string fallback = "") => indices.TryGetValue(key, out int i)
            ? parts[i].Substring(parts[i].IndexOf('=') + 1).Trim() : fallback;

        public string Description(string key)
        {
            var comments = new List<string>();
            if (indices.TryGetValue(key, out int index))
                for (int i = index - 2; i >= 0; i -= 2)
                {
                    string line = parts[i].Trim();
                    if (!line.StartsWith("#", StringComparison.Ordinal) && !line.StartsWith(";", StringComparison.Ordinal)) break;
                    comments.Insert(0, line.Substring(1).Trim());
                }
            if (key.Equals("ServerName", StringComparison.OrdinalIgnoreCase))
                comments.Add("If using AppHealthMonitor pulse checks, its PulseServerName must match this name.");
            return string.Join("\n", comments);
        }

        public void Set(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key) || key.IndexOfAny(new[] { '=', '\r', '\n' }) >= 0 || key.TrimStart().StartsWith("#") || key.TrimStart().StartsWith(";"))
                throw new ArgumentException("Enter a single CFG key without '=' or a comment prefix.");
            if (value == null || value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("CFG values must fit on a single line.");
            if (indices.TryGetValue(key, out int index))
            {
                string line = parts[index];
                int equals = line.IndexOf('=');
                string oldValue = line.Substring(equals + 1);
                int leading = oldValue.Length - oldValue.TrimStart().Length;
                int trailing = oldValue.Length - oldValue.TrimEnd().Length;
                string suffix = leading == oldValue.Length ? "" : oldValue.Substring(oldValue.Length - trailing);
                parts[index] = line.Substring(0, equals + 1) + oldValue.Substring(0, leading) + value + suffix;
            }
            else
            {
                string newline = parts.Where((_, i) => (i & 1) == 1).FirstOrDefault() ?? "\n";
                if (parts[parts.Count - 1].Length != 0) { parts.Add(newline); parts.Add(""); }
                parts[parts.Count - 1] = key.Trim() + "=" + value;
                parts.Add(newline); parts.Add("");
                Reindex();
            }
        }

        public void Remove(string key)
        {
            // Remove all spellings/duplicates, so deleting the last definition cannot revive an earlier one.
            for (int i = parts.Count - 1; i >= 0; i -= 2)
            {
                string line = parts[i].TrimStart();
                int equals = line.IndexOf('=');
                if (line.StartsWith("#") || line.StartsWith(";") || equals < 1 || !line.Substring(0, equals).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                parts[i] = "";
            }
            Reindex();
        }

        public void Validate(bool production)
        {
            if (string.IsNullOrWhiteSpace(Get("ServerName"))) throw new InvalidOperationException("ServerName is required.");
            if (indices.ContainsKey("Port") && (!int.TryParse(Get("Port"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port < 0 || port > 65535))
                throw new InvalidOperationException("Port must be between 0 and 65535.");
            if (indices.ContainsKey("MaximumClients") && (!int.TryParse(Get("MaximumClients"), out int clients) || clients < 1))
                throw new InvalidOperationException("MaximumClients must be a positive integer.");
            if (indices.ContainsKey("AutoVerifyAccounts"))
            {
                if (!bool.TryParse(Get("AutoVerifyAccounts"), out bool verify)) throw new InvalidOperationException("AutoVerifyAccounts must be true or false.");
                if (production && verify) throw new InvalidOperationException("AutoVerifyAccounts must be false in Production, as required by the server.");
            }
        }

        public void Save(bool production)
        {
            Validate(production);
            if (!File.ReadAllBytes(PathName).SequenceEqual(savedBytes))
                throw new IOException("The CFG changed outside the dashboard. Reload it before saving.");
            byte[] bytes = GetBytes();
            if (bytes.SequenceEqual(savedBytes)) return;
            ReplaceFile(PathName, bytes);
            savedBytes = bytes;
        }

        public static void CopyToBuild(IEnumerable<ServerCfgDocument> documents, string directory, bool production)
        {
            var docs = documents.ToArray();
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Choose an existing built server folder.");
            foreach (var doc in docs)
            {
                doc.Validate(production);
                string target = Path.Combine(directory, Path.GetFileName(doc.PathName));
                if (Path.GetFullPath(target) == doc.PathName) throw new IOException("Choose the built server folder, not the setup template folder.");
                if (!File.Exists(target)) throw new IOException("The selected folder must contain all three server CFG files.");
                if (!File.ReadAllBytes(doc.PathName).SequenceEqual(doc.savedBytes)) throw new IOException("A source CFG changed outside the dashboard. Reload before applying.");
            }
            foreach (var doc in docs)
            {
                doc.Save(production);
                ReplaceFile(Path.Combine(directory, Path.GetFileName(doc.PathName)), doc.GetBytes());
            }
        }

        public byte[] GetBytes()
        {
            byte[] text = Encoding.UTF8.GetBytes(string.Concat(parts));
            return bom ? new byte[] { 0xef, 0xbb, 0xbf }.Concat(text).ToArray() : text;
        }

        private static void ReplaceFile(string path, byte[] bytes)
        {
            string temporary = path + ".dashboard-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                // Copy first to retain the source file's attributes/mode, then replace atomically.
                File.Copy(path, temporary);
                File.WriteAllBytes(temporary, bytes);
                File.Replace(temporary, path, null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
#endif
