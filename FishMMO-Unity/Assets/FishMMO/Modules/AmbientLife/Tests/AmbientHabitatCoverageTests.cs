using System.Linq;
using FishMMO.Client;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Every biome gives the Ambient Life module a habitat (split from BiomeContentCoverageTests; read
	/// from the biome templates). That every biome's spec names its country is the World Design module's test.
	/// </summary>
	[TestFixture]
	public class AmbientHabitatCoverageTests
	{
		[Test]
		public void EveryBiome_HasAnAmbientHabitat()
		{
			// The name reading a template never authored still falls back on. Alien ground alone names None (the Alien bit is
			// read live from the template's air): AmbientBiomeTemplateTests checks the live reading is never None.
			string[] missing = AmbientBiomeAssets.All().Select(b => b.name).Where(b => AmbientGating.HabitatOf(b) == AmbientHabitat.None).ToArray();
			Assert.IsEmpty(missing, "no keyword matches these biomes' names, so their animals fall back on the climate: " + string.Join(", ", missing));
		}
	}
}
