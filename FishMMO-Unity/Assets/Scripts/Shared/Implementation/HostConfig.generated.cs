// ═══════════════════════════════════════════════════════════════════════════════
// AUTO-GENERATED — written by FishMMO Dashboard > Game Settings.
// Do NOT edit manually.  Use the FishMMO-Installer or set env vars in CI.
// Generated at: 2026-08-12T01:19:36.9471104Z
// ═══════════════════════════════════════════════════════════════════════════════
//
// Sentinel check: the build validator (ClientSecurityBuildValidator) blocks
// non-development builds that contain the FISHMMO_SENTINEL_PLACEHOLDER marker.
// CI must replace every occurrence before invoking Unity.
//
// CI env vars:
//   FISHMMO_API_HOST   — API gateway URL  (e.g. "https://api.fishmmo.com")
//   FISHMMO_GAME_HOST  — Game server host  (e.g. "game.fishmmo.com")
//   FISHMMO_PLAY_HOST  — WebGL client host (e.g. "play.fishmmo.com")
//   FISHMMO_ROOT_DOMAIN — Root domain for certs/smtp (e.g. "fishmmo.com")

namespace FishMMO.Shared
{
	/// <summary>
	/// IL-embedded host configuration. The real values are substituted at
	/// build time by CI or the FishMMO-Installer. The committed sentinel
	/// values are intentionally invalid.
	/// </summary>
	public static class GeneratedHostConfig
	{
		public const string SentinelMarker = "FISHMMO_SENTINEL_PLACEHOLDER";

		/// <summary>API gateway URL with trailing slash (e.g. "https://api.fishmmo.com/").</summary>
		public const string ApiHost =
			"http://127.0.0.1:8888/";

		/// <summary>Game server hostname for WebTransport/QUIC connections.</summary>
		public const string GameHost =
			"127.0.0.1";

		/// <summary>WebGL client / player-facing hostname.</summary>
		public const string PlayHost =
			"127.0.0.1";

		/// <summary>Root domain (no subdomain) for TLS certs and email.</summary>
		public const string RootDomain =
			"localhost";

		/// <summary>SMTP from address for verification emails.</summary>
		public const string SmtpFromAddress =
			"noreply@localhost";

		/// <summary>SMTP from display name.</summary>
		public const string SmtpFromName = "FishMMO";

		/// <summary>Launcher HTML/news page URL.</summary>
		public const string LauncherHtmlUrl =
			"https://www.localhost/docs/introduction.html";
	}
}
