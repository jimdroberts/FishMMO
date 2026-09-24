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
		private static readonly int TexelId = Shader.PropertyToID("_FishWaterShoreTexel");

		/// <summary>
		/// How many metres one texel should cover.
		/// </summary>
		/// <remarks>
		/// <b>This is the number that decides whether a shoreline looks like a shoreline.</b> The
		/// wave amplitude is clamped by the depth under it, so the depth field's texel size is
		/// also the granularity of the WAVE HEIGHT — at 5.6 m a texel, measured, the sea surface
		/// steps in 5.6 m blocks and the waterline inherits every one of them as a stair. One
		/// metre puts the steps below the size of the swash itself.
		/// </remarks>
		[Tooltip("Metres per texel to aim for. 1 m keeps the waterline smooth; the resolution follows from the scene size.")]
		[Range(0.25f, 16f)] public float TargetTexelMetres = 1f;

		/// <summary>
		/// The most texels a side, whatever the scene size.
		/// </summary>
		/// <remarks>
		/// 2048² of R16 is 8 MB, which is affordable. A 20 km scene still lands at about 10 m a
		/// texel at that cap, so the very largest scenes keep a coarse field — the answer there is
		/// a field that follows the camera, not a bigger one.
		/// </remarks>
		[Tooltip("Upper limit on resolution. 2048 is 8 MB.")]
		[Range(64, 4096)] public int MaximumResolution = 2048;

		/// <summary>Metres a texel actually covers, once the scene has been measured.</summary>
		public float TexelMetres { get; private set; }

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

			/* Resolution FOLLOWS the scene, rather than the scene being squeezed into a fixed
			 * resolution: a small bay and a twenty-kilometre coast want very different grids, and
			 * the thing that has to stay constant is the metres a texel covers. */
			float span = Mathf.Max(area.width, area.height);
			int wanted = Mathf.CeilToInt(span / Mathf.Max(0.05f, TargetTexelMetres));
			int resolution = Mathf.Clamp(Mathf.NextPowerOfTwo(wanted), 64,
				Mathf.Clamp(Mathf.NextPowerOfTwo(MaximumResolution), 64, 4096));
			TexelMetres = span / resolution;
			if (field == null || field.width != resolution)
			{
				if (field != null)
				{
					DestroyImmediate(field);
				}
				field = new Texture2D(resolution, resolution, TextureFormat.RGHalf, false, true)
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

			/* The SIGNED DISTANCE to the waterline, in metres, in the green channel.
			 *
			 * Depth alone cannot drive a shoreline. Everything a beach does is organised by how
			 * far a point is from the water's edge: waves arrive as fronts parallel to it, the
			 * swash runs up a distance and drains back, foam is left in a band behind it. Depth is
			 * a poor stand-in because it depends on the slope — the same 0.5 m of depth is two
			 * metres from the edge on a steep beach and sixty on a flat one.
			 *
			 * A distance field also interpolates cleanly where depth does not: depth has a sharp
			 * zero crossing, so bilinear filtering of a coarse grid gives a blocky contour, while
			 * distance is smooth through the edge and stays smooth at any resolution.
			 */
			Distance(pixels, resolution, area.width / resolution);

			field.SetPixels(pixels);
			field.Apply(false, false);

			Shader.SetGlobalTexture(FieldId, field);
			Shader.SetGlobalVector(RectId, new Vector4(area.xMin, area.yMin, area.width, area.height));
			Shader.SetGlobalFloat(RangeId, Mathf.Max(1f, deepest));
			Shader.SetGlobalFloat(TexelId, TexelMetres);
		}

		/// <summary>
		/// Fills the green channel with the signed distance, in metres, to the water's edge.
		/// </summary>
		/// <remarks>
		/// A two-pass chamfer transform: one sweep down-right, one up-left, each taking the best
		/// of its neighbours plus the step between them. It is exact to a few per cent for a cost
		/// of two passes, where a true Euclidean transform needs several — and a few per cent of a
		/// metre is far below anything the surf does with it.
		/// </remarks>
		private static void Distance(Color[] pixels, int resolution, float metresPerTexel)
		{
			const float Straight = 1f;
			// The diagonal step of a unit grid; using 1 here makes the field visibly square.
			const float Diagonal = 1.41421356f;
			float far = resolution * 4f;

			// Seed: zero on the texels that straddle the waterline, far everywhere else.
			var distance = new float[pixels.Length];
			for (int y = 0; y < resolution; y++)
			{
				for (int x = 0; x < resolution; x++)
				{
					int index = y * resolution + x;
					bool wet = pixels[index].r > 0f;
					bool edge = false;
					if (x > 0 && (pixels[index - 1].r > 0f) != wet) edge = true;
					if (x < resolution - 1 && (pixels[index + 1].r > 0f) != wet) edge = true;
					if (y > 0 && (pixels[index - resolution].r > 0f) != wet) edge = true;
					if (y < resolution - 1 && (pixels[index + resolution].r > 0f) != wet) edge = true;
					distance[index] = edge ? 0f : far;
				}
			}

			for (int y = 0; y < resolution; y++)
			{
				for (int x = 0; x < resolution; x++)
				{
					int i = y * resolution + x;
					float best = distance[i];
					if (x > 0) best = Mathf.Min(best, distance[i - 1] + Straight);
					if (y > 0) best = Mathf.Min(best, distance[i - resolution] + Straight);
					if (x > 0 && y > 0) best = Mathf.Min(best, distance[i - resolution - 1] + Diagonal);
					if (x < resolution - 1 && y > 0) best = Mathf.Min(best, distance[i - resolution + 1] + Diagonal);
					distance[i] = best;
				}
			}
			for (int y = resolution - 1; y >= 0; y--)
			{
				for (int x = resolution - 1; x >= 0; x--)
				{
					int i = y * resolution + x;
					float best = distance[i];
					if (x < resolution - 1) best = Mathf.Min(best, distance[i + 1] + Straight);
					if (y < resolution - 1) best = Mathf.Min(best, distance[i + resolution] + Straight);
					if (x < resolution - 1 && y < resolution - 1) best = Mathf.Min(best, distance[i + resolution + 1] + Diagonal);
					if (x > 0 && y < resolution - 1) best = Mathf.Min(best, distance[i + resolution - 1] + Diagonal);
					distance[i] = best;
				}
			}

			// Negative on dry land, positive in the water, so one number says both which side of
			// the edge a point is on and how far.
			for (int i = 0; i < pixels.Length; i++)
			{
				float metres = distance[i] * metresPerTexel;
				pixels[i].g = pixels[i].r > 0f ? metres : -metres;
			}
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
