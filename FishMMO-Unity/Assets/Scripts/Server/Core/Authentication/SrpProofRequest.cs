namespace FishMMO.Server.Core.Authentication
{
	/// <summary>
	/// Immutable request data for async SRP proof processing.
	/// Contains encrypted proof and pre-validated account data to avoid blocking the network thread.
	/// Generic over connection type to maintain engine independence.
	/// </summary>
	/// <typeparam name="TConnection">The transport's connection representation.</typeparam>
	public readonly struct SrpProofRequest<TConnection>
	{
		/// <summary>
		/// The network connection that submitted the SRP proof.
		/// </summary>
		public readonly TConnection Connection;

		/// <summary>
		/// Encrypted client proof bytes (AES). Decryption deferred to worker thread.
		/// </summary>
		public readonly byte[] EncryptedClientProof;

		/// <summary>
		/// AES symmetric key for decrypting request data on the worker thread.
		/// </summary>
		public readonly byte[] SymmetricKey;

		/// <summary>
		/// AES initialization vector for decrypting request data on the worker thread.
		/// </summary>
		public readonly byte[] IV;

		/// <summary>
		/// Creates a new SRP proof request with encrypted data for deferred processing.
		/// </summary>
		/// <param name="connection">The originating network connection.</param>
		/// <param name="encryptedClientProof">Encrypted client proof bytes.</param>
		/// <param name="symmetricKey">AES key for decryption.</param>
		/// <param name="iv">AES IV for decryption.</param>
		public SrpProofRequest(TConnection connection, byte[] encryptedClientProof,
			byte[] symmetricKey, byte[] iv)
		{
			Connection = connection;
			EncryptedClientProof = encryptedClientProof;
			SymmetricKey = symmetricKey;
			IV = iv;
		}
	}
}