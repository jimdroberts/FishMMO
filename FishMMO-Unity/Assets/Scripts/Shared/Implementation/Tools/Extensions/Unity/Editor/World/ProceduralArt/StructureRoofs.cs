#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The pitch a roof is built at (2026-10-18, I6e): the classes the roof materials want
	/// (<see cref="StructurePieces.MinimumPitch"/>), flat for mud brick, and any class a palette names for its roofs
	/// (BiomeStructureSurface.Pitch, I6h: the Valley's low 30° shingles, the Carpathian pastures' 60° shakes).
	/// </summary>
	/// <remarks>
	/// Append-only: the names are in generated mesh and prefab paths (<see cref="StructureRoofs.Suffix"/>), and the order is
	/// the steepness (<see cref="StructureRoofs.Rise"/> compares classes by it). The runtime mirror is BiomeRoofPitch, one
	/// value on (its first, Material, is "the material's own").
	/// </remarks>
	public enum StructureRoofPitch : byte
	{
		/// <summary>A flat roof behind a parapet, drained by spouts: mud brick (and any roof a palette names flat).</summary>
		Flat,
		/// <summary>30°: bamboo half-culms, log roofs, marble slabs.</summary>
		Pitch30,
		/// <summary>35°: sandstone slabs, ice blocks, sod and turf (2026-10-18, I6h: earth roofs are pitched sod, not flat).</summary>
		Pitch35,
		/// <summary>40°: slates.</summary>
		Pitch40,
		/// <summary>45°: shingles and planks, bone work, and anything else laid in courses.</summary>
		Pitch45,
		/// <summary>50°: thatch and reed, laid thick and steep.</summary>
		Pitch50,
		/// <summary>
		/// 60°: the steep riven-shake roofs of the Carpathian and Goral highlands (2026-10-18, I6h, decided in the Alpine Meadow
		/// round), shedding a mountain winter's snow. No material wants it of itself; a palette names it for its roofs.
		/// </summary>
		Pitch60,
	}

	/// <summary>
	/// How a roof that follows its palette is built (2026-10-18, I6h): a house's roof (StructurePieces.BuildHouse), a gable a
	/// piece builds for itself on posts or walls (a well's, a shrine's, a smithy's), or a turned or hipped roof (a tower's
	/// pyramid, a hut's cone, a keep's hips), whose texture has no grain to turn with its covering.
	/// </summary>
	public enum StructureRoofShape : byte
	{
		House,
		Gable,
		Turned,
	}

	/// <summary>
	/// One roof that follows its palette (<see cref="StructureRoofs.Followers"/>): the piece's variant, its kit material, its shape,
	/// and the rise its builder gives it over the run its pitch is measured on, for a roof a piece builds for itself and for a
	/// house whose builder asks more rise than its material's class (the church's, the turf longhouse's); 0 and 0 for a house
	/// built at its class (BuildHouse works that rise out from its walls).
	/// </summary>
	public readonly struct StructureRoofFollower
	{
		public readonly string Piece;
		public readonly int Variant;
		public readonly StructureMaterial Roof;
		public readonly StructureRoofShape Shape;
		/// <summary>The rise the kit builds the roof at, metres (0 for a house built at its class: BuildHouse works it out).</summary>
		public readonly float KitRise;
		/// <summary>The run its pitch is measured over, metres: half a gable's span, a pyramid's apothem, a cone's radius (0 with a 0 rise).</summary>
		public readonly float Half;

		public StructureRoofFollower(string piece, int variant, StructureMaterial roof, StructureRoofShape shape = StructureRoofShape.House, float kitRise = 0f, float half = 0f)
		{
			Piece = piece;
			Variant = variant;
			Roof = roof;
			Shape = shape;
			KitRise = kitRise;
			Half = half;
		}

		/// <summary>The roof's slope as the kit builds it, degrees; 0 for a house built at its material's class.</summary>
		public float KitDegrees => Half > 0f ? Mathf.Atan2(KitRise, Half) * Mathf.Rad2Deg : 0f;

		public void Deconstruct(out string piece, out int variant, out StructureMaterial roof)
		{
			piece = Piece;
			variant = Variant;
			roof = Roof;
		}

		public override string ToString() => $"{Piece} v{Variant} ({Roof}, {Shape})";
	}

	/// <summary>One roof the art is generated at besides its kit pitch: a piece's variant and the pitch.</summary>
	public readonly struct StructureRoofVariant : IEquatable<StructureRoofVariant>
	{
		public readonly string Piece;
		public readonly int Variant;
		public readonly StructureRoofPitch Pitch;

		public StructureRoofVariant(string piece, int variant, StructureRoofPitch pitch)
		{
			Piece = piece;
			Variant = variant;
			Pitch = pitch;
		}

		public bool Equals(StructureRoofVariant other) => string.Equals(Piece, other.Piece, StringComparison.Ordinal) && Variant == other.Variant && Pitch == other.Pitch;
		public override bool Equals(object obj) => obj is StructureRoofVariant other && Equals(other);
		public override int GetHashCode() => ((Piece ?? string.Empty).GetHashCode() * 397 ^ Variant) * 31 + (int)Pitch;
		public override string ToString() => $"{Piece}_{Variant}_{StructureRoofs.Suffix(Pitch)}";
	}

	/// <summary>
	/// A roof follows its palette's roof (2026-10-18, I6e for the houses; I6h for every roofed piece, decided 2026-10-18 after
	/// I6e): a reed or thatch roof is built steep, slate at 40°, sandstone and sod at 35°, bamboo at 30°, and mud brick flat
	/// behind a parapet; a palette may name its roofs' class itself (the Valley's low 30° shingles, the Carpathian pastures' 60°
	/// shakes: BiomeStructureSurface.Pitch). Before, a palette could only lay another material on the kit's roof, so reed lay on
	/// a 45° shingle roof (and was said once a session: StructurePalettes.RoofFits).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Which roofs.</b> Every roof of thatch (its palette role the Roof), timber planks (the ShingleRoof) or turf (the Turf)
	/// that a house is built with (StructurePieces.BuildHouse: the cottages, the large and stone houses, both longhouses, the
	/// stilt huts and houses, the church, the tavern, the stables, the watermill), and every roof a piece builds for itself:
	/// the well's, the shrines', the smithy's, the sawmill's and the granary's gables, the watchtower's and the square wall
	/// tower's pyramids, the round stilt hut's and the dovecote's cones, the keep's hips (<see cref="Followers"/>). What does not
	/// follow: a tent's cloth, the lean-tos, lids and awnings (shelters of a few boards or a cloth, pitched by their poles and
	/// mostly goods), the stone pediments of the temple and the mausoleum and the dovecote's corbelled stone cone (stone, the
	/// form of their order), the church's spire (a spire, not a roof), the lighthouse's iron cap, and a windmill's cap (a
	/// turning part: its ogee and its thatched cone are carried on the windshaft, not pitched over walls).
	/// </para>
	/// <para>
	/// <b>The art.</b> Only the (piece, variant, pitch) a palette in use needs is generated (<see cref="Needed"/>, deduplicated
	/// over every spec palette, every biome template's and every cloud world's, as the palette materials are), as meshes and
	/// prefabs of their own named with the pitch (<see cref="Suffix"/>: <c>Structure_Tavern_0_Flat_Mossy</c>). The kit's own
	/// pitch is never a variant, and neither is a pitch the roof would come out of exactly as the kit's (a low well roof
	/// asked for 40° keeps its 31°, <see cref="Rise"/>): under the kit's palette every piece is exactly today's prefab.
	/// </para>
	/// <para>
	/// <b>At the cut</b> the site lays the variant its palette wants (StructurePaletteDressing.RoofVariant, through the kit's
	/// piece source and PointOfInterestSiteContext.AddProp), and dresses it in the palette's materials as any other piece.
	/// A variant is the same piece in every other way: its doors, rooms and footprint are the kit's (a flat roof has no eaves,
	/// so it is a little smaller), only its roof, its parapets and its chimney's height differ.
	/// </para>
	/// </remarks>
	public static class StructureRoofs
	{
		/// <summary>
		/// Every roof that follows its palette, by piece and variant, with its kit material and shape: the houses' roofs of thatch,
		/// timber planks or turf (StructurePieces.BuildHouse), then the roofs pieces build for themselves, with the rise their
		/// builders give them and the run it is measured over. A Unity test builds every piece and holds this list to what the
		/// builders do.
		/// </summary>
		public static readonly StructureRoofFollower[] Followers =
		{
			new StructureRoofFollower("TimberHouseSmall", 0, StructureMaterial.Thatch),
			new StructureRoofFollower("TimberHouseSmall", 1, StructureMaterial.Timber),
			new StructureRoofFollower("TimberHouseLarge", 0, StructureMaterial.Timber),
			new StructureRoofFollower("TimberHouseLarge", 1, StructureMaterial.Timber),
			new StructureRoofFollower("StoneHouse", 0, StructureMaterial.Thatch),
			new StructureRoofFollower("StoneHouse", 1, StructureMaterial.Timber),
			new StructureRoofFollower("Longhouse", 0, StructureMaterial.Thatch),
			new StructureRoofFollower("StiltHut", 0, StructureMaterial.Thatch),
			new StructureRoofFollower("StiltHouse", 0, StructureMaterial.Thatch),
			new StructureRoofFollower("StiltHouse", 1, StructureMaterial.Timber),
			new StructureRoofFollower("Church", 0, StructureMaterial.Timber, StructureRoofShape.House, 4.2f, 3.5f),
			new StructureRoofFollower("Church", 1, StructureMaterial.Timber, StructureRoofShape.House, 4.2f, 3.5f),
			new StructureRoofFollower("Tavern", 0, StructureMaterial.Timber),
			new StructureRoofFollower("Tavern", 1, StructureMaterial.Thatch),
			new StructureRoofFollower("Stables", 0, StructureMaterial.Timber),
			new StructureRoofFollower("Stables", 1, StructureMaterial.Thatch),
			new StructureRoofFollower("Watermill", 0, StructureMaterial.Timber),
			new StructureRoofFollower("Watermill", 1, StructureMaterial.Thatch),
			// 2026-10-18, I6h: the turf longhouse's sod roof, and every roof a piece builds for itself.
			new StructureRoofFollower("Longhouse", 1, StructureMaterial.Earth, StructureRoofShape.House, 3.6f, 4f),
			new StructureRoofFollower("Well", 0, StructureMaterial.Timber, StructureRoofShape.Gable, 0.6f, 1f),
			new StructureRoofFollower("Shrine", 0, StructureMaterial.Thatch, StructureRoofShape.Gable, 1f, 1.2f),
			new StructureRoofFollower("Shrine", 1, StructureMaterial.Timber, StructureRoofShape.Gable, 0.28f, 0.275f),
			new StructureRoofFollower("Smithy", 0, StructureMaterial.Timber, StructureRoofShape.Gable, 1.5f, 2.5f),
			new StructureRoofFollower("Smithy", 1, StructureMaterial.Timber, StructureRoofShape.Gable, 1.5f, 2.5f),
			new StructureRoofFollower("Sawmill", 0, StructureMaterial.Timber, StructureRoofShape.Gable, 2f, 3f),
			new StructureRoofFollower("Sawmill", 1, StructureMaterial.Thatch, StructureRoofShape.Gable, 2f, 3f),
			new StructureRoofFollower("Granary", 0, StructureMaterial.Thatch, StructureRoofShape.Gable, 1.9f, 2f),
			new StructureRoofFollower("Granary", 1, StructureMaterial.Timber, StructureRoofShape.Gable, 1.9f, 2f),
			new StructureRoofFollower("Watchtower", 0, StructureMaterial.Thatch, StructureRoofShape.Turned, 1.8f, 2.5f),
			new StructureRoofFollower("Watchtower", 1, StructureMaterial.Timber, StructureRoofShape.Turned, 1.8f, 2.5f),
			new StructureRoofFollower("WallTower", 1, StructureMaterial.Timber, StructureRoofShape.Turned, 3.7f, 3.5f),
			new StructureRoofFollower("Keep", 1, StructureMaterial.Timber, StructureRoofShape.Turned, 4f, 5f),
			new StructureRoofFollower("StiltHut", 1, StructureMaterial.Thatch, StructureRoofShape.Turned, 3.05f, 3.32f),
			new StructureRoofFollower("Dovecote", 1, StructureMaterial.Thatch, StructureRoofShape.Turned, 3.4f, 3.2f),
		};

		/// <summary>Every pitch, in order.</summary>
		public static readonly StructureRoofPitch[] Pitches = (StructureRoofPitch[])Enum.GetValues(typeof(StructureRoofPitch));

		/// <summary>
		/// Whether a house roof of this kit material follows its palette: thatch (the Roof), timber planks (the ShingleRoof) and,
		/// since 2026-10-18 (I6h), turf (the Turf: the turf longhouse's sod roof). A tent's cloth does not.
		/// </summary>
		public static bool Follows(StructureMaterial kitRoof) => kitRoof == StructureMaterial.Thatch || kitRoof == StructureMaterial.Timber || kitRoof == StructureMaterial.Earth;

		/// <summary>
		/// The pitch a roof material wants: flat for mud brick, else its least pitch (<see cref="StructurePieces.MinimumPitch"/>)
		/// to the class. Packed earth is pitched sod at 35° (2026-10-18, I6h: "earth roofs are pitched sod/turf, only adobe gives
		/// flat roofs"; I6e had laid it flat).
		/// </summary>
		public static StructureRoofPitch PitchOf(StructureMaterial roof)
		{
			if (roof == StructureMaterial.Adobe)
			{
				return StructureRoofPitch.Flat;
			}
			return ClassOf(StructurePieces.MinimumPitch(roof));
		}

		/// <summary>The class a pitch in degrees falls in: the steepest class it reaches (to half a degree), flat under 0.5°.</summary>
		public static StructureRoofPitch ClassOf(float degrees)
		{
			return degrees >= 59.5f ? StructureRoofPitch.Pitch60
				: degrees >= 49.5f ? StructureRoofPitch.Pitch50
				: degrees >= 44.5f ? StructureRoofPitch.Pitch45
				: degrees >= 39.5f ? StructureRoofPitch.Pitch40
				: degrees >= 34.5f ? StructureRoofPitch.Pitch35
				: degrees >= 0.5f ? StructureRoofPitch.Pitch30
				: StructureRoofPitch.Flat;
		}

		/// <summary>
		/// The pitch a palette entry builds its roof at: the class it names (BiomeStructureSurface.Pitch, 2026-10-18, I6h), else
		/// its material's (<see cref="PitchOf(StructureMaterial)"/>).
		/// </summary>
		public static StructureRoofPitch PitchOf(BiomeStructureSurface surface)
		{
			StructureRoofPitch? named = Named(surface.Pitch);
			return named ?? PitchOf((StructureMaterial)(int)surface.Material);
		}

		/// <summary>The class a palette's pitch names, or null for "the material's own" (BiomeRoofPitch is this enum one value on).</summary>
		public static StructureRoofPitch? Named(BiomeRoofPitch pitch)
		{
			return pitch == BiomeRoofPitch.Material ? (StructureRoofPitch?)null : (StructureRoofPitch)((int)pitch - 1);
		}

		/// <summary>A pitch's slope, degrees (0 for a flat roof).</summary>
		public static float Degrees(StructureRoofPitch pitch)
		{
			switch (pitch)
			{
				case StructureRoofPitch.Pitch30: return 30f;
				case StructureRoofPitch.Pitch35: return 35f;
				case StructureRoofPitch.Pitch40: return 40f;
				case StructureRoofPitch.Pitch45: return 45f;
				case StructureRoofPitch.Pitch50: return 50f;
				case StructureRoofPitch.Pitch60: return 60f;
				default: return 0f;
			}
		}

		/// <summary>
		/// Whether a roof at this pitch is laid as thatch is: thick, its grain down the slope (thatch and reed's 50°). The 60°
		/// shake roofs are riven boards in courses, laid hard as shingles are.
		/// </summary>
		public static bool Soft(StructureRoofPitch pitch) => pitch == StructureRoofPitch.Pitch50;

		/// <summary>The part of a variant's names that is its pitch: "Flat", "Pitch30" … "Pitch60".</summary>
		public static string Suffix(StructureRoofPitch pitch) => pitch == StructureRoofPitch.Flat ? "Flat" : "Pitch" + Mathf.RoundToInt(Degrees(pitch));

		/// <summary>The pitch a name's part names (<see cref="Suffix"/>), or false.</summary>
		public static bool TryParseSuffix(string part, out StructureRoofPitch pitch)
		{
			foreach (StructureRoofPitch p in Pitches)
			{
				if (string.Equals(part, Suffix(p), StringComparison.Ordinal))
				{
					pitch = p;
					return true;
				}
			}
			pitch = default;
			return false;
		}

		/// <summary>Whether a piece's variant has a roof that follows its palette, and that roof's kit material. Pure.</summary>
		public static bool TryFollower(StructurePiece piece, int variant, out StructureMaterial kitRoof)
		{
			bool found = TryGetFollower(piece, variant, out StructureRoofFollower follower);
			kitRoof = found ? follower.Roof : default;
			return found;
		}

		/// <summary>As <see cref="TryFollower(StructurePiece, int, out StructureMaterial)"/>, by piece id.</summary>
		public static bool TryFollower(string pieceId, int variant, out StructureMaterial kitRoof)
		{
			bool found = TryGetFollower(pieceId, variant, out StructureRoofFollower follower);
			kitRoof = found ? follower.Roof : default;
			return found;
		}

		/// <summary>A piece's variant's roof that follows its palette (none for a ruin: it has lost its roof). Pure.</summary>
		public static bool TryGetFollower(StructurePiece piece, int variant, out StructureRoofFollower follower)
		{
			follower = default;
			if (piece == null || piece.RuinOf != null)
			{
				return false;
			}
			return TryGetFollower(piece.Id, variant, out follower);
		}

		/// <summary>As <see cref="TryGetFollower(StructurePiece, int, out StructureRoofFollower)"/>, by piece id.</summary>
		public static bool TryGetFollower(string pieceId, int variant, out StructureRoofFollower follower)
		{
			foreach (StructureRoofFollower f in Followers)
			{
				if (f.Variant == variant && string.Equals(f.Piece, pieceId, StringComparison.Ordinal))
				{
					follower = f;
					return true;
				}
			}
			follower = default;
			return false;
		}

		/// <summary>
		/// The palette role a roof of a kit material plays in a style (StructureParts with the roof's hints): the Roof for
		/// thatch, the ShingleRoof for timber planks, the Turf for turf.
		/// </summary>
		public static BiomeStructureRole RoleOf(StructureStyle style, StructureMaterial kitRoof)
		{
			BiomeStructureRole? role = StructureParts.Resolve(style, true, kitRoof, StructurePieces.RoofRoles);
			return role ?? (kitRoof == StructureMaterial.Thatch ? BiomeStructureRole.Roof
				: kitRoof == StructureMaterial.Earth ? BiomeStructureRole.Turf : BiomeStructureRole.ShingleRoof);
		}

		/// <summary>
		/// The pitch a palette wants a piece's variant's roof at, or null when that is the kit's own (a roof that does not follow,
		/// no palette, a palette whose roof wants the kit's pitch, or a pitch the roof would come out of exactly as the kit's,
		/// <see cref="SameAsKit"/>): null is always the kit's prefab. Pure.
		/// </summary>
		public static StructureRoofPitch? Wanted(StructurePiece piece, int variant, BiomeStructurePalette palette)
		{
			if (palette == null || !TryGetFollower(piece, variant, out StructureRoofFollower follower))
			{
				return null;
			}
			BiomeStructureRole role = RoleOf(piece.Style, follower.Roof);
			StructureRoofPitch wanted = PitchOf(palette.For((BiomeStructureStyle)piece.Style, role));
			return wanted == PitchOf(follower.Roof) || SameAsKit(follower, wanted) ? (StructureRoofPitch?)null : wanted;
		}

		/// <summary>
		/// Whether a roof a piece builds for itself comes out at a pitch exactly as the kit builds it: its rise is the kit's
		/// (<see cref="Rise"/>: a roof the kit builds lower than its material's class, asked for a class between the two, keeps its
		/// own) and, on a gable or a house, its covering's grain is the kit's (a thatched gable asked for 45° is re-laid in courses).
		/// Never for a house built at its class (given as 0: any other class moves it) or a flat roof. Pure.
		/// </summary>
		public static bool SameAsKit(StructureRoofFollower follower, StructureRoofPitch pitch)
		{
			if (follower.Half <= 0f || pitch == StructureRoofPitch.Flat)
			{
				return false;
			}
			StructureRoofPitch kit = PitchOf(follower.Roof);
			if (follower.Shape != StructureRoofShape.Turned && Soft(pitch) != Soft(kit))
			{
				return false;
			}
			return Rise(kit, follower.KitRise, pitch, follower.Half) == follower.KitRise;
		}

		/// <summary>
		/// Every roof variant the palettes need, each once, in first-use order (palette, then <see cref="Followers"/>' order): what
		/// the art generator makes. None for the kit's palette, or one that roofs every piece at the kit's pitch. Pure.
		/// </summary>
		public static List<StructureRoofVariant> Needed(IEnumerable<BiomeStructurePalette> palettes)
		{
			var needed = new List<StructureRoofVariant>();
			var seen = new HashSet<StructureRoofVariant>();
			if (palettes == null)
			{
				return needed;
			}
			foreach (BiomeStructurePalette palette in palettes)
			{
				if (palette == null)
				{
					continue;
				}
				foreach (StructureRoofFollower f in Followers)
				{
					StructurePiece piece = StructurePieces.Find(f.Piece);
					StructureRoofPitch? wanted = piece != null ? Wanted(piece, f.Variant, palette) : null;
					if (wanted != null)
					{
						var variant = new StructureRoofVariant(f.Piece, f.Variant, wanted.Value);
						if (seen.Add(variant))
						{
							needed.Add(variant);
						}
					}
				}
			}
			return needed;
		}

		/// <summary>
		/// Every variant there can ever be: each follower at each pitch but its kit's own and those it would come out of as the
		/// kit's (<see cref="SameAsKit"/>). The worst case the art could grow to. Pure.
		/// </summary>
		public static List<StructureRoofVariant> EveryVariant()
		{
			var every = new List<StructureRoofVariant>();
			foreach (StructureRoofFollower f in Followers)
			{
				foreach (StructureRoofPitch pitch in Pitches)
				{
					if (pitch != PitchOf(f.Roof) && !SameAsKit(f, pitch))
					{
						every.Add(new StructureRoofVariant(f.Piece, f.Variant, pitch));
					}
				}
			}
			return every;
		}

		/// <summary>
		/// A roof's rise over its half span at a pitch other than its kit one: the pitch's own, but never past the kit roof's
		/// rise the other way (a steeper class never builds it lower than the kit does, a flatter one never higher), so a house
		/// whose builder asked for more rise than its kit pitch keeps it where that is still the steeper roof, and a low roof a
		/// piece builds for itself (a well's) keeps its own where the class asked for is no lower. Pure.
		/// </summary>
		public static float Rise(StructureRoofPitch kit, float kitRise, StructureRoofPitch pitch, float halfSpan)
		{
			float rise = Mathf.Tan(Degrees(pitch) * Mathf.Deg2Rad) * halfSpan;
			return pitch > kit ? Mathf.Max(kitRise, rise) : Mathf.Min(kitRise, rise);
		}

		// ── Flat roofs ─────────────────────────────────────────────────

		/// <summary>How high a flat roof's parapet stands over its deck, metres: the walls run on up past the roof, as mud brick houses' do.</summary>
		public const float Parapet = 0.6f;

		/// <summary>How far a flat roof's walls rise over a doorway that would have run up into a gable, metres: a lintel's depth and the beams over it.</summary>
		public const float FlatLintel = 0.45f;

		/// <summary>How far a drainage spout reaches out from its wall, metres (inside the eaves the pitched roof had, 0.4 m at the least).</summary>
		public const float SpoutReach = 0.35f;

		/// <summary>How far a roof beam's end stands out of the wall under a flat roof, metres.</summary>
		public const float BeamEnd = 0.3f;

		/// <summary>The most roof beams' ends shown along one wall.</summary>
		public const int MostBeamEnds = 7;
	}
}
#endif
