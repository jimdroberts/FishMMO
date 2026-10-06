using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Client;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The sea's background creatures (SeaLifePlacement, SeaCreatureMeshes) and the sea globals the
	/// sea-floor plants read: the same sea for every player, never in the shallows, never out of the water.
	/// </summary>
	[TestFixture]
	public class SeaLifeTests
	{
		private const float Mean = 0f;
		private const uint Seed = 0xC0FFEEu;

		/// <summary>A sea floor 30 m down, an island rising out of it round (200, 200), nothing past ±1000 m.</summary>
		private sealed class FakeGround : ISeaGround
		{
			public string Biome;
			public float Temperature = 0.5f;
			public int Reads;

			public bool TryGround(float x, float z, out float y)
			{
				Reads++;
				if (Mathf.Abs(x) > 1000f || Mathf.Abs(z) > 1000f)
				{
					y = float.PositiveInfinity;
					return true;
				}
				float island = 45f * Mathf.Exp(-((x - 200f) * (x - 200f) + (z - 200f) * (z - 200f)) / (2f * 60f * 60f));
				y = -30f + island + 2f * Mathf.Sin(x * 0.05f) * Mathf.Cos(z * 0.04f);
				return true;
			}

			public void Habitat(Vector3 position, out string biome, out float temperature)
			{
				biome = Biome;
				temperature = Temperature;
			}
		}

		private static List<SeaGroup> PlaceArea(SeaLifeSettings settings, ISeaGround ground, bool reverse)
		{
			var groups = new List<SeaGroup>();
			for (int i = 0; i < 64; i++)
			{
				int n = reverse ? 63 - i : i;
				int cx = n % 8 - 4, cz = n / 8 - 4;
				for (int k = 0; k < settings.Kinds.Length; k++)
				{
					Assert.That(SeaLifePlacement.TryPlace(settings.Kinds[k], k, Seed, cx, cz, settings.CellMetres, Mean, ground, groups), Is.True);
				}
			}
			groups.Sort((a, b) => a.Seed.CompareTo(b.Seed));
			return groups;
		}

		[Test]
		public void Placement_IsTheSameWhicheverOrderTheCellsAreSeenIn()
		{
			var settings = new SeaLifeSettings();
			List<SeaGroup> a = PlaceArea(settings, new FakeGround(), false);
			List<SeaGroup> b = PlaceArea(settings, new FakeGround(), true);
			Assert.That(a.Count, Is.GreaterThan(5), "the area should hold some life");
			Assert.That(b.Count, Is.EqualTo(a.Count));
			for (int i = 0; i < a.Count; i++)
			{
				Assert.That(b[i].Seed, Is.EqualTo(a[i].Seed));
				Assert.That(b[i].Home, Is.EqualTo(a[i].Home));
				Assert.That(b[i].Count, Is.EqualTo(a[i].Count));
				Assert.That(b[i].Period, Is.EqualTo(a[i].Period));
			}
			// Another scene, another sea.
			var other = new List<SeaGroup>();
			for (int k = 0; k < settings.Kinds.Length; k++)
			{
				SeaLifePlacement.TryPlace(settings.Kinds[k], k, Seed ^ 0x1234u, 0, 0, settings.CellMetres, Mean, new FakeGround(), other);
			}
			Assert.That(other.Exists(g => a.Exists(h => h.Home == g.Home)), Is.False, "a different scene should place differently");
		}

		[Test]
		public void NoGroupSwimsIntoTheShallows_OrPastTheScenesEdge()
		{
			var settings = new SeaLifeSettings();
			var ground = new FakeGround();
			List<SeaGroup> groups = PlaceArea(settings, ground, false);
			foreach (SeaGroup g in groups)
			{
				SeaCreatureKind kind = settings.Kinds[g.Kind];
				for (int k = 0; k < 48; k++)
				{
					Vector3 at = SeaLifePlacement.LoopPoint(g, k / 48f);
					Assert.That(ground.TryGround(at.x, at.z, out float floor), Is.True);
					// Checked at twelve points when placed; between them a gentle bed changes little.
					Assert.That(Mean - floor, Is.GreaterThan(kind.MinWater * 0.6f), $"{kind.Name} at {at} swims over {Mean - floor:0.0} m of water");
				}
			}
		}

		[Test]
		public void EveryAnimalStaysUnderTheSurface_AndOffTheFloor()
		{
			var settings = new SeaLifeSettings();
			var ground = new FakeGround();
			List<SeaGroup> groups = PlaceArea(settings, ground, false);
			var instances = new List<SeaInstance>();
			int members = 0, drawn = 0;
			foreach (double seconds in new[] { 0.0, 37.5, 1234.25, 3.1e9 })
			{
				foreach (SeaGroup g in groups)
				{
					SeaCreatureKind kind = settings.Kinds[g.Kind];
					instances.Clear();
					SeaLifePlacement.Evaluate(g, kind, seconds, Mean, ground, 1f, instances);
					// One whose water is too thin for it at that moment is left out, never drawn out of the sea.
					Assert.That(instances.Count, Is.LessThanOrEqualTo(g.Count));
					members += g.Count;
					drawn += instances.Count;
					foreach (SeaInstance s in instances)
					{
						Vector3 p = s.Matrix.GetColumn(3);
						Assert.That(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z), Is.False, kind.Name);
						Assert.That(p.y, Is.LessThanOrEqualTo(Mean - SeaLifePlacement.SurfaceClearance + 1e-3f), $"{kind.Name} breaks the surface at {seconds} s");
						ground.TryGround(p.x, p.z, out float floor);
						Assert.That(p.y, Is.GreaterThanOrEqualTo(floor - 0.05f), $"{kind.Name} is under the sea floor at {seconds} s");
						Assert.That(s.Anim.x, Is.InRange(0f, 1f));
					}
				}
			}
			Assert.That(drawn, Is.GreaterThan(members * 0.8f), "most of the sea's life should have room to swim");
		}

		[Test]
		public void WhereAnAnimalIs_IsAFunctionOfTheClock_SmoothEvenAtLargeWorldTimes()
		{
			var settings = new SeaLifeSettings();
			var ground = new FakeGround();
			List<SeaGroup> groups = PlaceArea(settings, ground, false);
			Assume.That(groups.Count, Is.GreaterThan(0));
			var a = new List<SeaInstance>();
			var b = new List<SeaInstance>();
			const double late = 3.1e9; // a century of world seconds
			foreach (SeaGroup g in groups)
			{
				SeaCreatureKind kind = settings.Kinds[g.Kind];
				a.Clear();
				b.Clear();
				SeaLifePlacement.Evaluate(g, kind, late, Mean, ground, 1f, a);
				SeaLifePlacement.Evaluate(g, kind, late, Mean, ground, 1f, b);
				for (int i = 0; i < a.Count; i++)
				{
					Assert.That(b[i].Matrix, Is.EqualTo(a[i].Matrix), "the same moment must place every animal the same, for every player");
				}
				b.Clear();
				SeaLifePlacement.Evaluate(g, kind, late + 0.05, Mean, ground, 1f, b);
				for (int i = 0; i < a.Count; i++)
				{
					float moved = Vector3.Distance(a[i].Matrix.GetColumn(3), b[i].Matrix.GetColumn(3));
					// A twentieth of a second at its speed, a school's wobble and a reef fish's dart: never a jump.
					Assert.That(moved, Is.LessThan(kind.Speed * 0.05f * 4f + 0.25f), $"{kind.Name} jumped {moved:0.00} m in 0.05 s");
				}
			}
			// The centre of the loop comes back to where it was a lap later.
			SeaGroup first = groups[0];
			float lap0 = SeaLifePlacement.Lap(first, late);
			float lap1 = SeaLifePlacement.Lap(first, late + first.Period);
			Assert.That(Mathf.Abs(lap0 - lap1) < 1e-3f || Mathf.Abs(Mathf.Abs(lap0 - lap1) - 1f) < 1e-3f, Is.True);
		}

		[Test]
		public void Habitat_PreferredBiomesAreHome_TheThermometerGatesTheRest()
		{
			SeaCreatureKind reef = System.Array.Find(SeaLifeSettings.Defaults(), k => k.Name == "Reef fish");
			Assert.That(reef, Is.Not.Null);
			Assert.That(SeaLifePlacement.HabitatWeight(reef, "Coral Reef", -0.8f), Is.EqualTo(reef.PreferWeight), "a reef is home whatever the reading");
			Assert.That(SeaLifePlacement.HabitatWeight(reef, "Ocean", -0.8f), Is.EqualTo(0f), "no reef fish in cold open water");
			Assert.That(SeaLifePlacement.HabitatWeight(reef, "Ocean", 0.8f), Is.EqualTo(reef.ElsewhereWeight).Within(1e-5f));
			SeaCreatureKind bait = System.Array.Find(SeaLifeSettings.Defaults(), k => k.Name == "Baitfish");
			Assert.That(SeaLifePlacement.HabitatWeight(bait, null, -1f), Is.EqualTo(1f), "an open temperature band has no edge");
		}

		[Test]
		public void ADiverSeesLife_ThroughTheWatersOwnVisibility()
		{
			// The sea is seen through about 22 m of water (WaterSurface.UnderwaterVisibility): the groups expected in that
			// disc of ordinary temperate coastal water (no preferred biome), across every kind, must be more than one.
			const float visibility = 22f;
			float disc = Mathf.PI * visibility * visibility * 1e-6f;
			float expected = 0f;
			foreach (SeaCreatureKind kind in SeaLifeSettings.Defaults())
			{
				expected += kind.GroupsPerKm2 * disc * SeaLifePlacement.HabitatWeight(kind, null, 0.3f);
			}
			Assert.That(expected, Is.GreaterThan(2f), "fish were 'way too rare' at a fifth of a school in view");
			Assert.That(new SeaLifeSettings().DrawDistance, Is.LessThanOrEqualTo(4f * visibility), "drawn no further than the water lets anyone see, near enough");
		}

		[Test]
		public void OldDefaults_AreUpgraded_AndHandSetNumbersAreLeftAlone()
		{
			var settings = new SeaLifeSettings { Version = 1, DrawDistance = 140f };
			SeaCreatureKind bait = System.Array.Find(settings.Kinds, k => k.Name == "Baitfish");
			SeaCreatureKind shark = System.Array.Find(settings.Kinds, k => k.Name == "Shark");
			bait.GroupsPerKm2 = 150f;   // the old default
			shark.GroupsPerKm2 = 7f;    // somebody's own number
			settings.Upgrade();
			Assert.That(settings.Version, Is.EqualTo(SeaLifeSettings.CurrentVersion));
			Assert.That(settings.DrawDistance, Is.EqualTo(70f));
			Assert.That(bait.GroupsPerKm2, Is.EqualTo(600f));
			Assert.That(shark.GroupsPerKm2, Is.EqualTo(7f));
			bait.GroupsPerKm2 = 150f;
			settings.Upgrade();
			Assert.That(bait.GroupsPerKm2, Is.EqualTo(150f), "once only");
		}

		[Test]
		public void TheDefaultSea_HasAtLeastEightKinds_InSevenShapes()
		{
			SeaCreatureKind[] kinds = SeaLifeSettings.Defaults();
			Assert.That(kinds.Length, Is.GreaterThanOrEqualTo(8));
			var shapes = new HashSet<SeaCreatureShape>();
			foreach (SeaCreatureKind k in kinds)
			{
				shapes.Add(k.Shape);
				Assert.That(k.Depth.x, Is.LessThan(k.Depth.y), k.Name);
				Assert.That(k.Length.x, Is.LessThanOrEqualTo(k.Length.y), k.Name);
			}
			Assert.That(shapes.Count, Is.GreaterThanOrEqualTo(7));
		}

		[Test]
		public void EveryCreatureMesh_IsSmall_AndCarriesTheShadersChannels()
		{
			foreach (SeaCreatureKind kind in SeaLifeSettings.Defaults())
			{
				Mesh mesh = SeaCreatureMeshes.Build(kind);
				try
				{
					Assert.That(mesh.vertexCount, Is.InRange(20, 1500), kind.Name);
					Assert.That(mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color), Is.True, kind.Name);
					Assert.That(mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1), Is.True, kind.Name);
					Vector3 size = mesh.bounds.size;
					Assert.That(Mathf.Max(size.x, size.z), Is.InRange(1f, 4f), $"{kind.Name} should be about one unit long (with room for its stroke)");
				}
				finally
				{
					Object.DestroyImmediate(mesh);
				}
			}
		}

		[Test]
		public void TheSeaClock_FoldsSeamlessly()
		{
			// Every frequency in FishSea.hlsl is a whole number of cycles in the 256 s fold, or the plants jump each fold.
			string source = File.ReadAllText("Assets/Prefabs/Client/Weather/Shaders/FishSea.hlsl");
			MatchCollection cycles = Regex.Matches(source, @"\bt \* ([0-9]+(?:\.[0-9]+)?)");
			Assert.That(cycles.Count, Is.GreaterThanOrEqualTo(4));
			foreach (Match m in cycles)
			{
				float n = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
				Assert.That(n, Is.EqualTo(Mathf.Round(n)), $"'{m.Value}' is not a whole number of cycles per fold");
			}
			StringAssert.Contains("#define FISH_SEA_PERIOD 256.0", source);
		}

		[Test]
		public void TheProfileAsset_CarriesTheSeaLifeShader_AndNoSeaPlantIsGrass()
		{
			// Code defaults do not reach a saved profile: the shader must be in the asset itself.
			var profile = AssetDatabase.LoadAssetAtPath<WeatherRenderProfile>(WeatherRenderAssets.ProfilePath);
			Assume.That(profile, Is.Not.Null);
			// Seagrass and seaweed drawn as blades read as a lawn and dead grass on the sea floor: both were removed.
			Assert.That(profile.Grass.IsBladePrototype("Detail_Seagrass"), Is.False);
			Assert.That(profile.Grass.IsBladePrototype("Detail_Seaweed"), Is.False);
			Assert.That(profile.SeaLifeShader, Is.Not.Null, "FishMMO/Sea Life must be referenced so a client build includes it");
			Assert.That(profile.SeaLifeShader.name, Is.EqualTo("FishMMO/Sea Life"));
			Assert.That(profile.SeaLife, Is.Not.Null);
			Assert.That(profile.SeaLife.Kinds.Length, Is.GreaterThanOrEqualTo(8));
		}
	}
}
