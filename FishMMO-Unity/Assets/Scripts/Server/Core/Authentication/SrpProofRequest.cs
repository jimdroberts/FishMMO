using FishMMO.Server.Core.Account;

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
		/// Per-connection encryption state holding the symmetric key, session prefix, and counters
		/// for deriving unique GCM nonces on the worker thread.
		/// </summary>
		public readonly ConnectionEncryptionData EncryptionData;

		/// <summary>
		/// Creates a new SRP proof request with encrypted data for deferred processing.
		/// </summary>
		/// <param name="connection">The originating network connection.</param>
		/// <param name="encryptedClientProof">Encrypted client proof bytes.</param>
		/// <param name="encryptionData">Per-connection encryption state for nonce derivation.</param>
		public SrpProofRequest(TConnection connection, byte[] encryptedClientProof,
			ConnectionEncryptionData encryptionData)
		{
			Connection = connection;
			EncryptedClientProof = encryptedClientProof;
			EncryptionData = encryptionData;
		}
	}
}