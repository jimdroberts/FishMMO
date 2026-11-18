using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// A cave's low glow air and the glow patches by it, as the dressing places them (split from AmbientGlowLifeTests,
	/// Phase 4, 2026-11-18).
	/// </summary>
	[TestFixture]
	public class CaveGlowAirTests
	{
		/// <summary>CaveLifeBakeTests' escarpment: a foot at 70 m, a face rising 60 m over 30 m, a rolling plateau behind.</summary>
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

		private static CaveSystem Plan(CaveForm form, int size, int seed)
		{
			float tilt = seed * 13f % 40f - 20f;
			CaveGround ground = CaveGround.Sample(Escarpment(tilt, seed), -560f, -560f, 560f, 560f, 2f);
			CaveSystem system = CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed, ground, 0f, out string problem, float.NaN);
			Assert.That(system, Is.Not.Null, $"{form} size {size} seed {seed} was refused: {problem}");
			return CaveSystem.FromValues(system.ToValues(), system.Origin, system.Seed);
		}

		[Test]
		public void ACaveHasLowGlowAirAndThePatchesByIt()
		{
			int low = 0, boxes = 0;
			foreach ((CaveForm form, int size, int seed) in new[] { (CaveForm.Cave, 1, 3), (CaveForm.Cave, 2, 6), (CaveForm.Mine, 1, 5) })
			{
				CaveSystem system = Plan(form, size, seed);
				List<SceneCaveLifeSpot> spots = CaveLifeBake.Bake(system, seed);
				List<SceneCaveLifeSpot> again = CaveLifeBake.Bake(CaveSystem.FromValues(system.ToValues(), system.Origin, system.Seed), seed);
				Assert.That(again.Count, Is.EqualTo(spots.Count), $"{form}: the same homes every bake");
				Assert.That(spots.Count, Is.LessThanOrEqualTo(CaveLifeBake.MaxSpots));
				for (int i = 0; i < spots.Count; i++)
				{
					Assert.That(again[i].Position, Is.EqualTo(spots[i].Position), $"{form} spot {i}");
					if (spots[i].Kind != SceneCaveLifeKind.Glow)
					{
						continue;
					}
					boxes++;
					// How high over the floor under it the box's middle hangs.
					Vector3 local = spots[i].Position - system.Origin;
					float floor = local.y;
					while (floor > local.y - 40f && system.Void(new Vector3(local.x, floor - 0.1f, local.z)) < 0f)
					{
						floor -= 0.1f;
					}
					float over = local.y - floor;
					low += over <= 4f ? 1 : 0;
					Assert.That(over, Is.GreaterThan(spots[i].Extent.y), $"{form}: glow air clear of the floor");
				}
			}
			Assert.That(boxes, Is.GreaterThan(0), "some glow air");
			Assert.That(low, Is.GreaterThan(2), "glow air low over the floor, where it is seen");
		}
	}
}
