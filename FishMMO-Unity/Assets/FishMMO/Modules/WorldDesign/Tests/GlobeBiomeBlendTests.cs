using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The globe bake softens biome borders and varies each biome inside itself, on a synthetic field:
	/// a border pixel takes a blend of both biomes, a pixel deep inside one keeps its colour exactly,
	/// a coast is never blended, and the variation follows the ground continuously.
	/// </summary>
	/// <remarks>
	/// The regression these guard: the bake painted one flat mean colour per biome with no blending,
	/// so biomes met in pixel steps and filled whole regions with a single swatch.
	/// </remarks>
	[TestFixture]
	public class GlobeBiomeBlendTests
	{
		private const int Width = 16;
		private const int Height = 8;

		private static readonly Color32 A = new Color32(200, 60, 40, 255);
		private static readonly Color32 B = new Color32(40, 160, 220, 255);

		private ushort[] ids;
		private Color32[] bases;
		private Color32[] cliffs;
		private bool[] art;

		/// <summary>Biome 1 (colour A) west of column 8, biome 2 (colour B) from it east; all land, all art.</summary>
		[SetUp]
		public void TwoBiomes()
		{
			ids = new ushort[Width * Height];
			bases = new Color32[Width * Height];
			cliffs = new Color32[Width * Height];
			art = new bool[Width * Height];
			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					int i = y * Width + x;
					bool west = x < Width / 2;
					ids[i] = (ushort)(west ? 1 : 2);
					bases[i] = west ? A : B;
					cliffs[i] = new Color32(128, 128, 128, 255);
					art[i] = true;
				}
			}
		}

		private PlanetSurfaceBaker.LandBlend At(int x, int y, float warpX = 0f, float warpY = 0f)
		{
			return PlanetSurfaceBaker.BlendAcrossBiomes(ids, bases, cliffs, art, Width, Height, x, y, x + warpX, y + warpY, 1f);
		}

		private static void AssertColour(Color32 expected, Color actual, string message)
		{
			Color e = expected;
			Assert.AreEqual(e.r, actual.r, 1e-4f, message + " (r)");
			Assert.AreEqual(e.g, actual.g, 1e-4f, message + " (g)");
			Assert.AreEqual(e.b, actual.b, 1e-4f, message + " (b)");
		}

		[Test]
		public void APixelDeepInsideABiome_KeepsItsColourExactly()
		{
			// Column 3 is five pixels from the border: past the blend's reach, so nothing changes.
			PlanetSurfaceBaker.LandBlend blend = At(3, 4);
			AssertColour(A, blend.Ground, "inside biome 1");
			Assert.AreEqual(1f, blend.Art);
		}

		[Test]
		public void BorderPixels_TakeABlendOfBothBiomes_WeightedByDistance()
		{
			Color a = A, b = B;
			Color west = At(7, 4).Ground;
			Color east = At(8, 4).Ground;
			Color farther = At(6, 4).Ground;

			Assert.Less(west.r, a.r, "the last pixel of biome 1 takes some of biome 2");
			Assert.Greater(west.r, b.r);
			Assert.Greater(west.r, east.r, "and stays nearer its own colour than the first pixel of biome 2 does");
			Assert.Greater(farther.r, west.r, "one pixel further from the border, less of the neighbour");
			Assert.AreEqual(a.r + b.r, west.r + east.r, 1e-4f, "the two sides of a straight border mirror each other");
		}

		[Test]
		public void TheWarp_MovesTheBorder()
		{
			// Read around a point two pixels east, a pixel of biome 1 three from the border takes some of biome 2.
			Color plain = At(5, 4).Ground;
			Color warped = At(5, 4, warpX: 2f).Ground;
			AssertColour(A, plain, "unwarped, three pixels in is untouched");
			Assert.Less(warped.r, plain.r, "warped toward the border, it blends: the border becomes ragged");
		}

		[Test]
		public void ACoast_IsNeverBlended()
		{
			// Biome 2 becomes sea (id 0): the shore pixel of biome 1 keeps its own colour, exactly as
			// the surface field draws the coast, and the sea is never mixed into the land.
			for (int y = 0; y < Height; y++)
			{
				for (int x = Width / 2; x < Width; x++)
				{
					ids[y * Width + x] = 0;
				}
			}
			AssertColour(A, At(7, 4).Ground, "the shore pixel");
		}

		[Test]
		public void TheBlend_WrapsEastWest()
		{
			// Column 0's western neighbours are columns 15 and 14: biome 2 across the antimeridian.
			Color a = A;
			Assert.Less(At(0, 4).Ground.r, a.r, "the map is a sphere: the border at the seam blends too");
		}

		[Test]
		public void AnArtlessNeighbour_ScalesTheVariationDown()
		{
			for (int i = 0; i < art.Length; i++)
			{
				art[i] = ids[i] == 1;
			}
			PlanetSurfaceBaker.LandBlend west = At(7, 4);
			Assert.That(west.Art, Is.InRange(0.01f, 0.99f), "the share of art follows the blend");
		}

		// ── Variation ────────────────────────────────────────────────

		private static float Luma(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

		private static PlanetSurfaceBaker.LandBlend Plain(float art = 1f)
		{
			return new PlanetSurfaceBaker.LandBlend
			{
				Ground = new Color(0.35f, 0.45f, 0.25f, 1f),
				Cliff = new Color(0.5f, 0.5f, 0.5f, 1f),
				Art = art,
			};
		}

		[Test]
		public void HighGround_IsPalerThanLowGround()
		{
			Color low = PlanetSurfaceBaker.Vary(Plain(), 100f, 0f, true, 0f, 0f);
			Color high = PlanetSurfaceBaker.Vary(Plain(), 5000f, 0f, true, 0f, 0f);
			Assert.Greater(Luma(high), Luma(low));
		}

		[Test]
		public void WetGround_IsGreener_DryGround_Browner_OnlyWhereThingsGrow()
		{
			Color wet = PlanetSurfaceBaker.Vary(Plain(), 500f, 0.9f, true, 0f, 0f);
			Color dry = PlanetSurfaceBaker.Vary(Plain(), 500f, -0.9f, true, 0f, 0f);
			Assert.Greater(wet.g / wet.r, dry.g / dry.r, "greener where wet");
			Color barrenWet = PlanetSurfaceBaker.Vary(Plain(), 500f, 0.9f, false, 0f, 0f);
			Color barrenDry = PlanetSurfaceBaker.Vary(Plain(), 500f, -0.9f, false, 0f, 0f);
			Assert.AreEqual(barrenWet, barrenDry, "a world without liquid water has nothing to green");
		}

		[Test]
		public void SteepGround_ShowsRock()
		{
			Color flat = PlanetSurfaceBaker.Vary(Plain(), 500f, 0f, true, 0f, 0f);
			Color steep = PlanetSurfaceBaker.Vary(Plain(), 500f, 0f, true, 0.2f, 0f);
			Color rock = Plain().Cliff;
			float flatDistance = Mathf.Abs(flat.g - rock.g) + Mathf.Abs(flat.b - rock.b);
			float steepDistance = Mathf.Abs(steep.g - rock.g) + Mathf.Abs(steep.b - rock.b);
			Assert.Less(steepDistance, flatDistance, "a steep pixel moves toward its biome's rock");
			Assert.Greater(steepDistance, 0f, "but a 20 km pixel is never all cliff");
		}

		[Test]
		public void TheVariation_IsSubtle_AndLeavesArtlessGroundAlone()
		{
			Color ground = Plain().Ground;
			Color extreme = PlanetSurfaceBaker.Vary(Plain(), 9000f, -1f, true, 0f, 1f);
			Assert.Less(Mathf.Abs(Luma(extreme) - Luma(ground)), 0.12f, "every term together moves brightness by a few percent");
			Color physical = PlanetSurfaceBaker.Vary(Plain(art: 0f), 9000f, -1f, true, 0.2f, 1f);
			Assert.AreEqual(ground.r, physical.r, 1e-6f, "the physical shading already varies; it is left as it is");
			Assert.AreEqual(ground.g, physical.g, 1e-6f);
			Assert.AreEqual(ground.b, physical.b, 1e-6f);
		}
	}
}
