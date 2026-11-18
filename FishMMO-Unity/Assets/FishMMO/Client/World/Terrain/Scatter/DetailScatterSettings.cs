using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The GPU detail scatter (<see cref="DetailScatterSystem"/>): how the mesh details it generates on the GPU every frame
	/// around each camera thin with distance, and the bushes' fixed reach. Every mesh detail the blade grass does not draw is
	/// scattered (<see cref="TerrainDetailRouting"/>; 2026-10-27, when the CPU chunk renderer was removed, the prefix list that
	/// chose ground cover went with it). On the Weather Render Profile ("GPU detail scatter"); read every frame, so changes
	/// apply while playing.
	/// </summary>
	/// <remarks>
	/// <see cref="BushPrefix"/> is a rendering rule — which kinds are cover, drawn alike for everyone — not a biome's decision,
	/// so it stays global: what a biome grows is its template's rules; how a bush is drawn is the same everywhere.
	/// </remarks>
	[Serializable]
	public class DetailScatterSettings
	{
		/// <summary>
		/// Every shrub prefab's name starts with this (the art generator's ProceduralArtCatalogue.BushPrefix: the two must
		/// match). A bush is cover players hide in, so it is never thinned, never scaled by a density setting and drawn to a distance no client setting changes:
		/// see <see cref="BushDrawMetresPerMetre"/>.
		/// </summary>
		public const string BushPrefix = "Detail_Bush_";

		/// <summary>The most levels of detail a scattered type draws (a bush prefab's root mesh plus its LOD1 and LOD2 children).</summary>
		public const int MaxLevels = 3;

		/// <summary>
		/// The most meshes a scattered type cycles per instance (2026-10-26, decision 22 of the biome suite): its root mesh plus
		/// bare-MeshFilter children <c>Variant1</c>, <c>Variant2</c> (<see cref="VariantChildPrefix"/>; the art generator's
		/// ProceduralArtCatalogue.DetailVariantChild: the two must match). A type with one level only (never a bush) may have
		/// them; its variants take the slots a bush's levels would, so the slot layout is unchanged.
		/// </summary>
		public const int MaxVariants = MaxLevels;

		/// <summary>The name of a detail prefab's variant child, before its index from 1 (<see cref="MaxVariants"/>).</summary>
		public const string VariantChildPrefix = "Variant";

		[Tooltip("Off: no mesh detail is drawn (there is no CPU path since 2026-10-27; an A/B of the scatter's cost). The dashboard's A/B button overrides it per play session.")]
		public bool Enabled = true;

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

		/// <summary>True when a prototype of this name is a bush (<see cref="BushPrefix"/>).</summary>
		public static bool IsBush(string name) => !string.IsNullOrEmpty(name) && name.StartsWith(BushPrefix, StringComparison.Ordinal);

		/// <summary>How far a bush this tall (metres, as placed) is drawn.</summary>
		public float BushDrawDistance(float height) => DetailScatterMath.BushDrawDistance(height, BushDrawMetresPerMetre, BushDrawMin, BushDrawMax);
	}
}
