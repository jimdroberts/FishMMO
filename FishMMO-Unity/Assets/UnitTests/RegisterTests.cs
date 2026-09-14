using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishMMO.Database.Data;
using FishMMO.UnitTests.Harness;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// EditMode tests covering the client-side registration flow through
	/// <c>ClientAuthenticatorCore</c>. Account creation is processed server-side by
	/// <c>AccountCreationSystem</c> (Unity), which lives outside the FishMMO-Auth DLLs;
	/// these tests therefore stop after asserting that the encrypted
	/// <c>CreateAccount</c> broadcast was emitted with well-formed payloads.
	/// </summary>
	[TestFixture]
	public class RegisterTests
	{
		private static async Task DriveHandshakeAndCapture(AuthTestHarness h, string user, string password, string email, int age)
		{
			await AuthTestTrace.Log("RegisterTests", "STEP", $"Attempting registration handshake for user '{user}' with email '{email}' and age {age}.");
			LogAssert.IsTrue(h.Client.SetLoginCredentials(user, password, register: true, email: email, age: age),
				"SetLoginCredentials returned false for valid registration credentials.");
			h.Client.OnConnected();
			await Task.Yield();
		}

		[Test]
		public async Task Register_HappyPath_SendsEncryptedCreateAccountBroadcast()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Register_HappyPath_SendsEncryptedCreateAccountBroadcast),
					"Test: Happy path registration.\n"
					+ "Procedure: Register a new user with valid credentials, email, and age.\n"
					+ "Expected: A well-formed encrypted CreateAccount broadcast is emitted, and the client does not disconnect.\n"
					+ "Failure: If the broadcast is missing, malformed, or the client disconnects, registration flow is broken.\n"
					+ "This test ensures the registration happy path works end-to-end."
				);
				using AuthTestHarness h = new AuthTestHarness();
				await DriveHandshakeAndCapture(h, "frank", "p@ssword1!", "frank@example.test", age: 21);

				await AuthTestTrace.Log("RegisterTests", "STEP", "Checking CreateAccount broadcast count and payloads...");
				LogAssert.AreEqual(1, h.Client.CreateAccountSends.Count,
					"Exactly one CreateAccount broadcast should be emitted on the happy path.");

				TestClientCore.CreateAccountCapture sent = h.Client.CreateAccountSends[0];
				LogAssert.IsNotNull(sent.EncryptedUsername);
				LogAssert.IsTrue(sent.EncryptedUsername.Length > 0, "Encrypted username payload was empty.");
				LogAssert.IsTrue(sent.EncryptedEmail.Length > 0, "Encrypted email payload was empty.");
				LogAssert.IsTrue(sent.EncryptedAge.Length > 0, "Encrypted age payload was empty.");
				LogAssert.IsTrue(sent.EncryptedSalt.Length > 0, "Encrypted salt payload was empty.");
				LogAssert.IsTrue(sent.EncryptedVerifier.Length > 0, "Encrypted verifier payload was empty.");
				LogAssert.IsFalse(h.Client.WasDisconnected, "Client should not have disconnected on a valid handshake.");
				await AuthTestTrace.Log("RegisterTests", "SUCCESS", nameof(Register_HappyPath_SendsEncryptedCreateAccountBroadcast));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("RegisterTests", "FAILURE", $"{nameof(Register_HappyPath_SendsEncryptedCreateAccountBroadcast)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Register_HappyPath_SendsEncryptedCreateAccountBroadcast));
			}
		}

		[Test]
		public async Task Register_EmptyEmail_DisconnectsBeforeCreateAccount()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Register_EmptyEmail_DisconnectsBeforeCreateAccount),
					"Test: Registration with empty email.\n"
					+ "Procedure: Attempt registration with an empty email field.\n"
					+ "Expected: The client rejects the registration before handshake, and no CreateAccount broadcast is sent.\n"
					+ "Failure: If the client accepts or emits a broadcast, email validation is broken.\n"
					+ "This test ensures email is required for registration."
				);
				using AuthTestHarness h = new AuthTestHarness();
				await AuthTestTrace.Log("RegisterTests", "STEP", "Attempting registration with empty email...");
				bool accepted = h.Client.SetLoginCredentials("grace", "pw1", register: true, email: "", age: 25);
				LogAssert.IsFalse(accepted, "Empty email must be rejected by SetLoginCredentials.");
				LogAssert.AreEqual(0, h.Client.CreateAccountSends.Count, "No broadcast should have been sent.");
				await AuthTestTrace.Log("RegisterTests", "SUCCESS", nameof(Register_EmptyEmail_DisconnectsBeforeCreateAccount));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("RegisterTests", "FAILURE", $"{nameof(Register_EmptyEmail_DisconnectsBeforeCreateAccount)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Register_EmptyEmail_DisconnectsBeforeCreateAccount));
			}
		}

		[Test]
		public void Register_InvalidUsername_RejectedByClient()
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(Register_InvalidUsername_RejectedByClient),
					"Test: Registration with invalid username.\n"
					+ "Procedure: Attempt registration with a username shorter than 3 characters.\n"
					+ "Expected: The client rejects the registration before handshake.\n"
					+ "Failure: If the client accepts or proceeds, username validation is broken.\n"
					+ "This test ensures username length rules are enforced."
				).GetAwaiter().GetResult();
				using AuthTestHarness h = new AuthTestHarness();
				AuthTestTrace.Log("RegisterTests", "STEP", "Attempting registration with username < 3 chars...").GetAwaiter().GetResult();
				bool ok = h.Client.SetLoginCredentials("ab", "pw1", register: true, email: "x@y.test", age: 20);
				LogAssert.IsFalse(ok, "Usernames shorter than 3 chars must be rejected by SetLoginCredentials.");
				AuthTestTrace.Log("RegisterTests", "SUCCESS", nameof(Register_InvalidUsername_RejectedByClient)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("RegisterTests", "FAILURE", $"{nameof(Register_InvalidUsername_RejectedByClient)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Register_InvalidUsername_RejectedByClient)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void Register_InvalidPassword_RejectedByClient()
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(Register_InvalidPassword_RejectedByClient),
					"Test: Registration with empty password.\n"
					+ "Procedure: Attempt registration with an empty password.\n"
					+ "Expected: The client rejects the registration before handshake.\n"
					+ "Failure: If the client accepts or proceeds, password validation is broken.\n"
					+ "This test ensures password presence is enforced."
				).GetAwaiter().GetResult();
				using AuthTestHarness h = new AuthTestHarness();
				AuthTestTrace.Log("RegisterTests", "STEP", "Attempting registration with empty password...").GetAwaiter().GetResult();
				bool ok = h.Client.SetLoginCredentials("henry", "", register: true, email: "x@y.test", age: 20);
				LogAssert.IsFalse(ok, "Empty password must be rejected by SetLoginCredentials.");
				AuthTestTrace.Log("RegisterTests", "SUCCESS", nameof(Register_InvalidPassword_RejectedByClient)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("RegisterTests", "FAILURE", $"{nameof(Register_InvalidPassword_RejectedByClient)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Register_InvalidPassword_RejectedByClient)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public async Task Register_DifferentCredentials_ProduceDifferentEncryptedPayloads()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Register_DifferentCredentials_ProduceDifferentEncryptedPayloads),
					"Test: Registration with different credentials produces unique encrypted verifiers.\n"
					+ "Procedure: Register two users with different credentials and compare the encrypted verifier payloads.\n"
					+ "Expected: Each registration produces a unique encrypted verifier.\n"
					+ "Failure: If verifiers are identical, it indicates a cryptographic or registration bug.\n"
					+ "This test ensures registration is unique and secure for each user."
				);
				using AuthTestHarness h1 = new AuthTestHarness();
				await DriveHandshakeAndCapture(h1, "ivan", "passwordA", "ivan@example.test", age: 22);

				using AuthTestHarness h2 = new AuthTestHarness();
				await DriveHandshakeAndCapture(h2, "judy", "passwordB", "judy@example.test", age: 33);

				await AuthTestTrace.Log("RegisterTests", "STEP", "Checking that each registration produced a unique encrypted verifier...");
				LogAssert.AreEqual(1, h1.Client.CreateAccountSends.Count);
				LogAssert.AreEqual(1, h2.Client.CreateAccountSends.Count);

				byte[] v1 = h1.Client.CreateAccountSends[0].EncryptedVerifier;
				byte[] v2 = h2.Client.CreateAccountSends[0].EncryptedVerifier;
				LogAssert.AreNotEqual(System.Convert.ToBase64String(v1), System.Convert.ToBase64String(v2),
					"Two independent registrations must not produce identical encrypted verifiers.");
				await AuthTestTrace.Log("RegisterTests", "SUCCESS", nameof(Register_DifferentCredentials_ProduceDifferentEncryptedPayloads));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("RegisterTests", "FAILURE", $"{nameof(Register_DifferentCredentials_ProduceDifferentEncryptedPayloads)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Register_DifferentCredentials_ProduceDifferentEncryptedPayloads));
			}
		}

		[Test]
		public async Task Register_SameCredentialsTwoAttempts_ProduceDifferentSalts()
		{
			try
			{
				await AuthTestTrace.LogTestStart(
					nameof(Register_SameCredentialsTwoAttempts_ProduceDifferentSalts),
					"Test: SRP salt is randomized on every registration attempt.\n"
					+ "Procedure: Perform two independent registration attempts with the identical username and password, then compare the encrypted salt payloads.\n"
					+ "Expected: The encrypted salt must differ between the two attempts because a fresh random salt is generated each time.\n"
					+ "Failure: If the salts are identical, the SRP registration path is reusing a fixed or deterministic salt, which weakens the credential's resistance to offline dictionary attacks."
				);
				const string user = "saltcheck";
				const string pass = "identical-password";
				const string email = "saltcheck@example.test";

				using AuthTestHarness h1 = new AuthTestHarness();
				await DriveHandshakeAndCapture(h1, user, pass, email, age: 25);

				using AuthTestHarness h2 = new AuthTestHarness();
				await DriveHandshakeAndCapture(h2, user, pass, email, age: 25);

				await AuthTestTrace.Log("RegisterTests", "STEP", "Comparing encrypted salt payloads from two identical registration attempts...");
				LogAssert.AreEqual(1, h1.Client.CreateAccountSends.Count);
				LogAssert.AreEqual(1, h2.Client.CreateAccountSends.Count);

				byte[] salt1 = h1.Client.CreateAccountSends[0].EncryptedSalt;
				byte[] salt2 = h2.Client.CreateAccountSends[0].EncryptedSalt;
				LogAssert.AreNotEqual(System.Convert.ToBase64String(salt1), System.Convert.ToBase64String(salt2),
					"Two registration attempts with identical credentials must produce different encrypted salts — salt must be randomized per attempt.");

				// The verifiers must also differ because they are derived from the fresh salt.
				byte[] v1 = h1.Client.CreateAccountSends[0].EncryptedVerifier;
				byte[] v2 = h2.Client.CreateAccountSends[0].EncryptedVerifier;
				LogAssert.AreNotEqual(System.Convert.ToBase64String(v1), System.Convert.ToBase64String(v2),
					"Different salts must produce different verifiers even for the same password.");
				await AuthTestTrace.Log("RegisterTests", "SUCCESS", nameof(Register_SameCredentialsTwoAttempts_ProduceDifferentSalts));
			}
			catch (Exception ex)
			{
				await AuthTestTrace.Log("RegisterTests", "FAILURE", $"{nameof(Register_SameCredentialsTwoAttempts_ProduceDifferentSalts)}: {ex.Message}\n{ex.StackTrace}");
				throw;
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Register_SameCredentialsTwoAttempts_ProduceDifferentSalts));
			}
		}

		// ───────────────────── issue #252: the registration profile field ─────────────────────

		/// <summary>
		/// Every optional detail survives the trip: client serialisation and encryption, then the
		/// server's six-field sequence arithmetic and decryption, then parsing.
		/// </summary>
		/// <remarks>
		/// Decrypts with <c>SrpService.ServerDecryptRegistrationFields</c> — the method
		/// <c>AccountCreationSystem</c> calls — against the server core's own connection keys, so a
		/// disagreement between the client's field order and the server's offsets fails here.
		/// </remarks>
		[Test]
		public async Task Register_ProfileRoundTripsThroughRegistrationEncryption()
		{
			await AuthTestTrace.LogTestStart(nameof(Register_ProfileRoundTripsThroughRegistrationEncryption),
				"Test: the registration profile is encrypted by the client, decrypted by the server path, and parses back unchanged.");
			try
			{
				using AuthTestHarness h = new AuthTestHarness();
				RegistrationProfile sentProfile = new RegistrationProfile
				{
					Phone = "+44 7700 900123",
					BetaCode = "ABCD-EFGH-JKMN",
					Country = "United Kingdom",
					RealName = "Zoë Ångström",
					Address = "1 High Street\nLondon",
					ReferralAccount = "old_friend",
					DiscordUsername = "Zoë Ångström#1234",
					VerificationChannels = RegistrationVerificationChannels.Email | RegistrationVerificationChannels.Sms | RegistrationVerificationChannels.Discord,
				};

				LogAssert.IsTrue(h.Client.SetLoginCredentials("frank", "p@ssword1!", register: true, email: "frank@example.test", age: 21, profile: sentProfile),
					"SetLoginCredentials refused a valid registration with a profile.");
				h.Client.OnConnected();
				await Task.Yield();

				LogAssert.AreEqual(1, h.Client.CreateAccountSends.Count, "Exactly one CreateAccount broadcast should be emitted.");
				TestClientCore.CreateAccountCapture sent = h.Client.CreateAccountSends[0];
				LogAssert.IsTrue(sent.EncryptedProfile != null && sent.EncryptedProfile.Length > 0, "Encrypted profile payload was empty.");
				LogAssert.IsTrue(sent.EncryptedProfile.Length <= RegistrationProfile.MaxEncryptedBytes, "Encrypted profile exceeds the server's size cap.");

				LogAssert.IsTrue(h.AccountManager.GetConnectionEncryptionData(1, out ConnectionEncryptionData serverKeys) && serverKeys != null,
					"The server holds no encryption data for the connection.");

				SrpService.ServerDecryptRegistrationFields(serverKeys, sent.Sequence,
					sent.EncryptedUsername, sent.EncryptedEmail, sent.EncryptedAge, sent.EncryptedSalt, sent.EncryptedVerifier, sent.EncryptedProfile,
					out byte[] username, out byte[] email, out byte[] age, out byte[] salt, out byte[] verifier, out byte[] profileBytes);

				LogAssert.AreEqual("frank", Encoding.UTF8.GetString(username), "Username did not survive the round trip.");
				LogAssert.AreEqual("frank@example.test", Encoding.UTF8.GetString(email), "Email did not survive the round trip.");
				LogAssert.AreEqual("21", Encoding.UTF8.GetString(age), "Age did not survive the round trip.");
				LogAssert.IsTrue(salt.Length > 0 && verifier.Length > 0, "Salt or verifier decrypted empty.");

				LogAssert.IsTrue(RegistrationProfile.TryDeserialize(profileBytes, out RegistrationProfile received), "The decrypted profile did not parse.");
				LogAssert.AreEqual(sentProfile.Phone, received.Phone, "Phone");
				LogAssert.AreEqual(sentProfile.BetaCode, received.BetaCode, "BetaCode");
				LogAssert.AreEqual(sentProfile.Country, received.Country, "Country");
				LogAssert.AreEqual(sentProfile.RealName, received.RealName, "RealName (non-ASCII)");
				LogAssert.AreEqual(sentProfile.Address, received.Address, "Address (with a line break)");
				LogAssert.AreEqual(sentProfile.ReferralAccount, received.ReferralAccount, "ReferralAccount");
				LogAssert.AreEqual(sentProfile.DiscordUsername, received.DiscordUsername, "DiscordUsername (non-ASCII, with a discriminator)");
				LogAssert.AreEqual(sentProfile.VerificationChannels, received.VerificationChannels, "VerificationChannels");

				// The six sequences were consumed: replaying the same message must fail, not decrypt twice.
				bool replayRefused = false;
				try
				{
					SrpService.ServerDecryptRegistrationFields(serverKeys, sent.Sequence,
						sent.EncryptedUsername, sent.EncryptedEmail, sent.EncryptedAge, sent.EncryptedSalt, sent.EncryptedVerifier, sent.EncryptedProfile,
						out _, out _, out _, out _, out _, out _);
				}
				catch (CryptographicException)
				{
					replayRefused = true;
				}
				LogAssert.IsTrue(replayRefused, "A replayed create-account message decrypted a second time.");
			}
			finally
			{
				await AuthTestTrace.LogTestEnd(nameof(Register_ProfileRoundTripsThroughRegistrationEncryption));
			}
		}

		/// <summary>A registration with no profile still sends one (empty), so the server's field count never varies.</summary>
		[Test]
		public async Task Register_WithoutAProfile_StillSendsAnEmptyProfileField()
		{
			using AuthTestHarness h = new AuthTestHarness();
			await DriveHandshakeAndCapture(h, "noprofile", "p@ssword1!", "noprofile@example.test", age: 30);
			LogAssert.AreEqual(1, h.Client.CreateAccountSends.Count, "Exactly one CreateAccount broadcast should be emitted.");
			TestClientCore.CreateAccountCapture sent = h.Client.CreateAccountSends[0];
			LogAssert.IsTrue(h.AccountManager.GetConnectionEncryptionData(1, out ConnectionEncryptionData serverKeys), "No server keys.");
			SrpService.ServerDecryptRegistrationFields(serverKeys, sent.Sequence,
				sent.EncryptedUsername, sent.EncryptedEmail, sent.EncryptedAge, sent.EncryptedSalt, sent.EncryptedVerifier, sent.EncryptedProfile,
				out _, out _, out _, out _, out _, out byte[] profileBytes);
			LogAssert.IsTrue(RegistrationProfile.TryDeserialize(profileBytes, out RegistrationProfile received), "The empty profile did not parse.");
			LogAssert.IsNull(received.Phone);
			LogAssert.IsNull(received.BetaCode);
			LogAssert.AreEqual(RegistrationVerificationChannels.Email, received.VerificationChannels, "An absent profile must mean email verification.");
		}

		/// <summary>
		/// The client's mirror of the profile rules agrees with the database's — the authority the login
		/// server applies — on the limits and on phone normalisation.
		/// </summary>
		[Test]
		public void RegistrationProfile_MirrorsTheDatabaseProfileRules()
		{
			LogAssert.AreEqual(AccountProfileRules.MaxRealNameLength, RegistrationProfile.MaxRealNameLength, "Real name limit drifted.");
			LogAssert.AreEqual(AccountProfileRules.MaxCountryLength, RegistrationProfile.MaxCountryLength, "Country limit drifted.");
			LogAssert.AreEqual(AccountProfileRules.MaxAddressLength, RegistrationProfile.MaxAddressLength, "Address limit drifted.");
			LogAssert.AreEqual((byte)FishMMO.Database.Data.Enums.AccountVerificationChannels.Email, (byte)RegistrationVerificationChannels.Email, "Email flag drifted.");
			LogAssert.AreEqual((byte)FishMMO.Database.Data.Enums.AccountVerificationChannels.Sms, (byte)RegistrationVerificationChannels.Sms, "SMS flag drifted.");
			LogAssert.AreEqual((byte)FishMMO.Database.Data.Enums.AccountVerificationChannels.Discord, (byte)RegistrationVerificationChannels.Discord, "Discord flag drifted.");
			LogAssert.AreEqual(AccountProfileRules.MinDiscordUsernameLength, RegistrationProfile.MinDiscordNameLength, "Discord username minimum drifted.");
			LogAssert.AreEqual(AccountProfileRules.MaxDiscordUsernameLength, RegistrationProfile.MaxDiscordNameLength, "Discord username maximum drifted.");
			LogAssert.IsTrue(RegistrationProfile.MaxDiscordUsernameLength >= AccountProfileRules.MaxDiscordTagLength + 1,
				"The client must accept as typed every tag the database stores, with room for a leading @.");

			/* Both Discord forms: a unique username, and a freer name that still carries its discriminator.
			 * Every case goes through both implementations, so a rule that changes in one fails here. */
			string[] discordNames =
			{
				"fishfan", "FishFan", "@fishfan", "  fish_fan.99  ", "fish..fan", "f", "fish-fan", "fish fan", new string('a', 32), new string('a', 33),
				"FishFan#1234", "Fish Fan#0001", "@Zoë#4321", "fishfan#123", "fishfan#12345", "fishfan#12a4", "fish:fan#1234", "fish@fan#1234",
				"a#1234", "```x#1234", "fish#fan#1234", "#1234", "", "   ",
			};
			foreach (string input in discordNames)
			{
				LogAssert.AreEqual(AccountProfileRules.NormalizeDiscordUsername(input), RegistrationProfile.NormalizeDiscordUsername(input),
					$"Discord username normalisation disagrees with the database for '{input}'.");
			}
			LogAssert.AreEqual("fishfan#1234", AccountProfileRules.NormalizeDiscordUsername("@FishFan#1234"), "Control: a tag is kept, lowercased, without the @.");
			LogAssert.IsNull(AccountProfileRules.NormalizeDiscordUsername("fish..fan"), "Control: a unique username never has two periods together.");

			RegistrationProfile discordWithoutName = new RegistrationProfile { VerificationChannels = RegistrationVerificationChannels.Discord };
			LogAssert.IsFalse(discordWithoutName.TryValidate(out _), "Discord verification without a Discord username must be refused.");
			LogAssert.IsFalse(AccountProfileRules.TryValidate(new AccountProfileData { VerificationChannels = FishMMO.Database.Data.Enums.AccountVerificationChannels.Discord }, out _, out _),
				"Control: the database refuses Discord without a username too.");

			string[] phones =
			{
				"+44 7700 900123", "0044 (7700) 900-123", "+1.555.010.9999", "07700900123", "+0123456789",
				"+1234567", "+12345678", "+123456789012345", "+1234567890123456", "+44 7700 900123x", "", "   ",
			};
			foreach (string input in phones)
			{
				LogAssert.AreEqual(AccountProfileRules.NormalizePhone(input), RegistrationProfile.NormalizePhone(input),
					$"Phone normalisation disagrees with the database for '{input}'.");
			}

			RegistrationProfile smsWithoutPhone = new RegistrationProfile { VerificationChannels = RegistrationVerificationChannels.Sms };
			LogAssert.IsFalse(smsWithoutPhone.TryValidate(out _), "SMS verification without a phone number must be refused.");
			bool dbAccepts = AccountProfileRules.TryValidate(new AccountProfileData { VerificationChannels = FishMMO.Database.Data.Enums.AccountVerificationChannels.Sms }, out _, out _);
			LogAssert.IsFalse(dbAccepts, "Control: the database refuses SMS without a phone too.");

			RegistrationProfile tooLong = new RegistrationProfile { RealName = new string('a', RegistrationProfile.MaxRealNameLength + 1) };
			LogAssert.IsFalse(tooLong.TryValidate(out _), "An over-long real name must be refused.");
		}

		/// <summary>
		/// A client one release behind sends the six-string version 1 profile, with no Discord username. The
		/// server must still read it, or that client could not register at all.
		/// </summary>
		[Test]
		public void RegistrationProfile_StillReadsTheVersionOneFormat()
		{
			var v1 = new System.Collections.Generic.List<byte> { 1, (byte)RegistrationVerificationChannels.Sms };
			foreach (string field in new[] { "+447700900123", "", "United Kingdom", "", "", "old_friend" })
			{
				byte[] utf8 = Encoding.UTF8.GetBytes(field);
				v1.Add((byte)(utf8.Length >> 8));
				v1.Add((byte)utf8.Length);
				v1.AddRange(utf8);
			}

			LogAssert.IsTrue(RegistrationProfile.TryDeserialize(v1.ToArray(), out RegistrationProfile received), "A version 1 profile must still parse.");
			LogAssert.AreEqual("+447700900123", received.Phone, "Phone");
			LogAssert.AreEqual("old_friend", received.ReferralAccount, "ReferralAccount");
			LogAssert.IsNull(received.DiscordUsername, "A version 1 profile has no Discord username.");
			LogAssert.AreEqual(RegistrationVerificationChannels.Sms, received.VerificationChannels, "VerificationChannels");

			byte[] current = new RegistrationProfile { DiscordUsername = "fishfan" }.Serialize();
			LogAssert.AreEqual(RegistrationProfile.FormatVersion, current[0], "Control: the encoder writes the current version.");
			v1[0] = RegistrationProfile.FormatVersion;
			LogAssert.IsFalse(RegistrationProfile.TryDeserialize(v1.ToArray(), out _), "Six strings labelled with the current version must be refused: it has seven.");
		}

		/// <summary>The decoder refuses anything the encoder could not have produced.</summary>
		[Test]
		public void RegistrationProfile_RefusesMalformedSerialisations()
		{
			byte[] good = new RegistrationProfile { Phone = "+447700900123" }.Serialize();
			LogAssert.IsTrue(RegistrationProfile.TryDeserialize(good, out _), "Control: a well-formed profile must parse.");

			byte[] trailing = new byte[good.Length + 1];
			Buffer.BlockCopy(good, 0, trailing, 0, good.Length);
			LogAssert.IsFalse(RegistrationProfile.TryDeserialize(trailing, out _), "Trailing bytes must be refused.");

			byte[] wrongVersion = (byte[])good.Clone();
			wrongVersion[0] = (byte)(RegistrationProfile.FormatVersion + 1);
			LogAssert.IsFalse(RegistrationProfile.TryDeserialize(wrongVersion, out _), "An unknown version must be refused.");

			byte[] truncated = new byte[good.Length - 1];
			Buffer.BlockCopy(good, 0, truncated, 0, truncated.Length);
			LogAssert.IsFalse(RegistrationProfile.TryDeserialize(truncated, out _), "A truncated profile must be refused.");

			LogAssert.IsFalse(RegistrationProfile.TryDeserialize(null, out _), "Null must be refused.");

			bool threw = false;
			try { new RegistrationProfile { Address = new string('x', RegistrationProfile.MaxAddressLength + 1) }.Serialize(); }
			catch (ArgumentException) { threw = true; }
			LogAssert.IsTrue(threw, "Serialising an over-long field must throw rather than produce an unparseable profile.");
		}
	}
}