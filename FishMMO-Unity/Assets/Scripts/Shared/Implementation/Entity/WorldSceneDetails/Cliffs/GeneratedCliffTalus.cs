using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The heightmap edits a cliff placer's talus cones made, so the next placement can take them out
	/// before raising its own: re-placing cliffs never piles cone on cone.
	/// </summary>
	/// <remarks>
	/// Lives on an <c>EditorOnly</c>-tagged child of the <see cref="GeneratedCliffs"/> root, which Unity
	/// leaves out of every build: the heights it describes are already baked into the terrain data.
	/// Sparse — only the heightmap samples a cone raised — one record per terrain.
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class GeneratedCliffTalus : MonoBehaviour
	{
		/// <summary>The raise applied to one terrain: heightmap sample indices (j × resolution + i) and normalised deltas.</summary>
		[Serializable]
		public sealed class TerrainEdit
		{
			public TerrainData Data;
			public int Resolution;
			public List<int> Samples = new List<int>();
			public List<float> Deltas = new List<float>();
		}

		/// <summary>The name of the editor-only child that carries this.</summary>
		public const string ObjectName = "Talus Edits";

		public List<TerrainEdit> Edits = new List<TerrainEdit>();
	}
}
