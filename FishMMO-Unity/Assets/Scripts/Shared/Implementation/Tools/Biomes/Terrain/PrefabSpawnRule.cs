using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>Which Unity terrain channel a spawn rule writes to.</summary>
	public enum PrefabSpawnChannel
	{
		DetailLayer = 0,
		TreeInstance = 1,
	}

	/// <summary>
	/// Where and how densely a set of prefabs is scattered wherever its texture layer dominates.
	/// Field names match the WorldEditor asset layout so exported biome templates load unchanged.
	/// </summary>
	[Serializable]
	public class PrefabSpawnRule
	{
		[SerializeField]
		private string stableGuid = Guid.NewGuid().ToString("N");
		[Tooltip("Enable or disable this spawn rule without removing configuration.")]
		public bool enableSpawning = true;

		[Header("Identification")]
		public string ruleName = "Untitled Rule";
		[Tooltip("List of prefabs to spawn. One will be randomly selected per spawn location for variation.")]
		public GameObject[] prefabs = new GameObject[0];

		[Header("Spawn Channel")]
		[Tooltip("Choose whether this rule writes to Unity detail layers or tree instances.")]
		public PrefabSpawnChannel spawnChannel = PrefabSpawnChannel.DetailLayer;

		[Header("Density & Limits")]
		[Tooltip("Expected prefab count per 100 square meters of dominant texture.")]
		[Range(0f, 50f)] public float densityPer100m2 = 0.5f;
		[Tooltip("Absolute safety cap per terrain chunk. 0 = unlimited (not recommended).")]
		[Min(0)] public int maxPerChunk = 10000;
		[Tooltip("Minimum spacing in meters between spawned prefabs.")]
		[Range(0f, 50f)] public float minSpacing = 2f;

		[Header("Texture Weight")]
		[Tooltip("Require the target texture weight to exceed this threshold in the alphamap to consider spawning.")]
		[Range(0f, 1f)] public float minTextureWeight = 0.45f;
		[Tooltip("Optional multiplier applied to the computed spawn probability.")]
		[Range(0.1f, 5f)] public float spawnProbabilityMultiplier = 1f;

		[Header("Height Constraint")]
		public bool useHeightConstraint = false;
		public MinMaxRange heightRange = new MinMaxRange(0f, 1f);
		[Range(0.001f, 0.2f)] public float heightFalloff = 0.05f;

		[Header("Slope Constraint")]
		public bool useSlopeConstraint = false;
		public MinMaxRange slopeRange = new MinMaxRange(0f, 45f);
		[Range(1f, 20f)] public float slopeFalloff = 5f;

		[Header("Randomization")]
		[Tooltip("Enable independent width and height scaling for more natural tree variation.")]
		public bool useNonUniformScaling = false;
		[Tooltip("Random scale range applied uniformly to width and height (when non-uniform scaling is disabled).")]
		public Vector2 uniformScaleRange = new Vector2(1f, 1f);
		[Tooltip("Random scale range for tree width (only used when non-uniform scaling is enabled).")]
		public Vector2 widthScaleRange = new Vector2(0.8f, 1.2f);
		[Tooltip("Random scale range for tree height (only used when non-uniform scaling is enabled).")]
		public Vector2 heightScaleRange = new Vector2(0.8f, 1.2f);
		public Vector2 yRotationRange = new Vector2(0f, 360f);
		public bool alignToTerrainNormal = true;
		[Tooltip("Seed offset applied on top of the global prefab seed to keep results deterministic per rule.")]
		public int seedOffset = 0;

		[Header("Detail Rendering")]
		[Tooltip("Controls how widely detail prototypes spread noise across the terrain patch.")]
		[Range(0.05f, 5f)] public float detailNoiseSpread = 0.5f;
		[Tooltip("Tint applied when the detail patch is considered healthy.")]
		public Color detailHealthyColor = new Color(0.9f, 0.95f, 0.9f, 1f);
		[Tooltip("Tint applied when the detail patch is considered dry.")]
		public Color detailDryColor = new Color(0.75f, 0.7f, 0.55f, 1f);
		[Tooltip("How many instances are written into the detail map for every accepted spawn sample.")]
		[Range(64, 255)] public int detailInstancesPerSpawn = 64;

		public string StableGuid => stableGuid;

		public bool HasValidPrefabs()
		{
			if (prefabs == null)
			{
				return false;
			}
			for (int i = 0; i < prefabs.Length; i++)
			{
				if (prefabs[i] != null)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>One of the rule's prefabs, drawn with the given RNG; null when it has none.</summary>
		public GameObject GetRandomPrefab(DeterministicRNG rng)
		{
			if (prefabs == null || prefabs.Length == 0)
			{
				return null;
			}
			var valid = new List<GameObject>(prefabs.Length);
			for (int i = 0; i < prefabs.Length; i++)
			{
				if (prefabs[i] != null)
				{
					valid.Add(prefabs[i]);
				}
			}
			if (valid.Count == 0)
			{
				return null;
			}
			return valid.Count == 1 ? valid[0] : valid[rng.Next(valid.Count)];
		}

		public void EnsureStableGuid()
		{
			if (string.IsNullOrEmpty(stableGuid))
			{
				stableGuid = Guid.NewGuid().ToString("N");
			}
		}
	}
}
