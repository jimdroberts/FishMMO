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
		/// <summary>
		/// The server is locked for maintenance and is not accepting this account.
		/// </summary>
		/// <remarks>
		/// Distinct from <see cref="ServerFull"/>, which it used to be reported as. "Full" tells
		/// the player to try again shortly, which is exactly wrong for a lock: capacity clears on
		/// its own, a maintenance lock does not. A locked world still admits accounts above
		/// <c>AccessLevel.Player</c>, so receiving this also means the account is an ordinary
		/// player one.
		/// </remarks>
		ServerLocked = 21,
		/// <summary>
		/// The password was proven, but the phone number the account chose to verify with has not
		/// been. The client must enter the code sent to the phone by SMS.
		/// </summary>
		/// <remarks>
		/// The SMS counterpart of <see cref="AccountUnverified"/>, which keeps meaning "the email
		/// code is outstanding". An account that chose both channels is asked for the email code
		/// first; a correct email code then answers with this value rather than
		/// <see cref="AccountVerified"/>, so the client knows to ask for the second code. Like
		/// <see cref="AccountUnverified"/>, it is only ever sent after a correct SRP proof or a
		/// correct verification code, so it reveals nothing to someone who has neither.
		/// </remarks>
		PhoneUnverified = 22,
		/// <summary>
		/// The server is in a closed test and this account holds no beta access. Sent only after a
		/// correct SRP proof, never before, so it cannot be used to learn which accounts exist.
		/// </summary>
		/// <remarks>
		/// Staff accounts (GameMaster and above) are never refused this way. A player can redeem a
		/// beta code in the Control Panel and sign in again.
		/// </remarks>
		BetaAccessRequired = 23,
		/// <summary>
		/// Two-factor sign-in for this account is locked after repeated wrong codes. The password
		/// was already proven, so the lock is reported explicitly; the broadcast carries how long
		/// it has left. The client must end the attempt rather than prompt for another code.
		/// </summary>
		TwoFactorLocked = 24,
		/// <summary>
		/// Account creation was refused because the server is in a closed test and the beta code
		/// was missing or could not be redeemed. One answer for every bad-code case — unknown,
		/// revoked, expired, used up, malformed — so a script cannot tell a real code from a guess.
		/// </summary>
		BetaCodeInvalid = 25,
		/// <summary>
		/// Account creation was refused because an optional detail (phone number, real name,
		/// country, address, referral account or the verification choice) failed the server's
		/// rules. The client validates the same rules first, so this is normally unreachable.
		/// </summary>
		AccountDetailsInvalid = 26,
	}
}
