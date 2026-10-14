using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The cave mesher's output to the bit and its rebuild key: the same system meshes to the same bytes every time
	/// (the buried-face test runs on worker threads), and the key a scene's caves asset keeps per cave
	/// (<see cref="CaveShaper.InputsKey"/>) changes with every input the mesher reads and with nothing else.
	/// </summary>
	[TestFixture]
	public class CaveMeshCacheTests
	{
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
				return face + roll * Mathf.Clamp01((u + 10f) / 30f) - 0.04f * Mathf.Max(0f, -u) + 60f;
			};
		}

		/// <summary>A planned system, rebuilt from its stored values with its cut read back, as CaveShaper.Place has it.</summary>
		private static CaveSystem Stored(CaveForm form, int size, int seed, out Func<float, float, float> g, out float[] values, out List<Vector2> cut)
		{
			float tilt = seed * 13f % 40f - 20f;
			g = Escarpment(tilt, seed);
			Func<float, float, float> ground = g;
			CaveSystem planned = CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed,
				CaveGround.Sample(ground, -560f, -560f, 560f, 560f, 2f), 0f, out string problem);
			Assert.That(planned, Is.Not.Null, $"{form} size {size} seed {seed} was refused: {problem}");
			List<Vector2Int> cells = planned.HoleCells(0, g, HoleStep, GridX, GridZ);
			cut = new List<Vector2>();
			foreach (Vector2Int c in cells)
			{
				cut.Add(new Vector2(GridX + (c.x + 0.5f) * HoleStep, GridZ + (c.y + 0.5f) * HoleStep));
			}
			values = planned.ToValues();
			CaveSystem system = CaveSystem.FromValues(values, planned.Origin, planned.Seed);
			system.UseCutHoles(0, cut, g, HoleStep, GridX, GridZ);
			return system;
		}

		private static CaveGround MeshGround(CaveSystem system, Func<float, float, float> g)
		{
			Bounds e = system.Extent;
			return CaveGround.Sample(g, system.Origin.x + e.min.x - 4f, system.Origin.z + e.min.z - 4f,
				system.Origin.x + e.max.x + 4f, system.Origin.z + e.max.z + 4f, HoleStep, GridX, GridZ);
		}

		/// <summary>Every bit the pieces carry into their meshes: keys, mouth flags, positions, normals, tangents, UVs, colours, triangles.</summary>
		private static ulong Bits(List<CaveMesher.Piece> pieces)
		{
			ulong h = 1469598103934665603UL;
			void Mix(long v)
			{
				unchecked
				{
					h ^= (ulong)v;
					h *= 1099511628211UL;
				}
			}
			void Float(float f) => Mix(BitConverter.ToInt32(BitConverter.GetBytes(f), 0));
			foreach (CaveMesher.Piece piece in pieces)
			{
				Mix(piece.Key.x);
				Mix(piece.Key.y);
				Mix(piece.Key.z);
				Mix(piece.Mouth ? 1 : 0);
				MeshBuilder m = piece.Mesh;
				for (int i = 0; i < m.Positions.Count; i++)
				{
					Float(m.Positions[i].x); Float(m.Positions[i].y); Float(m.Positions[i].z);
					Float(m.Normals[i].x); Float(m.Normals[i].y); Float(m.Normals[i].z);
					Float(m.Tangents[i].x); Float(m.Tangents[i].y); Float(m.Tangents[i].z); Float(m.Tangents[i].w);
					Float(m.UVs[i].x); Float(m.UVs[i].y);
					Mix(m.Colors[i].r | m.Colors[i].g << 8 | m.Colors[i].b << 16 | m.Colors[i].a << 24);
				}
				foreach (List<int> sub in m.Submeshes)
				{
					Mix(sub.Count);
					foreach (int i in sub)
					{
						Mix(i);
					}
				}
			}
			return h;
		}

		[Test]
		public void TheMesherGivesTheSameBitsEveryTime()
		{
			foreach ((CaveForm form, int size, int seed) in new[] { (CaveForm.Cave, 0, 4), (CaveForm.LavaTube, 0, 2), (CaveForm.Mine, 1, 3) })
			{
				CaveSystem system = Stored(form, size, seed, out Func<float, float, float> g, out _, out _);
				CaveGround ground = MeshGround(system, g);
				ulong first = Bits(CaveMesher.Build(system, ground, out string report));
				ulong second = Bits(CaveMesher.Build(system, ground, out _));
				Console.WriteLine($"{form} {size} {seed}: {first:X16} ({report})");
				Assert.That(second, Is.EqualTo(first), $"{form}: two builds of one system differ");
			}
		}

		[Test]
		public void TheRebuildKeyIsTheSameForTheSameInputs()
		{
			CaveSystem a = Stored(CaveForm.Cave, 0, 4, out Func<float, float, float> g, out _, out _);
			CaveSystem b = Stored(CaveForm.Cave, 0, 4, out _, out _, out _);
			string key = CaveShaper.InputsKey(7, a, MeshGround(a, g));
			Assert.That(key, Has.Length.EqualTo(64));
			Assert.That(CaveShaper.InputsKey(7, b, MeshGround(b, g)), Is.EqualTo(key), "a repaint of an unchanged cave keeps its meshes");
		}

		[Test]
		public void TheRebuildKeyChangesWithEveryInputTheMesherReads()
		{
			CaveSystem system = Stored(CaveForm.LavaTube, 0, 2, out Func<float, float, float> g, out float[] values, out List<Vector2> cut);
			CaveGround ground = MeshGround(system, g);
			string key = CaveShaper.InputsKey(3, system, ground);

			Assert.That(CaveShaper.InputsKey(4, system, ground), Is.Not.EqualTo(key), "another site (the meshes' names)");

			var heights = (float[])ground.Heights.Clone();
			heights[heights.Length / 2] += 0.001f;
			var moved = new CaveGround(ground.OriginX, ground.OriginZ, ground.Spacing, ground.Width, ground.Depth, heights);
			Assert.That(CaveShaper.InputsKey(3, system, moved), Is.Not.EqualTo(key), "one ground sample a millimetre higher");

			CaveSystem other = CaveSystem.FromValues(values, system.Origin, system.Seed + 1);
			other.UseCutHoles(0, cut, g, HoleStep, GridX, GridZ);
			Assert.That(CaveShaper.InputsKey(3, other, ground), Is.Not.EqualTo(key), "another seed (the rock's noise)");

			CaveSystem shifted = CaveSystem.FromValues(values, system.Origin + new Vector3(0f, 0.001f, 0f), system.Seed);
			shifted.UseCutHoles(0, cut, g, HoleStep, GridX, GridZ);
			Assert.That(CaveShaper.InputsKey(3, shifted, ground), Is.Not.EqualTo(key), "another frame (the UVs are world metres)");

			var wider = (float[])values.Clone();
			wider[3] += 0.001f;
			CaveSystem widened = CaveSystem.FromValues(wider, system.Origin, system.Seed);
			widened.UseCutHoles(0, cut, g, HoleStep, GridX, GridZ);
			Assert.That(CaveShaper.InputsKey(3, widened, ground), Is.Not.EqualTo(key), "a stored value (the bore)");

			CaveSystem uncut = CaveSystem.FromValues(values, system.Origin, system.Seed);
			Assert.That(CaveShaper.InputsKey(3, uncut, ground), Is.Not.EqualTo(key), "no cut read back (the collar's outline)");

			CaveSystem lessCut = CaveSystem.FromValues(values, system.Origin, system.Seed);
			lessCut.UseCutHoles(0, cut.GetRange(0, cut.Count - 1), g, HoleStep, GridX, GridZ);
			Assert.That(CaveShaper.InputsKey(3, lessCut, ground), Is.Not.EqualTo(key), "one hole fewer at the mouth");
		}
	}
}
