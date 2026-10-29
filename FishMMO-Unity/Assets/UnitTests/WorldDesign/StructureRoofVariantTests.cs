using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A roof follows its palette's roof (2026-10-18, I6e for houses; I6h for every roofed piece, sod pitched at 35°, a 60° class
	/// and a pitch a palette names): the pitch classes, which roofs follow, the variant a palette wants and the deduplicated set
	/// the art is generated for, the kit's palette laying exactly today's prefab, the names, and a cloud world's own palette and
	/// lights (empty: the kit). The pure tests need no art and no native maths; the ones that build the pieces (marked) run in
	/// Unity only.
	/// </summary>
	[TestFixture]
	public class StructureRoofVariantTests
	{
		private const int Seed = ProceduralArtCatalogue.DefaultSeed;

		private static IEnumerable<StructureMaterial> AllMaterials => (StructureMaterial[])Enum.GetValues(typeof(StructureMaterial));

		/// <summary>A copy of the kit's palette with one role of one style (every style when null) laid in another material, at a pitch.</summary>
		private static BiomeStructurePalette With(BiomeStructureRole role, BiomeStructureMaterial material, BiomeStructureStyle? style = null,
			BiomeRoofPitch pitch = BiomeRoofPitch.Material)
		{
			BiomeStructurePalette palette = BiomeStructurePalette.Kit();
			for (int i = 0; i < palette.Surfaces.Count; i++)
			{
				BiomeStructureSurface s = palette.Surfaces[i];
				if (s.Role == role && (style == null || s.Style == style.Value))
				{
					s.Material = material;
					s.Tint = Color.white;
					s.Pitch = pitch;
					palette.Surfaces[i] = s;
				}
			}
			return palette;
		}

		/// <summary>The kit's palette with every style's roofs of one role at a named pitch, their material the kit's.</summary>
		private static BiomeStructurePalette Pitched(BiomeStructureRole role, BiomeRoofPitch pitch)
		{
			BiomeStructurePalette palette = BiomeStructurePalette.Kit();
			for (int i = 0; i < palette.Surfaces.Count; i++)
			{
				BiomeStructureSurface s = palette.Surfaces[i];
				if (s.Role == role)
				{
					s.Pitch = pitch;
					palette.Surfaces[i] = s;
				}
			}
			return palette;
		}

		private static StructureRoofFollower Follower(string id, int v)
		{
			Assert.That(StructureRoofs.TryGetFollower(id, v, out StructureRoofFollower f), Is.True, $"{id} v{v} follows");
			return f;
		}

		// ── Pitch classes ─────────────────────────────────────────────

		[Test]
		public void Pitches_AreTheMaterialsLeastPitch_OnlyMudIsFlat_AndSodIsPitched()
		{
			Assert.That(StructureRoofs.PitchOf(StructureMaterial.Thatch), Is.EqualTo(StructureRoofPitch.Pitch50));
			Assert.That(StructureRoofs.PitchOf(StructureMaterial.Reed), Is.EqualTo(StructureRoofPitch.Pitch50));
			Assert.That(StructureRoofs.PitchOf(StructureMaterial.Timber), Is.EqualTo(StructureRoofPitch.Pitch45));
			Assert.That(StructureRoofs.PitchOf(StructureMaterial.Shingle), Is.EqualTo(StructureRoofPitch.Pitch45));
			Assert.That(StructureRoofs.PitchOf(StructureMaterial.BoneWork), Is.EqualTo(StructureRoofPitch.Pitch45));
			Assert.That(StructureRoofs.PitchOf(StructureMaterial.Slate), Is.EqualTo(StructureRoofPitch.Pitch40));
			Assert.That(StructureRoofs.PitchOf(StructureMaterial.Sandstone), Is.EqualTo(StructureRoofPitch.Pitch35));
			Assert.That(StructureRoofs.PitchOf(StructureMaterial.IceBlock), Is.EqualTo(StructureRoofPitch.Pitch35));
			Assert.That(StructureRoofs.PitchOf(StructureMaterial.Bamboo), Is.EqualTo(StructureRoofPitch.Pitch30));
			Assert.That(StructureRoofs.PitchOf(StructureMaterial.Log), Is.EqualTo(StructureRoofPitch.Pitch30));
			Assert.That(StructureRoofs.PitchOf(StructureMaterial.Marble), Is.EqualTo(StructureRoofPitch.Pitch30));
			Assert.That(StructureRoofs.PitchOf(StructureMaterial.Adobe), Is.EqualTo(StructureRoofPitch.Flat), "only mud brick gives a flat roof");
			Assert.That(StructureRoofs.PitchOf(StructureMaterial.Earth), Is.EqualTo(StructureRoofPitch.Pitch35), "sod and turf are pitched (2026-10-18, decision 30)");
			Assert.That(StructurePieces.MinimumPitch(StructureMaterial.Earth), Is.EqualTo(35f));
			foreach (StructureMaterial m in AllMaterials)
			{
				StructureRoofPitch pitch = StructureRoofs.PitchOf(m);
				Assert.That(pitch == StructureRoofPitch.Flat, Is.EqualTo(m == StructureMaterial.Adobe), $"{m}: flat only for mud brick");
				if (pitch != StructureRoofPitch.Flat)
				{
					Assert.That(StructureRoofs.Degrees(pitch), Is.EqualTo(StructurePieces.MinimumPitch(m)), $"{m}: its class is its least pitch");
				}
				Assert.That(pitch, Is.Not.EqualTo(StructureRoofPitch.Pitch60), $"{m}: no material wants 60° of itself; a palette names it");
			}
			Assert.That(StructureRoofs.Pitches.Select(StructureRoofs.Degrees), Is.EqualTo(new[] { 0f, 30f, 35f, 40f, 45f, 50f, 60f }));
			Assert.That(StructureRoofs.Pitches.Where(StructureRoofs.Soft), Is.EqualTo(new[] { StructureRoofPitch.Pitch50 }), "only thatch's pitch is laid thick; 60° shakes are laid in courses");
			Assert.That((int)StructureRoofPitch.Flat, Is.EqualTo(0));
			Assert.That((int)StructureRoofPitch.Pitch50, Is.EqualTo(5), "append-only: the names are in paths");
			Assert.That((int)StructureRoofPitch.Pitch60, Is.EqualTo(6), "appended (2026-10-18, decision 33), and the steepest: Rise orders classes by value");
			// The ladder from degrees to a class: the steepest class reached, to half a degree.
			Assert.That(StructureRoofs.ClassOf(0f), Is.EqualTo(StructureRoofPitch.Flat));
			Assert.That(StructureRoofs.ClassOf(31f), Is.EqualTo(StructureRoofPitch.Pitch30));
			Assert.That(StructureRoofs.ClassOf(44.6f), Is.EqualTo(StructureRoofPitch.Pitch45));
			Assert.That(StructureRoofs.ClassOf(55f), Is.EqualTo(StructureRoofPitch.Pitch50));
			Assert.That(StructureRoofs.ClassOf(59.6f), Is.EqualTo(StructureRoofPitch.Pitch60));
			foreach (StructureRoofPitch p in StructureRoofs.Pitches)
			{
				Assert.That(StructureRoofs.ClassOf(StructureRoofs.Degrees(p)), Is.EqualTo(p), p.ToString());
			}
		}

		/// <summary>The runtime mirror a palette names its pitch with: "the material's own", then the kit's classes, name for name.</summary>
		[Test]
		public void PalettePitch_MirrorsTheClasses_OneOn_AndNamesThePitch()
		{
			var mirror = (BiomeRoofPitch[])Enum.GetValues(typeof(BiomeRoofPitch));
			Assert.That(mirror[0], Is.EqualTo(BiomeRoofPitch.Material));
			Assert.That((int)BiomeRoofPitch.Material, Is.EqualTo(0), "a palette entry written before 2026-10-18 reads as the material's own");
			Assert.That(mirror.Skip(1).Select(m => m.ToString()), Is.EqualTo(StructureRoofs.Pitches.Select(p => p.ToString())), "append-only, in step with StructureRoofPitch");
			Assert.That(StructureRoofs.Named(BiomeRoofPitch.Material), Is.Null);
			foreach (StructureRoofPitch p in StructureRoofs.Pitches)
			{
				Assert.That(StructureRoofs.Named((BiomeRoofPitch)((int)p + 1)), Is.EqualTo(p));
			}
			// An entry's pitch: the one it names, else its material's.
			var shingle = new BiomeStructureSurface(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, Color.white);
			Assert.That(shingle.Pitch, Is.EqualTo(BiomeRoofPitch.Material));
			Assert.That(StructureRoofs.PitchOf(shingle), Is.EqualTo(StructureRoofPitch.Pitch45));
			shingle.Pitch = BiomeRoofPitch.Pitch60;
			Assert.That(StructureRoofs.PitchOf(shingle), Is.EqualTo(StructureRoofPitch.Pitch60), "the Carpathian pastures' shakes");
			shingle.Pitch = BiomeRoofPitch.Pitch30;
			Assert.That(StructureRoofs.PitchOf(shingle), Is.EqualTo(StructureRoofPitch.Pitch30), "the Valley's low shingle roofs");
			shingle.Pitch = BiomeRoofPitch.Flat;
			Assert.That(StructureRoofs.PitchOf(shingle), Is.EqualTo(StructureRoofPitch.Flat));
		}

		/// <summary>
		/// A pitch at the material's own leaves every authored entry's signature (the authoring's mark) as it was; a named one
		/// signs, and is no longer the kit's.
		/// </summary>
		[Test]
		public void PalettePitch_SignsOnlyWhenNamed_AndANamedPitchIsNotTheKit()
		{
			var plain = new BiomeStructureSurface(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, Color.white, "fen");
			Assert.That(plain.Signature(), Is.EqualTo("Stone/Roof=Thatch(1,1,1,1)@fen"), "the format every mark was written in");
			Assert.That(BiomeStructurePalette.Kit().Signature(), Does.Not.Contain("^"));
			BiomeStructureSurface named = plain;
			named.Pitch = BiomeRoofPitch.Pitch60;
			Assert.That(named.Signature(), Is.EqualTo("Stone/Roof=Thatch(1,1,1,1)@fen^Pitch60"));
			Assert.That(named.Signature(), Is.Not.EqualTo(plain.Signature()), "a hand-named pitch is an edit the authoring keeps");
			BiomeStructurePalette kit = BiomeStructurePalette.Kit();
			Assert.That(kit.BuildsAsKit(BiomeStructureStyle.Timber), Is.True);
			BiomeStructurePalette steep = Pitched(BiomeStructureRole.ShingleRoof, BiomeRoofPitch.Pitch60);
			Assert.That(steep.BuildsAsKit(BiomeStructureStyle.Timber), Is.False, "a named pitch is not the kit's");
			Assert.That(StructurePalettes.KeysOf(steep), Is.Empty, "a pitch costs no material");
			Assert.That(steep.Clone().Signature(), Is.EqualTo(steep.Signature()), "the pitch is copied with the entry");
		}

		[Test]
		public void Rise_IsThePitchs_NeverPastTheKitsTheOtherWay()
		{
			// A church: span 7, its builder's rise 4.2 over the kit's 45° (3.5).
			Assert.That(StructureRoofs.Rise(StructureRoofPitch.Pitch45, 4.2f, StructureRoofPitch.Pitch50, 3.5f), Is.EqualTo(4.2f), "steeper never lower than the kit");
			Assert.That(StructureRoofs.Rise(StructureRoofPitch.Pitch45, 4.2f, StructureRoofPitch.Pitch30, 3.5f), Is.EqualTo(Mathf.Tan(30f * Mathf.Deg2Rad) * 3.5f).Within(1e-5f));
			// A thatched cottage (half span 4.5 at 50°) re-pitched for slate.
			float kit = Mathf.Tan(50f * Mathf.Deg2Rad) * 4.5f;
			Assert.That(StructureRoofs.Rise(StructureRoofPitch.Pitch50, kit, StructureRoofPitch.Pitch40, 4.5f), Is.EqualTo(Mathf.Tan(40f * Mathf.Deg2Rad) * 4.5f).Within(1e-5f));
			foreach (StructureRoofPitch k in StructureRoofs.Pitches.Where(p => p != StructureRoofPitch.Flat))
			{
				foreach (StructureRoofPitch p in StructureRoofs.Pitches.Where(p => p != StructureRoofPitch.Flat))
				{
					foreach (float half in new[] { 2.5f, 3.5f, 4.5f, 8f })
					{
						float kitRise = Mathf.Max(2f, Mathf.Tan(StructureRoofs.Degrees(k) * Mathf.Deg2Rad) * half);
						float rise = StructureRoofs.Rise(k, kitRise, p, half);
						if (p > k) Assert.That(rise, Is.GreaterThanOrEqualTo(kitRise), $"{k}→{p} at {half}");
						if (p < k) Assert.That(rise, Is.LessThanOrEqualTo(kitRise), $"{k}→{p} at {half}");
					}
				}
			}
		}

		// ── Which roofs follow ────────────────────────────────────────

		/// <summary>
		/// Every roofed piece follows (2026-10-18, decision 30): the houses' roofs (thatch, planks, and the turf longhouse's sod), then
		/// the roofs pieces build for themselves, each with the rise its builder gives it over its run.
		/// </summary>
		[Test]
		public void Followers_AreEveryRoofOfTheKit_ThatchPlanksOrTurf()
		{
			Assert.That(StructureRoofs.Followers.Length, Is.EqualTo(71), "34, the 26 roofs of the town's other buildings, the 6 of the cathedral and the outskirts, the 4 of the palace and the corner tower's (2026-10-19)");
			Assert.That(StructureRoofs.Followers.Count(f => f.Shape == StructureRoofShape.House), Is.EqualTo(33),
				"the 18 house roofs of I6e, the turf longhouse, 13 house-kit roofs of the town's other buildings and the shepherd's turf bothy (2026-10-19)");
			var seen = new HashSet<(string, int)>();
			foreach (StructureRoofFollower f in StructureRoofs.Followers)
			{
				(string id, int v, StructureMaterial roof) = f;
				StructurePiece piece = StructurePieces.Find(id);
				Assert.That(piece, Is.Not.Null, id);
				Assert.That(seen.Add((id, v)), Is.True, $"{id} v{v} listed twice");
				Assert.That(v, Is.InRange(0, piece.Variants - 1), id);
				Assert.That(piece.RuinOf, Is.Null, $"{id}: a ruin has no roof to follow");
				Assert.That(piece.Style, Is.EqualTo(StructureStyle.Timber).Or.EqualTo(StructureStyle.Stone), id);
				Assert.That(StructureRoofs.Follows(roof), Is.True, $"{id} v{v}");
				BiomeStructureRole role = StructureRoofs.RoleOf(piece.Style, roof);
				BiomeStructureRole expected = roof == StructureMaterial.Thatch ? BiomeStructureRole.Roof
					: roof == StructureMaterial.Earth ? BiomeStructureRole.Turf : BiomeStructureRole.ShingleRoof;
				Assert.That(role, Is.EqualTo(expected), $"{id} v{v}");
				Assert.That(StructureParts.Fits(piece.Style, role, roof), Is.True, $"{id} v{v}: the roof plays its role as built");
				Assert.That(StructureRoofs.TryFollower(piece, v, out StructureMaterial back) && back == roof, Is.True);
				Assert.That(f.KitRise >= 0f && f.Half >= 0f && (f.KitRise > 0f) == (f.Half > 0f), Is.True, $"{f}: a rise and its run, or neither");
				if (f.Shape != StructureRoofShape.House)
				{
					Assert.That(f.Half, Is.GreaterThan(0f), $"{f}: a piece's own roof gives its rise and run");
					Assert.That(f.KitDegrees, Is.InRange(25f, 60f), $"{f}: a roof's slope");
				}
			}
			// The roofs pieces build for themselves (2026-10-18): the well's, the shrines', the smithy's, the sawmill's, the
			// granary's, the towers' and the keep's, the round stilt hut's and the thatched dovecote's.
			foreach ((string id, int v) in new[] { ("Well", 0), ("Shrine", 0), ("Shrine", 1), ("Smithy", 0), ("Smithy", 1), ("Sawmill", 0), ("Sawmill", 1),
				("Granary", 0), ("Granary", 1), ("Watchtower", 0), ("Watchtower", 1), ("WallTower", 1), ("Keep", 1), ("StiltHut", 1), ("Dovecote", 1), ("Longhouse", 1) })
			{
				Assert.That(StructureRoofs.TryFollower(StructurePieces.Find(id), v, out _), Is.True, $"{id} v{v} follows its palette");
			}
			// What does not follow: a tent's cloth, the lean-tos, lids and awnings, the stone pediments and the stone cone, the
			// lighthouse's iron cap, a windmill's turning cap, the open-topped towers, and every ruin.
			Assert.That(StructureRoofs.Follows(StructureMaterial.Canvas), Is.False);
			Assert.That(StructureRoofs.Follows(StructureMaterial.Earth), Is.True, "the turf longhouse's sod roof follows (2026-10-18)");
			foreach ((string id, int v) in new[] { ("TentMedium", 0), ("TentLarge", 0), ("LeanTo", 0), ("NoticeBoard", 0), ("MarketStall", 0), ("Woodpile", 0),
				("SawPit", 1), ("Apiary", 0), ("TownTemple", 0), ("Mausoleum", 0), ("Dovecote", 0), ("Lighthouse", 0), ("MillCap", 0), ("MillCap", 1),
				("WallTower", 0), ("Keep", 0), ("Well", 1) })
			{
				Assert.That(StructurePieces.Find(id), Is.Not.Null, id);
				Assert.That(StructureRoofs.TryFollower(StructurePieces.Find(id), v, out _), Is.False, $"{id} v{v} keeps the kit's roof");
			}
			foreach (StructurePiece ruin in StructurePieces.All.Where(p => p.RuinOf != null))
			{
				Assert.That(StructureRoofs.TryFollower(ruin, 0, out _), Is.False, ruin.Id);
			}
		}

		/// <summary>
		/// A roof a piece builds for itself (or a house built steeper than its class) that a class would leave exactly as the kit
		/// builds it asks for no variant: a low well roof asked for slate's 40° keeps its 31°; a thatched gable asked for 45° is
		/// re-laid in courses, so it is one; a turned cone has no grain to re-lay.
		/// </summary>
		[Test]
		public void SameAsKit_IsARiseTheKitAlreadyHas_AndTheSameGrain()
		{
			StructureRoofFollower well = Follower("Well", 0), shrine = Follower("Shrine", 0), watch = Follower("Watchtower", 0), turf = Follower("Longhouse", 1);
			Assert.That(well.KitDegrees, Is.EqualTo(Mathf.Atan2(0.6f, 1f) * Mathf.Rad2Deg).Within(1e-4f));
			Assert.That(StructureRoofs.SameAsKit(well, StructureRoofPitch.Pitch40), Is.True, "31° is no steeper than slate's 40°: the kit's roof");
			Assert.That(StructureRoofs.SameAsKit(well, StructureRoofPitch.Pitch35), Is.True);
			Assert.That(StructureRoofs.SameAsKit(well, StructureRoofPitch.Pitch30), Is.False, "30° is lower than the well's 31°");
			Assert.That(StructureRoofs.SameAsKit(well, StructureRoofPitch.Pitch60), Is.False);
			Assert.That(StructureRoofs.SameAsKit(well, StructureRoofPitch.Flat), Is.False);
			// A thatched gable (40°) asked for 45°: the same rise, but laid in courses, not thatch's grain.
			Assert.That(StructureRoofs.SameAsKit(shrine, StructureRoofPitch.Pitch45), Is.False);
			// A thatched pyramid (36°) asked for 40° or 45°: the same rise, and a turned roof has no grain to turn.
			Assert.That(StructureRoofs.SameAsKit(watch, StructureRoofPitch.Pitch45), Is.True);
			Assert.That(StructureRoofs.SameAsKit(watch, StructureRoofPitch.Pitch35), Is.False);
			// A house at its class moves with any other; the turf longhouse (42°, sod's 35°) keeps its own at 40°.
			Assert.That(StructureRoofs.Pitches.Any(p => StructureRoofs.SameAsKit(Follower("TimberHouseSmall", 0), p)), Is.False);
			Assert.That(StructureRoofs.SameAsKit(turf, StructureRoofPitch.Pitch40), Is.True);
			Assert.That(StructureRoofs.SameAsKit(turf, StructureRoofPitch.Pitch45), Is.False);
			// The church (50°, planks' 45°) asked for thatch's 50°: the same rise, laid thick: a variant.
			Assert.That(StructureRoofs.SameAsKit(Follower("Church", 0), StructureRoofPitch.Pitch50), Is.False);
			Assert.That(StructureRoofs.Wanted(StructurePieces.Find("Well"), 0, With(BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Slate)), Is.Null);
		}

		// ── The variant a palette wants ───────────────────────────────

		/// <summary>The kit's palette gives exactly today's prefab for every piece: no variant wanted, none needed, the same names.</summary>
		[Test]
		public void KitPalette_WantsNoVariant_SoEveryPieceIsTodaysPrefab()
		{
			BiomeStructurePalette kit = BiomeStructurePalette.Kit();
			foreach (StructurePiece piece in StructurePieces.All)
			{
				for (int v = 0; v < piece.Variants; v++)
				{
					Assert.That(StructureRoofs.Wanted(piece, v, kit), Is.Null, $"{piece.Id} v{v}");
					Assert.That(StructureRoofs.Wanted(piece, v, new BiomeStructurePalette()), Is.Null, $"{piece.Id} v{v}: an empty palette is the kit's");
					Assert.That(StructureRoofs.Wanted(piece, v, null), Is.Null);
					foreach (StructureFinish finish in StructurePieces.Finishes)
					{
						Assert.That(StructurePieces.PrefabName(piece.Id, v, finish, null), Is.EqualTo(StructurePieces.PrefabName(piece.Id, v, finish)));
						Assert.That(StructureKit.PrefabPath(piece.Id, v, finish, null), Is.EqualTo(StructureKit.PrefabPath(piece.Id, v, finish)));
					}
					for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
					{
						Assert.That(StructurePieces.MeshName(piece.Id, v, lod, null), Is.EqualTo(StructurePieces.MeshName(piece.Id, v, lod)));
					}
				}
			}
			Assert.That(StructureRoofs.Needed(new[] { kit, null, new BiomeStructurePalette() }), Is.Empty);
			// Reed on a thatched roof, shingles on a planked one: the same pitch, no variant.
			Assert.That(StructureRoofs.Needed(new[] { With(BiomeStructureRole.Roof, BiomeStructureMaterial.Reed), With(BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle) }), Is.Empty);
		}

		[Test]
		public void SpecPalettes_NeedNoRoofVariantToday()
		{
			// The authored rounds build their own roofs (legacy equality only): every other biome builds as the kit until its round,
			// and the generator makes a roof variant exactly when some biome's palette wants one.
			Assert.That(StructureRoofs.Needed(BiomeArtSpec.Entries.Where(e => !BiomeAuthoredRounds.Contains(e.Biome)).Select(e => e.StructurePalette())), Is.Empty,
				"every unauthored biome builds as the kit until its round");
			bool wanted = ProceduralArtCatalogue.SpecRoofs().Count > 0;
			Assert.That(ProceduralArtCatalogue.StructurePrefabNames().Any(n => n.Contains("_Flat") || n.Contains("_Pitch")), Is.EqualTo(wanted));
			Assert.That(ProceduralArtCatalogue.StructurePayloadPaths().Any(n => n.Contains("_Flat_") || n.Contains("_Pitch")), Is.EqualTo(wanted));
		}

		[Test]
		public void Wanted_IsThePitchOfThePalettesMaterialForTheRoofsRole()
		{
			StructurePiece cottage = StructurePieces.Find("TimberHouseSmall"), stone = StructurePieces.Find("StoneHouse"), church = StructurePieces.Find("Church");
			// Reed on the shingled roofs: the planked cottage goes to thatch's 50°, the thatched one is already there.
			BiomeStructurePalette reed = With(BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Reed);
			Assert.That(StructureRoofs.Wanted(cottage, 1, reed), Is.EqualTo(StructureRoofPitch.Pitch50));
			Assert.That(StructureRoofs.Wanted(cottage, 0, reed), Is.Null);
			Assert.That(StructureRoofs.Wanted(church, 0, reed), Is.EqualTo(StructureRoofPitch.Pitch50));
			// Mud brick on the thatched roofs: those go flat; the planked ones keep their pitch.
			BiomeStructurePalette adobe = With(BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe);
			Assert.That(StructureRoofs.Wanted(cottage, 0, adobe), Is.EqualTo(StructureRoofPitch.Flat));
			Assert.That(StructureRoofs.Wanted(cottage, 1, adobe), Is.Null);
			Assert.That(StructureRoofs.Wanted(stone, 0, adobe), Is.EqualTo(StructureRoofPitch.Flat));
			// Slate, sandstone, bamboo; and earth is flat too.
			Assert.That(StructureRoofs.Wanted(cottage, 0, With(BiomeStructureRole.Roof, BiomeStructureMaterial.Slate)), Is.EqualTo(StructureRoofPitch.Pitch40));
			Assert.That(StructureRoofs.Wanted(cottage, 1, With(BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Sandstone)), Is.EqualTo(StructureRoofPitch.Pitch35));
			Assert.That(StructureRoofs.Wanted(cottage, 1, With(BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Bamboo)), Is.EqualTo(StructureRoofPitch.Pitch30));
			Assert.That(StructureRoofs.Wanted(cottage, 1, With(BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Earth)), Is.EqualTo(StructureRoofPitch.Pitch35), "sod is pitched (2026-10-18)");
			// The style is the piece's own: a stone palette entry moves the stone houses, not the timber ones.
			BiomeStructurePalette stoneOnly = With(BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe, BiomeStructureStyle.Stone);
			Assert.That(StructureRoofs.Wanted(stone, 0, stoneOnly), Is.EqualTo(StructureRoofPitch.Flat));
			Assert.That(StructureRoofs.Wanted(cottage, 0, stoneOnly), Is.Null);
			// A tint alone, or a role that is no roof, changes no roof.
			BiomeStructurePalette tinted = BiomeStructurePalette.Kit();
			for (int i = 0; i < tinted.Surfaces.Count; i++)
			{
				BiomeStructureSurface s = tinted.Surfaces[i];
				s.Tint = new Color(0.5f, 0.4f, 0.3f);
				tinted.Surfaces[i] = s;
			}
			Assert.That(StructureRoofs.Needed(new[] { tinted, With(BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe) }), Is.Empty);
			// A palette for cloth (the tents) asks no house of the kit for a roof.
			Assert.That(StructureRoofs.Needed(new[] { With(BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe, BiomeStructureStyle.Hide) }), Is.Empty);
		}

		/// <summary>Every roofed piece takes its palette's roof (2026-10-18, decision 30): its own gable, pyramid, cone or hips, and the turf longhouse's sod.</summary>
		[Test]
		public void Wanted_ReachesEveryRoofedPiece_AndThePitchAPaletteNames()
		{
			StructurePiece well = StructurePieces.Find("Well"), smithy = StructurePieces.Find("Smithy"), shrine = StructurePieces.Find("Shrine"),
				keep = StructurePieces.Find("Keep"), tower = StructurePieces.Find("Watchtower"), hut = StructurePieces.Find("StiltHut"),
				longhouse = StructurePieces.Find("Longhouse"), cottage = StructurePieces.Find("TimberHouseSmall");
			BiomeStructurePalette adobe = With(BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Adobe);
			Assert.That(StructureRoofs.Wanted(well, 0, adobe), Is.EqualTo(StructureRoofPitch.Flat), "a mud-roofed well canopy");
			Assert.That(StructureRoofs.Wanted(smithy, 0, adobe), Is.EqualTo(StructureRoofPitch.Flat));
			Assert.That(StructureRoofs.Wanted(keep, 1, adobe), Is.EqualTo(StructureRoofPitch.Flat));
			Assert.That(StructureRoofs.Wanted(well, 1, adobe), Is.Null, "the windlass well has no roof");
			BiomeStructurePalette reed = With(BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Reed);
			Assert.That(StructureRoofs.Wanted(well, 0, reed), Is.EqualTo(StructureRoofPitch.Pitch50));
			Assert.That(StructureRoofs.Wanted(shrine, 1, reed), Is.EqualTo(StructureRoofPitch.Pitch50), "the wayside shrine's little roof (a stone piece)");
			Assert.That(StructureRoofs.Wanted(tower, 1, reed), Is.EqualTo(StructureRoofPitch.Pitch50));
			Assert.That(StructureRoofs.Wanted(tower, 0, reed), Is.Null, "the thatched watchtower is roofed by the Roof, not the ShingleRoof");
			Assert.That(StructureRoofs.Wanted(hut, 1, With(BiomeStructureRole.Roof, BiomeStructureMaterial.Slate)), Is.EqualTo(StructureRoofPitch.Pitch40));
			// The turf longhouse follows its Turf: sod is the kit's, thatch steeper, mud brick flat.
			Assert.That(StructureRoofs.Wanted(longhouse, 1, With(BiomeStructureRole.Turf, BiomeStructureMaterial.Earth, null, BiomeRoofPitch.Material)), Is.Null);
			Assert.That(StructureRoofs.Wanted(longhouse, 1, With(BiomeStructureRole.Turf, BiomeStructureMaterial.Thatch)), Is.EqualTo(StructureRoofPitch.Pitch50));
			Assert.That(StructureRoofs.Wanted(longhouse, 1, With(BiomeStructureRole.Turf, BiomeStructureMaterial.Adobe)), Is.EqualTo(StructureRoofPitch.Flat));
			Assert.That(StructureRoofs.Wanted(longhouse, 0, With(BiomeStructureRole.Turf, BiomeStructureMaterial.Adobe)), Is.Null, "the timber longhouse is thatched");
			// A pitch the palette names: the Valley's 30° shingles on every planked roof, the Carpathian 60° shakes.
			BiomeStructurePalette valley = With(BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, null, BiomeRoofPitch.Pitch30);
			Assert.That(StructureRoofs.Wanted(cottage, 1, valley), Is.EqualTo(StructureRoofPitch.Pitch30));
			Assert.That(StructureRoofs.Wanted(well, 0, valley), Is.EqualTo(StructureRoofPitch.Pitch30));
			Assert.That(StructureRoofs.Wanted(keep, 1, valley), Is.EqualTo(StructureRoofPitch.Pitch30));
			Assert.That(StructureRoofs.Wanted(cottage, 0, valley), Is.Null, "the thatched cottage is roofed by the Roof");
			BiomeStructurePalette goral = Pitched(BiomeStructureRole.ShingleRoof, BiomeRoofPitch.Pitch60);
			Assert.That(StructureRoofs.Wanted(cottage, 1, goral), Is.EqualTo(StructureRoofPitch.Pitch60));
			Assert.That(StructureRoofs.Wanted(StructurePieces.Find("Church"), 0, goral), Is.EqualTo(StructureRoofPitch.Pitch60));
			Assert.That(StructureRoofs.Wanted(smithy, 1, goral), Is.EqualTo(StructureRoofPitch.Pitch60));
			// Named at the kit's own class: no variant.
			Assert.That(StructureRoofs.Needed(new[] { Pitched(BiomeStructureRole.ShingleRoof, BiomeRoofPitch.Pitch45), Pitched(BiomeStructureRole.Roof, BiomeRoofPitch.Pitch50),
				Pitched(BiomeStructureRole.Turf, BiomeRoofPitch.Pitch35) }), Is.Empty);
			// Every roofed piece of the Valley's palette at 30°: each planked roof once, the low ones that are already 30° or less none.
			List<StructureRoofVariant> low = StructureRoofs.Needed(new[] { valley });
			Assert.That(low.All(r => r.Pitch == StructureRoofPitch.Pitch30), Is.True);
			Assert.That(low.Select(r => (r.Piece, r.Variant)), Is.EquivalentTo(StructureRoofs.Followers.Where(f => f.Roof == StructureMaterial.Timber).Select(f => (f.Piece, f.Variant))));
		}

		[Test]
		public void Needed_IsEachVariantOnce_InFirstUseOrder()
		{
			BiomeStructurePalette adobe = With(BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe);
			List<StructureRoofVariant> one = StructureRoofs.Needed(new[] { adobe });
			var thatched = StructureRoofs.Followers.Where(f => f.Roof == StructureMaterial.Thatch).Select(f => new StructureRoofVariant(f.Piece, f.Variant, StructureRoofPitch.Flat)).ToList();
			Assert.That(one, Is.EqualTo(thatched), "every thatched house, flat, in the followers' order");
			// The same palette twice, an equal one, and nulls: the same set.
			Assert.That(StructureRoofs.Needed(new[] { adobe, null, With(BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe), adobe }), Is.EqualTo(one));
			Assert.That(StructureRoofs.Needed(new[] { adobe, With(BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, null, BiomeRoofPitch.Flat) }), Is.EqualTo(one), "a named flat pitch is mud brick's");
			// Two palettes asking different roofs: the union, the first palette's first.
			BiomeStructurePalette slate = With(BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Slate);
			List<StructureRoofVariant> both = StructureRoofs.Needed(new[] { adobe, slate });
			int planked = StructureRoofs.Followers.Count(f => f.Roof == StructureMaterial.Timber);
			int sameAtSlate = StructureRoofs.Followers.Count(f => f.Roof == StructureMaterial.Timber && StructureRoofs.SameAsKit(f, StructureRoofPitch.Pitch40));
			Assert.That(sameAtSlate, Is.GreaterThan(0), "a low planked roof (the well's, the smithy's) is no steeper than slate's 40° already");
			Assert.That(both.Count, Is.EqualTo(one.Count + planked - sameAtSlate));
			Assert.That(both.Take(one.Count), Is.EqualTo(one));
			Assert.That(both.Skip(one.Count).All(r => r.Pitch == StructureRoofPitch.Pitch40), Is.True);
			Assert.That(both.Distinct().Count(), Is.EqualTo(both.Count), "no variant twice");
			// The most there can ever be: every follower at every pitch but its own and those it would come out of as the kit's.
			var every = new List<BiomeStructurePalette>();
			foreach (BiomeRoofPitch pitch in (BiomeRoofPitch[])Enum.GetValues(typeof(BiomeRoofPitch)))
			{
				foreach (BiomeStructureRole role in new[] { BiomeStructureRole.Roof, BiomeStructureRole.ShingleRoof, BiomeStructureRole.Turf })
				{
					every.Add(Pitched(role, pitch));
				}
			}
			List<StructureRoofVariant> all = StructureRoofs.Needed(every);
			Assert.That(all, Is.EquivalentTo(StructureRoofs.EveryVariant()));
			Assert.That(all.Count, Is.EqualTo(410), "71 roofs × 6 pitches, less 16 a class leaves as the kit builds them (2026-10-18; 26 roofs of the town's other buildings, the keep's and the square tower's re-pitched, 6 of the cathedral and the outskirts, 4 of the palace and the corner tower's, 2026-10-19): the art's worst case");
			Assert.That(all.Count, Is.LessThan(StructureRoofs.Followers.Length * (StructureRoofs.Pitches.Length - 1)));
		}

		// ── Names ─────────────────────────────────────────────────────

		[Test]
		public void RoofVariantNames_ParseTheirPieceVariantFinishAndRoof()
		{
			Assert.That(StructurePieces.PrefabName("Tavern", 0, StructureFinish.Mossy, StructureRoofPitch.Flat), Is.EqualTo("Structures/Structure_Tavern_0_Flat_Mossy"));
			Assert.That(StructurePieces.PrefabName("Tavern", 1, StructureFinish.None, StructureRoofPitch.Pitch50), Is.EqualTo("Structures/Structure_Tavern_1_Pitch50"));
			Assert.That(StructurePieces.MeshName("Church", 1, 2, StructureRoofPitch.Pitch30), Is.EqualTo("Structure_Church_1_Pitch30_LOD2"));
			foreach (StructureRoofPitch pitch in StructureRoofs.Pitches)
			{
				Assert.That(StructureRoofs.TryParseSuffix(StructureRoofs.Suffix(pitch), out StructureRoofPitch back) && back == pitch, Is.True, pitch.ToString());
				foreach (StructureFinish finish in StructurePieces.Finishes)
				{
					string name = StructurePieces.PrefabName("StoneHouse", 1, finish, pitch);
					name = name.Substring(name.LastIndexOf('/') + 1);
					Assert.That(StructurePieces.TryParsePrefab(name, out StructurePiece piece, out int variant, out StructureFinish f, out StructureRoofPitch? roof), Is.True, name);
					Assert.That((piece.Id, variant, f, roof), Is.EqualTo(("StoneHouse", 1, finish, (StructureRoofPitch?)pitch)), name);
					// Every reader that wants only the piece and the variant reads a roof variant as its piece.
					Assert.That(StructurePieces.TryParsePrefab(name, out piece, out variant), Is.True);
					Assert.That((piece.Id, variant), Is.EqualTo(("StoneHouse", 1)));
				}
			}
			Assert.That(StructurePieces.TryParsePrefab("Structure_Tavern_1_Mossy", out _, out _, out _, out StructureRoofPitch? none), Is.True);
			Assert.That(none, Is.Null, "the kit's prefab has the kit's roof");
			Assert.That(StructureRoofs.TryParseSuffix("Pitch55", out _), Is.False);
			Assert.That(StructureRoofs.TryParseSuffix("Mossy", out _), Is.False);
			Assert.That(StructureRoofs.Pitches.Select(StructureRoofs.Suffix).Distinct().Count(), Is.EqualTo(StructureRoofs.Pitches.Length));
		}

		// ── Cloud worlds ──────────────────────────────────────────────

		/// <summary>A cloud world's own settings, untouched, are the kit: no palette (no materials, no roofs), no lights.</summary>
		[Test]
		public void CloudWorld_ByDefault_IsTheKit()
		{
			var settings = new CloudWorldStructures();
			Assert.That(settings.BuildsAsKit, Is.True);
			Assert.That(settings.Laid, Is.Null, "the cut is given no palette: the kit's own materials, as before");
			Assert.That(CloudWorldStructures.PaletteOf(null), Is.Null);
			Assert.That(settings.TryLight(CloudWorldWayPlace.BridgeEnd, out _), Is.False);
			Assert.That(settings.TryLight(CloudWorldWayPlace.LandingStage, out _), Is.False);
			foreach (BiomeStructureStyle style in BiomeStructurePalette.Styles)
			{
				foreach (BiomeStructureRole role in BiomeStructurePalette.Roles)
				{
					Assert.That(settings.Palette.For(style, role), Is.EqualTo(BiomeStructurePalette.KitSurface(style, role)), $"{style}/{role}");
				}
				Assert.That(settings.Palette.BuildsAsKit(style), Is.True);
			}
			Assert.That(StructurePalettes.KeysOf(settings.Palette), Is.Empty);
			Assert.That(StructureRoofs.Needed(new[] { settings.Palette }), Is.Empty);

			// Filled with the kit's entries to edit: still the kit, and a filled palette is left alone.
			Assert.That(settings.FillWithKit(), Is.True);
			Assert.That(settings.Palette.Surfaces.Count, Is.EqualTo(BiomeStructurePalette.Kit().Surfaces.Count));
			Assert.That(StructurePalettes.KeysOf(settings.Palette), Is.Empty);
			Assert.That(StructureRoofs.Needed(new[] { settings.Palette }), Is.Empty);
			Assert.That(settings.FillWithKit(), Is.False);
			Assert.That(settings.Laid, Is.SameAs(settings.Palette), "a named palette is laid");

			// A light named for a place lights that place only.
			settings.WayLights.Add(new CloudWorldWayLight(CloudWorldWayPlace.BridgeEnd, BiomeWayLightKind.TorchPost));
			Assert.That(settings.TryLight(CloudWorldWayPlace.BridgeEnd, out CloudWorldWayLight light) && light.Light == BiomeWayLightKind.TorchPost, Is.True);
			Assert.That(light.PieceQuery, Is.EqualTo("=TorchPost"));
			Assert.That(settings.TryLight(CloudWorldWayPlace.LandingStage, out _), Is.False);
			Assert.That((int)CloudWorldWayPlace.BridgeEnd, Is.EqualTo(0));
			Assert.That((int)CloudWorldWayPlace.LandingStage, Is.EqualTo(1));
		}

		[Test]
		public void BridgeEndLight_StandsBesideTheLanding_OnTheIslandsGrass()
		{
			var island = new CloudIslands.Island { Centre = new Vector2(0f, 0f), Radius = 40f };
			Func<float, float, float> ground = (x, z) => 12f;
			// A bridge leaving east from a post 4 m in from the outline.
			var post = new Vector3(36f, 12f, 0f);
			Assert.That(CloudWorldCut.BridgeEndLight(island, post, Vector3.right, ground, new List<Vector3> { post }, out Vector3 at), Is.True);
			Assert.That(at.y, Is.EqualTo(12f), "on the ground");
			Assert.That(at.x, Is.EqualTo(post.x - CloudWorldCut.EndLightBehind).Within(1e-4f), "behind the posts, along the landing");
			Assert.That(Mathf.Abs(at.z), Is.EqualTo(CloudWorldCut.EndLightAside).Within(1e-4f), "beside the way, clear of the posts");
			Assert.That(at.z, Is.LessThan(0f), "on the right of the way out (east: right is −z)");
			// Another bridge's post there: the other side.
			var other = new Vector3(at.x, 12f, at.z + 0.5f);
			Assert.That(CloudWorldCut.BridgeEndLight(island, post, Vector3.right, ground, new List<Vector3> { post, other }, out Vector3 left), Is.True);
			Assert.That(left.z, Is.GreaterThan(0f));
			// At the very edge (the post on the outline): no grass for it.
			Assert.That(CloudWorldCut.BridgeEndLight(island, new Vector3(39.5f, 12f, 0f), Vector3.right, ground, null, out _), Is.False);
			Assert.That(CloudWorldCut.BridgeEndLight(null, post, Vector3.right, ground, null, out _), Is.False);
		}

		// ── Wiring ────────────────────────────────────────────────────

		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";

		private static string Code(string path) => File.ReadAllText(Path.Combine(World, path));

		/// <summary>
		/// The cut lays the roof its palette wants (the site context's AddProp, before the sink; the kit's piece source), the
		/// generator writes the variants the palettes in use need (cloud worlds' too), and a cloud world's docks, airships and
		/// bridges take the world body's palette. Source scan, run from the project root.
		/// </summary>
		[Test]
		public void TheCutLaysThePalettesRoof_AndCloudWorldsTheirBodysPalette()
		{
			Assert.That(Directory.Exists(World), Is.True, "run from the project root");
			string site = Code("PointsOfInterest/PointOfInterestSiteContext.cs");
			int pick = site.IndexOf("prefab = StructurePaletteDressing.RoofVariant(prefab, Palette);", StringComparison.Ordinal);
			int sink = site.IndexOf("Sink?.Add(prefab, position, rotation, scale", StringComparison.Ordinal);
			Assert.That(pick, Is.GreaterThan(0));
			Assert.That(pick, Is.LessThan(sink), "the roof variant is picked before the prop is laid and dressed");
			Assert.That(site, Does.Contain("StructurePieces.EmittersOf(piece, variant, roof)"), "a variant's chimney smokes from its own roof");
			Assert.That(Code("PointsOfInterest/Templates/StructureKitPieceSource.cs"), Does.Contain("StructurePaletteDressing.RoofVariant(prefab, palette)"));
			string generator = Code("ProceduralArt/BiomeArtGenerator.Structures.cs");
			Assert.That(generator, Does.Contain("StructureRoofs.Needed(PalettesInUse())"));
			Assert.That(generator, Does.Contain("WriteStructurePiece(c, piece, roofs)"));
			Assert.That(generator, Does.Contain("CloudWorldStructures.Palette"), "the cloud worlds' palettes are generated for");
			string cloud = Code("CloudWorld/CloudWorldCut.cs");
			Assert.That(cloud, Does.Contain("Materials = StructurePaletteDressing.MaterialsFor(prefab, palette)"), "rope bridges");
			Assert.That(cloud, Does.Contain("structures?.Laid, structures"), "sky docks and airships");
			Assert.That(Code("SceneGenerator.cs"), Does.Contain("request.Body != null ? request.Body.CloudWorldStructures : null"));
			Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain("StructureRoofs.cs"));
			Assert.That(ProceduralArtFamilies.FamiliesOfFile("StructureRoofs.cs"), Is.EqualTo(new[] { ArtFamily.Structures }));
			// Every builder of a roof that follows lays it through the plan (StructurePieces.OwnRoof / OwnGable, or BuildHouse).
			foreach ((string file, int calls) in new[] { ("Buildings", 2), ("Towers", 2), ("Keep", 1), ("Town", 1), ("Sacred", 2), ("Mills", 3), ("Outskirts", 4), ("Palace", 4) })
			{
				string code = Code($"ProceduralArt/StructurePieces.{file}.cs");
				int n = code.Split(new[] { "OwnRoof(d, ", "OwnGable(d, " }, StringSplitOptions.None).Length - 1;
				Assert.That(n, Is.EqualTo(calls), $"StructurePieces.{file}.cs lays its pieces' own roofs through the plan");
				// A gable laid past the plan is only a stone pediment (the temple's, the mausoleum's), which keeps its order's pitch.
				foreach (string line in code.Split('\n').Where(l => l.TrimStart().StartsWith("GableRoof(d, ", StringComparison.Ordinal)))
				{
					Assert.That(line, Does.Contain("StructureMaterial.Stone").Or.Contain(" stone, "), $"{file}: a gable of its own past the plan: {line.Trim()}");
				}
			}
		}

		/// <summary>
		/// The values outside the generator's folder that bake into the art are hashed with the structures (2026-10-18, I6h): the cold
		/// glows' colours (SceneGlow.cs, the Glowlight materials) and the kit palette (BiomeStructurePalette.cs, the parts and tints and
		/// the pitches a palette names). SceneEmitter.cs is the cut's, not the art's.
		/// </summary>
		[Test]
		public void TheGlowAndThePalette_AreHashedWithTheStructures()
		{
			Assert.That(ProceduralArtPayload.ForeignSources, Is.EquivalentTo(new[] { "SceneGlow.cs", "BiomeStructurePalette.cs", "CaveCoverTypes.cs" }));
			foreach (string file in ProceduralArtPayload.ForeignSources)
			{
				Assert.That(ProceduralArtFamilies.FamiliesOfFile(file), Is.EqualTo(new[] { ArtFamily.Structures }), file);
				Assert.That(ProceduralArtPayload.SourceFiles, Does.Not.Contain(file), "outside the generator's folder: found by its script, not listed");
				Assert.That(Directory.GetFiles("Assets", file, SearchOption.AllDirectories).Length, Is.EqualTo(1), $"{file}: one script of the name, so it is found");
			}
			Assert.That(ProceduralArtPayload.ForeignSources, Does.Not.Contain("SceneEmitter.cs"));
			string generator = Code("ProceduralArt/BiomeArtGenerator.Structures.cs");
			Assert.That(generator, Does.Contain("SceneGlow.Colour(glow)"), "the glow's colour is baked into the Glowlight material");
			Assert.That(Code("ProceduralArt/ProceduralArtPayload.cs"), Does.Contain("sources.AddRange(ForeignSources);"));
		}

		// ── Built (Unity only: the builders need native maths) ────────

		/// <summary>
		/// The list of roofs that follow is what the builders do: every roof a piece's builder lays through the plan
		/// (StructureDraft.FollowingRoofs: BuildHouse, OwnRoof) is a follower and no other, with the rise and run the list gives;
		/// and a draft asked for the kit's own pitch builds the kit exactly.
		/// </summary>
		[Test]
		public void Followers_AreWhatTheBuildersLay_AndTheKitsPitchIsTheKit()
		{
			foreach (StructurePiece piece in StructurePieces.All)
			{
				for (int v = 0; v < piece.Variants; v++)
				{
					StructureDraft draft = StructurePieces.Draft(piece, v, StructurePieces.LevelCount - 1, Seed);
					bool listed = StructureRoofs.TryFollower(piece, v, out StructureMaterial roof);
					bool built = piece.RuinOf == null && draft.FollowingRoofs.Count > 0;
					Assert.That(listed, Is.EqualTo(built), $"{piece.Id} v{v}: {(built ? "builds a house roof that follows, unlisted" : "listed, but builds no such roof")}");
					if (listed)
					{
						StructureRoofs.TryGetFollower(piece, v, out StructureRoofFollower follower);
						Assert.That(draft.FollowingRoofs, Is.EqualTo(new[] { (roof, follower.KitRise, follower.Half) }), $"{piece.Id} v{v}: the material, rise and run its builder gives");
						for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
						{
							Assert.That(Positions(StructurePieces.Build(piece, v, lod, Seed, StructureRoofs.PitchOf(roof))), Is.EqualTo(Positions(StructurePieces.Build(piece, v, lod, Seed))),
								$"{piece.Id} v{v} LOD{lod}: asked for its own pitch, the kit");
						}
					}
				}
			}
		}

		/// <summary>
		/// Every roof variant is sound: valid and closed enough at every level, one material a submesh, within its piece's budget, the same doors and rooms as the
		/// kit's (only the roof differs), no wider than the kit's piece, and a flat roof no higher than the pitched one.
		/// </summary>
		[Test]
		public void EveryRoofVariant_IsSound_AndOnlyItsRoofDiffers()
		{
			foreach ((string id, int v, StructureMaterial kitRoof) in StructureRoofs.Followers)
			{
				StructurePiece piece = StructurePieces.Find(id);
				Bounds kit = StructurePieces.Build(piece, v, 0, Seed).Mesh.Bounds;
				StructureRoofs.TryGetFollower(id, v, out StructureRoofFollower follower);
				foreach (StructureRoofPitch pitch in StructureRoofs.Pitches)
				{
					if (pitch == StructureRoofs.PitchOf(kitRoof))
					{
						continue;
					}
					string name = $"{id} v{v} {StructureRoofs.Suffix(pitch)}";
					if (StructureRoofs.SameAsKit(follower, pitch))
					{
						// No variant is made: the roof at this pitch is the kit's, to the bit, at every level.
						for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
						{
							StructureMesh at = StructurePieces.Build(piece, v, lod, Seed, pitch), own = StructurePieces.Build(piece, v, lod, Seed);
							Assert.That(Positions(at), Is.EqualTo(Positions(own)), $"{name} LOD{lod}: said to be the kit's");
							Assert.That(at.Mesh.UVs, Is.EqualTo(own.Mesh.UVs), $"{name} LOD{lod}: said to be the kit's");
						}
						continue;
					}
					int previous = int.MaxValue;
					for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
					{
						StructureMesh m = StructurePieces.Build(piece, v, lod, Seed, pitch);
						List<string> problems = m.Mesh.Validate(true);
						Assert.That(problems, Is.Empty, $"{name} LOD{lod}: {(problems.Count > 0 ? problems[0] : "")}");
						int tris = m.Mesh.TriangleCount;
						Assert.That(tris, Is.LessThanOrEqualTo(previous), $"{name} LOD{lod} has more triangles than the level before it");
						if (lod == 0)
						{
							Assert.That(tris, Is.LessThanOrEqualTo(piece.TriangleBudget), $"{name}: {tris} triangles over its {piece.Size} budget");
						}
						previous = tris;
						Assert.That(m.Materials.Length, Is.EqualTo(m.Mesh.Submeshes.Count), $"{name} LOD{lod}: one material per submesh");
						Assert.That(OddEdgeShare(m.Mesh), Is.LessThan(0.02), $"{name} LOD{lod} is open");
					}
					Bounds b = StructurePieces.Build(piece, v, 0, Seed, pitch).Mesh.Bounds;
					Assert.That(b.size.x, Is.LessThanOrEqualTo(kit.size.x + 0.02f), $"{name} is wider than its piece");
					Assert.That(b.size.z, Is.LessThanOrEqualTo(kit.size.z + 0.02f), $"{name} is deeper than its piece");
					if (pitch == StructureRoofPitch.Flat)
					{
						Assert.That(b.max.y, Is.LessThanOrEqualTo(kit.max.y + 1e-3f), $"{name}: a flat roof stands no higher than the pitched one (a church's tower still tops it)");
					}
					// The same doors (the leaves are hung from them) and rooms (furnished from them) as the kit's.
					StructureDraft draft = StructurePieces.Draft(piece, v, 0, Seed, pitch);
					IReadOnlyList<StructureDoor> doors = StructurePieces.DoorsOf(piece, v);
					Assert.That(draft.Doors.Count, Is.EqualTo(doors.Count), name);
					for (int i = 0; i < doors.Count; i++)
					{
						Assert.That(draft.Doors[i].Hinge, Is.EqualTo(doors[i].Hinge), $"{name} door {i}");
						Assert.That(draft.Doors[i].Width, Is.EqualTo(doors[i].Width), $"{name} door {i}");
					}
					Assert.That(draft.Rooms.Select(r => r.Centre), Is.EqualTo(StructurePieces.RoomsOf(piece, v).Select(r => r.Centre)), name);
				}
			}
		}

		private static List<Vector3> Positions(StructureMesh m) => new List<Vector3>(m.Mesh.Positions);

		/// <summary>The share of edges (welded by position) used by an odd number of triangles: about 0 for closed solids (as StructureKitTests).</summary>
		private static double OddEdgeShare(MeshBuilder m)
		{
			var keys = new Dictionary<(long, long, long), int>();
			int Id(Vector3 p)
			{
				var k = ((long)Math.Round(p.x * 1e4), (long)Math.Round(p.y * 1e4), (long)Math.Round(p.z * 1e4));
				if (!keys.TryGetValue(k, out int id))
				{
					id = keys.Count;
					keys[k] = id;
				}
				return id;
			}
			var edges = new Dictionary<(int, int), int>();
			foreach (List<int> s in m.Submeshes)
			{
				for (int t = 0; t + 2 < s.Count; t += 3)
				{
					int a = Id(m.Positions[s[t]]), b = Id(m.Positions[s[t + 1]]), c = Id(m.Positions[s[t + 2]]);
					foreach ((int, int) e in new[] { (a, b), (b, c), (c, a) })
					{
						(int, int) k = e.Item1 < e.Item2 ? e : (e.Item2, e.Item1);
						edges.TryGetValue(k, out int n);
						edges[k] = n + 1;
					}
				}
			}
			int odd = edges.Values.Count(n => (n & 1) != 0);
			return edges.Count == 0 ? 0 : odd / (double)edges.Count;
		}
	}
}
