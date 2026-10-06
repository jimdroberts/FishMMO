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
		private MaterialPropertyBlock flowBlock;

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
				if (this != null && handle.Status == AsyncOperationStatus.Succeeded)
				{
					SetFlow(handle.Result);
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

		/// <summary>Gives each river its field: the texture, and its length in metres (0 turns the solved flow off).</summary>
		private void ApplyFlow()
		{
			flowBlock ??= new MaterialPropertyBlock();
			foreach (KeyValuePair<int, MeshRenderer> pair in riverRenderers)
			{
				if (pair.Value == null)
				{
					continue;
				}
				SceneRiverFlow.River field = flow != null ? flow.Find(pair.Key) : null;
				if (field == null || field.Field == null)
				{
					pair.Value.SetPropertyBlock(null);
					continue;
				}
				flowBlock.Clear();
				flowBlock.SetTexture(FlowFieldId, field.Field);
				flowBlock.SetVector(FlowInfoId, new Vector4(field.Length * field.AlongMetres, 1f, 0f, 0f));
				pair.Value.SetPropertyBlock(flowBlock);
			}
		}
	}
}
