namespace FishMMO.Server.Core
{
	/// <summary>
	/// Represents the type of server being launched.
	/// Provides a type-safe way to handle different server behaviors.
	/// </summary>
	public enum ServerType
	{
		/// <summary>
		/// Invalid server type. Used as a default or error value.
		/// </summary>
		Invalid = 0,

		/// <summary>
		/// Login server type. Handles authentication and account management.
		/// </summary>
		Login,

		/// <summary>
		/// World server type. Manages the persistent world state and player interactions.
		/// </summary>
		World,

		/// <summary>
		/// Scene server type. Handles individual scenes or instances within the world.
		/// </summary>
		Scene,
	}
}