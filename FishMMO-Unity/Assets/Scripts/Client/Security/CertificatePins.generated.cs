// ═══════════════════════════════════════════════════════════════════════════════
// AUTO-GENERATED — written by FishMMO > Security > Fetch Certificate Pins.
// Do NOT edit manually.  Pins are fetched from live TLS connections and written
// directly to this file by the Unity Editor tool.
// ═══════════════════════════════════════════════════════════════════════════════
//
// Sentinel check: the build validator (ClientSecurityBuildValidator) blocks
// non-development builds that contain the FISHMMO_SENTINEL_PLACEHOLDER marker.
// Run FishMMO > Security > Fetch Certificate Pins to replace the sentinels.

namespace FishMMO.Client.Security
{
	/// <summary>
	/// IL-embedded certificate pin set. The real values are substituted at
	/// build time by CI. The committed sentinel values are intentionally
	/// invalid so pinning cannot accidentally ship with empty values.
	/// </summary>
	public static class GeneratedPinSet
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
		public static readonly string[] Pins =
		{
			"FISHMMO_SENTINEL_PLACEHOLDER_ACTIVE_PIN",
			"FISHMMO_SENTINEL_PLACEHOLDER_BACKUP_PIN",
		};

		/// <summary>
		/// Ed25519 public key (base64) for verifying signed pin update
		/// manifests from the API. Empty string disables runtime updates.
		/// </summary>
		internal const string ManifestPublicKeyBase64 = "";
	}
}
