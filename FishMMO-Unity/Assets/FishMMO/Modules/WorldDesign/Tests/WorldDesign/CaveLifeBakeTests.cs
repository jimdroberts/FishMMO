using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The cave-life spots the cut bakes from a cave system's field (<see cref="CaveLifeBake"/>): the same spots for the same
	/// system however it is rebuilt, every patch on the rock with air over it, every box of flight clear, a mouth's run in clear,
	/// a grotto's pool under water, and the dark reckoned from the mouths.
	/// </summary>
	[TestFixture]
	public class CaveLifeBakeTests
	{
		/// <summary>A foot at 70 m, a face rising 60 m over 30 m, a rolling plateau behind (PointOfInterestCaveTests' escarpment, lifted over the sea).</summary>
		private static Func<float, float, float> Escarpment(float tiltDegrees, int variant)
		{
			float a = tiltDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			return (x, z) =>
			{
				float u = c * z - s * x, w = s * z + c * x;
				float t = Mathf.Clamp01(u / 30f);
				float face = 10f + 60f * (t * t * (3f - 2f * t));
				float roll = 3f * Mathf.Sin(w * 0.05f + variant) + 2f * Mathf.Sin(u * 0.031f + 0.7f * variant) * Mathf.Clamp01(u / 40f);
				return 60f + face + roll * Mathf.Clamp01((u + 10f) / 30f) - 0.04f * Mathf.Max(0f, -u);
			};
		}

		/// <summary>A sea cliff: sea floor at −1.5 m past z −12, a beach rising to 1 m at z −2, a cliff to 40 m by z 25.</summary>
		private static Func<float, float, float> SeaCliff()
			=> (x, z) =>
			{
				float t = Mathf.Clamp01(z / 25f);
				return -1.5f + 2.5f * Mathf.Clamp01((z + 12f) / 10f) + 39f * (t * t * (3f - 2f * t)) + 0.8f * Mathf.Sin(x * 0.07f) * Mathf.Clamp01(z / 10f);
			};

		private static CaveSystem Plan(CaveForm form, int size, int seed)
		{
			float tilt = seed * 13f % 40f - 20f;
			bool sea = form == CaveForm.SeaCave || form == CaveForm.Grotto;
			Func<float, float, float> g = sea ? SeaCliff() : Escarpment(tilt, seed);
			CaveGround ground = CaveGround.Sample(g, -560f, -560f, 560f, 560f, 2f);
			CaveSystem system = CaveSystem.Plan(form, size, new Vector3(0f, 0f, sea ? -4f : -1f), sea ? 180f : Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed, ground, 0f,
				out string problem, form == CaveForm.Grotto ? 0f : float.NaN);
			Assert.That(system, Is.Not.Null, $"{form} size {size} seed {seed} was refused: {problem}");
			// As the dressing reads it: rebuilt from the stored shape.
			return CaveSystem.FromValues(system.ToValues(), system.Origin, system.Seed);
		}

		private static readonly (CaveForm Form, int Size, int Seed)[] Systems =
		{
			(CaveForm.Cave, 1, 3), (CaveForm.Cave, 2, 6), (CaveForm.Grotto, 1, 2), (CaveForm.LavaTube, 1, 4), (CaveForm.Mine, 1, 5),
		};

		private static List<(CaveSystem System, List<SceneCaveLifeSpot> Spots)> baked;

		private static List<(CaveSystem System, List<SceneCaveLifeSpot> Spots)> Baked()
		{
			if (baked == null)
			{
				baked = new List<(CaveSystem, List<SceneCaveLifeSpot>)>();
				foreach ((CaveForm form, int size, int seed) in Systems)
				{
					CaveSystem system = Plan(form, size, seed);
					baked.Add((system, CaveLifeBake.Bake(system, seed)));
				}
			}
			return baked;
		}

		[Test]
		public void TheFormsAreTheCutsByTheirOrdinals()
		{
			foreach (CaveForm form in Enum.GetValues(typeof(CaveForm)))
			{
				Assert.That(CaveLifeBake.FormOf(form).ToString(), Is.EqualTo(form.ToString()), $"{form}");
			}
		}

		[Test]
		public void TheSameSystemBakesTheSameSpots()
		{
			foreach ((CaveSystem system, List<SceneCaveLifeSpot> spots) in Baked())
			{
				CaveSystem again = CaveSystem.FromValues(system.ToValues(), system.Origin, system.Seed);
				List<SceneCaveLifeSpot> twice = CaveLifeBake.Bake(again, system.Seed);
				Assert.That(twice.Count, Is.EqualTo(spots.Count), $"{system.Form}");
				for (int i = 0; i < spots.Count; i++)
				{
					Assert.That(twice[i].Kind, Is.EqualTo(spots[i].Kind));
					Assert.That(twice[i].Position, Is.EqualTo(spots[i].Position), $"{system.Form} spot {i}");
					Assert.That(twice[i].Extent, Is.EqualTo(spots[i].Extent));
					Assert.That(twice[i].Flags, Is.EqualTo(spots[i].Flags));
				}
				Assert.That(spots.Count, Is.GreaterThan(5).And.LessThanOrEqualTo(CaveLifeBake.MaxSpots), $"{system.Form}: a cave with a few homes in it");
				foreach (SceneCaveLifeSpot spot in spots)
				{
					Assert.That(spot.Form, Is.EqualTo(CaveLifeBake.FormOf(system.Form)));
					Assert.That(spot.Dark, Is.InRange(0f, 1f));
				}
			}
		}

		[Test]
		public void EveryPatchLiesOnTheRockWithAirOverIt()
		{
			int checkedPatches = 0;
			foreach ((CaveSystem system, List<SceneCaveLifeSpot> spots) in Baked())
			{
				foreach (SceneCaveLifeSpot spot in spots)
				{
					if (spot.Kind != SceneCaveLifeKind.Floor && spot.Kind != SceneCaveLifeKind.Wall && spot.Kind != SceneCaveLifeKind.Roost)
					{
						continue;
					}
					Vector3 n = spot.Normal.normalized;
					if (spot.Kind == SceneCaveLifeKind.Floor)
					{
						Assert.That(n.y, Is.GreaterThan(Mathf.Cos(36f * Mathf.Deg2Rad)), "a floor patch is walkable");
					}
					else if (spot.Kind == SceneCaveLifeKind.Wall)
					{
						Assert.That(Mathf.Abs(n.y), Is.LessThanOrEqualTo(0.46f), "a wall patch stands up");
					}
					else
					{
						Assert.That(n.y, Is.LessThan(-0.59f), "a roost faces down");
					}
					Vector3 t1 = Vector3.Cross(Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up, n).normalized, t2 = Vector3.Cross(n, t1);
					for (int k = 0; k <= 8; k++)
					{
						float a = k * Mathf.PI / 4f, r = k == 8 ? 0f : 0.85f * spot.Extent.x;
						Vector3 p = spot.Position - system.Origin + (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * r;
						Assert.That(system.Void(p + n * 0.15f), Is.LessThan(0f), $"{system.Form} {spot.Kind} at {spot.Position}: air over the patch");
						Assert.That(system.Void(p - n * 0.15f), Is.GreaterThanOrEqualTo(0f), $"{system.Form} {spot.Kind} at {spot.Position}: rock under the patch");
					}
					checkedPatches++;
				}
			}
			Assert.That(checkedPatches, Is.GreaterThan(20));
		}

		[Test]
		public void EveryBoxOfAirIsClearAndEveryMouthsRunIn()
		{
			bool flight = false, mouth = false, deep = false;
			foreach ((CaveSystem system, List<SceneCaveLifeSpot> spots) in Baked())
			{
				foreach (SceneCaveLifeSpot spot in spots)
				{
					Vector3 local = spot.Position - system.Origin;
					if (spot.Kind == SceneCaveLifeKind.Flight || spot.Kind == SceneCaveLifeKind.Glow)
					{
						float r = spot.Yaw * Mathf.Deg2Rad;
						var along = new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
						var across = new Vector3(along.z, 0f, -along.x);
						for (int i = -1; i <= 1; i++)
						{
							for (int j = -1; j <= 1; j++)
							{
								for (int k = -1; k <= 1; k++)
								{
									Vector3 q = local + along * (i * spot.Extent.x) + Vector3.up * (j * spot.Extent.y) + across * (k * spot.Extent.z);
									Assert.That(system.Void(q), Is.LessThan(0f), $"{system.Form} {spot.Kind} at {spot.Position}: a corner in the rock");
								}
							}
						}
						flight |= spot.Kind == SceneCaveLifeKind.Flight;
					}
					else if (spot.Kind == SceneCaveLifeKind.Mouth)
					{
						Assert.That(spot.Dark, Is.Zero, "a mouth is the daylight");
						Assert.That(spot.Extent.z, Is.GreaterThanOrEqualTo(4f));
						for (float d = 0f; d <= spot.Extent.z; d += 0.5f)
						{
							Assert.That(system.Void(local - spot.Normal * d), Is.LessThan(0f), $"{system.Form}: the mouth's run in meets rock {d} m in");
						}
						mouth = true;
					}
					deep |= spot.Dark > 0.99f;
				}
			}
			Assert.That(flight, Is.True, "some cave has air to fly in");
			Assert.That(mouth, Is.True, "some mouth to stream out of");
			Assert.That(deep, Is.True, "some spot past the twilight");
		}

		[Test]
		public void AGrottosPoolIsUnderItsWaterAndNoFloorIs()
		{
			(CaveSystem grotto, List<SceneCaveLifeSpot> spots) = Baked().Find(b => b.System.Form == CaveForm.Grotto);
			bool pool = false;
			foreach (SceneCaveLifeSpot spot in spots)
			{
				if (spot.Kind == SceneCaveLifeKind.Pool)
				{
					Assert.That(spot.Position.y, Is.EqualTo(grotto.WaterLevel).Within(1e-3f), "at the water's surface");
					Assert.That(spot.Extent.y, Is.GreaterThanOrEqualTo(0.3f), "deep enough to swim in");
					Assert.That(spot.Flags & SceneCaveLifeFlags.Damp, Is.Not.EqualTo(SceneCaveLifeFlags.None));
					pool = true;
				}
				else if (spot.Kind == SceneCaveLifeKind.Floor)
				{
					Assert.That(spot.Position.y, Is.GreaterThan(grotto.WaterLevel), "no floor patch under the water");
				}
			}
			Assert.That(pool, Is.True, "a grotto has its pool");
		}
	}
}
