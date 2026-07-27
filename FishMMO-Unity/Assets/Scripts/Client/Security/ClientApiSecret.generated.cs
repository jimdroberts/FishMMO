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
	/// <remarks>
	/// Must be <c>public</c> so the Editor assembly
	/// (<c>FishMMO.Client.Security.Editor</c> / <see cref="Editor.ClientSecurityBuildValidator"/>)
	/// can read the sentinel. <c>internal</c> is assembly-scoped only and caused CS0122.
	/// Matches <see cref="GeneratedPinSet"/> and ClientSecretTool output.
	/// </remarks>
	public static class GeneratedClientSecret
	{
		/// <summary>
		/// Shared secret for X-FishMMO-Client HMAC header signing.
		/// The committed value is a sentinel — CI must replace it.
		/// </summary>
		public const string Secret = "FISHMMO_SENTINEL_PLACEHOLDER_CLIENT_GATE_SECRET";
	}
}
