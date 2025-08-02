namespace FishMMO.Shared
{
	/// <summary>
	/// Enum representing possible outcomes of client authentication attempts.
	/// Used to communicate authentication status and errors to the client.
	/// </summary>
	public enum ClientAuthenticationResult : byte
	{
		/// <summary>
		/// Account was successfully created.
		/// </summary>
		AccountCreated,
		/// <summary>
		/// SRP verification step required.
		/// </summary>
		SrpVerify,
		/// <summary>
		/// SRP proof step required.
		/// </summary>
		SrpProof,
		/// <summary>
		/// Username or password is invalid.
		/// </summary>
		InvalidUsernameOrPassword,
		/// <summary>
		/// Account is already online and cannot log in again.
		/// </summary>
		AlreadyOnline,
		/// <summary>
		/// Account is banned and cannot log in.
		/// </summary>
		Banned,
		/// <summary>
		/// Login was successful.
		/// </summary>
		LoginSuccess,
		/// <summary>
		/// Login to the world server was successful.
		/// </summary>
		WorldLoginSuccess,
		/// <summary>
		/// Login to the scene was successful.
		/// </summary>
		SceneLoginSuccess,
		/// <summary>
		/// Server is full and cannot accept new connections.
		/// </summary>
		ServerFull,
	}
}