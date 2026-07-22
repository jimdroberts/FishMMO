using System;
using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Client.Security
{
	/// <summary>
	/// Scaffold interface for out-of-band TLS pin updates.
	///
	/// This is a forward-planning scaffold only. No production implementation
	/// is currently wired into the client. The <see cref="NullPinUpdateSidecar"/>
	/// default is always returned, meaning the static pin set from
	/// <see cref="ClientSecurityBootstrap"/> is never overridden at runtime.
	///
	/// Hard-coded compile-time pins (see <see cref="ClientSecurityBootstrap"/>) and
	/// StreamingAssets configuration both require shipping a new client build to
	/// rotate keys. That is unacceptable when a CA-incident or HSM rotation forces
	/// an emergency pin swap on a faster cadence than the client release pipeline.
	///
	/// The intended production implementation:
	///   * Fetches a signed manifest (Ed25519 / RSA-PSS) from a well-known URL,
	///   * Verifies the signature against a compile-time embedded public key
	///     (a separate trust anchor from the TLS PKI it is updating!),
	///   * Returns the new pin set with an effective-from / expires-at window
	///     so the bootstrap can reject stale manifests.
	///
	/// The bootstrap MUST treat sidecar failures as advisory only and never
	/// downgrade pinning below what was statically configured. A compromised
	/// sidecar host must not be able to remove pins — only narrow them.
	/// </summary>
	public interface IPinUpdateSidecar
	{
		/// <summary>
		/// Attempts to retrieve an updated pin manifest. Implementations must
		/// validate signature + freshness before returning. Returning <c>null</c>
		/// means "no update available" and is not an error.
		/// </summary>
		Task<PinUpdateManifest> TryFetchUpdateAsync(CancellationToken cancellationToken);
	}

	/// <summary>
	/// Result of a successful pin update fetch. Fields are intentionally minimal
	/// to constrain what a compromised sidecar can express.
	/// </summary>
	public sealed class PinUpdateManifest
	{
		/// <summary>Replacement pin set (base64 SHA-256(SPKI)).</summary>
		public string[] Pins { get; }

		/// <summary>UTC effective-from instant. Manifests dated in the future are rejected.</summary>
		public DateTime EffectiveFromUtc { get; }

		/// <summary>UTC expiry. Manifests past expiry are rejected to prevent replay of an old, possibly compromised set.</summary>
		public DateTime ExpiresAtUtc { get; }

		public PinUpdateManifest(string[] pins, DateTime effectiveFromUtc, DateTime expiresAtUtc)
		{
			Pins = pins ?? throw new ArgumentNullException(nameof(pins));
			if (effectiveFromUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("effectiveFromUtc must be UTC.", nameof(effectiveFromUtc));
			if (expiresAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("expiresAtUtc must be UTC.", nameof(expiresAtUtc));
			if (expiresAtUtc <= effectiveFromUtc) throw new ArgumentException("expiresAtUtc must be strictly after effectiveFromUtc.");
			EffectiveFromUtc = effectiveFromUtc;
			ExpiresAtUtc = expiresAtUtc;
		}
	}

	/// <summary>
	/// No-op default sidecar used until a signed-manifest implementation is wired in.
	/// Always returns "no update available". Never downgrades the static pin set.
	/// </summary>
	public sealed class NullPinUpdateSidecar : IPinUpdateSidecar
	{
		/// <inheritdoc/>
		public Task<PinUpdateManifest> TryFetchUpdateAsync(CancellationToken cancellationToken)
		{
			return Task.FromResult<PinUpdateManifest>(null);
		}
	}
}
