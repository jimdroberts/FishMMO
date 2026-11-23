using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// The server tick "now" means for world state every peer derives from the shared clock: tides, weather, world motion.
	/// </summary>
	/// <remarks>
	/// Core: movement samples water at this tick, and the water contract
	/// must not depend on the weather module to know what time it is. Set by the server host or the client mirror; 0 when
	/// nothing has set it. <c>WeatherQuery.TickSource</c> forwards here.
	/// </remarks>
	public static class WorldTick
	{
		/// <summary>The tick source, or null.</summary>
		public static Func<uint> Source;

		/// <summary>The current tick, or 0.</summary>
		public static uint Current => Source != null ? Source() : 0u;
	}
}
