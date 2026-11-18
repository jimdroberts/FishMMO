using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The neutral world the core's surface shaders see before any world module sets its globals: dry, calm, no snow, no
	/// season, and a mild sky's ambient light.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Core (Phase 4 of the core and modules plan, 2026-11-17; the "core surface stack" decision). FishWeatherLit and
	/// FishVegetation, with their includes (Assets/FishMMO/Shaders/Surface), are core rendering: core content (the
	/// characters, the teleporter, the gathering nodes) is drawn with them. Their globals are core-owned, and the world
	/// modules only set them: the sky its ambient, the weather its wind, cover and snow line, the terrain its ground maps.
	/// </para>
	/// <para>
	/// Unset globals read zero, and the surface shaders are written so that zero is the neutral world: no cloud, rain, wind,
	/// cover or season; every map off until its rect, info or switch says it is set; the distance blends and the vegetation
	/// fade off while their bands are empty. The one exception is the ambient light, which at zero is black: it is set here
	/// once before the first scene loads, and the sky overwrites it every frame when it is installed.
	/// </para>
	/// </remarks>
	public static class WorldSurfaceDefaults
	{
		public static readonly int AmbientSkyId = Shader.PropertyToID("_FishAmbientSky");
		public static readonly int AmbientEquatorId = Shader.PropertyToID("_FishAmbientEquator");
		public static readonly int AmbientGroundId = Shader.PropertyToID("_FishAmbientGround");

		/// <summary>A mild overcast day's ambient, linear: what lights a plant or a falling flake with no sky installed.</summary>
		public static readonly Color AmbientSky = new Color(0.42f, 0.46f, 0.52f, 1f);
		public static readonly Color AmbientEquator = new Color(0.34f, 0.35f, 0.36f, 1f);
		public static readonly Color AmbientGround = new Color(0.16f, 0.15f, 0.13f, 1f);

		/// <summary>Sets the neutral values. The world modules set theirs after, every frame they run.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		public static void Apply()
		{
			Shader.SetGlobalColor(AmbientSkyId, AmbientSky);
			Shader.SetGlobalColor(AmbientEquatorId, AmbientEquator);
			Shader.SetGlobalColor(AmbientGroundId, AmbientGround);
		}
	}
}
