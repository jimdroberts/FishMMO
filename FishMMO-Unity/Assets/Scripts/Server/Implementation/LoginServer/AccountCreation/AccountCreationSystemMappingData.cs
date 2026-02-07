using System;
using System.Collections.Concurrent;
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
		/// <summary>
		/// Tracks last account creation attempt per IP address for rate limiting.
		/// </summary>
		public ConcurrentDictionary<string, DateTime> IpRateLimitTracker { get; private set; }

		/// <summary>
		/// Tracks number of failed attempts per IP for DoS detection.
		/// </summary>
		public ConcurrentDictionary<string, int> IpFailureTracker { get; private set; }

		/// <summary>
		/// Initializes the mapping data container with empty concurrent dictionaries.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			IpRateLimitTracker = new ConcurrentDictionary<string, DateTime>();
			IpFailureTracker = new ConcurrentDictionary<string, int>();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all mapping data and releases references.
		/// </summary>
		public override void Clear()
		{
			IpRateLimitTracker?.Clear();
			IpRateLimitTracker = null;
			IpFailureTracker?.Clear();
			IpFailureTracker = null;
		}

		/// <summary>
		/// Deinitializes the mapping data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
		}
	}
}