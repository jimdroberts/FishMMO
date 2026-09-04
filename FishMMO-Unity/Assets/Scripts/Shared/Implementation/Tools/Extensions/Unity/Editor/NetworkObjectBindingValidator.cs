#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Finds networked components whose serialized owner points at a NetworkObject in a
	/// different asset, and repairs them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// FishNet caches the owning NetworkObject on every NetworkBehaviour in two hidden serialized
	/// fields. Inside a prefab or scene that reference is local, a bare <c>fileID</c>. It gains a
	/// <c>guid</c> only when it reaches into another asset, which is never right for an owner: a
	/// component cannot be owned by an object in a different file. The stock FishNet editor code
	/// trusted the cached value forever, so anything that copies serialized fields between prefabs
	/// (Paste Component Values, <c>EditorUtility.CopySerialized</c>, a <c>SerializedObject</c>
	/// migration) planted a reference that survived every reimport. Three NPC prefabs carried one
	/// for five months and the orc warrior could not be damaged (PR #212).
	/// </para>
	/// <para>
	/// This is a text scan of the YAML rather than a component walk because the tell is the
	/// <c>guid</c> itself, and the text is what git carries. The same scan runs from four places:
	/// the import hook below (an error the moment a bad asset lands), the build hook (a failed
	/// build), the menu (a repair), and the unit test. The pre-commit hook and CI job in the
	/// repository root grep for the same shape without Unity.
	/// </para>
	/// </remarks>
	public static class NetworkObjectBindingValidator
	{
		/// <summary>Fields FishNet uses to cache the owning NetworkObject.</summary>
		public static readonly string[] OwnerFields =
		{
			"_addedNetworkObject",
			"_networkObjectCache",
		};

		/* Unity wraps long references after the guid, so "type" may sit on the next line. Both
		 * the field name and the guid are always on the same line, which is what the shell grep
		 * relies on. */
		private static readonly Regex Foreign = new Regex(
			"(?<field>" + string.Join("|", OwnerFields) + "): \\{fileID: (?<fileID>-?\\d+), guid: (?<guid>[0-9a-f]{32}),\\s*type: \\d+\\}",
			RegexOptions.Compiled);

		/// <summary>One owner reference that names another asset.</summary>
		public readonly struct Finding
		{
			public readonly string AssetPath;
			public readonly int Line;
			public readonly string Field;
			public readonly string TargetGuid;

			public Finding(string assetPath, int line, string field, string targetGuid)
			{
				AssetPath = assetPath;
				Line = line;
				Field = field;
				TargetGuid = targetGuid;
			}

			public override string ToString()
			{
				string target = AssetDatabase.GUIDToAssetPath(TargetGuid);
				if (string.IsNullOrEmpty(target))
					target = "guid " + TargetGuid;
				return $"{AssetPath}:{Line} {Field} -> {target}";
			}
		}

		/// <summary>True for the asset kinds that serialize NetworkBehaviours.</summary>
		public static bool IsCandidate(string assetPath)
		{
			return assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
				|| assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Scans serialized text. <paramref name="label"/> names it in findings.</summary>
		public static List<Finding> ScanText(string text, string label)
		{
			List<Finding> findings = new List<Finding>();
			foreach (Match match in Foreign.Matches(text))
			{
				int line = 1;
				for (int i = 0; i < match.Index; ++i)
				{
					if (text[i] == '\n')
						++line;
				}
				findings.Add(new Finding(label, line, match.Groups["field"].Value, match.Groups["guid"].Value));
			}
			return findings;
		}

		/// <summary>Scans one prefab or scene file on disk.</summary>
		public static List<Finding> Scan(string assetPath)
		{
			if (!IsCandidate(assetPath) || !File.Exists(assetPath))
				return new List<Finding>();
			return ScanText(File.ReadAllText(assetPath), assetPath);
		}

		/// <summary>Scans every prefab and scene under <paramref name="root"/>.</summary>
		public static List<Finding> ScanAll(string root)
		{
			List<Finding> findings = new List<Finding>();
			foreach (string pattern in new[] { "*.prefab", "*.unity" })
			{
				foreach (string path in Directory.GetFiles(root, pattern, SearchOption.AllDirectories))
					findings.AddRange(Scan(path));
			}
			return findings;
		}

		/// <summary>
		/// Rewrites every foreign owner reference to the bare local <c>fileID</c>. Returns the
		/// repaired text; the count is how many references changed.
		/// </summary>
		/// <remarks>
		/// Dropping the guid is the whole fix. Every prefab in a duplicated family keeps the same
		/// fileIDs, so the id already names this asset's own NetworkObject once the guid stops
		/// redirecting it elsewhere. That is exactly the edit PR #212 made by hand.
		/// </remarks>
		public static string RepairText(string text, out int repaired)
		{
			int count = 0;
			string result = Foreign.Replace(text, m =>
			{
				++count;
				return $"{m.Groups["field"].Value}: {{fileID: {m.Groups["fileID"].Value}}}";
			});
			repaired = count;
			return result;
		}

		/// <summary>Repairs one file in place. Returns how many references changed.</summary>
		public static int Repair(string assetPath)
		{
			if (!IsCandidate(assetPath) || !File.Exists(assetPath))
				return 0;

			string text = File.ReadAllText(assetPath);
			string fixedText = RepairText(text, out int repaired);
			if (repaired > 0)
				File.WriteAllText(assetPath, fixedText, new UTF8Encoding(false));
			return repaired;
		}

		/// <summary>Formats findings for a log line or an exception.</summary>
		public static string Describe(List<Finding> findings)
		{
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("A NetworkBehaviour must be bound to the NetworkObject in its own asset. These point at another asset, which silently breaks targeting and health on the affected entity:");
			foreach (Finding f in findings)
				sb.Append("  ").AppendLine(f.ToString());
			sb.Append("Run FishMMO > Validate > Repair NetworkObject Bindings, or re-open the prefab so FishNet's OnValidate rebinds it.");
			return sb.ToString();
		}

		[MenuItem("FishMMO/Validate/NetworkObject Bindings", priority = 201)]
		public static void Validate()
		{
			List<Finding> findings = ScanAll("Assets");
			if (findings.Count == 0)
				Debug.Log("NetworkObject bindings: every networked component is bound to its own asset.");
			else
				Debug.LogError(Describe(findings));
		}

		[MenuItem("FishMMO/Validate/Repair NetworkObject Bindings", priority = 202)]
		public static void RepairAll()
		{
			List<Finding> findings = ScanAll("Assets");
			HashSet<string> paths = new HashSet<string>();
			foreach (Finding f in findings)
				paths.Add(f.AssetPath);

			int total = 0;
			foreach (string path in paths)
			{
				total += Repair(path);
				AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
			}

			Debug.Log($"NetworkObject bindings: repaired {total} reference(s) in {paths.Count} asset(s).");
		}
	}

	/// <summary>
	/// Reports a foreign owner binding the moment the asset carrying it is imported, which is
	/// the first time the editor sees a paste, a merge, or a pull that introduced one.
	/// </summary>
	internal sealed class NetworkObjectBindingImportGuard : AssetPostprocessor
	{
		private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
		{
			List<NetworkObjectBindingValidator.Finding> findings = null;
			foreach (string path in importedAssets)
			{
				if (!NetworkObjectBindingValidator.IsCandidate(path))
					continue;

				List<NetworkObjectBindingValidator.Finding> here = NetworkObjectBindingValidator.Scan(path);
				if (here.Count == 0)
					continue;

				findings ??= new List<NetworkObjectBindingValidator.Finding>();
				findings.AddRange(here);
			}

			if (findings != null)
				Debug.LogError(NetworkObjectBindingValidator.Describe(findings));
		}
	}

	/// <summary>
	/// Fails any build that would ship a foreign owner binding. Applies to every environment:
	/// this is asset corruption, not configuration, so a development build is no safer with it.
	/// </summary>
	internal sealed class NetworkObjectBindingBuildGuard : IPreprocessBuildWithReport
	{
		public int callbackOrder => 0;

		public void OnPreprocessBuild(BuildReport report)
		{
			List<NetworkObjectBindingValidator.Finding> findings = NetworkObjectBindingValidator.ScanAll("Assets");
			if (findings.Count > 0)
				throw new BuildFailedException("Build blocked: " + NetworkObjectBindingValidator.Describe(findings));
		}
	}
}
#endif
