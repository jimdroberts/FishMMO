#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace FishMMO.Shared
{
	/// <summary>
	/// Reads every text-serialized asset in the project and reports the references that no longer
	/// resolve: components whose script is gone, references to assets that no longer exist, and saved
	/// polymorphic (<c>[SerializeReference]</c>) data whose type cannot be found.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why it reads files instead of loading assets.</b> The project holds about 32,000 YAML assets.
	/// Loading them to ask Unity would take minutes and dirty nothing useful; reading the text takes a few
	/// seconds and sees exactly what is on disk, including scenes nobody has open. It is the safety net for
	/// moving code and assets between folders and assemblies (the Core and modules split): a move that
	/// loses a script's <c>.meta</c>, deletes an asset still in use, or relocates a serialized type
	/// without <c>[MovedFrom]</c> shows up here before it ships.
	/// </para>
	/// <para>
	/// <b>What each finding means.</b> A missing script is an <c>m_Script</c> GUID with no <c>.meta</c>
	/// anywhere in Assets, Packages or the package cache: the component loads as "missing" and its data is
	/// dropped on the next save. A dangling reference is any other asset GUID with no <c>.meta</c>: the field
	/// loads as None. An unresolved type is a managed reference whose recorded assembly, namespace and class
	/// match no loaded type, directly or through <see cref="MovedFromAttribute"/>: Unity keeps the data until
	/// the asset is saved, then silently drops it.
	/// </para>
	/// <para>
	/// Unity's built-in GUIDs (those starting with sixteen zeros) are not files and are skipped. Folders that
	/// are machine-generated or gitignored (<see cref="ExcludedFolders"/>) are skipped too, because another
	/// machine does not have them and its result would differ.
	/// </para>
	/// </remarks>
	public static class AssetReferenceAudit
	{
		/// <summary>Project-relative folders not scanned: generated or gitignored content that differs per machine.</summary>
		public static readonly string[] ExcludedFolders =
		{
			"Assets/LOCAL",
			"Assets/Generated",
			"Assets/StreamingAssets",
			"Assets/Dependencies",
			"Assets/Plugins/FishNet/Demos",
			"Assets/Plugins/FishNet/Example",
			"Assets/Plugins/FishNet/Upgrading",
			"Assets/Templates/World/Atlas",
			"Assets/Templates/World/Systems",
			"Assets/Resources/Prefabs/Shared/WorldSceneDetails",
		};

		/// <summary>The extensions of text-serialized Unity assets that can hold references.</summary>
		private static readonly HashSet<string> ScannedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".asset", ".prefab", ".unity", ".mat", ".lighting", ".controller", ".overrideController", ".anim",
			".spriteatlas", ".spriteatlasv2", ".terrainlayer", ".mixer", ".playable", ".signal", ".guiskin",
			".fontsettings", ".renderTexture", ".cubemap", ".flare", ".shadervariants", ".preset", ".physicMaterial",
		};

		/// <summary>The roots whose <c>.meta</c> files define the GUIDs that exist.</summary>
		private static readonly string[] GuidRoots = { "Assets", "Packages", "Library/PackageCache" };

		private static readonly Regex ScriptPattern = new Regex(@"m_Script: \{fileID: -?\d+, guid: ([0-9a-f]{32}), type: \d\}", RegexOptions.Compiled);
		private static readonly Regex GuidPattern = new Regex(@"guid: ([0-9a-f]{32})", RegexOptions.Compiled);
		private static readonly Regex ManagedTypePattern = new Regex(@"type: \{class: ([^,]*), ns: ([^,]*), asm: ([^}]*)\}", RegexOptions.Compiled);

		/// <summary>The result of one scan.</summary>
		public sealed class Report
		{
			/// <summary>One line per component whose script is gone: <c>missing-script &lt;guid&gt; in &lt;path&gt;</c>.</summary>
			public readonly SortedSet<string> MissingScripts = new SortedSet<string>(StringComparer.Ordinal);

			/// <summary>One line per asset that references a GUID with no asset: <c>dangling &lt;guid&gt; in &lt;path&gt;</c>.</summary>
			public readonly SortedSet<string> DanglingReferences = new SortedSet<string>(StringComparer.Ordinal);

			/// <summary>One line per asset holding a managed reference whose type does not resolve: <c>unresolved-type &lt;asm&gt; &lt;ns&gt;.&lt;class&gt; in &lt;path&gt;</c>.</summary>
			public readonly SortedSet<string> UnresolvedTypes = new SortedSet<string>(StringComparer.Ordinal);

			/// <summary>How many asset files were read.</summary>
			public int FilesScanned;

			/// <summary>How many managed-reference type records were checked.</summary>
			public int ManagedReferencesChecked;

			/// <summary>Every finding, one line each, sorted.</summary>
			public IEnumerable<string> All()
			{
				foreach (string line in MissingScripts) yield return line;
				foreach (string line in DanglingReferences) yield return line;
				foreach (string line in UnresolvedTypes) yield return line;
			}

			/// <summary>A readable summary followed by every finding.</summary>
			public string Format()
			{
				var text = new StringBuilder();
				text.Append("[Asset references] ").Append(FilesScanned).Append(" assets read, ")
					.Append(ManagedReferencesChecked).Append(" managed references checked: ")
					.Append(MissingScripts.Count).Append(" missing script(s), ")
					.Append(DanglingReferences.Count).Append(" dangling reference(s), ")
					.Append(UnresolvedTypes.Count).Append(" unresolved type(s).");
				foreach (string line in All())
				{
					text.Append("\n  ").Append(line);
				}
				return text.ToString();
			}
		}

		/// <summary>
		/// Scans the project at <paramref name="projectRoot"/>. <paramref name="resolveType"/> answers whether a
		/// managed reference's (assembly, namespace, class) resolves; null skips that check.
		/// </summary>
		public static Report Scan(string projectRoot, Func<string, string, string, bool> resolveType)
		{
			var report = new Report();
			HashSet<string> guids = KnownGuids(projectRoot);
			var typeVerdicts = new Dictionary<string, bool>(StringComparer.Ordinal);

			string assets = Path.Combine(projectRoot, "Assets");
			foreach (string file in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
			{
				if (!ScannedExtensions.Contains(Path.GetExtension(file)))
				{
					continue;
				}
				string relative = Relative(projectRoot, file);
				if (IsExcluded(relative) || !IsYaml(file))
				{
					continue;
				}
				report.FilesScanned++;
				ScanFile(file, relative, guids, resolveType, typeVerdicts, report);
			}
			return report;
		}

		/// <summary>
		/// Whether a project-relative path is one of the <see cref="ExcludedFolders"/>, lies inside one, or is a
		/// file of the same name (<c>Atlas.asset</c> beside <c>Atlas/</c>), as the matching .gitignore patterns do.
		/// </summary>
		public static bool IsExcluded(string relativePath)
		{
			foreach (string folder in ExcludedFolders)
			{
				if (relativePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase) &&
					(relativePath.Length == folder.Length || relativePath[folder.Length] == '/' || relativePath[folder.Length] == '.'))
				{
					return true;
				}
			}
			return false;
		}

		private static void ScanFile(string file, string relative, HashSet<string> guids, Func<string, string, string, bool> resolveType,
			Dictionary<string, bool> typeVerdicts, Report report)
		{
			foreach (string line in File.ReadLines(file))
			{
				if (line.IndexOf("guid: ", StringComparison.Ordinal) >= 0)
				{
					Match script = ScriptPattern.Match(line);
					if (script.Success)
					{
						string guid = script.Groups[1].Value;
						if (!IsBuiltIn(guid) && !guids.Contains(guid))
						{
							report.MissingScripts.Add($"missing-script {guid} in {relative}");
						}
						continue;
					}
					foreach (Match reference in GuidPattern.Matches(line))
					{
						string guid = reference.Groups[1].Value;
						if (!IsBuiltIn(guid) && !guids.Contains(guid))
						{
							report.DanglingReferences.Add($"dangling {guid} in {relative}");
						}
					}
				}
				else if (resolveType != null && line.IndexOf("type: {class:", StringComparison.Ordinal) >= 0)
				{
					Match managed = ManagedTypePattern.Match(line);
					if (!managed.Success)
					{
						continue;
					}
					string className = managed.Groups[1].Value.Trim();
					if (className.Length == 0)
					{
						continue;   // a null reference
					}
					string ns = managed.Groups[2].Value.Trim();
					string asm = managed.Groups[3].Value.Trim();
					report.ManagedReferencesChecked++;
					string key = asm + "|" + ns + "|" + className;
					if (!typeVerdicts.TryGetValue(key, out bool resolves))
					{
						resolves = resolveType(asm, ns, className);
						typeVerdicts[key] = resolves;
					}
					if (!resolves)
					{
						string name = ns.Length > 0 ? ns + "." + className : className;
						report.UnresolvedTypes.Add($"unresolved-type {asm} {name} in {relative}");
					}
				}
			}
		}

		private static HashSet<string> KnownGuids(string projectRoot)
		{
			var guids = new HashSet<string>(StringComparer.Ordinal);
			foreach (string root in GuidRoots)
			{
				string folder = Path.Combine(projectRoot, root);
				if (!Directory.Exists(folder))
				{
					continue;
				}
				foreach (string meta in Directory.EnumerateFiles(folder, "*.meta", SearchOption.AllDirectories))
				{
					foreach (string line in File.ReadLines(meta))
					{
						if (line.StartsWith("guid:", StringComparison.Ordinal))
						{
							guids.Add(line.Substring(5).Trim());
							break;
						}
					}
				}
			}
			return guids;
		}

		private static bool IsBuiltIn(string guid) => guid.StartsWith("0000000000000000", StringComparison.Ordinal);

		private static bool IsYaml(string file)
		{
			using (FileStream stream = File.OpenRead(file))
			{
				var head = new byte[5];
				return stream.Read(head, 0, 5) == 5 && head[0] == '%' && head[1] == 'Y' && head[2] == 'A' && head[3] == 'M' && head[4] == 'L';
			}
		}

		private static string Relative(string projectRoot, string file)
		{
			string relative = file.Substring(projectRoot.Length).Replace('\\', '/');
			return relative.TrimStart('/');
		}

		// ── Type resolution against the loaded assemblies ───────────

		private static Dictionary<string, Assembly> assembliesByName;
		private static Dictionary<string, Type> movedFrom;

		/// <summary>
		/// Whether a managed reference's recorded (assembly, namespace, class) names a loaded type, directly
		/// or through a <see cref="MovedFromAttribute"/> on the type it became. Nested classes are recorded as
		/// <c>Outer/Inner</c>; generic arguments after the first <c>[</c> are ignored.
		/// </summary>
		public static bool ResolveLoadedType(string assembly, string ns, string className)
		{
			EnsureTypeTables();
			int generic = className.IndexOf('[');
			string bare = generic >= 0 ? className.Substring(0, generic) : className;
			if (movedFrom.ContainsKey(MovedKey(assembly, ns, bare)))
			{
				return true;
			}
			if (!assembliesByName.TryGetValue(assembly, out Assembly loaded))
			{
				return false;
			}
			string fullName = (ns.Length > 0 ? ns + "." : "") + bare.Replace('/', '+');
			return loaded.GetType(fullName, false) != null;
		}

		/// <summary>Forgets the cached assembly and <see cref="MovedFromAttribute"/> tables (after a domain reload they rebuild anyway).</summary>
		public static void ResetTypeTables()
		{
			assembliesByName = null;
			movedFrom = null;
		}

		private static void EnsureTypeTables()
		{
			if (assembliesByName != null)
			{
				return;
			}
			assembliesByName = new Dictionary<string, Assembly>(StringComparer.Ordinal);
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				string name = assembly.GetName().Name;
				if (!assembliesByName.ContainsKey(name))
				{
					assembliesByName[name] = assembly;
				}
			}

			movedFrom = new Dictionary<string, Type>(StringComparer.Ordinal);
			foreach (Type type in TypeCache.GetTypesWithAttribute<MovedFromAttribute>())
			{
				foreach (CustomAttributeData data in type.GetCustomAttributesData())
				{
					if (data.AttributeType != typeof(MovedFromAttribute))
					{
						continue;
					}
					string oldNamespace = null, oldAssembly = null, oldClass = null;
					IList<CustomAttributeTypedArgument> args = data.ConstructorArguments;
					int first = args.Count > 0 && args[0].ArgumentType == typeof(bool) ? 1 : 0;
					if (args.Count > first) oldNamespace = args[first].Value as string;
					if (args.Count > first + 1) oldAssembly = args[first + 1].Value as string;
					if (args.Count > first + 2) oldClass = args[first + 2].Value as string;
					movedFrom[MovedKey(
						string.IsNullOrEmpty(oldAssembly) ? type.Assembly.GetName().Name : oldAssembly,
						oldNamespace ?? type.Namespace ?? "",
						string.IsNullOrEmpty(oldClass) ? NestedName(type) : oldClass)] = type;
				}
			}
		}

		private static string MovedKey(string assembly, string ns, string className) => assembly + "|" + ns + "|" + className;

		private static string NestedName(Type type)
		{
			string name = type.Name;
			for (Type outer = type.DeclaringType; outer != null; outer = outer.DeclaringType)
			{
				name = outer.Name + "/" + name;
			}
			return name;
		}

		// ── Dashboard ───────────────────────────────────────────────

		[DashboardTool(DashboardToolAttribute.Validate, "Audit asset references", Section = "Project", Order = 40,
			Tooltip = "Reads every text asset in the project (a few seconds) and logs components whose script is missing, references to assets that no longer exist, and saved [SerializeReference] data whose type no longer resolves. Changes nothing.")]
		public static void Audit()
		{
			ResetTypeTables();
			Report report = Scan(Directory.GetCurrentDirectory(), ResolveLoadedType);
			if (report.MissingScripts.Count + report.DanglingReferences.Count + report.UnresolvedTypes.Count == 0)
			{
				Debug.Log(report.Format());
			}
			else
			{
				Debug.LogWarning(report.Format());
			}
		}
	}
}
#endif
