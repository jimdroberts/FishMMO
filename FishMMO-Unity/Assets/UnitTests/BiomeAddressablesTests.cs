using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Every biome asset is addressable, so a player build can load it.
	/// </summary>
	/// <remarks>
	/// Biomes register with <c>BiomeRegistry</c> when the cached-object loader brings them in, and
	/// that loader finds them through Addressables. A biome with no entry exists in the editor —
	/// where the naming loader registers every asset in the folder — and silently does not exist
	/// in a build: the resolver never offers it and a baked biome map naming it resolves to
	/// nothing. Twenty-three biomes added on 2026-09-23, every alien one among them, went nine
	/// days like that before anyone looked. Read from the files, not the API, so it needs no
	/// Addressables settings loaded and runs in any editor.
	/// </remarks>
	[TestFixture]
	public class BiomeAddressablesTests
	{
		private const string BiomesFolder = "Assets/Templates/Entity/Biomes";
		private const string GroupsFolder = "Assets/AddressableAssetsData/AssetGroups";
		/// <summary>The BiomeTemplate script's GUID, so other asset types in the folder are ignored.</summary>
		private const string BiomeTemplateScriptGuid = "b10de7e3a4c14e1f8a9d5c2b6f7e8a01";

		private static readonly Regex MetaGuid = new Regex(@"^guid: (\w+)", RegexOptions.Multiline);
		private static readonly Regex EntryGuid = new Regex(@"m_GUID: (\w+)");

		[Test]
		public void EveryBiomeAssetHasAnAddressableEntry()
		{
			Assume.That(Directory.Exists(BiomesFolder), $"{BiomesFolder} is missing.");
			Assume.That(Directory.Exists(GroupsFolder), $"{GroupsFolder} is missing.");

			var addressable = new HashSet<string>();
			foreach (string group in Directory.GetFiles(GroupsFolder, "*.asset"))
			{
				foreach (Match match in EntryGuid.Matches(File.ReadAllText(group)))
				{
					addressable.Add(match.Groups[1].Value);
				}
			}

			var missing = new List<string>();
			foreach (string asset in Directory.GetFiles(BiomesFolder, "*.asset", SearchOption.AllDirectories))
			{
				if (!File.ReadAllText(asset).Contains(BiomeTemplateScriptGuid))
				{
					continue;
				}
				string meta = asset + ".meta";
				Match guid = File.Exists(meta) ? MetaGuid.Match(File.ReadAllText(meta)) : Match.Empty;
				if (!guid.Success || !addressable.Contains(guid.Groups[1].Value))
				{
					missing.Add(Path.GetFileNameWithoutExtension(asset));
				}
			}
			missing.Sort(System.StringComparer.Ordinal);

			Assert.That(missing, Is.Empty,
				"These biomes have no Addressables entry, so a build never loads them. Run Maintenance → Apply biome envelopes and world requirements (it registers every unregistered biome), or the Addressables dashboard's smart grouping: " +
				string.Join(", ", missing));
		}
	}
}
