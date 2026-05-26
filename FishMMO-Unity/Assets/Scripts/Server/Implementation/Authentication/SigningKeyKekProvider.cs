using System;
using FishMMO.Auth.Implementation;
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
	///   <item><description><c>SigningKeyKekBase64</c> in the server configuration file.</description></item>
	///   <item><description><c>FISHMMO_SIGNING_KEY_KEK_BASE64</c> environment variable.</description></item>
	/// </list>
	/// The value must Base64-decode to exactly 32 bytes. A short, missing, or malformed key is a
	/// fatal configuration error — the server SHOULD refuse to start.
	/// </remarks>
	public static class SigningKeyKekProvider
	{
		/// <summary>Configuration file key.</summary>
		public const string ConfigKey = "SigningKeyKekBase64";

		/// <summary>Environment variable fallback.</summary>
		public const string EnvKey = "FISHMMO_SIGNING_KEY_KEK_BASE64";

		/// <summary>Required KEK byte length (AES-256).</summary>
		public const int KekLength = 32;

		/// <summary>
		/// Attempts to resolve and decode the KEK. Returns <c>true</c> with a 32-byte buffer on
		/// success; on failure returns <c>false</c> and writes a non-null <paramref name="error"/>.
		/// </summary>
		public static bool TryLoad(IServerConfiguration configuration, out byte[] kek, out string error)
		{
			kek = null;
			error = null;

			string b64 = null;
			if (configuration != null && configuration.TryGetString(ConfigKey, out string cfgValue) && !string.IsNullOrWhiteSpace(cfgValue))
			{
				b64 = cfgValue.Trim();
			}
			else
			{
				string envValue = Environment.GetEnvironmentVariable(EnvKey);
				if (!string.IsNullOrWhiteSpace(envValue))
				{
					b64 = envValue.Trim();
				}
			}

			if (string.IsNullOrEmpty(b64))
			{
				error = $"Signing-key KEK not configured. Set '{ConfigKey}' in the server configuration file " +
						$"or the '{EnvKey}' environment variable to a Base64-encoded 32-byte AES-256 key.";
				return false;
			}

			byte[] decoded;
			try
			{
				decoded = Convert.FromBase64String(b64);
			}
			catch (FormatException)
			{
				error = $"Signing-key KEK value is not valid Base64.";
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