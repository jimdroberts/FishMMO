using System;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The cave dressing's plenty (2026-10-20, cave realism): mushroom trees stand 5–15 m whatever their art and whatever is
	/// asked, always with their clearance under the roof; the plan (CaveFungi.For) gives each form what grows in it and the
	/// biome what it adds; the new glow art (mushroom trees, middle glowcaps, puffballs, bracket tiers, glowroots, roots and
	/// glow-vines) is as tall as it says and within its budget.
	/// </summary>
	[TestFixture]
	public class CaveDressingDensityTests
	{
		private const int Seed = ProceduralArtCatalogue.DefaultSeed;

		[Test]
		public void MushroomTrees_StandFiveToFifteenMetres_WhateverIsAsked()
		{
			foreach (float art in new[] { 4.5f, 10f, 12.3f, 14.4f, 19f })
			{
				foreach (float want in new[] { 0f, 1f, 5f, 9.3f, 15f, 22f })
				{
					float stood = art * CaveFungi.TowerScale(art, want);
					Assert.That(stood, Is.InRange(CaveFungi.TowerMin - 1e-3f, CaveFungi.TowerMax + 1e-3f), $"art {art} m asked {want} m");
					if (want >= CaveFungi.TowerMin && want <= CaveFungi.TowerMax)
					{
						Assert.That(stood, Is.EqualTo(want).Within(1e-3f), "stood as asked inside the range");
					}
				}
				float mid = art * CaveFungi.MidScale(art, 9f);
				Assert.That(mid, Is.InRange(CaveFungi.MidMin - 1e-3f, CaveFungi.MidMax + 1e-3f), "a middle glowcap stays 3–5 m");
			}
			Assert.That(CaveFungi.TowerMax, Is.EqualTo(15f), "the largest mushrooms get up to 15 m");
			Assert.That(CaveFungi.TowerMin, Is.EqualTo(5f));
		}

		[Test]
		public void TheTallestUnderARoof_KeepsItsClearance()
		{
			float last = 0f;
			for (float room = 0f; room <= 60f; room += 0.25f)
			{
				float h = CaveFungi.TallestUnder(room, CaveFungi.TowerMax);
				Assert.That(h, Is.InRange(0f, CaveFungi.TowerMax), $"room {room}");
				Assert.That(h, Is.GreaterThanOrEqualTo(last - 1e-4f), "a higher roof never makes the tallest shorter");
				last = h;
				if (h > 0f)
				{
					Assert.That(CaveFungi.Fits(0f, room, h), Is.True, $"{h:0.00} m under a {room} m roof keeps its clearance");
					Assert.That(room - h, Is.GreaterThanOrEqualTo(CaveFungi.Clearance(h) - 1e-3f));
				}
			}
			Assert.That(CaveFungi.TallestUnder(16.8f, CaveFungi.TowerMax), Is.EqualTo(15f).Within(1e-3f), "15 m wants 1.8 m over its crown");
			Assert.That(CaveFungi.TallestUnder(100f, CaveFungi.TowerMax), Is.EqualTo(15f), "never over 15 m however high the roof");
			Assert.That(CaveFungi.TallestUnder(6.4f, CaveFungi.TowerMax), Is.LessThan(CaveFungi.TowerMin), "a 6.4 m roof is too low for a mushroom tree");
		}

		[Test]
		public void TheClearanceRule_RefusesWhatTouchesTheRoof()
		{
			Assert.That(CaveFungi.Clearance(5f), Is.EqualTo(1.5f), "at least a metre and a half");
			Assert.That(CaveFungi.Clearance(15f), Is.EqualTo(1.8f).Within(1e-4f), "an eighth of the tallest's height");
			Assert.That(CaveFungi.Fits(2f, 2f + 15f + 1.8f, 15f), Is.True);
			Assert.That(CaveFungi.Fits(2f, 2f + 15f + 1.7f, 15f), Is.False, "10 cm short of its clearance");
			Assert.That(CaveFungi.Fits(0f, 6f, 5f), Is.False, "a 5 m cap under a 6 m roof");
			Assert.That(CaveFungi.Fits(0f, 6.5f, 5f), Is.True);
		}

		[Test]
		public void ThePlan_ByForm()
		{
			var unknown = default(CaveBiomeTraits);
			CaveFungusPlan cave = CaveFungi.For(in unknown, CaveForm.Cave);
			Assert.That(cave.Fungi, Is.True);
			Assert.That(cave.Forests, Is.GreaterThan(0f), "a cave's great halls grow forests");
			Assert.That(cave.TowerMax, Is.EqualTo(CaveFungi.TowerMax), "to 15 m");
			Assert.That(cave.Groves, Is.GreaterThan(0f));
			Assert.That(cave.GlowRoots, Is.GreaterThan(0f));
			Assert.That(cave.RootsNearMouth, Is.GreaterThan(0f));
			Assert.That(cave.Clutter, Is.EqualTo(0f), "no mine clutter in a natural cave");

			CaveFungusPlan grotto = CaveFungi.For(in unknown, CaveForm.Grotto);
			Assert.That(grotto.Groves, Is.GreaterThan(0f), "groves in every natural form that grows fungi, not only halls");
			Assert.That(grotto.TowerMax, Is.InRange(CaveFungi.TowerMin, CaveFungi.TowerMax - 1f), "a grotto's chamber is low: smaller trees");

			foreach (CaveForm dead in new[] { CaveForm.IceCave, CaveForm.LavaTube })
			{
				CaveFungusPlan p = CaveFungi.For(in unknown, dead);
				Assert.That(p.Fungi, Is.False, $"nothing lives in a {dead}");
				Assert.That(p.Forests + p.Groves + p.GlowRoots + p.Rings, Is.EqualTo(0f), dead.ToString());
				Assert.That(p.Rubble, Is.GreaterThan(0f), $"a {dead} still sheds its rock");
			}
			Assert.That(CaveFungi.For(in unknown, CaveForm.LavaTube).RootsNearMouth, Is.GreaterThan(0f), "roots hang through a lava tube's thin roof");

			CaveFungusPlan mine = CaveFungi.For(in unknown, CaveForm.Mine);
			Assert.That(mine.Clutter, Is.GreaterThan(0f), "a mine is littered with its working");
			Assert.That(mine.TowerMax, Is.LessThan(CaveFungi.TowerMin), "no mushroom trees in a mine");
			Assert.That(mine.Forests, Is.EqualTo(0f));
			Assert.That(mine.RootsNearMouth, Is.LessThan(cave.RootsNearMouth), "few roots, by the portal");
			Assert.That(mine.DaylightVines, Is.LessThan(cave.DaylightVines), "few vines");

			CaveFungusPlan sea = CaveFungi.For(in unknown, CaveForm.SeaCave);
			Assert.That(sea.Forests, Is.EqualTo(0f), "salt spray: no forests");
			Assert.That(sea.Groves, Is.LessThan(cave.Groves));

			foreach (CaveForm form in (CaveForm[])Enum.GetValues(typeof(CaveForm)))
			{
				CaveFungusPlan p = CaveFungi.For(in unknown, form);
				Assert.That(p.TowerMax, Is.LessThanOrEqualTo(CaveFungi.TowerMax), form.ToString());
				CaveFungusPlan none = CaveFungi.For((FishMMO.Shared.Biomes.BiomeTemplate)null, form);
				Assert.That(none.Groves, Is.EqualTo(p.Groves), "no biome: the form's defaults");
				Assert.That(none.DaylightVines, Is.EqualTo(p.DaylightVines));
			}
		}

		[Test]
		public void ThePlan_ByWhatTheBiomeIs()
		{
			var bare = new CaveBiomeTraits { Known = true };
			var wooded = new CaveBiomeTraits { Known = true, Vines = true, VineCaves = 1f, Trees = 0.3f, Greenery = true };
			var lit = new CaveBiomeTraits { Known = true, GlowingCrystals = true };
			CaveFungusPlan baseline = CaveFungi.For(default(CaveBiomeTraits), CaveForm.Cave);
			Assert.That(CaveFungi.For(in bare, CaveForm.Cave).DaylightVines, Is.EqualTo(0f), "no vines inside where the biome grows none");
			Assert.That(CaveFungi.For(in wooded, CaveForm.Cave).DaylightVines, Is.GreaterThan(baseline.DaylightVines * 0.9f), "thick where it grows them thickly");
			Assert.That(CaveFungi.For(in wooded, CaveForm.Cave).RootsNearMouth, Is.GreaterThan(CaveFungi.For(in bare, CaveForm.Cave).RootsNearMouth), "a wooded hill lets more roots down");
			Assert.That(CaveFungi.For(in lit, CaveForm.Cave).Groves, Is.LessThan(baseline.Groves), "the biome's glowing crystals share the halls");
			Assert.That(CaveFungi.CharacterWeight(CaveCharacter.Halls), Is.GreaterThan(CaveFungi.CharacterWeight(CaveCharacter.Crystal)), "a hall cave's halls carry the forests");
		}

		[Test]
		public void TheNewGlowArt_IsAsTallAsItSays_AndWithinItsBudget()
		{
			(CaveGlowKind kind, float low, float high, int budget)[] kinds =
			{
				(CaveGlowKind.TowerFungus, 9.5f, CaveFungi.TowerMax, 10000),
				(CaveGlowKind.MidFungus, CaveFungi.MidMin, CaveFungi.MidMax, 4000),
				(CaveGlowKind.Puffballs, 0.3f, 2.5f, 3000),
				(CaveGlowKind.ShelfTiers, 1.2f, 3.2f, 5000),
				(CaveGlowKind.GlowRoots, 1f, 4f, 6000),
				(CaveGlowKind.HangingRoots, 1f, 3f, 5000),
				(CaveGlowKind.GlowVine, 1f, 2.8f, 5000),
			};
			foreach ((CaveGlowKind kind, float low, float high, int budget) in kinds)
			{
				CaveGlowFamily f = ProceduralArtCatalogue.CaveGlow(kind);
				for (int v = 0; v < f.Variants; v++)
				{
					MeshBuilder m = CaveGlowMeshes.Build(kind, v, 0, Seed);
					Bounds b = m.Bounds;
					// The extent the art is measured by: a floor piece's height, a wall piece's tallest side, a roof piece's drop.
					float extent = f.Mount == CaveMount.Floor ? b.max.y : f.Mount == CaveMount.Roof ? -b.min.y : Mathf.Max(b.size.x, b.size.y);
					Console.WriteLine($"{kind} v{v}: {extent:0.00} m, {m.TriangleCount} / {CaveGlowMeshes.Build(kind, v, 1, Seed).TriangleCount} / {CaveGlowMeshes.Build(kind, v, 2, Seed).TriangleCount} tris");
					Assert.That(extent, Is.InRange(low, high), $"{kind} v{v}");
					Assert.That(m.TriangleCount, Is.LessThanOrEqualTo(budget), $"{kind} v{v} LOD0");
				}
			}
			Assert.That(ProceduralArtCatalogue.CaveGlow(CaveGlowKind.TowerFungus).Collides, Is.True, "a mushroom tree is walked into, not through");
			Assert.That(ProceduralArtCatalogue.CaveGlow(CaveGlowKind.HangingRoots).Hues[0].Emission.maxColorComponent, Is.EqualTo(0f), "tree roots do not glow");
		}
	}
}
