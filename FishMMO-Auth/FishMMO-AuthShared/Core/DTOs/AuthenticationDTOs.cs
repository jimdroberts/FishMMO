namespace FishMMO.Auth.Core
{
	/// <summary>
	/// Transport-agnostic DTOs mirroring the FishNet authentication broadcast structs.
	/// These contain no engine or networking framework dependencies.
	/// </summary>
	/// 
	/// <summary>
	/// DTO for account creation, containing SRP username, salt, and verifier.
	/// Mirrors <c>CreateAccountBroadcast</c>.
	/// </summary>
	public struct CreateAccountDto
	{
		/// <summary>SRP username as a byte array.</summary>
		public byte[] Username;
		/// <summary>Encrypted email address (AES-GCM).</summary>
		public byte[] Email;
		/// <summary>Encrypted age value as UTF-8 string bytes (AES-GCM).</summary>
		public byte[] Age;
		/// <summary>SRP salt for password hashing.</summary>
		public byte[] Salt;
		/// <summary>SRP verifier for password authentication.</summary>
		public byte[] Verifier;
		/// <summary>Explicit message sequence number (client->server).</summary>
		public uint Seq;
	}

	/// <summary>
	/// DTO for client handshake initiation, containing the ephemeral
	/// X25519 public key for ECDH key agreement and supported protocol version range.
	/// Mirrors <c>ClientHandshake</c>.
	/// </summary>
	public struct ClientHandshakeDto
	{
		/// <summary>Client's ephemeral X25519 public key (32 bytes).</summary>
		public byte[] PublicKey;
		/// <summary>Stateless cookie echoed from a prior server challenge. Null on initial handshake.</summary>
		public byte[] Cookie;
		/// <summary>Minimum protocol version supported by this client.</summary>
		public ushort MinVersion;
		/// <summary>Maximum protocol version supported by this client.</summary>
		public ushort MaxVersion;
		/// <summary>Client game version string (e.g. "0.1.0"). Mirrors <c>ClientHandshake.GameVersion</c>.</summary>
		public string GameVersion;
	}

	/// <summary>
	/// DTO for server handshake response, containing the server's
	/// X25519 public key for ECDH key agreement and the negotiated protocol version.
	/// Mirrors <c>ServerHandshake</c>.
	/// </summary>
	public struct ServerHandshakeDto
	{
		/// <summary>Server's ephemeral X25519 public key (32 bytes). Null when this is a cookie challenge.</summary>
		public byte[] PublicKey;
		/// <summary>Stateless HMAC challenge cookie. Null when this is the final handshake response.</summary>
		public byte[] Cookie;
		/// <summary>Negotiated protocol version agreed during handshake.</summary>
		public ushort AgreedVersion;
	}

	/// <summary>
	/// DTO for SRP verify step, containing salt/identifier and public ephemeral value.
	/// Mirrors <c>SrpVerifyRequestBroadcast</c>.
	/// </summary>
	public struct SrpVerifyDto
	{
		/// <summary>SRP salt value (or encrypted username/email on client->server).</summary>
		public byte[] S;
		/// <summary>SRP public ephemeral value.</summary>
		public byte[] PublicEphemeral;
		/// <summary>Explicit message sequence number (client->server).</summary>
		public uint Seq;
	}

	/// <summary>
	/// DTO for SRP proof step, containing the proof value.
	/// Mirrors <c>SrpProofBroadcast</c>.
	/// </summary>
	public struct SrpProofDto
	{
		/// <summary>SRP proof value for authentication.</summary>
		public byte[] Proof;
		/// <summary>Explicit message sequence number (client->server).</summary>
		public uint Seq;
	}

	/// <summary>
	/// DTO for successful SRP authentication, containing proof and result.
	/// Mirrors <c>SrpSuccessBroadcast</c>.
	/// </summary>
	public struct SrpSuccessDto
	{
		/// <summary>SRP proof value for successful authentication.</summary>
		public byte[] Proof;
		/// <summary>Result of client authentication.</summary>
		public ClientAuthenticationResult Result;
		/// <summary>Explicit message sequence number (server->client).</summary>
		public uint Seq;
		/// <summary>Encrypted signed auth token for World/Scene server authentication. Null if not enabled.</summary>
		public byte[]? Token;
	}

	/// <summary>
	/// DTO for communicating the result of client authentication.
	/// Mirrors <c>ClientAuthResultBroadcast</c>.
	/// </summary>
	public struct ClientAuthResultDto
	{
		/// <summary>Result of client authentication.</summary>
		public ClientAuthenticationResult Result;
	}

	/// <summary>
	/// DTO for token-based authentication with a World or Scene server.
	/// Mirrors <c>TokenAuthBroadcast</c>.
	/// </summary>
	public struct TokenAuthDto
	{
		/// <summary>AES-GCM encrypted signed auth token.</summary>
		public byte[] Token;
		/// <summary>Explicit message sequence number (client->server).</summary>
		public uint Seq;
	}

	/// <summary>
	/// DTO for account verification using a verification code.
	/// Mirrors <c>AccountVerifyBroadcast</c>.
	/// </summary>
	public struct AccountVerifyDto
	{
		/// <summary>Encrypted account username (AES-GCM).</summary>
		public byte[] Username;
		/// <summary>Encrypted verification code as UTF-8 string bytes (AES-GCM).</summary>
		public byte[] VerifyCode;
		/// <summary>Explicit message sequence number (client->server).</summary>
		public uint Seq;
	}

	/// <summary>
	/// DTO for two-factor setup data sent after account creation.
	/// Mirrors <c>TwoFactorSetupBroadcast</c>.
	/// </summary>
	public struct TwoFactorSetupDto
	{
		/// <summary>Encrypted otpauth:// URI for authenticator app setup (AES-GCM, server->client).</summary>
		public byte[] OtpauthUri;
		/// <summary>Encrypted newline-delimited plaintext recovery codes (AES-GCM, server->client).</summary>
		public byte[] RecoveryCodes;
		/// <summary>Explicit message sequence number (server->client).
		/// The server calls NextSendSequence() twice sequentially: first for
		/// OtpauthUri (seq1), then for RecoveryCodes (seq2). Seq is set to seq2.
		/// The client also calls receiveNonceCtx.NextNonce() twice sequentially
		/// and does NOT read this Seq field for nonce derivation — it always
		/// uses the next two sequential receive nonces regardless of Seq.
		/// Both sides are consistent only because no other server→client messages
		/// are sent between the two encryptions in the registration flow.</summary>
		public uint Seq;
	}

	/// <summary>
	/// DTO for submitting a TOTP code during login when two-factor authentication is required.
	/// Mirrors <c>TwoFactorVerifyBroadcast</c>.
	/// </summary>
	public struct TwoFactorVerifyDto
	{
		/// <summary>Encrypted 6-digit TOTP code as UTF-8 string bytes (AES-GCM).</summary>
		public byte[] Code;
		/// <summary>Explicit message sequence number (client->server).</summary>
		public uint Seq;
	}
}