using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// A renderer whose static shadow casters the cached main-light shadows (<see cref="CachedMainLightShadows"/>) draw into
	/// their cache: the GPU-driven terrain and props.
	/// </summary>
	/// <remarks>
	/// The cache is core rendering; what it caches belongs to whichever world module draws it. A caster joins
	/// <see cref="ShadowCacheCasters"/> while it is live, so the cache iterates casters, never a module's renderer type
	/// (Phase 4 of the core and modules plan, 2026-11-17).
	/// </remarks>
	public interface IShadowCacheCaster
	{
		/// <summary>Whether this caster has static casters for the cache this frame.</summary>
		bool ServesShadowCache { get; }

		/// <summary>A version of the static content; the cache redraws when any caster's changes.</summary>
		int ShadowContentVersion { get; }

		/// <summary>Culls this caster's static casters against a cache window.</summary>
		void RecordShadowCacheCull(CommandBuffer cmd, Camera camera, Vector3 anchor, Vector4[] planes, Vector4 light);

		/// <summary>Draws this caster's static casters into the current cache tile; returns the draws issued.</summary>
		int RecordStaticShadowCasters(RasterCommandBuffer cmd);
	}

	/// <summary>The live <see cref="IShadowCacheCaster"/>s, and the device capabilities GPU-driven drawing shares.</summary>
	public static class ShadowCacheCasters
	{
		private static readonly List<IShadowCacheCaster> live = new List<IShadowCacheCaster>();

		/// <summary>Every live caster, in registration order.</summary>
		public static IReadOnlyList<IShadowCacheCaster> Live => live;

		/// <summary>Adds a caster while it is live; safe to call twice.</summary>
		public static void Register(IShadowCacheCaster caster)
		{
			if (caster != null && !live.Contains(caster))
			{
				live.Add(caster);
			}
		}

		/// <summary>Removes a caster.</summary>
		public static void Unregister(IShadowCacheCaster caster)
		{
			live.Remove(caster);
		}

		/// <summary>Diagnostics: how often a caster's coverage forced the cache to redraw for new shadow work.</summary>
		public static int CoverageRedraws { get; set; }

		/// <summary>
		/// Whether the instance index carries a run's start on this API, so one indirect call can draw many commands and the
		/// cached casters' procedural variants work. Must match FishIndirectSetup's backend/feature branches.
		/// </summary>
		public static bool SupportsInstanceIdBase(GraphicsDeviceType api, bool indirectFirstInstance)
			=> api == GraphicsDeviceType.Vulkan || (api == GraphicsDeviceType.WebGPU && indirectFirstInstance);

		/// <summary><see cref="SupportsInstanceIdBase"/> for this device.</summary>
		public static bool InstanceIdCarriesBase => SupportsInstanceIdBase(SystemInfo.graphicsDeviceType,
			SystemInfo.graphicsDeviceType == GraphicsDeviceType.WebGPU && WebGpuDrawCapabilities.IndirectFirstInstance);
	}
}
