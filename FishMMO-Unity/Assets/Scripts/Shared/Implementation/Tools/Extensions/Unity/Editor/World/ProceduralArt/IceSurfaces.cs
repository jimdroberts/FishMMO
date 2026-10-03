#if UNITY_EDITOR
using System;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// How an ice surface's material should be set up on URP Lit, for the generator to apply.
	/// </summary>
	public struct IceMaterialProposal
	{
		/// <summary>The surface recipe it dresses.</summary>
		public string Surface;
		/// <summary>Specular colour for URP Lit's Specular workflow (sRGB): ice's own reflectance.</summary>
		public Color Specular;
		/// <summary>Base smoothness; the albedo alpha carries the per-texel variation on top.</summary>
		public float Smoothness;
		/// <summary>Emission as a share of the albedo: a faint glow standing in for light scattered back out of the ice.</summary>
		public float EmissionShare;
		/// <summary>Complex Lit clear coat mask, 0 for none: the film of melt water on a washed or sunlit face.</summary>
		public float ClearCoat;
	}

	/// <summary>
	/// The ice surfaces: tileable recipes for the ice meshes, built with the existing motifs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why ice has more than one colour.</b> Ice is clear; its colour is what its volume does to
	/// light. Water ice absorbs red a little (about 0.6 per metre at 650 nm, a hundred times less at
	/// 450), so light that has travelled metres through dense, bubble-free ice comes out blue — the
	/// glacial blue of a calving face or an overturned berg. Air bubbles scatter all wavelengths
	/// before any path is long enough to absorb, so bubbly ice and firn are white with only a blue
	/// shade in their shadows. Sea ice holds brine pockets and is thinner, so it is grey-white, greyer
	/// where thin; and basal ice, frozen on at the glacier's bed, carries bands of silt and stones.
	/// The four recipes are those four materials, with ramps picked from daylight photographs of
	/// each (Jökulsárlón and Perito Moreno for glacial blue, Greenland and Antarctic bergs for the
	/// white tops, first-year Arctic sea ice, and debris-banded Antarctic and Svalbard basal ice), in
	/// sRGB as shadowed, typical and sunlit stops like every other recipe.
	/// </para>
	/// <para>
	/// <b>What an opaque URP Lit material can and cannot do.</b> It cannot scatter light under the
	/// surface: no subsurface scattering, so a thin spire or a berg's edge with the sun behind it does
	/// not glow; no transmission and no refraction, so one cannot see into the ice; and no absorption
	/// by thickness, so the blue does not deepen where the ice is thicker. It shades the surface only:
	/// the hue (albedo), how much and how sharply it reflects (specular and smoothness), small relief
	/// (the normal map) and cavity darkening (occlusion). So the blue lives in the albedo, which is
	/// right for what the eye mostly sees, and wrong mainly in back-light and at thin edges.
	/// </para>
	/// <para>
	/// <b>Proposed settings</b> (<see cref="Materials"/>). Use the Specular workflow, not Metallic:
	/// Metallic fixes a dielectric's reflectance at 4 %, but ice's refractive index of 1.31 gives
	/// ((1.31 − 1)/(1.31 + 1))² = 1.8 %, a specular of #252525 in sRGB — at 4 % ice looks like
	/// plastic. Smoothness high on dense blue ice and on everything the sea washes (0.85–0.9), low on
	/// bubbly white ice and firn (0.3–0.4), whose surface is a granular crust. A faint emission (a few
	/// percent of the albedo) lifts the shadowed side the way scattered light does in real ice and is
	/// the cheapest stand-in for subsurface light; keep it small, since it ignores shadows and night.
	/// Where it can be afforded, Complex Lit's clear coat (mask about 0.5, smoothness 0.95) gives the
	/// wet sheen of melting or wave-washed ice. A berg's submesh 0 takes <see cref="BubblyWhite"/>
	/// and submesh 1 (the wave-washed band and all below it) <see cref="GlacialBlue"/>. Real
	/// translucency needs a Shader Graph: wrap lighting plus a thickness term for back-lit edges.
	/// </para>
	/// </remarks>
	public static class IceSurfaces
	{
		/// <summary>Dense, bubble-poor glacier ice: deep blue, polished, crossed by white fracture planes.</summary>
		public const string GlacialBlue = "IceGlacialBlue";
		/// <summary>Bubbly white ice and firn: berg tops and weathered faces.</summary>
		public const string BubblyWhite = "IceBubblyWhite";
		/// <summary>First-year sea ice: grey-white, rafted and brine-channelled.</summary>
		public const string SeaIce = "IceSea";
		/// <summary>Basal ice: blue-grey ice banded with silt and studded with stones.</summary>
		public const string BasalDebris = "IceBasalDebris";

		private static Color H(string hex) => SurfaceCatalogue.Hex(hex);

		/// <summary>Every ice surface, in a fixed order.</summary>
		public static readonly SurfaceRecipe[] Recipes =
		{
			new SurfaceRecipe
			{
				Name = GlacialBlue,
				TileMetres = 8f,
				ReliefMetres = 0.04f,
				Dark = H("#1d5a86"),
				Mid = H("#4b93c1"),
				Light = H("#a6d6ee"),
				BaseCells = 3,
				Octaves = 4,
				Persistence = 0.45f,
				Warp = 0.08f,
				// Large crystals and old fracture planes, drawn as thin pale joints in the blue.
				Motif = SurfaceMotif.Fractured,
				MotifCells = 4,
				MotifSize = 0.015f,
				MotifWeight = 0.3f,
				MotifDark = H("#2b6e9c"),
				MotifLight = H("#dff2fb"),
				Smoothness = 0.85f,
				SmoothnessVariation = 0.08f,
				Metallic = 0f,
				Occlusion = 0.35f,
				NormalScale = 0.8f,
			},
			new SurfaceRecipe
			{
				Name = BubblyWhite,
				TileMetres = 6f,
				ReliefMetres = 0.03f,
				Dark = H("#9fb5c6"),
				Mid = H("#dce7ef"),
				Light = H("#f6fafc"),
				BaseCells = 4,
				Octaves = 5,
				Persistence = 0.5f,
				Warp = 0.05f,
				// A granular crust of bubbly grains, with the odd brighter cluster of bubbles.
				Motif = SurfaceMotif.Granular,
				MotifCells = 20,
				MotifSize = 0.1f,
				MotifWeight = 0.4f,
				MotifDark = H("#c6d5e0"),
				MotifLight = H("#ffffff"),
				Speckles = new[] { H("#ffffff"), H("#eef5fa") },
				SpeckleCount = 600,
				SpeckleSize = 0.0015f,
				Smoothness = 0.35f,
				SmoothnessVariation = 0.1f,
				Metallic = 0f,
				Occlusion = 0.45f,
				NormalScale = 1f,
			},
			new SurfaceRecipe
			{
				Name = SeaIce,
				TileMetres = 6f,
				ReliefMetres = 0.04f,
				Dark = H("#7b93a3"),
				Mid = H("#b7c7d1"),
				Light = H("#e3ebf0"),
				BaseCells = 4,
				Octaves = 5,
				Persistence = 0.5f,
				Warp = 0.06f,
				// Low raised ridges where thin sheets rafted over each other, paler than the level ice.
				Motif = SurfaceMotif.Ridges,
				MotifCells = 8,
				MotifSize = 0.02f,
				MotifWeight = 0.45f,
				MotifDark = H("#9fb3c0"),
				MotifLight = H("#edf3f6"),
				Smoothness = 0.4f,
				SmoothnessVariation = 0.2f,
				Metallic = 0f,
				Occlusion = 0.5f,
				NormalScale = 1f,
			},
			new SurfaceRecipe
			{
				Name = BasalDebris,
				TileMetres = 6f,
				ReliefMetres = 0.08f,
				Dark = H("#3b4a52"),
				Mid = H("#6f848e"),
				Light = H("#a9bcc4"),
				BaseCells = 4,
				Octaves = 5,
				Persistence = 0.5f,
				Warp = 0.1f,
				// Silt bands frozen on at the bed, folded by the flow; stones scattered through them.
				Motif = SurfaceMotif.Strata,
				MotifCells = 18,
				MotifSize = 0.1f,
				MotifWeight = 0.6f,
				MotifDark = H("#3f362c"),
				MotifLight = H("#8d7f6b"),
				Speckles = new[] { H("#2e2a26"), H("#5b544b"), H("#8a8378") },
				SpeckleCount = 900,
				SpeckleSize = 0.003f,
				Smoothness = 0.45f,
				SmoothnessVariation = 0.25f,
				Metallic = 0f,
				Occlusion = 0.7f,
				NormalScale = 1f,
			},
		};

		/// <summary>URP Lit settings for each surface (see the class remarks for why).</summary>
		public static readonly IceMaterialProposal[] Materials =
		{
			new IceMaterialProposal { Surface = GlacialBlue, Specular = H("#252525"), Smoothness = 0.88f, EmissionShare = 0.05f, ClearCoat = 0.5f },
			new IceMaterialProposal { Surface = BubblyWhite, Specular = H("#252525"), Smoothness = 0.35f, EmissionShare = 0.03f, ClearCoat = 0f },
			new IceMaterialProposal { Surface = SeaIce, Specular = H("#252525"), Smoothness = 0.45f, EmissionShare = 0.02f, ClearCoat = 0.2f },
			new IceMaterialProposal { Surface = BasalDebris, Specular = H("#252525"), Smoothness = 0.5f, EmissionShare = 0.01f, ClearCoat = 0.3f },
		};

		/// <summary>The ice recipe with this name, or false.</summary>
		public static bool TryGet(string name, out SurfaceRecipe recipe)
		{
			int i = Array.FindIndex(Recipes, r => r.Name == name);
			recipe = i >= 0 ? Recipes[i] : default;
			return i >= 0;
		}
	}
}
#endif
