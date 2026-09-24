using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Water
{
	/// <summary>
	/// How deep the water is over every part of the scene, as a texture the shader can read.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the waves need this at all.</b> Everything a shoreline does is a function of depth.
	/// A wave entering shallow water slows, shortens and grows until it is too tall for the water
	/// under it and breaks; the surf line sits where that happens; the swash runs up the sand until
	/// it runs out of height. None of that can be worked out from the camera's depth buffer,
	/// because the answer is needed in the VERTEX stage — the wave has to be a different shape
	/// there — and a vertex knows nothing about what is behind it on screen.
	/// </para>
	/// <para>
	/// <b>Built at load, not baked to an asset.</b> Sampling a scene's terrains at 512 x 512 takes
	/// a few milliseconds, and the result depends on nothing but the terrain — so an asset would be
	/// a second copy of something already in the scene, with all the staleness that implies. It
	/// also means a designer raising the beach sees the surf move immediately.
	/// </para>
	/// </remarks>
	[ExecuteAlways]
	[AddComponentMenu("FishMMO/Water/Water Shore Field")]
	[RequireComponent(typeof(WaterSurface))]
	public sealed class WaterShoreField : MonoBehaviour
	{
		private static readonly int FieldId = Shader.PropertyToID("_FishWaterShore");
		private static readonly int RectId = Shader.PropertyToID("_FishWaterShoreRect");
		private static readonly int RangeId = Shader.PropertyToID("_FishWaterShoreRange");

		[Tooltip("Samples across the scene. 512 over a 2 km scene is about four metres a sample.")]
		[Range(64, 2048)] public int Resolution = 512;

		[Tooltip("Rebuild when the terrain changes. Off is one build at load, which is all a shipped scene needs.")]
		public bool RebuildOnValidate = true;

		/// <summary>Depth recorded where the scene has no terrain at all, in metres.</summary>
		/// <remarks>
		/// Deep enough that no wave shoals against it, small enough to stay well inside a half.
		/// </remarks>
		private const float OpenWaterDepth = 400f;

		private Texture2D field;
		private Rect area;
		private float deepest;
		private WaterSurface surface;

		/// <summary>The world-space rectangle the field covers.</summary>
		public Rect Area => area;

		private void OnEnable()
		{
			surface = GetComponent<WaterSurface>();
			Build();
		}

		private void OnDisable()
		{
			// Leave the globals pointing at nothing, or the next scene reads this scene's beach.
			Shader.SetGlobalVector(RectId, Vector4.zero);
			if (field != null)
			{
				if (Application.isPlaying)
				{
					Destroy(field);
				}
				else
				{
					DestroyImmediate(field);
				}
				field = null;
			}
		}

		private void OnValidate()
		{
			if (RebuildOnValidate && isActiveAndEnabled)
			{
				Build();
			}
		}

		/// <summary>Reads the scene's terrains and rebuilds the depth field.</summary>
		public void Build()
		{
			var terrains = new List<Terrain>();
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				if (terrain != null && terrain.terrainData != null
					&& terrain.gameObject.scene == gameObject.scene)
				{
					terrains.Add(terrain);
				}
			}
			if (terrains.Count == 0)
			{
				// Open ocean with no ground in the scene: no shore, no shoaling, no surf.
				Shader.SetGlobalVector(RectId, Vector4.zero);
				return;
			}

			float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
			for (int i = 0; i < terrains.Count; i++)
			{
				Vector3 origin = terrains[i].GetPosition();
				Vector3 size = terrains[i].terrainData.size;
				minX = Mathf.Min(minX, origin.x);
				minZ = Mathf.Min(minZ, origin.z);
				maxX = Mathf.Max(maxX, origin.x + size.x);
				maxZ = Mathf.Max(maxZ, origin.z + size.z);
			}

			/* Padded by a tile's worth, so the field's own edge is not the shoreline. Sampled
			 * exactly at the boundary, the bilinear filter would blend the last row of real ground
			 * with whatever the clamp returns and draw a false beach along the scene's edge. */
			float pad = Mathf.Max(32f, (maxX - minX) * 0.05f);
			area = Rect.MinMaxRect(minX - pad, minZ - pad, maxX + pad, maxZ + pad);

			int resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Resolution), 64, 2048);
			if (field == null || field.width != resolution)
			{
				if (field != null)
				{
					DestroyImmediate(field);
				}
				field = new Texture2D(resolution, resolution, TextureFormat.RHalf, false, true)
				{
					name = "Shore depth",
					wrapMode = TextureWrapMode.Clamp,
					filterMode = FilterMode.Bilinear,
					hideFlags = HideFlags.HideAndDontSave,
				};
			}

			float sea = surface != null ? surface.MeanSeaLevel : transform.position.y;
			var pixels = new Color[resolution * resolution];
			deepest = 0f;

			for (int y = 0; y < resolution; y++)
			{
				float wz = Mathf.Lerp(area.yMin, area.yMax, (y + 0.5f) / resolution);
				for (int x = 0; x < resolution; x++)
				{
					float wx = Mathf.Lerp(area.xMin, area.xMax, (x + 0.5f) / resolution);
					float ground = Ground(terrains, wx, wz);
					// Positive in the water, negative on dry land. One metre is one unit. Off every
					// terrain is open water, not a trench a mile deep.
					float depth = ground > float.MinValue ? sea - ground : OpenWaterDepth;
					deepest = Mathf.Max(deepest, depth);
					pixels[y * resolution + x] = new Color(depth, 0f, 0f, 0f);
				}
			}

			field.SetPixels(pixels);
			field.Apply(false, false);

			Shader.SetGlobalTexture(FieldId, field);
			Shader.SetGlobalVector(RectId, new Vector4(area.xMin, area.yMin, area.width, area.height));
			Shader.SetGlobalFloat(RangeId, Mathf.Max(1f, deepest));
		}

		/// <summary>The highest ground at a world XZ across every terrain in the scene.</summary>
		/// <remarks>
		/// The highest, not the first: stitched tiles overlap along their seams by a sample, and
		/// taking whichever was listed first put a one-sample trench down every join, which the
		/// surf then broke along.
		/// </remarks>
		private static float Ground(List<Terrain> terrains, float worldX, float worldZ)
		{
			float best = float.MinValue;
			for (int i = 0; i < terrains.Count; i++)
			{
				Terrain terrain = terrains[i];
				Vector3 origin = terrain.GetPosition();
				Vector3 size = terrain.terrainData.size;
				float u = (worldX - origin.x) / size.x;
				float v = (worldZ - origin.z) / size.z;
				if (u < 0f || u > 1f || v < 0f || v > 1f)
				{
					continue;
				}
				best = Mathf.Max(best, origin.y + terrain.terrainData.GetInterpolatedHeight(u, v));
			}
			// Off every terrain: open water, as deep as the scene gets.
			return best > float.MinValue ? best : float.MinValue;
		}
	}
}
