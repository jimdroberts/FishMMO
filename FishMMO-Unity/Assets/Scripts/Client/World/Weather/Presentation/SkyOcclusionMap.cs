using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// A top-down map of the highest surface around the camera, so rain and snow stop at roofs.
	/// </summary>
	/// <remarks>
	/// Built from downward raycasts against the scene's own physics (world scenes have local
	/// physics), a few hundred per frame, and rebuilt once the camera has moved a quarter of the
	/// map away. The heights go to the GPU as 16 bits across two channels of an 8-bit texture,
	/// which every target samples in a vertex shader, WebGL2 included.
	/// </remarks>
	public sealed class SkyOcclusionMap
	{
		public static readonly int TextureId = Shader.PropertyToID("_FishOcclusionTex");
		public static readonly int RectId = Shader.PropertyToID("_FishOcclusionRect");
		public static readonly int RangeId = Shader.PropertyToID("_FishOcclusionRange");

		private const float CastAbove = 250f;
		private const float CastDistance = 800f;

		private Texture2D texture;
		private float[] heights = new float[0];
		private float[] building = new float[0];
		private int resolution;
		private float texelMeters;
		private Vector2 centre;
		private Vector2 buildCentre;
		private float buildTop;
		private int nextRay = -1;
		private bool valid;
		private float minHeight;
		private float heightRange = 1f;

		public bool IsValid => valid;
		public int Resolution => resolution;
		public Vector2 Centre => centre;
		public float SizeMeters => resolution * texelMeters;
		public Texture2D Texture => texture;

		/// <summary>The highest surface at a point, or false when the point is off the map.</summary>
		public bool TryGetHeight(float x, float z, out float height)
		{
			height = float.NegativeInfinity;
			if (!valid)
			{
				return false;
			}
			float size = SizeMeters;
			float u = (x - (centre.x - size * 0.5f)) / texelMeters;
			float v = (z - (centre.y - size * 0.5f)) / texelMeters;
			int ix = Mathf.FloorToInt(u), iz = Mathf.FloorToInt(v);
			if (ix < 0 || iz < 0 || ix >= resolution || iz >= resolution)
			{
				return false;
			}
			height = heights[iz * resolution + ix];
			return true;
		}

		/// <summary>True when something solid is overhead at a position.</summary>
		public bool IsCovered(Vector3 position, float clearance = 0.5f)
		{
			return TryGetHeight(position.x, position.z, out float top) && top > position.y + clearance;
		}

		/// <summary>Advances the build and publishes the map when a build finishes.</summary>
		public void Update(Vector3 camera, PhysicsScene physics, WeatherTierSettings tier, LayerMask layers)
		{
			if (tier.OcclusionResolution != resolution || !Mathf.Approximately(tier.OcclusionTexelMeters, texelMeters))
			{
				Allocate(tier.OcclusionResolution, tier.OcclusionTexelMeters);
			}
			var cameraXZ = new Vector2(camera.x, camera.z);
			if (nextRay < 0)
			{
				if (valid && Vector2.Distance(cameraXZ, centre) < SizeMeters * 0.25f)
				{
					return;
				}
				// Snap to the texel grid so rebuilds do not shimmer.
				buildCentre = new Vector2(Mathf.Round(camera.x / texelMeters) * texelMeters, Mathf.Round(camera.z / texelMeters) * texelMeters);
				buildTop = camera.y + CastAbove;
				nextRay = 0;
			}

			int count = resolution * resolution;
			int end = Mathf.Min(count, nextRay + Mathf.Max(1, tier.OcclusionRaysPerFrame));
			float size = SizeMeters;
			float left = buildCentre.x - size * 0.5f, bottom = buildCentre.y - size * 0.5f;
			for (int i = nextRay; i < end; i++)
			{
				int ix = i % resolution, iz = i / resolution;
				var origin = new Vector3(left + (ix + 0.5f) * texelMeters, buildTop, bottom + (iz + 0.5f) * texelMeters);
				building[i] = physics.Raycast(origin, Vector3.down, out RaycastHit hit, CastDistance, layers, QueryTriggerInteraction.Ignore)
					? hit.point.y
					: buildTop - CastDistance;
			}
			nextRay = end;
			if (nextRay >= count)
			{
				Publish();
			}
		}

		/// <summary>Uses a finished set of heights (tests and the build both end here).</summary>
		public void Publish(float[] source, Vector2 mapCentre)
		{
			System.Array.Copy(source, building, Mathf.Min(source.Length, building.Length));
			buildCentre = mapCentre;
			Publish();
		}

		private void Publish()
		{
			nextRay = -1;
			float min = float.MaxValue, max = float.MinValue;
			for (int i = 0; i < building.Length; i++)
			{
				min = Mathf.Min(min, building[i]);
				max = Mathf.Max(max, building[i]);
			}
			float range = Mathf.Max(1f, max - min);
			var pixels = new Color32[building.Length];
			for (int i = 0; i < building.Length; i++)
			{
				int encoded = Mathf.Clamp(Mathf.RoundToInt((building[i] - min) / range * 65535f), 0, 65535);
				pixels[i] = new Color32((byte)(encoded >> 8), (byte)(encoded & 0xff), 0, 255);
			}
			(heights, building) = (building, heights);
			centre = buildCentre;
			minHeight = min;
			heightRange = range;
			valid = true;
			texture.SetPixels32(pixels);
			texture.Apply(false, false);
			Bind();
		}

		/// <summary>Points the shader globals at this map.</summary>
		public void Bind()
		{
			if (texture == null)
			{
				Shader.SetGlobalVector(RangeId, Vector4.zero);
				return;
			}
			float size = SizeMeters;
			Shader.SetGlobalTexture(TextureId, texture);
			Shader.SetGlobalVector(RectId, new Vector4(centre.x - size * 0.5f, centre.y - size * 0.5f, size, size));
			Shader.SetGlobalVector(RangeId, new Vector4(minHeight, heightRange, valid ? 1f : 0f, texelMeters));
		}

		/// <summary>Forgets the map (scene change).</summary>
		public void Invalidate()
		{
			valid = false;
			nextRay = -1;
			Shader.SetGlobalVector(RangeId, Vector4.zero);
		}

		/// <summary>Sets the map size, discarding any map.</summary>
		public void Allocate(int size, float texel)
		{
			Dispose();
			resolution = Mathf.Max(4, size);
			texelMeters = Mathf.Max(0.1f, texel);
			heights = new float[resolution * resolution];
			building = new float[resolution * resolution];
			texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
			{
				name = "Sky Occlusion",
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
			valid = false;
			nextRay = -1;
		}

		public void Dispose()
		{
			if (texture != null)
			{
				if (Application.isPlaying)
				{
					Object.Destroy(texture);
				}
				else
				{
					Object.DestroyImmediate(texture);
				}
				texture = null;
			}
			valid = false;
		}
	}
}
