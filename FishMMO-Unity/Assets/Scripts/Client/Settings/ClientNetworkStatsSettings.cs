using System;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Whether the network statistics overlay is drawn, and whether it draws its traffic graph.
	/// </summary>
	/// <remarks>
	/// <para><b>Off by default.</b> The overlay is a diagnostic: a player who has never asked how
	/// much the game sends has no use for a table of byte counts in the corner of the screen, and
	/// a fresh install should look the way it did before the overlay existed.</para>
	///
	/// <para><b>Why the settings live here rather than on the panel.</b> For the reason
	/// <see cref="ClientCrosshairSettings"/> gives: <see cref="UITKNetworkStats"/> is a panel whose
	/// <c>OnStarting</c> can re-run, and the options panel must be able to read and write the
	/// choice in a scene where the overlay has not started yet. These are plain reads off
	/// <see cref="ClientSettings"/>, so neither side has to wait for the other.</para>
	///
	/// <para><b>Visibility is the only thing the setting owns.</b> Sampling follows it: the
	/// overlay reads the transport's counters only while it is shown, so turning it off also
	/// stops the once-a-second connection statistics read the transport makes on its behalf
	/// (see <c>TransportTraffic.Capture</c>).</para>
	/// </remarks>
	public static class ClientNetworkStatsSettings
	{
		/// <summary>Whether a fresh install draws the overlay.</summary>
		public const bool DefaultEnabled = false;

		/// <summary>Whether a fresh install draws the traffic graph inside the overlay.</summary>
		/// <remarks>
		/// On, because a player who has turned the overlay on has asked to see the traffic, and
		/// the graph is the part that shows a spike after it has passed. It is a separate choice
		/// for the player who wants the numbers in less screen space.
		/// </remarks>
		public const bool DefaultShowGraph = true;

		/// <summary>
		/// Raised when either setting changes.
		/// </summary>
		/// <remarks>
		/// The overlay subscribes; the options panel raises. Neither holds a reference to the
		/// other, which matters because the options panel can be open while the overlay's tree has
		/// not been built.
		/// </remarks>
		public static event Action OnChanged;

		/// <summary>Whether the overlay is drawn at all.</summary>
		public static bool Enabled => ClientSettings.GetBool(ClientSettings.NetworkStatsEnabledKey, DefaultEnabled);

		/// <summary>Whether the overlay draws its traffic graph.</summary>
		public static bool ShowGraph => ClientSettings.GetBool(ClientSettings.NetworkStatsGraphKey, DefaultShowGraph);

		/// <summary>Writes whether the overlay is drawn and notifies it.</summary>
		public static void SetEnabled(bool value)
		{
			ClientSettings.Set(ClientSettings.NetworkStatsEnabledKey, value);
			Raise();
		}

		/// <summary>Writes whether the traffic graph is drawn and notifies the overlay.</summary>
		public static void SetShowGraph(bool value)
		{
			ClientSettings.Set(ClientSettings.NetworkStatsGraphKey, value);
			Raise();
		}

		/// <summary>Notifies subscribers, reporting rather than propagating a handler's failure.</summary>
		private static void Raise()
		{
			try
			{
				OnChanged?.Invoke();
			}
			catch (Exception ex)
			{
				Log.Error("ClientNetworkStatsSettings", "A network-statistics-settings subscriber threw.", ex);
			}
		}
	}
}
