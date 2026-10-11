#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The ground families biomes paint with, by name. Constants, so a spec entry that misspells
	/// one is a compile error rather than a silently unpainted biome.
	/// </summary>
	/// <remarks>
	/// The name is the identity of everything derived from it: the terrain layer
	/// <c>Ground_&lt;Name&gt;.terrainlayer</c> and its textures <c>Ground_&lt;Name&gt;_Albedo.png</c>,
	/// <c>_Normal.png</c> and <c>_Mask.png</c>. Renaming one orphans those files and changes the
	/// file name a LOCAL override must use, so a name, once committed, stays.
	/// </remarks>
	public static class Ground
	{
		public const string Grass = "Grass";
		public const string GrassDry = "GrassDry";
		public const string GrassMeadow = "GrassMeadow";
		public const string Moss = "Moss";
		public const string ForestFloor = "ForestFloor";
		public const string NeedleLitter = "NeedleLitter";
		public const string JungleFloor = "JungleFloor";
		public const string Soil = "Soil";
		public const string Mud = "Mud";
		public const string Clay = "Clay";
		public const string Sandstone = "Sandstone";
		public const string Sand = "Sand";
		public const string SandBeach = "SandBeach";
		public const string Gravel = "Gravel";
		public const string Pebbles = "Pebbles";
		public const string Rock = "Rock";
		public const string CliffRock = "CliffRock";
		public const string Basalt = "Basalt";
		public const string Lava = "Lava";
		public const string Ash = "Ash";
		public const string Snow = "Snow";
		public const string Ice = "Ice";
		public const string SaltCrust = "SaltCrust";
		public const string Regolith = "Regolith";
		public const string Sulphur = "Sulphur";
		public const string Peat = "Peat";
		public const string Limestone = "Limestone";
		public const string Lichen = "Lichen";
		public const string Silt = "Silt";
		public const string Coral = "Coral";
		public const string Tholin = "Tholin";
		public const string Frost = "Frost";
		public const string CrackedEarth = "CrackedEarth";
		public const string Flagstone = "Flagstone";

		// ── Added by the vegetation expansion (2026-10-10) ──
		// No biome paints with one until the spec names it.

		/// <summary>Living sphagnum carpet of a raised bog: red, green and ochre cushions in hummocks, wet between.</summary>
		public const string Sphagnum = "Sphagnum";
		/// <summary>Purple-brown wiry moorland heath (ling heather), flecked with its flowers.</summary>
		public const string Heath = "Heath";
		/// <summary>Grey-white siliceous sinter round hot springs, in crusted rims streaked orange and brown by microbial mats.</summary>
		public const string Sinter = "Sinter";
		/// <summary>Loose black and oxidised-red cinder: the lapilli and scoria of a cinder cone's flanks.</summary>
		public const string Cinder = "Cinder";
		/// <summary>Ropy, glassy pahoehoe basalt: a lava flow's skin dragged into wrinkled folds as it set.</summary>
		public const string Pahoehoe = "Pahoehoe";
		/// <summary>Reg, desert pavement: a mosaic of wind-sorted stones blackened by desert varnish on a pale silt.</summary>
		public const string Reg = "Reg";
		/// <summary>Fine bright dust with low ripples: an airless or thin-aired world's dust ponds and mantles.</summary>
		public const string Dust = "Dust";
		/// <summary>Europa's lineae: bright ice cut by bands of rust-brown salts along its fractures.</summary>
		public const string Lineae = "Lineae";
		/// <summary>"Metal frost": the bright metallic sheen on Venus's highlands, over dark basalt.</summary>
		public const string MetalFrost = "MetalFrost";
		/// <summary>Titan's organic dune sand: dark orange-brown tholin grains in long ripples.</summary>
		public const string TholinDune = "TholinDune";

		// ── Paths (2026-10-10) ── The ground a way wears down to: what is under a biome's cover, packed by feet and
		// wheels (SurfaceCatalogue.PathGroundFor). Never painted by a biome's splat; the path overlay draws them.

		/// <summary>Trodden loam under grass and moss: dark, packed, a little grit worked up through it.</summary>
		public const string PathLoam = "PathLoam";
		/// <summary>Dusty pale earth of dry grass and steppe, small stones showing.</summary>
		public const string PathDryEarth = "PathDryEarth";
		/// <summary>Packed red earth of clay, laterite and red-rock country.</summary>
		public const string PathRedEarth = "PathRedEarth";
		/// <summary>Sand packed flat by feet and hooves, its ripples gone, darker grit in it.</summary>
		public const string PathSand = "PathSand";
		/// <summary>Grit and broken stone over rock and gravel ground: the way a scree or a moor path wears.</summary>
		public const string PathGrit = "PathGrit";
		/// <summary>Trampled snow: packed, glazed, dirtied with grit and earth.</summary>
		public const string PathSnow = "PathSnow";
		/// <summary>Wet trodden mud of bogs, marshes and jungle: dark and shining.</summary>
		public const string PathMud = "PathMud";
		/// <summary>Packed ash and cinder.</summary>
		public const string PathAsh = "PathAsh";
		/// <summary>A salt crust broken and packed into a grey track.</summary>
		public const string PathSalt = "PathSalt";
		/// <summary>A forest path: dark packed earth with the odd leaf and needle on it.</summary>
		public const string PathLitter = "PathLitter";
	}

	/// <summary>Bark families: the tileable trunk textures trees and woody details use.</summary>
	public static class Bark
	{
		public const string Brown = "Brown";
		public const string Pine = "Pine";
		public const string Birch = "Birch";
		public const string Palm = "Palm";
		public const string Cactus = "Cactus";
		public const string Bamboo = "Bamboo";
		public const string Dead = "Dead";

		// ── Added by the vegetation expansion (2026-10-10) ──

		/// <summary>Smooth silver-grey bark, faintly wrinkled and lichen-blotched: beech, kapok, baobab, dragon tree.</summary>
		public const string Smooth = "Smooth";
		/// <summary>Pale green-white bark with black lenticels and scars: aspen.</summary>
		public const string Aspen = "Aspen";
		/// <summary>Grey-brown, deeply fissured, twisted bark: olive.</summary>
		public const string Olive = "Olive";
		/// <summary>A date palm's stem: the diamond pattern of old frond bases.</summary>
		public const string DatePalm = "DatePalm";
		/// <summary>Shaggy reddish fibrous bark in long strips: Joshua tree, cypress, bald cypress, tree fern, banana.</summary>
		public const string Fibrous = "Fibrous";
	}

	/// <summary>
	/// The recipe for every procedural surface: ground families and bark.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Colours are picked from photographs of the real material in daylight and authored in sRGB
	/// (<see cref="Hex"/>); the ramp's three stops are the shadowed, typical and sunlit-dry tones,
	/// not light and dark versions of one colour, because real ground varies in hue as well as
	/// value (dry grass yellows, wet soil reddens).
	/// </para>
	/// <para>
	/// Tile sizes follow what the material's features measure: grass blades and leaf litter are a
	/// few centimetres, so their tiles are three metres; rock and cliff strata read at tens of
	/// centimetres, so theirs are six to ten. Too small a tile shows the repeat across a valley,
	/// too large blurs the near ground.
	/// </para>
	/// </remarks>
	public static class SurfaceCatalogue
	{
		/// <summary>An sRGB colour from #rrggbb.</summary>
		public static Color Hex(string hex)
		{
			if (string.IsNullOrEmpty(hex))
			{
				return Color.magenta;
			}
			int start = hex[0] == '#' ? 1 : 0;
			int value = Convert.ToInt32(hex.Substring(start, 6), 16);
			return new Color(((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f, 1f);
		}

		private static Color[] Hexes(params string[] hex) => Array.ConvertAll(hex, Hex);

		private static SurfaceRecipe R(string name, float tile, float relief, string dark, string mid, string light,
			int baseCells, SurfaceMotif motif, int motifCells, float motifSize, float motifWeight, string motifDark, string motifLight,
			float smoothness, float smoothVar = 0.1f, float occlusion = 0.7f, int octaves = 5, float persistence = 0.5f, float warp = 0.05f,
			int motifCount = 0, bool vertical = false, Color[] speckles = null, int speckleCount = 0, float speckleSize = 0.002f, float metallic = 0f)
		{
			return new SurfaceRecipe
			{
				Name = name,
				TileMetres = tile,
				ReliefMetres = relief,
				Dark = Hex(dark),
				Mid = Hex(mid),
				Light = Hex(light),
				BaseCells = baseCells,
				Octaves = octaves,
				Persistence = persistence,
				Warp = warp,
				Motif = motif,
				MotifCells = motifCells,
				MotifCount = motifCount,
				MotifSize = motifSize,
				MotifWeight = motifWeight,
				MotifDark = Hex(motifDark),
				MotifLight = Hex(motifLight),
				Vertical = vertical,
				Speckles = speckles,
				SpeckleCount = speckleCount,
				SpeckleSize = speckleSize,
				Smoothness = smoothness,
				SmoothnessVariation = smoothVar,
				Metallic = metallic,
				Occlusion = occlusion,
				NormalScale = 1f,
			};
		}

		/// <summary>Every ground family, in a fixed order.</summary>
		public static readonly SurfaceRecipe[] GroundRecipes =
		{
			R(Ground.Grass, 3f, 0.04f, "#26341a", "#46632a", "#77953f", 4, SurfaceMotif.Blades, 8, 0.03f, 0.8f, "#3b5822", "#93b24c", 0.15f, motifCount: 36000),
			R(Ground.GrassDry, 3f, 0.04f, "#463b22", "#83703f", "#b29b5e", 4, SurfaceMotif.Blades, 8, 0.032f, 0.8f, "#977d46", "#d6c27e", 0.1f, motifCount: 32000),
			R(Ground.GrassMeadow, 3f, 0.04f, "#2a3a1c", "#4f6d2e", "#82a046", 4, SurfaceMotif.Blades, 8, 0.028f, 0.8f, "#43622a", "#9ab854", 0.15f, motifCount: 34000,
				speckles: Hexes("#f2f2e8", "#f0d040", "#9b6fc8", "#e07a9a"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.Moss, 3f, 0.03f, "#1f3312", "#3d5e1e", "#6f8f2e", 6, SurfaceMotif.Clumps, 24, 0.5f, 0.7f, "#2f4a18", "#8aa040", 0.25f, 0.15f, 0.8f),
			R(Ground.ForestFloor, 3f, 0.04f, "#2a1d12", "#4a3420", "#6b4c2e", 6, SurfaceMotif.Leaves, 8, 0.028f, 0.85f, "#5a3a1a", "#b07a32", 0.2f, 0.2f, 0.85f, motifCount: 2200),
			R(Ground.NeedleLitter, 3f, 0.03f, "#24180e", "#3e2a18", "#5e4024", 6, SurfaceMotif.Needles, 8, 0.03f, 0.8f, "#5a3418", "#a0663a", 0.15f, 0.1f, 0.8f, motifCount: 9000),
			R(Ground.JungleFloor, 3f, 0.05f, "#18120a", "#2c2214", "#44341e", 6, SurfaceMotif.Leaves, 8, 0.04f, 0.85f, "#2e3a14", "#6a5a24", 0.3f, 0.25f, 0.85f, motifCount: 1600),
			R(Ground.Soil, 4f, 0.03f, "#3a2a1c", "#5a4330", "#7a5e44", 8, SurfaceMotif.Granular, 64, 0.1f, 0.5f, "#2e2216", "#6a5038", 0.12f, 0.1f),
			R(Ground.Mud, 4f, 0.03f, "#2a2016", "#3e3022", "#54422e", 6, SurfaceMotif.Granular, 40, 0.1f, 0.4f, "#211912", "#4a3a28", 0.45f, 0.4f),
			R(Ground.Clay, 5f, 0.04f, "#7a3e22", "#a0562e", "#c27446", 6, SurfaceMotif.Cracks, 10, 0.04f, 0.7f, "#4a2414", "#5a2c18", 0.15f),
			R(Ground.Sandstone, 8f, 0.25f, "#8a4a2a", "#b86a3e", "#d89a64", 4, SurfaceMotif.Strata, 14, 0.1f, 0.75f, "#7a3c22", "#e0b080", 0.1f, 0.05f, 0.8f),
			R(Ground.Sand, 4f, 0.02f, "#b08a56", "#d2ae74", "#e8cc94", 6, SurfaceMotif.Ripples, 18, 0.1f, 0.6f, "#a88452", "#c8a46c", 0.12f, 0.05f, 0.4f),
			R(Ground.SandBeach, 4f, 0.02f, "#c8b48c", "#e0cfa6", "#f0e2c0", 6, SurfaceMotif.Ripples, 10, 0.1f, 0.4f, "#b8a47c", "#d8c69c", 0.2f, 0.3f, 0.4f,
				speckles: Hexes("#f4ede0", "#cbbfa8", "#8a8478"), speckleCount: 400, speckleSize: 0.0025f),
			R(Ground.Gravel, 2f, 0.03f, "#4a443c", "#6a645a", "#8a8478", 8, SurfaceMotif.Cobbles, 40, 0.12f, 0.85f, "#5a5650", "#a8a294", 0.15f, 0.1f, 0.85f),
			R(Ground.Pebbles, 2f, 0.05f, "#4e4436", "#6e624e", "#8e8068", 8, SurfaceMotif.Cobbles, 14, 0.25f, 0.9f, "#4e4a44", "#b4aa98", 0.3f, 0.2f, 0.85f),
			R(Ground.Rock, 6f, 0.3f, "#4a4744", "#6e6a64", "#94908a", 4, SurfaceMotif.Fractured, 6, 0.06f, 0.7f, "#3a3734", "#7a766e", 0.2f, 0.1f, 0.8f),
			R(Ground.CliffRock, 10f, 0.6f, "#4e4840", "#746c60", "#9a9284", 4, SurfaceMotif.Strata, 22, 0.1f, 0.8f, "#423c34", "#a8a090", 0.15f, 0.05f, 0.85f),
			R(Ground.Basalt, 6f, 0.3f, "#1c1c1e", "#2e2e30", "#464648", 4, SurfaceMotif.Fractured, 8, 0.05f, 0.75f, "#141416", "#3a3a3c", 0.25f, 0.1f, 0.85f),
			R(Ground.Lava, 6f, 0.15f, "#1a1210", "#2a1a14", "#3a2a22", 4, SurfaceMotif.Cracks, 8, 0.05f, 0.8f, "#ff4a00", "#ffb020", 0.2f, 0.1f, 0.6f),
			R(Ground.Ash, 4f, 0.02f, "#2a2826", "#3c3a37", "#55524e", 6, SurfaceMotif.Granular, 48, 0.1f, 0.5f, "#1e1d1c", "#4a4744", 0.05f, 0.05f),
			R(Ground.Snow, 6f, 0.03f, "#c8d0dc", "#e4e9f0", "#f8fbff", 4, SurfaceMotif.Granular, 16, 0.1f, 0.3f, "#d0d8e4", "#ffffff", 0.45f, 0.1f, 0.4f),
			R(Ground.Ice, 8f, 0.05f, "#6f9cc0", "#a6c8e0", "#d8ecf8", 3, SurfaceMotif.Fractured, 4, 0.02f, 0.5f, "#e8f4ff", "#ffffff", 0.8f, 0.1f, 0.5f),
			R(Ground.SaltCrust, 6f, 0.03f, "#c8c4bc", "#e2ded6", "#f6f4f0", 6, SurfaceMotif.Ridges, 8, 0.03f, 0.7f, "#f8f8f6", "#ffffff", 0.3f, 0.1f, 0.6f),
			R(Ground.Regolith, 4f, 0.03f, "#4a4a48", "#6e6c68", "#8e8c88", 8, SurfaceMotif.Granular, 80, 0.1f, 0.5f, "#3e3e3c", "#8a8884", 0.05f, 0.05f,
				speckles: Hexes("#3a3936", "#9a9894"), speckleCount: 1500, speckleSize: 0.002f),
			R(Ground.Sulphur, 4f, 0.03f, "#b89a20", "#d8c040", "#f0e070", 6, SurfaceMotif.Cracks, 12, 0.03f, 0.6f, "#8a6a10", "#c0a030", 0.2f),
			R(Ground.Peat, 3f, 0.03f, "#1e140c", "#2e2014", "#44301e", 6, SurfaceMotif.Fibres, 48, 0.1f, 0.6f, "#120c08", "#5a4028", 0.35f, 0.3f),
			R(Ground.Limestone, 8f, 0.3f, "#9a968a", "#bcb8aa", "#dcd8ca", 4, SurfaceMotif.Fractured, 5, 0.08f, 0.7f, "#8a8676", "#d0ccbc", 0.2f, 0.1f, 0.8f),
			R(Ground.Lichen, 3f, 0.03f, "#4a4a3a", "#6a6a52", "#8a8a6a", 6, SurfaceMotif.Clumps, 30, 0.35f, 0.6f, "#7a8a4a", "#b0b070", 0.15f, 0.1f, 0.7f,
				speckles: Hexes("#d0c890", "#c0603a"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.Silt, 6f, 0.03f, "#2a3a40", "#44565a", "#627478", 4, SurfaceMotif.Granular, 32, 0.1f, 0.4f, "#22323a", "#56686c", 0.3f, 0.1f),
			R(Ground.Coral, 4f, 0.12f, "#a8a088", "#c4bca4", "#ddd6c0", 6, SurfaceMotif.Clumps, 20, 0.4f, 0.8f, "#c05a5a", "#e8a060", 0.25f, 0.1f, 0.8f,
				speckles: Hexes("#6ac0c8", "#c070c0", "#f0e080"), speckleCount: 700, speckleSize: 0.004f),
			R(Ground.Tholin, 4f, 0.03f, "#5a2a12", "#8a4a22", "#b06a34", 6, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#4a220e", "#a05e2c", 0.08f, 0.05f),
			R(Ground.Frost, 6f, 0.03f, "#b8b0b8", "#d8d0d8", "#f0ecf0", 6, SurfaceMotif.Ridges, 10, 0.025f, 0.6f, "#e8e2e8", "#ffffff", 0.55f, 0.1f, 0.5f),
			R(Ground.CrackedEarth, 4f, 0.04f, "#6a5034", "#8a6a48", "#a8886a", 6, SurfaceMotif.Cracks, 14, 0.03f, 0.7f, "#3a2a1a", "#4a3424", 0.1f),
			R(Ground.Flagstone, 4f, 0.04f, "#5a5650", "#7a766e", "#9a968c", 6, SurfaceMotif.Cobbles, 6, 0.04f, 0.8f, "#6a665e", "#aaa69a", 0.2f, 0.1f, 0.8f),
			// ── The vegetation expansion (2026-10-10) ── Appended, so no other family's order moves (each texture is
			// seeded by its own family's name anyway, SurfaceSynth).

			// Raised-bog sphagnum: Sphagnum magellanicum's wine-red and S. papillosum's ochre cushions among green
			// hummocks a hand across, wet and shining in the hollows between; red, ochre and pale capitula flecked over.
			R(Ground.Sphagnum, 3f, 0.04f, "#2e3212", "#5a6a22", "#9a9436", 6, SurfaceMotif.Clumps, 18, 0.55f, 0.75f, "#6a2a1e", "#94a63a", 0.35f, 0.25f, 0.75f,
				speckles: Hexes("#8a3020", "#b08a30", "#a8c060"), speckleCount: 1400, speckleSize: 0.003f),
			// Ling heather moorland seen from above: dark wiry stems every way over brown peaty litter, flecked with the
			// purple of its flowers and the green of new shoots.
			R(Ground.Heath, 3f, 0.05f, "#261a1e", "#4c3840", "#6e5660", 4, SurfaceMotif.Blades, 8, 0.022f, 0.8f, "#3a2a2c", "#7a5e5e", 0.1f, motifCount: 30000,
				speckles: Hexes("#8a4a7e", "#a46a9a", "#4e5e34"), speckleCount: 4000, speckleSize: 0.003f),
			// Geyserite: pale grey sinter in crusted rims a few centimetres high, the rims stained orange and brown by
			// the thermophile mats that grow where the runoff cools.
			R(Ground.Sinter, 6f, 0.04f, "#a8a49a", "#ccc8bc", "#e8e4d8", 6, SurfaceMotif.Ridges, 10, 0.06f, 0.6f, "#a8622e", "#d8a050", 0.35f, 0.15f, 0.6f,
				speckles: Hexes("#c06a2a", "#7a5a3a", "#eee8d6"), speckleCount: 900, speckleSize: 0.004f),
			// Cinder: loose lapilli a centimetre or two across, glassy black, some oxidised red, in a finer black grit.
			R(Ground.Cinder, 2.5f, 0.04f, "#100e0e", "#221a18", "#382622", 8, SurfaceMotif.Cobbles, 56, 0.15f, 0.8f, "#161010", "#4a2218", 0.06f, 0.05f, 0.85f,
				speckles: Hexes("#7a3020", "#0e0c0c"), speckleCount: 1200, speckleSize: 0.003f),
			// Pahoehoe: a glassy black skin dragged into ropes a hand apart as the flow beneath it moved, the folds
			// curving (warped ripples) and shining where the glass is fresh.
			R(Ground.Pahoehoe, 4f, 0.08f, "#121214", "#242428", "#3c3a40", 4, SurfaceMotif.Ripples, 22, 0.1f, 0.7f, "#0c0c0e", "#4e4648", 0.55f, 0.2f, 0.8f, warp: 0.12f,
				speckles: Hexes("#4a3a5a", "#3a4a5a"), speckleCount: 400, speckleSize: 0.002f),
			// Reg: a close mosaic of wind-sorted stones two to five centimetres across, varnished brown-black and rusty
			// by the desert, set in pale silt; the varnish has a dull shine.
			R(Ground.Reg, 2.5f, 0.03f, "#8a7458", "#a88e6c", "#c4aa86", 8, SurfaceMotif.Cobbles, 44, 0.08f, 0.8f, "#2a2420", "#5e3e2a", 0.3f, 0.15f, 0.8f),
			// Dust: fine bright grains with low ripples a few tens of centimetres apart, a few darker and brighter specks.
			R(Ground.Dust, 6f, 0.015f, "#7a7670", "#9e9a92", "#c2beb4", 8, SurfaceMotif.Ripples, 14, 0.1f, 0.35f, "#8a867e", "#b4b0a6", 0.04f, 0.03f, 0.4f,
				speckles: Hexes("#6a6660", "#d8d4cc"), speckleCount: 900, speckleSize: 0.0015f),
			// Lineae: bright water ice, its fractures filled with bands of rust-brown salts (Europa's reddish material).
			R(Ground.Lineae, 8f, 0.05f, "#a8b8c4", "#c8d4dc", "#e4ecf0", 4, SurfaceMotif.Cracks, 6, 0.06f, 0.6f, "#6a3a22", "#9a5a34", 0.5f, 0.1f, 0.5f,
				speckles: Hexes("#8a4a2a"), speckleCount: 500, speckleSize: 0.003f),
			// Metal frost: a bright metallic coating crystallised in a net over dark basalt, brassy where pyrite shows.
			R(Ground.MetalFrost, 4f, 0.03f, "#4e4e52", "#86868a", "#b8b6b0", 6, SurfaceMotif.Ridges, 12, 0.03f, 0.6f, "#c8c4b8", "#e8e4d4", 0.55f, 0.15f, 0.6f,
				speckles: Hexes("#b8a060", "#8a8a90"), speckleCount: 1000, speckleSize: 0.002f, metallic: 0.6f),
			// Tholin dunes: dark orange-brown organic sand in long ripples, darker grains in the troughs.
			R(Ground.TholinDune, 4f, 0.03f, "#4a2410", "#7a3e1c", "#a05c2e", 6, SurfaceMotif.Ripples, 16, 0.1f, 0.6f, "#5a2a12", "#9a5a2c", 0.06f, 0.05f, 0.45f,
				speckles: Hexes("#2e160a"), speckleCount: 600, speckleSize: 0.002f),
			// ── Paths (2026-10-10) ── What a way wears down to (PathGroundFor). Low relief and fine grain: packed ground has
			// lost its loose crumb, and what shows is grit and small stones worked up through it, a little shine where pressed.

			// Trodden loam: dark brown packed earth, fine and close, grey grit and the odd pebble.
			R(Ground.PathLoam, 3f, 0.015f, "#2c2219", "#46382a", "#63513d", 6, SurfaceMotif.Granular, 36, 0.12f, 0.45f, "#221a12", "#7a6650", 0.2f, 0.1f, 0.75f,
				speckles: Hexes("#8a847a", "#5e5a54", "#a49a88"), speckleCount: 1600, speckleSize: 0.003f),
			// Dry earth: dusty fawn, paler where the dust lies thickest, pale and grey stones showing.
			R(Ground.PathDryEarth, 3f, 0.015f, "#6a5842", "#8c7656", "#ae9672", 6, SurfaceMotif.Granular, 40, 0.12f, 0.45f, "#5a4834", "#c2ad8a", 0.1f, 0.05f, 0.75f,
				speckles: Hexes("#d0c4ac", "#7a7064", "#9a8e7e"), speckleCount: 1800, speckleSize: 0.003f),
			// Red earth: packed laterite, rust and brick, pale stones in it.
			R(Ground.PathRedEarth, 3f, 0.015f, "#5e3020", "#82462c", "#a46244", 6, SurfaceMotif.Granular, 40, 0.12f, 0.45f, "#4a2416", "#b4744e", 0.12f, 0.05f, 0.75f,
				speckles: Hexes("#c8a080", "#5a3a2a"), speckleCount: 1200, speckleSize: 0.003f),
			// Packed sand: the ripples trodden out, a firmer, slightly darker crust with grit in it.
			R(Ground.PathSand, 4f, 0.01f, "#9c7c54", "#bc9c70", "#d6ba8c", 6, SurfaceMotif.Granular, 24, 0.1f, 0.35f, "#8a6c48", "#e0c69a", 0.12f, 0.05f, 0.5f,
				speckles: Hexes("#7a6a54", "#e8dcc0"), speckleCount: 1000, speckleSize: 0.0025f),
			// Grit: angular broken stone a centimetre or two across in a grey-brown fine.
			R(Ground.PathGrit, 2.5f, 0.02f, "#544e46", "#726a60", "#90887a", 8, SurfaceMotif.Cobbles, 64, 0.06f, 0.6f, "#4a4640", "#a8a092", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#3a3632", "#b4ac9c"), speckleCount: 1400, speckleSize: 0.003f),
			// Trampled snow: packed and glazed, greyer than fresh snow, dirtied with earth and grit.
			R(Ground.PathSnow, 5f, 0.01f, "#9aa2ae", "#bcc4d0", "#dde3ec", 4, SurfaceMotif.Granular, 20, 0.1f, 0.35f, "#8a929e", "#eef2f8", 0.55f, 0.15f, 0.5f,
				speckles: Hexes("#6a645c", "#8a8278"), speckleCount: 900, speckleSize: 0.003f),
			// Mud: dark, wet, shining where it is pressed, bits of green and peat in it.
			R(Ground.PathMud, 3f, 0.012f, "#1a130c", "#2a2016", "#3e3022", 6, SurfaceMotif.Granular, 30, 0.12f, 0.4f, "#120d08", "#4e3c2a", 0.55f, 0.35f, 0.7f,
				speckles: Hexes("#4a4034", "#2e3a1c"), speckleCount: 600, speckleSize: 0.003f),
			// Ash: packed charcoal grey, finer than the loose ash beside it, rusty cinders in it.
			R(Ground.PathAsh, 3f, 0.012f, "#1a1817", "#2a2826", "#3e3b38", 6, SurfaceMotif.Granular, 50, 0.1f, 0.45f, "#121110", "#4a4642", 0.08f, 0.05f, 0.7f,
				speckles: Hexes("#5a3a2a", "#6a6660"), speckleCount: 1000, speckleSize: 0.003f),
			// Salt: the crust broken and packed into a grey track, its polygons gone.
			R(Ground.PathSalt, 4f, 0.01f, "#9e9a92", "#bcb8b0", "#d6d2ca", 6, SurfaceMotif.Granular, 30, 0.1f, 0.4f, "#8a867e", "#e6e2da", 0.3f, 0.1f, 0.6f,
				speckles: Hexes("#7a766e"), speckleCount: 800, speckleSize: 0.003f),
			// A forest path: dark packed earth, a few leaves and needles kicked onto it, roots' grit.
			R(Ground.PathLitter, 3f, 0.015f, "#2a1f15", "#433222", "#5e4733", 6, SurfaceMotif.Leaves, 8, 0.025f, 0.35f, "#5a3a1a", "#9a6a32", 0.15f, 0.15f, 0.8f,
				motifCount: 400, speckles: Hexes("#6a6258", "#3a2e22"), speckleCount: 800, speckleSize: 0.003f),
		};

		/// <summary>
		/// The path ground a biome's footpaths and trails wear down to, by the ground it is painted with: what lies under its
		/// cover, packed. A grassland's is loam, a forest's a littered path, a bog's mud, a desert's packed sand, a
		/// snowfield's trampled snow, rock's grit. A biome's own Small Path layer, when it has one, wins over this.
		/// </summary>
		public static string PathGroundFor(string ground)
		{
			switch (ground)
			{
				case Ground.GrassDry:
				case Ground.Soil:
				case Ground.Dust:
				case Ground.Lichen:
					return Ground.PathDryEarth;
				case Ground.Clay:
				case Ground.CrackedEarth:
				case Ground.Sandstone:
				case Ground.Tholin:
					return Ground.PathRedEarth;
				case Ground.ForestFloor:
				case Ground.NeedleLitter:
					return Ground.PathLitter;
				case Ground.JungleFloor:
				case Ground.Mud:
				case Ground.Peat:
				case Ground.Sphagnum:
				case Ground.Silt:
					return Ground.PathMud;
				case Ground.Sand:
				case Ground.SandBeach:
				case Ground.TholinDune:
				case Ground.Coral:
					return Ground.PathSand;
				case Ground.Gravel:
				case Ground.Pebbles:
				case Ground.Reg:
				case Ground.Rock:
				case Ground.CliffRock:
				case Ground.Limestone:
				case Ground.Basalt:
				case Ground.Pahoehoe:
				case Ground.Regolith:
				case Ground.Lineae:
				case Ground.MetalFrost:
				case Ground.Flagstone:
					return Ground.PathGrit;
				case Ground.Snow:
				case Ground.Frost:
				case Ground.Ice:
					return Ground.PathSnow;
				case Ground.Ash:
				case Ground.Cinder:
				case Ground.Lava:
				case Ground.Sulphur:
					return Ground.PathAsh;
				case Ground.SaltCrust:
				case Ground.Sinter:
					return Ground.PathSalt;
				default:
					// Grass, meadow, moss, heath, and anything unknown.
					return Ground.PathLoam;
			}
		}

		/// <summary>
		/// The ground a biome's roads are made of: gravel, except where the country makes its roads of what it has: packed
		/// sand in the desert, packed snow on the ice, ash on the volcano, salt on the pan. A biome's own Road layer wins.
		/// </summary>
		public static string RoadGroundFor(string ground)
		{
			switch (PathGroundFor(ground))
			{
				case Ground.PathSand: return Ground.PathSand;
				case Ground.PathSnow: return Ground.PathSnow;
				case Ground.PathAsh: return Ground.PathAsh;
				case Ground.PathSalt: return Ground.PathSalt;
				default: return Ground.Gravel;
			}
		}

		/// <summary>Every bark family. Bark tiles are one metre: a trunk's UVs run in metres round and along it.</summary>
		public static readonly SurfaceRecipe[] BarkRecipes =
		{
			R(Bark.Brown, 1f, 0.02f, "#2e2218", "#4a3828", "#6a5440", 4, SurfaceMotif.Fibres, 24, 0.1f, 0.8f, "#20180f", "#5a4632", 0.1f, vertical: true),
			R(Bark.Pine, 1f, 0.025f, "#4a2e1c", "#6e4428", "#8e5c38", 4, SurfaceMotif.Cracks, 10, 0.05f, 0.8f, "#24160c", "#3a2414", 0.1f),
			R(Bark.Birch, 1f, 0.01f, "#c8c4b8", "#e0ddd2", "#f2f0e8", 4, SurfaceMotif.Fibres, 32, 0.1f, 0.5f, "#2a2622", "#4a4640", 0.2f,
				speckles: Hexes("#2a2622", "#3a3630"), speckleCount: 500, speckleSize: 0.004f),
			R(Bark.Palm, 1f, 0.02f, "#5a4a34", "#7a664a", "#9a8664", 4, SurfaceMotif.Strata, 12, 0.1f, 0.7f, "#4a3a26", "#a89470", 0.1f),
			R(Bark.Cactus, 1f, 0.015f, "#2e5a2a", "#3e7238", "#5a8a48", 4, SurfaceMotif.Fibres, 16, 0.1f, 0.7f, "#24481f", "#6a9a58", 0.35f, vertical: true,
				speckles: Hexes("#e8e0c0"), speckleCount: 800, speckleSize: 0.0025f),
			R(Bark.Bamboo, 1f, 0.01f, "#4a7a2a", "#6a9a3a", "#8ab45a", 4, SurfaceMotif.Strata, 4, 0.1f, 0.5f, "#3a6020", "#a0c070", 0.5f),
			R(Bark.Dead, 1f, 0.02f, "#5a564e", "#7a766c", "#9a968a", 4, SurfaceMotif.Fibres, 24, 0.1f, 0.8f, "#3a3630", "#aaa69a", 0.1f, vertical: true),
			// ── The vegetation expansion (2026-10-10) ──
			// Smooth: a beech's silver-grey skin, near flat, with faint wrinkles running round the stem and blotches
			// of pale and greenish lichen; worn by kapok, baobab and dragon tree too.
			R(Bark.Smooth, 1f, 0.006f, "#6a6a64", "#8e8e86", "#aeada4", 4, SurfaceMotif.Fibres, 20, 0.1f, 0.3f, "#5e5e58", "#b8b6ac", 0.25f,
				speckles: Hexes("#9aa088", "#c8c8b8", "#5a5c50"), speckleCount: 300, speckleSize: 0.006f),
			// Aspen: smooth, chalky green-white, with black lenticel bands running round the stem and black scars.
			R(Bark.Aspen, 1f, 0.008f, "#b4bca4", "#d0d6c0", "#e6e8da", 4, SurfaceMotif.Fibres, 20, 0.1f, 0.4f, "#2e2e26", "#5a5a4c", 0.2f,
				speckles: Hexes("#1e1e1a", "#3a3a30"), speckleCount: 260, speckleSize: 0.007f),
			// Olive: grey-brown, deeply and irregularly furrowed up the stem, a few pale lichen flecks.
			R(Bark.Olive, 1f, 0.03f, "#4a4438", "#6e6656", "#948a78", 4, SurfaceMotif.Fibres, 18, 0.1f, 0.85f, "#2a261e", "#8a826e", 0.1f, vertical: true,
				speckles: Hexes("#a8a890"), speckleCount: 120, speckleSize: 0.005f),
			// Date palm: the stubs of old frond bases, a hand across, packed in a diamond lattice up the stem.
			R(Bark.DatePalm, 1f, 0.04f, "#3e3224", "#6e5c44", "#8e7a5e", 4, SurfaceMotif.Cobbles, 8, 0.12f, 0.85f, "#3a2e20", "#a08a68", 0.1f, 0.1f, 0.85f),
			// Fibrous: shaggy reddish-brown bark in long, fine, peeling strips.
			R(Bark.Fibrous, 1f, 0.03f, "#3a2a20", "#5e4634", "#80644c", 4, SurfaceMotif.Fibres, 40, 0.1f, 0.85f, "#24180f", "#9a7a5a", 0.08f, vertical: true),
		};

		/// <summary>The ground recipe with this name, or false.</summary>
		public static bool TryGround(string name, out SurfaceRecipe recipe) => TryFind(GroundRecipes, name, out recipe);

		/// <summary>The bark recipe with this name, or false.</summary>
		public static bool TryBark(string name, out SurfaceRecipe recipe) => TryFind(BarkRecipes, name, out recipe);

		private static bool TryFind(SurfaceRecipe[] recipes, string name, out SurfaceRecipe recipe)
		{
			for (int i = 0; i < recipes.Length; i++)
			{
				if (recipes[i].Name == name)
				{
					recipe = recipes[i];
					return true;
				}
			}
			recipe = default;
			return false;
		}

		/// <summary>Every ground family's name, in catalogue order.</summary>
		public static IEnumerable<string> GroundNames()
		{
			foreach (SurfaceRecipe r in GroundRecipes)
			{
				yield return r.Name;
			}
		}
	}
}
#endif
