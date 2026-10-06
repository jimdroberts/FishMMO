using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The GPU detail scatter (<see cref="DetailScatterSystem"/>): which mesh detail prototypes are generated on the GPU
	/// every frame around each camera instead of being built into chunks on the CPU, and how they thin with distance.
	/// On the Weather Render Profile ("GPU detail scatter"); read every frame, so changes apply while playing (the
	/// prototype list when a terrain is next taken).
	/// </summary>
	[Serializable]
	public class DetailScatterSettings
	{
		[Tooltip("Off: every mesh detail stays on the instanced chunk renderer (built on the CPU, culled on the GPU). The dashboard's A/B button overrides it per play session.")]
		public bool Enabled = true;

		[Tooltip("Mesh detail prototype prefab names starting with any of these are scattered on the GPU (and skipped by the chunk renderer). Ground cover first: pebbles, small rocks, shells, forest litter. Anything colliderless works, sparse or dense: each 2 m block keeps its exact expected count, so a single plant stays where its block is. Prototypes the blade grass draws are never taken here.")]
		public string[] PrototypePrefixes = { "Detail_Pebbles", "Detail_Rocks", "Detail_Shells", "Detail_DebrisForest" };

		[Header("Distance")]
		[Tooltip("Instances are drawn whole to this distance (m); beyond it a share of them, picked by a hash of their position (the same ones every frame), falls to Thin Keep At Distance at the draw distance.")]
		[Min(0f)] public float ThinStartMetres = 60f;
		[Tooltip("The share of instances kept at the draw distance itself.")]
		[Range(0.02f, 1f)] public float ThinKeepAtDistance = 0.25f;
		[Tooltip("The last share of the draw distance over which every instance dissolves (dithered), rather than stopping at a line. Rocks (FishMMO/Weather Lit) have no fade of their own at the detail distance.")]
		[Range(0.02f, 0.5f)] public float EdgeFadeBand = 0.1f;

		[Header("Budget")]
		[Tooltip("The most instances one prototype can draw for one camera view. A slot is sized from the exact upper bound of the blocks a camera lists (grow-only), never past this; appends past it are dropped, and the editor warns once.")]
		[Min(1024)] public int MaxInstancesPerSlot = 2000000;

		/// <summary>True when a prototype of this name is scattered on the GPU.</summary>
		public bool IsScatterPrototype(string name)
		{
			if (PrototypePrefixes == null || string.IsNullOrEmpty(name))
			{
				return false;
			}
			foreach (string prefix in PrototypePrefixes)
			{
				if (!string.IsNullOrEmpty(prefix) && name.StartsWith(prefix, StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}
	}
}
