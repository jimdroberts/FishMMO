using FishMMO.Client;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using FishMMO.Shared;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A cave spot's habitat for the ambient cave life (split from BiomeCaveInteriorTests, Phase 4, 2026-11-17).
	/// </summary>
	[TestFixture]
	public class AmbientCaveHabitatTests
	{
		private const string WorldFolder = "Assets/FishMMO/Modules/WorldDesign/Editor";

		private static string Code(string path) => ProjectFiles.ReadAllText(path);

		[Test]
		public void ALinkedSpotsHabitat_IsTheInteriors_ButTheLandKeepsItsBarrenBit()
		{
			const int barren = (int)AmbientHabitat.Barren, cave = (int)AmbientHabitat.Cave, ice = (int)AmbientHabitat.Ice, volcanic = (int)AmbientHabitat.Volcanic;
			Assert.That(BiomeCaveInteriors.LinkedHabitat(volcanic, barren, barren), Is.EqualTo(0), "the Lava Tube's own Barren is its lava fields'");
			Assert.That(BiomeCaveInteriors.LinkedHabitat(ice | barren, cave, barren), Is.EqualTo(cave | barren), "barren land over it stays barren");
			Assert.That(BiomeCaveInteriors.LinkedHabitat(volcanic, cave | ice, barren), Is.EqualTo(cave | ice));
			// A spot of no interior keeps the land's (every spot baked before 2026-10-25).
			var spot = new SceneCaveLifeSpot { Kind = SceneCaveLifeKind.Floor };
			Assert.That(AmbientCaveLife.SpotHabitat(in spot, AmbientHabitat.Volcanic | AmbientHabitat.Barren), Is.EqualTo(AmbientHabitat.Volcanic | AmbientHabitat.Barren));
			spot.Biome = 123456789;
			Assert.That(AmbientCaveLife.SpotHabitat(in spot, AmbientHabitat.Forest), Is.EqualTo(AmbientHabitat.Forest), "an id this client does not hold");
			if (InstalledModules.Has("WorldDesign"))
			{
				string life = Code(WorldFolder + "/PointsOfInterest/Features/PointOfInterestCaveDressing.Life.cs");
				Assert.That(life, Does.Contain("int inside = caves.Linked ? BiomeRegistry.IDOf(caves.Interior) : 0;"));
			}
			Assert.That(Code("Assets/FishMMO/Modules/AmbientLife/Client/AmbientLife/AmbientCaveLife.cs"), Does.Contain("habitat = SpotHabitat(in spot, habitat);"));
		}
	}
}
