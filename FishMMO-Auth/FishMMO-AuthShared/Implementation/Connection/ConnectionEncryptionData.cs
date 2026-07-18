using System.Security.Cryptography;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// Holds per-connection encryption state including directional AES keys, nonce contexts,
	/// and negotiated protocol version. Keys are established via X25519 ECDH key agreement
	/// during the handshake phase.
	/// <para>Thread-safety: nonce contexts use <c>Interlocked</c> internally for counter operations.</para>
	/// </summary>
	public class ConnectionEncryptionData
	{
		/// <summary>
		/// The client's X25519 public key received during the handshake.
		/// </summary>
		public byte[]? PublicKey;

		/// <summary>
		/// Master secret derived via X25519 ECDH + HKDF. Null after promotion to directional keys.
		/// </summary>
		public byte[]? MasterSecret;

		/// <summary>
		/// Directional AES keys derived via HKDF-SHA256. <c>ClientToServerKey</c> is used to decrypt client→server messages.
		/// <c>ServerToClientKey</c> is used to encrypt server→client messages.
		/// </summary>
		public byte[]? ClientToServerKey;
		public byte[]? ServerToClientKey;

		/// <summary>
		/// Nonce context for server→client (send/encrypt) direction.
		/// Owns the server prefix and send counter. Null until <see cref="PromoteToDirectional"/> is called.
		/// </summary>
		public CryptoHelper.GcmNonceContext? SendNonceCtx;

		/// <summary>
		/// Nonce context for client→server (receive/decrypt) direction.
		/// Owns the client prefix and receive counter. Null until <see cref="PromoteToDirectional"/> is called.
		/// </summary>
		public CryptoHelper.GcmNonceContext? ReceiveNonceCtx;

		/// <summary>
		/// Negotiated protocol version for this connection.
		/// Set during the handshake and used in AAD construction for all subsequent messages.
		/// Defaults to <see cref="CryptoHelper.ProtocolVersion"/> for backward compatibility.
		/// </summary>
		public ushort AgreedVersion;

		/// <summary>
		/// Initializes a new instance storing the peer's public key.
		/// Directional keys and nonce contexts remain null until <see cref="PromoteToDirectional"/> is called
		/// after the X25519 ECDH key agreement completes.
		/// </summary>
		/// <param name="publicKey">The peer's X25519 public key (32 bytes).</param>
		public ConnectionEncryptionData(byte[] publicKey)
		{
			PublicKey = publicKey;
			MasterSecret = null;
			ClientToServerKey = null;
			ServerToClientKey = null;
			SendNonceCtx = null;
			ReceiveNonceCtx = null;
			AgreedVersion = CryptoHelper.ProtocolVersion;
		}

		/// <summary>
		/// Builds the next server→client GCM nonce and advances the send counter.
		/// </summary>
		/// <returns>Tuple of (12-byte nonce, sequence number).</returns>
		public (byte[] Nonce, uint Sequence) NextSendNonce()
		{
			return SendNonceCtx!.NextNonce();
		}

		/// <summary>
		/// Atomically increments and returns the next send sequence number without
		/// building a nonce. Use when the sequence is needed for AAD construction
		/// and the nonce will be built separately via <see cref="BuildSendNonce"/>.
		/// </summary>
		/// <returns>The next send sequence number.</returns>
		public uint NextSendSequence()
		{
			return SendNonceCtx!.NextSequenceOnly();
		}

		/// <summary>
		/// Builds the next client→server GCM nonce and advances the receive counter.
		/// </summary>
		/// <returns>Tuple of (12-byte nonce, sequence number).</returns>
		public (byte[] Nonce, uint Sequence) NextReceiveNonce()
		{
			return ReceiveNonceCtx!.NextNonce();
		}

		/// <summary>
		/// Attempts to consume an expected receive sequence number.
		/// Returns <c>true</c> and advances the counter if <paramref name="seq"/> is exactly
		/// one greater than the current counter. Returns <c>false</c> for duplicates or gaps.
		/// </summary>
		/// <param name="seq">The expected incoming sequence number to consume.</param>
		/// <returns><c>true</c> if consumed; <c>false</c> otherwise.</returns>
		public bool TryConsumeReceiveSequence(uint seq)
		{
			return ReceiveNonceCtx!.TryConsumeSequence(seq);
		}

		/// <summary>
		/// Atomically consumes a contiguous receive-sequence range
		/// <c>[baseSeq, baseSeq + count - 1]</c>. Use when a single logical message
		/// spans multiple wire-level sequence numbers (e.g. multi-field encrypted
		/// payloads) so that a partial failure cannot leave the counter advanced
		/// part-way through.
		/// </summary>
		/// <param name="baseSeq">First sequence in the range.</param>
		/// <param name="count">Number of consecutive sequences; must be &gt; 0.</param>
		/// <returns><c>true</c> on success; <c>false</c> on duplicate/gap/exhaustion.</returns>
		public bool TryConsumeReceiveSequenceRange(uint baseSeq, uint count)
		{
			return ReceiveNonceCtx!.TryConsumeSequenceRange(baseSeq, count);
		}

		/// <summary>
		/// Builds a receive-direction nonce for a specific sequence number.
		/// Use after <see cref="TryConsumeReceiveSequence"/> has validated the sequence.
		/// </summary>
		public byte[] BuildReceiveNonce(uint seq)
		{
			return ReceiveNonceCtx!.BuildNonceForSequence(seq);
		}

		/// <summary>
		/// Builds a send-direction nonce for a specific sequence number.
		/// Use when the sequence was obtained from <see cref="NextSendSequence"/>.
		/// </summary>
		public byte[] BuildSendNonce(uint seq)
		{
			return SendNonceCtx!.BuildNonceForSequence(seq);
		}

		/// <summary>
		/// Zeroes all sensitive key material and disposes nonce contexts.
		/// Call before removing the entry from the <c>AccountManager</c>.
		/// </summary>
		public void Clear()
		{
			if (MasterSecret != null)
			{
				CryptographicOperations.ZeroMemory(MasterSecret);
				MasterSecret = null;
			}
			if (ClientToServerKey != null)
			{
				CryptographicOperations.ZeroMemory(ClientToServerKey);
				ClientToServerKey = null;
			}
			if (ServerToClientKey != null)
			{
				CryptographicOperations.ZeroMemory(ServerToClientKey);
				ServerToClientKey = null;
			}
			SendNonceCtx?.Dispose();
			SendNonceCtx = null;
			ReceiveNonceCtx?.Dispose();
			ReceiveNonceCtx = null;
			if (PublicKey != null)
			{
				// Zero the peer public key before dropping the reference. Public keys
				// are not secret cryptographically, but their byte buffer may have been
				// pooled or share a backing array with other handshake state, and an
				// attacker with read-only memory access could otherwise correlate this
				// session’s key bytes against transport captures.
				CryptographicOperations.ZeroMemory(PublicKey);
				PublicKey = null;
			}
		}

		/// <summary>
		/// Promotes the stored state into directional keys and nonce contexts using derived session keys.
		/// Creates <see cref="GcmNonceContext"/> instances that own their respective prefixes
		/// and enforce nonce uniqueness per direction.
		/// </summary>
		public void PromoteToDirectional(CryptoHelper.SessionKeys keys)
		{
			// Assign derived directional AES keys
			ClientToServerKey = keys.ClientToServerKey;
			ServerToClientKey = keys.ServerToClientKey;

			// Create nonce contexts that own copies of the session prefixes.
			// Server send = server→client direction, uses ServerPrefix
			// Server receive = client→server direction, uses ClientPrefix
			SendNonceCtx = new CryptoHelper.GcmNonceContext(keys.ServerPrefix, CryptoHelper.NonceSide.ServerToClient);
			ReceiveNonceCtx = new CryptoHelper.GcmNonceContext(keys.ClientPrefix, CryptoHelper.NonceSide.ClientToServer);

			// Zero and drop master secret
			if (MasterSecret != null)
			{
				CryptographicOperations.ZeroMemory(MasterSecret);
				MasterSecret = null;
			}
		}

		/// <summary>
		/// Creates a snapshot suitable for async worker processing.
		/// Key material is shared (read-only after derivation); nonce contexts
		/// are SHARED (not cloned) — <see cref="GcmNonceContext"/> is already
		/// thread-safe via <see cref="Interlocked"/> for all counter operations.
		/// Sharing ensures that all workers processing messages for the same
		/// connection observe a single monotonic sequence space, preventing both
		/// receive-sequence rejection and catastrophic send-nonce reuse.
		/// </summary>
		public ConnectionEncryptionData CloneForAsyncWorker()
		{
			var clone = new ConnectionEncryptionData(PublicKey!)
			{
				MasterSecret = MasterSecret,
				ClientToServerKey = ClientToServerKey,
				ServerToClientKey = ServerToClientKey,
				AgreedVersion = AgreedVersion,
				// Share nonce contexts — they are thread-safe.
				// Never call Clear() on the clone; the original owns disposal.
				SendNonceCtx = SendNonceCtx,
				ReceiveNonceCtx = ReceiveNonceCtx,
			};
			return clone;
		}
	}
}