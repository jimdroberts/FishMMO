using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The heightmap edits the cut's ridge-and-furrow fields made (2026-10-18, phase I2h), so the next repaint or Regenerate
	/// POIs can take them out before anything reads the ground: every field is carved again from the ground it was first
	/// carved on, never on top of its own ridges.
	/// </summary>
	/// <remarks>
	/// Lives on an <c>EditorOnly</c>-tagged child of the "Points of Interest" root, which Unity leaves out of every build: the
	/// heights it describes are already in the terrain data. Sparse (only the samples a field raised), one record per field
	/// per tile, the tile named by its place in the scene's tile grid rather than by its terrain data, so a LOCAL copy with
	/// terrain data of its own takes its own ridges out. The deltas are what the terrain data took (read back after the
	/// write, so its 16-bit rounding is in them), which is what makes taking them out exact.
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class GeneratedFieldCarve : MonoBehaviour
	{
		/// <summary>One field's raise on one tile: heightmap sample indices (row × resolution + column) and normalised deltas.</summary>
		[Serializable]
		public sealed class TileEdit
		{
			/// <summary>The tile's column and row in the scene's tile grid (TerrainTilePlan).</summary>
			public int TileX;
			public int TileZ;
			/// <summary>The tile's heightmap resolution when it was carved; a tile of another resolution is left alone.</summary>
			public int Resolution;
			public List<int> Samples = new List<int>();
			public List<float> Deltas = new List<float>();
		}

		/// <summary>The name of the editor-only child that carries this.</summary>
		public const string ObjectName = "Field Carve";

		/// <summary>The edits in the order they were made (taken out in the reverse order).</summary>
		public List<TileEdit> Edits = new List<TileEdit>();
	}
}
