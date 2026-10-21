using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A cave's columns (CaveEnclosure, 2026-10-21: "Lighting in cave seems off ... seems to be based on distance", Moron 2's
	/// lava tube, a hall under a hillside that falls away lit grey at its far end): the air deep in every cave reads as in it,
	/// however thin the rock over it; the hillside over it, and the ground before its mouths, never does (past a trace). On the same
	/// synthetic escarpment as CaveWallClosureTests.
	/// </summary>
	[TestFixture]
	public class CaveEnclosureTests
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

		[Test]
		public void TheColumnsKeepThroughTheirBytes()
		{
			var e = new CaveEnclosure(-40, 12, 9, 5);
			var rng = new System.Random(7);
			var set = new Dictionary<(int, int), (float, float, float)>();
			for (int k = 0; k < 20; k++)
			{
				int x = -40 + rng.Next(9), z = 12 + rng.Next(5);
				float floor = 300f + (float)rng.NextDouble() * 200f, roof = floor + (float)rng.NextDouble() * 80f, shut = (float)rng.NextDouble();
				e.Set(x, z, floor, roof, shut);
				set[(x, z)] = (floor, roof, shut);
			}
			CaveEnclosure back = CaveEnclosure.Decode(e.Encode());
			Assert.That(back, Is.Not.Null);
			Assert.That(back.Count, Is.EqualTo(set.Count));
			foreach (var kv in set)
			{
				Assert.That(back.TryColumn(kv.Key.Item1, kv.Key.Item2, out float floor, out float roof, out float shut), Is.True);
				Assert.That(floor, Is.LessThanOrEqualTo(kv.Value.Item1 + 1e-3f).And.GreaterThan(kv.Value.Item1 - 0.11f), "a floor is kept, rounded down");
				Assert.That(roof, Is.GreaterThanOrEqualTo(kv.Value.Item2 - 1e-3f).And.LessThan(kv.Value.Item2 + 0.11f), "a roof is kept, rounded up");
				Assert.That(shut, Is.EqualTo(kv.Value.Item3).Within(0.5f / 255f + 1e-4f));
			}
			Assert.That(CaveEnclosure.Decode(new byte[3]), Is.Null);
		}

		[Test]
		public void DeepInIsInTheCave_TheHillsideOverItNever()
		{
			int caves = 0, deepNodes = 0, lit = 0, ground = 0, darkened = 0;
			float worstGround = 0f;
			string worstAt = "", litAt = "";
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
						Bounds b = system.Extent;
						CaveGround sampled = CaveGround.Sample(g, system.Origin.x + b.min.x - 4f, system.Origin.z + b.min.z - 4f, system.Origin.x + b.max.x + 4f,
							system.Origin.z + b.max.z + 4f, HoleStep, GridX, GridZ);
						var pieces = new List<(IList<Vector3>, IList<int>)>();
						foreach (CaveMesher.Piece piece in CaveMesher.Build(system, sampled, out _))
						{
							pieces.Add((piece.Mesh.Positions, piece.Mesh.Submeshes[0]));
						}
						CaveEnclosure e = system.EnclosureOf(pieces);
						caves++;
						int caveDeep = 0, caveLit = 0, caveDark = 0;

						// The air deep in: every passage node well past every mouth's reach, a man's height over its floor.
						foreach (CavePassage passage in system.Passages)
						{
							foreach (CaveNode n in passage.Nodes)
							{
								Vector3 p = n.Centre + system.Origin;
								bool deep = true;
								foreach (CaveMouth m in system.Mouths)
								{
									float reach = m.Kind == CaveMouthKind.Skylight ? 1.5f * m.Radius + 14f : m.Zone + 2f * m.Radius + 18f;
									if ((p - (m.Floor + system.Origin)).magnitude < reach)
									{
										deep = false;
									}
								}
								if (!deep || system.Void(n.Centre) >= -0.5f)
								{
									continue;
								}
								caveDeep++;
								if (e.At(p) < 0.99f)
								{
									caveLit++;
									litAt = $"{form} size {size} seed {seed} at {p:F1}: {e.At(p):F2}";
								}
							}
						}

						// The ground over every column, and anything a few metres tall standing on it.
						for (int z = e.Z0; z < e.Z0 + e.Depth; z++)
						{
							for (int x = e.X0; x < e.X0 + e.Width; x++)
							{
								for (int sub = 0; sub < 4; sub++)
								{
									float wx = (x + 0.25f + 0.5f * (sub & 1)) * CaveEnclosure.Cell, wz = (z + 0.25f + 0.5f * (sub >> 1)) * CaveEnclosure.Cell;
									float lx = wx - system.Origin.x, lz = wz - system.Origin.z;
									float terrain = g(wx, wz);
									// Not where the mouth's holes cut the ground away: there the ground is the cave's own floor.
									if (system.Void(new Vector3(lx, terrain - system.Origin.y - 0.5f, lz)) < 1f)
									{
										continue;
									}
									ground++;
									float worst = Mathf.Max(e.At(new Vector3(wx, terrain, wz)), e.At(new Vector3(wx, terrain + 2f, wz)));
									if (worst > 0.01f)
									{
										caveDark++;
										if (worst > worstGround)
										{
											worstGround = worst;
											worstAt = $"{form} size {size} seed {seed} at ({wx:F1}, {terrain:F1}, {wz:F1}): {worst:F2}";
										}
									}
								}
							}
						}
						deepNodes += caveDeep;
						lit += caveLit;
						darkened += caveDark;
						report.Add($"{form} size {size} seed {seed}: {e.Count} columns, {caveDeep} deep nodes ({caveLit} not in the cave), {caveDark} ground points darkened");
					}
				}
			}
			foreach (string line in report)
			{
				Console.WriteLine(line);
			}
			Console.WriteLine($"{caves} caves: {deepNodes} deep nodes, {lit} not in the cave{(lit > 0 ? " (" + litAt + ")" : "")}; {ground} ground points, {darkened} darkened{(darkened > 0 ? " (worst " + worstAt + ")" : "")}");
			Assert.That(caves, Is.GreaterThan(10));
			Assert.That(deepNodes, Is.GreaterThan(200));
			Assert.That(lit, Is.Zero, "the air deep in a cave reads as in it");
			// A trace only, at a mouth's edge, where its collar's rock comes up to the ground and the closure has barely begun
			// to rise (2026-10-21: 67 of 427k points, the most 0.03).
			Assert.That(worstGround, Is.LessThan(0.05f), "the hillside over a cave never reads as in it, past a trace at a mouth's edge");
		}
	}
}
