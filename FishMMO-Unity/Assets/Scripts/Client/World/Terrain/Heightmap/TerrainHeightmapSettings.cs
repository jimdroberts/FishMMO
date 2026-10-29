using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>The client's own heightmap draw (<see cref="TerrainHeightmapSystem"/>), on the weather render profile.</summary>
	[Serializable]
	public class TerrainHeightmapSettings
	{
		[Tooltip("Draw the generated scenes' ground with the client's own GPU-driven heightmap draw (one indirect draw a scene for every tile, geomorphed patches chosen on the GPU, shadows from the same patches) instead of Unity's terrain. Off: Unity's terrain draws it again, as before (an A/B; live in play). Collision, the editor, the cut and the server always use Unity's terrain.")]
		public bool Enabled = true;
	}
}
