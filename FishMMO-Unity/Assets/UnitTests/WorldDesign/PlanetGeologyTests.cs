using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A planet's rock: provinces that are regions and not noise, kinds of rock the world allows, and
	/// beds whose hardness changes with height the way strata do.
	/// </summary>
	[TestFixture]
	public class PlanetGeologyTests
	{
		private const double AtlasRadiusKm = 30.0;
		private WorldBody body;

		[SetUp]
		public void SetUp()
		{
			body = ScriptableObject.CreateInstance<WorldBody>();
			body.name = "Geology World";
			body.TerrainSeed = 4242u;
		}

		[TearDown]
		public void TearDown()
		{
			Object.DestroyImmediate(body);
		}

		private PlanetGeology Earthlike() => PlanetGeology.For(body, BiomeWorldConditions.Earthlike, AtlasRadiusKm);

		/// <summary>Evenly spread directions over the sphere.</summary>
		private static IEnumerable<Vector3> Directions(int count)
		{
			for (int i = 0; i < count; i++)
			{
				float y = 1f - 2f * (i + 0.5f) / count;
				float r = Mathf.Sqrt(1f - y * y);
				float a = i * 2.39996323f;
				yield return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
			}
		}

		/// <summary>A point a few metres along the surface from <paramref name="direction"/>.</summary>
		private static Vector3 Nudge(Vector3 direction, float metres)
		{
			Vector3 tangent = Vector3.Cross(direction, Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
			return (direction + tangent * (float)(metres / (AtlasRadiusKm * 1000.0))).normalized;
		}

		[Test]
		public void TheSameBodyHasTheSameRock()
		{
			PlanetGeology a = Earthlike(), b = Earthlike();
			foreach (Vector3 direction in Directions(200))
			{
				GeologyColumn x = a.ColumnAt(direction), y = b.ColumnAt(direction);
				Assert.That(y.Province, Is.EqualTo(x.Province));
				Assert.That(y.LithologyIndex, Is.EqualTo(x.LithologyIndex));
				Assert.That(y.HardnessAt(120f), Is.EqualTo(x.HardnessAt(120f)));
			}
		}

		[Test]
		public void ProvincesAreRegionsWithEdgesWhereTheRockChanges()
		{
			/* Many provinces over the globe, and a province only changes where the column says its
			 * edge is: two points 20 m apart in different provinces are both within 20 m of an edge. */
			PlanetGeology geology = Earthlike();
			var provinces = new HashSet<int>();
			int crossings = 0;
			foreach (Vector3 direction in Directions(4000))
			{
				GeologyColumn here = geology.ColumnAt(direction);
				provinces.Add(here.Province);
				GeologyColumn there = geology.ColumnAt(Nudge(direction, 20f));
				if (there.Province != here.Province)
				{
					crossings++;
					Assert.That(here.BoundaryMetres, Is.LessThan(20.5f), "a province changed where its column said the edge was far");
					Assert.That(there.BoundaryMetres, Is.LessThan(20.5f));
				}
			}
			Assert.That(provinces.Count, Is.GreaterThan(30), "a 30 km globe should hold dozens of provinces");
			Assert.That(crossings, Is.LessThan(4000 / 20), "20 m steps should rarely cross a 12 km province's edge");
		}

		[Test]
		public void AnEarthLikeWorldIsMostlySediment()
		{
			float[] shares = PlanetGeology.Shares(BiomeWorldConditions.Earthlike);
			Assert.That(FamilyShare(shares, GeologyFamily.Sedimentary), Is.GreaterThan(0.45f));
			Assert.That(FamilyShare(shares, GeologyFamily.Ice), Is.EqualTo(0f));
		}

		[Test]
		public void AnAirlessDryRockHasNoCarbonateAndIsMostlyIgneous()
		{
			BiomeWorldConditions moon = BiomeWorldConditions.Earthlike;
			moon.Atmosphere = AtmosphereKind.None;
			moon.Water = 0f;
			moon.InternalHeat = 0.16f;
			float[] shares = PlanetGeology.Shares(moon);
			for (int i = 0; i < shares.Length; i++)
			{
				if (PlanetGeology.Lithologies[i].NeedsLiquidWater)
				{
					Assert.That(shares[i], Is.EqualTo(0f), PlanetGeology.Lithologies[i].Name);
				}
			}
			Assert.That(FamilyShare(shares, GeologyFamily.Igneous), Is.GreaterThan(0.5f));
		}

		[Test]
		public void AnIceWorldsCrustIsIce()
		{
			BiomeWorldConditions europa = BiomeWorldConditions.Earthlike;
			europa.Atmosphere = AtmosphereKind.None;
			europa.Water = 0.5f;
			europa.MeanTemperature = (float)ClimateModel.ToScaleUnclamped(100.0);
			Assert.That(europa.IsIceWorld, Is.True, "test world must be an ice world");
			Assert.That(FamilyShare(PlanetGeology.Shares(europa), GeologyFamily.Ice), Is.GreaterThan(0.6f));
		}

		[Test]
		public void FlatBedsChangeHardnessWithHeightAndOnlyUpward()
		{
			/* Up a column of flat-lying beds the hardness steps from bed to bed — hard members and soft
			 * ones — which is what will leave ledges over slopes; the bed index only ever climbs. */
			PlanetGeology geology = Earthlike();
			GeologyColumn column = default;
			bool found = false;
			foreach (Vector3 direction in Directions(4000))
			{
				column = geology.ColumnAt(direction);
				if (column.FlatLying && column.Lithology.Style == StrataStyle.Bedded)
				{
					found = true;
					break;
				}
			}
			Assert.That(found, Is.True, "an Earth-like world should have flat-lying sediment somewhere");

			Lithology rock = column.Lithology;
			int changes = 0, previousBed = column.BedAt(0f);
			float softest = 1f, hardest = 0f;
			for (float altitude = 0.25f; altitude <= 400f; altitude += 0.25f)
			{
				int bed = column.BedAt(altitude);
				Assert.That(bed, Is.GreaterThanOrEqualTo(previousBed), "beds never fold back on themselves");
				float hardness = column.HardnessAt(altitude);
				Assert.That(hardness, Is.InRange(0f, 1f));
				softest = Mathf.Min(softest, hardness);
				hardest = Mathf.Max(hardest, hardness);
				if (bed != previousBed)
				{
					changes++;
				}
				previousBed = bed;
			}
			Assert.That(changes, Is.InRange(Mathf.FloorToInt(400f / rock.BedMaxMetres / 2f), Mathf.CeilToInt(400f / rock.BedMinMetres * 2f)),
				$"{changes} beds in 400 m of {rock.Name} ({rock.BedMinMetres}–{rock.BedMaxMetres} m)");
			Assert.That(hardest - softest, Is.GreaterThan(rock.BedContrast * 0.5f), "the beds differ in hardness");
		}

		[Test]
		public void MassiveRockIsTheSameHardnessAllTheWayDown()
		{
			PlanetGeology geology = Earthlike();
			foreach (Vector3 direction in Directions(4000))
			{
				GeologyColumn column = geology.ColumnAt(direction);
				if (column.Lithology.Style != StrataStyle.Massive)
				{
					continue;
				}
				for (float altitude = -100f; altitude <= 500f; altitude += 37f)
				{
					Assert.That(column.HardnessAt(altitude), Is.EqualTo(column.Lithology.Hardness));
				}
				return;
			}
			Assert.Fail("an Earth-like world should have massive rock somewhere");
		}

		[Test]
		public void EveryRockTheGeologyNamesHasCliffRocks()
		{
			/* Cliffs and canyon walls wear the rock under them (CliffPlacerOptions.RockTypeAt); a name
			 * the art has no rock for would quietly fall back to the biome's own. */
			foreach (Lithology rock in PlanetGeology.Lithologies)
			{
				if (rock.Family == GeologyFamily.Ice)
				{
					Assert.That(rock.RockTypeName, Is.Null, "ice is the cliffs' own ice, not a rock type");
					continue;
				}
				Assert.That(RockTypes.TryGet(rock.RockTypeName, out _), Is.True, $"{rock.Name} has no rock type for its cliffs");
			}
		}

		private static float FamilyShare(float[] shares, GeologyFamily family)
		{
			float sum = 0f;
			for (int i = 0; i < shares.Length; i++)
			{
				if (PlanetGeology.Lithologies[i].Family == family)
				{
					sum += shares[i];
				}
			}
			return sum;
		}
	}
}
