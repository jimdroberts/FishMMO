using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>How a texture layer blends with the others in its biome.</summary>
	[Serializable]
	public enum TextureBlendMode
	{
		Linear,
		Bilinear,
	}

	/// <summary>
	/// One terrain texture of a biome — its maps, tiling, blend noise, and the height and slope
	/// bands it is confined to — plus the prefab spawn rules that apply where it dominates.
	/// Field names match the WorldEditor asset layout so exported biome templates load unchanged.
	/// </summary>
	[Serializable]
	public class TerrainTextureLayer
	{
		[Header("Textures")]
		public Texture2D albedoTexture;
		public Texture2D normalTexture;
		public Texture2D maskTexture;

		[Header("Material Properties")]
		[Range(0f, 1f)] public float metallic = 0f;
		[Range(0f, 1f)] public float smoothness = 0f;

		[Header("Tiling")]
		public Vector2 tileSize = Vector2.one * 15f;

		[Header("Blending Configuration")]
		[Tooltip("Blending mode for this texture layer.")]
		public TextureBlendMode blendMode = TextureBlendMode.Linear;
		[Tooltip("Scale of the noise pattern used for blending this texture with others in the same biome.")]
		[Range(1f, 256f)] public float blendNoiseScale = 64f;
		[Tooltip("Adds a random offset to the noise pattern to avoid repetition.")]
		public float blendNoiseOffsetX = 0f;
		public float blendNoiseOffsetY = 0f;
		[Tooltip("Controls the sharpness of the transition. Higher values create harder edges between textures.")]
		[Range(0.1f, 10f)] public float blendSharpness = 2.0f;

		[Header("Height Constraint")]
		[Tooltip("Constrains the texture to a specific height range (normalized 0-1).")]
		public bool useHeightConstraint = false;
		[MinMaxRange(0f, 1f)]
		public MinMaxRange heightRange = new MinMaxRange(0f, 1f);
		[Tooltip("How sharply the texture blends at the edges of its height range.")]
		[Range(0.001f, 0.2f)] public float heightFalloff = 0.05f;

		[Header("Slope Constraint")]
		[Tooltip("Constrains the texture to a specific slope range in degrees (0-90).")]
		public bool useSlopeConstraint = false;
		[MinMaxRange(0f, 90f)]
		public MinMaxRange slopeRange = new MinMaxRange(0f, 90f);
		[Tooltip("How sharply the texture blends at the edges of its slope range.")]
		[Range(1f, 20f)] public float slopeFalloff = 5f;

		[Header("Prefab Spawning")]
		[Tooltip("Prefab spawn rules that become active wherever this texture dominates.")]
		public List<PrefabSpawnRule> prefabSpawnRules = new List<PrefabSpawnRule>();

		/// <summary>True when the layer has a texture to paint with.</summary>
		public bool HasAlbedo => albedoTexture != null;
	}
}
