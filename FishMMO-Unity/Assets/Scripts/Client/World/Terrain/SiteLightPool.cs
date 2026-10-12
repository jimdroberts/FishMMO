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
	/// <b>When.</b> Lamps from dusk (<c>_FishNightLights</c>, SkySystem); fires all day, faint in daylight, flickering;
	/// forges low and steady with a slow breath. No shadows: dozens of small lights cast none worth their cost.
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
			float time = Time.time;
			int shadowed = 0;
			for (int i = 0; i < MaxLights; i++)
			{
				bool wanted = i < near.Count;
				if (!wanted)
				{
					if (i < pool.Count && pool[i] != null && pool[i].enabled)
					{
						pool[i].enabled = false;
					}
					continue;
				}
				SceneEmitter e = near[i];
				Light light = LightAt(i);
				float distance = Vector3.Distance(eye, e.Position);
				float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Reach * 0.75f, Reach, distance));
				float phase = (e.Seed & 0xFFFF) * 0.0137f;
				float intensity;
				light.shadows = LightShadows.None;
				switch (e.Kind)
				{
					case SceneEmitterKind.Indoor:
					{
						// A room's hearth and candles, filling the room and no further. Most homes are lived in tonight, not all.
						bool home = (e.Seed & 0xFF) < 205;
						float flick = 0.9f + 0.06f * Mathf.Sin(time * 7.3f + phase) + 0.04f * Mathf.Sin(time * 12.1f + phase * 1.7f);
						light.color = new Color(1f, 0.62f, 0.32f);
						light.range = e.Reach > 0f ? e.Reach : 5f;
						intensity = home ? 1.8f * Mathf.Lerp(0.25f, 1f, night) * flick : 0f;
						// The nearest few cast shadows where the quality tier has them (none leak through windows and walls then).
						light.shadows = shadowed++ < MaxShadowedIndoor ? LightShadows.Soft : LightShadows.None;
						break;
					}
					case SceneEmitterKind.Crystal:
					{
						// Cave crystals: violet, blue or green by the seed, breathing slowly, the same by day as by night.
						uint hue = (e.Seed >> 8) & 3u;
						light.color = hue == 0 ? new Color(0.62f, 0.4f, 1f) : hue == 2 ? new Color(0.4f, 1f, 0.7f) : new Color(0.35f, 0.62f, 1f);
						light.range = e.Reach > 0f ? e.Reach : 6f;
						intensity = 1.4f * (0.85f + 0.15f * Mathf.Sin(time * 0.7f + phase));
						break;
					}
					case SceneEmitterKind.Torch:
					{
						// A torch: underground (a mine's, a cave camp's) there is no sky to outshine it, so it burns as bright at noon
						// as at midnight; in the open it is a small light by day (a gatehouse's, a village street's).
						float flick = 0.88f + 0.08f * Mathf.Sin(time * 9.7f + phase) + 0.04f * Mathf.Sin(time * 15.3f + phase * 1.9f);
						bool buried = TerrainSet.Current.TryHeight(e.Position.x, e.Position.z, out float ground) && e.Position.y < ground - 3f;
						light.color = new Color(1f, 0.66f, 0.36f);
						light.range = e.Reach > 0f ? e.Reach : 8f;
						intensity = 1.6f * flick * (buried ? 1f : Mathf.Lerp(0.3f, 1f, night));
						break;
					}
					case SceneEmitterKind.Fire:
						// A fire's flicker: three unrelated beats, never in step with the next fire.
						float flicker = 0.8f + 0.1f * Mathf.Sin(time * 11.3f + phase) + 0.06f * Mathf.Sin(time * 17.9f + phase * 2.1f) + 0.04f * Mathf.Sin(time * 5.1f + phase * 3.7f);
						light.color = new Color(1f, 0.56f, 0.24f);
						light.range = 11f;
						intensity = 3.2f * Mathf.Lerp(0.3f, 1f, night) * flicker;
						break;
					case SceneEmitterKind.Forge:
						light.color = new Color(1f, 0.45f, 0.16f);
						light.range = 7f;
						intensity = 2.6f * Mathf.Lerp(0.5f, 1f, night) * (0.9f + 0.1f * Mathf.Sin(time * 1.3f + phase));
						break;
					default:
						light.color = new Color(1f, 0.72f, 0.42f);
						light.range = 9f;
						intensity = 2.2f * night;
						break;
				}
				intensity *= fade;
				light.transform.position = e.Position;
				light.intensity = intensity;
				light.enabled = intensity > 0.01f;
			}
		}

		/// <summary>Indoor lights that cast shadows at once (where the URP tier supports additional-light shadows).</summary>
		public const int MaxShadowedIndoor = 4;

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
				pool.Add(light);
			}
			return pool[index];
		}
	}
}
