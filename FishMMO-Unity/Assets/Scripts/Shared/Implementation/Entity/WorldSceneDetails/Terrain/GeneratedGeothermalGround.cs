using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// What the cut did to a scene's hot ground (2026-10-18, phase I2q, decision 47): the heightmap edits that dug its springs'
	/// pools and mud pots, raised its geysers' cones and rims and ran its runoff channels, the pools' water, and where its Great
	/// Spout stands. The next repaint takes the edits out before anything reads the ground (so every site is carved again from
	/// the ground it was first carved on, never on top of itself), and the place stage writes the pools' water again.
	/// </summary>
	/// <remarks>
	/// Lives on an <c>EditorOnly</c>-tagged root object, which Unity leaves out of every build: the heights it describes are in
	/// the terrain data, the water in the hydrology asset, and every peer finds the sites themselves from the biome map
	/// (GeothermalVents.SceneSites). Edits are per tile, named by the tile's place in the scene's tile grid, with the deltas read
	/// back after the write, as <see cref="GeneratedFieldCarve"/>'s are, so taking them out is exact.
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class GeneratedGeothermalGround : MonoBehaviour
	{
		/// <summary>The name of the editor-only root object that carries this.</summary>
		public const string ObjectName = "Geothermal Ground";

		/// <summary>The edits in the order they were made (taken out in the reverse order).</summary>
		public List<GeneratedFieldCarve.TileEdit> Edits = new List<GeneratedFieldCarve.TileEdit>();

		/// <summary>The hot springs' pools, as the hydrology holds them (lake ids in the paddies' range, kind 7).</summary>
		public List<SceneHydrology.Lake> Pools = new List<SceneHydrology.Lake>();

		/// <summary>How many sites the scene has, and how many the cut dug (the rest stand in water or in a site's footprint).</summary>
		public int Sites, Carved, Runoffs;

		/// <summary>True when the scene has a geyser, and so a Great Spout (GeothermalVents.GreatSpoutIndex).</summary>
		public bool HasGreatSpout;
		/// <summary>The Great Spout's place (world xz), seed and size: what a Spout clock follows (its eruptions from GeothermalVents.Moment).</summary>
		public Vector2 GreatSpoutPosition;
		public uint GreatSpoutSeed;
		public float GreatSpoutSize;
	}
}
