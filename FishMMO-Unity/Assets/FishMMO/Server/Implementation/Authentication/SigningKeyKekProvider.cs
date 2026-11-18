using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Auth.Implementation;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Loads the deployment-shared 32-byte AES-256 KEK used by <see cref="KeyEnvelope"/> to wrap
	/// per-LoginServer HMAC signing keys at rest. The KEK MUST be identical across the LoginServer
	/// process (which writes wrapped blobs) and every World/Scene server (which unwraps them) for
	/// a given deployment.
	/// </summary>
	/// <remarks>
	/// Resolution order:
	/// <list type="number">
	///   <item><description><c>deployment_secrets</c> database table with key='signing_key_kek'.</description></item>
	/// </list>
	/// The value must Base64-decode to exactly 32 bytes. A short, missing, or malformed key is a
	/// fatal configuration error — the server SHOULD refuse to start.
	/// No environment variable or .cfg file fallback is supported.
	/// </remarks>
	public static class SigningKeyKekProvider
	{
		/// <summary>Database key for the signing key KEK in the deployment_secrets table.</summary>
		public const string DatabaseKey = "signing_key_kek";

		/// <summary>Required KEK byte length (AES-256).</summary>
		public const int KekLength = 32;

		/// <summary>
		/// Loads the signing-key KEK from the <c>deployment_secrets</c> database table without
		/// blocking any thread. This is the preferred entry point — the token-verification path
		/// is already asynchronous and must never block a worker on a database round trip.
		/// </summary>
		/// <param name="secretService">Deployment secret service.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The loaded key, or a failure carrying the reason.</returns>
		public static async Task<KekLoadResult> LoadFromDatabaseAsync(
			IDeploymentSecretService secretService,
			CancellationToken cancellationToken = default)
		{
			if (secretService == null)
			{
				return KekLoadResult.Failure("IDeploymentSecretService is null.");
			}

			try
			{
				var result = await secretService.FetchAsync(DatabaseKey, cancellationToken).ConfigureAwait(false);

				if (!result.IsSuccess || string.IsNullOrEmpty(result.Data))
				{
					return KekLoadResult.Failure(
						$"Failed to fetch '{DatabaseKey}' from deployment_secrets: " +
						$"[{result.ErrorCode}] {result.ErrorMessage ?? "No data returned"}.");
				}

				byte[] decoded;
				try
				{
					decoded = Convert.FromBase64String(result.Data);
				}
				catch (FormatException)
				{
					return KekLoadResult.Failure("Signing-key KEK value from deployment_secrets is not valid Base64.");
				}

				if (decoded.Length != KekLength)
				{
					return KekLoadResult.Failure(
						$"Signing-key KEK must decode to exactly {KekLength} bytes (got {decoded.Length}).");
				}

				return KekLoadResult.Ok(decoded);
			}
			catch (Exception ex)
			{
				return KekLoadResult.Failure($"Exception loading signing_key_kek from deployment_secrets: {ex.Message}");
			}
		}

		/// <summary>
		/// Outcome of a KEK load. <see cref="Kek"/> is non-null only when <see cref="Success"/>
		/// is <c>true</c>; otherwise <see cref="Error"/> explains why, for fail-closed callers.
		/// </summary>
		public readonly struct KekLoadResult
		{
			/// <summary>Whether a valid 32-byte KEK was loaded.</summary>
			public bool Success { get; }

			/// <summary>The loaded KEK, or <c>null</c> on failure.</summary>
			public byte[] Kek { get; }

			/// <summary>Failure reason, or <c>null</c> on success.</summary>
			public string Error { get; }

			private KekLoadResult(bool success, byte[] kek, string error)
			{
				Success = success;
				Kek = kek;
				Error = error;
			}

			/// <summary>Creates a successful result.</summary>
			public static KekLoadResult Ok(byte[] kek) => new KekLoadResult(true, kek, null);

			/// <summary>Creates a failed result.</summary>
			public static KekLoadResult Failure(string error) => new KekLoadResult(false, null, error);
		}

		/// <summary>
		/// Builds the 8-byte big-endian AAD bound to wrapped signing-key blobs. AAD = the owning
		/// LoginServer's stable row id, so a blob copied between rows fails authentication.
		/// </summary>
		public static byte[] BuildAad(long loginServerId)
		{
			byte[] aad = new byte[8];
			ulong v = unchecked((ulong)loginServerId);
			aad[0] = (byte)(v >> 56);
			aad[1] = (byte)(v >> 48);
			aad[2] = (byte)(v >> 40);
			aad[3] = (byte)(v >> 32);
			aad[4] = (byte)(v >> 24);
			aad[5] = (byte)(v >> 16);
			aad[6] = (byte)(v >> 8);
			aad[7] = (byte)v;
			return aad;
		}
	}
}