using FishMMO.Client;
using FishNet.Transporting.WebTransport;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for explaining a sign-in connection that failed before the login server answered.
	/// </summary>
	/// <remarks>
	/// The game connects only over QUIC. Security software that intercepts it answers the
	/// handshake itself, in about a millisecond, and never forwards the game's messages; a
	/// blocked network never opens the connection at all. Each shape gets its own explanation
	/// and its own section of the help page.
	/// </remarks>
	public class ConnectionTroubleTests
	{
		private static ClientConnectDiagnosis Diagnosis(bool opened, double openMs, bool serverData = false,
			string failure = null, bool tls = false, bool attempted = true)
		{
			return new ClientConnectDiagnosis(attempted, opened, openMs, serverData, failure, tls);
		}

		[Test]
		public void NoAttempt_IsNothingToExplain()
		{
			Assert.AreEqual(ConnectionTroubleKind.None, ConnectionTrouble.Classify(default));
			Assert.AreEqual(ConnectionTroubleKind.None,
				ConnectionTrouble.Classify(Diagnosis(false, -1, attempted: false)));
		}

		[Test]
		public void AnyServerData_IsNothingToExplain()
		{
			// The server spoke, so whatever followed is its own explanation, even a fast open on a LAN.
			Assert.AreEqual(ConnectionTroubleKind.None, ConnectionTrouble.Classify(Diagnosis(true, 0.4, serverData: true)));
			Assert.AreEqual(ConnectionTroubleKind.None, ConnectionTrouble.Classify(Diagnosis(true, 40, serverData: true)));
		}

		[Test]
		public void NeverOpened_IsBlocked()
		{
			Assert.AreEqual(ConnectionTroubleKind.NeverOpened,
				ConnectionTrouble.Classify(Diagnosis(false, -1, failure: "Ready failed: Opening handshake failed.")));
			Assert.AreEqual(ConnectionTroubleKind.NeverOpened, ConnectionTrouble.Classify(Diagnosis(false, -1)));
		}

		[Test]
		public void InstantOpenWithSilence_IsInterception()
		{
			// The confirmed ESET case answered in about 1 ms.
			Assert.AreEqual(ConnectionTroubleKind.Intercepted, ConnectionTrouble.Classify(Diagnosis(true, 1.0)));
			Assert.AreEqual(ConnectionTroubleKind.Intercepted,
				ConnectionTrouble.Classify(Diagnosis(true, ConnectionTrouble.LocalOpenMilliseconds - 0.01)));
		}

		[Test]
		public void NormalOpenWithSilence_IsNoAnswer()
		{
			Assert.AreEqual(ConnectionTroubleKind.NoAnswer,
				ConnectionTrouble.Classify(Diagnosis(true, ConnectionTrouble.LocalOpenMilliseconds)));
			Assert.AreEqual(ConnectionTroubleKind.NoAnswer, ConnectionTrouble.Classify(Diagnosis(true, 85)));
		}

		[Test]
		public void TlsFailure_IsCertificateRejected_EvenUnopened()
		{
			Assert.AreEqual(ConnectionTroubleKind.CertificateRejected,
				ConnectionTrouble.Classify(Diagnosis(false, -1, failure: "TLS error", tls: true)));
		}

		[Test]
		public void Products_MatchSecurityCenterNames()
		{
			AssertProduct("ESET Security", "eset", "ESET");
			AssertProduct("ESET NOD32 Antivirus", "eset", "ESET");
			AssertProduct("Kaspersky Premium", "kaspersky", "Kaspersky");
			AssertProduct("Bitdefender Antivirus", "bitdefender", "Bitdefender");
			AssertProduct("Norton 360", "norton", "Norton");
			AssertProduct("Avast Antivirus", "avast-avg", "Avast");
			AssertProduct("AVG Antivirus", "avast-avg", "AVG");
			AssertProduct("McAfee VirusScan", "mcafee", "McAfee");
			AssertProduct("Malwarebytes", "malwarebytes", "Malwarebytes");
		}

		[Test]
		public void Defender_AndUnknownProducts_AreNotNamed()
		{
			Assert.IsFalse(ConnectionTrouble.TryMatchProduct(new[] { "Windows Defender" }, out _, out _));
			Assert.IsFalse(ConnectionTrouble.TryMatchProduct(new[] { "Microsoft Defender Antivirus" }, out _, out _));
			Assert.IsFalse(ConnectionTrouble.TryMatchProduct(new[] { "Some Other Shield" }, out _, out _));
			Assert.IsFalse(ConnectionTrouble.TryMatchProduct(null, out _, out _));

			// Defender listed first does not hide the product that matters.
			Assert.IsTrue(ConnectionTrouble.TryMatchProduct(new[] { "Windows Defender", "ESET Security" }, out string anchor, out _));
			Assert.AreEqual("eset", anchor);
		}

		[Test]
		public void HelpUrl_PicksTheSection()
		{
			Assert.AreEqual("https://play.example.com/help/connection.html#eset",
				ConnectionTrouble.HelpUrl("play.example.com", ConnectionTroubleKind.Intercepted, "eset"));
			Assert.AreEqual("https://play.example.com/help/connection.html#blocked",
				ConnectionTrouble.HelpUrl("play.example.com", ConnectionTroubleKind.NeverOpened, null));
			Assert.AreEqual("https://play.example.com/help/connection.html#security-software",
				ConnectionTrouble.HelpUrl("play.example.com", ConnectionTroubleKind.Intercepted, null));
			Assert.AreEqual("https://play.example.com/help/connection.html#security-software",
				ConnectionTrouble.HelpUrl("play.example.com", ConnectionTroubleKind.NoAnswer, null));
		}

		[Test]
		public void Message_MentionsTheButtonOnlyWhenThereIsOne()
		{
			foreach (ConnectionTroubleKind kind in new[] { ConnectionTroubleKind.NeverOpened, ConnectionTroubleKind.Intercepted,
				ConnectionTroubleKind.NoAnswer, ConnectionTroubleKind.CertificateRejected })
			{
				StringAssert.Contains(ConnectionTrouble.HelpButtonLabel, ConnectionTrouble.Message(kind, null), kind.ToString());
				StringAssert.Contains("ESET", ConnectionTrouble.Message(kind, "ESET"), kind.ToString());
				StringAssert.DoesNotContain(ConnectionTrouble.HelpButtonLabel,
					ConnectionTrouble.Message(kind, "ESET", helpLink: false), kind.ToString());
			}
			Assert.IsNull(ConnectionTrouble.Message(ConnectionTroubleKind.None, null));
		}

		private static void AssertProduct(string reported, string anchor, string display)
		{
			Assert.IsTrue(ConnectionTrouble.TryMatchProduct(new[] { reported }, out string a, out string d), reported);
			Assert.AreEqual(anchor, a, reported);
			Assert.AreEqual(display, d, reported);
		}
	}
}
