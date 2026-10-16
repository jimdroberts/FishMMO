using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// What a structure piece is mostly built of: the runtime mirror of the structure kit's StructureStyle (the kit is editor
	/// code), member for member and in the same order (a test holds them equal).
	/// </summary>
	public enum BiomeStructureStyle : byte
	{
		Timber,
		Stone,
		/// <summary>Cloth and hide: tents, pavilions, bedrolls.</summary>
		Hide,
		Iron,
		Earth,
	}

	/// <summary>
	/// What a structure part is made of: the runtime mirror of the structure kit's StructureMaterial (the kit is editor code),
	/// member for member and in the same order (a test holds them equal). Append-only, as the kit's.
	/// </summary>
	public enum BiomeStructureMaterial : byte
	{
		Timber,
		Plaster,
		Thatch,
		/// <summary>Dressed stone: ashlar blocks in courses.</summary>
		Stone,
		/// <summary>Rough, undressed stone.</summary>
		Fieldstone,
		/// <summary>Undyed canvas or hide.</summary>
		Canvas,
		/// <summary>Dyed cloth.</summary>
		Dyed,
		Iron,
		/// <summary>Packed soil and turf.</summary>
		Earth,
		Bone,
		/// <summary>Ash and charcoal; a tent's dark door flap.</summary>
		Ash,
		/// <summary>Stone carved or raised in one piece.</summary>
		Monolith,
		Glass,
		Lamplight,
		// The palette's own surfaces (2026-10-16, I6a): no kit builder uses them; a biome's palette builds a role in them.
		/// <summary>Round or hewn logs laid in courses.</summary>
		Log,
		/// <summary>Riven wooden shingles in staggered courses.</summary>
		Shingle,
		/// <summary>Split slates hung in courses.</summary>
		Slate,
		/// <summary>Sun-dried mud brick under a mud wash.</summary>
		Adobe,
		/// <summary>Sandstone dressed into ashlar.</summary>
		Sandstone,
		/// <summary>Bamboo culms side by side.</summary>
		Bamboo,
		/// <summary>Veined marble in great blocks.</summary>
		Marble,
		/// <summary>Blocks of clear ice.</summary>
		IceBlock,
		/// <summary>Bones and antler lashed as a building's walls or roof.</summary>
		BoneWork,
		/// <summary>Reed bundles laid as thatch.</summary>
		Reed,
	}

	/// <summary>The part a material plays in a building, which a biome's palette gives its own material (<see cref="BiomeStructurePalette"/>).</summary>
	/// <remarks>Append-only: the values are serialized.</remarks>
	public enum BiomeStructureRole : byte
	{
		/// <summary>The ground-floor walls (and gables).</summary>
		Wall,
		/// <summary>An upper storey's walls, where they differ from the ground floor's.</summary>
		UpperWall,
		/// <summary>A soft roof: thatch (and turf, reed, palm, cloth) laid thick over the rafters.</summary>
		Roof,
		/// <summary>A hard roof: shingles (and slates, tiles) in courses, where a building's variant is not thatched.</summary>
		ShingleRoof,
		/// <summary>The plinth and foundation, and the steps up to the door.</summary>
		Plinth,
		/// <summary>The timber frame over the walls, bargeboards, door and window surrounds.</summary>
		Trim,
		/// <summary>The door leaf (a tent's is its dark flap).</summary>
		Door,
		Floor,
		/// <summary>Fences, palisades and field walls.</summary>
		Fence,
		/// <summary>Yards, squares, steps and the ground worked round a building.</summary>
		Paving,
		// The roles every piece is built of (2026-10-16, I6a): the kit's pieces that are not houses, and the parts of houses a
		// palette could not reach before. Each is the same material in every style (KitMaterial).
		/// <summary>Walls of logs or heavy planks: a smithy's back wall, a timber longhouse, a stilt hut.</summary>
		LogWall,
		/// <summary>A stone ground floor under a timber-framed storey (the tall timber house's second variant).</summary>
		GroundFloor,
		/// <summary>Turf and packed earth: a turf longhouse's walls and roof, a crypt's banks, a grave mound.</summary>
		Turf,
		/// <summary>Dressed stone in a building of other stuff: chimney caps and drip courses, sills, staddles.</summary>
		Dressing,
		/// <summary>Stone carved or raised in one piece: statues, columns, headstones, altars, standing stones.</summary>
		Carving,
		/// <summary>Carpentry that is not the building's frame: furniture, carts, barrels, crates, boats and ships' hulls.</summary>
		Woodwork,
		/// <summary>Iron fittings: hinges, straps, braziers, lantern frames, railings, tools.</summary>
		Ironwork,
		/// <summary>Canvas and hide that is not a tent's walls: sails, awnings, bedding, sacks.</summary>
		Cloth,
		/// <summary>Dyed cloth: banners, pennants, awnings' stripes, blankets.</summary>
		Banner,
	}

	/// <summary>
	/// One role's material in one structure style (<see cref="BiomeStructurePalette"/>): what that style's buildings make the role
	/// of here, its colour over the surface, and the surface variant it is drawn with.
	/// </summary>
	[Serializable]
	public struct BiomeStructureSurface
	{
		[Tooltip("The structure style whose buildings this entry is for.")]
		public BiomeStructureStyle Style;
		[Tooltip("The part of the building.")]
		public BiomeStructureRole Role;
		[Tooltip("What that part is made of here.")]
		public BiomeStructureMaterial Material;
		[Tooltip("The material's colour over its surface (the material's base colour; white keeps the surface's own).")]
		public Color Tint;
		[Tooltip("The biome's own variant of the material's surface (its grain, palette and wear), by id. Empty: the surface every biome shares.")]
		public string Surface;

		public BiomeStructureSurface(BiomeStructureStyle style, BiomeStructureRole role, BiomeStructureMaterial material, Color tint, string surface = null)
		{
			Style = style;
			Role = role;
			Material = material;
			Tint = tint;
			Surface = surface ?? string.Empty;
		}

		/// <summary>Every value, exactly: "Style/Role=Material(r,g,b,a)@surface", the tint's channels round-tripped.</summary>
		public string Signature() => $"{Style}/{Role}={Material}({R(Tint.r)},{R(Tint.g)},{R(Tint.b)},{R(Tint.a)})@{Surface ?? string.Empty}";

		private static string R(float value) => value.ToString("R", CultureInfo.InvariantCulture);

		public override string ToString() => Signature();
	}

	/// <summary>
	/// A biome's structure palette (<see cref="BiomeTemplate.StructurePalette"/>, 2026-10-16): for each structure style, what its
	/// buildings make each part of in this biome (walls, upper walls, thatched and shingled roofs, plinth, trim, door, floor,
	/// fences, paving), with a
	/// tint and the surface variant, so a desert's timber town can be built of adobe and palm thatch and a fen's of reed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Today's materials are the default.</b> Until now a building's materials were fixed by its piece in the structure kit
	/// (its builder chose plaster walls and thatch for a timber house, ashlar and shingle for a stone one, canvas for a tent),
	/// whatever the biome: style-driven, not biome-driven. <see cref="Kit"/> is that choice per style and role, and the biome art
	/// authoring writes it onto every template, so nothing changes until a palette is edited. The kit's houses are thatched in
	/// one variant and shingled in the other, so a palette has both roofs (<see cref="BiomeStructureRole.Roof"/>,
	/// <see cref="BiomeStructureRole.ShingleRoof"/>). Where the kit builds a role of one style in another material for a single
	/// piece or variant (the log walls of a smithy, the turf walls of a longhouse, the stone ground floor of a tall timber
	/// house), that part plays a role of its own (<see cref="BiomeStructureRole.LogWall"/>, <see cref="BiomeStructureRole.Turf"/>,
	/// <see cref="BiomeStructureRole.GroundFloor"/>), as do the parts of pieces that are not houses (woodwork, ironwork, cloth,
	/// banners, carvings, dressed stone).
	/// </para>
	/// <para>
	/// <b>How a palette becomes art</b> (2026-10-16, I6a). Every solid of every kit piece plays one role (StructureParts: a
	/// house's walls and roof, a fence, woodwork, ironwork, cloth…), and the piece's meshes have a submesh per material and role;
	/// its prefab wears the kit's materials. The cut lays the kit's prefab with this palette's material on each submesh (the
	/// art generator makes one material per key — material, tint, surface — and finish that any palette lays), so a palette
	/// costs materials only, never prefabs or meshes. Where the palette is the kit's the cut lays exactly what it always did.
	/// </para>
	/// </remarks>
	[Serializable]
	public sealed class BiomeStructurePalette
	{
		[Tooltip("One entry per structure style and role: what that style's buildings make the part of in this biome. A style and role with no entry is built as the kit builds it.")]
		public List<BiomeStructureSurface> Surfaces = new List<BiomeStructureSurface>();

		/// <summary>True when the palette names nothing (never authored, or emptied).</summary>
		public bool IsEmpty => Surfaces == null || Surfaces.Count == 0;

		/// <summary>The first entry for a style and role; false when none.</summary>
		public bool TryGet(BiomeStructureStyle style, BiomeStructureRole role, out BiomeStructureSurface surface)
		{
			if (Surfaces != null)
			{
				foreach (BiomeStructureSurface entry in Surfaces)
				{
					if (entry.Style == style && entry.Role == role)
					{
						surface = entry;
						return true;
					}
				}
			}
			surface = KitSurface(style, role);
			return false;
		}

		/// <summary>A role's surface for a style: this palette's entry, else the kit's.</summary>
		public BiomeStructureSurface For(BiomeStructureStyle style, BiomeStructureRole role)
		{
			TryGet(style, role, out BiomeStructureSurface surface);
			return surface;
		}

		/// <summary>
		/// Whether a style's buildings come out here exactly as the kit builds them: every role's material, tint and surface the
		/// kit's (<see cref="KitSurface"/>), a role with no entry counting as the kit's.
		/// </summary>
		public bool BuildsAsKit(BiomeStructureStyle style)
		{
			foreach (BiomeStructureRole role in Roles)
			{
				BiomeStructureSurface mine = For(style, role), kit = KitSurface(style, role);
				if (mine.Material != kit.Material || mine.Tint != kit.Tint || !string.IsNullOrEmpty(mine.Surface))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Every entry's <see cref="BiomeStructureSurface.Signature"/>, one a line, in order: the authoring's mark.</summary>
		public string Signature()
		{
			var text = new StringBuilder(1024);
			if (Surfaces != null)
			{
				foreach (BiomeStructureSurface entry in Surfaces)
				{
					text.Append(entry.Signature()).Append('\n');
				}
			}
			return text.ToString();
		}

		public BiomeStructurePalette Clone() => new BiomeStructurePalette { Surfaces = new List<BiomeStructureSurface>(Surfaces ?? new List<BiomeStructureSurface>()) };

		// ── The kit's own materials ──────────────────────────────────────

		/// <summary>Every style, in order.</summary>
		public static readonly BiomeStructureStyle[] Styles = (BiomeStructureStyle[])Enum.GetValues(typeof(BiomeStructureStyle));

		/// <summary>Every role, in order.</summary>
		public static readonly BiomeStructureRole[] Roles = (BiomeStructureRole[])Enum.GetValues(typeof(BiomeStructureRole));

		/// <summary>
		/// The palette every biome's buildings were built in until 2026-10-16: <see cref="KitSurface"/> for every style and role,
		/// in style then role order. A fresh object each call.
		/// </summary>
		public static BiomeStructurePalette Kit()
		{
			var palette = new BiomeStructurePalette();
			foreach (BiomeStructureStyle style in Styles)
			{
				foreach (BiomeStructureRole role in Roles)
				{
					palette.Surfaces.Add(KitSurface(style, role));
				}
			}
			return palette;
		}

		/// <summary>What the structure kit builds a role of in a style, tinted as the kit tints the material (<see cref="KitTint"/>).</summary>
		public static BiomeStructureSurface KitSurface(BiomeStructureStyle style, BiomeStructureRole role)
		{
			BiomeStructureMaterial material = KitMaterial(style, role);
			return new BiomeStructureSurface(style, role, material, KitTint(material));
		}

		/// <summary>
		/// The material the structure kit's builders give a role in a style (StructurePieces.*): timber's half-timbered houses
		/// and tavern (plaster panels in an oak frame over a fieldstone plinth, thatched or shingled by variant, plank doors,
		/// timber floors and fences, palisades of logs); stone's houses, church and temple (ashlar walls, a plastered upper
		/// storey where a timber one stands on a stone ground floor, thatched or shingled by variant, ashlar steps and walls);
		/// hide's tents (canvas over earth, a dark flap for a door); iron's fittings (railings, braziers, lanterns) and earth's
		/// mounds and turf, whose roles no building of theirs yet has, at the nearest the kit makes. The roles added with the
		/// palette's art (log walls, a stone ground floor, turf, dressed stone, carvings, woodwork, ironwork, cloth, banners) are
		/// one material in every style.
		/// </summary>
		public static BiomeStructureMaterial KitMaterial(BiomeStructureStyle style, BiomeStructureRole role)
		{
			// The roles added with the palette's art (2026-10-16): one material in every style, what the kit's builders use there.
			switch (role)
			{
				case BiomeStructureRole.LogWall:
				case BiomeStructureRole.Woodwork: return BiomeStructureMaterial.Timber;
				case BiomeStructureRole.GroundFloor:
				case BiomeStructureRole.Dressing: return BiomeStructureMaterial.Stone;
				case BiomeStructureRole.Turf: return BiomeStructureMaterial.Earth;
				case BiomeStructureRole.Carving: return BiomeStructureMaterial.Monolith;
				case BiomeStructureRole.Ironwork: return BiomeStructureMaterial.Iron;
				case BiomeStructureRole.Cloth: return BiomeStructureMaterial.Canvas;
				case BiomeStructureRole.Banner: return BiomeStructureMaterial.Dyed;
			}
			switch (style)
			{
				case BiomeStructureStyle.Timber:
					switch (role)
					{
						case BiomeStructureRole.Wall:
						case BiomeStructureRole.UpperWall: return BiomeStructureMaterial.Plaster;
						case BiomeStructureRole.Roof: return BiomeStructureMaterial.Thatch;
						case BiomeStructureRole.Plinth:
						case BiomeStructureRole.Paving: return BiomeStructureMaterial.Fieldstone;
						default: return BiomeStructureMaterial.Timber; // shingle roof, trim, door, floor, fence
					}
				case BiomeStructureStyle.Stone:
					switch (role)
					{
						case BiomeStructureRole.Wall:
						case BiomeStructureRole.Floor:
						case BiomeStructureRole.Fence:
						case BiomeStructureRole.Paving: return BiomeStructureMaterial.Stone;
						case BiomeStructureRole.UpperWall: return BiomeStructureMaterial.Plaster;
						case BiomeStructureRole.Roof: return BiomeStructureMaterial.Thatch;
						case BiomeStructureRole.Plinth: return BiomeStructureMaterial.Fieldstone;
						default: return BiomeStructureMaterial.Timber; // shingle roof, trim, door
					}
				case BiomeStructureStyle.Hide:
					switch (role)
					{
						case BiomeStructureRole.Wall:
						case BiomeStructureRole.UpperWall:
						case BiomeStructureRole.Roof:
						case BiomeStructureRole.ShingleRoof: return BiomeStructureMaterial.Canvas;
						case BiomeStructureRole.Door: return BiomeStructureMaterial.Ash;
						case BiomeStructureRole.Trim:
						case BiomeStructureRole.Fence: return BiomeStructureMaterial.Timber;
						default: return BiomeStructureMaterial.Earth; // plinth, floor, paving
					}
				case BiomeStructureStyle.Iron:
					switch (role)
					{
						case BiomeStructureRole.Plinth:
						case BiomeStructureRole.Floor:
						case BiomeStructureRole.Paving: return BiomeStructureMaterial.Stone;
						default: return BiomeStructureMaterial.Iron;
					}
				default: // Earth
					switch (role)
					{
						case BiomeStructureRole.Plinth: return BiomeStructureMaterial.Fieldstone;
						case BiomeStructureRole.Trim:
						case BiomeStructureRole.Door: return BiomeStructureMaterial.Timber;
						default: return BiomeStructureMaterial.Earth;
					}
			}
		}

		/// <summary>
		/// The colour the kit gives a material over its surface (StructureSurfaces.TintOf: the two cloths' dyes, bone's ivory,
		/// ash's grey, the panes' amber; white for the rest).
		/// </summary>
		public static Color KitTint(BiomeStructureMaterial material)
		{
			switch (material)
			{
				case BiomeStructureMaterial.Canvas: return new Color(0.86f, 0.78f, 0.62f);
				case BiomeStructureMaterial.Dyed: return new Color(0.62f, 0.14f, 0.11f);
				case BiomeStructureMaterial.Bone: return new Color(0.96f, 0.91f, 0.78f);
				case BiomeStructureMaterial.Ash: return new Color(0.3f, 0.29f, 0.28f);
				case BiomeStructureMaterial.Glass: return new Color(0.42f, 0.37f, 0.3f);
				case BiomeStructureMaterial.Lamplight: return new Color(1f, 0.82f, 0.55f);
				default: return Color.white;
			}
		}
	}
}
