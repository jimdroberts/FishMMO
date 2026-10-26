using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2s of the biome suite (2026-10-25, decision 68 (a), 34): the open ice shelf. Where the ice sheet meets a sea colder
	/// than −5 °C mean, the ice runs out 2–8 km over it at 12–35 m freeboard to a vertical calving front, then fast ice at
	/// +0.6 m to a floe edge; the new water role IceShelf (= 7) makes the role's holder the biome of those cells. Opt-in: no
	/// name stands in for the role and no spec biome holds it, so no scene changes until a template does. Pure.
	/// </summary>
	public class IceShelfReachTests
	{
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";

		private const float Cell = 128f;
		private const int Columns = 120;
		private const int Rows = 40;
		private const int LandColumns = 10;
		private const float LandIce = 40f;
		private const float LandGround = 5f + LandIce;
		private const float SeaFloor = -60f;
		private const uint Seed = 7u;

		/// <summary>The coast's east edge, metres: land in the first <see cref="LandColumns"/> columns, sea beyond.</summary>
		private const float CoastEast = LandColumns * Cell;

		private static string Read(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative)).Replace("\r\n", "\n");

		/// <summary>A straight north–south coast: ice-sheet land to the west, sea to the east, cold where <paramref name="cold"/> says.</summary>
		private static IceShelfReach Coast(Func<int, int, bool> cold, float landIce = LandIce, uint seed = Seed, Func<int, int, bool> land = null,
			float landRaw = 5f)
		{
			int count = Columns * Rows;
			var ground = new float[count];
			var ice = new float[count];
			var coldSea = new bool[count];
			for (int z = 0; z < Rows; z++)
			{
				for (int x = 0; x < Columns; x++)
				{
					int i = z * Columns + x;
					if (x < LandColumns)
					{
						ice[i] = landIce;
						ground[i] = landRaw + landIce;
					}
					else if (land != null && land(x, z))
					{
						ground[i] = 30f;
					}
					else
					{
						ground[i] = SeaFloor;
						coldSea[i] = cold(x, z);
					}
				}
			}
			return IceShelfReach.Build(Columns, Rows, Cell, new Vector2(0f, -Rows * Cell * 0.5f), ground, ice, coldSea, seed);
		}

		private static float GroundBefore(float east) => east < CoastEast ? LandGround : SeaFloor;

		// ── The role ──────────────────────────────────────────────────

		[Test]
		public void Role_IsAppendedAsSeven_AndOptIn()
		{
			Assert.That((int)BiomeWaterRole.IceShelf, Is.EqualTo(7));
			var values = (BiomeWaterRole[])Enum.GetValues(typeof(BiomeWaterRole));
			for (int i = 0; i < values.Length; i++)
			{
				Assert.That((int)values[i], Is.EqualTo(i), "append only: " + values[i]);
			}
			Assert.That(BiomeTemplateArt.LegacyWaterBiomeName(BiomeWaterRole.IceShelf), Is.Null, "no name stands in: nothing is laid until a template holds it");
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.That(entry.WaterRole, Is.Not.EqualTo(BiomeWaterRole.IceShelf), entry.Biome + " holds it: the change is opt-in (see I2s)");
			}
			Assert.That(BiomeArtSpec.For("Ice Shelf").WaterRole, Is.EqualTo(BiomeWaterRole.None));
			Assert.That(BiomeWaterLooks.IsWaterBody(BiomeWaterRole.IceShelf), Is.False, "the shelf is ground, not a body of water");
			Assert.That(PathCrossings.IsWaterBiome(BiomeWaterRole.IceShelf), Is.False, "ways cross it as ground");
		}

		[Test]
		public void BiomeOf_ShelfAndFastIce_TakeTheRolesHolder()
		{
			Assert.That(IceShelfReach.BiomeOf(IceShelfPart.None, "Tundra", "Ice Shelf"), Is.EqualTo("Tundra"));
			Assert.That(IceShelfReach.BiomeOf(IceShelfPart.Shelf, "Ice Sheet", "Ice Shelf"), Is.EqualTo("Ice Shelf"));
			Assert.That(IceShelfReach.BiomeOf(IceShelfPart.Apron, "Ocean", "Ice Shelf"), Is.EqualTo("Ice Shelf"));
			Assert.That(IceShelfReach.BiomeOf<string>(IceShelfPart.Shelf, "Ocean", null), Is.EqualTo("Ocean"), "no holder: the climate's pick");
		}

		[Test]
		public void Wiring_FieldOverridesBeforeTheScenesOwnSay_AndTheGroundIsUnchangedWithoutAReach()
		{
			string field = Read(World + "/Terrain/SceneBiomeField.cs");
			int role = field.IndexOf("BiomeTemplateArt.WaterBiome(BiomeWaterRole.IceShelf)", StringComparison.Ordinal);
			int pick = field.IndexOf("biome = IceShelfReach.BiomeOf(shelf.IceShelfPartAt(east, north), biome, shelfBiome);", StringComparison.Ordinal);
			int overrides = field.IndexOf("biome = overrides.Apply(", StringComparison.Ordinal);
			Assert.That(role, Is.GreaterThan(0));
			Assert.That(pick, Is.GreaterThan(role));
			Assert.That(pick, Is.LessThan(overrides), "a role biome, so the scene's own say keeps it as it keeps the lakes");

			string generation = Read(World + "/SceneGeneration.cs");
			StringAssert.Contains("return iceShelf != null ? iceShelf.SurfaceAt(eastMetres, northMetres, altitude) : altitude;", generation);
			StringAssert.Contains("iceShelf = IceShelfReach.For(request, this);", generation);

			string reach = Read(World + "/Terrain/IceShelfReach.cs");
			int optIn = reach.IndexOf("if (BiomeTemplateArt.WaterBiome(BiomeWaterRole.IceShelf) == null)", StringComparison.Ordinal);
			Assert.That(optIn, Is.GreaterThan(0));
			Assert.That(optIn, Is.LessThan(reach.IndexOf("Parallel.For(", StringComparison.Ordinal)), "nothing is sampled before the opt-in");
			StringAssert.Contains("request.FrozenSeas", reach);
			StringAssert.Contains("<= IceOccurrence.IceShelfLimitC", reach);
		}

		[Test]
		public void SceneAltitude_WithNoHolder_LaysNoShelf_AndIsTheGroundBeforeIt()
		{
			var request = new SceneGenerationRequest { SceneName = "Shelf Test", SizeKm = new Vector2(2f, 2f), FineDetail = false };
			var altitude = new SceneAltitude(request);
			Assert.That(altitude.IceShelf, Is.Null);
			for (int i = 0; i < 64; i++)
			{
				float east = -900f + 31f * i, north = 700f - 23f * i;
				Assert.That(altitude.At(east, north), Is.EqualTo(altitude.BeforeShelf(east, north, out _, out _)));
				Assert.That(altitude.IceShelfPartAt(east, north), Is.EqualTo(IceShelfPart.None));
			}
		}

		// ── The reach ─────────────────────────────────────────────────

		[Test]
		public void NoColdSea_OrThinIce_LaysNothing()
		{
			Assert.That(Coast((x, z) => false), Is.Null, "a warm sea holds no shelf");
			Assert.That(Coast((x, z) => true, landIce: IceShelfReach.SourceIceMetres - 1f), Is.Null, "a thin margin calves no shelf");
			Assert.That(Coast((x, z) => true, landRaw: IceShelfReach.SourceGroundMetres + 10f), Is.Null, "an ice-capped sea cliff keeps its tidewater cliff");
			Assert.That(Coast((x, z) => true), Is.Not.Null);
			Assert.Throws<ArgumentException>(() => IceShelfReach.Build(2, 2, Cell, Vector2.zero, new float[3], new float[4], new bool[4], Seed));
		}

		[Test]
		public void Walk_Out_ShelfThenFrontThenFastIceThenOpenSea()
		{
			IceShelfReach reach = Coast((x, z) => true);
			foreach (float north in new[] { -1500f, -320f, 0f, 777f, 2100f })
			{
				Assert.That(reach.SurfaceAt(CoastEast - 200f, north, LandGround), Is.EqualTo(LandGround), "land is untouched");
				Assert.That(reach.PartAt(CoastEast - 200f, north, LandGround), Is.EqualTo(IceShelfPart.None));

				IceShelfPart last = IceShelfPart.Shelf;
				float front = float.NaN, floe = float.NaN, before = float.NaN;
				for (float east = CoastEast + 1f; east < Columns * Cell - 200f; east += 1f)
				{
					IceShelfPart part = reach.PartAt(east, north, GroundBefore(east));
					float surface = reach.SurfaceAt(east, north, GroundBefore(east));
					if (part != last)
					{
						Assert.That((int)part, Is.EqualTo(((int)last + 1) % 3), $"shelf → fast ice → open sea, never back (north {north}, east {east})");
						if (part == IceShelfPart.Apron)
						{
							front = east;
							Assert.That(before, Is.GreaterThanOrEqualTo(IceShelfReach.MinFreeboardMetres), "the front stands its freeboard…");
							Assert.That(surface, Is.EqualTo(IceShelfReach.ApronFreeboardMetres), "…straight down to the fast ice in one metre: a vertical front");
						}
						else
						{
							floe = east;
						}
						last = part;
					}
					switch (part)
					{
						case IceShelfPart.Shelf:
							Assert.That(surface, Is.InRange(IceShelfReach.MinFreeboardMetres, LandGround + IceShelfReach.SwellMetres));
							reach.Sample(east, north, out float metres, out _);
							if (metres > 1500f)
							{
								Assert.That(surface, Is.InRange(IceShelfReach.MinFreeboardMetres, IceShelfReach.MaxFreeboardMetres + IceShelfReach.SwellMetres), "12–35 m once past the hinge");
							}
							break;
						case IceShelfPart.Apron:
							Assert.That(surface, Is.EqualTo(IceShelfReach.ApronFreeboardMetres));
							break;
						default:
							Assert.That(surface, Is.EqualTo(SeaFloor), "the open sea beyond the floe edge is the sea it was");
							break;
					}
					before = surface;
				}
				Assert.That(float.IsNaN(front), Is.False, "a front");
				Assert.That(float.IsNaN(floe), Is.False, "a floe edge");
				Assert.That(last, Is.EqualTo(IceShelfPart.None));
				reach.Sample(front, north, out float atFront, out _);
				Assert.That(atFront, Is.InRange(IceShelfReach.MinReachMetres, IceShelfReach.MaxReachMetres + 2f), "the front 2–8 km out");
				reach.Sample(floe, north, out float atFloe, out _);
				Assert.That(atFloe - atFront, Is.InRange(IceShelfReach.MinApronMetres - 250f, IceShelfReach.MaxApronMetres + 250f), "fast ice 0.5–2 km");
			}
		}

		[Test]
		public void Reach_IsDeterministic_AndTheSeedMovesTheFront()
		{
			IceShelfReach a = Coast((x, z) => true), b = Coast((x, z) => true), other = Coast((x, z) => true, seed: 99u);
			int moved = 0;
			for (int i = 0; i < 4000; i++)
			{
				float east = CoastEast + 3.7f * i, north = -2000f + 0.97f * i;
				float ground = GroundBefore(east);
				Assert.That(b.SurfaceAt(east, north, ground), Is.EqualTo(a.SurfaceAt(east, north, ground)));
				Assert.That(b.PartAt(east, north, ground), Is.EqualTo(a.PartAt(east, north, ground)));
				moved += other.PartAt(east, north, ground) != a.PartAt(east, north, ground) ? 1 : 0;
			}
			Assert.That(moved, Is.GreaterThan(0), "the front is the body's own, not a fixed line");
			Assert.That(a.Sources, Is.EqualTo(Rows), "one calving-coast cell per row");
		}

		[Test]
		public void WarmWater_StopsTheShelf_AndAHeadlandHidesTheBayBehindIt()
		{
			// Cold for 20 columns past the coast only.
			IceShelfReach band = Coast((x, z) => x < LandColumns + 20);
			for (float east = (LandColumns + 22) * Cell; east < Columns * Cell; east += 50f)
			{
				Assert.That(band.PartAt(east, 0f, SeaFloor), Is.EqualTo(IceShelfPart.None), "no shelf over water warmer than −5 °C");
			}
			Assert.That(band.PartAt((LandColumns + 5) * Cell, 0f, SeaFloor), Is.EqualTo(IceShelfPart.Shelf));

			// A wall of land 30 columns out, open only at the far north: the bay behind it is out of reach.
			IceShelfReach wall = Coast((x, z) => true, land: (x, z) => x == LandColumns + 18 && z < Rows - 2);
			Assert.That(wall.PartAt((LandColumns + 10) * Cell, -1000f, SeaFloor), Is.EqualTo(IceShelfPart.Shelf), "before the wall");
			Assert.That(wall.Sample((LandColumns + 20) * Cell, -2000f, out float behind, out _), Is.True);
			Assert.That(behind, Is.GreaterThan(((LandColumns + 20) * Cell - CoastEast) + 2000f), "measured round the headland, not through it");
		}

		// ── The profile ───────────────────────────────────────────────

		[Test]
		public void Profile_StaysInItsRanges_AndVaries()
		{
			float reachMin = float.MaxValue, reachMax = float.MinValue, apronMin = float.MaxValue, apronMax = float.MinValue;
			for (int i = 0; i < 2500; i++)
			{
				float east = (i % 50) * 777f - 19000f, north = (i / 50) * 777f - 19000f;
				float reach = IceShelfReach.ReachMetres(Seed, east, north);
				float apron = IceShelfReach.ApronMetres(Seed, east, north);
				float front = IceShelfReach.FrontFreeboardMetres(Seed, east, north);
				Assert.That(reach, Is.InRange(IceShelfReach.MinReachMetres, IceShelfReach.MaxReachMetres));
				Assert.That(apron, Is.InRange(IceShelfReach.MinApronMetres, IceShelfReach.MaxApronMetres));
				Assert.That(front, Is.InRange(IceShelfReach.MinFreeboardMetres, IceShelfReach.MinFreeboardMetres + IceShelfReach.FrontRiseMetres));
				Assert.That(IceShelfReach.ReachMetres(Seed, east, north), Is.EqualTo(reach), "pure");
				reachMin = Mathf.Min(reachMin, reach);
				reachMax = Mathf.Max(reachMax, reach);
				apronMin = Mathf.Min(apronMin, apron);
				apronMax = Mathf.Max(apronMax, apron);
			}
			Assert.That(reachMax - reachMin, Is.GreaterThan(3000f), "embayments and ice tongues");
			Assert.That(apronMax - apronMin, Is.GreaterThan(700f));
		}

		[Test]
		public void Freeboard_MeetsTheSheet_ThinsSeaward_ToTheFront()
		{
			const float reach = 5000f, front = 15f;
			Assert.That(IceShelfReach.Freeboard(0f, reach, 45f, front), Is.EqualTo(45f).Within(1e-4f), "meets the sheet at the grounding line");
			Assert.That(IceShelfReach.Freeboard(reach, reach, 45f, front), Is.EqualTo(front).Within(1e-3f), "the front's own freeboard at the front");
			Assert.That(IceShelfReach.Freeboard(reach, reach, 25f, 30f), Is.EqualTo(25f).Within(1e-4f), "never a front above its grounding line");
			Assert.That(IceShelfReach.Freeboard(1000f, reach, 45f, front), Is.LessThanOrEqualTo(IceShelfReach.MaxFreeboardMetres + 0.2f), "35 m past the hinge");
			float previous = float.MaxValue;
			for (float d = 0f; d <= reach; d += 25f)
			{
				float f = IceShelfReach.Freeboard(d, reach, 45f, front);
				Assert.That(f, Is.LessThanOrEqualTo(previous + 1e-4f), "thins seaward");
				Assert.That(f, Is.GreaterThanOrEqualTo(front - 1e-4f));
				previous = f;
			}
		}

		[Test]
		public void Describe_CountsTheSceneOnly()
		{
			IceShelfReach reach = Coast((x, z) => true);
			string inside = reach.Describe(new Rect(CoastEast, -2000f, 9000f, 4000f));
			StringAssert.StartsWith("Ice shelf: 40 calving-coast cell(s)", inside);
			StringAssert.Contains("km² of floating shelf", inside);
			StringAssert.Contains("0 km² of floating shelf", reach.Describe(new Rect(-5000f, -2000f, 1000f, 4000f)));
		}
	}
}
