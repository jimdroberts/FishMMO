using FishMMO.Database.Data;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Runtime data container for kick request processing state.
	/// Tracks where this server's poll of the kick requests has got to.
	/// </summary>
	public interface IKickRequestSystemQueueData : IRuntimeDataContainer
	{
		/// <summary>
		/// Indicates whether a kick request fetch is currently in progress.
		/// </summary>
		bool IsProcessing { get; set; }

		/// <summary>
		/// Where the poll has got to and what it has handled, on the database clock. Touched only
		/// by the poll that holds <see cref="IsProcessing"/>.
		/// </summary>
		KickRequestReadWindow ReadWindow { get; }

		/// <summary>
		/// When this server began watching for kicks, in <see cref="MonotonicClock"/> seconds. A
		/// first read reaches back this long before the database's "now", so a kick issued before
		/// the first successful read is not skipped.
		/// </summary>
		double WatchStartedAt { get; }
	}
}
