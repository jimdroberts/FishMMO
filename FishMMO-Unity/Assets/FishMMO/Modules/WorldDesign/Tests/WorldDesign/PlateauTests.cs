using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Plateaus: the staircase flat-lying rock wears into, its treads on the rock's hard caps and its
	/// risers steep enough to carry cliffs.
	/// </summary>
	[TestFixture]
	public class PlateauTests
	{
		[Test]
		public void TheStaircaseIsContinuousRisingAndFlatOnEveryWholeStep()
		{
			const float Riser = 0.3f;
			float previous = PlateauTerrace.Step(-3f, Riser);
			for (float t = -3f; t <= 3f; t += 0.001f)
			{
				float value = PlateauTerrace.Step(t, Riser);
				Assert.That(value, Is.GreaterThanOrEqualTo(previous - 1e-5f), $"falls at {t}");
				Assert.That(value - previous, Is.LessThan(0.01f), $"jumps at {t}");
				previous = value;
			}
			for (int k = -2; k <= 2; k++)
			{
				// The tread is centred on the whole step and (1 - riser) wide.
				Assert.That(PlateauTerrace.Step(k, Riser), Is.EqualTo(k).Within(1e-5f));
				Assert.That(PlateauTerrace.Step(k + 0.3f, Riser), Is.EqualTo(k).Within(1e-5f));
				Assert.That(PlateauTerrace.Step(k - 0.3f, Riser), Is.EqualTo(k).Within(1e-5f));
			}
		}

		[Test]
		public void TheStaircaseKeepsTheGroundsMeanHeight()
		{
			/* Symmetric about every tread, so a slope stepped into benches stands no higher or lower on
			 * average: a coast on a plateau stays where the globe draws it. */
			double sum = 0.0, stepped = 0.0;
			int n = 0;
			for (float t = 0f; t < 10f; t += 0.0005f, n++)
			{
				sum += t;
				stepped += PlateauTerrace.Step(t, 0.25f);
			}
			Assert.That(stepped / n, Is.EqualTo(sum / n).Within(0.01));
		}

		private static float[] Tilt(int width, int depth, float cell, float slope)
		{
			var height = new float[width * depth];
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					height[z * width + x] = 100f + slope * x * cell;
				}
			}
			return height;
		}

		[Test]
		public void AGentleSlopeBecomesFlatTreadsAndSteepRisers()
		{
			/* 5% ground, the kind a plateau tilts by. Stepped, most of it is flat and its risers stand
			 * steep enough for cliff paint (35–42°), which is what puts the cliff rocks on them. */
			const int Width = 1000, Depth = 8;
			const float Cell = 2f;
			float[] height = Tilt(Width, Depth, Cell, 0.05f);
			var weight = new float[height.Length];
			var offset = new float[height.Length];
			var step = new float[height.Length];
			for (int i = 0; i < height.Length; i++)
			{
				weight[i] = 1f;
				step[i] = 20f;
			}
			var settings = new PlateauSettings { DetailKept = 0f };
			PlateauTerrace.Apply(height, Width, Depth, Cell, weight, offset, step, float.NegativeInfinity, settings);

			int flat = 0, counted = 0;
			float steepest = 0f;
			int z = Depth / 2;
			// Away from the ends, where the broad shape's smoothing reaches past the ground.
			for (int x = 160; x < Width - 160; x++)
			{
				float slope = (height[z * Width + x + 1] - height[z * Width + x]) / Cell;
				float degrees = Mathf.Atan(Mathf.Abs(slope)) * Mathf.Rad2Deg;
				steepest = Mathf.Max(steepest, degrees);
				if (degrees < 1f)
				{
					flat++;
				}
				counted++;
			}
			Assert.That(flat, Is.GreaterThan(counted / 2), $"{flat} of {counted} samples flat: the treads");
			Assert.That(steepest, Is.GreaterThan(42f), $"the steepest riser is {steepest:0}°");
			Assert.That(steepest, Is.LessThan(75f), "a riser must not become a one-sample wall");
		}

		[Test]
		public void GroundWithNoPlateauIsLeftAlone()
		{
			const int Width = 64, Depth = 64;
			float[] height = Tilt(Width, Depth, 2f, 0.05f);
			var before = (float[])height.Clone();
			var none = new float[height.Length];
			var step = new float[height.Length];
			PlateauTerrace.Apply(height, Width, Depth, 2f, none, none, step, float.NegativeInfinity, new PlateauSettings());
			CollectionAssert.AreEqual(before, height);
		}

		[Test]
		public void FlatLyingSedimentComesInEvenBedsUnderAHardCap()
		{
			/* What the plateau's treads land on: every third bed hard, the two under it soft, beds of
			 * one thickness so the steps line up with them. */
			var body = ScriptableObject.CreateInstance<WorldBody>();
			body.name = "Plateau Geology World";
			body.TerrainSeed = 77u;
			try
			{
				PlanetGeology geology = PlanetGeology.For(body, BiomeWorldConditions.Earthlike, 30.0);
				GeologyColumn column = default;
				bool found = false;
				for (int i = 0; i < 4000 && !found; i++)
				{
					float y = 1f - 2f * (i + 0.5f) / 4000f;
					float r = Mathf.Sqrt(1f - y * y);
					column = geology.ColumnAt(new Vector3(Mathf.Cos(i * 2.39996323f) * r, y, Mathf.Sin(i * 2.39996323f) * r));
					found = column.FlatLying && column.Lithology.Style == StrataStyle.Bedded;
				}
				Assert.That(found, Is.True, "an Earth-like world should have flat-lying sediment somewhere");

				float bed = column.BedMetres;
				Lithology rock = column.Lithology;
				for (int k = 0; k < 12; k++)
				{
					// The middle of bed k, in the column's own stack.
					float middle = (k + 0.5f) * bed - column.Offset;
					Assert.That(column.BedAt(middle), Is.EqualTo(k), "beds are one thickness");
					bool cap = k % GeologyColumn.CapEvery == GeologyColumn.CapEvery - 1;
					float hardness = column.HardnessAt(middle);
					if (cap)
					{
						Assert.That(hardness, Is.GreaterThan(rock.Hardness), $"bed {k} should be a hard cap");
					}
					else
					{
						Assert.That(hardness, Is.LessThan(rock.Hardness), $"bed {k} should be soft");
					}
				}
			}
			finally
			{
				Object.DestroyImmediate(body);
			}
		}
	}
}
