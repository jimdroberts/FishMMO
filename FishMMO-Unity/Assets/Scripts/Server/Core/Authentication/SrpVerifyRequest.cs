namespace FishMMO.Server.Core.Authentication
{
	/// <summary>
	/// Immutable request data for async SRP verification processing.
	/// Contains ENCRYPTED credentials to avoid blocking the network thread with decryption.
	/// Generic over connection type to maintain engine independence.
	/// </summary>
	/// <typeparam name="TConnection">The transport's connection representation.</typeparam>
	public readonly struct SrpVerifyRequest<TConnection>
	{
		/// <summary>
		/// The network connection that initiated the SRP verify handshake.
		/// </summary>
		public readonly TConnection Connection;

		/// <summary>
		/// Encrypted username bytes (AES). Decryption deferred to worker thread.
		/// </summary>
		public readonly byte[] EncryptedUsername;

		/// <summary>
		/// Encrypted client public ephemeral bytes (AES). Decryption deferred to worker thread.
		/// </summary>
		public readonly byte[] EncryptedPublicEphemeral;

		/// <summary>
		/// AES symmetric key for decrypting request data on the worker thread.
		/// </summary>
		public readonly byte[] SymmetricKey;

		/// <summary>
		/// AES initialization vector for decrypting request data on the worker thread.
		/// </summary>
		public readonly byte[] IV;

		/// <summary>
		/// Creates a new SRP verify request with encrypted data for deferred processing.
		/// </summary>
		/// <param name="connection">The originating network connection.</param>
		/// <param name="encryptedUsername">Encrypted username bytes.</param>
		/// <param name="encryptedPublicEphemeral">Encrypted client public ephemeral bytes.</param>
		/// <param name="symmetricKey">AES key for decryption.</param>
		/// <param name="iv">AES IV for decryption.</param>
		public SrpVerifyRequest(TConnection connection, byte[] encryptedUsername, byte[] encryptedPublicEphemeral,
			byte[] symmetricKey, byte[] iv)
		{
			Connection = connection;
			EncryptedUsername = encryptedUsername;
			EncryptedPublicEphemeral = encryptedPublicEphemeral;
			SymmetricKey = symmetricKey;
			IV = iv;
		}
	}
}