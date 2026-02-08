using System;
using System.Collections.Concurrent;

namespace FishMMO.Server.Core.LoginServer
{
	/// <summary>
	/// Mapping data for per-IP rate limiting and DoS protection.
	/// Tracks IP addresses and their request/failure history.
	/// Thread-safe: accessed from both network and worker threads.
	/// </summary>
	public interface IAccountCreationSystemMappingData : IRuntimeDataContainer
	{
		/// <summary>
		/// Tracks last account creation attempt per IP address for rate limiting.
		/// Key: IP Address, Value: Last attempt timestamp (UTC).
		/// </summary>
		ConcurrentDictionary<string, DateTime> IpRateLimitTracker { get; }

		/// <summary>
		/// Tracks number of failed attempts per IP for DoS detection and blocking.
		/// Key: IP Address, Value: Failed attempt count.
		/// </summary>
		ConcurrentDictionary<string, int> IpFailureTracker { get; }
	}
}