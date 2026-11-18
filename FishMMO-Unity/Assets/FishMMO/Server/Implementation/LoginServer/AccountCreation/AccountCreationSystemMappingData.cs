using FishMMO.Server.Core;
using FishMMO.Server.Core.LoginServer;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Mapping data for per-IP rate limiting and DoS protection.
	/// Tracks IP addresses and their request/failure history.
	/// Thread-safe: accessed from both network and worker threads.
	/// </summary>
	public class AccountCreationSystemMappingData : RuntimeDataContainer, IAccountCreationSystemMappingData
	{
		/// <inheritdoc/>
		public IpAbuseTracker IpAbuse { get; private set; }

		/// <summary>
		/// Initializes the mapping data container with an empty tracker.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			IpAbuse = new IpAbuseTracker();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all mapping data entries. Does not null the reference, since the tracker may be
		/// accessed from other threads during runtime.
		/// </summary>
		public override void Clear()
		{
			IpAbuse?.Clear();
		}

		/// <summary>
		/// Deinitializes the mapping data container and releases references.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
			IpAbuse = null;
		}
	}
}