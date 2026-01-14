namespace FishMMO.Server.Core.LoginServer
{
	/// <summary>
	/// Engine-agnostic public API for account creation system.
	/// Handles player account creation requests, validates credentials,
	/// and stores new accounts in the database.
	/// </summary>
	public interface IAccountCreationSystem : IServerBehaviour
	{
	}
}