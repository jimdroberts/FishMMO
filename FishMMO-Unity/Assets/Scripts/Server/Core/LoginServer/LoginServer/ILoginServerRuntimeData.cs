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
	}
}
