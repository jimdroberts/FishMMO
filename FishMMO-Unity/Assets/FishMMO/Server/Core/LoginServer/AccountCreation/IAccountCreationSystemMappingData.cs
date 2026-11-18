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
		/// Per-IP creation-attempt rate limit and failure block, expired in activity order.
		/// See <see cref="IpAbuseTracker"/> for the rules.
		/// </summary>
		IpAbuseTracker IpAbuse { get; }
	}
}