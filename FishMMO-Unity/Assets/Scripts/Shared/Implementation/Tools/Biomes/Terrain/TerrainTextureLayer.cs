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

	/// <summary>What a ground is made of, as the planet's looks see it (<see cref="TerrainTextureLayer.GroundMakeup"/>).</summary>
	[Serializable]
	public struct SurfaceMakeup
	{
		[Tooltip("Off: not set — the layer's name decides what it is made of, as before 2026-10-15, with a warning.")]
		public bool Set;
		[Tooltip("Living plant matter: grass sward, moss, heath (1); litter and peat partly. Wears the planet's and the patch's leaves.")]
		[Range(0f, 1f)] public float Living;
		[Tooltip("Lichen and coral: wears the patch's lichen colour.")]
		[Range(0f, 1f)] public float Lichen;
		[Tooltip("Rock, sand, soil, ash: takes the world's own ground colour where its air is thin or absent. Snow and ice keep most of their white (0.3).")]
		[Range(0f, 1f)] public float Mineral;
	}

	/// <summary>
	/// The ways across one ground layer of a biome (<see cref="TerrainTextureLayer.ways"/>): the path, road and cobble grounds a
	/// way is surfaced with where this ground is the strongest under it. Each empty one is the biome's own slot (Small Path,
	/// Road, Cobbles), as every ground was before 2026-10-16.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Decision 19 (2026-10-16, the Taiga round): "a biome's detail layer may carry its own road/path surface (e.g. Taiga
	/// corduroy log road only across bog layers, gravel elsewhere)". The scene cut lays each override into the terrain arrays
	/// beside the biome's own way grounds and points the ground layer's column of the layer table at it, so the terrain draws a
	/// road over this ground with this ground's road; the way's shape (trail, cart track, road, street) still picks which of the
	/// three it wears, exactly as it does with the biome's.
	/// </para>
	/// <para>
	/// Read on the biome's main and detail layers only: cliffs, beds and bars carry no ways of their own, and the ways' own
	/// slots (Small Path, Road, Cobbles) are the biome's. Terrain layer assets rather than whole texture layers, because a way
	/// ground is never painted or scattered on (only its art and tiling are read) and a texture layer cannot hold another.
	/// Biome art authoring fills them from the biome's spec (a layer's Ways) and keeps any somebody set by hand.
	/// </para>
	/// </remarks>
	[Serializable]
	public struct GroundWays
	{
		[Tooltip("The ground a footpath, trail or cart track wears to across this ground. Empty: the biome's Small Path layer.")]
		public TerrainLayer path;
		[Tooltip("The ground a road is surfaced with across this ground. Empty: the biome's Road layer.")]
		public TerrainLayer road;
		[Tooltip("The paving of a street, highway or old road across this ground. Empty: the biome's Cobbles layer.")]
		public TerrainLayer cobbles;

		/// <summary>True when no way has a ground of its own here.</summary>
		public bool IsEmpty => path == null && road == null && cobbles == null;
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

		/// <remarks>
		/// <para>
		/// What the ground in this slot is made of, as the planet's looks see it (2026-10-15: moved off the layer's NAME, which
		/// BiomeLooks.GroundShareOf read by keywords): x the share of living plant matter (grass sward, moss, heath: it wears
		/// the planet's and the patch's leaves), y of lichen and coral (the lichen colour), z of mineral (rock, sand, soil, ash:
		/// the world's own ground colour where its air is thin or absent). A frozen ground keeps most of its own white (a small
		/// mineral share); lava none of anything (it glows its own colour).
		/// </para>
		/// <para>
		/// A property of the art in the slot: the biome art authoring writes the generated ground's own (SurfaceCatalogue
		/// .MakeupOf) whenever the slot holds generated art, and keeps a value somebody typed. Not set, the terrain array bake
		/// has none for the layer and the runtime reads the layer's name, as before, with a warning. Hand-made art needs one
		/// typed here (and Set ticked).
		/// </para>
		/// <para>
		/// A struct with its own Set flag rather than a sentinel value, so a slot serialised before the field existed reads as
		/// "not set" however the deserialiser fills it in (all zeros is a real makeup: lava's).
		/// </para>
		/// </remarks>
		[Header("Ground Makeup")]
		[Tooltip("What this ground is made of for the planet's looks. Written by Author biome art for generated grounds; for hand-made art, tick Set and type it.")]
		[SerializeField] private SurfaceMakeup groundMakeup;

		/// <summary>What the ground is made of (x living, y lichen, z mineral); only meaningful when <see cref="HasGroundMakeup"/>.</summary>
		public Vector3 GroundMakeup
		{
			get => new Vector3(groundMakeup.Living, groundMakeup.Lichen, groundMakeup.Mineral);
			set => groundMakeup = new SurfaceMakeup { Set = true, Living = value.x, Lichen = value.y, Mineral = value.z };
		}

		/// <summary>True when <see cref="GroundMakeup"/> holds a makeup rather than "not set".</summary>
		public bool HasGroundMakeup => groundMakeup.Set;

		/// <summary>Forgets the makeup ("not set": the layer's name decides).</summary>
		public void ClearGroundMakeup() => groundMakeup = default;

		[Header("Ways Across This Ground")]
		[Tooltip("Main and detail layers only: the path, road and cobble grounds a way takes where this ground is the strongest under it. Each empty one is the biome's own slot. Written by Author biome art from the spec; set by hand to override.")]
		public GroundWays ways;

		[Header("Prefab Spawning")]
		[Tooltip("Prefab spawn rules that become active wherever this texture dominates.")]
		public List<PrefabSpawnRule> prefabSpawnRules = new List<PrefabSpawnRule>();

		/// <summary>True when the layer has a texture to paint with, from its terrain layer or its own field.</summary>
		public bool HasAlbedo => (terrainLayer != null && terrainLayer.diffuseTexture != null) || albedoTexture != null;
	}
}
