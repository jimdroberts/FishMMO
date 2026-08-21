// Explicit rather than relying on the Web SDK's ImplicitUsings: this file is also compiled
// into the round-trip test harness by source link, and that project is not a Web SDK project.
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using FishMMO.Logging;
using FishMMO.WebServers.Signing;

/// <summary>
/// Loads the Ed25519 release key and signs every version-manifest document the patcher emits.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the manifest is signed at all.</b> The <c>sha256</c> field in this document is the ONLY
/// integrity check the launcher applies to a patch archive, and <c>latest_version</c> becomes half
/// of a file name on the player's disk. Whoever writes this document therefore chooses the bytes
/// that get installed. TLS authenticates the transport and nothing else — it says nothing about a
/// compromised gateway, a mis-issued certificate, or a CDN edge holding a substituted copy. A
/// signature moves the trust anchor from "whoever is answering on this host" to "whoever holds the
/// release key", which is the property that makes checking the SHA-256 worth anything.
/// </para>
/// <para>
/// <b>Key sources, in priority order.</b> All three carry a base64 Ed25519 private key (a 32-byte
/// seed, or the 64-byte <c>seed||public</c> form):
/// </para>
/// <list type="number">
///   <item><description>
///     <c>Signing:VersionManifestPrivateKeyFile</c> — path to a file containing the base64 key.
///     Preferred. A file can be mode 600 and owned by the service user, it does not appear in
///     <c>/proc/&lt;pid&gt;/environ</c> where any process running as the same user can read it,
///     and it is what systemd <c>LoadCredential=</c> and Docker/Kubernetes secret mounts produce.
///   </description></item>
///   <item><description>
///     <c>FISHMMO_VERSION_MANIFEST_SIGNING_KEY</c> environment variable.
///   </description></item>
///   <item><description>
///     <c>Signing:VersionManifestPrivateKeyBase64</c> in configuration. Least preferred: it means
///     the key is sitting in an appsettings file that is easy to copy into a repository by
///     accident. Supported so a single-box deployment is not blocked, not recommended.
///   </description></item>
/// </list>
/// <para>
/// <b>The key is never logged.</b> Not at Debug, not in an exception message, not truncated. The
/// only key material this class ever emits is the derived PUBLIC key, at Info, on startup —
/// which is deliberate and useful: it is exactly the value that must be embedded in the client as
/// <c>GeneratedPinSet.VersionManifestPublicKeyBase64</c>, and an operator comparing the two is the
/// cheapest way to catch a mismatched pair before it reaches players.
/// </para>
/// <para>
/// <b>No key configured: refuse to start in Production.</b> The alternative — serve unsigned and
/// log loudly — was rejected. The log entry would be written on a host nobody is watching, while
/// the thing it is warning about is invisible from the outside: an unsigned manifest and a
/// manifest whose signature an attacker stripped are the same document, so nothing downstream can
/// tell "signing was never configured here" from "signing was removed in transit". A server that
/// starts and serves is a server that is in production, and this is the last CRITICAL in the patch
/// path; a deployment that reintroduces it should not be reachable by players at all. Refusing to
/// start puts the failure in front of the operator at deploy time, when a keypair is one
/// <c>ManifestSigner keygen</c> away, instead of in a log file after the fact. There is
/// deliberately no override flag: an override is how "temporarily unsigned" becomes permanent.
/// Outside Production the server starts, serves an empty <c>signature</c> field and logs at Error
/// on every request, so development is not gated on key management.
/// </para>
/// </remarks>
public sealed class VersionManifestSigner : IDisposable
{
	private const string logChannel = "VersionManifestSigner";

	/// <summary>Configuration key: path to a file holding the base64 private key.</summary>
	public const string PrivateKeyFileSetting = "Signing:VersionManifestPrivateKeyFile";

	/// <summary>Configuration key: the base64 private key, inline.</summary>
	public const string PrivateKeyInlineSetting = "Signing:VersionManifestPrivateKeyBase64";

	/// <summary>Environment variable carrying the base64 private key.</summary>
	public const string PrivateKeyEnvironmentVariable = "FISHMMO_VERSION_MANIFEST_SIGNING_KEY";

	private byte[]? privateSeed;

	/// <summary>
	/// Base64 of the public key matching the loaded private key, or null when unconfigured.
	/// This is the value that belongs in <c>GeneratedPinSet.VersionManifestPublicKeyBase64</c>.
	/// </summary>
	public string? PublicKeyBase64 { get; }

	/// <summary>True when a usable signing key was loaded.</summary>
	public bool IsConfigured => privateSeed != null;

	public VersionManifestSigner(IHostEnvironment env, IConfiguration config)
	{
		string? source = null;
		string? keyText = ReadKeyText(config, out source);

		if (string.IsNullOrWhiteSpace(keyText))
		{
			// See the class remarks for why Production is a hard stop and Development is not.
			if (env.IsProduction())
			{
				throw new InvalidOperationException(
					"No Ed25519 version-manifest signing key is configured, and the patcher refuses to serve an " +
					"unsigned version manifest in Production. The sha256 field it carries is the only integrity " +
					"check applied to a patch archive.\n" +
					"  Generate a keypair:  dotnet run --project Tools/ManifestSigner -- keygen --out-dir /etc/fishmmo/patcher\n" +
					$"  Then set one of:     {PrivateKeyFileSetting} (preferred), " +
					$"{PrivateKeyEnvironmentVariable}, or {PrivateKeyInlineSetting}.\n" +
					"  Embed the printed PUBLIC key in the client as GeneratedPinSet.VersionManifestPublicKeyBase64.");
			}

			_ = Log.Error(logChannel,
				$"VERSION MANIFEST SIGNING IS DISABLED: no key found via {PrivateKeyFileSetting}, " +
				$"{PrivateKeyEnvironmentVariable} or {PrivateKeyInlineSetting}. Every /latest_version response will " +
				"carry an empty signature and any client with a verification key embedded will refuse it. " +
				"A Production environment would refuse to start in this state.");
			return;
		}

		if (!ManifestSigning.TryDecodePrivateKey(keyText, out byte[]? seed, out string? error))
		{
			// The message names the SOURCE, never the value.
			throw new InvalidOperationException(
				$"The version-manifest signing key supplied via {source} could not be used: {error}");
		}

		privateSeed = seed;
		PublicKeyBase64 = Convert.ToBase64String(ManifestSigning.DerivePublicKey(privateSeed!));

		_ = Log.Info(logChannel,
			$"Version manifest signing enabled (key source: {source}). " +
			$"Client must embed GeneratedPinSet.VersionManifestPublicKeyBase64 = \"{PublicKeyBase64}\"");
	}

	/// <summary>
	/// Reads the key text from the highest-priority configured source.
	/// </summary>
	/// <remarks>
	/// The file path is resolved and read here rather than being fed through the configuration
	/// system so that a missing or unreadable key file is a hard error naming the path, not a
	/// silent fall-through to the next source. Falling through would mean a typo in the path
	/// quietly selects a different key — or no key — which is precisely the class of mistake
	/// signing exists to make impossible.
	/// </remarks>
	private static string? ReadKeyText(IConfiguration config, out string? source)
	{
		string? path = config[PrivateKeyFileSetting];
		if (!string.IsNullOrWhiteSpace(path))
		{
			source = $"{PrivateKeyFileSetting} ('{path}')";
			try
			{
				return File.ReadAllText(path).Trim();
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException(
					$"{PrivateKeyFileSetting} is set to '{path}' but the file could not be read: {ex.Message}. " +
					"Refusing to fall back to another key source — a typo here must not silently change which key signs releases.");
			}
		}

		string? fromEnv = Environment.GetEnvironmentVariable(PrivateKeyEnvironmentVariable);
		if (!string.IsNullOrWhiteSpace(fromEnv))
		{
			source = PrivateKeyEnvironmentVariable;
			return fromEnv.Trim();
		}

		string? inline = config[PrivateKeyInlineSetting];
		if (!string.IsNullOrWhiteSpace(inline))
		{
			source = PrivateKeyInlineSetting;
			return inline.Trim();
		}

		source = null;
		return null;
	}

	/// <summary>
	/// Wraps <paramref name="writer"/>'s fields in a complete, signed JSON document.
	/// </summary>
	/// <returns>The exact bytes to write to the response.</returns>
	/// <remarks>
	/// When no key is configured (non-Production only) the document is still emitted with a
	/// <c>signature</c> field, carrying an empty value. That is deliberate: the field must always
	/// be present and always in the same place, so the only difference between a signed and an
	/// unsigned deployment is whether the value is filled in. An absent field would make
	/// "unsigned" a different document SHAPE, and the client's verifier returns null — refusing to
	/// guess a canonical form — the moment it cannot find the field.
	/// </remarks>
	public string BuildDocument(ManifestJsonWriter writer)
	{
		string body = writer.Build();

		byte[]? seed = privateSeed;
		if (seed == null)
		{
			return "{" + (body.Length > 0 ? body + ", " : "") + ManifestSigning.BlankSignatureField + "}";
		}

		return ManifestSigning.SignDocument(body, seed);
	}

	/// <summary>
	/// Zeroes the private seed. The singleton lives for the process lifetime, so this only
	/// matters on a clean shutdown, but leaving key material in a heap that may be swapped or
	/// core-dumped for no reason is not a trade worth making.
	/// </summary>
	public void Dispose()
	{
		byte[]? seed = privateSeed;
		privateSeed = null;
		if (seed != null)
		{
			Array.Clear(seed, 0, seed.Length);
		}
	}
}
