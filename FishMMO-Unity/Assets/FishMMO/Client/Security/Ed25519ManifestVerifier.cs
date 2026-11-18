using System;
using System.Text;
using FishMMO.Logging;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace FishMMO.Client.Security
{
	/// <summary>
	/// Verifies an Ed25519 signature carried inside a JSON document, against a public key
	/// embedded in the client at build time.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Extracted from <see cref="ApiPinUpdateSidecar"/>, which had the only implementation. It
	/// is shared now because a second manifest needs exactly the same treatment: the version
	/// manifest from <c>/latest_version</c> carries the SHA-256 the patch download is checked
	/// against, and was itself unsigned — the project's own TODO, and the finding (S4) that made
	/// the traversal and integrity issues around it reachable in the first place.
	/// </para>
	/// <para>
	/// <b>What signing buys that TLS does not.</b> The manifest already travels over a pinned
	/// TLS connection, so this is not about a passive observer. It is about the fact that
	/// everything downstream trusts the manifest completely: the SHA-256 in it is the ONLY
	/// integrity check on the patch archive, so whoever writes the manifest chooses which bytes
	/// the updater will accept. TLS authenticates the transport; it says nothing about a
	/// compromised or misconfigured gateway, a mis-issued certificate, or a CDN edge with a
	/// stale/hostile copy. A signature moves the trust anchor from "whoever is answering on
	/// this host" to "whoever holds the release key" — which is the property the patch pipeline
	/// actually needs, because it is the same property that makes the SHA-256 worth checking.
	/// </para>
	/// <para>
	/// <b>The canonical form</b> is the document with the value of its <c>"signature"</c> field
	/// replaced by an empty string — specifically, rewritten to the exact text
	/// <see cref="BlankSignatureField"/>, so the signed bytes do not depend on which spacing the
	/// serialiser used. It is deliberately
	/// textual rather than a re-serialisation: re-serialising means the verifier and the signer
	/// must agree on key order, spacing and escaping, and any disagreement is either a failure
	/// to verify a good manifest or — far worse — a way to alter a field without disturbing what
	/// gets hashed. Operating on the exact bytes received removes that whole class of question.
	/// This is the same construction the pin manifest already used, kept identical so one
	/// signing tool serves both.
	/// </para>
	/// </remarks>
	public static class Ed25519ManifestVerifier
	{
		private const string logChannel = "Ed25519ManifestVerifier";

		/// <summary>Ed25519 public keys are exactly 32 bytes.</summary>
		private const int PublicKeyLength = 32;

		/// <summary>Ed25519 signatures are exactly 64 bytes.</summary>
		private const int SignatureLength = 64;

		/// <summary>
		/// Decodes and validates a base64 Ed25519 public key.
		/// </summary>
		/// <param name="publicKeyBase64">The embedded key, or null/empty when not configured.</param>
		/// <param name="publicKey">The 32-byte key on success.</param>
		/// <returns>True when a usable key was configured.</returns>
		public static bool TryDecodePublicKey(string publicKeyBase64, out byte[] publicKey)
		{
			publicKey = null;

			if (string.IsNullOrEmpty(publicKeyBase64))
			{
				return false; // not configured; the caller decides what that means
			}

			try
			{
				publicKey = Convert.FromBase64String(publicKeyBase64);
			}
			catch (Exception ex)
			{
				_ = Log.Warning(logChannel, $"Manifest public key is not valid base64: {ex.Message}");
				publicKey = null;
				return false;
			}

			if (publicKey.Length != PublicKeyLength)
			{
				_ = Log.Warning(logChannel,
					$"Manifest public key is {publicKey.Length} bytes (expected {PublicKeyLength} for Ed25519).");
				publicKey = null;
				return false;
			}

			return true;
		}

		/// <summary>
		/// Verifies <paramref name="signatureBase64"/> over <paramref name="fullJson"/>.
		/// </summary>
		/// <param name="publicKey">32-byte Ed25519 public key.</param>
		/// <param name="fullJson">The raw JSON document exactly as received, signature field included.</param>
		/// <param name="signatureBase64">The base64 signature value taken from that document.</param>
		/// <returns>True only when the signature verifies. Every other outcome is false.</returns>
		/// <remarks>
		/// Fails closed by construction: there is no path through this method that returns true
		/// without <c>VerifySignature</c> having returned true. Malformed input, a decode
		/// failure and a cryptographic mismatch are all simply "false" — the caller must not be
		/// able to tell them apart and act differently, because the correct action is identical.
		/// </remarks>
		public static bool Verify(byte[] publicKey, string fullJson, string signatureBase64)
		{
			if (publicKey == null || publicKey.Length != PublicKeyLength)
			{
				return false;
			}
			if (string.IsNullOrEmpty(fullJson) || string.IsNullOrEmpty(signatureBase64))
			{
				_ = Log.Warning(logChannel, "Manifest carries no signature.");
				return false;
			}

			byte[] signatureBytes;
			try
			{
				signatureBytes = Convert.FromBase64String(signatureBase64);
			}
			catch
			{
				_ = Log.Warning(logChannel, "Manifest signature is not valid base64.");
				return false;
			}

			if (signatureBytes.Length != SignatureLength)
			{
				_ = Log.Warning(logChannel,
					$"Signature is {signatureBytes.Length} bytes (expected {SignatureLength} for Ed25519).");
				return false;
			}

			string message = BuildCanonicalSignedMessage(fullJson, signatureBase64);
			if (message == null)
			{
				// BuildCanonicalSignedMessage has already said why.
				return false;
			}

			byte[] messageBytes = Encoding.UTF8.GetBytes(message);

			try
			{
				Ed25519Signer signer = new Ed25519Signer();
				signer.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
				signer.BlockUpdate(messageBytes, 0, messageBytes.Length);
				return signer.VerifySignature(signatureBytes);
			}
			catch (Exception ex)
			{
				_ = Log.Warning(logChannel, $"Ed25519 verification error: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// The exact text the signature field is normalised to before signing/verifying.
		/// </summary>
		/// <remarks>
		/// A single constant, deliberately. The verifier accepts either <c>":"</c> or <c>": "</c>
		/// spacing on the wire but always rewrites the field to THIS form before hashing, so the
		/// signed bytes do not depend on which serialiser produced the document. A signer that
		/// emits the placeholder verbatim, signs, and then substitutes the base64 value into it
		/// reproduces these bytes exactly.
		/// </remarks>
		public const string BlankSignatureField = "\"signature\": \"\"";

		/// <summary>
		/// Produces the canonical signed message: the document with the value of its
		/// <c>"signature"</c> field replaced by an empty string.
		/// </summary>
		/// <returns>The canonical message, or null when the signature field could not be located.</returns>
		/// <remarks>
		/// <para>
		/// Returning null on a missing field is a behaviour change from the version this was
		/// extracted from, and it is a security fix rather than tidying. That version fell back
		/// to signing the raw JSON plus the signature and left a warning in the log — a fallback
		/// with an entirely different canonical form, which means a signer could produce a
		/// document that verifies down the fallback path while the field the verifier believes
		/// it checked is not the field it hashed. A verifier that cannot find what it is
		/// verifying has not verified anything; it says so and fails.
		/// </para>
		/// <para>
		/// <b>The signature is NOT appended to the message, and this is the second correction
		/// carried in here.</b> Both this method and the <c>ApiPinUpdateSidecar</c> version it
		/// came from used to return <c>stripped + signatureBase64</c> — a message containing the
		/// very signature being verified. Producing one requires solving
		/// <c>sig = Sign(sk, stripped || base64(sig))</c>, i.e. a fixed point of a hash-driven
		/// function over a 64-byte value: Ed25519 derives <c>R</c> from <c>H(prefix || M)</c>, so
		/// any change to <c>M</c> re-randomises the entire signature and a fixed point costs
		/// roughly 2^256 work to find. No signer could ever satisfy it, which is why the scheme
		/// survived unnoticed — nothing in the tree has ever signed either manifest, so the
		/// verifier had never been handed a document that was supposed to pass. The signature is
		/// not needed in the message in any case: it is a signature *over* the document, and the
		/// blanked field already pins the field's presence and position.
		/// </para>
		/// <para>
		/// Both spacings are tried because JSON serialisers differ on whether a colon is
		/// followed by a space, and the document is compared as received rather than reformatted.
		/// The LAST occurrence is used: the signature value is 88 base64 characters and will not
		/// appear elsewhere by accident, but if a document did contrive to repeat it, the field
		/// itself is conventionally last.
		/// </para>
		/// </remarks>
		public static string BuildCanonicalSignedMessage(string json, string signatureBase64)
		{
			if (json == null || string.IsNullOrEmpty(signatureBase64))
			{
				return null;
			}

			string search = "\"signature\": \"" + signatureBase64 + "\"";
			int idx = json.LastIndexOf(search, StringComparison.Ordinal);
			if (idx < 0)
			{
				search = "\"signature\":\"" + signatureBase64 + "\"";
				idx = json.LastIndexOf(search, StringComparison.Ordinal);
			}

			if (idx < 0)
			{
				_ = Log.Warning(logChannel,
					"Could not locate the signature field in the manifest JSON; refusing to verify against a guessed canonical form.");
				return null;
			}

			return json.Substring(0, idx) + BlankSignatureField +
				json.Substring(idx + search.Length);
		}
	}
}
