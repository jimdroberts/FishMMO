using FishNet.Broadcast;

namespace FishMMO.Shared
{
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
	/// Broadcast sent by the client to verify SRP authentication, containing salt and public ephemeral value.
	/// </summary>
	public struct SrpVerifyBroadcast : IBroadcast
	{
		/// <summary>SRP salt value (or encrypted username/email on client->server).</summary>
		public byte[] S;
		/// <summary>SRP public ephemeral value.</summary>
		public byte[] PublicEphemeral;

		/// <summary>Explicit message sequence number (client->server).</summary>
		public uint Seq;
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
}