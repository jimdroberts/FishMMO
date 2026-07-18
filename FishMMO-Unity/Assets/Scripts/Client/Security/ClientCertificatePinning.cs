using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Logging;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace FishMMO.Client.Security
{
	/// <summary>
	/// Centralised TLS certificate pinning for outbound <c>UnityWebRequest</c>
	/// traffic (launcher version probe, patch download, login-server discovery,
	/// renewal endpoints, etc.).
	///
	/// Pins are SHA-256 hashes of the <c>SubjectPublicKeyInfo</c> (SPKI) DER
	/// encoding, base64-encoded — the same format used by HPKP / RFC 7469. SPKI
	/// pinning is preferred over full-certificate pinning because it survives
	/// certificate renewals as long as the underlying key pair is reused, and is
	/// preferred over CA pinning because it cannot be bypassed by any CA in the
	/// trust store.
	///
	/// Unity's <c>CertificateHandler.ValidateCertificate(byte[])</c> only exposes
	/// the leaf certificate, so this implementation deliberately ignores chain
	/// validity and instead requires that the leaf's SPKI match a known-good
	/// pin. Always configure at least two pins (active + backup) so an emergency
	/// key rotation does not require a client patch.
	/// </summary>
	public static class ClientCertificatePinning
	{
		private const string LogChannel = "ClientCertificatePinning";

		private static readonly object Sync = new object();
		private static HashSet<string> pins = new HashSet<string>(StringComparer.Ordinal);
		private static bool allowOnEmptyPins;

		/// <summary>
		/// Set once the TOFU fallback path has actually accepted a real certificate
		/// at runtime (i.e. <see cref="ValidateCertificate"/> was called with an
		/// empty pin set and <see cref="allowOnEmptyPins"/> true). Used to escalate
		/// the warning to a single loud Error so a misconfigured release/staging
		/// build that silently drops into "trust everything" mode is impossible
		/// to miss in the logs.
		/// </summary>
		private static int tofuAcceptanceLogged;

		/// <summary>
		/// True when at least one pin has been registered. Callers can use this
		/// to short-circuit a network request before it is dispatched.
		/// </summary>
		public static bool HasPins
		{
			get
			{
				lock (Sync)
				{
					return pins.Count > 0;
				}
			}
		}

		/// <summary>
		/// Replace the active pin set. Pins are SHA-256(SPKI) base64 strings.
		/// Passing <c>null</c> or an empty collection clears the pin set.
		/// </summary>
		/// <param name="newPins">
		///   The new pin list. Whitespace and empty entries are ignored.
		/// </param>
		/// <param name="allowOnEmpty">
		///   When <c>true</c> and the pin set is empty, <see cref="ValidateCertificate"/>
		///   will fall back to <em>temporal validity only</em> instead of rejecting
		///   the certificate outright. Use only for editor / development builds.
		/// </param>
		public static void Configure(IEnumerable<string> newPins, bool allowOnEmpty = false)
		{
			var rebuilt = new HashSet<string>(StringComparer.Ordinal);
			if (newPins != null)
			{
				foreach (var raw in newPins)
				{
					if (string.IsNullOrWhiteSpace(raw))
					{
						continue;
					}
					rebuilt.Add(raw.Trim());
				}
			}

			lock (Sync)
			{
				pins = rebuilt;
				allowOnEmptyPins = allowOnEmpty;
			}

			Log.Debug(LogChannel, $"Configured {rebuilt.Count} TLS pin(s); allowOnEmpty={allowOnEmpty}.");
		}

		/// <summary>
		/// Validate a leaf certificate (DER-encoded) against the configured pin
		/// set. Performs:
		///   1. DER → <see cref="X509Certificate"/> parse via BouncyCastle.
		///   2. NotBefore / NotAfter temporal check (UTC).
		///   3. SHA-256(SPKI) computation and constant-time comparison against
		///      every configured pin.
		/// </summary>
		/// <param name="certificateDer">Raw DER bytes from the server.</param>
		/// <returns><c>true</c> when the certificate is accepted.</returns>
		public static bool ValidateCertificate(byte[] certificateDer)
		{
			if (certificateDer == null || certificateDer.Length == 0)
			{
				Log.Warning(LogChannel, "Rejecting empty certificate payload.");
				return false;
			}

			X509Certificate cert;
			try
			{
				cert = new X509CertificateParser().ReadCertificate(certificateDer);
			}
			catch (Exception ex)
			{
				Log.Warning(LogChannel, $"Failed to parse leaf certificate: {ex.Message}");
				return false;
			}

			if (cert == null)
			{
				Log.Warning(LogChannel, "BouncyCastle returned a null certificate.");
				return false;
			}

			// Temporal validity (UTC).
			var nowUtc = DateTime.UtcNow;
			if (nowUtc < cert.NotBefore.ToUniversalTime() ||
				nowUtc > cert.NotAfter.ToUniversalTime())
			{
				Log.Warning(LogChannel,
					$"Certificate outside validity window ({cert.NotBefore:o} → {cert.NotAfter:o}).");
				return false;
			}

			string spkiPin;
			try
			{
				spkiPin = ComputeSpkiSha256Base64(cert);
			}
			catch (Exception ex)
			{
				Log.Warning(LogChannel, $"Failed to compute SPKI hash: {ex.Message}");
				return false;
			}

			HashSet<string> snapshot;
			bool allowEmpty;
			lock (Sync)
			{
				snapshot = pins;
				allowEmpty = allowOnEmptyPins;
			}

			if (snapshot.Count == 0)
			{
				if (allowEmpty)
				{
					// Loud one-shot Error on the first real TOFU acceptance.
					// Subsequent fallbacks remain at Warning so we don't spam logs
					// once the operator has acknowledged the misconfiguration.
					if (System.Threading.Interlocked.Exchange(ref tofuAcceptanceLogged, 1) == 0)
					{
						Log.Error(LogChannel,
							"TLS pin set is empty — falling back to temporal-validity-only validation. " +
							$"First accepted SPKI={spkiPin}. This is a MITM-vulnerable configuration; " +
							"populate StreamingAssets/client-security.json with the production pins.");
					}
					else
					{
						Log.Warning(LogChannel,
							$"No pins configured; accepting on temporal validity only. SPKI={spkiPin}.");
					}
					return true;
				}
				Log.Error(LogChannel,
					$"Rejecting certificate: no pins configured. Observed SPKI={spkiPin}. " +
					"Call ClientCertificatePinning.Configure(...) during bootstrap.");
				return false;
			}

			if (ConstantTimeContains(snapshot, spkiPin))
			{
				return true;
			}

			Log.Warning(LogChannel,
				$"Pin mismatch. Observed SPKI={spkiPin}; expected one of [{string.Join(", ", snapshot)}].");
			return false;
		}

		/// <summary>
		/// Compute the base64-encoded SHA-256 of the certificate's
		/// <c>SubjectPublicKeyInfo</c>. Exposed so build tooling can derive pin
		/// values from a PEM/DER cert without depending on OpenSSL.
		/// </summary>
		public static string ComputeSpkiSha256Base64(byte[] certificateDer)
		{
			if (certificateDer == null || certificateDer.Length == 0)
			{
				throw new ArgumentException("Certificate DER is empty.", nameof(certificateDer));
			}
			var cert = new X509CertificateParser().ReadCertificate(certificateDer);
			return ComputeSpkiSha256Base64(cert);
		}

		private static string ComputeSpkiSha256Base64(X509Certificate cert)
		{
			SubjectPublicKeyInfo spki = SubjectPublicKeyInfoFactory
				.CreateSubjectPublicKeyInfo(cert.GetPublicKey());
			byte[] der = spki.GetDerEncoded();
			// Use BCL SHA256.Create() + ComputeHash() instead of
			// DigestUtilities.CalculateDigest("SHA-256", der) because the algorithm
			// name lookup path in DigestUtilities may fail on AOT/IL2CPP platforms
			// where dynamic reflection-based type resolution is constrained.
			// SHA256.HashData(der) is .NET 5+ only — not available in Unity Mono.
			byte[] hash;
			using (var sha = System.Security.Cryptography.SHA256.Create())
				hash = sha.ComputeHash(der);
			return Convert.ToBase64String(hash);
		}

		/// <summary>
		/// Constant-time membership test. <see cref="HashSet{T}.Contains"/> is
		/// short-circuiting; this routine compares every candidate so timing
		/// cannot leak which pin matched.
		/// </summary>
		private static bool ConstantTimeContains(HashSet<string> set, string candidate)
		{
			int matches = 0;
			foreach (var pin in set)
			{
				matches |= ConstantTimeEquals(pin, candidate) ? 1 : 0;
			}
			return matches != 0;
		}

		private static bool ConstantTimeEquals(string a, string b)
		{
			// All pins are SHA-256(SPKI) hashes which always produce exactly 44
			// base64 characters (32 bytes -> 44 chars with no padding issues).
			// Because the length is fixed for all valid pins, the early-return
			// length check below is effectively constant-time in practice — an
			// attacker cannot distinguish a length mismatch from a content mismatch.
			if (a == null || b == null || a.Length != b.Length)
			{
				return false;
			}
			int diff = 0;
			for (int i = 0; i < a.Length; i++)
			{
				diff |= a[i] ^ b[i];
			}
			return diff == 0;
		}
	}
}
