using System;
using System.Collections.Generic;
using System.IO;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// File access for tests that read the project's own files (source scans, shaders, UXML, assets), by a path
	/// that keeps working when the file moves.
	/// </summary>
	/// <remarks>
	/// <para>
	/// About 250 test files read source and assets by a full project path. The Core and modules split moves
	/// thousands of files between folders, and every such path would break. So every test read goes through
	/// here: a path that exists is used as written; one that does not is treated as a hint and resolved to the
	/// file's current location by the longest trailing part of the path that names exactly one file in the
	/// project (<c>Assets/Scripts/Client/GUI/World/Options/UITKOptions.cs</c> is found wherever
	/// <c>UITKOptions.cs</c> now lives; <c>Attributes.cs</c>, which several plugins have, needs its folder
	/// too). A path that still matches nothing, or matches several files equally well, is reported with the
	/// candidates rather than guessed.
	/// </para>
	/// <para>
	/// Only paths inside the project's <c>Assets</c> and <c>Packages</c> folders are resolved. Anything else
	/// (temporary files a test writes, <c>Library/</c>, paths outside the project) behaves exactly as the
	/// <see cref="File"/> and <see cref="Directory"/> calls it replaces, so a test that checks a file it wrote
	/// is gone still sees it gone.
	/// </para>
	/// <para>
	/// Plain <see cref="System.IO"/> only, no UnityEditor: the same calls work in the editor's test runner and
	/// in the out-of-editor runners that load this assembly by reflection from the project root.
	/// </para>
	/// </remarks>
	internal static class ProjectFiles
	{
		private static readonly string[] Roots = { "Assets", "Packages" };

		private static string indexedRoot;
		private static Dictionary<string, List<string>> filesByName;
		private static Dictionary<string, List<string>> foldersByName;

		/// <summary>The Unity project's root folder (the working directory the test runners use).</summary>
		public static string ProjectRoot => Directory.GetCurrentDirectory();

		/// <summary>
		/// The path a test should use for <paramref name="path"/>: as written when it exists, else the moved
		/// file's current project-relative path. Paths outside Assets and Packages are returned unchanged.
		/// </summary>
		public static string Path(string path) => Resolve(path, folder: false);

		/// <summary>As <see cref="Path"/>, for a folder.</summary>
		public static string Folder(string path) => Resolve(path, folder: true);

		/// <summary><see cref="File.ReadAllText(string)"/> through <see cref="Path"/>.</summary>
		public static string ReadAllText(string path) => File.ReadAllText(Path(path));

		/// <summary><see cref="File.ReadAllLines(string)"/> through <see cref="Path"/>.</summary>
		public static string[] ReadAllLines(string path) => File.ReadAllLines(Path(path));

		/// <summary><see cref="File.ReadLines(string)"/> through <see cref="Path"/>.</summary>
		public static IEnumerable<string> ReadLines(string path) => File.ReadLines(Path(path));

		/// <summary>Whether the file exists as written or can be resolved to its moved location.</summary>
		public static bool Exists(string path) => File.Exists(path) || TryResolve(path, folder: false, out _);

		/// <summary>Whether the folder exists as written or can be resolved to its moved location.</summary>
		public static bool FolderExists(string path) => Directory.Exists(path) || TryResolve(path, folder: true, out _);

		private static string Resolve(string path, bool folder)
		{
			if (string.IsNullOrEmpty(path) || (folder ? Directory.Exists(path) : File.Exists(path)))
			{
				return path;
			}
			if (TryResolve(path, folder, out string resolved))
			{
				return resolved;
			}
			if (Inside(path, out _) && Candidates(path, folder, out List<string> best) && best.Count > 1)
			{
				throw new FileNotFoundException(
					$"'{path}' does not exist, and its name matches {best.Count} {(folder ? "folders" : "files")} equally well: " +
					string.Join(", ", best) + ". Give more of its path.", path);
			}
			return path;   // not resolvable: the caller's File/Directory call reports it as it always did
		}

		private static bool TryResolve(string path, bool folder, out string resolved)
		{
			resolved = null;
			if (string.IsNullOrEmpty(path) || !Inside(path, out _))
			{
				return false;
			}
			if (Candidates(path, folder, out List<string> best) && best.Count == 1)
			{
				resolved = best[0];
				return true;
			}
			return false;
		}

		/// <summary>
		/// The project-relative paths matching the longest trailing part of <paramref name="path"/> that matches
		/// anything: one entry when that part names a single file or folder, several when it is ambiguous.
		/// </summary>
		private static bool Candidates(string path, bool folder, out List<string> best)
		{
			EnsureIndex();
			best = null;
			string[] parts = Normalise(path).Split('/');
			string name = parts[parts.Length - 1];
			Dictionary<string, List<string>> index = folder ? foldersByName : filesByName;
			if (!index.TryGetValue(name, out List<string> sameName))
			{
				return false;
			}
			if (sameName.Count == 1)
			{
				best = sameName;
				return true;
			}
			// Several share the name: keep the ones that also share the most trailing folders.
			for (int depth = parts.Length; depth >= 1; depth--)
			{
				string suffix = "/" + string.Join("/", parts, parts.Length - depth, depth);
				var matching = sameName.FindAll(candidate => ("/" + candidate).EndsWith(suffix, StringComparison.Ordinal));
				if (matching.Count > 0)
				{
					best = matching;
					return true;
				}
			}
			return false;
		}

		/// <summary>Whether the path lies inside the project's Assets or Packages folder; relative paths are taken from the project root.</summary>
		private static bool Inside(string path, out string relative)
		{
			relative = Normalise(path);
			string root = Normalise(ProjectRoot);
			if (System.IO.Path.IsPathRooted(path))
			{
				if (!relative.StartsWith(root + "/", StringComparison.Ordinal))
				{
					return false;
				}
				relative = relative.Substring(root.Length + 1);
			}
			foreach (string top in Roots)
			{
				if (relative.StartsWith(top + "/", StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		private static string Normalise(string path) => path.Replace('\\', '/').TrimEnd('/');

		private static void EnsureIndex()
		{
			string root = ProjectRoot;
			if (filesByName != null && indexedRoot == root)
			{
				return;
			}
			indexedRoot = root;
			filesByName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			foldersByName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			foreach (string top in Roots)
			{
				string folder = System.IO.Path.Combine(root, top);
				if (!Directory.Exists(folder))
				{
					continue;
				}
				foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
				{
					if (!file.EndsWith(".meta", StringComparison.Ordinal))
					{
						Add(filesByName, root, file);
					}
				}
				foreach (string dir in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories))
				{
					Add(foldersByName, root, dir);
				}
			}
		}

		private static void Add(Dictionary<string, List<string>> index, string root, string fullPath)
		{
			string relative = Normalise(fullPath.Substring(root.Length)).TrimStart('/');
			string name = relative.Substring(relative.LastIndexOf('/') + 1);
			if (!index.TryGetValue(name, out List<string> list))
			{
				index[name] = list = new List<string>();
			}
			list.Add(relative);
		}
	}
}
