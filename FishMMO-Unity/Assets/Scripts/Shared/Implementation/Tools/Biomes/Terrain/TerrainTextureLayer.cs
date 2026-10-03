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
		[Header("Terrain Layer")]
		/// <remarks>
		/// <para>
		/// The one place a layer's art is swapped. Generated ground textures arrive as
		/// <see cref="TerrainLayer"/> assets and are assigned here; replacing them with bought or
		/// painted art is assigning a different asset, and no generator ever writes over a layer
		/// whose asset is not one it made.
		/// </para>
		/// <para>
		/// A TerrainLayer rather than more fields because it already carries everything the ground
		/// shader reads — normal scale, tile offset and the mask and diffuse remaps — and because
		/// it is what Unity's own terrain tools paint with, so a designer touching up a generated
		/// scene by hand paints with exactly the layers the generator used.
		/// </para>
		/// </remarks>
		[Tooltip("The layer's art. When set, this asset IS the layer: its textures, tiling, normal scale and remaps are what the ground draws, and the texture and material fields below are ignored. Leave empty to have one built from those fields.")]
		public TerrainLayer terrainLayer;

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

		/// <summary>True when the layer has a texture to paint with, from its terrain layer or its own field.</summary>
		public bool HasAlbedo => (terrainLayer != null && terrainLayer.diffuseTexture != null) || albedoTexture != null;
	}
}
