using System;
using System.Threading;
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
		/// Loads the signing-key KEK from the <c>deployment_secrets</c> database table
		/// using <paramref name="secretService"/>. The value for key 'signing_key_kek'
		/// must be a Base64-encoded 32-byte AES-256 key.
		/// Returns <c>true</c> with a 32-byte buffer on success; on failure returns
		/// <c>false</c> and writes a non-null <paramref name="error"/>.
		/// </summary>
		public static bool TryLoadFromDatabase(IDeploymentSecretService secretService, out byte[] kek, out string error)
		{
			kek = null;
			error = null;

			if (secretService == null)
			{
				error = "IDeploymentSecretService is null.";
				return false;
			}

			try
			{
				// MUST leave the Unity SynchronizationContext. LoginServerSystem.InitializeOnce
				// calls this on the main thread, then StartServer() only runs after it returns.
				// A raw FetchAsync(...).GetResult() deadlocks: EF/Npgsql completions post back
				// to the blocked main thread, last_pulse freezes, and UDP :7770 never binds.
				// UnitySyncOverAsync.Run is the same Task.Run + timeout pattern used for
				// Login/World/Scene DB registration.
				var result = UnitySyncOverAsync.Run(
					() => secretService.FetchAsync(DatabaseKey, CancellationToken.None));

				if (!result.IsSuccess || string.IsNullOrEmpty(result.Data))
				{
					error = $"Failed to fetch '{DatabaseKey}' from deployment_secrets: " +
							$"[{result.ErrorCode}] {result.ErrorMessage ?? "No data returned"}.";
					return false;
				}

				byte[] decoded;
				try
				{
					decoded = Convert.FromBase64String(result.Data);
				}
				catch (FormatException)
				{
					error = $"Signing-key KEK value from deployment_secrets is not valid Base64.";
					return false;
				}

				if (decoded.Length != KekLength)
				{
					error = $"Signing-key KEK must decode to exactly {KekLength} bytes (got {decoded.Length}).";
					return false;
				}

				kek = decoded;
				return true;
			}
			catch (Exception ex)
			{
				error = $"Exception loading signing_key_kek from deployment_secrets: {ex.Message}";
				return false;
			}
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