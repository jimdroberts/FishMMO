using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// Whether this device can read a texture back from the GPU synchronously (<c>Texture2D.ReadPixels</c>). WebGPU cannot:
	/// the call logs "Texture Readback is not supported by WebGPU" and reads nothing. The runtime's fallbacks for a scene
	/// missing its bake (the ground colour map's layer means, the grass's albedo copy) check this and do without, warning once
	/// with what is missing, rather than computing from an empty read.
	/// </summary>
	public static class SyncTextureReadback
	{
		private static bool warned;

		/// <summary>True where <c>ReadPixels</c> works.</summary>
		public static bool Supported => SystemInfo.graphicsDeviceType != GraphicsDeviceType.WebGPU;

		/// <summary>Logs, once a session, that <paramref name="what"/> was skipped for want of a readback.</summary>
		public static void WarnSkipped(string what)
		{
			if (warned)
			{
				return;
			}
			warned = true;
			Debug.LogWarning($"[Texture readback] {what} skipped: this device cannot read textures back synchronously (WebGPU). The scene is missing its terrain bake (Dashboard → Biomes → \"Bake terrain grass and scatter\"), which carries it precomputed.");
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics() => warned = false;
	}
}
