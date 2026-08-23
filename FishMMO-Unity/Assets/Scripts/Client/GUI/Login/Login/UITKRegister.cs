using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Logging;
using FishMMO.Client.Security;
using System.Collections.Generic;
using System.IO;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the registration control providing username, email, password,
	/// age confirmation and an optional key field for account creation.
	/// </summary>
	/// <remarks>
	/// <para><b>Field clearing:</b> All input fields are cleared immediately after the register
	/// button is pressed, before the network round-trip begins, to minimise the window during
	/// which sensitive text resides in the managed heap.</para>
	/// </remarks>
	public class UITKRegister : UITKControl
	{
		/// <summary>
		/// Full-screen forms are not windows: there is nowhere to drag them to.
		/// </summary>
		/// <remarks>See <see cref="UITKControl.CanDrag"/>, which defaults every
		/// <see cref="UITKPanelLayer.Window"/> panel to draggable.</remarks>
		protected override bool CanDrag => false;

		/// <summary>
		/// The name of the username TextField in the UI.
		/// </summary>
		private const string USERNAME_NAME = "register-username";
		/// <summary>
		/// The name of the email TextField in the UI.
		/// </summary>
		private const string EMAIL_NAME = "register-email";
		/// <summary>
		/// The name of the password TextField in the UI.
		/// </summary>
		private const string PASSWORD_NAME = "register-password";
		/// <summary>
		/// The name of the age DropdownField in the UI.
		/// </summary>
		private const string AGE_SELECT_NAME = "register-age";
		/// <summary>
		/// The name of the register submit button in the UI.
		/// </summary>
		private const string REGISTER_BUTTON_NAME = "register-submit-btn";
		/// <summary>
		/// The name of the quit button in the UI.
		/// </summary>
		private const string QUIT_BUTTON_NAME = "register-quit-btn";
		/// <summary>
		/// The name of the status Label in the UI.
		/// </summary>
		private const string STATUS_NAME = "register-status";

		/// <summary>
		/// Minimum age index required to register (index into the age dropdown options).
		/// Index 0 is treated as "not selected" / under-age.
		/// </summary>
		private const int MinAgeSelectIndex = 1;

		/// <summary>
		/// True when this panel has an active registration/verification flow.
		/// Used to gate auth-result handling and prevent cross-talk with UITKLogin,
		/// which shares the same <see cref="ClientLoginAuthenticator.OnClientAuthenticationResult"/> event.
		/// </summary>
		private bool isAuthFlowActive;

		private TextField username;
		private TextField email;
		private TextField password;
		private DropdownField ageSelect;
		private Button registerButton;
		private Button quitToLoginButton;
		private Label statusMessage;

		/// <summary>
		/// Temporarily stores the username after account creation for verification code submission.
		/// </summary>
		private string pendingVerifyUsername;

		/// <summary>
		/// Absolute path of the on-disk copy of the 2FA setup payload written during registration.
		/// Tracked so every terminal exit from the flow can scrub the file.
		/// </summary>
		private string savedTwoFactorSetupPath;

		/// <summary>
		/// The password the in-flight registration was submitted with, held only until the 2FA
		/// payload has been encrypted with it.
		/// </summary>
		/// <remarks>
		/// <para>This is a deliberate, bounded extension of the password's lifetime, and it is the
		/// price of encrypting the recovery codes at rest. The alternative — a key bound to this
		/// machine — is worse: it does not survive the reinstall that is the whole reason a player
		/// reaches for recovery codes. See <see cref="TwoFactorRecoveryCrypto"/>.</para>
		/// <para>The window is from the Register click to the arrival of
		/// <c>TwoFactorSetupBroadcast</c>, which is the same window the SRP handshake already holds
		/// the password open for; it is dropped the instant the envelope is written, and again on
		/// every terminal exit from the flow. It is never written to disk and never logged. .NET
		/// strings cannot be zeroed, so releasing the reference early is the whole of the available
		/// mitigation — the same reasoning as <c>UITKLogin.PendingCredentials</c>.</para>
		/// </remarks>
		private string pendingAccountPassword;

		/// <summary>
		/// True once this panel has looked for recovery payloads left behind by an earlier,
		/// interrupted registration. Once per panel lifetime — the offer is a recovery aid, not a
		/// thing to re-ask on every hide/show.
		/// </summary>
		private bool leftoverRecoveryChecked;

		/// <summary>
		/// Resolves and caches visual elements, populates age dropdown, and wires up button callbacks.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			username = Root.Q<TextField>(USERNAME_NAME);
			email = Root.Q<TextField>(EMAIL_NAME);
			password = Root.Q<TextField>(PASSWORD_NAME);
			ageSelect = Root.Q<DropdownField>(AGE_SELECT_NAME);
			registerButton = Root.Q<Button>(REGISTER_BUTTON_NAME);
			quitToLoginButton = Root.Q<Button>(QUIT_BUTTON_NAME);
			statusMessage = Root.Q<Label>(STATUS_NAME);

			if (password != null)
			{
				password.isPasswordField = true;
			}

			// Populate age dropdown: index 0 = "Select your age", index 1 = "13", ..., index 108 = "120".
			if (ageSelect != null)
			{
				var choices = new System.Collections.Generic.List<string>(109);
				choices.Add("Select your age");
				for (int age = 13; age <= 120; age++)
					choices.Add(age.ToString());
				ageSelect.choices = choices;
				ageSelect.index = 0;
			}

			if (registerButton != null)
			{
				registerButton.clicked += OnClick_Register;
			}
			if (quitToLoginButton != null)
			{
				quitToLoginButton.clicked += OnClick_QuitToLogin;
			}

			// Enter registers, Escape goes back to the sign-in screen. Enter observes the same
			// lock as the Register button it mirrors; see LoginKeys.Attach.
			LoginKeys.Attach(this, Root, OnClick_Register, OnClick_QuitToLogin, () => !replyGuard.IsPending);
			LoginKeys.SetTabOrder(Root, username, email, password, ageSelect, registerButton, quitToLoginButton);
		}

		/// <summary>
		/// Subscribes to connection and authentication events when the client is injected.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
			Client.LoginAuthenticator.OnTwoFactorSetupReceived += OnTwoFactorSetupReceived;
		}

		/// <summary>
		/// Unsubscribes from connection and authentication events when the client is cleared.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			Client.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			Client.LoginAuthenticator.OnTwoFactorSetupReceived -= OnTwoFactorSetupReceived;
		}

		/// <summary>
		/// Shows and unlocks the registration panel when quitting to login.
		/// </summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			pendingVerifyUsername = null;
			this.accountCreated = false;
			DeleteSavedTwoFactorSetupFile();
			ClearAllFields();
			SetFormLocked(false);
		}

		/// <summary>
		/// Hides the registration panel and resets the status message.
		/// </summary>
		/// <remarks>
		/// Overrides <c>Hide(bool)</c>, not <c>Hide()</c>. <c>Hide()</c> is non-virtual and only
		/// forwards here, so this is the one place teardown can live where every caller reaches
		/// it — quit-to-login calls the bool form directly.
		/// </remarks>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);

			if (overrideIsAlwaysOpen || Document == null)
			{
				// The base refused the hide; the form is still up, so keep its status line.
				return;
			}

			if (statusMessage != null)
			{
				statusMessage.text = "";
			}
		}

		/// <summary>
		/// Handles client connection state changes. Unlocks the form when disconnected.
		/// </summary>
		/// <param name="args">Connection state arguments.</param>
		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				/* Explain the drop before unlocking the form.
				 *
				 * Registration is refused before authentication for reasons the server
				 * deliberately keeps off the wire — an unverifiable connection token, an
				 * unsupported protocol version, a tripped handshake rate limit — and every one
				 * of those is a bare transport close. Clearing the status text and handing the
				 * form back is then indistinguishable from the button having done nothing.
				 *
				 * Read before SetFormLocked below, which clears isAuthFlowActive. */
				bool droppedWithoutExplanation = isAuthFlowActive;

				/* Whether the account survives the drop is the difference between "try again"
				 * and "you already have an account". Read before it is cleared below. */
				bool accountWasCreated = this.accountCreated;

				SetStatus(null);
				SetFormLocked(false);
				pendingVerifyUsername = null;
				this.accountCreated = false;
				DeleteSavedTwoFactorSetupFile();

				/* The panel has to come back. Registration hides itself for the whole 2FA and
				 * email-verification stretch, so a drop in that window left nothing at all on
				 * screen — and this is the window in which drops are most likely, because it is
				 * the longest. */
				if (droppedWithoutExplanation)
				{
					Show();

					ShowValidationError(accountWasCreated
						? "Your account was created, but the connection to the login server was lost before " +
							"verification finished. Sign in with your new account to continue; you will be asked " +
							"for the verification code that was emailed to you."
						: "Connection to login server lost. Please check your network and try again. " +
							"If the problem persists, the login server may be temporarily down.");
				}
			}
		}

		/// <summary>
		/// Handles authentication results from the server and displays appropriate feedback.
		/// </summary>
		/// <param name="result">The result of client authentication.</param>
		private void Authenticator_OnClientAuthenticationResult(ClientAuthenticationResult result)
		{
			/* Any result is the server telling us it is still working this request —
			 * the SRP exchange and the two-factor prompt both report progress before
			 * they finish, and a client can sit in the login queue for minutes. Each
			 * one buys the reply deadline again rather than counting against it. */
			replyGuard.Refresh();

			// Only process auth results when this panel owns the active flow.
			// Without this guard, hidden panels would still react to auth results
			// intended for the other panel (e.g., UITKRegister force-disconnecting
			// on InvalidUsernameOrPassword during UITKLogin's login attempt).
			if (!isAuthFlowActive) return;

			switch (result)
			{
				case ClientAuthenticationResult.AccountCreated:
					OnAccountCreated();
					break;
				case ClientAuthenticationResult.AccountVerified:
					OnAccountVerified();
					break;
				case ClientAuthenticationResult.InvalidUsernameOrPassword:
					OnRegistrationDialog("Invalid Username or Password.");
					break;
				case ClientAuthenticationResult.Banned:
					OnRegistrationDialog("Account creation failed. Please contact the system administrator.");
					break;
				case ClientAuthenticationResult.ServerBusy:
					OnRegistrationDialog("Server is busy. Please try again later.");
					break;
				case ClientAuthenticationResult.ServerFull:
					OnRegistrationDialog("The server is full. Please try again shortly.");
					break;
				case ClientAuthenticationResult.AlreadyOnline:
					OnRegistrationDialog("That account is already online.");
					break;
				case ClientAuthenticationResult.AccountUnverified:
					/* The account exists and is waiting for its code. Registering again cannot
					 * help, but the verification prompt can — it is the same code, and the same
					 * account name is already in pendingVerifyUsername. */
					OpenVerifyCodeDialog();
					break;
				case ClientAuthenticationResult.TokenInvalid:
				case ClientAuthenticationResult.TokenExpired:
				case ClientAuthenticationResult.TokenRevoked:
				case ClientAuthenticationResult.TokenDecryptFailed:
					OnRegistrationDialog("Your connection to the login server has expired. Please try again.");
					break;
				case ClientAuthenticationResult.VersionMismatch:
					OnVersionMismatch();
					break;
				/* The fall-through that used to swallow this whole block is the bug: the eleven
				 * cases above it were listed as "not applicable during registration flow" with a
				 * comment and no `break`, so every one of them fell into OnVersionMismatch and
				 * told the player their client was out of date. A full server, an account that is
				 * already online, an expired connection token and a mid-handshake SRP progress
				 * message all reported "game version mismatch" and advised an update that could
				 * not possibly fix any of them. Each now says what actually happened, and the
				 * genuinely inapplicable ones below say nothing at all. */
				case ClientAuthenticationResult.SrpVerify:
				case ClientAuthenticationResult.SrpProof:
				case ClientAuthenticationResult.LoginSuccess:
				case ClientAuthenticationResult.WorldLoginSuccess:
				case ClientAuthenticationResult.SceneLoginSuccess:
				case ClientAuthenticationResult.NoCharacterSelected:
				case ClientAuthenticationResult.TwoFactorRequired:
				case ClientAuthenticationResult.TwoFactorInvalid:
					break;
			}
		}

		/// <summary>
		/// Handles successful account creation: hides the form and waits for the 2FA setup broadcast.
		/// </summary>
		private void OnAccountCreated()
		{
			/* The account now exists in the database. Everything after this point is recoverable
			 * by verifying the email, so the flag below turns the reply timeout from "give up"
			 * into "skip the step that went missing". */
			this.accountCreated = true;

			SetFormLocked(true);

			/* Deliberately does NOT hide the panel here any more, and deliberately leaves the
			 * reply deadline armed. What the client is waiting for at this point is one more
			 * message from the server — TwoFactorSetupBroadcast — and it is the one message in
			 * this flow the server can silently decline to send: AccountCreationSystem logs and
			 * carries on when TotpMasterKey is missing or the wrong length, and again if the
			 * setup block throws. The account is created either way, so the player was left on a
			 * hidden panel, form locked, in front of a status line they could not see, forever. */
			SetStatus("Setting up two-factor authentication...");
		}

		/// <summary>
		/// True once the server has confirmed the account exists.
		/// </summary>
		/// <remarks>
		/// Distinguishes "the request failed" from "the request succeeded and a later step went
		/// missing". Only the second is recoverable, and the recovery is different — retrying
		/// registration for an account that already exists just produces a refusal.
		/// </remarks>
		private bool accountCreated;

		/// <summary>
		/// Writes the status line, holding the text across tree rebuilds.
		/// </summary>
		/// <param name="text">The message, or null to clear the line.</param>
		private void SetStatus(string text)
		{
			this.pendingStatus = text;

			if (statusMessage != null)
			{
				statusMessage.text = text ?? string.Empty;
			}
		}

		/// <summary>The status message this panel wants displayed, held across tree rebuilds.</summary>
		private string pendingStatus;

		/// <summary>
		/// Re-applies the status line and the form lock after the visual tree was rebuilt.
		/// </summary>
		/// <remarks>
		/// <see cref="SetFormLocked"/> writes into elements that the next hide/show replaces, so a
		/// panel that came back mid-registration — which is what the drop handler and the reply
		/// timeout both do — offered an enabled Register button over a request that was still
		/// outstanding. Driven off the guard rather than off a second copy of the flag, so the two
		/// cannot disagree.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();

			if (statusMessage != null)
			{
				statusMessage.text = this.pendingStatus ?? string.Empty;
			}

			ReleaseControls(!replyGuard.IsPending);
		}

		/// <inheritdoc/>
		protected override void OnAfterShow()
		{
			base.OnAfterShow();

			if (statusMessage != null)
			{
				statusMessage.text = this.pendingStatus ?? string.Empty;
			}

			ReleaseControls(!replyGuard.IsPending);

			LoginKeys.FocusFirst(Root, username);

			OfferLeftoverRecovery();
		}

		/// <summary>
		/// Handles the 2FA setup data received from the server after account creation.
		/// Displays the otpauth URI and recovery codes, then opens the verify code dialog.
		/// </summary>
		/// <param name="otpauthUri">The otpauth provisioning URI.</param>
		/// <param name="recoveryCodes">The generated recovery codes.</param>
		private void OnTwoFactorSetupReceived(string otpauthUri, string[] recoveryCodes)
		{
			if (string.IsNullOrEmpty(pendingVerifyUsername))
			{
				return;
			}

			/* Recovery codes and the otpauth secret are written to disk so the player has a copy
			 * they can act on, and scrubbed on every terminal exit of the flow.
			 *
			 * S2, first pass: the filename used to be $"2fa_setup_{username}.txt" in
			 * persistentDataPath — a world-readable directory on every desktop platform. That
			 * leaked the account name to anything that could list the directory even after the
			 * contents were deleted, and it made the path guessable. The name is now a random
			 * token that says nothing about the account, in its own permission-tightened folder.
			 *
			 * S2, this pass: the *contents* were still plaintext, so the leak survived every one
			 * of those mitigations — a chmod that did not take, a backup agent, a sync client or a
			 * lifted disk all read the codes and the TOTP secret straight out of the file. They
			 * are now encrypted under the account password; TwoFactorRecoveryCrypto documents why
			 * that key and not a machine-bound one, and TwoFactorRecoveryStore documents why the
			 * write is verified before it is published.
			 *
			 * The password is available here because this is still the same registration attempt
			 * the player typed it into — see pendingAccountPassword. If it is somehow not (a
			 * broadcast arriving outside a flow this panel started), the payload is not written at
			 * all rather than written in the clear, and the dialog below says so: the codes are on
			 * screen either way, which is the copy that actually matters.
			 *
			 * Residual risk: a process killed between this write and the scrub leaves the
			 * envelope behind. It is now an encrypted envelope rather than a readable file, and
			 * OfferLeftoverRecovery below is the path that finds it on the next run. */
			string savePath = null;
			string storagePassword = pendingAccountPassword;
			try
			{
				if (string.IsNullOrEmpty(storagePassword))
				{
					Log.Warning("UITKRegister", "No account password is in hand; the 2FA payload was not written to disk.");
				}
				else
				{
					string saveDirectory = TwoFactorRecoveryStore.EnsureDirectory(Application.persistentDataPath);
					string saveContent = $"OTPAuth URI:\n{otpauthUri}\n\nRecovery Codes:\n{string.Join("\n", recoveryCodes)}\n";

					if (TwoFactorRecoveryStore.TrySave(saveDirectory, storagePassword, saveContent, out savePath))
					{
						savedTwoFactorSetupPath = savePath;

						// Debug, not Info, and never the path — the log is shipped with bug reports.
						Log.Debug("UITKRegister", "Two-factor setup data written to the local recovery folder.");

						/* The one moment in the whole client where a *server-confirmed* password is
						 * in hand: account creation just succeeded with it. That is the only safe
						 * moment to re-encrypt a payload left in the clear by an older build,
						 * because encrypting under a merely-typed password would succeed and
						 * produce a file nobody can ever open. */
						MigrateLegacyRecoveryFiles(saveDirectory, storagePassword);
					}
					else
					{
						savePath = null;
						savedTwoFactorSetupPath = null;
					}
				}
			}
			catch (System.Exception ex)
			{
				Log.Warning("UITKRegister", $"Failed to save 2FA setup data: {ex.Message}");
				savePath = null;
				savedTwoFactorSetupPath = null;
			}
			finally
			{
				// The password has done its one job. Drop it before anything else can capture it.
				storagePassword = null;
				pendingAccountPassword = null;
			}

			string codesDisplay = string.Join("\n", recoveryCodes);
			string message = "Two-Factor Authentication Setup\n\n" +
				"Scan the following URI with your authenticator app (e.g. Google Authenticator):\n\n" +
				otpauthUri + "\n\n" +
				"Recovery Codes (save these somewhere safe!):\n\n" +
				codesDisplay + "\n\n" +
				(savePath != null
					? $"An encrypted copy was written to:\n{savePath}\n" +
						"It is protected with your account password and is deleted once registration completes. " +
						"If this client is interrupted before then, it will offer the codes back the next time you " +
						"open this screen.\n\n"
					: "Write these down now — no copy could be saved to this computer.\n\n") +
				"Press Confirm to continue to email verification.";

			/* Stop the clock. From here the client is waiting on a person — one who has to open
			 * an authenticator app and scan a URI — not on the server, and a reply deadline that
			 * kept running would tear the session down mid-scan. It is re-armed the moment a code
			 * is actually submitted; see OpenVerifyCodeDialog. */
			replyGuard.Clear();

			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiDialogBox))
			{
				uiDialogBox.Open(
					message,
					() =>
					{
						OpenVerifyCodeDialog();
					},
					AbandonFlowAndReturnToLogin);
			}
		}

		/// <summary>
		/// Opens the verification code input dialog for email verification.
		/// </summary>
		private void OpenVerifyCodeDialog()
		{
			// Waiting on the player, not on the server. See OnTwoFactorSetupReceived.
			replyGuard.Clear();

			if (UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox uiDialogInputBox))
			{
				uiDialogInputBox.Open(
					"Please enter the verification code sent to your email.",
					(code) =>
					{
						if (!string.IsNullOrWhiteSpace(pendingVerifyUsername) && !string.IsNullOrWhiteSpace(code))
						{
							Client.LoginAuthenticator.SendVerifyCode(pendingVerifyUsername, code.Trim());

							// A request is outstanding again; restart the clock.
							replyGuard.Begin();
						}
					},
					AbandonFlowAndReturnToLogin);
			}
		}

		/// <summary>
		/// Handles successful account verification: disconnects and returns to the login screen.
		/// </summary>
		private void OnAccountVerified()
		{
			pendingVerifyUsername = null;
			this.accountCreated = false;
			DeleteSavedTwoFactorSetupFile();
			Client.ForceDisconnect();
			SetFormLocked(false);

			LoginNotice.Show("Your account has been verified! You may now log in.");
			if (UIManager.TryGetTK("UILogin", out UITKLogin uiLogin))
			{
				Hide();
				uiLogin.Show();
			}
		}

		/// <summary>
		/// Shows a dialog box for registration feedback and disconnects the client.
		/// </summary>
		/// <param name="message">The message to display.</param>
		private void OnVersionMismatch()
			{
				string myVersion = MainBootstrapSystem.GameVersion ?? "unknown";
			LoginNotice.Show($"Game version mismatch.\n\nYour client is version {myVersion}.\nThe server expects a different version.\n\nPlease update your client to match the server.");
			pendingVerifyUsername = null;
			this.accountCreated = false;
			DeleteSavedTwoFactorSetupFile();
			Client.ForceDisconnect();
			SetFormLocked(false);
		}

		/// <summary>
		/// Shows a dialog box for registration feedback and disconnects the client.
		/// </summary>
		/// <param name="message">The message to display.</param>
		private void OnRegistrationDialog(string message)
		{
			LoginNotice.Show(message);
			pendingVerifyUsername = null;
			this.accountCreated = false;
			DeleteSavedTwoFactorSetupFile();
			Client.ForceDisconnect();
			SetFormLocked(false);
		}

		/// <summary>
		/// Drops the in-flight account password and deletes the on-disk copy of the 2FA setup
		/// payload, if any.
		/// </summary>
		private void DeleteSavedTwoFactorSetupFile()
		{
			/* Every terminal exit from the flow already calls this, which makes it the one place
			 * that reliably sees the end of a registration attempt — so the password backstop lives
			 * here too. It is normally already null by now (the storage block drops it the moment
			 * the envelope is written); this covers the attempts that never got that far. */
			pendingAccountPassword = null;

			string path = savedTwoFactorSetupPath;
			savedTwoFactorSetupPath = null;
			if (string.IsNullOrEmpty(path))
			{
				return;
			}
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch (System.Exception ex)
			{
				Log.Warning("UITKRegister", $"Failed to delete 2FA setup file '{path}': {ex.Message}");
			}
		}

		/// <summary>
		/// Re-encrypts any recovery payloads an older build left in the clear.
		/// </summary>
		/// <param name="directory">The recovery directory.</param>
		/// <param name="confirmedPassword">
		/// A password the server has just accepted. Never a merely-typed one — see the remarks on
		/// <see cref="TwoFactorRecoveryStore.TryMigrateLegacy"/>.
		/// </param>
		/// <remarks>
		/// Best effort, and deliberately silent on failure: a plaintext file that will not migrate
		/// is still a file the player can read, which is the outcome that matters most. It is
		/// reported by <see cref="OfferLeftoverRecovery"/> instead, where the player can act on it.
		/// </remarks>
		private void MigrateLegacyRecoveryFiles(string directory, string confirmedPassword)
		{
			List<string> legacy = TwoFactorRecoveryStore.List(directory, legacyPlaintext: true);
			for (int i = 0; i < legacy.Count; ++i)
			{
				// Never the file this attempt just wrote — that one is already an envelope.
				if (string.Equals(legacy[i], savedTwoFactorSetupPath, System.StringComparison.Ordinal))
				{
					continue;
				}
				TwoFactorRecoveryStore.TryMigrateLegacy(directory, legacy[i], confirmedPassword, out _);
			}
		}

		/// <summary>
		/// Looks for recovery payloads left behind by an earlier registration that never finished,
		/// and offers them back to the player.
		/// </summary>
		/// <remarks>
		/// <para>Encrypting the payload closed a leak but removed something real: the player used
		/// to be able to open the file in a text editor after a crash. This is what replaces that,
		/// and it is the reason the key is the account password — the password is the one secret
		/// the player still has on a fresh install, so it is the one that can open the file at the
		/// moment they actually need it.</para>
		/// <para>Runs once per panel lifetime and only outside an active flow, so it can never
		/// interrupt a registration in progress. A plaintext leftover is *reported*, not opened:
		/// nothing here has a confirmed password, and the player can read it themselves — the
		/// warning tells them to, and to delete it afterwards.</para>
		/// </remarks>
		private void OfferLeftoverRecovery()
		{
			if (leftoverRecoveryChecked || isAuthFlowActive || accountCreated || !string.IsNullOrEmpty(pendingVerifyUsername))
			{
				return;
			}
			leftoverRecoveryChecked = true;

			string directory;
			List<string> envelopes;
			List<string> legacy;
			try
			{
				directory = Path.Combine(Application.persistentDataPath, TwoFactorRecoveryStore.DirectoryName);
				if (!Directory.Exists(directory))
				{
					return;
				}
				envelopes = TwoFactorRecoveryStore.List(directory);
				legacy = TwoFactorRecoveryStore.List(directory, legacyPlaintext: true);
			}
			catch (System.Exception ex)
			{
				Log.Warning("UITKRegister", $"Could not inspect the recovery folder: {ex.Message}");
				return;
			}

			if (legacy.Count > 0)
			{
				/* Queued, not Open'd: the shared dialog refuses a second question rather than
				 * replacing the first, and this one has to be seen — it is telling the player
				 * their recovery codes are sitting on the disk unencrypted. */
				LoginNotice.Show(
					$"{legacy.Count} unencrypted two-factor recovery file(s) from an older version of this " +
					$"client are still on this computer, in:\n{directory}\n\n" +
					"Copy the codes somewhere safe and delete those files. Newer files are encrypted with " +
					"your account password.");
			}

			if (envelopes.Count < 1)
			{
				return;
			}

			string newest = envelopes[0];
			if (!UIManager.TryGetTK("UIDialogBox", out UITKDialogBox dialogBox) ||
				!dialogBox.Open(
					"A two-factor recovery file from an interrupted registration was found on this computer.\n\n" +
					"It is encrypted with the account password it was created under. Would you like to unlock and " +
					"view it now?",
					() => PromptForLeftoverRecoveryPassword(newest),
					null))
			{
				// The dialog was busy or absent. The file is untouched and the offer returns on the
				// next run; nothing here is allowed to destroy a payload the player has not seen.
				leftoverRecoveryChecked = false;
			}
		}

		/// <summary>
		/// Asks for the account password and, if it opens the envelope, shows the codes.
		/// </summary>
		/// <param name="path">The envelope to unlock.</param>
		/// <remarks>
		/// Opened through the masked overload of <see cref="UITKDialogInputBox.Open(string, bool,
		/// Action{string}, Action)"/>, so the account password is not rendered in clear text while
		/// it is typed. That overload was added for this call site: the prompt previously used the
		/// plain form and put the password on screen, which is a shoulder-surfing exposure on a
		/// recovery path the player has deliberately opened.
		/// </remarks>
		private void PromptForLeftoverRecoveryPassword(string path)
		{
			if (!UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox inputBox))
			{
				return;
			}

			inputBox.Open(
				"Enter the account password this recovery file was created with.",
				masked: true,
				onAccept: (entered) =>
				{
					if (string.IsNullOrEmpty(entered))
					{
						return;
					}

					TwoFactorRecoveryReadResult result = TwoFactorRecoveryStore.TryRead(path, entered, out string payload);
					entered = null;

					switch (result)
					{
						case TwoFactorRecoveryReadResult.Success:
							/* Shown, then removed. The player is looking at the codes; leaving the
							 * file would re-ask this question on every launch forever, and the
							 * payload has served its only purpose. Nothing is deleted on any other
							 * branch of this switch. */
							LoginNotice.Show("Recovered two-factor setup data:\n\n" + payload);
							TwoFactorRecoveryStore.Delete(path);
							break;

						case TwoFactorRecoveryReadResult.WrongPasswordOrTampered:
							/* Both halves are said out loud on purpose. AES-GCM cannot distinguish
							 * a wrong password from an edited file, and quietly implying the first
							 * would hide the second. The file is kept either way. */
							LoginNotice.Show(
								"That password did not open the recovery file.\n\n" +
								"Either it is not the password the file was created with, or the file has been " +
								"altered. It has been left where it is so you can try again.");
							break;

						case TwoFactorRecoveryReadResult.LegacyPlaintext:
							LoginNotice.Show("That file is an older unencrypted one:\n\n" + payload);
							break;

						case TwoFactorRecoveryReadResult.Empty:
							LoginNotice.Show("The recovery file is no longer there.");
							break;

						default:
							LoginNotice.Show(
								"The recovery file could not be read — it is damaged or was written by a newer " +
								"client. It has been left where it is.");
							break;
					}
				},
				null);
		}

		/// <summary>
		/// Validates all fields, clears sensitive input immediately, then initiates account creation.
		/// </summary>
		public void OnClick_Register()
		{
			/* Refuse re-entry while a registration is already outstanding. The button is disabled
			 * for the whole of that wait, but Enter does not go through the button — see
			 * LoginKeys.Attach — so a second press ran this method again against fields the first
			 * press had already cleared, hit the "invalid username" path, and called
			 * ShowValidationError -> SetFormLocked(false). That unlocks the form, clears
			 * isAuthFlowActive and disarms the watchdog, after which every result belonging to the
			 * registration that was genuinely in flight was dropped by the `if (!isAuthFlowActive)
			 * return` gate at the top of the auth handler. The account was created on the server
			 * and the client never said a word about it. */
			if (replyGuard.IsPending)
			{
				return;
			}

			string usernameText = username != null ? username.value : null;
			string emailText = email != null ? email.value : null;
			string passwordText = password != null ? password.value : null;
			int ageIndex = ageSelect != null ? ageSelect.index : 0;
			// Map dropdown index to actual age: index 0 = not selected, index 1 = age 13, etc.
			int age = ageIndex > 0 ? ageIndex + 12 : 0;

			/* No identifiers in the log. This used to print the username and the email address
			 * verbatim on every click, at Info level, into a log that is written to disk, shipped
			 * with bug reports and read by anyone with the machine — and it printed them before
			 * validation, so a mistyped password attempt still deposited the email address. The
			 * account name is recoverable from the server's own logs when it is actually needed. */
			Log.Debug("UITKRegister", "Create Account clicked.");

			ClearAllFields();

			if (!Authentication.IsAllowedUsername(usernameText))
			{
				ShowValidationError(Authentication.InvalidUsernameError);
				return;
			}

			if (!Authentication.IsAllowedPassword(passwordText))
			{
				ShowValidationError(Authentication.InvalidPasswordError);
				return;
			}

			if (!Authentication.IsAllowedEmailUsername(emailText))
			{
				ShowValidationError("A valid email address is required to register.");
				return;
			}

			if (ageIndex < MinAgeSelectIndex)
			{
				ShowValidationError("You must confirm your age to register.");
				return;
			}

			SetFormLocked(true);

			/* Held from here until the 2FA payload has been encrypted with it, and no longer. See
			 * the field's remarks: this is the price of encrypting the recovery codes at rest, and
			 * the window is the same one the SRP handshake already keeps the password open for. */
			pendingAccountPassword = passwordText;

			StartCoroutine(Client.GetLoginServerList((error) =>
			{
				LoginNotice.Show(error);
				Log.Error("UITKRegister", error);
				SetFormLocked(false);
			},
			(servers, token) =>
			{
				pendingVerifyUsername = usernameText;
					if (!string.IsNullOrEmpty(token)) Client.LoginAuthenticator.ConnectionToken = token;
				Connect(usernameText, passwordText, emailText, age);
			}));
		}

		/// <summary>
		/// Abandons whatever this panel had in flight and hands the screen back to sign-in.
		/// </summary>
		/// <remarks>
		/// Back — and the Escape key that mirrors it — used to do nothing but clear the fields and
		/// swap the panels, which is correct only for a player who has not pressed Register yet.
		/// Pressed during a registration it left the connection open, <c>isAuthFlowActive</c> set,
		/// the plaintext account password resident in <c>pendingAccountPassword</c> for the rest
		/// of the session, and the 2FA setup file on disk — and because the flow was still
		/// nominally live, the dialogs it goes on to open (the recovery-code display, the
		/// verification prompt) appeared over the login screen the player had just returned to.
		/// This is the same teardown the dialog cancel callbacks perform.
		/// </remarks>
		public void OnClick_QuitToLogin()
		{
			ClearAllFields();
			AbandonFlowAndReturnToLogin();
		}

		/// <summary>
		/// Ends the registration flow, drops everything it was holding, and shows the login panel.
		/// </summary>
		/// <remarks>
		/// The single exit used by Back, by the recovery-code dialog's cancel and by the
		/// verification prompt's cancel. Those three had three copies of it, which is how Back
		/// came to be missing most of it.
		/// </remarks>
		private void AbandonFlowAndReturnToLogin()
		{
			pendingVerifyUsername = null;
			this.accountCreated = false;

			// Drops the in-flight account password as well as the on-disk envelope.
			DeleteSavedTwoFactorSetupFile();

			if (Client != null)
			{
				Client.ForceDisconnect();
			}

			SetFormLocked(false);
			SetStatus(null);
			Hide();

			if (UIManager.TryGetTK("UILogin", out UITKLogin uiLogin))
			{
				uiLogin.Show();
			}
		}

		/// <summary>
		/// Attempts to connect to the login server with registration credentials.
		/// </summary>
		/// <param name="usernameText">The validated username.</param>
		/// <param name="passwordText">The validated password.</param>
		/// <param name="emailText">The validated email address.</param>
		/// <param name="age">The selected age value.</param>
		private void Connect(string usernameText, string passwordText, string emailText, int age)
		{
			if (Client.IsConnectionReady(LocalConnectionState.Stopped) &&
				Client.TryGetRandomLoginServerPort(out ushort serverPort))
			{
				if (statusMessage != null)
				{
					statusMessage.text = "Creating account...";
				}
					Log.Debug("UITKRegister", "Submitting account creation.");
				/* See the matching check in UITKLogin.Connect: credentials the authenticator
				 * refused leave the connection to fail its own pre-validation and disconnect
				 * itself, which this panel can only report as an unexplained drop. */
				if (!Client.LoginAuthenticator.SetLoginCredentials(usernameText, passwordText, true, emailText, age))
				{
					if (statusMessage != null)
					{
						statusMessage.text = "Those account details were rejected. Please check them and try again.";
					}
					Log.Warning("UITKRegister", "Create account failed: the authenticator rejected the credentials.");
					SetFormLocked(false);
					return;
				}

				Client.ConnectToServer(serverPort);
			}
			else
			{
				SetFormLocked(false);
			}
		}

		/// <summary>
		/// Displays a client-side validation error via dialog box.
		/// </summary>
		/// <param name="error">The validation error message.</param>
		private void ShowValidationError(string error)
		{
			LoginNotice.Show(error);
			SetFormLocked(false);
		}

		/// <summary>
		/// Clears all input fields and resets the age dropdown.
		/// </summary>
		private void ClearAllFields()
		{
			if (username != null)
			{
				username.value = "";
			}
			if (email != null)
			{
				email.value = "";
			}
			if (password != null)
			{
				password.value = "";
			}
			if (ageSelect != null)
			{
				ageSelect.index = 0;
			}
		}


		/// <summary>
		/// Guards the control this panel disables while a server reply is outstanding.
		/// </summary>
		/// <remarks>See <see cref="PendingReplyGuard"/>.</remarks>
		private readonly PendingReplyGuard replyGuard = new PendingReplyGuard();

		/// <inheritdoc/>
		protected override void OnTick()
		{
			base.OnTick();

			// Login-flow notices are refused while another dialog is up; see LoginNotice.
			LoginNotice.Pump();

			if (replyGuard.HasExpired())
			{
				OnReplyTimedOut();
			}
		}

		/// <summary>
		/// Ends a registration the server stopped answering, on a screen the player can see.
		/// </summary>
		/// <remarks>
		/// The timeout used to re-enable the controls and write a sentence into
		/// <c>statusMessage</c>. That label is on this panel, and the panel is hidden for the
		/// whole of the part of the flow that can actually stall — so the one failure this
		/// watchdog exists for produced no visible change whatsoever.
		/// <para>
		/// The <see cref="accountCreated"/> branch is the real fix for the 2FA hang. When the
		/// account exists and only <c>TwoFactorSetupBroadcast</c> went missing — which is
		/// guaranteed, not hypothetical, whenever the login server's <c>TotpMasterKey</c> is
		/// unset or not 32 bytes: <c>AccountCreationSystem</c> logs an error and carries straight
		/// on — the correct exit is not to fail, it is to skip the step. The account is real, the
		/// verification email has been sent, and email verification is the next step regardless.
		/// Restarting registration instead would only earn a refusal for an account that already
		/// exists.
		/// </para>
		/// </remarks>
		private void OnReplyTimedOut()
		{
			ReleaseControls(true);

			/* The password's documented lifetime is "until the 2FA payload has been encrypted with
			 * it". This is the path on which that payload never arrives, so nothing else was ever
			 * going to drop it: the flow continues into email verification and can sit on that
			 * dialog indefinitely, with the plaintext password held in a field the whole time. It
			 * has no remaining use — the envelope it existed to encrypt is not coming. */
			pendingAccountPassword = null;

			/* Cancel any prompt still on screen first. Under the shared dialog contract a Hide()
			 * on an armed dialog resolves it down its cancel path, so this cannot strand a
			 * caller. */
			if (UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox inputBox) && inputBox.Visible)
			{
				inputBox.Hide();
			}

			Show();

			if (this.accountCreated && !string.IsNullOrEmpty(pendingVerifyUsername))
			{
				SetStatus("Two-factor setup did not complete.");

				string message = "Your account was created, but the server did not finish setting up " +
					"two-factor authentication.\n\nYour account is fine. Continue to email verification " +
					"and sign in as normal; two-factor authentication can be set up later.";

				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox dialogBox) &&
					dialogBox.Open(message, OpenVerifyCodeDialog, OpenVerifyCodeDialog))
				{
					return;
				}

				// Dialog busy or absent — the verification prompt is the important half.
				Log.Warning("UITKRegister", message);
				OpenVerifyCodeDialog();
				return;
			}

			SetStatus("The server did not respond. Please try again.");
		}

		/// <summary>
		/// Sets the locked state of all form controls (enables/disables interactivity).
		/// Also manages the <see cref="isAuthFlowActive"/> flag: locking marks
		/// the start of a registration flow; unlocking marks its termination.
		/// </summary>
		/// <param name="locked">True to lock (disable) controls, false to unlock.</param>
		public void SetFormLocked(bool locked)
		{
			// Locking means a request is outstanding; unlocking means it is not.
			// See PendingReplyGuard for why the wait needs a deadline.
			if (locked) { replyGuard.Begin(); } else { replyGuard.Clear(); }

			// Track auth-flow ownership: locking = start of flow, unlocking = end.
			if (locked) isAuthFlowActive = true;
			else isAuthFlowActive = false;

			ReleaseControls(!locked);
		}

		/// <summary>
		/// Enables or disables this panel's controls without touching the auth-flow flag.
		/// </summary>
		/// <remarks>
		/// Split out for the reply timeout. <c>SetFormLocked(false)</c> also clears
		/// <c>isAuthFlowActive</c>, which is what gates this panel's auth-result handler —
		/// so handing the controls back that way would make the panel ignore a reply that
		/// arrives after the deadline, turning a merely slow login into a stuck one. The
		/// timeout is deliberately non-destructive: it re-enables the controls and says so,
		/// and a late reply is still handled normally.
		/// </remarks>
		/// <param name="interactable">True to enable the controls.</param>
		private void ReleaseControls(bool interactable)
		{
			if (registerButton != null)
			{
				registerButton.SetEnabled(interactable);
			}
			if (quitToLoginButton != null)
			{
				quitToLoginButton.SetEnabled(interactable);
			}
			if (username != null)
			{
				username.SetEnabled(interactable);
			}
			if (email != null)
			{
				email.SetEnabled(interactable);
			}
			if (password != null)
			{
				password.SetEnabled(interactable);
			}
			if (ageSelect != null)
			{
				ageSelect.SetEnabled(interactable);
			}
		}
	}
}
