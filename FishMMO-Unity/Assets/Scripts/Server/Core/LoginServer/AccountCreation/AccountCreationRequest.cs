using FishMMO.Server.Core.Account;

namespace FishMMO.Server.Core.LoginServer
{
	/// <summary>
	/// Immutable request data for async account creation processing.
	/// Contains ENCRYPTED credentials to avoid blocking network thread with decryption.
	/// Generic over connection type to maintain engine independence.
	/// </summary>
	/// <typeparam name="TConnection">The transport's connection representation.</typeparam>
	public readonly struct AccountCreationRequest<TConnection>
	{
		/// <summary>
		/// Network connection of the client requesting account creation.
		/// </summary>
		public readonly TConnection Connection;

		/// <summary>
		/// Encrypted username data (AES encrypted).
		/// </summary>
		public readonly byte[] EncryptedUsername;

		/// <summary>
		/// Encrypted SRP salt data (AES encrypted).
		/// </summary>
		public readonly byte[] EncryptedSalt;

		/// <summary>
		/// Encrypted SRP verifier data (AES encrypted).
		/// </summary>
		public readonly byte[] EncryptedVerifier;

		/// <summary>
		/// Per-connection encryption state holding the symmetric key, session prefix, and counters
		/// for deriving unique GCM nonces on the worker thread.
		/// </summary>
		public readonly ConnectionEncryptionData EncryptionData;

		/// <summary>
		/// IP address of the client for rate limiting and DoS protection.
		/// </summary>
		public readonly string IpAddress;

		/// <summary>
		/// Initializes a new account creation request with encrypted credentials.
		/// </summary>
		/// <param name="connection">Network connection of the client.</param>
		/// <param name="encryptedUsername">Encrypted username bytes.</param>
		/// <param name="encryptedSalt">Encrypted salt bytes.</param>
		/// <param name="encryptedVerifier">Encrypted verifier bytes.</param>
		/// <param name="encryptionData">Per-connection encryption state for nonce derivation.</param>
		/// <param name="ipAddress">IP address of the client.</param>
		public AccountCreationRequest(TConnection connection, byte[] encryptedUsername, byte[] encryptedSalt,
			byte[] encryptedVerifier, ConnectionEncryptionData encryptionData, string ipAddress)
		{
			Connection = connection;
			EncryptedUsername = encryptedUsername;
			EncryptedSalt = encryptedSalt;
			EncryptedVerifier = encryptedVerifier;
			EncryptionData = encryptionData;
			IpAddress = ipAddress;
		}
	}
}