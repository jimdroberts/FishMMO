using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>Native routing for procedural vegetation shadow draws already clipped by punctual shadow shaders.</summary>
	public static class DirectionalShadowDraws
	{
		public const string LayerName = "DirectionalShadowOnly";
		public static bool Enabled = true;
		private static int cachedLayer = -2;
		public static int Layer => cachedLayer == -2 ? cachedLayer = LayerMask.NameToLayer(LayerName) : cachedLayer;

		/// <summary>Only the shadow-only draw changes layer; its lit draw keeps its original illumination masks.</summary>
		public static int LayerFor(Camera camera, Light sun, int original)
		{
			int layer = Layer;
			if (!Enabled || layer < 0 || camera == null || sun == null || sun.type != LightType.Directional) return original;
			int bit = 1 << layer;
			// Preserve customized camera/sun masks by retaining the original path unless both allow the routed draw.
			if ((camera.cullingMask & bit) == 0 || (camera.cullingMask & (1 << original)) == 0 || (sun.cullingMask & bit) == 0 || (sun.cullingMask & (1 << original)) == 0) return original;
			return layer;
		}

		/// <summary>The reserved layer has no lit geometry: excluding it removes only the already-empty shadow passes.</summary>
		public static void ConfigurePunctualLight(Light light)
		{
			if (Layer >= 0 && light != null && (light.type == LightType.Point || light.type == LightType.Spot))
				light.cullingMask &= ~(1 << Layer);
		}
	}
}
