using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// Sets <c>_FishVegetationFade</c>, the distances over which the vegetation shader dissolves terrain
	/// details and trees, from the loaded terrains' own draw distances — so plants fade out before the
	/// terrain drops them instead of popping.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why details popped "in square chunks" (Jim, 2026-10-02).</b> Unity draws terrain details by
	/// patch — <c>detailResolutionPerPatch</c> cells a side, 32 m at the generator's old 32 × 1 m — and
	/// culls a whole patch at <c>detailObjectDistance</c>. Each patch therefore appears and vanishes at
	/// once, a square of grass at a time. Trees are dropped one by one at <c>treeDistance</c>, whatever
	/// their LODGroup says. Neither has a fade of its own for mesh prototypes.
	/// </para>
	/// <para>
	/// <b>The bands.</b> A patch is culled by its distance as a whole, so its nearest plants can be
	/// a patch diagonal closer than the cull distance when it goes (and when it comes back): the detail
	/// fade must be over by <c>distance − patch·√2</c> (<see cref="DetailBand"/>), and it runs over the
	/// <see cref="DetailBandMetres"/> before that. With the generator's 120 m and 16 m patches that is
	/// 72.4 → 97.4 m. Trees fade over <see cref="TreeBand"/>: the last tenth of the tree distance (at
	/// least <see cref="TreeBandMinMetres"/>), ending 3% inside it — 1305 → 1455 m at 1.5 km.
	/// </para>
	/// <para>
	/// <b>Followed, not baked.</b> The distances are read from the terrains (and the quality settings'
	/// terrain overrides, which replace them when set) whenever they could have changed — a scene
	/// loaded, a terrain enabled, the quality level changed — by checking a cheap signature each frame
	/// before rendering, so a quality setting that shortens the draw distance moves the fade with it.
	/// With no terrain loaded the global is zero, which the shader reads as "no fade". It runs in the
	/// editor too, so the scene view shows what the game will.
	/// </para>
	/// </remarks>
	public static class VegetationDistanceFade
	{
		/// <summary>The global the vegetation shader reads: x, y detail start and end; z, w tree start and end (metres).</summary>
		public static readonly int FadeId = Shader.PropertyToID("_FishVegetationFade");

		/// <summary>Metres over which a detail dissolves before its patch could be culled.</summary>
		public const float DetailBandMetres = 25f;

		/// <summary>The shortest tree fade, metres.</summary>
		public const float TreeBandMinMetres = 50f;

		/// <summary>Share of the tree distance the tree fade spans.</summary>
		public const float TreeBandShare = 0.1f;

		/// <summary>How far inside the tree distance the tree fade is complete, as a share of it.</summary>
		public const float TreeEndMargin = 0.03f;

		/// <summary>Unity clamps a terrain's detail distance to this.</summary>
		public const float MaxDetailDistance = 250f;

		private static readonly List<Terrain> terrains = new List<Terrain>();
		private static bool hooked;
		private static int signature;
		private static Vector4 current;

		/// <summary>
		/// The detail fade (start, end) in metres for a detail draw distance and a patch size: complete
		/// a patch diagonal inside the distance, so a patch is invisible before Unity can cull it, over
		/// <paramref name="band"/> metres — shortened, never inverted, when the distance is short.
		/// (0, 0) — no fade — for a terrain that draws no details.
		/// </summary>
		public static Vector2 DetailBand(float detailDistance, float patchMetres, float band = DetailBandMetres)
		{
			if (detailDistance <= 0f)
			{
				return Vector2.zero;
			}
			float end = Mathf.Max(detailDistance - Mathf.Max(0f, patchMetres) * 1.41421356f, detailDistance * 0.5f);
			float width = Mathf.Min(band, end * 0.4f);
			return new Vector2(end - width, end);
		}

		/// <summary>The tree fade (start, end) in metres for a tree distance; (0, 0) when trees are not drawn.</summary>
		public static Vector2 TreeBand(float treeDistance)
		{
			if (treeDistance <= 0f)
			{
				return Vector2.zero;
			}
			float end = treeDistance * (1f - TreeEndMargin);
			float width = Mathf.Min(Mathf.Max(TreeBandMinMetres, treeDistance * TreeBandShare), end * 0.5f);
			return new Vector2(end - width, end);
		}

		/// <summary>One terrain's detail patch side in metres: cells per patch × the cell size.</summary>
		public static float PatchMetres(float terrainWidth, int detailResolution, int detailResolutionPerPatch)
		{
			return detailResolution > 0 ? terrainWidth / detailResolution * detailResolutionPerPatch : 0f;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked)
			{
				return;
			}
			hooked = true;
			signature = 0;
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
		}

#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
		private static void HookEditor() => Hook();
#endif

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			Refresh(false);
		}

		/// <summary>Recomputes the fade when the terrains or quality settings changed (or always, with <paramref name="force"/>).</summary>
		public static void Refresh(bool force)
		{
			Terrain.GetActiveTerrains(terrains);
			int sig = QualitySettings.GetQualityLevel() * 31 + (int)QualitySettings.terrainQualityOverrides;
			for (int i = 0; i < terrains.Count; i++)
			{
				Terrain t = terrains[i];
				if (t == null)
				{
					continue;
				}
				sig = sig * 397 ^ t.GetInstanceID();
				// The instanced detail renderer zeroes it in play mode and keeps it (TerrainDetailInstancing).
				sig = sig * 397 ^ TerrainDetailInstancing.DetailDistanceOf(t).GetHashCode();
				// The instanced tree renderer zeroes a terrain's own distance in play mode and keeps it (TerrainTreeInstancing).
				sig = sig * 397 ^ TerrainTreeInstancing.TreeDistanceOf(t).GetHashCode();
				sig = sig * 397 ^ (t.drawTreesAndFoliage ? 1 : 0);
			}
			sig = sig * 397 ^ QualitySettings.terrainDetailDistance.GetHashCode();
			sig = sig * 397 ^ QualitySettings.terrainTreeDistance.GetHashCode();
			if (!force && sig == signature)
			{
				return;
			}
			signature = sig;

			Vector4 fade = Compute(terrains);
			if (force || fade != current)
			{
				current = fade;
				Shader.SetGlobalVector(FadeId, fade);
			}
		}

		/// <summary>
		/// The fade for a set of terrains: the earliest detail and tree band among those that draw
		/// foliage (a band that ends too early only fades sooner; one that ends too late pops).
		/// </summary>
		private static Vector4 Compute(List<Terrain> active)
		{
			TerrainQualityOverrides overrides = QualitySettings.terrainQualityOverrides;
			bool detailOverride = (overrides & TerrainQualityOverrides.DetailDistance) != 0;
			bool treeOverride = (overrides & TerrainQualityOverrides.TreeDistance) != 0;
			Vector2 detail = Vector2.zero, tree = Vector2.zero;
			bool anyDetail = false, anyTree = false;
			foreach (Terrain t in active)
			{
				if (t == null || !t.drawTreesAndFoliage || t.terrainData == null)
				{
					continue;
				}
				TerrainData data = t.terrainData;
				// Unity's own drawing clamps at MaxDetailDistance; the instanced renderer draws to the player's
				// grass distance, which may be further (TerrainDetailInstancing, ClientGrassSettings).
				float detailDistance = detailOverride ? Mathf.Min(QualitySettings.terrainDetailDistance, MaxDetailDistance)
					: TerrainDetailInstancing.IsDriving(t) ? TerrainDetailInstancing.DetailDistanceOf(t)
					: Mathf.Min(t.detailObjectDistance, MaxDetailDistance);
				if (data.detailPrototypes.Length > 0 && detailDistance > 0f)
				{
					Vector2 band = DetailBand(detailDistance, PatchMetres(data.size.x, data.detailResolution, data.detailResolutionPerPatch));
					if (!anyDetail || band.y < detail.y)
					{
						detail = band;
						anyDetail = true;
					}
				}
				float treeDistance = treeOverride ? QualitySettings.terrainTreeDistance : TerrainTreeInstancing.TreeDistanceOf(t);
				if (data.treePrototypes.Length > 0 && treeDistance > 0f)
				{
					Vector2 band = TreeBand(treeDistance);
					if (!anyTree || band.y < tree.y)
					{
						tree = band;
						anyTree = true;
					}
				}
			}
			return new Vector4(detail.x, detail.y, tree.x, tree.y);
		}
	}
}
