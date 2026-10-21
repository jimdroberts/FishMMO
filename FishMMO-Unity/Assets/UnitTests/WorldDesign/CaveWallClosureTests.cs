using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The rock as the cut ships it, buried faces dropped, has no hole a player in the cave can see through (2026-10-21: "We see
	/// issues around these coordinates with cave walls", a lava tube's branch beside a cavern on Moron 2): every open edge left by
	/// the cull lies on a face that looks out at the hill's rock, none on a face that looks out at the cave. On the same synthetic
	/// escarpment as PointOfInterestCaveTests.
	/// </summary>
	[TestFixture]
	public class CaveWallClosureTests
	{
		private const float CaveLift = 60f;
		private const float HoleStep = 1200f / 512f, GridX = -2400f, GridZ = -2400f;

		private static Func<float, float, float> Escarpment(float tiltDegrees, int variant)
		{
			float a = tiltDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			return (x, z) =>
			{
				float u = c * z - s * x, w = s * z + c * x;
				float t = Mathf.Clamp01(u / 30f);
				float face = 10f + 60f * (t * t * (3f - 2f * t));
				float roll = 3f * Mathf.Sin(w * 0.05f + variant) + 2f * Mathf.Sin(u * 0.031f + 0.7f * variant) * Mathf.Clamp01(u / 40f);
				return face + roll * Mathf.Clamp01((u + 10f) / 30f) - 0.04f * Mathf.Max(0f, -u);
			};
		}

		private static CaveSystem PlanCave(CaveForm form, int size, int seed, out Func<float, float, float> g)
		{
			float tilt = seed * 13f % 40f - 20f;
			Func<float, float, float> face = Escarpment(tilt, seed);
			g = (px, pz) => face(px, pz) + CaveLift;
			return CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed,
				CaveGround.Sample(g, -560f, -560f, 560f, 560f, 2f), 0f, out _);
		}

		/// <summary>Open edges of the shipped rock whose face looks out at the cave's air: holes seen from inside.</summary>
		private static int SeenHoles(CaveSystem system, List<CaveMesher.Piece> pieces, out Vector3 where)
		{
			var faces = new Dictionary<(Vector3, Vector3), (int Count, Vector3 Centre, Vector3 Normal)>();
			foreach (CaveMesher.Piece piece in pieces)
			{
				List<Vector3> v = piece.Mesh.Positions;
				List<int> t = piece.Mesh.Submeshes[0];
				for (int i = 0; i < t.Count; i += 3)
				{
					Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
					Vector3 centre = (a + b + c) / 3f, n = Vector3.Cross(b - a, c - a);
					n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.zero;
					void Edge(Vector3 p, Vector3 q)
					{
						var key = (p.x < q.x || (p.x == q.x && (p.y < q.y || (p.y == q.y && p.z < q.z)))) ? (p, q) : (q, p);
						faces.TryGetValue(key, out var e);
						faces[key] = (e.Count + 1, centre, n);
					}
					Edge(a, b);
					Edge(b, c);
					Edge(c, a);
				}
			}
			int holes = 0;
			where = default;
			float h = system.MeshCell;
			foreach (var e in faces.Values)
			{
				if ((e.Count & 1) == 1 && (system.Void(e.Centre + e.Normal * (0.5f * h)) < 0f || system.Void(e.Centre + e.Normal * (1.5f * h)) < 0f))
				{
					holes++;
					where = e.Centre;
				}
			}
			return holes;
		}

		[Test]
		public void TheArchsPullFadesOut_NeverLetsGoWithAStep()
		{
			// 2026-10-21 (Moron 2's lava tube): the mouth's arch (CaveSystem.ArchAt, read through by everything that reads the
			// field) drew points in toward a narrow second mouth from 30 m along its face and let them go a step further on, so the
			// void jumped by metres there, and a ghost of the passage beside it ended in a torn skin of rock. Round every mouth,
			// out past where the arch reaches, the pull moves smoothly: points a quarter metre apart are moved alike.
			MethodInfo archAt = typeof(CaveSystem).GetMethod("ArchAt", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(archAt, Is.Not.Null, "CaveSystem.ArchAt");
			int caves = 0, mouths = 0, steps = 0;
			float worst = 0f;
			string where = "";
			foreach (CaveForm form in new[] { CaveForm.Cave, CaveForm.IceCave, CaveForm.LavaTube, CaveForm.Mine })
			{
				foreach (int size in new[] { 1, 2 })
				{
					for (int seed = 1; seed <= 3; seed++)
					{
						CaveSystem system = PlanCave(form, size, seed, out _);
						if (system == null)
						{
							continue;
						}
						caves++;
						Vector3 Pull(Vector3 p) => (Vector3)archAt.Invoke(system, new object[] { p }) - p;
						foreach (CaveMouth m in system.Mouths)
						{
							mouths++;
							float outer = m.Zone + 6f * m.Radius + 4f;
							for (float x = -outer; x <= outer; x += 1.1f)
							{
								for (float z = -outer; z <= outer; z += 1.1f)
								{
									Vector3 p = m.Floor + new Vector3(x, 0.4f, z);
									Vector3 here = Pull(p);
									foreach (Vector3 step in new[] { Vector3.right, Vector3.forward })
									{
										float jump = (Pull(p + step * 0.25f) - here).magnitude;
										if (jump > 0.3f)
										{
											steps++;
											if (jump > worst)
											{
												worst = jump;
												where = $"{form} size {size} seed {seed}, mouth radius {m.Radius:F1}, at {p - m.Floor:F1} from its floor";
											}
										}
									}
								}
							}
						}
					}
				}
			}
			Console.WriteLine($"{caves} caves, {mouths} mouths: {steps} steps of the arch's pull over 0.3 m in a quarter metre (worst {worst:F2} m{(steps > 0 ? ", " + where : "")})");
			Assert.That(caves, Is.GreaterThan(10));
			Assert.That(steps, Is.Zero, "the arch's pull fades out, never lets go with a step");
		}

		private static bool Near(CaveSystem system, Vector3 p)
		{
			foreach (CaveMouth m in system.Mouths)
			{
				float reach = m.Zone + 2f * m.Radius + 6f;
				if ((p - m.Floor).sqrMagnitude < reach * reach)
				{
					return true;
				}
			}
			return false;
		}

		[Test]
		public void NoHoleIsSeenFromInside_AsTheCutShipsTheRock()
		{
			int caves = 0, total = 0;
			var report = new List<string>();
			foreach (CaveForm form in new[] { CaveForm.Cave, CaveForm.IceCave, CaveForm.LavaTube, CaveForm.Mine })
			{
				foreach (int size in new[] { 1, 2 })
				{
					for (int seed = 1; seed <= 2; seed++)
					{
						CaveSystem system = PlanCave(form, size, seed, out Func<float, float, float> g);
						if (system == null)
						{
							continue;
						}
						for (int m = 0; m < system.Mouths.Count; m++)
						{
							system.HoleCells(m, g, HoleStep, GridX, GridZ);
						}
						Bounds e = system.Extent;
						CaveGround ground = CaveGround.Sample(g, system.Origin.x + e.min.x - 4f, system.Origin.z + e.min.z - 4f, system.Origin.x + e.max.x + 4f,
							system.Origin.z + e.max.z + 4f, HoleStep, GridX, GridZ);
						int holes = SeenHoles(system, CaveMesher.Build(system, ground, out _), out Vector3 where);
						report.Add($"{form} size {size} seed {seed}: {holes} open edges seen from the cave{(holes > 0 ? $" (one at {where:F1})" : "")}");
						caves++;
						total += holes;
					}
				}
			}
			foreach (string line in report)
			{
				Console.WriteLine(line);
			}
			Assert.That(caves, Is.GreaterThan(10));
			Assert.That(total, Is.Zero, "no hole in the rock seen from inside the cave");
		}
	}
}
