using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// How each biome's ground wears: the table that assigns the profiles, and the scene field that
	/// blends them for the shaping passes.
	/// </summary>
	[TestFixture]
	public class TerrainProcessTests
	{
		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void TearDown()
		{
			foreach (Object o in created)
			{
				Object.DestroyImmediate(o);
			}
			created.Clear();
		}

		private BiomeTemplate Biome(string name, TerrainProcess values)
		{
			var profile = ScriptableObject.CreateInstance<TerrainProcessProfile>();
			profile.name = name + " Process";
			profile.Values = values;
			created.Add(profile);
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			biome.name = name;
			biome.TerrainProcess = profile;
			created.Add(biome);
			return biome;
		}

		private static TerrainProcess ProfileValues(string name)
		{
			Assert.That(TerrainProcessTable.TryGetProfile(name, out TerrainProcessTable.Profile profile), Is.True, name);
			return profile.Values;
		}

		// ── The table ─────────────────────────────────────────────────

		[Test]
		public void EveryBiomeTheClimatePlacesHasAWayOfWearing()
		{
			/* A climate-selected biome missing from the table would silently wear like temperate soil,
			 * a desert eroding like a forest. The spec table is the list of what the climate places. */
			var assigned = new HashSet<string>();
			foreach ((string biome, string _) in TerrainProcessTable.Assignments)
			{
				Assert.That(assigned.Add(biome), Is.True, $"{biome} is assigned twice");
			}
			foreach (BiomeSpecTable.Entry entry in BiomeSpecTable.Entries)
			{
				Assert.That(assigned, Does.Contain(entry.Name), $"{entry.Name} is placed by the climate but has no terrain process");
			}
		}

		[Test]
		public void EveryAssignmentNamesARealProfileAndARealBiome()
		{
			foreach ((string biome, string profile) in TerrainProcessTable.Assignments)
			{
				Assert.That(TerrainProcessTable.TryGetProfile(profile, out _), Is.True, $"{biome} wears '{profile}', which the table does not define");
				string path = $"{BiomeSpecTable.Folder}/{biome}.asset";
				Assert.That(AssetDatabase.LoadAssetAtPath<BiomeTemplate>(path), Is.Not.Null, $"no biome asset at {path}");
			}
		}

		[Test]
		public void EveryProfileValueIsInsideTheRangeTheInspectorAllows()
		{
			/* The struct's [Range] is what a designer can set; a table value outside it would be
			 * clamped the first time anyone touched the slider, and the table would never match again. */
			foreach (TerrainProcessTable.Profile profile in TerrainProcessTable.Profiles)
			{
				object boxed = profile.Values;
				foreach (FieldInfo field in typeof(TerrainProcess).GetFields(BindingFlags.Public | BindingFlags.Instance))
				{
					UnityEngine.RangeAttribute range = field.GetCustomAttribute<UnityEngine.RangeAttribute>();
					Assert.That(range, Is.Not.Null, $"{field.Name} has no range");
					float value = (float)field.GetValue(boxed);
					Assert.That(value, Is.InRange(range.min, range.max), $"{profile.Name}.{field.Name}");
				}
			}
		}

		[Test]
		public void BadlandsWearFastestAndKarstKeepsItsHollows()
		{
			/* Two relations the profiles exist to express, pinned so a retune cannot invert them. */
			TerrainProcess temperate = ProfileValues("Temperate");
			TerrainProcess badlands = ProfileValues("Badlands");
			TerrainProcess karst = ProfileValues("Karst");
			Assert.That(temperate.SameAs(TerrainProcess.Temperate), Is.True, "the Temperate profile is the reference");
			Assert.That(badlands.Erodibility, Is.GreaterThan(temperate.Erodibility * 2f));
			Assert.That(badlands.ChannelThreshold, Is.LessThan(temperate.ChannelThreshold), "badlands drain through denser gullies");
			Assert.That(karst.DepressionKeeping, Is.EqualTo(1f));
			Assert.That(karst.ChannelThreshold, Is.GreaterThan(temperate.ChannelThreshold), "karst has few surface channels");
		}

		[Test]
		public void ABiomeWithNoProfileWearsLikeTemperateSoil()
		{
			var bare = ScriptableObject.CreateInstance<BiomeTemplate>();
			created.Add(bare);
			Assert.That(bare.ResolvedTerrainProcess.SameAs(TerrainProcess.Temperate), Is.True);
		}

		// ── The scene field ───────────────────────────────────────────

		private static SceneBiomeField HalfAndHalf(BiomeTemplate west, BiomeTemplate east, int cells, float cellMetres)
		{
			var indices = new byte[cells * cells];
			for (int z = 0; z < cells; z++)
			{
				for (int x = 0; x < cells; x++)
				{
					indices[z * cells + x] = (byte)(x < cells / 2 ? 0 : 1);
				}
			}
			float size = cells * cellMetres;
			return SceneBiomeField.FromCells(cells, cells, cellMetres, new Vector2(-size * 0.5f, -size * 0.5f),
				new[] { west, east }, indices);
		}

		[Test]
		public void TwoBiomesWearTheirOwnWayAndBlendBetween()
		{
			/* Arid on the west half, wetland on the east: each side reads its own profile, and the
			 * change across the line is spread over the blend, never a step a gully density would
			 * carve into the ground. */
			TerrainProcess arid = ProfileValues("Arid");
			TerrainProcess wet = ProfileValues("Wetland");
			const float CellMetres = 16f;
			const int Cells = 160; // 2560 m
			SceneBiomeField biomes = HalfAndHalf(Biome("Dry", arid), Biome("Wet", wet), Cells, CellMetres);
			float size = Cells * CellMetres;
			var footprint = new AtlasFootprint { Latitude = 10.0, Longitude = 20.0, SizeKm = new Vector2(size / 1000f, size / 1000f) };
			SceneTerrainProcess field = SceneTerrainProcess.Build(biomes, null, size, size, footprint, 30.0, (d, lat) => 0.5f);

			Assert.That(field.ProcessAt(-1200f, 0f).Erodibility, Is.EqualTo(arid.Erodibility).Within(1e-3f), "far west is arid");
			Assert.That(field.ProcessAt(1200f, 0f).Erodibility, Is.EqualTo(wet.Erodibility).Within(1e-3f), "far east is wetland");
			float middle = field.ProcessAt(0f, 0f).Erodibility;
			Assert.That(middle, Is.InRange(Mathf.Min(arid.Erodibility, wet.Erodibility) + 0.1f, Mathf.Max(arid.Erodibility, wet.Erodibility) - 0.1f),
				"the boundary is a blend of both");

			float steepest = 0f;
			for (float east = -600f; east < 600f; east += 8f)
			{
				float step = Mathf.Abs(field.ProcessAt(east + 8f, 0f).Erodibility - field.ProcessAt(east, 0f).Erodibility);
				steepest = Mathf.Max(steepest, step);
			}
			float jump = Mathf.Abs(arid.Erodibility - wet.Erodibility);
			Assert.That(steepest, Is.LessThan(jump * 0.05f), "no 8 m of ground should carry more than a twentieth of the change");
		}

		[Test]
		public void TheRainIsTheClimatesAtThatPlace()
		{
			/* Rain that rises eastward across the scene: each point reads the rain of its own place on
			 * the globe, so a rain shadow crossing a scene erodes one side harder than the other. */
			BiomeTemplate grass = Biome("Grass", TerrainProcess.Temperate);
			SceneBiomeField biomes = HalfAndHalf(grass, grass, 32, 32f);
			var footprint = new AtlasFootprint { Latitude = 0.0, Longitude = 0.0, SizeKm = new Vector2(1.024f, 1.024f) };
			// The scene's own east on the globe, so the rain rises eastward whichever axis that is.
			Vector3 eastward = (AtlasGeometry.SceneToUnit(footprint, 1.0, 0.0, 30.0) - AtlasGeometry.SceneToUnit(footprint, -1.0, 0.0, 30.0)).ToVector3().normalized;
			Vector3 middle = AtlasGeometry.SceneToUnit(footprint, 0.0, 0.0, 30.0).ToVector3().normalized;
			float Rain(Vector3 direction) => Mathf.Clamp01(0.5f + Vector3.Dot(direction - middle, eastward) * 20f);
			SceneTerrainProcess field = SceneTerrainProcess.Build(biomes, null, 1024f, 1024f, footprint, 30.0, (direction, latitude) => Rain(direction));

			float west = field.RainAt(-500f, 0f), centre = field.RainAt(0f, 0f), east = field.RainAt(500f, 0f);
			Assert.That(west, Is.LessThan(centre));
			Assert.That(centre, Is.LessThan(east));
			Assert.That(centre, Is.EqualTo(0.5f).Within(1e-3f));
		}
	}
}
