using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared
{
	/// <summary>One biome's use of a terrain layer: which biome, and which of its layer slots.</summary>
	[Serializable]
	public sealed class TerrainArrayLayerUse
	{
		public BiomeTemplate Biome;
		[Tooltip("The biome's slot: main, detail/<i>, cliff/<i>, lakebed or riverbed.")]
		public string Slot;
	}

	/// <summary>One layer of a scene's terrain, in channel order, and every biome slot that painted with it.</summary>
	[Serializable]
	public sealed class TerrainArrayLayerSource
	{
		[Tooltip("The committed layer the terrain paints with. Slice i of the arrays draws it, or its LOCAL override.")]
		public TerrainLayer Layer;
		[Tooltip("The biome slots that use this layer, the biome covering most of the scene first: that one's LOCAL override wins a disagreement.")]
		public List<TerrainArrayLayerUse> Uses = new List<TerrainArrayLayerUse>();
	}

	/// <summary>
	/// Where each of a scene's terrain layers came from, so its art can be found again — LOCAL
	/// overrides included — every time the arrays are baked.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Committed, and references only committed assets</b>: a biome and its slot key, and the
	/// committed <see cref="TerrainLayer"/>. A LOCAL override is looked up from these at bake time,
	/// never stored, so a clone without the LOCAL folder bakes the committed art and one with it bakes
	/// the licensed art, from the same scene.
	/// </para>
	/// <para>
	/// <b>On its own <c>EditorOnly</c> object</b>, a child of the binder's. Only the baker reads it,
	/// and a scene reference to a <see cref="BiomeTemplate"/> would otherwise pull every biome's
	/// textures and spawn prefabs into the scene bundle — art the arrays replace, shipped twice.
	/// <c>EditorOnly</c> objects never reach a build, so neither do the references.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class TerrainArrayProvenance : MonoBehaviour
	{
		/// <summary>The child object's name and tag.</summary>
		public const string ObjectName = "Terrain Array Provenance";
		public const string EditorOnlyTag = "EditorOnly";

		[Tooltip("The scene's terrain layers in channel order.")]
		public List<TerrainArrayLayerSource> Layers = new List<TerrainArrayLayerSource>();
	}
}
