using System.Collections.Generic;
using FishMMO.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// Real light from the sites' lamps, fires and forges (SceneSiteEmitters, baked by the cut): a small pool of point
	/// lights given to the nearest of them round the camera, so a lantern lights the street under it at night and a
	/// campfire flickers on the tents round it (2026-10-11).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A pool, not a light a lamp.</b> A town has hundreds of lanterns; only the nearest <see cref="MaxLights"/> within
	/// <see cref="Reach"/> are lit for real, each faded out over the last quarter of the reach so none pops. The rest still
	/// glow (their panes, FishStructureWeathering.hlsl), which is all a lamp far off is.
	/// </para>
	/// <para>
	/// <b>When.</b> Lamps from dusk (<c>_FishNightLights</c>, SkySystem), underground always; fires all day, faint in daylight, flickering;
	/// forges low and steady with a slow breath. Shadows only from the nearest few indoor lights, and only with the camera
	/// indoors or beside them (<see cref="IndoorShadowMetres"/>); GPU-drawn vegetation never casts into them
	/// (FishVegetationPasses.hlsl). Dozens of small lights otherwise cast none worth their cost.
	/// </para>
	/// <para>Play mode, client only. Self-starting, like the ambient life.</para>
	/// </remarks>
	public static class SiteLightPool
	{
		public const int MaxLights = 16;
		public const float Reach = 70f;
		private const float Refind = 8f;

		private static readonly int NightLightsId = Shader.PropertyToID("_FishNightLights");
		private static readonly List<Light> pool = new List<Light>();
		private static readonly List<SceneEmitter> near = new List<SceneEmitter>();
		private static Vector3 foundAt = new Vector3(float.MaxValue, 0f, 0f);
		private static int foundVersion = -1;
		private static int updatedFrame = -1;
		private static bool hooked;
		private static GameObject host;

		// What each pooled light was last given, so a frame writes only what changed: the look (colour, range,
		// shadows) and place change only when a slot's emitter does, the intensity every frame.
		private static readonly Color[] lastColor = new Color[MaxLights];
		private static readonly float[] lastRange = new float[MaxLights];
		private static readonly LightShadows[] lastShadows = new LightShadows[MaxLights];
		private static readonly Vector3[] lastPosition = new Vector3[MaxLights];
		private static readonly bool[] lastEnabled = new bool[MaxLights];

		private static readonly Unity.Profiling.ProfilerMarker UpdateMarker = new Unity.Profiling.ProfilerMarker("SiteLightPool.Update");

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Release();
			hooked = false;
			updatedFrame = -1;
			foundVersion = -1;
			foundAt = new Vector3(float.MaxValue, 0f, 0f);
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			SceneManager.sceneUnloaded += _ => foundVersion = -1;
			Application.quitting += Shutdown;
		}

		private static void Shutdown()
		{
			RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
			hooked = false;
			Release();
		}

		private static void Release()
		{
			foreach (Light light in pool)
			{
				if (light != null)
				{
					Object.Destroy(light.gameObject);
				}
			}
			pool.Clear();
			if (host != null)
			{
				Object.Destroy(host);
				host = null;
			}
		}

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			if (updatedFrame == Time.frameCount)
			{
				return;
			}
			updatedFrame = Time.frameCount;
			using (UpdateMarker.Auto())
			{
				UpdateLights();
			}
		}

		private static void UpdateLights()
		{
			Camera camera = Camera.main;
			if (camera == null)
			{
				return;
			}
			Vector3 eye = camera.transform.position;
			if (SceneSiteEmitters.Version != foundVersion || (eye - foundAt).sqrMagnitude > Refind * Refind)
			{
				foundVersion = SceneSiteEmitters.Version;
				foundAt = eye;
				// Not a chimney's smoke, and not an indoor hearth's bed: a room's light is its Indoor emitter, at its middle.
				SceneSiteEmitters.Near(eye, Reach + Refind, kind => kind != SceneEmitterKind.Smoke && kind != SceneEmitterKind.Hearth, MaxLights, near);
			}
			float night = Mathf.Clamp01(Shader.GetGlobalFloat(NightLightsId));
			// The shared motion clock, wrapped for a float: every player's hearths flicker together.
			float time = FishMMO.Shared.Celestial.WorldMotion.Wrapped(FishMMO.Shared.Celestial.WorldMotion.ShaderWrapSeconds);
			int shadowed = 0;
			// Indoors, the room's own lights may cast; outdoors only those right beside the camera, where a light leaking
			// through a wall would show.
			bool indoors = IndoorRooms.IndoorAt(eye) > 0f;
			for (int i = 0; i < MaxLights; i++)
			{
				bool wanted = i < near.Count;
				if (!wanted)
				{
					if (i < pool.Count && pool[i] != null && lastEnabled[i])
					{
						pool[i].enabled = false;
						lastEnabled[i] = false;
					}
					continue;
				}
				SceneEmitter e = near[i];
				Light light = LightAt(i);
				float distance = Vector3.Distance(eye, e.Position);
				float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Reach * 0.75f, Reach, distance));
				float phase = (e.Seed & 0xFFFF) * 0.0137f;
				float intensity;
				Color color;
				float range;
				LightShadows shadows = LightShadows.None;
				switch (e.Kind)
				{
					case SceneEmitterKind.Indoor:
					{
						// A room's hearth and candles, filling the room and no further. Most homes are lived in tonight, not all.
						bool home = (e.Seed & 0xFF) < 205;
						float flick = 0.9f + 0.06f * Mathf.Sin(time * 7.3f + phase) + 0.04f * Mathf.Sin(time * 12.1f + phase * 1.7f);
						color = new Color(1f, 0.62f, 0.32f);
						range = e.Reach > 0f ? e.Reach : 5f;
						intensity = home ? 1.8f * Mathf.Lerp(0.25f, 1f, night) * flick : 0f;
						// The nearest few cast shadows where the quality tier has them (none leak through windows and walls then),
						// and only while the camera is indoors or within IndoorShadowMetres: a shadowed point light renders six
						// cube faces of every caster in reach, a cost only worth paying where the leak could be seen.
						if (indoors || distance <= IndoorShadowMetres)
						{
							shadows = shadowed++ < MaxShadowedIndoor ? LightShadows.Soft : LightShadows.None;
						}
						break;
					}
					case SceneEmitterKind.Crystal:
					{
						// Cave crystals (and the glowing fungi and ores beside them): violet, blue, green or amber by the seed, breathing
						// slowly, the same by day as by night.
						uint hue = (e.Seed >> 8) & 3u;
						color = hue == 0 ? new Color(0.62f, 0.4f, 1f) : hue == 2 ? new Color(0.4f, 1f, 0.7f) : hue == 3 ? new Color(1f, 0.68f, 0.32f) : new Color(0.35f, 0.62f, 1f);
						range = e.Reach > 0f ? e.Reach : 6f;
						intensity = 1.4f * (0.85f + 0.15f * Mathf.Sin(time * 0.7f + phase));
						break;
					}
					case SceneEmitterKind.Torch:
					{
						// A torch: underground (a mine's, a cave camp's) there is no sky to outshine it, so it burns as bright at noon
						// as at midnight; in the open it is a small light by day (a gatehouse's, a village street's).
						float flick = 0.88f + 0.08f * Mathf.Sin(time * 9.7f + phase) + 0.04f * Mathf.Sin(time * 15.3f + phase * 1.9f);
						bool buried = TerrainSet.Current.TryHeight(e.Position.x, e.Position.z, out float ground) && e.Position.y < ground - 3f;
						color = new Color(1f, 0.66f, 0.36f);
						range = e.Reach > 0f ? e.Reach : 8f;
						intensity = 1.6f * flick * (buried ? 1f : Mathf.Lerp(0.3f, 1f, night));
						break;
					}
					case SceneEmitterKind.Fire:
						// A fire's flicker: three unrelated beats, never in step with the next fire.
						float flicker = 0.8f + 0.1f * Mathf.Sin(time * 11.3f + phase) + 0.06f * Mathf.Sin(time * 17.9f + phase * 2.1f) + 0.04f * Mathf.Sin(time * 5.1f + phase * 3.7f);
						color = new Color(1f, 0.56f, 0.24f);
						range = 11f;
						intensity = 3.2f * Mathf.Lerp(0.3f, 1f, night) * flicker;
						break;
					case SceneEmitterKind.Forge:
						color = new Color(1f, 0.45f, 0.16f);
						range = 7f;
						intensity = 2.6f * Mathf.Lerp(0.5f, 1f, night) * (0.9f + 0.1f * Mathf.Sin(time * 1.3f + phase));
						break;
					default:
					{
						// A lamp: lit from dusk, but underground (a mine's lanterns, a cave camp's) no dusk comes, so it burns day
						// and night, as a torch there does.
						bool buried = TerrainSet.Current.TryHeight(e.Position.x, e.Position.z, out float ground) && e.Position.y < ground - 3f;
						color = new Color(1f, 0.72f, 0.42f);
						range = e.Reach > 0f ? e.Reach : 9f;
						intensity = 2.2f * (buried ? 1f : night);
						break;
					}
				}
				intensity *= fade;
				// Exact comparisons: a value is skipped only when it is the very one the light already has.
				if (color.r != lastColor[i].r || color.g != lastColor[i].g || color.b != lastColor[i].b || color.a != lastColor[i].a)
				{
					light.color = color;
					lastColor[i] = color;
				}
				if (range != lastRange[i])
				{
					light.range = range;
					lastRange[i] = range;
				}
				if (shadows != lastShadows[i])
				{
					light.shadows = shadows;
					lastShadows[i] = shadows;
				}
				if (e.Position.x != lastPosition[i].x || e.Position.y != lastPosition[i].y || e.Position.z != lastPosition[i].z)
				{
					light.transform.position = e.Position;
					lastPosition[i] = e.Position;
				}
				light.intensity = intensity;
				bool enabled = intensity > 0.01f;
				if (enabled != lastEnabled[i])
				{
					light.enabled = enabled;
					lastEnabled[i] = enabled;
				}
			}
		}

		/// <summary>Indoor lights that cast shadows at once (where the URP tier supports additional-light shadows).</summary>
		public const int MaxShadowedIndoor = 4;

		/// <summary>From outdoors, how near an indoor light must be to cast shadows (indoors, the nearest few always may).</summary>
		public const float IndoorShadowMetres = 12f;

		private static Light LightAt(int index)
		{
			while (pool.Count <= index)
			{
				if (host == null)
				{
					host = new GameObject("Site Lights") { hideFlags = HideFlags.DontSave };
					Object.DontDestroyOnLoad(host);
				}
				var go = new GameObject("Site Light " + pool.Count) { hideFlags = HideFlags.DontSave };
				go.transform.SetParent(host.transform, false);
				Light light = go.AddComponent<Light>();
				light.type = LightType.Point;
				light.shadows = LightShadows.None;
				light.renderMode = LightRenderMode.Auto;
				light.enabled = false;
				// Nothing remembered for a new light: NaN never equals, so its first frame writes everything.
				int slot = pool.Count;
				lastColor[slot] = new Color(float.NaN, float.NaN, float.NaN, float.NaN);
				lastRange[slot] = float.NaN;
				lastShadows[slot] = LightShadows.None;
				lastPosition[slot] = new Vector3(float.NaN, float.NaN, float.NaN);
				lastEnabled[slot] = false;
				pool.Add(light);
			}
			return pool[index];
		}
	}
}
