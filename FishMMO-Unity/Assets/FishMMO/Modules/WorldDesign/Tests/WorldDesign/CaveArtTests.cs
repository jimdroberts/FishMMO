using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The cave and mine kit (2026-10-13): the glow art's meshes are sound at every level and sit on their mount as the
	/// placer is told (CaveArt), its files are listed where the generator looks, its lights carry hues SiteLightPool knows;
	/// the ladders and ropes stack at one even pitch; the lanterns give off their lamps.
	/// </summary>
	[TestFixture]
	public class CaveArtTests
	{
		private const int Seed = ProceduralArtCatalogue.DefaultSeed;

		[Test]
		public void TheFamilies_AreInKindOrder_WithHuesAndShapes()
		{
			CaveGlowFamily[] all = ProceduralArtCatalogue.CaveGlowFamilies;
			Assert.That(all.Length, Is.EqualTo(Enum.GetValues(typeof(CaveGlowKind)).Length));
			for (int i = 0; i < all.Length; i++)
			{
				Assert.That((int)all[i].Kind, Is.EqualTo(i), "CaveGlow(kind) indexes the table by kind");
				Assert.That(all[i].Variants, Is.GreaterThanOrEqualTo(2), $"{all[i].Kind} has at least two shapes");
				Assert.That(all[i].Hues, Is.Not.Empty);
				CaveGlowFamily owner = ProceduralArtCatalogue.CaveGlow(all[i].Shape);
				Assert.That(owner.Shape, Is.EqualTo(owner.Kind), $"{all[i].Kind} wears a kind that owns its meshes");
				Assert.That(owner.Variants, Is.EqualTo(all[i].Variants), $"{all[i].Kind} has as many shapes as the meshes it wears");
				foreach (CaveGlowHue hue in all[i].Hues)
				{
					Assert.That(hue.EmitterHue, Is.InRange(-1, 3), $"{all[i].Kind} {hue.Name}: SiteLightPool reads two bits of hue");
					bool glows = hue.Emission.maxColorComponent > 0f;
					Assert.That(glows, Is.EqualTo(hue.EmitterHue >= 0), $"{all[i].Kind} {hue.Name}: what glows has a light's hue, what does not has none");
				}
			}
			Assert.That(CaveArt.HueCount(CaveGlowKind.Crystal), Is.EqualTo(4), "ice-blue, green, violet, amber");
			Assert.That(CaveArt.HueCount(CaveGlowKind.Fungus), Is.EqualTo(3), "teal, blue, gold");
			Assert.That(CaveArt.HueIndex(CaveGlowKind.Crystal, "amber"), Is.EqualTo(3));
			Assert.That(CaveArt.HueName(CaveGlowKind.Crystal, 5), Is.EqualTo("Green"), "a hue past the end wraps round");
		}

		[Test]
		public void EveryShape_IsSound_AtEveryLevel_AndSitsOnItsMount()
		{
			foreach (CaveGlowFamily f in ProceduralArtCatalogue.CaveGlowFamilies.Where(f => ProceduralArtCatalogue.OwnsShape(in f)))
			{
				for (int v = 0; v < f.Variants; v++)
				{
					int previous = int.MaxValue;
					for (int lod = 0; lod < CaveGlowMeshes.Levels; lod++)
					{
						string name = $"{f.Kind} v{v} LOD{lod}";
						MeshBuilder m = CaveGlowMeshes.Build(f.Shape, v, lod, Seed);
						List<string> problems = m.Validate(true);
						Assert.That(problems, Is.Empty, $"{name}: {(problems.Count > 0 ? problems[0] : "")}");
						Assert.That(m.TriangleCount, Is.GreaterThan(0), $"{name} is empty");
						Assert.That(m.TriangleCount, Is.LessThanOrEqualTo(previous), $"{name} has more triangles than the level before it");
						Assert.That(m.TriangleCount, Is.LessThanOrEqualTo(10000), $"{name} is too heavy for a prop");
						previous = m.TriangleCount;
						Assert.That(m.Submeshes.Count, Is.EqualTo(CaveGlowMeshes.Submeshes(f.Shape)), $"{name}: one submesh a material");
						Assert.That(m.Submeshes.All(s => s.Count > 0), Is.True, $"{name}: an empty submesh");
						Bounds b = m.Bounds;
						switch (f.Mount)
						{
							case CaveMount.Floor:
								Assert.That(b.max.y, Is.GreaterThan(0.05f), $"{name} stands up from its foot");
								Assert.That(b.min.y, Is.InRange(-1f, 0f), $"{name} is seated a little below its foot");
								break;
							case CaveMount.Wall:
								Assert.That(b.min.z, Is.LessThan(-0.01f), $"{name} stands out of the wall along −z");
								Assert.That(b.max.z, Is.InRange(0f, 0.2f), $"{name} reaches a little into the wall");
								break;
							case CaveMount.Roof:
								Assert.That(b.min.y, Is.LessThan(-0.05f), $"{name} hangs down");
								Assert.That(b.max.y, Is.InRange(0f, 0.35f), $"{name} reaches a little into the roof");
								break;
						}
						foreach (Vector2 uv in m.UVs)
						{
							Assert.That(float.IsNaN(uv.x) || float.IsNaN(uv.y), Is.False, $"{name}: a texture coordinate is not a number");
						}
					}
					MeshBuilder a = CaveGlowMeshes.Build(f.Shape, v, 0, Seed), again = CaveGlowMeshes.Build(f.Shape, v, 0, Seed);
					Assert.That(again.Positions, Is.EqualTo(a.Positions), $"{f.Kind} v{v} is the same every time");
				}
			}
		}

		[Test]
		public void GlowLevels_StayInsideTheRamp()
		{
			// The ramps repeat; a level of exactly 0 or 1 would blend the two ends under bilinear filtering.
			Assert.That(CaveGlowMeshes.Glow(0f), Is.GreaterThan(0.01f));
			Assert.That(CaveGlowMeshes.Glow(1f), Is.LessThan(0.99f));
			foreach (CaveGlowKind shape in new[] { CaveGlowKind.Fungus, CaveGlowKind.GlowWorms, CaveGlowKind.ShelfFungus })
			{
				foreach (Vector2 uv in CaveGlowMeshes.Build(shape, 0, 0, Seed).UVs)
				{
					Assert.That(uv.x, Is.InRange(CaveGlowMeshes.Glow(0f) - 1e-4f, CaveGlowMeshes.Glow(1f) + 1e-4f), $"{shape}");
				}
			}
		}

		[Test]
		public void GeneratedFiles_AreListed_WhereTheGeneratorLooks()
		{
			var payload = new HashSet<string>(ProceduralArtCatalogue.PayloadPaths());
			var wrappers = new HashSet<string>(ProceduralArtCatalogue.WrapperPaths());
			var prefabs = new HashSet<string>(ProceduralArtCatalogue.AllPrefabNames());
			Assert.That(payload, Does.Contain(ProceduralArtCatalogue.CaveGlowAlbedoPath));
			Assert.That(payload, Does.Contain(ProceduralArtCatalogue.CaveGlowEmissionPath));
			Assert.That(payload, Does.Contain(ProceduralArtCatalogue.CaveOreAlbedoPath));
			Assert.That(wrappers, Does.Contain(ProceduralArtCatalogue.MaterialPath(ProceduralArtCatalogue.CaveGangueMaterial)));
			foreach (CaveGlowFamily f in ProceduralArtCatalogue.CaveGlowFamilies)
			{
				for (int v = 0; v < f.Variants; v++)
				{
					for (int lod = 0; lod < CaveGlowMeshes.Levels; lod++)
					{
						Assert.That(payload, Does.Contain(ProceduralArtCatalogue.MeshPath(ProceduralArtCatalogue.CaveGlowMesh(f.Shape, v, lod))));
					}
					foreach (CaveGlowHue hue in f.Hues)
					{
						string name = ProceduralArtCatalogue.CaveGlowPrefab(f.Kind, hue.Name, v);
						Assert.That(prefabs, Does.Contain(name));
						Assert.That(wrappers, Does.Contain(ProceduralArtCatalogue.PrefabPath(name)));
						Assert.That(ProceduralArtCatalogue.PrefabPath(name), Does.StartWith(ProceduralArtCatalogue.CavePrefabsFolder + "/"));
					}
				}
				foreach (CaveGlowHue hue in f.Hues)
				{
					Assert.That(wrappers, Does.Contain(ProceduralArtCatalogue.MaterialPath(ProceduralArtCatalogue.CaveGlowMaterial(f.MaterialSet, hue.Name))));
				}
			}
			Assert.That(ProceduralArtCatalogue.PrefabPath(ProceduralArtCatalogue.CaveGlowPrefab(CaveGlowKind.Crystal, "Violet", 2)),
				Is.EqualTo("Assets/Prefabs/Shared/Biomes/Generated/Prefabs/Cave/Cave_Crystal_Violet_2.prefab"), "paths are a contract");
			foreach (string source in new[] { "CaveGlowMeshes.cs", "ProceduralArtCatalogue.Cave.cs", "BiomeArtGenerator.CaveGlow.cs", "StructurePieces.Cave.cs" })
			{
				Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain(source), $"{source} decides generated bytes, so it is hashed");
				Assert.That(ProceduralArtFamilies.FamiliesOfFile(source), Is.EqualTo(new[] { ArtFamily.Structures }), $"{source} is built with the structures");
			}
			Assert.That(ProceduralArtFamilies.FamiliesOfStep("Structure surfaces: cave glow"), Is.EqualTo(new[] { ArtFamily.Structures }));
			Assert.That(ProceduralArtFamilies.FamiliesOfStep("Structure: cave GlowWorms"), Is.EqualTo(new[] { ArtFamily.Structures }));
		}

		[Test]
		public void Ladders_StackAtOneEvenPitch()
		{
			foreach (bool rope in new[] { false, true })
			{
				List<(string id, float foot)> stack = CaveArt.LadderStack(7.4f, rope);
				Assert.That(stack.Select(s => s.id), Is.EqualTo(rope
					? new[] { "RopeLadder", "RopeLadder", "RopeLadderShort", "RopeLadderShort" }
					: new[] { "Ladder", "Ladder", "LadderShort", "LadderShort" }));
				Assert.That(stack.Select(s => s.foot), Is.EqualTo(new[] { 0f, 3f, 6f, 7f }));
				// Every rung of every module, in order: a third of a metre apart, the first a sixth up.
				var rungs = new List<float>();
				foreach ((string id, float foot) in stack)
				{
					float module = id.EndsWith("Short") ? CaveArt.LadderShortModule : CaveArt.LadderModule;
					int count = Mathf.RoundToInt(module / CaveArt.LadderRungPitch);
					for (int k = 0; k < count; k++)
					{
						rungs.Add(foot + StructurePieces.LadderRungHeight(k));
					}
				}
				for (int i = 1; i < rungs.Count; i++)
				{
					Assert.That(rungs[i] - rungs[i - 1], Is.EqualTo(CaveArt.LadderRungPitch).Within(1e-4f));
				}
				Assert.That(rungs[rungs.Count - 1], Is.GreaterThan(7.4f - CaveArt.LadderRungPitch), "the top rung reaches the lip");
			}
			Assert.That(CaveArt.LadderStack(0.6f, false).Select(s => s.id), Is.EqualTo(new[] { "LadderShort" }));
		}

		[Test]
		public void ClimbingRopes_HangTheirLength()
		{
			List<(float top, float scaleY)> stack = CaveArt.ClimbRopeStack(7.5f);
			Assert.That(stack.Select(s => s.top), Is.EqualTo(new[] { 0f, -3f, -6f }));
			Assert.That(stack.Sum(s => s.scaleY * CaveArt.ClimbRopeModule), Is.EqualTo(7.5f).Within(1e-3f));
			Assert.That(CaveArt.TrestleBentScale(11.6f, 0), Is.EqualTo(2f).Within(1e-4f));
		}

		[Test]
		public void EmitterSeeds_CarryTheirHue()
		{
			Assert.That((CaveArt.EmitterSeed(0xFFFFFFFFu, 3) >> 8) & 3u, Is.EqualTo(3u));
			Assert.That((CaveArt.EmitterSeed(0xFFFFFFFFu, 0) >> 8) & 3u, Is.EqualTo(0u));
			Assert.That(CaveArt.EmitterSeed(0x12345678u, -1), Is.EqualTo(0x12345678u));
			Assert.That(CaveArt.EmitterSeed(0x12345678u, 2) & ~0x300u, Is.EqualTo(0x12345678u & ~0x300u), "only the hue bits change");
		}

		[Test]
		public void Lanterns_GiveOffTheirLamps_AndHangBelowTheirFixing()
		{
			foreach (string id in new[] { CaveArt.HangingLantern, CaveArt.WallLantern, CaveArt.LanternStand })
			{
				StructurePiece piece = StructureKit.Get(id);
				Assert.That(piece, Is.Not.Null, id);
				for (int v = 0; v < piece.Variants; v++)
				{
					IReadOnlyList<(Vector3 at, SceneEmitterKind kind)> emitters = StructurePieces.EmittersOf(piece, v);
					Assert.That(emitters.Count(e => e.kind == SceneEmitterKind.Lamp), Is.EqualTo(1), $"{id} v{v}");
					Assert.That(StructurePieces.Build(piece, v, 0, Seed).Materials, Does.Contain(StructureMaterial.Lamplight), $"{id} v{v} has a lit pane");
				}
			}
			StructurePiece hanging = StructureKit.Get(CaveArt.HangingLantern);
			Assert.That(hanging.Anchor, Is.EqualTo(StructureAnchor.Roof));
			for (int v = 0; v < hanging.Variants; v++)
			{
				Bounds b = StructurePieces.Build(hanging, v, 0, Seed).Mesh.Bounds;
				Assert.That(b.min.y, Is.EqualTo(-CaveArt.HangingLanternDrop(v)).Within(0.02f), $"v{v}'s bottom");
				Assert.That(b.max.y, Is.LessThanOrEqualTo(0.05f), $"v{v} hangs from its fixing");
			}
			StructurePiece rope = StructureKit.Get(CaveArt.ClimbRope);
			Bounds r = StructurePieces.Build(rope, 0, 0, Seed).Mesh.Bounds;
			Assert.That(r.min.y, Is.EqualTo(-CaveArt.ClimbRopeModule).Within(0.06f));
			StructurePiece ladder = StructureKit.Get(CaveArt.Ladder);
			Bounds l = StructurePieces.Build(ladder, 0, 0, Seed).Mesh.Bounds;
			Assert.That(l.min.y, Is.EqualTo(0f).Within(0.01f));
			Assert.That(l.max.y, Is.EqualTo(CaveArt.LadderModule).Within(0.01f));
			Assert.That(l.max.z, Is.EqualTo(0f).Within(0.005f), "its back is against the wall at z = 0");
			Assert.That(l.min.z, Is.EqualTo(-CaveArt.LadderDepth).Within(0.005f));
		}
	}
}
