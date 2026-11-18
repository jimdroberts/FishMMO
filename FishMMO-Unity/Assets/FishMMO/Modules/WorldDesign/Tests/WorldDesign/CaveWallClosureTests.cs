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

		[Test]
		public void ALinkAlongASteepHillsideKeepsItsDownhillWallUnderTheRock()
		{
			// 2026-10-26 (Moron 2, a link joining two caves under a 60° hillside): the link walker tested the rock only over the
			// bore's middle, so a link run along the hill's contour kept 3–5 m over its middle and had its downhill wall 2 m out of
			// the ground. The shell stops a skin under the ground, so there the cave lay open to the sky through a hole metres
			// across. Walked along a face rising 2 m a metre across its way, every point of its air stays under the rock's skin.
			CaveSystem system = PlanCave(CaveForm.Cave, 1, 1, out _);
			Assert.That(system, Is.Not.Null);
			Type endType = typeof(CaveSystem).GetNestedType("LinkEnd", BindingFlags.NonPublic);
			Assert.That(endType, Is.Not.Null, "CaveSystem.LinkEnd");
			MethodInfo endOf = endType.GetMethod("Of", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(CaveNode).MakeByRefType(), typeof(Vector3) }, null);
			MethodInfo walkTo = typeof(CaveSystem).GetMethod("WalkTo", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(endOf, Is.Not.Null, "LinkEnd.Of(node, run)");
			Assert.That(walkTo, Is.Not.Null, "CaveSystem.WalkTo");

			// Far off the planned cave (its own void plays no part), in its frame: a level way north between two ends deep under
			// the hill, the line between them 4.5 m under a face that rises 2 m a metre to the east.
			const float slope = 2f, middleCover = 4.5f, length = 150f;
			float r = Mathf.Max(CaveSystem.MinBore, system.Radius * 0.8f);
			var x0 = new Vector3(1500f, -30f, 0f);
			float floor = x0.y;
			float centreY = floor + CaveSystem.FloorDepth * r * system.Flat;
			float top = centreY + r * system.Flat + system.Amp;
			Vector3 o = system.Origin;
			float GroundLocal(float x, float z)
			{
				float fromEnd = Mathf.Min(z - x0.z, x0.z + length - z);
				return Mathf.Max(top + middleCover + slope * (x - x0.x), top + 40f - 3f * Mathf.Max(0f, fromEnd - 20f));
			}
			Func<float, float, float> g = (wx, wz) => GroundLocal(wx - o.x, wz - o.z) + o.y;
			CaveGround ground = CaveGround.Sample(g, o.x + x0.x - 80f, o.z + x0.z - 80f, o.x + x0.x + 80f, o.z + x0.z + length + 80f, 2f);
			var start = new CaveNode { Centre = new Vector3(x0.x, centreY, x0.z), Radius = r, Floor = floor, Along = 0f };
			var finish = new CaveNode { Centre = new Vector3(x0.x, centreY, x0.z + length), Radius = r, Floor = floor, Along = 0f };
			object end = endOf.Invoke(null, new object[] { finish, Vector3.forward });
			Func<Vector3, float, bool> blocked = (_, _) => false;
			var args = new object[] { start, 0f, end, 3f * length, new FishMMO.Shared.DeterministicRNG(7), ground, blocked, -1e4f, float.NaN, 0f, 0f, null };
			var walk = (List<CaveNode>)walkTo.Invoke(system, args);
			Assert.That(walk, Is.Not.Null, $"a way along the face still fits, into the hill ({args[11]})");

			// The way's own air (its ends are given, laid by the test on the face): nowhere above the ground less the skin.
			var link = new CavePassage { Kind = CavePassageKind.Link };
			link.Nodes.AddRange(walk);
			system.Passages.Add(link);
			system.Prepare();
			float clear = 2f * r + 2f, reach = r + system.Amp + 2f;
			int open = 0, columns = 0;
			float worst = float.NegativeInfinity;
			Vector3 at = default;
			var seen = new HashSet<Vector2Int>();
			foreach (CaveNode n in walk)
			{
				for (float x = n.Centre.x - reach; x <= n.Centre.x + reach; x += 0.5f)
				{
					for (float z = n.Centre.z - reach; z <= n.Centre.z + reach; z += 0.5f)
					{
						var cell = new Vector2Int(Mathf.RoundToInt(x * 2f), Mathf.RoundToInt(z * 2f));
						var p = new Vector3(cell.x * 0.5f, 0f, cell.y * 0.5f);
						if (!seen.Add(cell) || new Vector2(p.x - start.Centre.x, p.z - start.Centre.z).magnitude < clear
							|| new Vector2(p.x - finish.Centre.x, p.z - finish.Centre.z).magnitude < clear)
						{
							continue;
						}
						columns++;
						float skin = GroundLocal(p.x, p.z) - system.Skin;
						// The highest air in the column over the skin.
						for (float y = skin + system.Skin + 4f; y >= skin; y -= 0.1f)
						{
							p.y = y;
							if (system.Void(p) < 0f)
							{
								open++;
								if (y - skin > worst)
								{
									worst = y - skin;
									at = p;
								}
								break;
							}
						}
					}
				}
			}
			float east = 0f;
			foreach (CaveNode n in walk)
			{
				east = Mathf.Max(east, n.Centre.x - x0.x);
			}
			Console.WriteLine($"{walk.Count} nodes, into the hill up to {east:F1} m; {columns} columns, {open} with air over the rock's skin"
				+ (open > 0 ? $" (worst {worst:F2} m over it at {at:F1})" : ""));
			Assert.That(columns, Is.GreaterThan(100));
			Assert.That(open, Is.Zero, "no air of the link over the rock's skin: the shell is never open to the sky along it");
		}
	}
}
