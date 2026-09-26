namespace FishMMO.Server.Core.LoginServer
{
	/// <summary>
	/// Interface for login server runtime data that tracks the server's unique identifier.
	/// </summary>
	public interface ILoginServerRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Gets the unique ID of this login server instance.
		/// </summary>
		long ID { get; set; }

		/// <summary>
		/// Atomically transitions the pulse gate from idle to in-flight.
		/// Returns true if this call won the race; false if a pulse is already in flight.
		/// </summary>
		bool TryBeginPulse();

		/// <summary>
		/// Atomically transitions the pulse gate from in-flight back to idle.
		/// </summary>
		void EndPulse();
	}
}