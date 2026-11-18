namespace FishMMO.Client.Security
{
	/// <summary>
	/// IL-embedded certificate pin set.
	///
	/// The defaults here are sentinels, and this file is tracked so the project always
	/// compiles. The real values live in the gitignored CertificatePins.generated.cs,
	/// written by FishMMO Dashboard > Game Settings (or by CI substituting the sentinels
	/// in GeneratedFileTemplates/CertificatePins.generated.cs.template), which implements
	/// <see cref="Configure"/> to overwrite them. See <see cref="FishMMO.Shared.HostConfig"/>
	/// for why the generated file no longer declares the whole class.
	/// </summary>
	public static partial class CertificatePinSet
	{
		/// <summary>
		/// Sentinel string the build validator checks for. CI replaces the
		/// entire sentinel value (including this marker) with the real pin.
		/// </summary>
		public const string SentinelMarker = "FISHMMO_SENTINEL_PLACEHOLDER";

		/// <summary>
		/// SHA-256 SPKI pins (base64). Minimum 2 entries required for
		/// release builds — one active key + one backup for rotation.
		/// </summary>
		public static readonly string[] Pins;

		/// <summary>
		/// Ed25519 public key (base64) for verifying signed pin update
		/// manifests from the API. Empty string disables runtime updates.
		/// </summary>
		public static readonly string ManifestPublicKeyBase64;

		/// <summary>
		/// Ed25519 public key (base64) for verifying the signed version manifest
		/// served by /latest_version. Empty string means signing is not deployed:
		/// the patch SHA-256 is then only as trustworthy as the API gateway, which
		/// HttpPatchServerService reports at Error level in a release build.
		/// </summary>
		public static readonly string VersionManifestPublicKeyBase64;

		static CertificatePinSet()
		{
			var values = new Values();
			Configure(values);

			Pins = values.Pins ?? new string[0];
			ManifestPublicKeyBase64 = values.ManifestPublicKeyBase64 ?? "";
			VersionManifestPublicKeyBase64 = values.VersionManifestPublicKeyBase64 ?? "";
		}

		/// <summary>
		/// Implemented by CertificatePins.generated.cs. Assigns only the values it knows,
		/// so a generated file written before a value was added still compiles and leaves
		/// that value at its default.
		/// </summary>
		static partial void Configure(Values values);

		/// <summary>
		/// The values <see cref="Configure"/> may overwrite, initialised to defaults.
		/// </summary>
		private sealed class Values
		{
			public string[] Pins =
			{
				SentinelMarker + "_ACTIVE_PIN",
				SentinelMarker + "_BACKUP_PIN",
			};
			public string ManifestPublicKeyBase64 = "";
			public string VersionManifestPublicKeyBase64 = "";
		}
	}
}
