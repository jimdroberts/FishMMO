using FishNet.Broadcast;
using FishMMO.Auth.Core;
using System.Runtime.InteropServices;

namespace FishMMO.Shared
{
	/// <para>
	/// <b>Byte Array Ownership Contract:</b> All <c>byte[]</c> fields on broadcast
	/// structs in this file contain cryptographically sensitive data (public keys,
	/// salts, verifiers, proofs, tokens). FishNet serializes these broadcasts on
	/// its network thread. Callers MUST NOT mutate or zeroize any byte array after
	/// assigning it to a broadcast struct field until the send operation has fully
	/// completed. The recommended pattern is:
	/// <list type="number">
	/// <item>Create the byte array.</item>
	/// <item>Assign it to the broadcast struct field.</item>
	/// <item>Call the appropriate FishNet send method.</item>
	/// <item>Zeroize the source array via <c>CryptographicOperations.ZeroMemory</c>.</item>
	/// </list>
	/// For the avoidance of doubt: these structs do <b>not</b> defensively copy
	/// byte arrays in constructors — they store the caller's reference directly
	/// for zero-allocation performance on hot auth paths.
	/// </para>
	/// <para>
	/// <b>Multi-Field Struct Field Order:</b> Structs in this file with multiple fields
	/// rely on FishNet's declaration-order serialization. <b>Do not reorder fields.</b>
	/// FishNet's serializer reads and writes fields in declaration order, which is the
	/// actual ordering guarantee — <b>not</b> CLR <c>StructLayout</c>. For non-blittable
	/// structs (those containing reference-type fields like <c>byte[]</c>), the CLR
	/// ignores <c>LayoutKind.Sequential</c> entirely and may reorder fields, especially
	/// under IL2CPP AOT compilation.  Consequently, no struct in this file carries
	/// <c>[StructLayout(LayoutKind.Sequential)]</c> — the attribute would be misleading
	/// because it provides no actual ordering guarantee for types with reference-type
	/// fields. Field ordering is solely enforced by convention and code review.
	/// </para>

	/// <summary>
	/// Size limits for authentication broadcast fields.
	/// Used by server-side validation to reject oversized payloads before any crypto work.
	/// </summary>
	public static class AuthSizeLimits
	{
		/// <summary>
		/// Maximum allowed size in bytes for the client's X25519 public key.
		/// X25519 keys are 32 bytes; limit is 64 to allow for future algorithm changes.
		/// </summary>
		public const int MaxPublicKeySize = 64;

		/// <summary>
		/// Maximum allowed size in bytes for the stateless cookie in ClientHandshake.
		/// </summary>
		public const int MaxCookieSize = 128;

		/// <summary>
		/// Maximum allowed length for the ConnectionToken string in ClientHandshake.
		/// </summary>
		public const int MaxConnectionTokenLength = 512;

		/// <summary>
		/// Maximum allowed length for the GameVersion string in ClientHandshake.
		/// </summary>
		public const int MaxGameVersionLength = 64;
		/// <summary>
		/// Maximum allowed size in bytes for any single encrypted field in CreateAccountBroadcast.
		/// </summary>
		public const int CreateAccountMaxFieldSize = 2048;

		/// <summary>
		/// Maximum allowed size in bytes for the encrypted Username field in SrpVerifyRequestBroadcast.
		/// AES-GCM overhead (~28 bytes) + encrypted username (max 128 bytes plaintext) = generous cap at 512.
		/// </summary>
		public const int MaxSrpUsernameSize = 512;

		/// <summary>
		/// Maximum allowed size in bytes for the encrypted PublicEphemeral field (SRP client A or server B).
		/// SRP-6a 4096-bit ephemeral (512 bytes) + AES-GCM overhead (~28 bytes) = generous cap at 1024.
		/// </summary>
		public const int MaxSrpEphemeralSize = 1024;

		/// <summary>
		/// Maximum allowed size in bytes for the encrypted Proof field (SrpProofBroadcast).
		/// SRP-6a client proof M1 = 64 bytes (SHA-512) + AES-GCM overhead (~28 bytes) = generous cap at 256.
		/// </summary>
		public const int MaxSrpProofSize = 256;

		/// <summary>
		/// Maximum allowed size in bytes for the encrypted Salt field (SrpVerifyResponseBroadcast).
		/// SRP-6a salt (max 64 bytes) + AES-GCM overhead (~28 bytes) = generous cap at 256.
		/// </summary>
		public const int MaxSrpSaltSize = 256;

		/// <summary>
		/// Maximum allowed size in bytes for the encrypted Token field in TokenAuthBroadcast.
		/// AES-GCM encrypted signed auth token = generous cap at 1024.
		/// </summary>
		public const int MaxTokenAuthSize = 1024;

		/// <summary>
		/// Maximum allowed size in bytes for the encrypted Code field in TwoFactorVerifyBroadcast.
		/// Encrypted 6-digit TOTP code + AES-GCM overhead (~28 bytes) = generous cap at 128.
		/// </summary>
		public const int MaxTotpCodeSize = 128;
	}

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

		/// <summary>
		/// Client game version string (e.g. "0.1.0"). Sent in plaintext so the server
		/// can reject mismatched clients before starting the expensive ECDH key agreement.
		/// Empty if the version is unavailable (development builds).
		/// </summary>
		public string GameVersion;
	}

	/// <summary>
	/// Broadcast sent by the server to complete the handshake, containing the server's
	/// X25519 public key for ECDH key agreement and the negotiated protocol version.
	/// </summary>
	/// <summary>
	/// Broadcast sent by the server to complete the handshake, containing the server's
	/// X25519 public key for ECDH key agreement and the negotiated protocol version.
	/// </summary>
	/// <remarks>
	/// <para><see cref="PublicKey"/> and <see cref="Cookie"/> are mutually exclusive:
	/// <list type="bullet">
	/// <item><description>A cookie challenge has <c>PublicKey == null</c> and <c>Cookie != null</c>.
	/// Use <see cref="IsChallenge"/> to test for this.</description></item>
	/// <item><description>The final handshake response has <c>PublicKey != null</c> and <c>Cookie == null</c>.
	/// Use <see cref="IsHandshakeResponse"/> to test for this.</description></item>
	/// </list>
	/// Consumers MUST use the helper properties instead of null-checking directly,
	/// so that any future wire-format changes remain isolated to this struct.
	/// </para>
	/// </remarks>
	public struct ServerHandshake : IBroadcast
	{
		/// <summary>
		/// Server's ephemeral X25519 public key (32 bytes).
		/// Null when this message is a cookie challenge (proof-of-reachability).
		/// Use <see cref="IsChallenge"/> or <see cref="IsHandshakeResponse"/> to discriminate.
		/// </summary>
		public byte[] PublicKey;

		/// <summary>
		/// Stateless HMAC challenge cookie. The client must echo this in a subsequent
		/// <see cref="ClientHandshake"/> to prove it can receive replies from the server.
		/// Null when this message contains the final handshake response.
		/// Use <see cref="IsChallenge"/> or <see cref="IsHandshakeResponse"/> to discriminate.
		/// </summary>
		public byte[] Cookie;

		/// <summary>
		/// Negotiated protocol version agreed during handshake.
		/// Both sides use this version in AAD and HKDF labels for all subsequent messages.
		/// </summary>
		public ushort AgreedVersion;

		/// <summary>
		/// Gets whether this message is a cookie challenge (proof-of-reachability).
		/// Mutually exclusive with <see cref="IsHandshakeResponse"/>.
		/// </summary>
		public bool IsChallenge => PublicKey == null && Cookie != null;

		/// <summary>
		/// Gets whether this message is the final handshake response (ECDH key exchange).
		/// Mutually exclusive with <see cref="IsChallenge"/>.
		/// </summary>
		public bool IsHandshakeResponse => PublicKey != null && Cookie == null;
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

		/// <summary>Explicit message sequence number (server→client).
		/// Echoed from the corresponding <see cref="SrpVerifyRequestBroadcast.Seq"/>
		/// so the client can correlate the response with its request.</summary>
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
	/// Broadcast sent by a World or Scene server immediately after a successful
	/// <see cref="TokenAuthBroadcast"/> authentication, carrying a freshly-minted
	/// auth token with a refreshed expiration window. The client decrypts the new
	/// token over the existing AES-GCM session channel and replaces its stored
	/// token so that future reconnects continue working past the original
	/// LoginServer-issued token's expiration.
	/// </summary>
	public struct RenewTokenResponseBroadcast : IBroadcast
	{
		/// <summary>AES-GCM encrypted signed auth token (server->client).</summary>
		public byte[] Token;

		/// <summary>Result of the token renewal operation. Client should check this field
		/// to distinguish success from failure before using the new token.</summary>
		public ClientAuthenticationResult Result;

		/// <summary>Explicit message sequence number (server->client).</summary>
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
	///
	/// <para>
	/// WARNING: The nonce-derivation scheme on both client and server depends on C#
	/// struct field declaration order matching FishNet serialization order.  The
	/// server sends OtpauthUri first, then RecoveryCodes, and the client consumes
	/// them in that exact sequence via <c>receiveNonceCtx.NextNonce()</c>.  If these
	/// fields are reordered, the nonce streams desynchronize and TOTP silently breaks.
	/// DO NOT reorder <see cref="OtpauthUri"/> and <see cref="RecoveryCodes"/>.
	/// </para>
	/// </summary>
	/// <para><b>WARNING: Wire-protocol-dependent field order.</b>
	/// FishNet serializes struct fields in declaration order regardless of CLR layout.
	/// The actual order guarantee comes from FishNet's serializer, not the CLR.
	/// <c>[StructLayout(LayoutKind.Sequential)]</c> is present for documentation purposes only:
	/// for non-blittable structs (those with <c>byte[]</c> fields), the CLR ignores
	/// Sequential layout and IL2CPP may reorder fields anyway. The attribute
	/// would be misleading at runtime. See the file-level doc comment for details.
	/// The nonce-derivation protocol is therefore fragile and should ideally use an
	/// explicit tagging scheme rather than declaration order.
	/// TODO: Migrate to an explicit tagging scheme (e.g., a tagged union or per-field
	/// nonce labels) to eliminate the declaration-order dependency.
	/// Reordering these fields WILL break TOTP setup. DO NOT reorder.</para>
	[StructLayout(LayoutKind.Sequential)] // Documentation only -- actual ordering is enforced by FishNet declaration-order serializer
	public struct TwoFactorSetupBroadcast : IBroadcast
	{
		/// <summary>
		/// Encrypted otpauth:// URI for authenticator app setup (AES-GCM, server->client).
		///
		/// WARNING: FIELD ORDER IS WIRE PROTOCOL -- DO NOT REORDER.
		/// The nonce-derivation scheme depends on field declaration order
		/// matching FishNet serialization order. See struct remarks.
		///
		/// WARNING: Declaration order IS the wire protocol (see struct remarks).
		/// This field MUST remain declared before <see cref="RecoveryCodes"/>.
		/// </summary>
		// WARNING: Field order is wire-protocol critical. DO NOT reorder.
		public byte[] OtpauthUri;
		/// <summary>
		/// Encrypted newline-delimited plaintext recovery codes (AES-GCM, server->client).
		///
		/// WARNING: Declaration order IS the wire protocol (see struct remarks).
		/// This field MUST remain declared after <see cref="OtpauthUri"/>.
		/// </summary>
		// WARNING: Field order is wire-protocol critical. DO NOT reorder.
		public byte[] RecoveryCodes;

		/// <summary>
		/// Explicit message sequence number (server->client).
		///
		/// NOTE: Seq is declared LAST. The nonce derivation calls NextNonce()
		/// for byte-array fields first, then uses Seq for replay tracking.
		/// Reordering fields will desynchronize the nonce stream.
		///
		/// The client does NOT use Seq-1 / Seq derivation for these
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