namespace FishMMO.Shared
{
	/// <summary>
	/// IL-embedded host configuration.
	///
	/// The defaults here are sentinels, and this file is tracked so the project always
	/// compiles. The real values live in the gitignored HostConfig.generated.cs, written
	/// by FishMMO Dashboard > Game Settings (or by CI substituting the sentinels in
	/// GeneratedFileTemplates/HostConfig.generated.cs.template), which implements
	/// <see cref="Configure"/> to overwrite them.
	///
	/// Why the split: a fresh clone has no generated file. When the generated file
	/// declared the whole class, its absence was a compile error, and a project that
	/// fails to compile on first open drops into Safe Mode, where no editor code runs —
	/// including the FishMMO menu that writes the file. An unimplemented partial method
	/// is removed by the compiler, so a missing generated file now just means sentinels,
	/// which ClientSecurityBuildValidator refuses to ship.
	/// </summary>
	public static partial class HostConfig
	{
		public const string SentinelMarker = "FISHMMO_SENTINEL_PLACEHOLDER";

		/// <summary>API gateway URL with trailing slash (e.g. "https://api.fishmmo.com/").</summary>
		public static readonly string ApiHost;

		/// <summary>Game server hostname for WebTransport/QUIC connections.</summary>
		public static readonly string GameHost;

		/// <summary>WebGL client / player-facing hostname.</summary>
		public static readonly string PlayHost;

		/// <summary>Root domain (no subdomain) for TLS certs and email.</summary>
		public static readonly string RootDomain;

		/// <summary>SMTP from address for verification emails.</summary>
		public static readonly string SmtpFromAddress;

		/// <summary>SMTP from display name.</summary>
		public static readonly string SmtpFromName;

		/// <summary>Launcher HTML/news page URL.</summary>
		public static readonly string LauncherHtmlUrl;

		static HostConfig()
		{
			var values = new Values();
			Configure(values);

			ApiHost = values.ApiHost;
			GameHost = values.GameHost;
			PlayHost = values.PlayHost;
			RootDomain = values.RootDomain;
			SmtpFromAddress = values.SmtpFromAddress;
			SmtpFromName = values.SmtpFromName;
			LauncherHtmlUrl = values.LauncherHtmlUrl;
		}

		/// <summary>
		/// Implemented by HostConfig.generated.cs. Assigns only the values it knows, so a
		/// generated file written before a value was added still compiles and leaves that
		/// value at its sentinel.
		/// </summary>
		static partial void Configure(Values values);

		/// <summary>
		/// The values <see cref="Configure"/> may overwrite, initialised to sentinels.
		/// </summary>
		private sealed class Values
		{
			public string ApiHost = "https://" + SentinelMarker + "_API_HOST/";
			public string GameHost = SentinelMarker + "_GAME_HOST";
			public string PlayHost = SentinelMarker + "_PLAY_HOST";
			public string RootDomain = SentinelMarker + "_ROOT_DOMAIN";
			public string SmtpFromAddress = "noreply@" + SentinelMarker + "_ROOT_DOMAIN";
			public string SmtpFromName = "FishMMO";
			public string LauncherHtmlUrl = "https://www." + SentinelMarker + "_ROOT_DOMAIN/docs/introduction.html";
		}
	}
}
