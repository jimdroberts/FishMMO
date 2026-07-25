// ═══════════════════════════════════════════════════════════════════════════════
// AUTO-GENERATED — CI substitutes the real secret before release builds.
// Do NOT edit manually.  Set FISHMMO_CLIENT_GATE_SECRET in CI.
// ═══════════════════════════════════════════════════════════════════════════════
//
// Sentinel check: the build validator (ClientSecurityBuildValidator) blocks
// non-development builds that contain the FISHMMO_SENTINEL_PLACEHOLDER marker.
// CI must replace this value before invoking Unity for a release build.

namespace FishMMO.Client.Security
{
	/// <summary>
	/// IL-embedded client gate secret. The real value is substituted at
	/// build time by CI from the FISHMMO_CLIENT_GATE_SECRET env var.
	/// </summary>
	internal static class GeneratedClientSecret
	{
		/// <summary>
		/// Shared secret for X-FishMMO-Client HMAC header signing.
		/// The committed value is a sentinel — CI must replace it.
		/// </summary>
		internal const string Secret = "FISHMMO_SENTINEL_PLACEHOLDER_CLIENT_GATE_SECRET";
	}
}
