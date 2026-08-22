using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Logging;
using FishMMO.Shared;
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

			// Decoding and length-checking live in Ed25519ManifestVerifier now: the version
			// manifest needs the identical treatment, and two copies of a signature check drift.
			if (!Ed25519ManifestVerifier.TryDecodePublicKey(publicKeyBase64, out byte[] publicKey))
			{
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
			if (!Ed25519ManifestVerifier.Verify(publicKey, responseJson, payload.signature))
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
