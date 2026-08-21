using System.Text;
using System.Text.Json.Nodes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace FishMMO.WebServers.Signing
{
	/// <summary>
	/// Ed25519 signing of JSON manifests, in the exact canonical form the FishMMO client
	/// verifies. Deliberately free of ASP.NET, DI and logging dependencies so the shipping
	/// server, the operator's offline signing tool and the round-trip test harness all compile
	/// the same source file rather than three transcriptions of the same rules.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The canonical form.</b> The signed message is the JSON document with the value of its
	/// <c>"signature"</c> field replaced by an empty string — the field itself stays where it is,
	/// rewritten to the exact text <see cref="BlankSignatureField"/>. Nothing is appended. The
	/// reference implementation is
	/// <c>FishMMO.Client.Security.Ed25519ManifestVerifier.BuildCanonicalSignedMessage</c>; this
	/// file mirrors it, and <c>Tools/ManifestSigning.Tests</c> proves the mirror by compiling the
	/// *actual client verifier source* and round-tripping documents produced here through it.
	/// </para>
	/// <para>
	/// <b>Why textual and not a re-serialisation.</b> The verifier operates on the exact bytes it
	/// received. If it re-serialised instead, signer and verifier would have to agree on key
	/// order, number formatting, spacing and string escaping, and any disagreement is either a
	/// good manifest that will not verify or — much worse — a field an attacker can alter without
	/// disturbing what actually gets hashed. Working on the received bytes removes that whole
	/// class of question, at the cost of requiring the signer to emit the document itself rather
	/// than hand an object to a serialiser. <see cref="ManifestJsonWriter"/> is that emitter.
	/// </para>
	/// <para>
	/// <b>The signature is not part of its own message.</b> Both this file and the client verifier
	/// used to end the canonical message with the base64 signature appended. That is unsatisfiable:
	/// it asks for <c>sig = Sign(sk, stripped || base64(sig))</c>, a fixed point of a hash-driven
	/// function over 64 bytes, which Ed25519 makes cost about 2^256 to find because <c>R</c> is
	/// derived from <c>H(prefix || M)</c>. It went unnoticed for as long as it did precisely
	/// because nothing in the tree had ever signed a manifest, so the verifier was never handed a
	/// document that was supposed to pass.
	/// </para>
	/// </remarks>
	public static class ManifestSigning
	{
		/// <summary>Ed25519 private keys (seeds) are exactly 32 bytes.</summary>
		public const int PrivateKeySeedLength = 32;

		/// <summary>Ed25519 public keys are exactly 32 bytes.</summary>
		public const int PublicKeyLength = 32;

		/// <summary>Ed25519 signatures are exactly 64 bytes.</summary>
		public const int SignatureLength = 64;

		/// <summary>
		/// The exact text the signature field is normalised to in the canonical message.
		/// Must stay byte-identical to
		/// <c>FishMMO.Client.Security.Ed25519ManifestVerifier.BlankSignatureField</c>.
		/// </summary>
		public const string BlankSignatureField = "\"signature\": \"\"";

		/// <summary>The field name carrying the signature.</summary>
		public const string SignatureFieldName = "signature";

		/// <summary>
		/// Generates a fresh Ed25519 keypair.
		/// </summary>
		/// <returns>The 32-byte private seed and the 32-byte public key.</returns>
		public static (byte[] PrivateSeed, byte[] PublicKey) GenerateKeyPair()
		{
			byte[] seed = new byte[PrivateKeySeedLength];
			// SecureRandom rather than System.Random: this is a long-lived release key.
			new SecureRandom().NextBytes(seed);
			var priv = new Ed25519PrivateKeyParameters(seed, 0);
			return (seed, priv.GeneratePublicKey().GetEncoded());
		}

		/// <summary>
		/// Derives the public key for a private seed.
		/// </summary>
		public static byte[] DerivePublicKey(byte[] privateSeed)
		{
			if (privateSeed == null || privateSeed.Length != PrivateKeySeedLength)
			{
				throw new ArgumentException($"Ed25519 private seed must be exactly {PrivateKeySeedLength} bytes.", nameof(privateSeed));
			}
			return new Ed25519PrivateKeyParameters(privateSeed, 0).GeneratePublicKey().GetEncoded();
		}

		/// <summary>
		/// Decodes a base64 private key.
		/// </summary>
		/// <remarks>
		/// Two encodings are accepted because the two tools an operator is likely to reach for
		/// disagree: a raw 32-byte seed (what <c>keygen</c> here emits, and what BouncyCastle and
		/// most .NET code mean by "private key"), and the 64-byte libsodium/OpenSSH form which is
		/// <c>seed || publicKey</c>. When 64 bytes are supplied the trailing half is checked
		/// against the key actually derived from the seed, so a truncated or spliced key is
		/// rejected here rather than silently producing signatures nobody can verify.
		/// </remarks>
		/// <param name="privateKeyBase64">Base64 of a 32-byte seed or a 64-byte seed||public blob.</param>
		/// <param name="privateSeed">The 32-byte seed on success.</param>
		/// <param name="error">Why it failed. Never contains any part of the key material.</param>
		public static bool TryDecodePrivateKey(string? privateKeyBase64, out byte[]? privateSeed, out string? error)
		{
			privateSeed = null;
			error = null;

			if (string.IsNullOrWhiteSpace(privateKeyBase64))
			{
				error = "No signing key supplied.";
				return false;
			}

			byte[] raw;
			try
			{
				raw = Convert.FromBase64String(privateKeyBase64.Trim());
			}
			catch
			{
				// Deliberately does not echo the value: it is a private key, and an operator
				// pasting one into a bug report because the error quoted it is a real outcome.
				error = "Signing key is not valid base64.";
				return false;
			}

			if (raw.Length == PrivateKeySeedLength)
			{
				privateSeed = raw;
				return true;
			}

			if (raw.Length == PrivateKeySeedLength + PublicKeyLength)
			{
				byte[] seed = new byte[PrivateKeySeedLength];
				Buffer.BlockCopy(raw, 0, seed, 0, PrivateKeySeedLength);
				byte[] derived = DerivePublicKey(seed);
				bool matches = true;
				for (int i = 0; i < PublicKeyLength; i++)
				{
					matches &= derived[i] == raw[PrivateKeySeedLength + i];
				}
				Array.Clear(raw, 0, raw.Length);
				if (!matches)
				{
					Array.Clear(seed, 0, seed.Length);
					error = "Signing key is 64 bytes but its trailing public half does not match the key derived from the seed.";
					return false;
				}
				privateSeed = seed;
				return true;
			}

			error = $"Signing key is {raw.Length} bytes; expected {PrivateKeySeedLength} (seed) or {PrivateKeySeedLength + PublicKeyLength} (seed||public).";
			Array.Clear(raw, 0, raw.Length);
			return false;
		}

		/// <summary>
		/// Signs <paramref name="message"/> (UTF-8) with the given private seed.
		/// </summary>
		public static byte[] SignMessage(byte[] privateSeed, string message)
		{
			byte[] messageBytes = Encoding.UTF8.GetBytes(message);
			var signer = new Ed25519Signer();
			signer.Init(true, new Ed25519PrivateKeyParameters(privateSeed, 0));
			signer.BlockUpdate(messageBytes, 0, messageBytes.Length);
			return signer.GenerateSignature();
		}

		/// <summary>
		/// Verifies a base64 signature over <paramref name="message"/>. Present so the tool and
		/// the server can self-check without depending on the Unity client assembly.
		/// </summary>
		public static bool VerifyMessage(byte[] publicKey, string message, string signatureBase64)
		{
			if (publicKey == null || publicKey.Length != PublicKeyLength) return false;
			if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(signatureBase64)) return false;

			byte[] signature;
			try { signature = Convert.FromBase64String(signatureBase64); }
			catch { return false; }
			if (signature.Length != SignatureLength) return false;

			byte[] messageBytes = Encoding.UTF8.GetBytes(message);
			var signer = new Ed25519Signer();
			signer.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
			signer.BlockUpdate(messageBytes, 0, messageBytes.Length);
			return signer.VerifySignature(signature);
		}

		/// <summary>
		/// The canonical-message construction, mirroring the client verifier exactly: locate the
		/// signature field carrying <paramref name="signatureBase64"/> and rewrite it to
		/// <see cref="BlankSignatureField"/>.
		/// </summary>
		/// <returns>The canonical message, or null when the field could not be located.</returns>
		/// <remarks>
		/// The server does not need this to produce a signature — <see cref="SignDocument"/>
		/// builds the blanked document first and substitutes afterwards, so there is nothing to
		/// search for. It exists so the server can *self-check* every document it is about to
		/// emit by running the client's own algorithm over it (see
		/// <see cref="SignDocument"/>'s post-condition), turning any future drift between the two
		/// files into a refusal here rather than a manifest no player can verify.
		/// </remarks>
		public static string? BuildCanonicalSignedMessage(string json, string signatureBase64)
		{
			if (json == null || string.IsNullOrEmpty(signatureBase64)) return null;

			string search = "\"" + SignatureFieldName + "\": \"" + signatureBase64 + "\"";
			int idx = json.LastIndexOf(search, StringComparison.Ordinal);
			if (idx < 0)
			{
				search = "\"" + SignatureFieldName + "\":\"" + signatureBase64 + "\"";
				idx = json.LastIndexOf(search, StringComparison.Ordinal);
			}
			if (idx < 0) return null;

			return json.Substring(0, idx) + BlankSignatureField + json.Substring(idx + search.Length);
		}

		/// <summary>
		/// Signs a manifest and returns the finished JSON document, signature field included.
		/// </summary>
		/// <param name="body">
		/// The document body WITHOUT the signature field and WITHOUT the enclosing braces —
		/// exactly what <see cref="ManifestJsonWriter.BuildBody"/> produces.
		/// </param>
		/// <param name="privateSeed">32-byte Ed25519 seed.</param>
		/// <returns>The complete JSON document to send.</returns>
		/// <remarks>
		/// <para>
		/// The signature field is always emitted LAST. The verifier uses <c>LastIndexOf</c> to
		/// find it, so putting it last means the search cannot be steered by a value elsewhere in
		/// the document that happens to reproduce the same 88 base64 characters — an 88-character
		/// collision is not a real risk, but "last" costs nothing and removes the question.
		/// </para>
		/// <para>
		/// <b>Post-condition, enforced.</b> Before returning, the client's own locate-and-blank
		/// algorithm is run over the finished document and the result compared to the bytes that
		/// were actually signed. If they differ, this throws instead of returning. That converts
		/// any future divergence between this file and <c>Ed25519ManifestVerifier</c> — a change
		/// to the placeholder spacing, an escaping bug in the writer, a field value that
		/// contrives to contain the signature — into a loud server-side failure, rather than a
		/// document that every client in the field silently refuses. It costs one string
		/// comparison over a payload of a few hundred bytes.
		/// </para>
		/// </remarks>
		public static string SignDocument(string body, byte[] privateSeed)
		{
			if (privateSeed == null || privateSeed.Length != PrivateKeySeedLength)
			{
				throw new ArgumentException($"Ed25519 private seed must be exactly {PrivateKeySeedLength} bytes.", nameof(privateSeed));
			}

			string prefix = string.IsNullOrEmpty(body)
				? "{"
				: "{" + body + ", ";

			// The exact bytes that get signed.
			string canonical = prefix + BlankSignatureField + "}";

			byte[] signature = SignMessage(privateSeed, canonical);
			string signatureBase64 = Convert.ToBase64String(signature);

			// The exact bytes that go on the wire: identical to `canonical` apart from the
			// signature value substituted into the placeholder. No search, no replace.
			string document = prefix + "\"" + SignatureFieldName + "\": \"" + signatureBase64 + "\"}";

			string? roundTrip = BuildCanonicalSignedMessage(document, signatureBase64);
			if (!string.Equals(roundTrip, canonical, StringComparison.Ordinal))
			{
				throw new InvalidOperationException(
					"Version manifest canonicalisation self-check failed: blanking the signature field of the " +
					"emitted document did not reproduce the bytes that were signed. Refusing to serve a manifest " +
					"no client can verify.");
			}

			return document;
		}

		/// <summary>
		/// Signs an arbitrary JSON object supplied as text (the offline path used by the signing
		/// tool). Any existing <c>signature</c> field is discarded and re-emitted last.
		/// </summary>
		/// <remarks>
		/// Unlike the server hot path this DOES re-serialise, because it is handed a document
		/// somebody else wrote and there is no other way to guarantee the emitted spacing matches
		/// the canonical form. The output — not the input — is the signed artifact, and the tool
		/// says so.
		/// </remarks>
		public static string SignJsonObject(string json, byte[] privateSeed)
		{
			JsonNode? node = JsonNode.Parse(json);
			if (node is not JsonObject obj)
			{
				throw new InvalidOperationException("Manifest must be a JSON object at the top level.");
			}
			obj.Remove(SignatureFieldName);
			return SignDocument(ManifestJsonWriter.BuildBody(obj), privateSeed);
		}
	}
}
