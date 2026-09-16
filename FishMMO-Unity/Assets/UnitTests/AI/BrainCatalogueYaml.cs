using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Reads the server's AI brain catalogue straight from its YAML, for the census tests that
	/// check every NPC prefab without loading assets.
	/// </summary>
	/// <remarks>
	/// NPC prefabs carry no AI since the brain moved to the server assembly; which archetype a
	/// prefab spawns with is its entry in <c>AIBrainCatalogue.asset</c>.
	/// </remarks>
	internal static class BrainCatalogueYaml
	{
		/// <summary>Project-relative path of the catalogue the scene server's AISystem names.</summary>
		public const string CataloguePath = "Assets/Prefabs/Server/SceneServer/AIBrainCatalogue.asset";

		/// <summary>Absolute path of the catalogue.</summary>
		public static string FullPath => Path.Combine(Directory.GetCurrentDirectory(), CataloguePath);

		/// <summary>
		/// Archetype asset guid by NPC prefab guid.
		/// </summary>
		public static Dictionary<string, string> ArchetypeGuidByPrefabGuid()
		{
			return Read("Archetype");
		}

		/// <summary>
		/// Boss script asset guid by NPC prefab guid.
		/// </summary>
		public static Dictionary<string, string> BossScriptGuidByPrefabGuid()
		{
			return Read("BossScript");
		}

		/// <summary>
		/// The guid of an asset, from its .meta file.
		/// </summary>
		public static string GuidOf(string assetPath)
		{
			string meta = assetPath + ".meta";
			if (!File.Exists(meta))
			{
				return null;
			}
			Match guid = Regex.Match(File.ReadAllText(meta), "guid: ([0-9a-f]{32})");
			return guid.Success ? guid.Groups[1].Value : null;
		}

		private static Dictionary<string, string> Read(string field)
		{
			Dictionary<string, string> map = new Dictionary<string, string>();
			if (!File.Exists(FullPath))
			{
				return map;
			}

			// Unity wraps a long reference onto a continuation line; rejoin it before matching.
			string yaml = Regex.Replace(File.ReadAllText(FullPath).Replace("\r\n", "\n"), @",\n\s+type:", ", type:");
			MatchCollection entries = Regex.Matches(yaml,
				@"- Prefab: \{fileID: -?\d+, guid: ([0-9a-f]{32}), type: \d\}\n((?:    .*\n?)*)");
			foreach (Match entry in entries)
			{
				Match value = Regex.Match(entry.Groups[2].Value,
					"    " + field + @": \{fileID: \d+, guid: ([0-9a-f]{32}), type: \d\}");
				if (value.Success)
				{
					map[entry.Groups[1].Value] = value.Groups[1].Value;
				}
			}
			return map;
		}
	}
}
