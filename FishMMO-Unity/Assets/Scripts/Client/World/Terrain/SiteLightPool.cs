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
	/// campfire flickers on the tents round it (Jim, 2026-10-11).
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
				SceneSiteEmitters.Near(eye, Reach + Refind, kind => kind != SceneEmitterKind.Smoke, MaxLights, near);
			}
			float night = Mathf.Clamp01(Shader.GetGlobalFloat(NightLightsId));
			float time = Time.time;
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
				switch (e.Kind)
				{
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
