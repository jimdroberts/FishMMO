using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The rock of a waterfall's face (<see cref="FallLedges"/>): ledges stand out of the face between the lip and the
	/// pool, never in the pool and never past the water's sides, no one rock wide enough to dam the fall, more of them in
	/// bedded rock than in massive, the same every time for a seed, and none at all on a drop too small to be a fall.
	/// </summary>
	[TestFixture]
	public class FallLedgeTests
	{
		private const float Spacing = 2f;
		private const int LipIndex = 15;
		private const float Width = 8f;
		private const float LipSurface = 50f;
		private const float Depth = 1f;

		/// <summary>
		/// A straight river along +x every 2 m, 8 m wide and a metre deep, level at 50 m to its lip (point 15), falling
		/// <paramref name="drop"/> over point 16 to its foot (point 17) and level after: a knickpoint step.
		/// </summary>
		private static SceneWater Water(float drop)
		{
			const int n = 40;
			var river = new RiverPath
			{
				Id = 5,
				X = new float[n], Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Surface = new float[n], Bed = new float[n], Curvature = new float[n], Reach = new RiverReach[n],
			};
			for (int i = 0; i < n; i++)
			{
				river.X[i] = i * Spacing;
				river.S[i] = i * Spacing;
				river.Discharge[i] = 6f;
				river.Width[i] = Width;
				river.Depth[i] = Depth;
				river.Surface[i] = i <= LipIndex ? LipSurface : i == LipIndex + 1 ? LipSurface - 0.9f * drop : LipSurface - drop;
				river.Bed[i] = river.Surface[i] - Depth;
				river.Reach[i] = i == LipIndex + 1 ? RiverReach.Fall : RiverReach.Run;
			}
			var water = new SceneWater();
			water.Rivers.Add(river);
			return water;
		}

		/// <summary>
		/// The ground of <see cref="Water"/>: the lip's bed a metre under its water, a sheer step a metre past the lip
		/// (half a metre's run, as a heightfield blurs it), and the pool's floor three metres under its water.
		/// </summary>
		private static Func<float, float, float> Ground(float drop)
		{
			float lipX = LipIndex * Spacing;
			float top = LipSurface - Depth, bottom = LipSurface - drop - 3f;
			return (x, z) =>
			{
				float t = Mathf.Clamp01((x - (lipX + 1f)) / 0.5f);
				return Mathf.Lerp(top, bottom, t);
			};
		}

		private static Func<float, float, float, string> Rock(string name) => (x, y, z) => name;

		private static readonly float[] Drops = { 3f, 8f, 20f, 45f };

		[Test]
		public void TheSameSeedPlacesTheSameRock()
		{
			foreach (float drop in Drops)
			{
				SceneWater water = Water(drop);
				List<FallLedge> a = FallLedges.Plan(water, Ground(drop), Rock("Sandstone"), 77u);
				List<FallLedge> b = FallLedges.Plan(water, Ground(drop), Rock("Sandstone"), 77u);
				Assert.That(b.Count, Is.EqualTo(a.Count));
				for (int i = 0; i < a.Count; i++)
				{
					Assert.That(b[i].Kind, Is.EqualTo(a[i].Kind));
					Assert.That(b[i].Position, Is.EqualTo(a[i].Position), "the same seed places it the same");
					Assert.That(b[i].Size, Is.EqualTo(a[i].Size));
					Assert.That(b[i].Prefab, Is.EqualTo(a[i].Prefab));
				}
			}
		}

		[Test]
		public void EveryLedgeStandsOnTheFaceBetweenLipAndFootWithinTheFallsSides()
		{
			float run = 2f * Spacing;
			int ledges = 0;
			foreach (float drop in Drops)
			{
				float pool = LipSurface - drop;
				for (uint seed = 1; seed <= 40; seed++)
				{
					foreach (FallLedge rock in FallLedges.Plan(Water(drop), Ground(drop), Rock("Limestone"), seed))
					{
						Assert.That(Mathf.Abs(rock.Across) + 0.5f * rock.Size.x, Is.LessThanOrEqualTo(0.5f * Width + 2f + 1e-3f),
							$"{rock.Kind} beyond the water's sides and two metres");
						Assert.That(rock.Position.z, Is.EqualTo(rock.Across).Within(1e-3f), "across is measured from the line through the lip");
						if (rock.Kind != FallRockKind.Ledge)
						{
							continue;
						}
						ledges++;
						Assert.That(rock.FrontAlong, Is.InRange(0f, run), "its front stands between the lip and the foot");
						float top = rock.Position.y + 0.5f * rock.Size.y;
						Assert.That(top, Is.LessThanOrEqualTo(LipSurface - Depth), "under the lip's bed: on the face, not in the channel above");
						Assert.That(top, Is.GreaterThan(pool), "over the pool");
						Assert.That(rock.Position.x - LipIndex * Spacing + 0.5f * rock.Size.z, Is.EqualTo(rock.FrontAlong).Within(1e-3f), "its front is its middle and half its depth");
						// Mostly inside the rock: no more than its out-share stands out of the face (the step a metre past the lip).
						float face = 1f + 0.5f * Mathf.Clamp01((LipSurface - Depth - top) / (LipSurface - Depth - (LipSurface - drop - 3f)));
						float outside = rock.FrontAlong - face;
						Assert.That(outside, Is.InRange(0.4f - 0.3f, 1.5f + 0.3f), "it stands out of the face by what the water strikes");
						Assert.That(outside, Is.LessThanOrEqualTo(0.5f * rock.Size.z + 0.3f), "most of it is inside the rock");
					}
				}
			}
			Assert.That(ledges, Is.GreaterThan(20), "bedded falls are given ledges");
		}

		[Test]
		public void NoRockStandsInThePlungePool()
		{
			foreach (float drop in Drops)
			{
				float pool = LipSurface - drop;
				for (uint seed = 1; seed <= 40; seed++)
				{
					foreach (string rockType in new[] { "Sandstone", "Basalt", "Granite" })
					{
						foreach (FallLedge rock in FallLedges.Plan(Water(drop), Ground(drop), Rock(rockType), seed))
						{
							// A lip boulder's position is its foot; a ledge's or an overhang's its middle.
							float underside = rock.Kind == FallRockKind.LipBoulder ? rock.Position.y : rock.Position.y - 0.5f * rock.Size.y;
							Assert.That(underside, Is.GreaterThanOrEqualTo(pool), $"a {rock.Kind} in {rockType} stands in the pool");
						}
					}
				}
			}
		}

		[Test]
		public void NoOneLedgeIsWiderThanEightTenthsOfTheFall()
		{
			int split = 0;
			foreach (float drop in Drops)
			{
				for (uint seed = 1; seed <= 60; seed++)
				{
					var perBand = new Dictionary<float, int>();
					foreach (FallLedge rock in FallLedges.Plan(Water(drop), Ground(drop), Rock("Sandstone"), seed))
					{
						if (rock.Kind != FallRockKind.Ledge)
						{
							continue;
						}
						Assert.That(rock.Size.x, Is.LessThanOrEqualTo(0.8f * Width + 1e-3f), "one rock wide enough to dam the fall");
						float key = Mathf.Round((rock.Position.y + 0.5f * rock.Size.y) * 1000f);
						perBand[key] = perBand.TryGetValue(key, out int c) ? c + 1 : 1;
					}
					foreach (int c in perBand.Values)
					{
						if (c > 1)
						{
							split++;
						}
					}
				}
			}
			Assert.That(split, Is.GreaterThan(0), "a ledge wider than one rock may be is laid as several");
		}

		[Test]
		public void AFallUnderOneAndAHalfMetresGetsNoRock()
		{
			for (uint seed = 1; seed <= 20; seed++)
			{
				Assert.That(FallLedges.Plan(Water(1.2f), Ground(1.2f), Rock("Sandstone"), seed), Is.Empty, "a 1.2 m drop is a rapid, not a fall");
			}
		}

		[Test]
		public void BeddedRockGetsMoreLedgesThanMassiveRock()
		{
			int Ledges(string rockType)
			{
				int count = 0;
				for (uint seed = 1; seed <= 40; seed++)
				{
					foreach (FallLedge rock in FallLedges.Plan(Water(20f), Ground(20f), Rock(rockType), seed))
					{
						count += rock.Kind == FallRockKind.Ledge ? 1 : 0;
					}
				}
				return count;
			}
			int bedded = Ledges("Sandstone"), massive = Ledges("Granite");
			Assert.That(bedded, Is.GreaterThan(massive), $"sandstone {bedded} ledges, granite {massive}");
		}

		[Test]
		public void OnlyAHardCapOverAPlungeFallOverhangs()
		{
			int Overhangs(Func<float, float, float, string> rockTypeAt)
			{
				int count = 0;
				for (uint seed = 1; seed <= 20; seed++)
				{
					foreach (FallLedge rock in FallLedges.Plan(Water(20f), Ground(20f), rockTypeAt, seed))
					{
						if (rock.Kind == FallRockKind.Overhang)
						{
							count++;
							Assert.That(rock.Position.y + 0.5f * rock.Size.y, Is.LessThanOrEqualTo(LipSurface - Depth),
								"its top under the lip's bed: the river runs on over it");
						}
					}
				}
				return count;
			}
			// Basalt over shale: the cap stands, the shale is scoured back under it.
			Assert.That(Overhangs((x, y, z) => y > LipSurface - Depth - 1f ? "Basalt" : "Shale"), Is.EqualTo(20), "one overhang per capped plunge fall");
			Assert.That(Overhangs(Rock("Granite")), Is.Zero, "massive rock has no cap");
			Assert.That(Overhangs(Rock("Sandstone")), Is.Zero, "nor does bedded sandstone");
		}
	}
}
