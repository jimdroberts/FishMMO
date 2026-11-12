using System;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Transporting.WebTransport;
using FishMMO.Logging;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>What went wrong with a connection that failed before the server said anything.</summary>
	public enum ConnectionTroubleKind
	{
		/// <summary>Nothing to explain: no attempt, or the server answered.</summary>
		None,
		/// <summary>The connection never opened: QUIC/UDP blocked, or the server unreachable.</summary>
		NeverOpened,
		/// <summary>It opened, but the server never sent anything.</summary>
		NoAnswer,
		/// <summary>It opened faster than any real round trip and the server never sent anything: intercepted on this machine.</summary>
		Intercepted,
		/// <summary>The server's certificate was rejected: something between is presenting its own.</summary>
		CertificateRejected,
	}

	/// <summary>
	/// Explains a sign-in connection that failed before the login server answered, and links the
	/// player to the connection help page.
	/// </summary>
	/// <remarks>
	/// The game connects only over QUIC (WebTransport), with no fallback. Players whose security
	/// software or network blocks it otherwise see a generic "could not sign in". The transport
	/// records how the attempt went (<see cref="ClientConnectDiagnosis"/>), and this turns that
	/// into an explanation.
	/// <para>
	/// An open time under <see cref="LocalOpenMilliseconds"/> cannot be a real server: a QUIC
	/// handshake plus the WebTransport request is at least one round trip. The confirmed case
	/// (ESET, 2026-11-12) answered in about 1 ms. A player on the same machine or network as the
	/// server can also open that fast; they see this only if that server then says nothing.
	/// </para>
	/// </remarks>
	public static class ConnectionTrouble
	{
		/// <summary>An open faster than this is taken to be local interception.</summary>
		public const double LocalOpenMilliseconds = 3.0;

		/// <summary>The help page, relative to the play host.</summary>
		public const string HelpPath = "/help/connection.html";

		/// <summary>The accept button's label on a trouble notice.</summary>
		public const string HelpButtonLabel = "Get help";

		/// <summary>Help-page sections by a fragment of the product name Security Center reports.</summary>
		private static readonly (string fragment, string anchor, string displayName)[] Products =
		{
			("ESET", "eset", "ESET"),
			("Kaspersky", "kaspersky", "Kaspersky"),
			("Bitdefender", "bitdefender", "Bitdefender"),
			("Norton", "norton", "Norton"),
			("Avast", "avast-avg", "Avast"),
			("AVG", "avast-avg", "AVG"),
			("McAfee", "mcafee", "McAfee"),
			("Malwarebytes", "malwarebytes", "Malwarebytes"),
		};

		/// <summary>Classifies a diagnosis. Pure, for tests.</summary>
		public static ConnectionTroubleKind Classify(ClientConnectDiagnosis diagnosis)
		{
			if (!diagnosis.Attempted || diagnosis.ServerDataReceived)
			{
				return ConnectionTroubleKind.None;
			}
			if (diagnosis.TlsFailure)
			{
				return ConnectionTroubleKind.CertificateRejected;
			}
			if (!diagnosis.Opened)
			{
				return ConnectionTroubleKind.NeverOpened;
			}
			if (diagnosis.OpenMilliseconds >= 0.0 && diagnosis.OpenMilliseconds < LocalOpenMilliseconds)
			{
				return ConnectionTroubleKind.Intercepted;
			}
			return ConnectionTroubleKind.NoAnswer;
		}

		/// <summary>
		/// The first recognised antivirus product among <paramref name="names"/>, as its help-page anchor and display name.
		/// </summary>
		/// <remarks>
		/// Microsoft Defender is skipped: it registers alongside every other product and does not
		/// inspect connections. Pure, for tests.
		/// </remarks>
		public static bool TryMatchProduct(IReadOnlyList<string> names, out string anchor, out string displayName)
		{
			anchor = null;
			displayName = null;
			if (names == null)
			{
				return false;
			}
			foreach (string name in names)
			{
				if (string.IsNullOrEmpty(name))
				{
					continue;
				}
				foreach (var product in Products)
				{
					if (name.IndexOf(product.fragment, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						anchor = product.anchor;
						displayName = product.displayName;
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>The help-page link for <paramref name="kind"/>, pointing at the product's section when one is known.</summary>
		public static string HelpUrl(string playHost, ConnectionTroubleKind kind, string productAnchor)
		{
			string anchor = !string.IsNullOrEmpty(productAnchor)
				? productAnchor
				: kind == ConnectionTroubleKind.NeverOpened ? "blocked" : "security-software";
			return "https://" + playHost + HelpPath + "#" + anchor;
		}

		/// <summary>The player-facing explanation. <paramref name="productName"/> is the detected antivirus, or null.</summary>
		/// <param name="kind">What went wrong.</param>
		/// <param name="productName">The detected antivirus, or null.</param>
		/// <param name="helpLink">Whether the notice carries the help button the text refers to.</param>
		public static string Message(ConnectionTroubleKind kind, string productName, bool helpLink = true)
		{
			string product = string.IsNullOrEmpty(productName)
				? string.Empty
				: $"\n\nWindows reports that {productName} is installed." +
					(helpLink ? $" Press {HelpButtonLabel} for steps to let the game through it." : string.Empty);
			string generic = string.IsNullOrEmpty(productName) && helpLink
				? $"\n\nPress {HelpButtonLabel} for steps to fix it."
				: string.Empty;

			switch (kind)
			{
				case ConnectionTroubleKind.NeverOpened:
					return "Could not reach the login server.\n\n" +
						"The game connects over QUIC (UDP), and something seems to be blocking it: " +
						"antivirus or firewall software on this computer, or a school, work or public network. " +
						"The server may also be down for a moment." + product + generic;
				case ConnectionTroubleKind.Intercepted:
					return "Could not sign in.\n\n" +
						"The connection was answered instantly, by something on this computer rather than the login server. " +
						"This usually means security software that scans web or encrypted traffic is intercepting the game." +
						product + generic;
				case ConnectionTroubleKind.CertificateRejected:
					return "Could not sign in.\n\n" +
						"The login server's security certificate was replaced on the way here. " +
						"Security software that scans encrypted traffic, or a network that inspects it, does this." +
						product + generic;
				case ConnectionTroubleKind.NoAnswer:
					return "Could not sign in.\n\n" +
						"The connection to the login server opened, but the server never answered. " +
						"Make sure your client is up to date. If it keeps happening, security software that scans " +
						"web or encrypted traffic may be interfering with the game." + product + generic;
				default:
					return null;
			}
		}

		/// <summary>
		/// Explains the client's last connection attempt if it failed before the server answered.
		/// </summary>
		/// <param name="networkManager">The client's network manager.</param>
		/// <param name="kind">What went wrong, or <see cref="ConnectionTroubleKind.None"/>.</param>
		/// <returns>True when a notice with a help link was queued.</returns>
		public static bool TryNotify(NetworkManager networkManager, out ConnectionTroubleKind kind)
		{
			kind = ConnectionTroubleKind.None;
			WebTransport transport = networkManager != null && networkManager.TransportManager != null
				? networkManager.TransportManager.GetTransport<WebTransport>()
				: null;
			if (transport == null)
			{
				return false;
			}

			ClientConnectDiagnosis diagnosis = transport.GetClientConnectDiagnosis();
			kind = Classify(diagnosis);
			if (kind == ConnectionTroubleKind.None)
			{
				return false;
			}

			TryMatchProduct(InstalledSecuritySoftware.AntivirusNames, out string anchor, out string productName);
			Log.Info("ConnectionTrouble", $"Sign-in connection failed before the server answered: {kind} ({diagnosis})" +
				(productName != null ? $"; antivirus {productName}" : string.Empty));

			string playHost = string.IsNullOrEmpty(HostConfig.PlayHost) ? HostConfig.RootDomain : HostConfig.PlayHost;
			if (string.IsNullOrEmpty(playHost))
			{
				LoginNotice.Show(Message(kind, productName, helpLink: false));
				return true;
			}
			LoginNotice.Show(Message(kind, productName), HelpUrl(playHost, kind, anchor), HelpButtonLabel);
			return true;
		}
	}
}
