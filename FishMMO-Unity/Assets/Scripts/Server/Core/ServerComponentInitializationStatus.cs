namespace FishMMO.Server.Core
{
	/// <summary>
	/// Enumeration representing the initialization status of server components (behaviours, data containers, etc.).
	/// Generic status enum used across all server component types.
	/// </summary>
	public enum ServerComponentInitializationStatus
	{
		/// <summary>
		/// The server component has not been initialized.
		/// </summary>
		NotInitialized = 0,

		/// <summary>
		/// The server component has been successfully initialized.
		/// </summary>
		Initialized,

		/// <summary>
		/// The server component was already initialized.
		/// </summary>
		AlreadyInitialized,

		/// <summary>
		/// The server component failed to initialize.
		/// </summary>
		InitializationFailed,

		/// <summary>
		/// The server component failed to find the server instance.
		/// </summary>
		FailedToFindServer,

		/// <summary>
		/// The server component failed to find the server manager instance.
		/// </summary>
		FailedToFindServerManager,

		/// <summary>
		/// The server component failed to find a required dependency.
		/// </summary>
		FailedToFindRequiredDependency,

		/// <summary>
		/// The server component failed to get a data container.
		/// </summary>
		FailedToGetDataContainer,

		/// <summary>
		/// The server component failed to get a database context.
		/// </summary>
		FailedToGetDbContext,
	}
}