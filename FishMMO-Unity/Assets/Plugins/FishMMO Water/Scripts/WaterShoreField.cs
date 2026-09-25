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

		/// <summary>Texels along each side of the field; 0 before it is built.</summary>
		public int Resolution => field != null ? field.width : 0;

		/// <summary>
		/// The field at a world point, on the CPU: metres of water over the ground at MEAN sea level
		/// (negative on land), and signed metres to the mean waterline (positive at sea). False off
		/// the field or before it is built.
		/// </summary>
		/// <remarks>
		/// Read back from the texture's own CPU copy, which is kept — it is built with
		/// <c>Apply(false, false)</c> — so buoyancy can ask the same field the shader shoals the sea
		/// with, at no memory cost beyond what already exists.
		/// </remarks>
		public bool TrySample(Vector2 xz, out float depth, out float edgeDistance)
		{
			depth = OpenWaterDepth;
			edgeDistance = 1000f;
			if (field == null || area.width < 1f || area.height < 1f)
			{
				return false;
			}
			float u = (xz.x - area.xMin) / area.width;
			float v = (xz.y - area.yMin) / area.height;
			if (u < 0f || u > 1f || v < 0f || v > 1f)
			{
				return false;
			}
			Color sample = field.GetPixelBilinear(u, v);
			depth = sample.r;
			edgeDistance = sample.g;
			return true;
		}

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
			if (!RebuildOnValidate || !isActiveAndEnabled)
			{
				return;
			}
#if UNITY_EDITOR
			/* Deferred to the next editor tick, and coalesced.
			 *
			 * A build samples every terrain up to 2048 x 2048 times and runs a two-pass distance
			 * transform over the result. OnValidate fires on every keystroke in the inspector and
			 * on every domain reload — where OnEnable then builds again straight afterwards — so
			 * building synchronously here froze the editor for each of those, twice per recompile.
			 * Unsubscribing first means ten edits in one frame cost one build. */
			UnityEditor.EditorApplication.delayCall -= DeferredBuild;
			UnityEditor.EditorApplication.delayCall += DeferredBuild;
#else
			Build();
#endif
		}

#if UNITY_EDITOR
		private void DeferredBuild()
		{
			// The component can be gone by the time the editor gets round to this.
			if (this != null && isActiveAndEnabled)
			{
				Build();
			}
		}
#endif

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
			/* SQUARE, padding the short side with more open water. The texture is square, so a
			 * rectangular scene gave rectangular texels — 3.49 by 2.96 m on Cov Viaduct — while the
			 * distance transform counted steps as if they were square, and every distance measured
			 * north-south came out an eighth too long. */
			float side = Mathf.Max(area.width, area.height);
			area = new Rect(area.center.x - side * 0.5f, area.center.y - side * 0.5f, side, side);

			/* Resolution FOLLOWS the scene, rather than the scene being squeezed into a fixed
			 * resolution: a small bay and a twenty-kilometre coast want very different grids, and
			 * the thing that has to stay constant is the metres a texel covers. */
			float span = Mathf.Max(area.width, area.height);
			int wanted = Mathf.CeilToInt(span / Mathf.Max(0.05f, TargetTexelMetres));
			int resolution = Mathf.Clamp(Mathf.NextPowerOfTwo(wanted), 64,
				Mathf.Clamp(Mathf.NextPowerOfTwo(MaximumResolution), 64, 4096));
			TexelMetres = span / resolution;
			if (field == null)
			{
				field = new Texture2D(resolution, resolution, TextureFormat.RGHalf, false, true)
				{
					name = "Shore depth",
					wrapMode = TextureWrapMode.Clamp,
					filterMode = FilterMode.Bilinear,
					hideFlags = HideFlags.HideAndDontSave,
				};
			}
			else if (field.width != resolution || field.format != TextureFormat.RGHalf)
			{
				/* Resized in place, never destroyed and recreated: this can run from OnValidate,
				 * where Unity refuses DestroyImmediate — the same error the ocean mesh was logging
				 * on every inspector edit, waiting here for the first time the resolution moved. */
				field.Reinitialize(resolution, resolution, TextureFormat.RGHalf, false);
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
			Distance(pixels, resolution, TexelMetres);

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
		/// <para>
		/// <b>Exact, not a chamfer.</b> This was a two-pass chamfer transform — each texel taking the
		/// best of its neighbours plus one step or a diagonal — which is cheap and is not a distance:
		/// its contours are octagons, its gradient kinks along eight directions, and around an
		/// island it was off by two and a half metres either way, a tenth of a surf wavelength. The
		/// surf's crests are this field's contours and its throw is this field's gradient, so the
		/// octagon went straight onto the water. Felzenszwalb and Huttenlocher's transform is exact
		/// and still linear: a lower envelope of parabolas along each row, then along each column.
		/// </para>
		/// <para>
		/// The edge lies between a wet texel and a dry one, so each side is measured to the nearest
		/// texel of the other and half a texel is taken off: a wet texel beside a dry one is half a
		/// texel from the water's edge, not on it.
		/// </para>
		/// </remarks>
		private static void Distance(Color[] pixels, int resolution, float metresPerTexel)
		{
			int count = pixels.Length;
			var toDry = new float[count];
			var toWet = new float[count];
			for (int i = 0; i < count; i++)
			{
				bool wet = pixels[i].r > 0f;
				toDry[i] = wet ? Far : 0f;
				toWet[i] = wet ? 0f : Far;
			}
			SquaredDistance(toDry, resolution);
			SquaredDistance(toWet, resolution);

			// Negative on dry land, positive in the water, so one number says both which side of
			// the edge a point is on and how far.
			for (int i = 0; i < count; i++)
			{
				bool wet = pixels[i].r > 0f;
				/* Capped: a field with no dry texel at all, or no wet one, leaves the stand-in for
				 * infinity in place, and ten billion metres overflows the half-float texture to inf —
				 * and the surf's phase to NaN. Four widths of the field is further than any wave cares. */
				float texels = Mathf.Min(Mathf.Sqrt(wet ? toDry[i] : toWet[i]), resolution * 4f) - 0.5f;
				float metres = Mathf.Max(0f, texels) * metresPerTexel;
				pixels[i].g = wet ? metres : -metres;
			}
		}

		/// <summary>Stands in for infinity: finite, so the envelope's arithmetic never meets inf - inf.</summary>
		private const float Far = 1e20f;

		/// <summary>
		/// Squared Euclidean distance, in texels, from every texel to the nearest one holding 0 —
		/// in place, rows then columns (Felzenszwalb and Huttenlocher, 2012).
		/// </summary>
		private static void SquaredDistance(float[] grid, int resolution)
		{
			var line = new float[resolution];
			var result = new float[resolution];
			var hull = new int[resolution];
			var bounds = new float[resolution + 1];
			for (int y = 0; y < resolution; y++)
			{
				System.Array.Copy(grid, y * resolution, line, 0, resolution);
				Envelope(line, result, hull, bounds, resolution);
				System.Array.Copy(result, 0, grid, y * resolution, resolution);
			}
			for (int x = 0; x < resolution; x++)
			{
				for (int y = 0; y < resolution; y++)
				{
					line[y] = grid[y * resolution + x];
				}
				Envelope(line, result, hull, bounds, resolution);
				for (int y = 0; y < resolution; y++)
				{
					grid[y * resolution + x] = result[y];
				}
			}
		}

		/// <summary>
		/// One line of the transform: the lower envelope of the parabolas (q - p)² + f(q), then
		/// read off at every p.
		/// </summary>
		private static void Envelope(float[] f, float[] d, int[] hull, float[] bounds, int n)
		{
			int k = 0;
			hull[0] = 0;
			bounds[0] = float.NegativeInfinity;
			bounds[1] = float.PositiveInfinity;
			for (int q = 1; q < n; q++)
			{
				float s = Intersection(f, q, hull[k]);
				while (s <= bounds[k])
				{
					k--;
					s = Intersection(f, q, hull[k]);
				}
				k++;
				hull[k] = q;
				bounds[k] = s;
				bounds[k + 1] = float.PositiveInfinity;
			}
			k = 0;
			for (int q = 0; q < n; q++)
			{
				while (bounds[k + 1] < q)
				{
					k++;
				}
				float offset = q - hull[k];
				d[q] = offset * offset + f[hull[k]];
			}
		}

		/// <summary>Where the parabolas rooted at q and p cross.</summary>
		private static float Intersection(float[] f, int q, int p)
		{
			return ((f[q] + (float)q * q) - (f[p] + (float)p * p)) / (2f * (q - p));
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
