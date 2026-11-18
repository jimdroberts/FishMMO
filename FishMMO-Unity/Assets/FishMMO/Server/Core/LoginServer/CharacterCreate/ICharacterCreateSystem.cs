namespace FishMMO.Server.Core.LoginServer
{
	/// <summary>
	/// Engine-agnostic public API for character creation system.
	/// Manages character creation for player accounts, validates character data,
	/// and initializes starting equipment and abilities.
	/// </summary>
	public interface ICharacterCreateSystem : IServerBehaviour
	{
		/// <summary>
		/// Maximum number of characters allowed per account.
		/// </summary>
		int MaxCharacters { get; }
	}
}