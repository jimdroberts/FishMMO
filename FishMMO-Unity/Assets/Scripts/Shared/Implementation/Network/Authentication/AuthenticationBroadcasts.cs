using FishNet.Broadcast;
using FishMMO.Auth.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast sent by the client during an explicit logout to ask the LoginServer to
	/// revoke its currently-held auth token before its TTL expires. The token is sent
	/// as the raw HMAC-signed bytes the LoginServer originally issued (and which the
	/// client decrypted into memory on SrpSuccess). The server hashes the bytes via
	/// <c>TokenService.HashToken</c> and matches the row in <c>IAuthTokenService</c>.
	///
	/// Security note: this broadcast is sent over the FishNet transport without
	/// additional encryption; the server's AES-GCM auth channel has typically been
	/// torn down by the time the user logs out. Because the only purpose is to
	/// revoke the token the eavesdropper would have captured anyway, this is safe.
	/// </summary>
	public struct RevokeTokenBroadcast : IBroadcast
	{
		/// <summary>Raw (HMAC-signed) auth token bytes the LoginServer originally issued.</summary>
		public byte[] Token;
	}

	/// <summary>
	/// Broadcast sent by the client to create a new account, containing SRP username, salt, and verifier.
	/// </summary>
	public struct CreateAccountBroadcast : IBroadcast
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
	/// Broadcast sent by the client to initiate a handshake, containing the ephemeral
	/// X25519 public key for ECDH key agreement and supported protocol version range.
	/// </summary>
	public struct ClientHandshake : IBroadcast
	{
		/// <summary>Client's ephemeral X25519 public key (32 bytes).</summary>
		public byte[] PublicKey;

		/// <summary>
		/// Stateless cookie echoed from a prior <see cref="ServerHandshake"/> challenge.
		/// Null on the initial handshake; set when retrying after a cookie challenge.
		/// </summary>
		public byte[] Cookie;

		/// <summary>
		/// One-time connection token from the IPFetch HTTP API. Used by the Login
		/// Server to recover the real client IP that was lost through the L4 UDP proxy.
		/// Null or empty for World/Scene server reconnections (token auth path).
		/// </summary>
		public string ConnectionToken;

		/// <summary>
		/// Minimum protocol version supported by this client.
		/// </summary>
		public ushort MinVersion;

		/// <summary>
		/// Maximum protocol version supported by this client.
		/// </summary>
		public ushort MaxVersion;
	}

	/// <summary>
	/// Broadcast sent by the server to complete the handshake, containing the server's
	/// X25519 public key for ECDH key agreement and the negotiated protocol version.
	/// </summary>
	public struct ServerHandshake : IBroadcast
	{
		/// <summary>
		/// Server's ephemeral X25519 public key (32 bytes).
		/// Null when this message is a cookie challenge (proof-of-reachability).
		/// </summary>
		public byte[] PublicKey;

		/// <summary>
		/// Stateless HMAC challenge cookie. The client must echo this in a subsequent
		/// <see cref="ClientHandshake"/> to prove it can receive replies from the server.
		/// Null when this message contains the final handshake response.
		/// </summary>
		public byte[] Cookie;

		/// <summary>
		/// Negotiated protocol version agreed during handshake.
		/// Both sides use this version in AAD and HKDF labels for all subsequent messages.
		/// </summary>
		public ushort AgreedVersion;
	}

	/// <summary>
	/// Broadcast sent by the client to initiate SRP authentication (client→server only).
	/// Carries the encrypted username/email and the client's SRP public ephemeral A.
	/// </summary>
	public struct SrpVerifyRequestBroadcast : IBroadcast
	{
		/// <summary>Encrypted username or email (AES-GCM).</summary>
		public byte[] Username;
		/// <summary>Encrypted SRP client public ephemeral A (AES-GCM).</summary>
		public byte[] PublicEphemeral;

		/// <summary>Explicit message sequence number (client→server).</summary>
		public uint Seq;
	}

	/// <summary>
	/// Broadcast sent by the server in response to SRP verification (server→client only).
	/// Carries the encrypted SRP salt s and the server's SRP public ephemeral B.
	/// </summary>
	public struct SrpVerifyResponseBroadcast : IBroadcast
	{
		/// <summary>Encrypted SRP salt s (AES-GCM).</summary>
		public byte[] Salt;
		/// <summary>Encrypted SRP server public ephemeral B (AES-GCM).</summary>
		public byte[] PublicEphemeral;
	}

	/// <summary>
	/// Broadcast sent by the client to prove SRP authentication, containing the proof value.
	/// </summary>
	public struct SrpProofBroadcast : IBroadcast
	{
		/// <summary>SRP proof value for authentication.</summary>
		public byte[] Proof;

		/// <summary>Explicit message sequence number (client->server).</summary>
		public uint Seq;
	}

	/// <summary>
	/// Broadcast sent by the server to indicate successful SRP authentication, containing proof and result.
	/// </summary>
	public struct SrpSuccessBroadcast : IBroadcast
	{
		/// <summary>SRP proof value for successful authentication.</summary>
		public byte[] Proof;
		/// <summary>Result of client authentication.</summary>
		public ClientAuthenticationResult Result;

		/// <summary>Explicit message sequence number (server->client).</summary>
		public uint Seq;

		/// <summary>Encrypted signed auth token for World/Scene server authentication. Null if token issuance is not enabled.</summary>
		public byte[] Token;
	}

	/// <summary>
	/// Broadcast sent by the server to communicate the result of client authentication.
	/// </summary>
	public struct ClientAuthResultBroadcast : IBroadcast
	{
		/// <summary>Result of client authentication.</summary>
		public ClientAuthenticationResult Result;
	}

	/// <summary>
	/// Broadcast sent by the client to authenticate with a World or Scene server using
	/// a signed token issued by the LoginServer after SRP success.
	/// </summary>
	public struct TokenAuthBroadcast : IBroadcast
	{
		/// <summary>AES-GCM encrypted signed auth token.</summary>
		public byte[] Token;

		/// <summary>Explicit message sequence number (client->server).</summary>
		public uint Seq;
	}

	/// <summary>
	/// Broadcast sent by a World or Scene server immediately after a successful
	/// <see cref="TokenAuthBroadcast"/> authentication, carrying a freshly-minted
	/// auth token with a refreshed expiration window. The client decrypts the new
	/// token over the existing AES-GCM session channel and replaces its stored
	/// token so that future reconnects continue working past the original
	/// LoginServer-issued token's expiration.
	/// </summary>
	public struct RenewTokenResponseBroadcast : IBroadcast
	{
		/// <summary>AES-GCM encrypted signed auth token (server-&gt;client).</summary>
		public byte[] Token;

		/// <summary>Explicit message sequence number (server-&gt;client).</summary>
		public uint Seq;
	}

	/// <summary>
	/// Broadcast sent by the client to verify an account using a verification code
	/// received after account creation.
	/// </summary>
	public struct AccountVerifyBroadcast : IBroadcast
	{
		/// <summary>Encrypted account username (AES-GCM).</summary>
		public byte[] Username;
		/// <summary>Encrypted verification code as UTF-8 string bytes (AES-GCM).</summary>
		public byte[] VerifyCode;

		/// <summary>Explicit message sequence number (client->server).</summary>
		public uint Seq;
	}

	/// <summary>
	/// Broadcast sent by the server after account creation containing encrypted TOTP
	/// setup data (otpauth URI and recovery codes) for two-factor authentication.
	/// </summary>
	public struct TwoFactorSetupBroadcast : IBroadcast
	{
		/// <summary>Encrypted otpauth:// URI for authenticator app setup (AES-GCM, server->client).</summary>
		public byte[] OtpauthUri;
		/// <summary>Encrypted newline-delimited plaintext recovery codes (AES-GCM, server->client).</summary>
		public byte[] RecoveryCodes;

		/// <summary>
		/// Explicit message sequence number (server->client).
		///
		/// NOTE: The client does NOT use Seq-1 / Seq derivation for these
		/// two fields. Instead, it calls receiveNonceCtx.NextNonce() twice
		/// sequentially — first for the OtpauthUri, then for the
		/// RecoveryCodes.  Consequently, the server MUST send the URI
		/// before the recovery codes (i.e. the order of the fields in the
		/// struct defines the nonce order on the wire).  The Seq value
		/// itself is only used for replay-window tracking, not for
		/// computing per-field nonces.
		/// </summary>
		public uint Seq;
	}

	/// <summary>
	/// Broadcast sent by the client to submit a TOTP code during login when
	/// two-factor authentication is required.
	/// </summary>
	public struct TwoFactorVerifyBroadcast : IBroadcast
	{
		/// <summary>Encrypted 6-digit TOTP code as UTF-8 string bytes (AES-GCM).</summary>
		public byte[] Code;

		/// <summary>Explicit message sequence number (client->server).</summary>
		public uint Seq;
	}
}