using System;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Client.Security
{
	/// <summary>
	/// Initialises <see cref="ClientCertificatePinning"/> before any scene loads
	/// or <c>UnityWebRequest</c> is dispatched. Pin sources, in order:
	///
	///   1. <c>CertificatePins.generated.cs</c> — IL-embedded pins injected at
	///      build time from FISHMMO_PIN_ACTIVE / FISHMMO_PIN_BACKUP env vars.
	///      The committed source contains sentinel placeholders; CI substitutes
	///      real values before invoking Unity.
	///
	///   2. API pin update manifest (optional, async) — fetched from
	///      <c>GET {APIHost}config/pins</c>, verified against an Ed25519
	///      public key embedded alongside the pins. Only ADDS pins; never
	///      removes. Failures are silent — the compile-time pin set remains
	///      active.
	///
	/// There is NO StreamingAssets fallback. Pins are always IL-embedded.
	/// </summary>
	public static class ClientSecurityBootstrap
	{
		private const string logChannel = "ClientSecurityBootstrap";

		/// <summary>
		/// Number of compile-time certificate pins configured for release-build
		/// validation. Exposed for the build validator.
		/// </summary>
		public static int DefaultPinCount
		{
			get
			{
				string[] p = GeneratedPinSet.Pins;
				if (p == null) return 0;
				int count = 0;
				foreach (var pin in p)
					if (!string.IsNullOrWhiteSpace(pin) && !pin.Contains(GeneratedPinSet.SentinelMarker))
						count++;
				return count;
			}
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Initialize()
		{
			try
			{
				ClientCertificatePinning.SetInitialPins(GeneratedPinSet.Pins);
			}
			catch (Exception ex)
			{
				Log.Critical(logChannel,
					$"Failed to initialise TLS certificate pinning: {ex.Message}. " +
					"ALL TLS CONNECTIONS WILL BE REJECTED.");
				return;
			}

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
			// Layer 2: attempt API-sourced pin update (non-blocking, best-effort).
			// The compile-time pins are already active and will remain so even if
			// this fetch fails. Only fires when a manifest public key is configured.
			if (!string.IsNullOrEmpty(GeneratedPinSet.ManifestPublicKeyBase64))
			{
				TryFetchPinUpdateAsync();
			}
#endif
		}

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
		/// <summary>
		/// Fire-and-forget API pin update. Failures are logged at Warning level
		/// — the compile-time pins remain active regardless.
		/// </summary>
		private static async void TryFetchPinUpdateAsync()
		{
			try
			{
				var sidecar = new ApiPinUpdateSidecar();
				var cts = new System.Threading.CancellationTokenSource(
					TimeSpan.FromSeconds(30)); // generous timeout for first fetch
				PinUpdateManifest manifest = await sidecar.TryFetchUpdateAsync(cts.Token);
				if (manifest?.Pins != null && manifest.Pins.Length > 0)
				{
					ClientCertificatePinning.TryAugmentPins(manifest.Pins);
				}
			}
			catch (Exception ex)
			{
				Log.Warning(logChannel, $"API pin update fetch failed (compile-time pins remain active): {ex.Message}");
			}
		}
#endif
	}
}
