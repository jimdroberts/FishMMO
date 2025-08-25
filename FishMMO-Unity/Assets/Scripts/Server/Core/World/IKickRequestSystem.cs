namespace FishMMO.Server.Core.World
{
	/// <summary>
	/// Engine-agnostic public API for the kick request processing system on a world server.
	/// Provides configuration surface and allows other systems to query or modify
	/// runtime pump parameters without referencing the implementation.
	/// </summary>
	public interface IKickRequestSystem : IServerBehaviour
	{
		/// <summary>
		/// The update/poll rate in seconds used when checking the database for kick requests.
		/// </summary>
		float UpdatePumpRate { get; set; }

		/// <summary>
		/// Maximum number of kick requests fetched per poll.
		/// </summary>
		int UpdateFetchCount { get; set; }
	}
}