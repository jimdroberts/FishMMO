#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Which part of a building each solid of a structure piece is in a biome's structure palette (2026-10-16, I6a): the role
	/// whose material the palette gives it (<see cref="BiomeStructureRole"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The one rule that keeps the kit's look.</b> A solid only ever plays a role whose kit material in its piece's style
	/// (<see cref="BiomeStructurePalette.KitMaterial"/>) is the solid's own material. The kit's palette gives every role its kit
	/// material, so under it every solid is laid in exactly the material it was built in: the default palette changes nothing.
	/// </para>
	/// <para>
	/// <b>Hints, then defaults.</b> A builder names the roles it means a run of solids to play (<see cref="StructureDraft.Builds"/>:
	/// a house's walls, its roof, its floors, a fence); the first of them that fits the rule above is the solid's part. Anything
	/// else takes the default for its material in its kind of piece (<see cref="Defaults"/>): in a building's fabric timber is
	/// its trim, plaster its walls, thatch its roof, fieldstone its plinth, dressed stone its walls (or, in a building of other
	/// stuff, its dressings); in goods (furniture, clutter, boats, lights) timber is woodwork; everywhere iron is ironwork,
	/// canvas cloth (a tent's walls), dyed cloth banners, monolith carving. Glass, a lamp's pane, a fire's ash, hay and bone
	/// are no part: no palette changes them.
	/// </para>
	/// <para>
	/// <b>Hay, straw and bone (decided 2026-10-16, I6c).</b> Goods of thatch (hay bales, a straw bed, a skep's straw, a stall's
	/// bedding) and every bone (a bone pile, an altar's skulls, a drying rack's antlers) are what they are in every biome, so
	/// they never take a part. A building of bone is the palette's to make: a biome gives its walls or its roof
	/// <c>BoneWork</c> (lashed long bones, BiomeStructureMaterial.BoneWork), and every wall or roof of its buildings is laid in
	/// it; no kit building is made of bone itself. Decks (a pier's, a bridge's, a rope walk's, a trestle's, a landing stage's
	/// planks) are their Floor; a turf roof's ridge beam is the frame's Trim.
	/// </para>
	/// </remarks>
	public static class StructureParts
	{
		/// <summary>
		/// Tags that make a piece goods rather than a building's fabric: what stands in, on or about a building (its furniture,
		/// clutter, stores, boats, lights and banners), whose timber is woodwork, not the frame's trim.
		/// </summary>
		public static readonly string[] GoodsTags =
		{
			"furniture", "clutter", "storage", "bedding", "boat", "ship", "airship", "banner", "lantern", "light", "torch",
			"lumber", "bones", "climb", "notice", "fire", "wreck",
		};

		/// <summary>True for a piece that is a building's fabric (a house, a wall, a tent, a mill, a pier); false for goods.</summary>
		public static bool IsFabric(StructurePiece piece) => piece != null && IsFabric(piece.Tags);

		/// <summary>True when none of the tags is a <see cref="GoodsTags"/> tag.</summary>
		public static bool IsFabric(IReadOnlyList<string> tags)
		{
			if (tags == null)
			{
				return true;
			}
			foreach (string tag in tags)
			{
				if (Array.IndexOf(GoodsTags, tag) >= 0)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>The roles a material defaults to, in preference, in a building's fabric or in goods: the first that fits is the part.</summary>
		public static BiomeStructureRole[] Defaults(bool fabric, StructureMaterial material)
		{
			switch (material)
			{
				case StructureMaterial.Timber: return fabric ? new[] { BiomeStructureRole.Trim } : new[] { BiomeStructureRole.Woodwork };
				case StructureMaterial.Plaster: return new[] { BiomeStructureRole.Wall, BiomeStructureRole.UpperWall };
				case StructureMaterial.Thatch: return fabric ? new[] { BiomeStructureRole.Roof } : Array.Empty<BiomeStructureRole>();
				case StructureMaterial.Stone: return fabric ? new[] { BiomeStructureRole.Wall, BiomeStructureRole.Dressing } : new[] { BiomeStructureRole.Dressing };
				case StructureMaterial.Fieldstone: return new[] { BiomeStructureRole.Plinth };
				case StructureMaterial.Canvas: return fabric ? new[] { BiomeStructureRole.Wall, BiomeStructureRole.Cloth } : new[] { BiomeStructureRole.Cloth };
				case StructureMaterial.Dyed: return new[] { BiomeStructureRole.Banner };
				case StructureMaterial.Iron: return new[] { BiomeStructureRole.Ironwork };
				case StructureMaterial.Earth: return fabric ? new[] { BiomeStructureRole.Plinth, BiomeStructureRole.Turf } : Array.Empty<BiomeStructureRole>();
				case StructureMaterial.Ash: return fabric ? new[] { BiomeStructureRole.Door } : Array.Empty<BiomeStructureRole>();
				case StructureMaterial.Monolith: return new[] { BiomeStructureRole.Carving };
				// Bone, glass, a lamp's pane, and the palette's own materials (no kit builder builds in them).
				default: return Array.Empty<BiomeStructureRole>();
			}
		}

		/// <summary>Whether a solid of <paramref name="material"/> may play <paramref name="role"/> in a piece of <paramref name="style"/>: the role's kit material is its own.</summary>
		public static bool Fits(StructureStyle style, BiomeStructureRole role, StructureMaterial material)
		{
			return (int)BiomeStructurePalette.KitMaterial((BiomeStructureStyle)style, role) == (int)material;
		}

		/// <summary>
		/// A solid's part: the first of <paramref name="hint"/> that <see cref="Fits"/>, else the first of its material's
		/// <see cref="Defaults"/> that does, else none. Pure.
		/// </summary>
		public static BiomeStructureRole? Resolve(StructureStyle style, bool fabric, StructureMaterial material, IReadOnlyList<BiomeStructureRole> hint)
		{
			if (hint != null)
			{
				foreach (BiomeStructureRole role in hint)
				{
					if (Fits(style, role, material))
					{
						return role;
					}
				}
			}
			foreach (BiomeStructureRole role in Defaults(fabric, material))
			{
				if (Fits(style, role, material))
				{
					return role;
				}
			}
			return null;
		}

		/// <summary>Gives every solid without a part its part (<see cref="Resolve"/>); a part already set (a ruin's intact solids) is kept.</summary>
		public static void Assign(StructurePiece piece, List<StructureSolid> solids)
		{
			bool fabric = IsFabric(piece);
			foreach (StructureSolid s in solids)
			{
				if (s.Part == null)
				{
					s.Part = Resolve(piece.Style, fabric, s.Material, s.Hint);
				}
			}
		}
	}

	/// <summary>
	/// What one submesh is laid in once a palette has spoken: a material, its tint over its surface, and the surface variant
	/// (I9's per-biome texture variant id, empty for the surface every biome shares). The structure material asset is named
	/// after it (<see cref="MaterialName"/>); a key that is the kit's own is named as the kit's material always was.
	/// </summary>
	public readonly struct StructureSurfaceKey : IEquatable<StructureSurfaceKey>
	{
		public readonly StructureMaterial Material;
		/// <summary>The tint, quantised to bytes: two tints a byte apart in no channel are one material.</summary>
		public readonly Color32 Tint;
		public readonly string Surface;

		public StructureSurfaceKey(StructureMaterial material, Color tint, string surface)
		{
			Material = material;
			Tint = tint;
			Surface = StructurePalettes.CleanSurface(surface);
		}

		/// <summary>The kit's own key for a material: its kit tint, the shared surface.</summary>
		public static StructureSurfaceKey Kit(StructureMaterial material) => new StructureSurfaceKey(material, StructureSurfaces.TintOf(material), null);

		/// <summary>True for the kit's own material: the kit's tint (to the byte) and the shared surface.</summary>
		public bool IsKit => Surface.Length == 0 && Same(Tint, (Color32)StructureSurfaces.TintOf(Material));

		/// <summary>The tint as the material's base colour.</summary>
		public Color Colour => IsKit ? StructureSurfaces.TintOf(Material) : (Color)Tint;

		/// <summary>
		/// The material asset's name in a finish: the kit's (<see cref="StructurePieces.MaterialName"/>) for a kit key, else
		/// <c>Structure_&lt;Material&gt;_&lt;RRGGBBAA&gt;[_&lt;surface&gt;][_&lt;Finish&gt;]</c>.
		/// </summary>
		public string MaterialName(StructureFinish finish)
		{
			if (IsKit)
			{
				return StructurePieces.MaterialName(Material, finish);
			}
			var name = new StringBuilder(64);
			name.Append(StructurePieces.Prefix).Append(Material).Append('_')
				.Append(Tint.r.ToString("X2")).Append(Tint.g.ToString("X2")).Append(Tint.b.ToString("X2")).Append(Tint.a.ToString("X2"));
			if (Surface.Length > 0)
			{
				name.Append('_').Append(Surface);
			}
			if (finish != StructureFinish.None)
			{
				name.Append('_').Append(finish);
			}
			return name.ToString();
		}

		private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

		public bool Equals(StructureSurfaceKey other) => Material == other.Material && Same(Tint, other.Tint) && string.Equals(Surface, other.Surface, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is StructureSurfaceKey other && Equals(other);
		public override int GetHashCode() => ((int)Material * 397) ^ (Tint.r << 24 | Tint.g << 16 | Tint.b << 8 | Tint.a) ^ (Surface ?? string.Empty).GetHashCode();
		public override string ToString() => MaterialName(StructureFinish.None);
	}

	/// <summary>
	/// The structure palette made art (2026-10-16, I6a): what material each submesh of a kit piece is laid in under a biome's
	/// palette, the material keys every palette needs (generated once each, deduplicated, by the art generator), and the roof
	/// rules the new surfaces bring.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Design.</b> A piece's meshes are built once, a submesh per material and palette part (<see cref="StructureSlot"/>); its
	/// prefab wears the kit's material on each. A palette never makes a mesh or a prefab: the cut lays the kit's prefab with the
	/// palette's material on each submesh (<see cref="MaterialNames"/>), written into the scene's prop set beside the prefab
	/// (ScenePropSet.Prototype.Materials) or onto a scene object's renderers. So the art grows by materials only, one per key
	/// (material, tint, surface variant) and finish that any palette uses, and never by pieces × palettes. Where the palette is
	/// the kit's the cut writes no materials at all and the scene is exactly as before.
	/// </para>
	/// <para>
	/// <b>Batching.</b> The instanced renderer pools and batches by material, so the extra submeshes the parts split a material
	/// into cost no draw call while the kit's palette lays them alike; a palette's new material is one more batch for every
	/// piece that wears it, whatever the piece.
	/// </para>
	/// </remarks>
	public static class StructurePalettes
	{
		/// <summary>A surface variant id as it may stand in an asset name: letters, digits and '-' only; null → empty.</summary>
		public static string CleanSurface(string surface)
		{
			if (string.IsNullOrWhiteSpace(surface))
			{
				return string.Empty;
			}
			var clean = new StringBuilder(surface.Length);
			foreach (char ch in surface.Trim())
			{
				clean.Append(char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-');
			}
			return clean.ToString();
		}

		/// <summary>
		/// The key a submesh is laid in: the palette's surface for its part in <paramref name="style"/>, or the kit's own key for
		/// a submesh that plays no part or with no palette. Pure.
		/// </summary>
		public static StructureSurfaceKey Resolve(BiomeStructurePalette palette, StructureStyle style, StructureSlot slot)
		{
			if (palette == null || slot.Part == null)
			{
				return StructureSurfaceKey.Kit(slot.Material);
			}
			BiomeStructureSurface surface = palette.For((BiomeStructureStyle)style, slot.Part.Value);
			return new StructureSurfaceKey((StructureMaterial)(int)surface.Material, surface.Tint, surface.Surface);
		}

		/// <summary>Every key a palette can lay (every style and role), the kit's own left out, each once, in style then role order. Pure.</summary>
		public static List<StructureSurfaceKey> KeysOf(BiomeStructurePalette palette)
		{
			return KeysOf(palette != null ? new[] { palette } : Array.Empty<BiomeStructurePalette>());
		}

		/// <summary>Every key the palettes can lay, the kit's own left out, deduplicated, in first-use order. Pure.</summary>
		public static List<StructureSurfaceKey> KeysOf(IEnumerable<BiomeStructurePalette> palettes)
		{
			var keys = new List<StructureSurfaceKey>();
			var seen = new HashSet<StructureSurfaceKey>();
			foreach (BiomeStructurePalette palette in palettes)
			{
				if (palette == null)
				{
					continue;
				}
				foreach (BiomeStructureStyle style in BiomeStructurePalette.Styles)
				{
					foreach (BiomeStructureRole role in BiomeStructurePalette.Roles)
					{
						BiomeStructureSurface surface = palette.For(style, role);
						var key = new StructureSurfaceKey((StructureMaterial)(int)surface.Material, surface.Tint, surface.Surface);
						if (!key.IsKit && seen.Add(key))
						{
							keys.Add(key);
						}
					}
				}
			}
			return keys;
		}

		// ── A piece's submeshes ──────────────────────────────────────

		private static readonly Dictionary<(string, int, int), StructureSlot[]> slots = new Dictionary<(string, int, int), StructureSlot[]>();

		/// <summary>
		/// A level's submeshes as the art generator built them (the generation seed, <see cref="StructurePieces.DefaultSeed"/>),
		/// built from the piece's solids once and cached.
		/// </summary>
		public static StructureSlot[] SlotsOf(StructurePiece piece, int variant, int lod)
		{
			lock (slots)
			{
				if (!slots.TryGetValue((piece.Id, variant, lod), out StructureSlot[] list))
				{
					list = StructureShapes.SlotsOf(StructurePieces.BuildSolids(piece, variant, lod, StructurePieces.DefaultSeed));
					slots[(piece.Id, variant, lod)] = list;
				}
				return list;
			}
		}

		/// <summary>
		/// The material name of every submesh of every level of a piece's variant in a finish under a palette, level by level
		/// (the order a prefab's LOD renderers wear them), and whether any differs from the kit's. <paramref name="host"/> is the
		/// style of the building the piece belongs to when that is not the piece's own (a door leaf hung in a stone church takes
		/// the stone style's door): a part takes the host's entry where its kit material is the same in the host's style, and its
		/// own piece's otherwise, so the kit's palette still lays every part as built. Pure but for building the slots.
		/// </summary>
		public static string[][] MaterialNames(StructurePiece piece, int variant, StructureFinish finish, BiomeStructurePalette palette,
			StructureStyle? host, out bool differs)
		{
			differs = false;
			var names = new string[StructurePieces.LevelCount][];
			for (int lod = 0; lod < names.Length; lod++)
			{
				StructureSlot[] level = SlotsOf(piece, variant, lod);
				names[lod] = new string[level.Length];
				for (int i = 0; i < level.Length; i++)
				{
					StructureSurfaceKey key = Resolve(palette, StyleFor(piece.Style, host, level[i]), level[i]);
					names[lod][i] = key.MaterialName(finish);
					differs |= !key.IsKit || key.Material != level[i].Material;
				}
			}
			return names;
		}

		/// <summary>The style whose palette entry a slot takes: the host's where its part has the slot's kit material there, else the piece's.</summary>
		public static StructureStyle StyleFor(StructureStyle own, StructureStyle? host, StructureSlot slot)
		{
			return host != null && slot.Part != null && StructureParts.Fits(host.Value, slot.Part.Value, slot.Material) ? host.Value : own;
		}

		// ── Roofs ─────────────────────────────────────────────────────

		/// <summary>
		/// The pitch a roof role is built at by the kit, degrees (StructurePieces.MinimumPitch of its kit material): a palette lays
		/// another material on the same roof, so a material that wants it steeper (reed or thatch on a shingle roof) is laid
		/// too flat for it. Null for a role that is no roof.
		/// </summary>
		public static float? BuiltPitch(BiomeStructureStyle style, BiomeStructureRole role)
		{
			switch (role)
			{
				case BiomeStructureRole.Roof:
				case BiomeStructureRole.ShingleRoof:
					return StructurePieces.MinimumPitch((StructureMaterial)(int)BiomeStructurePalette.KitMaterial(style, role));
				default:
					return null;
			}
		}

		/// <summary>
		/// Whether a material may roof a roof role: the role is no roof, or the material's least pitch is no steeper than the
		/// roof is built. Pure.
		/// </summary>
		public static bool RoofFits(BiomeStructureStyle style, BiomeStructureRole role, BiomeStructureMaterial material)
		{
			float? built = BuiltPitch(style, role);
			return built == null || StructurePieces.MinimumPitch((StructureMaterial)(int)material) <= built.Value + 1e-3f;
		}
	}
}
#endif
