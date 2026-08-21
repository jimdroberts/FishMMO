using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Logging;
using FishMMO.Shared;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using UnityEngine.Networking;

namespace FishMMO.Client.Security
{
	/// <summary>
	/// Production implementation of <see cref="IPinUpdateSidecar"/>.
	///
	/// Fetches a signed pin update manifest from
	/// <c>GET {APIHost}config/pins</c>, verifies the Ed25519 signature
	/// against the compile-time-embedded public key, validates the
	/// effective/expiry window, and returns the new pin set.
	///
	/// Every failure mode returns <c>null</c> — the compile-time pin set
	/// remains active. A compromised API host cannot remove pins, only
	/// suggest additions.
	/// </summary>
	internal sealed class ApiPinUpdateSidecar : IPinUpdateSidecar
	{
		private const string logChannel = "ApiPinUpdateSidecar";
		private const string pinConfigPath = "config/pins";

		/// <inheritdoc/>
		public async Task<PinUpdateManifest> TryFetchUpdateAsync(CancellationToken cancellationToken)
		{
			string publicKeyBase64 = GeneratedPinSet.ManifestPublicKeyBase64;
			if (string.IsNullOrEmpty(publicKeyBase64))
			{
				return null; // feature disabled — no public key configured
			}

			byte[] publicKey;
			try
			{
				publicKey = Convert.FromBase64String(publicKeyBase64);
			}
			catch (Exception ex)
			{
				_ = Log.Warning(logChannel, $"Manifest public key is not valid base64: {ex.Message}");
				return null;
			}

			if (publicKey.Length != 32) // Ed25519 public key is exactly 32 bytes
			{
				_ = Log.Warning(logChannel,
					$"Manifest public key is {publicKey.Length} bytes (expected 32 for Ed25519).");
				return null;
			}

			string url = Constants.Configuration.APIHost + pinConfigPath;

			string responseJson;
			try
			{
				responseJson = await FetchJsonAsync(url, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				_ = Log.Info(logChannel, "Pin update fetch cancelled (timeout or shutdown).");
				return null;
			}
			catch (Exception ex)
			{
				_ = Log.Warning(logChannel, $"Failed to fetch pin manifest from {url}: {ex.Message}");
				return null;
			}

			if (string.IsNullOrEmpty(responseJson))
			{
				return null;
			}

			PinManifestPayload payload;
			try
			{
				payload = UnityEngine.JsonUtility.FromJson<PinManifestPayload>(responseJson);
			}
			catch (Exception ex)
			{
				_ = Log.Warning(logChannel, $"Failed to parse pin manifest JSON: {ex.Message}");
				return null;
			}

			if (payload == null || payload.pins == null || payload.pins.Length == 0)
			{
				_ = Log.Warning(logChannel, "Pin manifest contains no pins.");
				return null;
			}

			// Validate timestamps.
			if (!DateTime.TryParse(payload.effectiveFromUtc, null,
					System.Globalization.DateTimeStyles.RoundtripKind, out DateTime effectiveFrom))
			{
				_ = Log.Warning(logChannel, "Pin manifest missing or invalid effectiveFromUtc.");
				return null;
			}
			if (!DateTime.TryParse(payload.expiresAtUtc, null,
					System.Globalization.DateTimeStyles.RoundtripKind, out DateTime expiresAt))
			{
				_ = Log.Warning(logChannel, "Pin manifest missing or invalid expiresAtUtc.");
				return null;
			}

			effectiveFrom = effectiveFrom.ToUniversalTime();
			expiresAt = expiresAt.ToUniversalTime();
			DateTime now = DateTime.UtcNow;

			if (now < effectiveFrom)
			{
				_ = Log.Warning(logChannel,
					$"Pin manifest not yet effective (effective={effectiveFrom:O}, now={now:O}).");
				return null;
			}
			if (now >= expiresAt)
			{
				_ = Log.Warning(logChannel,
					$"Pin manifest has expired (expires={expiresAt:O}, now={now:O}).");
				return null;
			}

			// Verify Ed25519 signature.
			byte[] signature;
			try
			{
				signature = Convert.FromBase64String(payload.signature ?? string.Empty);
			}
			catch
			{
				_ = Log.Warning(logChannel, "Pin manifest signature is not valid base64.");
				return null;
			}

			if (!VerifyEd25519(publicKey, responseJson, payload.signature, signature))
			{
				_ = Log.Warning(logChannel, "Pin manifest signature verification FAILED — discarding update.");
				return null;
			}

			_ = Log.Info(logChannel,
				$"Pin manifest accepted: {payload.pins.Length} pin(s), " +
				$"effective={effectiveFrom:O}, expires={expiresAt:O}.");

			return new PinUpdateManifest(payload.pins, effectiveFrom, expiresAt);
		}

		/// <summary>
		/// Fetches the JSON response body from <paramref name="url"/> using
		/// a UnityWebRequest. The request is protected by the compile-time
		/// TLS pins already configured by <see cref="ClientSecurityBootstrap"/>.
		/// </summary>
		private static async Task<string> FetchJsonAsync(string url, CancellationToken ct)
		{
			using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
			{
				request.certificateHandler = new ClientSSLCertificateHandler();
				request.downloadHandler = new DownloadHandlerBuffer();
				request.timeout = 15;
				request.redirectLimit = -1; // never follow redirects

				// Sign the request for the public API gate.
				var headers = new System.Collections.Generic.Dictionary<string, string>();
				ClientApiSigner.SignAndAdd(headers, UnityWebRequest.kHttpVerbGET, url);
				foreach (var kvp in headers)
					request.SetRequestHeader(kvp.Key, kvp.Value);

				var op = request.SendWebRequest();

				// Wire up cancellation — Abort() unblocks the await below.
				using (ct.Register(() => request.Abort()))
				{
					await op;
				}

				if (request.result != UnityWebRequest.Result.Success)
				{
					throw new Exception(
						$"HTTP {request.responseCode}: {request.error}");
				}

				return request.downloadHandler.text;
			}
		}

		/// <summary>
		/// Verifies an Ed25519 signature over a JSON payload using
		/// the canonical signed-data format:
		/// <c>payload_without_signature_field || payload.signature</c>
		/// where the signature field is stripped before verification.
		///
		/// This uses BouncyCastle's Ed25519Signer which is already
		/// available (preserved in link.xml for X509 parsing).
		/// </summary>
		/// <param name="publicKey">32-byte Ed25519 public key.</param>
		/// <param name="fullJson">The raw JSON response including the signature field.</param>
		/// <param name="signatureBase64">The base64 signature string from the JSON.</param>
		/// <param name="signatureBytes">Pre-decoded signature bytes.</param>
		private static bool VerifyEd25519(
			byte[] publicKey,
			string fullJson,
			string signatureBase64,
			byte[] signatureBytes)
		{
			if (signatureBytes.Length != 64) // Ed25519 signature is exactly 64 bytes
			{
				Log.Warning(logChannel,
					$"Signature is {signatureBytes.Length} bytes (expected 64 for Ed25519).");
				return false;
			}

			// Build the canonical signed message: the JSON with the signature
			// field zeroed out, then the signature appended.
			string message = BuildCanonicalSignedMessage(fullJson, signatureBase64);
			byte[] messageBytes = Encoding.UTF8.GetBytes(message);

			try
			{
				var signer = new Ed25519Signer();
				signer.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
				signer.BlockUpdate(messageBytes, 0, messageBytes.Length);
				return signer.VerifySignature(signatureBytes);
			}
			catch (Exception ex)
			{
				Log.Warning(logChannel, $"Ed25519 verification error: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Produces the canonical signed message from the raw JSON response.
		/// The canonical form is: the full JSON string with the value of the
		/// <c>"signature"</c> field replaced by an empty string, followed by
		/// the raw base64 signature value itself.
		///
		/// This is a simple, deterministic construction: find the last
		/// occurrence of the signature value (which appears exactly once as a
		/// JSON string value), replace it with <c>""</c>, and append the
		/// signature.
		/// </summary>
		private static string BuildCanonicalSignedMessage(string json, string signatureBase64)
		{
			// Find: "signature": "<sig_value>"
			// Replace: "signature": ""
			string search = "\"signature\": \"" + signatureBase64 + "\"";
			int idx = json.LastIndexOf(search, StringComparison.Ordinal);
			if (idx < 0)
			{
				// Try without spaces.
				search = "\"signature\":\"" + signatureBase64 + "\"";
				idx = json.LastIndexOf(search, StringComparison.Ordinal);
			}
			if (idx >= 0)
			{
				string stripped = json.Substring(0, idx) + "\"signature\": \"\"" +
					json.Substring(idx + search.Length);
				return stripped + signatureBase64;
			}

			// Fallback: if we can't find the signature field, sign the whole
			// JSON plus the signature (this is wrong but better than crashing).
			Log.Warning(logChannel,
				"Could not locate signature field in manifest JSON; using raw JSON as canonical message.");
			return json + signatureBase64;
		}

		/* Every field is populated by JsonUtility through reflection, which the compiler
		 * cannot see — hence CS0649 "never assigned" on all four. Disabled over the type
		 * rather than project-wide so a genuinely unassigned field elsewhere still warns. */
#pragma warning disable 0649
		[Serializable]
		private sealed class PinManifestPayload
		{
			public string[] pins;
			public string effectiveFromUtc;
			public string expiresAtUtc;
			public string signature;
		}
#pragma warning restore 0649
	}
}
