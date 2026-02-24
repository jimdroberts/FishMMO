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
		/// <summary>SRP salt for password hashing.</summary>
		public byte[] Salt;
		/// <summary>SRP verifier for password authentication.</summary>
		public byte[] Verifier;
	}

	/// <summary>
	/// Broadcast sent by the client to initiate a handshake, containing the public key.
	/// </summary>
	public struct ClientHandshake : IBroadcast
	{
		/// <summary>Client's public key for encryption handshake.</summary>
		public byte[] PublicKey;
	}

	/// <summary>
	/// Broadcast sent by the server to complete the handshake, containing the encrypted
	/// symmetric key and session prefix for counter-based GCM nonce derivation.
	/// </summary>
	public struct ServerHandshake : IBroadcast
	{
		/// <summary>Server's encryption key (RSA-encrypted AES-256 key).</summary>
		public byte[] Key;
		/// <summary>RSA-encrypted 4-byte session prefix for GCM nonce derivation.</summary>
		public byte[] SessionPrefix;
	}

	/// <summary>
	/// Broadcast sent by the client to verify SRP authentication, containing salt and public ephemeral value.
	/// </summary>
	public struct SrpVerifyBroadcast : IBroadcast
	{
		/// <summary>SRP salt value.</summary>
		public byte[] S;
		/// <summary>SRP public ephemeral value.</summary>
		public byte[] PublicEphemeral;
	}

	/// <summary>
	/// Broadcast sent by the client to prove SRP authentication, containing the proof value.
	/// </summary>
	public struct SrpProofBroadcast : IBroadcast
	{
		/// <summary>SRP proof value for authentication.</summary>
		public byte[] Proof;
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
	}

	/// <summary>
	/// Broadcast sent by the server to communicate the result of client authentication.
	/// </summary>
	public struct ClientAuthResultBroadcast : IBroadcast
	{
		/// <summary>Result of client authentication.</summary>
		public ClientAuthenticationResult Result;
	}
}