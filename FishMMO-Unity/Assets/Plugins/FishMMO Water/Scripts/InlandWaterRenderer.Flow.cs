using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
#if !UNITY_SERVER
using UnityEngine.ResourceManagement.AsyncOperations;
#endif

namespace FishMMO.Water
{
	/// <summary>
	/// The solved flow (<see cref="SceneRiverFlow"/>): loaded from the scene's biome map on the client and handed to each
	/// river's surface, which then rides the solved current (eddies behind its boulders, slack water at its banks) in
	/// place of the blended one.
	/// </summary>
	public sealed partial class InlandWaterRenderer
	{
		private static readonly int FlowFieldId = Shader.PropertyToID("_RiverFlowField");
		private static readonly int FlowInfoId = Shader.PropertyToID("_RiverFlowInfo");

		private readonly Dictionary<int, MeshRenderer> riverRenderers = new Dictionary<int, MeshRenderer>();
		private SceneRiverFlow flow;
		private SceneBiomeMap flowMap;
		private bool flowLoading;

		/* Each lake, river, curtain and spray draws with a copy of its material of its own, carrying its numbers in the
		 * shader's UnityPerMaterial (2026-10-28). They rode property blocks, and a renderer with a block is drawn outside the
		 * SRP Batcher: URP set up each river's whole shader state again (a SetPass call per river); copies of one shader
		 * variant batch, uploading only each one's constants. Same draws, same order (sortingOrder), same stencil. */
		private readonly DrawMaterials surfaceCopies = new DrawMaterials("inland water");
		private readonly DrawMaterials curtainCopies = new DrawMaterials("waterfall");
		private readonly DrawMaterials sprayCopies = new DrawMaterials("waterfall spray");
		/// <summary>Each lake's and river's slot among <see cref="surfaceCopies"/>, in the order they were built.</summary>
		private readonly Dictionary<MeshRenderer, int> surfaceSlots = new Dictionary<MeshRenderer, int>();
		/// <summary>The materials the copies were last made from (<see cref="SourcesChanged"/>).</summary>
		private Material copiedSurface, copiedCurtain, copiedSpray;
#if UNITY_EDITOR
		private int copiedSurfaceEdits, copiedCurtainEdits, copiedSprayEdits;
#endif

		/// <summary>The solved flow the rivers ride, once loaded; null without one.</summary>
		public SceneRiverFlow Flow => flow;

		/// <summary>Hands the rivers a solved flow directly (tools and probes, which have no biome map to load it from).</summary>
		public void SetFlow(SceneRiverFlow solved)
		{
			flow = solved;
			// The falls take their water's spread across the lip from the solved flow: built before it arrived, they
			// are built again with it (Rebuild ends by applying the flow to the rivers).
			if (solved != null && built.Count > 0)
			{
				Rebuild();
				return;
			}
			ApplyFlow();
		}

		/// <summary>Loads the scene's solved flow from its biome map: on demand, on the client only.</summary>
		private void LoadFlow()
		{
#if !UNITY_SERVER
			if (flow != null || flowLoading)
			{
				return;
			}
			SceneBiomeMap map = null;
			foreach (GameObject root in gameObject.scene.GetRootGameObjects())
			{
				WorldSceneSettings settings = root.GetComponentInChildren<WorldSceneSettings>(true);
				if (settings != null)
				{
					map = settings.BiomeMap;
					break;
				}
			}
			if (map == null || map.RiverFlow == null)
			{
				return;
			}
#if UNITY_EDITOR
			if (!Application.isPlaying)
			{
				SetFlow(map.RiverFlow.editorAsset);
				return;
			}
#endif
			if (!map.RiverFlow.RuntimeKeyIsValid())
			{
				return;
			}
			flowLoading = true;
			flowMap = map;
			map.RiverFlow.LoadAssetAsync().Completed += handle =>
			{
				flowLoading = false;
				if (this == null)
				{
					return;
				}
				if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
				{
					SetFlow(handle.Result);
				}
				else
				{
					// No flow after all: the falls Start held back for it are built without it.
					Rebuild();
				}
			};
#endif
		}

		private void ReleaseFlow()
		{
#if !UNITY_SERVER
			if (flowMap != null && flowMap.RiverFlow != null && flowMap.RiverFlow.IsValid())
			{
				flowMap.RiverFlow.ReleaseAsset();
			}
			flowMap = null;
#endif
		}

		/// <summary>
		/// Gives each river its field: the texture, and its length in metres (0 turns the solved flow off); and each river and
		/// lake its water's look by its biome (Looks), where that is not the material's own, and each fall the look of the water
		/// it pours from. Every number of every surface is set, each on the surface's own copy of the material.
		/// </summary>
		private void ApplyFlow()
		{
			RememberSources();
			foreach (KeyValuePair<int, MeshRenderer> pair in riverRenderers)
			{
				Material own = SurfaceCopy(pair.Value);
				if (own == null)
				{
					continue;
				}
				SceneRiverFlow.River field = flow != null ? flow.Find(pair.Key) : null;
				if (field != null && field.Field != null)
				{
					own.SetTexture(FlowFieldId, field.Field);
					own.SetVector(FlowInfoId, new Vector4(field.Length * field.AlongMetres, 1f, 0f, 0f));
				}
				// The foam the falls above its water beat up, carried downstream (FishInlandWater.hlsl FallTrail).
				ApplyRiverFalls(pair.Key, own);
				ApplyRiverLook(pair.Key, own);
			}
			ApplyLakeLooks();
			// And each fall's sheet and spray, the look of the water it pours from (Looks, 2026-10-18).
			ApplyFallLooks();
		}

		/// <summary>
		/// The copy of <see cref="Material"/> in a lake's or river's slot, given to its renderer, with every number of the
		/// surface's own at "none" (no solved flow, no falls, the material's look) for the caller to set; null for a renderer
		/// gone or never built. A slot's copy is reused build after build, so what an earlier surface set on it is cleared here.
		/// </summary>
		private Material SurfaceCopy(MeshRenderer renderer)
		{
			if (renderer == null || !surfaceSlots.TryGetValue(renderer, out int slot))
			{
				return null;
			}
			Material own = surfaceCopies.For(Material, slot);
			if (own == null)
			{
				return null;
			}
			own.SetTexture(FlowFieldId, null);
			own.SetVector(FlowInfoId, Vector4.zero);
			for (int i = 0; i < RiverFallIds.Length; i++)
			{
				own.SetVector(RiverFallIds[i], Vector4.zero);
			}
			own.SetFloat(RiverFallCountId, 0f);
			own.SetFloat(LookOnId, 0f);
			renderer.sharedMaterial = own;
			return own;
		}

		/// <summary>Notes the materials the copies are being made from, to see an edit or a swap later (<see cref="SourcesChanged"/>).</summary>
		private void RememberSources()
		{
			copiedSurface = Material;
			copiedCurtain = FallMaterial;
			copiedSpray = SprayMaterial;
#if UNITY_EDITOR
			copiedSurfaceEdits = Edits(Material);
			copiedCurtainEdits = Edits(FallMaterial);
			copiedSprayEdits = Edits(SprayMaterial);
#endif
		}

		/// <summary>
		/// Whether a material the copies were made from was swapped, or (in the editor) edited, since: its surfaces then take
		/// their copies afresh. A copy is made once; the material's own values do not reach it by themselves, as they reached
		/// a renderer that only had a block over it.
		/// </summary>
		private bool SourcesChanged()
		{
			if (!ReferenceEquals(copiedSurface, Material) || !ReferenceEquals(copiedCurtain, FallMaterial) || !ReferenceEquals(copiedSpray, SprayMaterial))
			{
				return true;
			}
#if UNITY_EDITOR
			return copiedSurfaceEdits != Edits(Material) || copiedCurtainEdits != Edits(FallMaterial) || copiedSprayEdits != Edits(SprayMaterial);
#else
			return false;
#endif
		}

#if UNITY_EDITOR
		private static int Edits(Material material) => material != null ? UnityEditor.EditorUtility.GetDirtyCount(material) : 0;
#endif

		/// <summary>Destroys every copy (the renderers using them are gone, or going).</summary>
		private void DisposeCopies()
		{
			surfaceCopies.Dispose();
			curtainCopies.Dispose();
			sprayCopies.Dispose();
			copiedSurface = copiedCurtain = copiedSpray = null;
		}
	}
}
