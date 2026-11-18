using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// A top-down map of the highest surface around the camera, so rain and snow stop at roofs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Built from downward raycasts against the scene's own physics (world scenes have local
	/// physics), and rebuilt once the camera has moved a quarter of the map away. The rays go as
	/// one batch (<see cref="RaycastCommand"/>) to the job workers in the frame a build starts and are
	/// taken in on the next — the same closest hit of the same ray in the same physics scene as a
	/// ray cast here; the lakes and rivers laid over them are read here, a few hundred texels a
	/// frame. The heights go to the GPU as 16 bits across two channels of an 8-bit texture,
	/// which every target samples in a vertex shader, WebGL2 included.
	/// </para>
	/// <para>
	/// <b>The sea is a surface too.</b> A ray goes straight through water, so the map on its own
	/// had rain and snow falling through the sea onto the bed under it. The open water's still
	/// level (<see cref="SurfaceWater"/>) is laid over the map — here, and in the shaders from
	/// <c>_FishOcclusionWater</c> — rather than baked into it: the tide moves it all the time, and
	/// the rays never need casting again for that.
	/// </para>
	/// </remarks>
	public sealed class SkyOcclusionMap
	{
		public static readonly int TextureId = Shader.PropertyToID("_FishOcclusionTex");
		public static readonly int RectId = Shader.PropertyToID("_FishOcclusionRect");
		public static readonly int RangeId = Shader.PropertyToID("_FishOcclusionRange");
		public static readonly int WaterId = Shader.PropertyToID("_FishOcclusionWater");

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
		private Color32[] pixels = new Color32[0];

		// The build's rays on the job workers: cast in the frame the build starts, taken in on the next.
		private NativeArray<RaycastCommand> commands;
		private NativeArray<RaycastHit> hits;
		private JobHandle casting;
		private bool cast;
		/// <summary>Rays cast and taken in; the lakes and rivers are laid over them from <see cref="nextRay"/> on.</summary>
		private bool castTaken;
		private PhysicsScene castPhysics;
		private int castLayers;

		/// <summary>
		/// Set by a probe: every batch is cast again one ray at a time here and any height that differs is reported, a
		/// check in the editor that the batch is the rays it replaced. Nothing in the game sets it.
		/// </summary>
		public static bool Verify;

		public bool IsValid => valid;
		public int Resolution => resolution;
		public Vector2 Centre => centre;
		public float SizeMeters => resolution * texelMeters;
		public Texture2D Texture => texture;

		/// <summary>
		/// The highest surface at a point — the ground, or the water where the ground is lower —
		/// or false when the point is off the map.
		/// </summary>
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
			if (SurfaceWater.TryGetLevel(out float water))
			{
				height = Mathf.Max(height, water);
			}
			return true;
		}

		/// <summary>
		/// Tells the shaders where the open water is: x its still level, y 1 when there is any, z the
		/// significant height of its waves, w how fast the sea dims the daylight with depth per metre (FishSeaDaylight; 0 none,
		/// 2026-10-25). Every frame, because the tide never stops.
		/// </summary>
		public static void BindWater()
		{
			Shader.SetGlobalVector(WaterId, SurfaceWater.TryGetLevel(out float level)
				? new Vector4(level, 1f, SurfaceWater.WaveHeight, SurfaceWater.DaylightExtinction)
				: Vector4.zero);
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
				Cast(physics, layers);
				return;
			}
			if (!castTaken)
			{
				Take();
			}

			int count = resolution * resolution;
			int end = Mathf.Min(count, nextRay + Mathf.Max(1, tier.OcclusionRaysPerFrame));
			float size = SizeMeters;
			float left = buildCentre.x - size * 0.5f, bottom = buildCentre.y - size * 0.5f;
			for (int i = nextRay; i < end; i++)
			{
				int ix = i % resolution, iz = i / resolution;
				float x = left + (ix + 0.5f) * texelMeters, z = bottom + (iz + 0.5f) * texelMeters;
				// A ray goes through a lake or a river to its bed; their surfaces are laid in here. The sea is
				// laid over at every read instead, because its tide moves.
				if (SurfaceWater.TryGetInlandSurfaceAt(x, z, out float inland) && inland > building[i])
				{
					building[i] = inland;
				}
			}
			nextRay = end;
			if (nextRay >= count)
			{
				Publish();
			}
		}

		/// <summary>Every ray of the build, as one batch on the job workers.</summary>
		private void Cast(PhysicsScene physics, LayerMask layers)
		{
			int count = resolution * resolution;
			float size = SizeMeters;
			float left = buildCentre.x - size * 0.5f, bottom = buildCentre.y - size * 0.5f;
			// What the single ray asked for: the layers, no triggers, and back faces as the physics settings have them.
			var query = new QueryParameters(layers, false, QueryTriggerInteraction.Ignore, Physics.queriesHitBackfaces);
			for (int i = 0; i < count; i++)
			{
				int ix = i % resolution, iz = i / resolution;
				var origin = new Vector3(left + (ix + 0.5f) * texelMeters, buildTop, bottom + (iz + 0.5f) * texelMeters);
				commands[i] = new RaycastCommand(physics, origin, Vector3.down, query, CastDistance);
			}
			castPhysics = physics;
			castLayers = layers;
			casting = RaycastCommand.ScheduleBatch(commands, hits, 32, default(JobHandle));
			cast = true;
			castTaken = false;
		}

		/// <summary>The batch's heights: the closest surface each ray met, or as far as it went.</summary>
		private void Take()
		{
			Complete();
			int count = resolution * resolution;
			float floor = MissFloor(buildTop);
			int differ = 0;
			for (int i = 0; i < count; i++)
			{
				RaycastHit hit = hits[i];
				building[i] = hit.colliderEntityId != EntityId.None ? hit.point.y : floor;
				if (Verify)
				{
					RaycastCommand ray = commands[i];
					float single = castPhysics.Raycast(ray.from, Vector3.down, out RaycastHit one, CastDistance, castLayers, QueryTriggerInteraction.Ignore)
						? one.point.y
						: floor;
					if (single != building[i])
					{
						differ++;
					}
				}
			}
			if (Verify)
			{
				Debug.Log($"[SkyOcclusionMap] {count} batched rays, {differ} heights differ from the single ray's.");
			}
			castTaken = true;
		}

		/// <summary>
		/// Where a ray that met nothing leaves the rain: as far as it went — or over a cloud world, down in its deck,
		/// out of sight. A ray cast between the islands meets nothing, and the rain and its splashes stopped where it
		/// gave up, hanging in the open air a few hundred metres under the camera.
		/// </summary>
		private static float MissFloor(float top)
		{
			float floor = top - CastDistance;
			FishMMO.Shared.SceneCloudWorld world = FishMMO.Shared.SceneCloudWorld.First;
			return world != null ? Mathf.Min(floor, world.DeckTop - Mathf.Max(CloudClimate.DeckBillowDepth, world.DeckDepth)) : floor;
		}

		/// <summary>Waits for the batch, if one is out.</summary>
		private void Complete()
		{
			if (cast)
			{
				casting.Complete();
				cast = false;
			}
		}

		/// <summary>Uses a finished set of heights (tests and the build both end here).</summary>
		public void Publish(float[] source, Vector2 mapCentre)
		{
			Complete();
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
			if (pixels.Length != building.Length)
			{
				pixels = new Color32[building.Length];
			}
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
			// The scene the rays were cast in is going.
			Complete();
			valid = false;
			nextRay = -1;
			Shader.SetGlobalVector(RangeId, Vector4.zero);
			Shader.SetGlobalVector(WaterId, Vector4.zero);
		}

		/// <summary>Sets the map size, discarding any map.</summary>
		public void Allocate(int size, float texel)
		{
			Dispose();
			resolution = Mathf.Max(4, size);
			texelMeters = Mathf.Max(0.1f, texel);
			heights = new float[resolution * resolution];
			building = new float[resolution * resolution];
			pixels = new Color32[resolution * resolution];
			commands = new NativeArray<RaycastCommand>(resolution * resolution, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			hits = new NativeArray<RaycastHit>(resolution * resolution, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
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
			Complete();
			if (commands.IsCreated)
			{
				commands.Dispose();
			}
			if (hits.IsCreated)
			{
				hits.Dispose();
			}
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
