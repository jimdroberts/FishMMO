using System;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// What hangs from a cave's roof and piles under it (2026-10-20, caves livened): the plan each form gets, the rules the
	/// roots and the guano keep, and the art (glow-worm silk, tap roots, guano heaps) as long, as seated and as cheap as it says.
	/// </summary>
	[TestFixture]
	public class CaveHangingTests
	{
		private const int Seed = ProceduralArtCatalogue.DefaultSeed;

		[Test]
		public void ThePlan_ByForm()
		{
			CaveHangingPlan cave = CaveHanging.For(CaveForm.Cave);
			Assert.That(cave.Silk, Is.EqualTo(1f));
			Assert.That(cave.DeepRoots, Is.EqualTo(1f));
			Assert.That(cave.Guano, Is.EqualTo(CaveHabitat.TraitsOf(CaveForm.Cave).Guano), "the habitat's guano, so heaps go where the rock is stained");
			CaveHangingPlan ice = CaveHanging.For(CaveForm.IceCave);
			Assert.That(ice.Silk + ice.DeepRoots + ice.Guano, Is.EqualTo(0f), "nothing lives or grows in an ice cave");
			Assert.That(CaveHanging.For(CaveForm.LavaTube).Silk, Is.EqualTo(0f), "a lava tube is dry and nothing glows in it");
			Assert.That(CaveHanging.For(CaveForm.LavaTube).DeepRoots, Is.GreaterThan(cave.DeepRoots), "a lava tube's thin roof lets the most roots through");
			Assert.That(CaveHanging.For(CaveForm.Mine).Silk, Is.EqualTo(0f), "a mine's palette has no glow-worms");
			Assert.That(CaveHanging.For(CaveForm.Grotto).Silk, Is.GreaterThan(cave.Silk), "a grotto's water breeds the larvae's prey");
			Assert.That(CaveHanging.For(CaveForm.SeaCave).Silk, Is.LessThan(cave.Silk), "salt air");
		}

		[Test]
		public void Roots_ComeThroughOnlyAThinRoof_UnderTrees()
		{
			Assert.That(CaveHanging.Wooded(null), Is.EqualTo(0f));
			Assert.That(CaveHanging.RootChance(0.1f, 1f, 1f), Is.EqualTo(0f), "under the soil's own underside: the near-mouth roots' and the cover's");
			Assert.That(CaveHanging.RootChance(CaveHanging.RootCoverMax + 0.5f, 1f, 1f), Is.EqualTo(0f), "too deep for any root");
			Assert.That(CaveHanging.RootChance(float.NaN, 1f, 1f), Is.EqualTo(0f), "no ground known");
			Assert.That(CaveHanging.RootChance(3f, 0f, 1f), Is.EqualTo(0f), "no trees over it, no roots");
			float last = float.MaxValue;
			for (float cover = CaveHanging.RootCoverMin; cover <= CaveHanging.RootCoverMax; cover += 0.25f)
			{
				float chance = CaveHanging.RootChance(cover, 1f, 1f);
				Assert.That(chance, Is.InRange(0f, 1f));
				Assert.That(chance, Is.LessThanOrEqualTo(last + 1e-6f), $"fewer the deeper ({cover} m)");
				last = chance;
			}
			Assert.That(CaveHanging.RootChance(2f, 0.5f, 1f), Is.LessThan(CaveHanging.RootChance(2f, 1f, 1f)), "fewer under a thinner wood");
		}

		[Test]
		public void ATapRoot_IsStretchedOntoTheFloor()
		{
			MeshBuilder m = CaveGlowMeshes.Build(CaveGlowKind.TapRoots, 0, 0, Seed);
			float height = -m.Bounds.min.y;
			foreach (float room in new[] { CaveHanging.TapRoomMin, 4f, CaveHanging.TapRoomMax })
			{
				float sy = CaveHanging.TapScaleY(room, height);
				Assert.That(height * sy, Is.EqualTo(room + 0.05f).Within(1e-4f), $"its lowest point a few cm into the floor {room} m under the fixing");
			}
		}

		[Test]
		public void TheTapRoots_ReachTheirLength_AndSpreadOverTheFloor()
		{
			for (int v = 0; v < CaveArt.VariantCount(CaveGlowKind.TapRoots); v++)
			{
				MeshBuilder m = CaveGlowMeshes.Build(CaveGlowKind.TapRoots, v, 0, Seed);
				Bounds b = m.Bounds;
				Console.WriteLine($"TapRoots v{v}: {b.min.y:0.000}..{b.max.y:0.000}, {b.size.x:0.00} by {b.size.z:0.00} m, {m.TriangleCount} tris");
				Assert.That(b.min.y, Is.InRange(-CaveGlowMeshes.TapRootLength - 0.1f, -CaveGlowMeshes.TapRootLength + 0.01f), $"v{v} hangs to the floor's line and a little into it");
				Assert.That(Mathf.Max(b.size.x, b.size.z), Is.GreaterThan(0.5f), $"v{v} spreads over the floor at its foot");
				// Points on the floor's line spread out from the roots (the rootlets), not only where they hang.
				int low = 0;
				foreach (Vector3 p in m.Positions)
				{
					if (p.y < -CaveGlowMeshes.TapRootLength + 0.06f)
					{
						low++;
					}
				}
				Assert.That(low, Is.GreaterThan(20), $"v{v} has rootlets along the floor");
			}
		}

		[Test]
		public void TheSilk_HangsLongThreads_FromTheRoof()
		{
			for (int v = 0; v < CaveArt.VariantCount(CaveGlowKind.GlowSilk); v++)
			{
				for (int lod = 0; lod < CaveGlowMeshes.Levels; lod++)
				{
					MeshBuilder m = CaveGlowMeshes.Build(CaveGlowKind.GlowSilk, v, lod, Seed);
					Bounds b = m.Bounds;
					if (lod == 0)
					{
						Console.WriteLine($"GlowSilk v{v}: drop {-b.min.y:0.00} m, {b.size.x:0.00} by {b.size.z:0.00} m, {m.TriangleCount} / {CaveGlowMeshes.Build(CaveGlowKind.GlowSilk, v, 1, Seed).TriangleCount} / {CaveGlowMeshes.Build(CaveGlowKind.GlowSilk, v, 2, Seed).TriangleCount} tris");
						Assert.That(-b.min.y, Is.InRange(0.7f, 1.7f), $"v{v}'s longest threads");
						Assert.That(b.size.x, Is.GreaterThan(0.6f * CaveGlowMeshes.GlowSilkSpans[v]), $"v{v} runs along its joint");
						Assert.That(m.TriangleCount, Is.LessThanOrEqualTo(6000), $"v{v} within its budget");
					}
					// The glow: every coordinate a level inside the ramp, the brightest the larvae.
					float top = 0f;
					foreach (Vector2 uv in m.UVs)
					{
						Assert.That(uv.x, Is.InRange(CaveGlowMeshes.Glow(0f) - 1e-4f, CaveGlowMeshes.Glow(1f) + 1e-4f));
						top = Mathf.Max(top, uv.x);
					}
					Assert.That(top, Is.EqualTo(CaveGlowMeshes.Glow(1f)).Within(1e-4f), $"v{v} LOD{lod}: its larvae glow brightest");
				}
			}
			Assert.That(ProceduralArtCatalogue.CaveGlow(CaveGlowKind.GlowSilk).Hues, Is.SameAs(ProceduralArtCatalogue.FungusHues), "the silk glows in the fungi's hues, as the cover's threads do");
		}

		[Test]
		public void AGuanoHeap_SitsOnTheFloor_AsWideAsItSays()
		{
			for (int v = 0; v < CaveArt.VariantCount(CaveGlowKind.GuanoHeap); v++)
			{
				MeshBuilder m = CaveGlowMeshes.Build(CaveGlowKind.GuanoHeap, v, 0, Seed);
				Bounds b = m.Bounds;
				float r = CaveGlowMeshes.GuanoRadii[v];
				Console.WriteLine($"GuanoHeap v{v}: {b.max.y:0.00} m high, {b.size.x:0.00} m across, {m.TriangleCount} tris");
				Assert.That(b.max.y, Is.InRange(0.08f, 0.75f), $"v{v} a low heap");
				Assert.That(b.min.y, Is.InRange(-0.1f, -0.05f), $"v{v}'s skirt reaches under the floor");
				Assert.That(0.5f * Mathf.Max(b.size.x, b.size.z), Is.InRange(r, 1.15f * r), $"v{v} as wide as its radius (and its skirt)");
				foreach (Vector3 n in m.Normals)
				{
					Assert.That(n.y, Is.GreaterThan(0f), $"v{v}: a heap faces up and out everywhere");
				}
			}
			Assert.That(ProceduralArtCatalogue.CaveGlow(CaveGlowKind.GuanoHeap).Hues[0].Emission.maxColorComponent, Is.EqualTo(0f), "guano does not glow");
			Assert.That(ProceduralArtCatalogue.CaveGlow(CaveGlowKind.GuanoHeap).Collides, Is.False, "walked through, as the cover's crust is");
		}

		[Test]
		public void TheGuano_IsAsWideAsTheColony()
		{
			(int narrowV, float narrow) = CaveHanging.GuanoFor(0.4f, 5f, 0.9f);
			(int wideV, float wide) = CaveHanging.GuanoFor(1.8f, 5f, 0.9f);
			Assert.That(narrowV, Is.EqualTo(0), "a small colony's pile peaks");
			Assert.That(wide * CaveGlowMeshes.GuanoRadii[wideV], Is.GreaterThan(narrow * CaveGlowMeshes.GuanoRadii[narrowV]), "a wider colony, a wider heap");
			Assert.That(CaveHanging.GuanoFor(0.5f, 20f, 0.9f).variant, Is.EqualTo(1), "a high roost's droppings scatter into a low spread");
			for (float r = 0.2f; r <= 2f; r += 0.1f)
			{
				for (float drop = 2f; drop <= 40f; drop += 4f)
				{
					foreach (float roll in new[] { 0.1f, 0.6f })
					{
						(int v, float s) = CaveHanging.GuanoFor(r, drop, roll);
						Assert.That(v, Is.InRange(0, CaveGlowMeshes.GuanoRadii.Length - 1));
						Assert.That(s, Is.InRange(0.5f, 1.8f));
					}
				}
			}
		}
	}
}
