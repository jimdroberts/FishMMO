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
			int motifCount = 0, bool vertical = false, Color[] speckles = null, int speckleCount = 0, float speckleSize = 0.002f)
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
				Metallic = 0f,
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
		};

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
