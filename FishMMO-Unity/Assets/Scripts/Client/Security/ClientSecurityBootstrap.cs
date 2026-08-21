using System;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Client.Security
{
	/// <summary>
	/// Initialises <see cref="ClientCertificatePinning"/> before any scene loads
	/// or <c>UnityWebRequest</c> is dispatched. Pin sources, in order:
	///
		///   1. <c>CertificatePins.generated.cs</c> — IL-embedded pins written by
		///      FishMMO &gt; Security &gt; Fetch Certificate Pins. The committed
		///      source contains sentinel placeholders; the editor tool overwrites them.
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
				/* Explicitly observed, and explicitly discarded.
				 *
				 * The discard is what makes "we are not awaiting this" a statement rather than
				 * an omission, and the continuation below is what makes an unobserved exception
				 * impossible. See TryFetchPinUpdateAsync for why this stopped being async void.
				 */
				_ = TryFetchPinUpdateAsync().ContinueWith(
					t => Log.Error(logChannel, $"API pin update task faulted: {t.Exception?.GetBaseException().Message}"),
					System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
			}
#endif
		}

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
		/// <summary>
		/// Fire-and-forget API pin update. Failures are logged at Warning level
		/// — the compile-time pins remain active regardless.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Returns Task, not void.</b> An <c>async void</c> method has no way to report a
		/// failure to its caller, because it has no caller to report to — the continuation runs
		/// on whatever context resumed it, and an exception escaping it is raised on that
		/// context rather than being captured. In .NET that is an unhandled exception on a
		/// thread-pool thread, which terminates the process. From a
		/// <c>RuntimeInitializeOnLoadMethod</c>, before any scene has loaded, that is the client
		/// dying at startup because a best-effort pin refresh went wrong.
		/// </para>
		/// <para>
		/// The body was already inside a try/catch, so the immediate risk was small — but "small"
		/// depended on the catch staying exhaustive across every future edit, and on nothing
		/// throwing in the <c>await</c> machinery itself. Returning a Task moves the guarantee
		/// from the body to the signature: the caller attaches a faulted continuation, so a
		/// failure is logged rather than raised no matter what the body does.
		/// </para>
		/// <para>
		/// The CancellationTokenSource is disposed. It schedules a timer, and a leaked timer
		/// holds its callback alive for the full 30 seconds after the fetch has already finished.
		/// </para>
		/// </remarks>
		private static async System.Threading.Tasks.Task TryFetchPinUpdateAsync()
		{
			try
			{
				var sidecar = new ApiPinUpdateSidecar();
				using (var cts = new System.Threading.CancellationTokenSource(
					TimeSpan.FromSeconds(30))) // generous timeout for first fetch
				{
					PinUpdateManifest manifest = await sidecar.TryFetchUpdateAsync(cts.Token);
					if (manifest?.Pins != null && manifest.Pins.Length > 0)
					{
						ClientCertificatePinning.TryAugmentPins(manifest.Pins);
					}
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
