namespace FishMMO.Auth.Implementation
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
		/// Encrypted username or email bytes (AES). Decryption deferred to worker thread.
		/// </summary>
		public readonly byte[] EncryptedUsername;

		/// <summary>
		/// Encrypted client public ephemeral bytes (AES). Decryption deferred to worker thread.
		/// </summary>
		public readonly byte[] EncryptedPublicEphemeral;

		/// <summary>
		/// Explicit client-sent sequence number (last sequence used for this broadcast).
		/// </summary>
		public readonly uint Seq;

		/// <summary>
		/// Per-connection encryption state holding the symmetric key, session prefix, and counters
		/// for deriving unique GCM nonces on the worker thread.
		/// </summary>
		public readonly ConnectionEncryptionData EncryptionData;

		/// <summary>
		/// Creates a new SRP verify request with encrypted data for deferred processing.
		/// </summary>
		/// <param name="connection">The originating network connection.</param>
		/// <param name="encryptedUsername">Encrypted username or email bytes.</param>
		/// <param name="encryptedPublicEphemeral">Encrypted client public ephemeral bytes.</param>
		/// <param name="encryptionData">Per-connection encryption state for nonce derivation.</param>
		/// <param name="seq">Explicit sequence number.</param>
		public SrpVerifyRequest(TConnection connection, byte[] encryptedUsername,
			byte[] encryptedPublicEphemeral,
			ConnectionEncryptionData encryptionData, uint seq)
		{
			Connection = connection;
			EncryptedUsername = encryptedUsername;
			EncryptedPublicEphemeral = encryptedPublicEphemeral;
			EncryptionData = encryptionData;
			Seq = seq;
		}
	}
}