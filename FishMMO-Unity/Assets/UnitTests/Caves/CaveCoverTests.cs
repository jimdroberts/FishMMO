using System;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.Caves
{
	/// <summary>
	/// The run-time cave cover (2026-10-20): its hashes are fixed functions of position (the same cover on every client, still as
	/// the camera moves), its density arithmetic grows each type where its habitat says and nowhere else, the distance rings keep
	/// the far cover a subset of the near, the type table and its art agree in order, and the cut's species lookup gives each form
	/// of cave its own cover and the dressing's glow colours. FishCaveCover.compute mirrors CaveCoverMath: these pin both.
	/// </summary>
	[TestFixture]
	public class CaveCoverTests
	{
		// ── Hashes ──

		[Test]
		public void TheHashesArePinned()
		{
			// Values worked out independently (32-bit unsigned arithmetic) so a change to the mix shows here and in the compute alike.
			Assert.That(CaveCoverMath.Mix(0u), Is.EqualTo(0u));
			Assert.That(CaveCoverMath.Mix(1u), Is.EqualTo(0x688990C0u));
			uint key = CaveCoverMath.TriangleKey(new Vector3(0.5f, 2.25f, -3.75f));
			Assert.That(key, Is.EqualTo(0xE9D1EA52u));
			uint salt = CaveCoverMath.Salt(CaveCoverType.Grit);
			Assert.That(salt, Is.EqualTo(0x079AEE3Fu));
			uint candidate = CaveCoverMath.Candidate(key, salt, 0u);
			Assert.That(candidate, Is.EqualTo(0xCEAB525Cu));
			Assert.That(CaveCoverMath.Unit(candidate, 0u), Is.EqualTo(0.08791112899780273f).Within(1e-7f));
		}

		[Test]
		public void ATrianglesKeyIsItsCentroidToTheCentimetre()
		{
			Vector3 c = new Vector3(12.3401f, -4.5672f, 7.8903f);
			Assert.That(CaveCoverMath.TriangleKey(c), Is.EqualTo(CaveCoverMath.TriangleKey(c)), "the same rock, the same key");
			Assert.That(CaveCoverMath.TriangleKey(c + new Vector3(0.002f, 0.002f, 0.002f)), Is.EqualTo(CaveCoverMath.TriangleKey(c)), "within its centimetre");
			Assert.That(CaveCoverMath.TriangleKey(c + new Vector3(0.05f, 0f, 0f)), Is.Not.EqualTo(CaveCoverMath.TriangleKey(c)), "the next centimetres over");
		}

		[Test]
		public void UnitsAreInTheHalfOpenUnitInterval_AndSpreadEvenly()
		{
			int[] buckets = new int[10];
			for (uint i = 0; i < 20000u; i++)
			{
				float u = CaveCoverMath.Unit(CaveCoverMath.Candidate(0xABCDu, 77u, i), 3u);
				Assert.That(u, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
				buckets[(int)(u * 10f)]++;
			}
			foreach (int b in buckets)
			{
				Assert.That(b, Is.InRange(1800, 2200), "roughly uniform");
			}
		}

		[Test]
		public void BarycentricPointsLieOnTheTriangle()
		{
			for (uint i = 0; i < 500u; i++)
			{
				uint h = CaveCoverMath.Candidate(5u, 9u, i);
				Vector3 b = CaveCoverMath.Barycentric(CaveCoverMath.Unit(h, 0u), CaveCoverMath.Unit(h, 1u));
				Assert.That(b.x, Is.GreaterThanOrEqualTo(-1e-6f));
				Assert.That(b.y, Is.GreaterThanOrEqualTo(0f));
				Assert.That(b.z, Is.GreaterThanOrEqualTo(0f));
				Assert.That(b.x + b.y + b.z, Is.EqualTo(1f).Within(1e-5f));
			}
		}

		// ── Habitat and density ──

		[Test]
		public void FloorsWallsAndRoofsAreTheNormalsUpDownAndAcross()
		{
			Assert.That(CaveCoverMath.ClassWeights(1f), Is.EqualTo(new Vector3(1f, 0f, 0f)));
			Assert.That(CaveCoverMath.ClassWeights(-1f), Is.EqualTo(new Vector3(0f, 0f, 1f)));
			Assert.That(CaveCoverMath.ClassWeights(0f), Is.EqualTo(new Vector3(0f, 1f, 0f)));
			for (float ny = -1f; ny <= 1f; ny += 0.05f)
			{
				Vector3 w = CaveCoverMath.ClassWeights(ny);
				Assert.That(w.x + w.y + w.z, Is.EqualTo(1f).Within(1e-5f), $"ny {ny}");
			}
		}

		[Test]
		public void AnOldMeshsWhiteIsTheModestDefault_AndNoUv3IsNoGlow()
		{
			CaveCoverHabitat old = CaveCoverMath.HabitatOf(Color.white, Vector2.zero, false);
			Assert.That(old.Glow, Is.EqualTo(CaveCoverMath.Unknown.Glow));
			Assert.That(old.Daylight, Is.EqualTo(0f), "no daylight assumed everywhere");
			CaveCoverHabitat written = CaveCoverMath.HabitatOf(new Color(0.8f, 0.1f, 0.2f, 0.3f), new Vector2(0.9f, 0.7f), false);
			Assert.That(written.Wet, Is.EqualTo(0.8f));
			Assert.That(written.Organic, Is.EqualTo(0f));
			Assert.That(written.Glow, Is.EqualTo(0f));
			CaveCoverHabitat full = CaveCoverMath.HabitatOf(new Color(0.8f, 0.1f, 0.2f, 0.3f), new Vector2(1.5f, 0.7f), true);
			Assert.That(full.Organic, Is.EqualTo(1f), "clamped");
			Assert.That(full.Glow, Is.EqualTo(0.7f));
		}

		[Test]
		public void EachTypeGrowsOnlyOnItsOwnSurfaces()
		{
			var rich = new CaveCoverHabitat(1f, 1f, 1f, 0f, 1f, 1f);
			Assert.That(CaveCoverMath.Density(in CaveCoverTypes.Info(CaveCoverType.SodaStraws), in rich, 1f, 1f), Is.EqualTo(0f), "no soda straws on a floor");
			Assert.That(CaveCoverMath.Density(in CaveCoverTypes.Info(CaveCoverType.SodaStraws), in rich, -1f, 1f), Is.GreaterThan(0f), "on a roof");
			Assert.That(CaveCoverMath.Density(in CaveCoverTypes.Info(CaveCoverType.Mushrooms), in rich, -1f, 1f), Is.EqualTo(0f), "no mushrooms on a roof");
			Assert.That(CaveCoverMath.Density(in CaveCoverTypes.Info(CaveCoverType.BracketFungus), in rich, 0f, 1f), Is.GreaterThan(0f), "brackets on a wall");
			Assert.That(CaveCoverMath.Density(in CaveCoverTypes.Info(CaveCoverType.BracketFungus), in rich, 1f, 1f), Is.EqualTo(0f), "not on a floor");
		}

		[Test]
		public void GlowCoverNeedsTheGlow_DaylightCoverTheDaylight()
		{
			var dark = new CaveCoverHabitat(0.5f, 0f, 0.3f, 0f, 0.3f, 0f);
			var glowing = dark;
			glowing.Glow = 1f;
			var lit = dark;
			lit.Daylight = 1f;
			ref readonly CaveCoverTypeInfo moss = ref CaveCoverTypes.Info(CaveCoverType.GlowMoss);
			Assert.That(CaveCoverMath.Density(in moss, in glowing, 1f, 1f), Is.GreaterThan(4f * CaveCoverMath.Density(in moss, in dark, 1f, 1f)));
			ref readonly CaveCoverTypeInfo lichen = ref CaveCoverTypes.Info(CaveCoverType.Lichen);
			Assert.That(CaveCoverMath.Density(in lichen, in lit, 0f, 1f), Is.GreaterThan(4f * CaveCoverMath.Density(in lichen, in dark, 0f, 1f) + 1e-4f));
			ref readonly CaveCoverTypeInfo roots = ref CaveCoverTypes.Info(CaveCoverType.HangingRoots);
			Assert.That(CaveCoverMath.Density(in roots, in dark, -1f, 1f), Is.EqualTo(0f), "no roots in the deep dark");
		}

		[Test]
		public void FeetWearAwayAllButTheGrit()
		{
			var worn = new CaveCoverHabitat(0.6f, 0.3f, 0.6f, 1f, 0.6f, 0.6f);
			var untrodden = worn;
			untrodden.Trodden = 0f;
			ref readonly CaveCoverTypeInfo grit = ref CaveCoverTypes.Info(CaveCoverType.Grit);
			ref readonly CaveCoverTypeInfo moss = ref CaveCoverTypes.Info(CaveCoverType.MossCushion);
			Assert.That(CaveCoverMath.Density(in grit, in worn, 1f, 1f), Is.GreaterThanOrEqualTo(CaveCoverMath.Density(in grit, in untrodden, 1f, 1f)), "grit is kept (more) on a way");
			Assert.That(CaveCoverMath.Density(in moss, in worn, 1f, 1f), Is.LessThan(0.2f * CaveCoverMath.Density(in moss, in untrodden, 1f, 1f)), "moss is worn off a way");
		}

		[Test]
		public void ATrianglesExpectedCountIsExact()
		{
			// Over many triangles, the candidates that exist average area × density (the last by its coin).
			const float n = 2.37f;
			long total = 0;
			const int triangles = 40000;
			for (uint t = 0; t < triangles; t++)
			{
				uint key = CaveCoverMath.Mix(t * 2654435761u + 1u);
				int count = CaveCoverMath.Candidates(1f, n);
				for (int k = 0; k < count; k++)
				{
					uint h = CaveCoverMath.Candidate(key, 11u, (uint)k);
					if (!CaveCoverMath.Exists(n, k, CaveCoverMath.Unit(h, 7u)))
					{
						break;
					}
					total++;
				}
			}
			Assert.That(total / (double)triangles, Is.EqualTo(n).Within(0.02));
			Assert.That(CaveCoverMath.Candidates(1000f, 10f), Is.EqualTo(CaveCoverMath.MaxPerTriangle), "capped");
		}

		// ── Distance ──

		private static readonly Vector2[] Rings = new FishMMO.Client.CaveCoverSettings().Rings;

		[Test]
		public void TheRingsKeepWholeNearAndNothingPastTheLast_FallingBetween()
		{
			Assert.That(CaveCoverMath.Keep(0f, Rings, Rings.Length), Is.EqualTo(1f));
			Assert.That(CaveCoverMath.Keep(Rings[Rings.Length - 1].x + 0.01f, Rings, Rings.Length), Is.EqualTo(0f));
			float last = 1f;
			for (float d = 0f; d < 60f; d += 0.25f)
			{
				float k = CaveCoverMath.Keep(d, Rings, Rings.Length);
				Assert.That(k, Is.LessThanOrEqualTo(last + 1e-6f), $"falls at {d} m");
				last = k;
			}
		}

		[Test]
		public void TheFarCoverIsASubsetOfTheNear()
		{
			for (uint i = 0; i < 2000u; i++)
			{
				float hash = CaveCoverMath.Unit(CaveCoverMath.Candidate(3u, 4u, i), 2u);
				for (float d = 0f; d < 48f; d += 1f)
				{
					float far = CaveCoverMath.ThinFade(CaveCoverMath.Keep(d + 1f, Rings, Rings.Length), hash);
					float near = CaveCoverMath.ThinFade(CaveCoverMath.Keep(d, Rings, Rings.Length), hash);
					Assert.That(near, Is.LessThanOrEqualTo(far + 1e-6f), "nearer, never more faded");
				}
			}
			Assert.That(CaveCoverMath.ThinFade(1f, 0.999f), Is.EqualTo(0f), "a full keep draws everything");
		}

		[Test]
		public void TheLevelsStepWithDistanceAndTheTypesReach()
		{
			Assert.That(CaveCoverMath.LevelOf(3f, 1f, 7f, 18f), Is.EqualTo(0));
			Assert.That(CaveCoverMath.LevelOf(10f, 1f, 7f, 18f), Is.EqualTo(1));
			Assert.That(CaveCoverMath.LevelOf(30f, 1f, 7f, 18f), Is.EqualTo(2));
			Assert.That(CaveCoverMath.LevelOf(10f, 1.5f, 7f, 18f), Is.EqualTo(0), "a big type keeps its full clump further");
		}

		// ── The table ──

		[Test]
		public void TheTableIsInEnumOrder_AndEveryMaskFits()
		{
			int count = Enum.GetValues(typeof(CaveCoverType)).Length;
			Assert.That(count, Is.EqualTo(CaveCoverTypes.Count));
			Assert.That(CaveCoverTypes.All.Length, Is.EqualTo(CaveCoverTypes.Count));
			for (int i = 0; i < CaveCoverTypes.All.Length; i++)
			{
				Assert.That((int)CaveCoverTypes.All[i].Type, Is.EqualTo(i));
				Assert.That(CaveCoverTypes.All[i].Scale.x, Is.LessThanOrEqualTo(CaveCoverTypes.All[i].Scale.y));
				Assert.That(CaveCoverTypes.All[i].Reach, Is.GreaterThan(0f));
			}
			Assert.That(CaveCoverTypes.Count, Is.LessThanOrEqualTo(32), "a cave's mask is one uint");
			uint all = (1u << CaveCoverTypes.Count) - 1u;
			foreach (uint mask in new[] { CaveCoverTypes.Natural, CaveCoverTypes.Ice, CaveCoverTypes.Lava, CaveCoverTypes.Mine })
			{
				Assert.That(mask & ~all, Is.EqualTo(0u));
			}
			Assert.That(CaveCoverTypes.SubmeshOf(CaveCoverType.CandleStubs, 2), Is.EqualTo(CaveCoverTypes.Count * CaveCoverTypes.Levels - 1));
		}

		[Test]
		public void TheComputeMirrorsTheTableSize()
		{
			// The compute's constants (COVER_TYPES, COVER_LEVELS, COVER_MAX_PER_TRIANGLE, COVER_THIN_BAND) are the table's.
			string compute = System.IO.File.ReadAllText("Assets/Prefabs/Client/Weather/Shaders/FishCaveCover.compute");
			StringAssert.Contains($"#define COVER_TYPES {CaveCoverTypes.Count}u", compute);
			StringAssert.Contains($"#define COVER_LEVELS {CaveCoverTypes.Levels}u", compute);
			StringAssert.Contains($"#define COVER_MAX_PER_TRIANGLE {CaveCoverMath.MaxPerTriangle}u", compute);
			StringAssert.Contains($"#define COVER_THIN_BAND {CaveCoverMath.FadeBand.ToString(System.Globalization.CultureInfo.InvariantCulture)}", compute);
			StringAssert.Contains("#pragma use_dxc vulkan", compute);
		}

		// ── The species lookup ──

		[Test]
		public void EachFormGrowsItsOwnCover()
		{
			CaveCoverSite.Choice cave = CaveCoverSpecies.For(null, CaveForm.Cave, 1234);
			CaveCoverSite.Choice ice = CaveCoverSpecies.For(null, CaveForm.IceCave, 1234);
			CaveCoverSite.Choice lava = CaveCoverSpecies.For(null, CaveForm.LavaTube, 1234);
			CaveCoverSite.Choice mine = CaveCoverSpecies.For(null, CaveForm.Mine, 1234);
			Assert.That(cave.Types, Is.EqualTo(CaveCoverTypes.Natural));
			Assert.That(ice.Types & CaveCoverTypes.Living, Is.EqualTo(0u), "nothing lives in an ice cave");
			Assert.That(lava.Types & CaveCoverTypes.Living, Is.EqualTo(0u), "nor in a lava tube");
			Assert.That(ice.Types & CaveCoverTypes.Bit(CaveCoverType.FrostCrystals), Is.Not.EqualTo(0u));
			Assert.That(lava.Types & CaveCoverTypes.Bit(CaveCoverType.EmberGlints), Is.Not.EqualTo(0u));
			Assert.That(mine.Types & CaveCoverTypes.Bit(CaveCoverType.CandleStubs), Is.Not.EqualTo(0u), "a mine's candles");
			Assert.That(cave.Types & CaveCoverTypes.Bit(CaveCoverType.CandleStubs), Is.EqualTo(0u), "but not a wild cave's");
			Assert.That((byte)CaveForm.Mine, Is.EqualTo(mine.Form));
		}

		[Test]
		public void TheGlowColoursAreTheSystemsAndRepeatable()
		{
			CaveCoverSite.Choice a = CaveCoverSpecies.For(null, CaveForm.Cave, 98765);
			CaveCoverSite.Choice b = CaveCoverSpecies.For(null, CaveForm.Cave, 98765);
			Assert.That(a.Fungus, Is.EqualTo(b.Fungus));
			Assert.That(a.Accent, Is.EqualTo(b.Accent));
			Assert.That(a.Crystal, Is.EqualTo(b.Crystal));
			Assert.That(ProceduralArtCatalogue.FungusHues.Any(h => h.Emission == a.Fungus), "a fungus hue of the glow art's");
			Assert.That(a.Accent, Is.Not.EqualTo(a.Fungus), "the accent is another hue");
			CaveCoverSite.Choice ice = CaveCoverSpecies.For(null, CaveForm.IceCave, 98765);
			Assert.That(ice.Crystal, Is.EqualTo(ProceduralArtCatalogue.CrystalHues.First(h => h.Name == "IceBlue").Emission));
		}

		// ── The art (builds meshes: under Unity or nativerun) ──

		[Test]
		public void EveryTypeHasArtAtEveryLevel_ThinningWithLevel()
		{
			for (int t = 0; t < CaveCoverTypes.Count; t++)
			{
				int previous = int.MaxValue;
				for (int level = 0; level < CaveCoverTypes.Levels; level++)
				{
					MeshBuilder b = CaveCoverMeshes.Build((CaveCoverType)t, level, ProceduralArtCatalogue.DefaultSeed);
					Assert.That(b.TriangleCount, Is.GreaterThan(0), $"{(CaveCoverType)t} level {level}");
					Assert.That(b.TriangleCount, Is.LessThanOrEqualTo(previous), $"{(CaveCoverType)t} level {level} is no heavier than the one before");
					Assert.That(b.TriangleCount, Is.LessThanOrEqualTo(level == 0 ? 400 : 160), $"{(CaveCoverType)t} level {level} stays light");
					Bounds bounds = b.Bounds;
					Assert.That(bounds.extents.magnitude, Is.LessThan(1.2f), $"{(CaveCoverType)t} is small cover");
					previous = b.TriangleCount;
				}
			}
		}

		[Test]
		public void TheArtIsOneMeshASubmeshATypeAndLevel()
		{
			MeshBuilder set = CaveCoverMeshes.BuildSet(ProceduralArtCatalogue.DefaultSeed);
			Assert.That(set.Submeshes.Count, Is.EqualTo(CaveCoverTypes.Count * CaveCoverTypes.Levels));
			Assert.That(set.Submeshes.All(s => s.Count > 0), "no empty submesh");
			Assert.That(set.Validate(false), Is.Empty);
			MeshBuilder again = CaveCoverMeshes.BuildSet(ProceduralArtCatalogue.DefaultSeed);
			Assert.That(again.Positions, Is.EqualTo(set.Positions), "deterministic");
		}

		[Test]
		public void TheArtIsHashedAndListed()
		{
			Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain("CaveCoverMeshes.cs"));
			Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain("BiomeArtGenerator.CaveCover.cs"));
			Assert.That(ProceduralArtPayload.ForeignSources, Does.Contain("CaveCoverTypes.cs"));
			Assert.That(ProceduralArtFamilies.FamiliesOfFile("CaveCoverMeshes.cs"), Is.EqualTo(new[] { ArtFamily.Structures }));
			Assert.That(ProceduralArtCatalogue.StructurePayloadPaths(), Does.Contain(ProceduralArtCatalogue.CaveCoverMeshPath));
			Assert.That(ProceduralArtCatalogue.CaveCoverMeshPath, Is.EqualTo(FishMMO.Client.CaveCoverRenderer.MeshAssetPath), "the renderer loads it where it is written");
		}
	}
}
