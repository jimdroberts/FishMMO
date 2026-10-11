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
	public class DetailScatterSettings : ISerializationCallbackReceiver
	{
		/// <summary>
		/// Every shrub prefab's name starts with this (the art generator's ProceduralArtCatalogue.BushPrefix: the two must
		/// match). A bush is cover players hide in, so it is always scattered here, whatever <see cref="PrototypePrefixes"/>
		/// says, and is never thinned, never scaled by a density setting and drawn to a distance no client setting changes:
		/// see <see cref="BushDrawMetresPerMetre"/>.
		/// </summary>
		public const string BushPrefix = "Detail_Bush_";

		/// <summary>The most levels of detail a scattered type draws (a bush prefab's root mesh plus its LOD1 and LOD2 children).</summary>
		public const int MaxLevels = 3;

		[Tooltip("Off: every mesh detail stays on the instanced chunk renderer (built on the CPU, culled on the GPU). The dashboard's A/B button overrides it per play session.")]
		public bool Enabled = true;

		[Tooltip("Mesh detail prototype prefab names starting with any of these are scattered on the GPU (and skipped by the chunk renderer). Ground cover first: pebbles, small rocks, shells, litter (every Detail_Debris*: forest, needle, palm, wrack, branch, bamboo). Anything colliderless works, sparse or dense: each 2 m block keeps its exact expected count, so a single plant stays where its block is. Prototypes the blade grass draws are never taken here.")]
		public string[] PrototypePrefixes = { "Detail_Pebbles", "Detail_Rocks", "Detail_Shells", LitterPrefix };

		/// <summary>
		/// Every ground-litter detail (the art generator's <c>Detail_Debris*</c>: DebrisForest, and since the vegetation
		/// expansion DebrisNeedle, DebrisPalm, DebrisWrack, DebrisBranch, DebrisBamboo). All colliderless cards and twigs,
		/// scattered on the GPU like the forest litter always was. No other prefab starts with it.
		/// </summary>
		public const string LitterPrefix = "Detail_Debris";

		/// <summary>The prefix that named the only litter there was before 2026-10-10; a profile saved with it reads <see cref="LitterPrefix"/>.</summary>
		public const string LegacyLitterPrefix = "Detail_DebrisForest";

		[Header("Distance")]
		[Tooltip("Instances are drawn whole to this distance (m); beyond it a share of them, picked by a hash of their position (the same ones every frame), falls to Thin Keep At Distance at the draw distance.")]
		[Min(0f)] public float ThinStartMetres = 60f;
		[Tooltip("The share of instances kept at the draw distance itself.")]
		[Range(0.02f, 1f)] public float ThinKeepAtDistance = 0.25f;
		[Tooltip("The last share of the draw distance over which every instance dissolves (dithered), rather than stopping at a line. Rocks (FishMMO/Weather Lit) have no fade of their own at the detail distance.")]
		[Range(0.02f, 0.5f)] public float EdgeFadeBand = 0.1f;

		[Header("Bushes (cover: the same for every player)")]
		[Tooltip("A bush is drawn to this many metres per metre of its own height, between Bush Draw Min and Bush Draw Max. Never thinned and never scaled by a quality or grass-distance setting: a player hiding in one must be hidden from everyone alike.")]
		[Min(1f)] public float BushDrawMetresPerMetre = 150f;
		[Min(10f)] public float BushDrawMin = 150f;
		[Min(10f)] public float BushDrawMax = 450f;
		[Tooltip("Where a bush steps from its full mesh to its reduced one, and from that to its far one, in metres per metre of its own height.")]
		public Vector2 BushLodMetresPerMetre = new Vector2(12f, 40f);
		[Tooltip("The share of each level's reach over which it cross-fades (dithered) into the next.")]
		[Range(0.02f, 0.5f)] public float BushLodFadeBand = 0.15f;

		[Header("Budget")]
		[Tooltip("The most instances one prototype can draw for one camera view. A slot is sized from the exact upper bound of the blocks a camera lists (grow-only), never past this; appends past it are dropped, and the editor warns once.")]
		[Min(1024)] public int MaxInstancesPerSlot = 2000000;

		/// <summary>True when a prototype of this name is scattered on the GPU.</summary>
		public bool IsScatterPrototype(string name)
		{
			if (IsBush(name))
			{
				return true;
			}
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

		/// <summary>
		/// A profile saved before the litter family grew lists <see cref="LegacyLitterPrefix"/>; it is read as
		/// <see cref="LitterPrefix"/>, so the new litter is scattered without editing the asset on disk (which the open
		/// editor would overwrite with its loaded copy), and the profile saves the new prefix the next time it is saved.
		/// Detail_DebrisForest matches either, so it is scattered exactly as before.
		/// </summary>
		public void OnAfterDeserialize()
		{
			if (PrototypePrefixes == null)
			{
				return;
			}
			for (int i = 0; i < PrototypePrefixes.Length; i++)
			{
				if (string.Equals(PrototypePrefixes[i], LegacyLitterPrefix, StringComparison.Ordinal))
				{
					PrototypePrefixes[i] = LitterPrefix;
				}
			}
		}

		public void OnBeforeSerialize() { }

		/// <summary>True when a prototype of this name is a bush (<see cref="BushPrefix"/>).</summary>
		public static bool IsBush(string name) => !string.IsNullOrEmpty(name) && name.StartsWith(BushPrefix, StringComparison.Ordinal);

		/// <summary>How far a bush this tall (metres, as placed) is drawn.</summary>
		public float BushDrawDistance(float height) => DetailScatterMath.BushDrawDistance(height, BushDrawMetresPerMetre, BushDrawMin, BushDrawMax);
	}
}
