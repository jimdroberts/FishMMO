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
		AccountCreated = 0,
		/// <summary>
		/// SRP verification step required.
		/// </summary>
		SrpVerify = 1,
		/// <summary>
		/// SRP proof step required.
		/// </summary>
		SrpProof = 2,
		/// <summary>
		/// Username or password is invalid.
		/// </summary>
		InvalidUsernameOrPassword = 3,
		/// <summary>
		/// Account is already online and cannot log in again.
		/// </summary>
		AlreadyOnline = 4,
		/// <summary>
		/// Account is banned and cannot log in.
		/// </summary>
		Banned = 5,
		/// <summary>
		/// Login was successful.
		/// </summary>
		LoginSuccess = 6,
		/// <summary>
		/// Login to the world server was successful.
		/// </summary>
		WorldLoginSuccess = 7,
		/// <summary>
		/// Login to the scene was successful.
		/// </summary>
		SceneLoginSuccess = 8,
		/// <summary>
		/// Server is full and cannot accept new connections.
		/// </summary>
		ServerFull = 9,
		/// <summary>
		/// Server is busy and cannot process the request at this time.
		/// </summary>
		ServerBusy = 10,
		/// <summary>
		/// No character is selected on the account. The client must select a character before connecting to a world server.
		/// </summary>
		NoCharacterSelected = 11,
		/// <summary>
		/// The authentication token is invalid (malformed, bad signature, or not found).
		/// </summary>
		TokenInvalid = 12,
		/// <summary>
		/// The authentication token has expired.
		/// </summary>
		TokenExpired = 13,
		/// <summary>
		/// The authentication token has been revoked.
		/// </summary>
		TokenRevoked = 14,
		/// <summary>
		/// Account email has not been verified. The user must enter the verification code sent during registration.
		/// </summary>
		AccountUnverified = 15,
		/// <summary>
		/// Account has been successfully verified with the correct verification code.
		/// </summary>
		AccountVerified = 16,
		/// <summary>
		/// Login requires TOTP two-factor authentication. The client must provide a valid TOTP code.
		/// </summary>
		TwoFactorRequired = 17,
		/// <summary>
		/// The submitted TOTP code was invalid or has already been used (anti-replay).
		/// </summary>
		TwoFactorInvalid = 18,
		/// <summary>
		/// Client-only: Auth token decryption failed after otherwise-successful SRP login.
		/// The user is authenticated at the Login server but will be unable to connect to
		/// World/Scene servers until re-authenticating. Not sent over the wire.
		/// </summary>
		TokenDecryptFailed = 19,
		/// <summary>
		/// Server rejected the client because the game version does not match.
		/// The client must update (or downgrade) to match the server's version.
		/// </summary>
		VersionMismatch = 20,
	}
}
