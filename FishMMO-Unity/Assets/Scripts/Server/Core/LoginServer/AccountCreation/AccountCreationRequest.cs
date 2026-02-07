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
		/// AES symmetric key for decryption (from connection encryption data).
		/// </summary>
		public readonly byte[] SymmetricKey;

		/// <summary>
		/// AES initialization vector for decryption (from connection encryption data).
		/// </summary>
		public readonly byte[] IV;

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
		/// <param name="symmetricKey">AES symmetric key for decryption.</param>
		/// <param name="iv">AES initialization vector for decryption.</param>
		/// <param name="ipAddress">IP address of the client.</param>
		public AccountCreationRequest(TConnection connection, byte[] encryptedUsername, byte[] encryptedSalt,
			byte[] encryptedVerifier, byte[] symmetricKey, byte[] iv, string ipAddress)
		{
			Connection = connection;
			EncryptedUsername = encryptedUsername;
			EncryptedSalt = encryptedSalt;
			EncryptedVerifier = encryptedVerifier;
			SymmetricKey = symmetricKey;
			IV = iv;
			IpAddress = ipAddress;
		}
	}
}