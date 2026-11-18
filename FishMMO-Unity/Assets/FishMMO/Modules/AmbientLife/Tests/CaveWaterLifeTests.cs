using FishMMO.Client;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using FishMMO.Shared;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The drowned galleries' water life: leeches and beads (split from BiomeCaveDrownedGalleriesTests, Phase 4, 2026-11-17).
	/// </summary>
	[TestFixture]
	public class CaveWaterLifeTests
	{
		private const string World = "Assets/FishMMO/Modules/WorldDesign/Editor/";
		private const string Shaders = "Assets/FishMMO/Modules/AmbientLife/Shaders/";

		private static string Code(string path) => ProjectFiles.ReadAllText(path);

		private static SceneCaveLifeSpot Spot(SceneCaveLifeKind kind, Vector3 at, Vector3 extent, int site, int biome)
			=> new SceneCaveLifeSpot { Kind = kind, Position = at, Extent = extent, SiteId = site, Biome = biome, Normal = Vector3.up };

		[Test]
		public void TheLeeches_CircleOnTheClock_AndTurnOnlyByTheViewersPull()
		{
			var surface = new Vector3(10f, -20f, 5f);
			for (int leech = 0; leech < SceneCaveWater.MaxLeeches; leech++)
			{
				uint seed = SceneCaveWater.SeedOf(surface);
				Vector3 a = SceneCaveWater.LeechHome(0f, seed, leech, surface, 2.5f, 1.2f);
				Vector3 b = SceneCaveWater.LeechHome(SceneCaveWater.Wrap, seed, leech, surface, 2.5f, 1.2f);
				Assert.That((a - b).magnitude, Is.LessThan(2e-3f), "a whole number of turns in the wrap");
				Assert.That(new Vector2(a.x - surface.x, a.z - surface.z).magnitude, Is.LessThanOrEqualTo(2.5f * 0.85f + 1e-3f), "inside its pool");
				Assert.That(a.y, Is.LessThan(surface.y).And.GreaterThan(surface.y - 1.2f - 0.05f), "in the water");
				// Moving slowly: under 0.2 m/s on its circuit.
				Vector3 c = SceneCaveWater.LeechHome(1f, seed, leech, surface, 2.5f, 1.2f);
				Assert.That((c - a).magnitude, Is.LessThan(0.2f));
				var feet = new Vector3(11f, -20.8f, 5.5f);
				Assert.That(SceneCaveWater.LeechAt(a, feet, 0f, seed, leech, surface, 1.2f), Is.EqualTo(a), "no pull, its home: what every viewer sees");
				Vector3 at = SceneCaveWater.LeechAt(a, feet, 1f, seed, leech, surface, 1.2f);
				Assert.That(new Vector2(at.x - feet.x, at.z - feet.z).magnitude, Is.LessThanOrEqualTo(0.3f), "at the wader's legs");
				Assert.That(at.y, Is.LessThan(surface.y).And.GreaterThan(surface.y - 1.2f), "still in the water");
			}
			// The pull: in over its seconds while someone wades, out after; a viewer's own (nothing else reads it).
			float pull = 0f;
			for (int f = 0; f < 60 * (int)SceneCaveWater.AttractSeconds + 2; f++)
			{
				pull = SceneCaveWater.Attract(pull, true, 1f / 60f);
			}
			Assert.That(pull, Is.EqualTo(1f));
			for (int f = 0; f < 60 * (int)SceneCaveWater.ReleaseSeconds + 2; f++)
			{
				pull = SceneCaveWater.Attract(pull, false, 1f / 60f);
			}
			Assert.That(pull, Is.EqualTo(0f));
			Assert.That(SceneCaveWater.Wades(new Vector3(11f, -20.5f, 5f), surface, 2.5f, 1.2f), Is.True);
			Assert.That(SceneCaveWater.Wades(new Vector3(11f, -19.9f, 5f), surface, 2.5f, 1.2f), Is.False, "standing over the water");
			Assert.That(SceneCaveWater.Wades(new Vector3(20f, -20.5f, 5f), surface, 2.5f, 1.2f), Is.False, "out of the pool");
			// A pool is drawn only where its cave biome names beads or leeches for its form.
			SceneCaveLifeSpot pool = Spot(SceneCaveLifeKind.Pool, surface, new Vector3(2.5f, 1.2f, 0f), 7, 42);
			Assert.That(CaveWaterLife.TryPool(in pool, null, out _), Is.False);
			Assert.That(CaveWaterLife.TryPool(in pool, new[] { new BiomeCaveFlood(BiomeCaveForm.Cave, 1.2f, 0.6f) }, out _), Is.False, "a flood with neither");
			Assert.That(CaveWaterLife.TryPool(in pool, new[] { new BiomeCaveFlood(BiomeCaveForm.Grotto, 1.5f, 1f, 1f, 1f) }, out _), Is.False, "another form's");
			Assert.That(CaveWaterLife.TryPool(in pool, new[] { new BiomeCaveFlood(BiomeCaveForm.Cave, 1.2f, 0.6f, 0.8f, 1f) }, out CaveWaterLife.Pool drawn), Is.True);
			Assert.That(drawn.Vents > 0 && drawn.Leeches > 0 && drawn.Seed == SceneCaveWater.SeedOf(surface), Is.True);
			SceneCaveLifeSpot floor = Spot(SceneCaveLifeKind.Floor, surface, new Vector3(2.5f, 1.2f, 0f), 7, 42);
			Assert.That(CaveWaterLife.TryPool(in floor, new[] { new BiomeCaveFlood(BiomeCaveForm.Cave, 1.2f, 0.6f, 0.8f, 1f) }, out _), Is.False, "pools only");
		}

		[Test]
		public void TheWaterLifeShader_MirrorsTheRules_OnDxc()
		{
			string shader = Code(Shaders + "FishCaveWaterLife.shader");
			Assert.That(System.Text.RegularExpressions.Regex.Matches(shader, "HLSLPROGRAM").Count,
				Is.EqualTo(System.Text.RegularExpressions.Regex.Matches(shader, "#pragma use_dxc vulkan").Count), "every pass on DXC");
			StringAssert.Contains($"#define WATER_POOLS {CaveWaterLife.MaxPools}", shader);
			StringAssert.Contains($"#define WATER_VENTS {SceneCaveWater.MaxVents}", shader);
			StringAssert.Contains($"#define WATER_BEADS {SceneCaveWater.BeadsPerBurst}", shader);
			StringAssert.Contains($"#define WATER_LEECHES {SceneCaveWater.MaxLeeches}", shader);
			StringAssert.Contains("#define WATER_BEAD_SPACING 0.35", shader);
			StringAssert.Contains("#define WATER_BEAD_SPEED 0.24", shader);
			StringAssert.Contains("#define WATER_RING_SECONDS 1.2", shader);
			StringAssert.Contains("#define WATER_MOST_DEPTH 2.0", shader);
			Assert.That(SceneCaveWater.BeadSpacing == 0.35f && SceneCaveWater.BeadSpeed == 0.24f && SceneCaveWater.RingSeconds == 1.2f && SceneCaveWater.MostDepth == 2f, Is.True);
			foreach (string c in new[] { "0x7FEB352Du", "0x846CA68Bu", "0x9E3779B1u", "0x632BE5ABu", "vent * 64u + 7u", "leech + 101u, 11u", "leech + 101u, 12u" })
			{
				StringAssert.Contains(c, shader);
			}
			foreach (float period in SceneCaveWater.Periods)
			{
				StringAssert.Contains(period.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(".") ? period.ToString(System.Globalization.CultureInfo.InvariantCulture) : period.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".0", shader);
			}
			Assert.That(shader, Does.Not.Contain("static const float"), "no constant arrays (indexed by a dynamic index)");
			// The seed in two halves; a build includes the shader through the render profile.
			StringAssert.Contains("seeds[i] = new Vector4(pool.Seed >> 16, pool.Seed & 0xFFFFu, pool.Bubbles, 0f);", Code("Assets/FishMMO/Modules/AmbientLife/Client/SiteLife/CaveWaterLife.cs"));
			StringAssert.Contains("uint seed = ((uint)round(seeds.x) << 16) | (uint)round(seeds.y);", shader);
			StringAssert.Contains("public Shader CaveWaterLifeShader;", Code("Assets/FishMMO/Modules/AmbientLife/Client/AmbientLife/AmbientLifeRenderProfile.cs"));
			StringAssert.Contains("ambient.CaveWaterLifeShader = AssetDatabase.LoadAssetAtPath<Shader>(CaveWaterLifeShaderPath);", Code("Assets/FishMMO/Modules/AmbientLife/Editor/AmbientLifeRenderAssets.cs"));
		}
	}
}
