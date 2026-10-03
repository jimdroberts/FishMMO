using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Every weather substance is addressable, so a running game knows what each world rains.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>WeatherPhysics.PrecipitateOf</c> finds a world's condensate (methane rain, nitrogen snow, sulphuric
	/// acid virga) by searching the cached-object cache, and only the Addressables loader puts a substance
	/// there. A biome's <c>Emits</c> reference brings its own substance in directly, so emission works
	/// either way — but a substance nothing references is never cached, and a world condensing it falls
	/// back to the water typing: methane drawn as rain streaks that wet the ground. Every substance
	/// in the project had no entry on 2026-10-02.
	/// </para>
	/// <para>
	/// The same failure as <see cref="BiomeAddressablesTests"/>, and the same cause: assets made by a
	/// tool are only registered when somebody opens them in the inspector. Read from the files, not the
	/// API, so it needs no Addressables settings loaded.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class WeatherSubstanceAddressablesTests
	{
		private const string SubstancesFolder = "Assets/Templates/Weather/Substances";
		private const string GroupsFolder = "Assets/AddressableAssetsData/AssetGroups";
		/// <summary>The WeatherSubstance script's GUID, so other asset types in the folder are ignored.</summary>
		private const string WeatherSubstanceScriptGuid = "fc31fd6c26d6fbb81b415e9bc031c46f";

		private static readonly Regex MetaGuid = new Regex(@"^guid: (\w+)", RegexOptions.Multiline);
		private static readonly Regex EntryGuid = new Regex(@"m_GUID: (\w+)");

		[Test]
		public void EveryWeatherSubstanceHasAnAddressableEntry()
		{
			Assume.That(Directory.Exists(SubstancesFolder), $"{SubstancesFolder} is missing.");
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
			foreach (string asset in Directory.GetFiles(SubstancesFolder, "*.asset", SearchOption.AllDirectories))
			{
				if (!File.ReadAllText(asset).Contains(WeatherSubstanceScriptGuid))
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
				"These weather substances have no Addressables entry, so a running game never caches them and a world condensing one falls back to water. Run the Addressables dashboard's smart grouping: " +
				string.Join(", ", missing));
		}
	}
}
