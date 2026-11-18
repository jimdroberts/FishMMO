using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldMaps;
using Object = UnityEngine.Object;

namespace FishMMO.Client
{
	/// <summary>
	/// Lights a world map's photograph as the scene's own sky lights it at local solar noon on the
	/// equinox, with the weather cleared, and puts every light, render setting and shader global
	/// back the moment the photograph is taken.
	/// </summary>
	/// <remarks>
	/// <para><b>Why the bake needs it.</b> In edit mode nothing drives the sky. The client's
	/// <see cref="SkySystem"/> makes the sun and moon lights and writes the ambient light at runtime
	/// only, so a scene opened in the editor has no sun at all — the sky's lights are its own
	/// children, made in play — and whatever ambient colours and weather globals were left behind by
	/// the last thing that set them: the scene's saved lighting, or the last frame of a play session,
	/// whose rain, snow cover and fog the shader globals still hold. A map photographed like that
	/// came out unlit and different every time. This writes what the sky would write at the
	/// capture's moment (<see cref="WorldMapCaptureMoment"/>), directly, before the synchronous
	/// render, using the sky's own sample (<see cref="SkyProfile.Evaluate"/>, then
	/// <see cref="SkySystem.TintBySuns"/>) and the sky's own light rotation, so the map's sun is the
	/// game's noon sun.</para>
	///
	/// <para><b>What is written, and why only that.</b></para>
	/// <list type="bullet">
	/// <item>One directional light at the primary sun, in the sample's sun colour and intensity, as
	/// <see cref="RenderSettings.sun"/>; the ambient trilight and its <c>_FishAmbient*</c> twin. With
	/// the weather clear there is no cloud to dim either (the sky's cloud-through factors are 1). No
	/// moonlight (a moon by day adds nothing a map can show) and no companion suns: the map is lit
	/// by the sun that names the time. No shadows: the camera is two kilometres up, past every
	/// quality level's shadow distance, so a shadow map would be work for an empty texture.</item>
	/// <item>The weather's surface globals cleared (snow, wet, ash and sand cover, the cover map,
	/// what is falling, wind, fog channels, the season), and the fog layer the fog passes ask about
	/// swapped for none, so neither the height fog nor the froxel fog draws. The clouds, cloud
	/// shadows, precipitation and lightning are the sky system's and the presenters', none of which
	/// exists outside play: the cloud pass asks <see cref="SkySystem.CloudsReady"/> and stands
	/// down.</item>
	/// <item>The season is cleared, not set to the equinox's spring: "no season known" is the
	/// foliage's healthy colours, which is what a map should show whichever hemisphere it is in.</item>
	/// </list>
	///
	/// <para><b>A scene with no day/night cycle keeps its own light.</b> At runtime the sky leaves
	/// such a scene (a dungeon, a tutorial) exactly as it was authored, so the map does too: only
	/// the weather is cleared.</para>
	///
	/// <para>Registered with the bake rather than called by it, because the bake is in a shared
	/// assembly that cannot see the client (<see cref="WorldMapBaker.CaptureStaging"/>). The scene
	/// view is not touched in any lasting way: every value is read before it is written and written
	/// back in <c>Dispose</c>.</para>
	/// </remarks>
	[InitializeOnLoad]
	public static class WorldMapCaptureSky
	{
		/// <summary>Where the light comes from when the scene is not placed in a solar system: the sky's own fallback.</summary>
		public static readonly Vector3 FallbackSunDirection = new Vector3(0.3f, 0.8f, 0.5f).normalized;

		/// <summary>The sun altitude the fallback light is sampled at, in degrees (the sky's own fallback).</summary>
		public const float FallbackSunAltitude = 50f;

		/// <summary>The temporary light's name, for anyone looking at a hierarchy mid-capture.</summary>
		public const string SunName = "World Map Capture Sun";

		private static readonly int AmbientSkyId = Shader.PropertyToID("_FishAmbientSky");
		private static readonly int AmbientEquatorId = Shader.PropertyToID("_FishAmbientEquator");
		private static readonly int AmbientGroundId = Shader.PropertyToID("_FishAmbientGround");
		private static readonly int CoverParamsId = Shader.PropertyToID("_FishCoverParams");
		private static readonly int FogLayerId = Shader.PropertyToID("_FishFogLayer");
		private static readonly int FogShellId = Shader.PropertyToID("_FishFogShell");

		static WorldMapCaptureSky()
		{
			WorldMapBaker.CaptureStaging.Add(Stage);
		}

		/// <summary>
		/// The weather's globals as a clear, calm, dry day writes them: what <see cref="WeatherShaderGlobals.Clear"/>
		/// publishes, plus the cover map switched off so the scene-wide (clear) figure is read everywhere.
		/// </summary>
		public static IEnumerable<(int Id, Vector4 Value)> ClearWeatherGlobals()
		{
			Vector2 wind = WeatherShaderGlobals.WindDirection(0f);
			yield return (WeatherShaderGlobals.Cloud, Vector4.zero);
			yield return (WeatherShaderGlobals.Precip, Vector4.zero);
			yield return (WeatherShaderGlobals.Mix, Vector4.zero);
			yield return (WeatherShaderGlobals.Mix2, Vector4.zero);
			yield return (WeatherShaderGlobals.Wind, new Vector4(wind.x, wind.y, 0f, 0f));
			yield return (WeatherShaderGlobals.Fog, Vector4.zero);
			yield return (WeatherShaderGlobals.Cover, Vector4.zero);
			yield return (WeatherShaderGlobals.Misc, Vector4.zero);
			yield return (WeatherShaderGlobals.Substance, new Vector4(1f, 1f, 1f, 0f));
			yield return (WeatherShaderGlobals.Season, Vector4.zero);
			yield return (CoverParamsId, Vector4.zero);
			yield return (FogLayerId, Vector4.zero);
			yield return (FogShellId, Vector4.zero);
		}

		/// <summary>
		/// Sets the scene up for its map photograph and returns what puts it all back. Public for the
		/// tests; the bake calls it through <see cref="WorldMapBaker.CaptureStaging"/>.
		/// </summary>
		public static IDisposable Stage(WorldMapCaptureContext context)
		{
			var restore = new Restore();
			try
			{
				restore.ClearWeather();
				if (context != null && context.HasSky)
				{
					restore.LightAtNoon(context);
				}
			}
			catch
			{
				restore.Dispose();
				throw;
			}
			return restore;
		}

		/// <summary>
		/// The sun the map is lit by: the direction toward it, its colour and intensity, and the
		/// ambient trilight, as the sky would have them at the context's moment with the weather clear.
		/// </summary>
		public static SkySample NoonSample(WorldMapCaptureContext context, SkyProfile profile, out Vector3 towardSun)
		{
			var state = new CelestialState();
			if (context.Hours.HasValue)
			{
				state.Compute(context.System, context.Body, context.Hours.Value, context.Latitude, context.Longitude, context.HeadingDegrees);
			}
			bool placed = state.Sun >= 0;
			towardSun = placed ? state.SunDirection : FallbackSunDirection;
			float altitude = placed ? state.SunAltitude : FallbackSunAltitude;
			SkySample sample = profile.Evaluate(altitude, state.Observer != null ? state.Observer : context.Body);
			// The profile is authored for the reference sun; the suns that are up recolour it.
			return SkySystem.TintBySuns(state, sample, out _);
		}

		/// <summary>Everything one capture changed, and how to put it back.</summary>
		private sealed class Restore : IDisposable
		{
			private readonly List<(int Id, Vector4 Value)> globals = new List<(int Id, Vector4 Value)>();
			private bool weatherCleared;
			private FogLayerView fogLayer;

			private bool lit;
			private Light sunLight;
			private SkyProfile ownProfile;
			private Light renderSun;
			private AmbientMode ambientMode;
			private Color ambientSky, ambientEquator, ambientGround;
			private SphericalHarmonicsL2 ambientProbe;
			private bool disposed;

			private void SetGlobal(int id, Vector4 value)
			{
				globals.Add((id, Shader.GetGlobalVector(id)));
				Shader.SetGlobalVector(id, value);
			}

			/// <summary>
			/// A colour global, written as the sky writes it — as a colour, so it reaches the shader in the
			/// working colour space the same way — and remembered raw, so putting it back cannot convert it
			/// a second time.
			/// </summary>
			private void SetGlobalColour(int id, Color value)
			{
				globals.Add((id, Shader.GetGlobalVector(id)));
				Shader.SetGlobalColor(id, value);
			}

			public void ClearWeather()
			{
				fogLayer = FogLayerView.ReplaceCurrent(default);
				weatherCleared = true;
				foreach ((int id, Vector4 value) in ClearWeatherGlobals())
				{
					SetGlobal(id, value);
				}
			}

			public void LightAtNoon(WorldMapCaptureContext context)
			{
				SkyProfile profile = context.Cycle != null && context.Cycle.SkyOverride != null ? context.Cycle.SkyOverride
					: context.Body != null && context.Body.Sky != null ? context.Body.Sky
					: null;
				if (profile == null)
				{
					// The sky's own default, made the way the sky makes it: one profile for every body,
					// the body's air deciding the colours.
					ownProfile = ScriptableObject.CreateInstance<SkyProfile>();
					ownProfile.hideFlags = HideFlags.HideAndDontSave;
					ownProfile.name = "World Map Capture Sky";
					profile = ownProfile;
				}
				SkySample sample = NoonSample(context, profile, out Vector3 towardSun);

				renderSun = RenderSettings.sun;
				ambientMode = RenderSettings.ambientMode;
				ambientSky = RenderSettings.ambientSkyColor;
				ambientEquator = RenderSettings.ambientEquatorColor;
				ambientGround = RenderSettings.ambientGroundColor;
				ambientProbe = RenderSettings.ambientProbe;
				lit = true;

				var holder = new GameObject(SunName) { hideFlags = HideFlags.HideAndDontSave };
				sunLight = holder.AddComponent<Light>();
				sunLight.type = LightType.Directional;
				sunLight.transform.rotation = SkySystem.LightRotation(towardSun);
				sunLight.color = sample.SunLight;
				sunLight.intensity = sample.SunIntensity;
				sunLight.shadows = LightShadows.None;

				RenderSettings.sun = sunLight;
				RenderSettings.ambientMode = AmbientMode.Trilight;
				RenderSettings.ambientSkyColor = sample.AmbientSky;
				RenderSettings.ambientEquatorColor = sample.AmbientEquator;
				RenderSettings.ambientGroundColor = sample.AmbientGround;
				// What Graphics.RenderMesh draws reads the trilight from here (FishAmbient.hlsl).
				SetGlobalColour(AmbientSkyId, sample.AmbientSky);
				SetGlobalColour(AmbientEquatorId, sample.AmbientEquator);
				SetGlobalColour(AmbientGroundId, sample.AmbientGround);
			}

			public void Dispose()
			{
				if (disposed)
				{
					return;
				}
				disposed = true;
				if (lit)
				{
					RenderSettings.sun = renderSun;
					RenderSettings.ambientMode = ambientMode;
					RenderSettings.ambientSkyColor = ambientSky;
					RenderSettings.ambientEquatorColor = ambientEquator;
					RenderSettings.ambientGroundColor = ambientGround;
					// Last: setting the colours may recompute the probe from them, which is not what a
					// skybox-lit scene had.
					RenderSettings.ambientProbe = ambientProbe;
				}
				if (sunLight != null)
				{
					Object.DestroyImmediate(sunLight.gameObject);
				}
				sunLight = null;
				if (ownProfile != null)
				{
					Object.DestroyImmediate(ownProfile);
				}
				ownProfile = null;
				// In reverse, so a global written twice ends at the value it had first.
				for (int i = globals.Count - 1; i >= 0; i--)
				{
					Shader.SetGlobalVector(globals[i].Id, globals[i].Value);
				}
				globals.Clear();
				if (weatherCleared)
				{
					FogLayerView.ReplaceCurrent(fogLayer);
				}
			}
		}
	}
}
