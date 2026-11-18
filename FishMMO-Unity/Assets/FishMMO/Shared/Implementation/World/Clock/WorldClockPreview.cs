using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// A preview holding the world clock (test scenes and editor previews; never on a server), in place of the shared clock.
	/// </summary>
	/// <remarks>
	/// Core, so <see cref="WorldMotion"/> can follow a preview without knowing what drives it. The sky
	/// (<c>WorldDayNightCycle</c>) owns the preview and writes both values: its <c>PreviewHours</c> and <c>SkyClockHours</c>
	/// are these.
	/// </remarks>
	public static class WorldClockPreview
	{
		/// <summary>A world time to show instead of the clock, hours; null when no preview holds it.</summary>
		public static double? Hours;

		/// <summary>
		/// The world clock in hours as the sky last read it (a preview's fast or stopped clock included), or null before
		/// any sky has run. While a preview holds the clock, the world's motion runs on this.
		/// </summary>
		public static double? ShownHours { get; set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetOnPlay()
		{
			ShownHours = null;
		}
	}
}
