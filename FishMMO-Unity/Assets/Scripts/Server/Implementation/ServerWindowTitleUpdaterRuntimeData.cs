
namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Runtime data container for ServerWindowTitleUpdater mutable state.
	/// Stores the transient window title string and countdown timer
	/// separate from the ServerBehaviour configuration.
	/// </summary>
	public class ServerWindowTitleUpdaterRuntimeData : RuntimeDataContainer
	{
		/// <summary>
		/// The current window or console title string, rebuilt each update cycle.
		/// </summary>
		public string Title { get; set; }

		/// <summary>
		/// Time remaining in seconds until the next window title update.
		/// </summary>
		public float NextUpdate { get; set; }

		/// <summary>
		/// Initializes the runtime data with default values.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			Title = "";
			NextUpdate = 0.0f;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the runtime data to default values.
		/// </summary>
		public override void Clear()
		{
			Title = "";
			NextUpdate = 0.0f;
		}

		/// <summary>
		/// Deinitializes the runtime data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
		}
	}
}