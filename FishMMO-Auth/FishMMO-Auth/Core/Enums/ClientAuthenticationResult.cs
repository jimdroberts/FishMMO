namespace FishMMO.Auth.Core
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
		/// <summary>
		/// Server is busy and cannot process the request at this time.
		/// </summary>
		ServerBusy,
		/// <summary>
		/// No character is selected on the account. The client must select a character before connecting to a world server.
		/// </summary>
		NoCharacterSelected,
		/// <summary>
		/// The authentication token is invalid (malformed, bad signature, or not found).
		/// </summary>
		TokenInvalid,
		/// <summary>
		/// The authentication token has expired.
		/// </summary>
		TokenExpired,
		/// <summary>
		/// The authentication token has been revoked.
		/// </summary>
		TokenRevoked,
		/// <summary>
		/// Account email has not been verified. The user must enter the verification code sent during registration.
		/// </summary>
		AccountUnverified,
		/// <summary>
		/// Account has been successfully verified with the correct verification code.
		/// </summary>
		AccountVerified,
		/// <summary>
		/// Login requires TOTP two-factor authentication. The client must provide a valid TOTP code.
		/// </summary>
		TwoFactorRequired,
		/// <summary>
		/// The submitted TOTP code was invalid or has already been used (anti-replay).
		/// </summary>
		TwoFactorInvalid,
	}
}