using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The structure palette made art (2026-10-16, I6a): the palette's ten new surfaces, the part every solid plays, the
	/// material keys and their names, the roof rules, and the one invariant that keeps the kit's look (every part's kit
	/// material is its own). Pure: the builders themselves are exercised in StructureKitTests (they need native maths).
	/// </summary>
	[TestFixture]
	public class StructurePaletteArtTests
	{
		private const int Seed = ProceduralArtCatalogue.DefaultSeed;

		private static readonly string[] PaletteSurfaces = { "Log", "Shingle", "Slate", "Adobe", "Sandstone", "Bamboo", "Marble", "IceBlock", "Bone", "Reed" };

		private static readonly StructureMaterial[] PaletteMaterials =
		{
			StructureMaterial.Log, StructureMaterial.Shingle, StructureMaterial.Slate, StructureMaterial.Adobe, StructureMaterial.Sandstone,
			StructureMaterial.Bamboo, StructureMaterial.Marble, StructureMaterial.IceBlock, StructureMaterial.BoneWork, StructureMaterial.Reed,
		};

		private static IEnumerable<StructureMaterial> AllMaterials => (StructureMaterial[])Enum.GetValues(typeof(StructureMaterial));
		private static IEnumerable<StructureStyle> AllStyles => (StructureStyle[])Enum.GetValues(typeof(StructureStyle));

		// ── Surfaces ──────────────────────────────────────────────────

		[Test]
		public void NewSurfaces_AreListed_AndEachMaterialWearsItsOwn()
		{
			Assert.That(StructureSurfaces.Names.Take(9), Is.EqualTo(new[] { "Timber", "Plaster", "Thatch", "Ashlar", "Fieldstone", "Cloth", "Iron", "Earth", "Monolith" }),
				"the kit's nine keep their order (texture paths)");
			Assert.That(StructureSurfaces.Names.Skip(9), Is.EqualTo(PaletteSurfaces));
			for (int i = 0; i < PaletteMaterials.Length; i++)
			{
				Assert.That(StructureSurfaces.SurfaceOf(PaletteMaterials[i]), Is.EqualTo(PaletteSurfaces[i]), PaletteMaterials[i].ToString());
				Assert.That(StructureSurfaces.TintOf(PaletteMaterials[i]), Is.EqualTo(Color.white), "the new surfaces carry their own colour");
			}
			// The kit's own materials still wear what they wore (the bone pile's ivory plaster included).
			Assert.That(StructureSurfaces.SurfaceOf(StructureMaterial.Bone), Is.EqualTo("Plaster"));
			Assert.That(StructureSurfaces.SurfaceOf(StructureMaterial.Stone), Is.EqualTo("Ashlar"));
			Assert.That(StructureSurfaces.SurfaceOf(StructureMaterial.Ash), Is.EqualTo("Earth"));
			foreach (StructureMaterial m in AllMaterials)
			{
				Assert.That(StructureSurfaces.Names, Does.Contain(StructureSurfaces.SurfaceOf(m)), m.ToString());
			}
		}

		[Test]
		public void NewSurfaces_AreFiniteInRange_AndTile()
		{
			const int size = 128;
			foreach (string surface in PaletteSurfaces)
			{
				SurfaceMaps maps = StructureSurfaces.Generate(surface, size, Seed);
				Assert.That(maps.Size, Is.EqualTo(size), surface);
				Assert.That(maps.Height.Length, Is.EqualTo(size * size), surface);
				Assert.That(maps.Albedo.Length, Is.EqualTo(size * size), surface);
				Assert.That(maps.Normal.Length, Is.EqualTo(size * size), surface);
				Assert.That(maps.Mask.Length, Is.EqualTo(size * size), surface);
				float min = float.MaxValue, max = float.MinValue;
				foreach (float h in maps.Height)
				{
					Assert.That(float.IsNaN(h) || float.IsInfinity(h), Is.False, $"{surface}: height not finite");
					min = Mathf.Min(min, h);
					max = Mathf.Max(max, h);
				}
				Assert.That(min, Is.GreaterThanOrEqualTo(-1e-4f), surface);
				Assert.That(max, Is.LessThanOrEqualTo(1f + 1e-4f), surface);
				Assert.That(max - min, Is.GreaterThan(0.3f), $"{surface}: has relief");
				// Albedo: not black, not flat, its alpha (smoothness) the mask's.
				double luma = 0, spread = 0;
				var lumas = new float[maps.Albedo.Length];
				for (int i = 0; i < maps.Albedo.Length; i++)
				{
					Color32 a = maps.Albedo[i];
					lumas[i] = (a.r + a.g + a.b) / (3f * 255f);
					luma += lumas[i];
					Assert.That(a.a, Is.EqualTo(maps.Mask[i].a), $"{surface}: smoothness in albedo alpha and mask alpha agree");
					Assert.That(maps.Mask[i].r, Is.EqualTo(0), $"{surface}: no metal");
				}
				luma /= lumas.Length;
				foreach (float l in lumas)
				{
					spread += Math.Abs(l - luma);
				}
				spread /= lumas.Length;
				Assert.That(luma, Is.InRange(0.08, 0.95), $"{surface}: mean albedo");
				Assert.That(spread, Is.GreaterThan(0.005), $"{surface}: albedo has detail");
				// Tiling: across the seam (last column to first, last row to first) the height steps no more than it steps
				// anywhere inside the tile (a course's joint falls on the seam, as at every course).
				Assert.That(SeamStep(maps.Height, size, true), Is.LessThanOrEqualTo(InnerStep(maps.Height, size, true) * 1.1f + 0.01f), $"{surface}: tiles along u");
				Assert.That(SeamStep(maps.Height, size, false), Is.LessThanOrEqualTo(InnerStep(maps.Height, size, false) * 1.1f + 0.01f), $"{surface}: tiles along v");
				foreach (StructureFinish finish in StructurePieces.Finishes)
				{
					Assert.That(StructureSurfaces.Finish(maps, surface, finish, Seed).Length, Is.EqualTo(size * size), $"{surface} {finish}");
				}
				Assert.That(StructureSurfaces.TileMetres(surface), Is.InRange(0.9f, 3.1f), surface);
			}
		}

		[Test]
		public void NewSurfaces_AreTheSameEveryTime_AndFollowTheSeed()
		{
			foreach (string surface in PaletteSurfaces)
			{
				SurfaceMaps a = StructureSurfaces.Generate(surface, 64, Seed), b = StructureSurfaces.Generate(surface, 64, Seed), c = StructureSurfaces.Generate(surface, 64, Seed + 1);
				Assert.That(a.Albedo, Is.EqualTo(b.Albedo), $"{surface}: deterministic (rows built in parallel)");
				Assert.That(a.Height, Is.EqualTo(b.Height), surface);
				Assert.That(a.Albedo.SequenceEqual(c.Albedo), Is.False, $"{surface}: the seed matters");
			}
		}

		private static float SeamStep(float[] h, int size, bool alongU)
		{
			double sum = 0;
			for (int i = 0; i < size; i++)
			{
				sum += alongU ? Math.Abs(h[i * size + size - 1] - h[i * size]) : Math.Abs(h[(size - 1) * size + i] - h[i]);
			}
			return (float)(sum / size);
		}

		/// <summary>The largest mean step between two neighbouring columns (rows) inside the tile: a course joint's, where there are courses.</summary>
		private static float InnerStep(float[] h, int size, bool alongU)
		{
			double worst = 0;
			for (int j = 0; j + 1 < size; j++)
			{
				double sum = 0;
				for (int i = 0; i < size; i++)
				{
					sum += alongU ? Math.Abs(h[i * size + j + 1] - h[i * size + j]) : Math.Abs(h[(j + 1) * size + i] - h[j * size + i]);
				}
				worst = Math.Max(worst, sum / size);
			}
			return (float)worst;
		}

		[Test]
		public void PaletteMoss_IsTheKitsForTheKitsMaterials()
		{
			foreach (StructureMaterial m in AllMaterials.Except(PaletteMaterials))
			{
				foreach (StructureFinish f in StructurePieces.Finishes)
				{
					Assert.That(StructureSurfaces.MossOf(m, f), Is.EqualTo(MossAmounts.ForStructure(m, f)), $"{m} {f}");
				}
			}
			Assert.That(StructureSurfaces.MossOf(StructureMaterial.IceBlock, StructureFinish.Mossy), Is.EqualTo(0f), "ice grows none");
			Assert.That(StructureSurfaces.MossOf(StructureMaterial.Log, StructureFinish.None), Is.GreaterThan(StructureSurfaces.MossOf(StructureMaterial.Adobe, StructureFinish.None)));
		}

		// ── Roofs ─────────────────────────────────────────────────────

		[Test]
		public void RoofPitch_KeepsTheKitsAndKnowsTheNewRoofs()
		{
			// The kit's own, as built since 2026-10-12.
			Assert.That(StructurePieces.MinimumPitch(StructureMaterial.Thatch), Is.EqualTo(50f));
			// Turf was laid on any pitch until 2026-10-18 (I6h: "earth roofs are pitched sod/turf, only adobe gives flat roofs"):
			// sod at 35°. No kit roof is built differently for it (the turf longhouse's own rise is the larger).
			Assert.That(StructurePieces.MinimumPitch(StructureMaterial.Earth), Is.EqualTo(35f));
			foreach (StructureMaterial m in AllMaterials.Except(PaletteMaterials).Except(new[] { StructureMaterial.Thatch, StructureMaterial.Earth }))
			{
				Assert.That(StructurePieces.MinimumPitch(m), Is.EqualTo(45f), m.ToString());
			}
			// Reed is thatch; shingles as planks; slate a little lower; mud brick flat.
			Assert.That(StructurePieces.MinimumPitch(StructureMaterial.Reed), Is.EqualTo(50f));
			Assert.That(StructurePieces.MinimumPitch(StructureMaterial.Shingle), Is.EqualTo(45f));
			Assert.That(StructurePieces.MinimumPitch(StructureMaterial.Slate), Is.EqualTo(40f));
			Assert.That(StructurePieces.MinimumPitch(StructureMaterial.Adobe), Is.EqualTo(0f));
			Assert.That(StructurePieces.MinimumPitch(StructureMaterial.BoneWork), Is.EqualTo(45f));
			Assert.That(StructurePieces.MinimumPitch(StructureMaterial.Marble), Is.LessThan(StructurePieces.MinimumPitch(StructureMaterial.Slate)));

			Assert.That(StructurePalettes.BuiltPitch(BiomeStructureStyle.Timber, BiomeStructureRole.Roof), Is.EqualTo(50f), "the thatched roof");
			Assert.That(StructurePalettes.BuiltPitch(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof), Is.EqualTo(45f), "the shingled roof");
			Assert.That(StructurePalettes.BuiltPitch(BiomeStructureStyle.Timber, BiomeStructureRole.Wall), Is.Null);
			Assert.That(StructurePalettes.RoofFits(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Reed), Is.True);
			Assert.That(StructurePalettes.RoofFits(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Reed), Is.False, "reed on a shingle roof is too flat");
			Assert.That(StructurePalettes.RoofFits(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Thatch), Is.False);
			Assert.That(StructurePalettes.RoofFits(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Slate), Is.True);
			Assert.That(StructurePalettes.RoofFits(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle), Is.True);
			Assert.That(StructurePalettes.RoofFits(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Reed), Is.True, "no roof, no rule");
			foreach (BiomeStructureStyle style in BiomeStructurePalette.Styles)
			{
				foreach (BiomeStructureRole role in BiomeStructurePalette.Roles)
				{
					Assert.That(StructurePalettes.RoofFits(style, role, BiomeStructurePalette.KitMaterial(style, role)), Is.True, $"the kit's own {style} {role} fits");
				}
			}
		}

		// ── Parts ─────────────────────────────────────────────────────

		/// <summary>
		/// The invariant behind "the kit's palette changes nothing": whatever the hint, a solid's part's kit material in its style
		/// is its own material, for every style, kind of piece and material.
		/// </summary>
		[Test]
		public void APartIsAlwaysItsSolidsKitMaterial()
		{
			var hints = new List<BiomeStructureRole[]> { null, Array.Empty<BiomeStructureRole>() };
			hints.AddRange(BiomeStructurePalette.Roles.Select(r => new[] { r }));
			hints.Add(BiomeStructurePalette.Roles.ToArray());
			hints.Add(BiomeStructurePalette.Roles.Reverse().ToArray());
			int resolved = 0;
			foreach (StructureStyle style in AllStyles)
			{
				foreach (bool fabric in new[] { true, false })
				{
					foreach (StructureMaterial m in AllMaterials)
					{
						foreach (BiomeStructureRole[] hint in hints)
						{
							BiomeStructureRole? part = StructureParts.Resolve(style, fabric, m, hint);
							if (part != null)
							{
								resolved++;
								Assert.That((int)BiomeStructurePalette.KitMaterial((BiomeStructureStyle)style, part.Value), Is.EqualTo((int)m), $"{style} {(fabric ? "fabric" : "goods")} {m} as {part}");
							}
						}
					}
				}
			}
			Assert.That(resolved, Is.GreaterThan(500));
		}

		[Test]
		public void Defaults_AreTheBuildingsFabric_OrGoods()
		{
			BiomeStructureRole? Part(StructureStyle s, bool fabric, StructureMaterial m, params BiomeStructureRole[] hint) => StructureParts.Resolve(s, fabric, m, hint.Length > 0 ? hint : null);
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Timber), Is.EqualTo(BiomeStructureRole.Trim));
			Assert.That(Part(StructureStyle.Timber, false, StructureMaterial.Timber), Is.EqualTo(BiomeStructureRole.Woodwork));
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Plaster), Is.EqualTo(BiomeStructureRole.Wall));
			Assert.That(Part(StructureStyle.Stone, true, StructureMaterial.Plaster), Is.EqualTo(BiomeStructureRole.UpperWall));
			Assert.That(Part(StructureStyle.Stone, true, StructureMaterial.Stone), Is.EqualTo(BiomeStructureRole.Wall));
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Stone), Is.EqualTo(BiomeStructureRole.Dressing));
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Thatch), Is.EqualTo(BiomeStructureRole.Roof));
			Assert.That(Part(StructureStyle.Timber, false, StructureMaterial.Thatch), Is.Null, "hay and straw stay hay");
			Assert.That(Part(StructureStyle.Hide, true, StructureMaterial.Canvas), Is.EqualTo(BiomeStructureRole.Wall), "a tent's walls");
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Canvas), Is.EqualTo(BiomeStructureRole.Cloth));
			Assert.That(Part(StructureStyle.Hide, true, StructureMaterial.Ash), Is.EqualTo(BiomeStructureRole.Door), "a tent's flap");
			Assert.That(Part(StructureStyle.Stone, true, StructureMaterial.Ash), Is.Null, "a fire's ash");
			Assert.That(Part(StructureStyle.Timber, false, StructureMaterial.Iron), Is.EqualTo(BiomeStructureRole.Ironwork));
			Assert.That(Part(StructureStyle.Stone, true, StructureMaterial.Monolith), Is.EqualTo(BiomeStructureRole.Carving));
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Dyed), Is.EqualTo(BiomeStructureRole.Banner));
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Glass), Is.Null);
			Assert.That(Part(StructureStyle.Iron, true, StructureMaterial.Lamplight), Is.Null);
			// Hints: the house's wall run plays Wall, GroundFloor, LogWall or Turf by its material; a roof Roof, ShingleRoof or Turf.
			BiomeStructureRole[] walls = { BiomeStructureRole.Wall, BiomeStructureRole.GroundFloor, BiomeStructureRole.LogWall, BiomeStructureRole.Turf };
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Plaster, walls), Is.EqualTo(BiomeStructureRole.Wall));
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Stone, walls), Is.EqualTo(BiomeStructureRole.GroundFloor));
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Timber, walls), Is.EqualTo(BiomeStructureRole.LogWall));
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Earth, walls), Is.EqualTo(BiomeStructureRole.Turf));
			Assert.That(Part(StructureStyle.Stone, true, StructureMaterial.Stone, walls), Is.EqualTo(BiomeStructureRole.Wall));
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Glass, walls), Is.Null, "a window in the wall keeps its glass");
			BiomeStructureRole[] roof = { BiomeStructureRole.Roof, BiomeStructureRole.ShingleRoof, BiomeStructureRole.Turf };
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Thatch, roof), Is.EqualTo(BiomeStructureRole.Roof));
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Timber, roof), Is.EqualTo(BiomeStructureRole.ShingleRoof));
			Assert.That(Part(StructureStyle.Timber, true, StructureMaterial.Earth, roof), Is.EqualTo(BiomeStructureRole.Turf));
			Assert.That(Part(StructureStyle.Hide, true, StructureMaterial.Canvas, roof), Is.EqualTo(BiomeStructureRole.Roof));
			// Pieces: goods by tag.
			Assert.That(StructureParts.IsFabric(new[] { "building", "house" }), Is.True);
			Assert.That(StructureParts.IsFabric(new[] { "farm" }), Is.True, "a field fence");
			Assert.That(StructureParts.IsFabric(new[] { "furniture", "bed" }), Is.False);
			Assert.That(StructureParts.IsFabric(new[] { "boat", "dock" }), Is.False);
			Assert.That(StructureParts.IsFabric(new[] { "light", "torch", "camp", "town" }), Is.False);
		}

		[Test]
		public void NewRoles_AreOneMaterialInEveryStyle_AndTheKitHasThemAll()
		{
			var expected = new Dictionary<BiomeStructureRole, BiomeStructureMaterial>
			{
				[BiomeStructureRole.LogWall] = BiomeStructureMaterial.Timber,
				[BiomeStructureRole.GroundFloor] = BiomeStructureMaterial.Stone,
				[BiomeStructureRole.Turf] = BiomeStructureMaterial.Earth,
				[BiomeStructureRole.Dressing] = BiomeStructureMaterial.Stone,
				[BiomeStructureRole.Carving] = BiomeStructureMaterial.Monolith,
				[BiomeStructureRole.Woodwork] = BiomeStructureMaterial.Timber,
				[BiomeStructureRole.Ironwork] = BiomeStructureMaterial.Iron,
				[BiomeStructureRole.Cloth] = BiomeStructureMaterial.Canvas,
				[BiomeStructureRole.Banner] = BiomeStructureMaterial.Dyed,
			};
			foreach (KeyValuePair<BiomeStructureRole, BiomeStructureMaterial> pair in expected)
			{
				foreach (BiomeStructureStyle style in BiomeStructurePalette.Styles)
				{
					Assert.That(BiomeStructurePalette.KitMaterial(style, pair.Key), Is.EqualTo(pair.Value), $"{style} {pair.Key}");
				}
			}
			// Append-only: the ten I5 roles keep their values, the new ones follow.
			Assert.That((int)BiomeStructureRole.Paving, Is.EqualTo(9));
			Assert.That((int)BiomeStructureRole.LogWall, Is.EqualTo(10));
			Assert.That((int)BiomeStructureRole.Banner, Is.EqualTo(18));
			Assert.That((int)BiomeStructureMaterial.Lamplight, Is.EqualTo(13));
			Assert.That((int)BiomeStructureMaterial.Log, Is.EqualTo(14));
			Assert.That((int)BiomeStructureMaterial.Reed, Is.EqualTo(23));
			BiomeStructurePalette kit = BiomeStructurePalette.Kit();
			Assert.That(kit.Surfaces.Count, Is.EqualTo(5 * 19));
			Assert.That(StructurePalettes.KeysOf(kit), Is.Empty, "the kit's palette needs no material beyond the kit's");
		}

		// ── Keys and names ────────────────────────────────────────────

		[Test]
		public void KitKeys_AreNamedAsTheKitsMaterials()
		{
			foreach (StructureMaterial m in AllMaterials)
			{
				StructureSurfaceKey key = StructureSurfaceKey.Kit(m);
				Assert.That(key.IsKit, Is.True, m.ToString());
				Assert.That(key.Colour, Is.EqualTo(StructureSurfaces.TintOf(m)), $"{m}: the kit's exact tint, not a byte-rounded one");
				foreach (StructureFinish f in StructurePieces.Finishes)
				{
					Assert.That(key.MaterialName(f), Is.EqualTo(StructurePieces.MaterialName(m, f)), $"{m} {f}");
				}
			}
			// Every style, role and material the kit palette names resolves to the kit's own key, as built.
			BiomeStructurePalette kit = BiomeStructurePalette.Kit();
			foreach (StructureStyle style in AllStyles)
			{
				foreach (StructureMaterial m in AllMaterials)
				{
					Assert.That(StructurePalettes.Resolve(kit, style, new StructureSlot(m, null)), Is.EqualTo(StructureSurfaceKey.Kit(m)), $"{style} {m}, no part");
					foreach (BiomeStructureRole role in BiomeStructurePalette.Roles)
					{
						if (!StructureParts.Fits(style, role, m))
						{
							continue;
						}
						var slot = new StructureSlot(m, role);
						Assert.That(StructurePalettes.Resolve(kit, style, slot), Is.EqualTo(StructureSurfaceKey.Kit(m)), $"{style} {m} as {role}");
						foreach (StructureStyle host in AllStyles)
						{
							Assert.That(StructurePalettes.Resolve(kit, StructurePalettes.StyleFor(style, host, slot), slot), Is.EqualTo(StructureSurfaceKey.Kit(m)), $"{style} {m} as {role} in a {host} building");
						}
					}
				}
			}
			Assert.That(StructurePalettes.Resolve(null, StructureStyle.Timber, new StructureSlot(StructureMaterial.Plaster, BiomeStructureRole.Wall)), Is.EqualTo(StructureSurfaceKey.Kit(StructureMaterial.Plaster)));
		}

		[Test]
		public void PaletteKeys_AreDeduplicated_AndNamedByMaterialTintAndSurface()
		{
			var adobe = new Color(0.8f, 0.6f, 0.4f);
			BiomeStructurePalette desert = new BiomeArtSpec.Entry { Biome = "Desert" }
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, adobe)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Adobe, adobe)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Sandstone)
				.StructurePalette();
			List<StructureSurfaceKey> keys = StructurePalettes.KeysOf(desert);
			Assert.That(keys.Count, Is.EqualTo(1), "two roles in one adobe are one key; white sandstone is sandstone's own (kit) key, made with the kit");
			Assert.That(keys[0].Material, Is.EqualTo(StructureMaterial.Adobe));
			Assert.That(keys[0].MaterialName(StructureFinish.None), Is.EqualTo("Structure_Adobe_CC9966FF"));
			Assert.That(keys[0].MaterialName(StructureFinish.Mossy), Is.EqualTo("Structure_Adobe_CC9966FF_Mossy"));
			Assert.That(StructurePalettes.Resolve(desert, StructureStyle.Stone, new StructureSlot(StructureMaterial.Stone, BiomeStructureRole.Wall)),
				Is.EqualTo(StructureSurfaceKey.Kit(StructureMaterial.Sandstone)), "a stone house's walls in sandstone");
			Assert.That(StructurePalettes.Resolve(desert, StructureStyle.Timber, new StructureSlot(StructureMaterial.Timber, BiomeStructureRole.Trim)),
				Is.EqualTo(StructureSurfaceKey.Kit(StructureMaterial.Timber)), "its frame as the kit's");

			// The same key from two palettes, or a tint a hair apart, is one material.
			BiomeStructurePalette again = new BiomeArtSpec.Entry { Biome = "Badlands" }
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, new Color(0.8f + 1e-4f, 0.6f, 0.4f))
				.StructurePalette();
			Assert.That(StructurePalettes.KeysOf(new[] { desert, again, BiomeStructurePalette.Kit(), null }).Count, Is.EqualTo(keys.Count));

			// A surface variant is a key of its own, named after it.
			var variant = new StructureSurfaceKey(StructureMaterial.Adobe, adobe, "desert 1/a");
			Assert.That(variant.Surface, Is.EqualTo("desert-1-a"), "only letters, digits and '-' in a name");
			Assert.That(variant.IsKit, Is.False);
			Assert.That(variant.MaterialName(StructureFinish.Algae), Is.EqualTo("Structure_Adobe_CC9966FF_desert-1-a_Algae"));
			Assert.That(variant, Is.Not.EqualTo(keys[0]));
			Assert.That(new StructureSurfaceKey(StructureMaterial.Plaster, Color.white, "  ").IsKit, Is.True, "blank surface is the shared one");
			// A kit material in another tint is not the kit's.
			Assert.That(new StructureSurfaceKey(StructureMaterial.Timber, new Color(0.5f, 0.4f, 0.3f), null).IsKit, Is.False);
		}

		[Test]
		public void SpecPalettes_NeedNoMaterialBeyondTheKitsToday()
		{
			// The authored rounds build in their own materials (legacy equality only): every other biome builds as the kit until its round.
			Assert.That(BiomeArtSpec.Entries.Length, Is.GreaterThan(80));
			var palettes = BiomeArtSpec.Entries.Where(e => !BiomeAuthoredRounds.Contains(e.Biome)).Select(e => e.StructurePalette()).ToList();
			Assert.That(StructurePalettes.KeysOf(palettes), Is.Empty, "every unauthored biome builds as the kit until its round");
			// The catalogue lists every kit material (the new ten too) in every finish, beside the authored rounds' own.
			string[] structureMaterials = ProceduralArtCatalogue.StructureWrapperPaths()
				.Where(p => p.Contains("/" + StructurePieces.Prefix) && p.EndsWith(".mat", StringComparison.Ordinal)).ToArray();
			int kit = Enum.GetValues(typeof(StructureMaterial)).Length * StructurePieces.Finishes.Length;
			foreach (StructureMaterial m in AllMaterials)
			{
				Assert.That(structureMaterials, Does.Contain(ProceduralArtCatalogue.MaterialPath(StructurePieces.MaterialName(m, StructureFinish.Charred))), m.ToString());
			}
			Assert.That(structureMaterials.Length, Is.GreaterThanOrEqualTo(kit));
		}

		[Test]
		public void BuildsAll_GivesEveryStyleThePart()
		{
			BiomeStructurePalette p = new BiomeArtSpec.Entry { Biome = "Fen" }.BuildsAll(BiomeStructureRole.Roof, BiomeStructureMaterial.Reed).StructurePalette();
			foreach (BiomeStructureStyle style in BiomeStructurePalette.Styles)
			{
				Assert.That(p.For(style, BiomeStructureRole.Roof).Material, Is.EqualTo(BiomeStructureMaterial.Reed), style.ToString());
				Assert.That(p.For(style, BiomeStructureRole.ShingleRoof), Is.EqualTo(BiomeStructurePalette.KitSurface(style, BiomeStructureRole.ShingleRoof)));
			}
			Assert.That(p.Surfaces.Count, Is.EqualTo(BiomeStructurePalette.Kit().Surfaces.Count), "replaced in place, not added");
		}

		[Test]
		public void SlotOrder_IsMaterialThenPart()
		{
			var slots = new[]
			{
				new StructureSlot(StructureMaterial.Plaster, BiomeStructureRole.UpperWall), new StructureSlot(StructureMaterial.Timber, BiomeStructureRole.ShingleRoof),
				new StructureSlot(StructureMaterial.Timber, null), new StructureSlot(StructureMaterial.Plaster, BiomeStructureRole.Wall), new StructureSlot(StructureMaterial.Timber, BiomeStructureRole.Trim),
			};
			Array.Sort(slots);
			Assert.That(slots.Select(s => s.ToString()), Is.EqualTo(new[] { "Timber", "Timber/ShingleRoof", "Timber/Trim", "Plaster/Wall", "Plaster/UpperWall" }));
		}

		[Test]
		public void PrefabNames_ParseTheirFinish()
		{
			Assert.That(StructurePieces.TryParsePrefab("Structure_Tavern_1_Mossy", out StructurePiece piece, out int variant, out StructureFinish finish), Is.True);
			Assert.That((piece.Id, variant, finish), Is.EqualTo(("Tavern", 1, StructureFinish.Mossy)));
			Assert.That(StructurePieces.TryParsePrefab("Structure_Tavern_0", out piece, out variant, out finish), Is.True);
			Assert.That((piece.Id, variant, finish), Is.EqualTo(("Tavern", 0, StructureFinish.None)));
			Assert.That(StructurePieces.TryParsePrefab("Structure_TentSmall_1_Charred", out piece, out variant, out finish), Is.True);
			Assert.That(finish, Is.EqualTo(StructureFinish.Charred));
			Assert.That(StructurePieces.TryParsePrefab("Boulder_Grey_Round", out _, out _, out _), Is.False);
		}

		// ── Wiring ────────────────────────────────────────────────────

		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";

		private static string Code(string path) => File.ReadAllText(Path.Combine(World, path));

		/// <summary>
		/// The palette reaches every piece a site lays: every prop through the site context's AddProp (which dresses it), every
		/// scene object made of a kit prefab (doors, boats, ships, mill sails) through Dress; and the sink keeps one prototype
		/// per prefab and materials. Source scan, run from the project root.
		/// </summary>
		[Test]
		public void EveryPieceASiteLays_IsLaidInItsPalette()
		{
			Assert.That(Directory.Exists(World), Is.True, "run from the project root");
			string site = Code("PointsOfInterest/PointOfInterestSiteContext.cs");
			Assert.That(site, Does.Contain("Sink?.Add(prefab, position, rotation, scale, Sink != null ? StructurePaletteDressing.MaterialsFor(prefab, Palette) : null);"));
			Assert.That(site, Does.Contain("Materials = materials != null && materials.Length > 0 ? materials : null"));
			foreach (string file in Directory.GetFiles(Path.Combine(World, "PointsOfInterest", "Features"), "*.cs"))
			{
				string text = File.ReadAllText(file);
				string name = Path.GetFileName(file);
				if (name == "WaypointFeature.cs")
				{
					continue; // a waypoint's own prefab, not a kit piece
				}
				bool makesObjects = text.Contains("Object.Instantiate(prefab") || text.Contains("PrefabUtility.InstantiatePrefab(prefab");
				Assert.That(!makesObjects || text.Contains("StructurePaletteDressing.Dress("), Is.True, $"{name} makes scene objects of pieces without dressing them in the palette");
				Assert.That(text.Contains("context.Sink.Add(") || text.Contains("sink.Add("), Is.False, $"{name} adds to the sink past AddProp (and its palette)");
			}
			Assert.That(Code("PointsOfInterest/Features/PointOfInterestDoors.cs"), Does.Contain("StructurePaletteDressing.StyleOf(door.Building)"), "a door takes its building's style");
			Assert.That(Code("PointsOfInterest/Features/PointOfInterestSeaTraffic.cs"), Does.Contain("out ship.Ship, palette)"), "ships under way are their home harbour's");
			Assert.That(Code("PointsOfInterest/Features/PointOfInterestSkyTraffic.cs"), Does.Contain("Materials = StructurePaletteDressing.MaterialsFor(prefab, palette)"), "sky docks");
			Assert.That(Code("ProceduralArt/StructurePieces.Town.cs"), Does.Contain("Under(DoorRoles, DoorLeaf)"));
			Assert.That(Code("ProceduralArt/StructurePieces.Town.cs"), Does.Contain("Under(FenceRoles, FieldFence)"));
		}

		[Test]
		public void NewGeneratorSources_AreTheStructuresAndHashed()
		{
			foreach (string file in new[] { "StructurePalettes.cs", "StructureSurfaces.Palette.cs" })
			{
				Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain(file));
				Assert.That(ProceduralArtFamilies.FamiliesOfFile(file), Is.EqualTo(new[] { ArtFamily.Structures }), file);
			}
		}
	}
}
