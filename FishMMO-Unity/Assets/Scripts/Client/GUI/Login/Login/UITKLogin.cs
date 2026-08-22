using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Logging;
using System;
using System.Collections;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the login control providing identifier/password input,
	/// registration access, sign-in and the full authentication-result handling flow.
	/// </summary>
	/// <remarks>
	/// <para><b>Secure password input limitation:</b> <see cref="TextField"/> stores text as a
	/// managed <see cref="string"/> and exposes no <c>byte[]</c>, <c>char[]</c>, or
	/// <c>SecureString</c> API. .NET strings are immutable and cannot be deterministically zeroed.
	/// The password-field masking provides visual masking only. The SRP protocol ensures the
	/// password never travels on the wire, which is the primary protection.</para>
	/// </remarks>
	public class UITKLogin : UITKControl
	{
		/// <summary>
		/// Full-screen forms are not windows: there is nowhere to drag them to.
		/// </summary>
		/// <remarks>See <see cref="UITKControl.CanDrag"/>, which defaults every
		/// <see cref="UITKPanelLayer.Window"/> panel to draggable.</remarks>
		protected override bool CanDrag => false;

		/// <summary>
		/// The name of the identifier TextField in the UI — a username <b>or</b> an email address.
		/// </summary>
		/// <remarks>
		/// One field, not two. The panel used to carry a separate Email input beside Username and
		/// pick whichever was filled, which asked the player to classify their own credential
		/// before typing it; the submit path already decides that from the text itself. It also
		/// carried an Age dropdown that was never populated and never read — an empty control that
		/// looked broken and collected nothing. Age belongs to registration, where it is gated.
		/// </remarks>
		private const string USERNAME_NAME = "login-username";
		/// <summary>
		/// The name of the password TextField in the UI.
		/// </summary>
		private const string PASSWORD_NAME = "login-password";
		/// <summary>
		/// <summary>
		/// The name of the register button in the UI.
		/// </summary>
		private const string REGISTER_BUTTON_NAME = "login-register-btn";
		/// <summary>
		/// The name of the sign-in button in the UI.
		/// </summary>
		private const string SIGN_IN_BUTTON_NAME = "login-signin-btn";
		/// <summary>
		/// The name of the options button in the UI.
		/// </summary>
		private const string OPTIONS_BUTTON_NAME = "login-options-btn";
		/// <summary>
		/// The name of the quit button in the UI.
		/// </summary>
		private const string QUIT_BUTTON_NAME = "login-quit-btn";
		/// <summary>
		/// The name of the handshake message Label in the UI.
		/// </summary>
		private const string HANDSHAKE_NAME = "login-handshake";

		private TextField username;
		private TextField password;
		private Button registerButton;
		private Button signInButton;
		private Label handshakeMessage;

		/// <summary>
		/// Called when a Login Success Client Authentication result is received from the server.
		/// </summary>
		public Action OnLoginSuccessStart;

		/// <summary>
		/// Called after OnLoginSuccessStart finishes.
		/// </summary>
		public Action OnLoginSuccessEnd;

		/// <summary>
		/// Temporarily stores the login identifier for verification code submission
		/// when the server responds with AccountUnverified.
		/// </summary>
		private string pendingVerifyUsername;

		/// <summary>
		/// True when this panel owns the active authentication flow (login, account
		/// verification, or TOTP). Gates auth-result handling so results belonging to
		/// <see cref="UITKRegister"/> — which shares the same
		/// <see cref="ClientLoginAuthenticator.OnClientAuthenticationResult"/> event — are not
		/// acted on here.
		/// </summary>
		/// <remarks>
		/// Panel visibility cannot serve this purpose, even though it looks equivalent. Both
		/// multi-step flows hide this panel while a modal dialog collects the next input:
		/// <see cref="OnAccountUnverified"/> and <see cref="OnTwoFactorRequired"/> call
		/// <see cref="Hide"/> before opening theirs. Gating on <c>Visible</c> therefore dropped
		/// every result that arrived after that point — the <c>AccountVerified</c> that follows
		/// a correct verification code, and the <c>LoginSuccess</c> or <c>TwoFactorInvalid</c>
		/// that follows a TOTP code — so an account with 2FA enabled could enter the right code
		/// and simply sit on the dialog forever. <see cref="UITKRegister"/> carries an explicit
		/// flag of its own for exactly this reason.
		/// </remarks>
		private bool isAuthFlowActive;

		/// <summary>
		/// True once the server has answered this panel's in-flight request with an
		/// authentication result, so the player has already been told what happened.
		/// </summary>
		/// <remarks>
		/// The login handshake is refused before authentication for reasons the server
		/// deliberately does not put on the wire — an expired or unverifiable connection token,
		/// a protocol version outside the supported range, an oversized field, a tripped
		/// handshake rate limit. Every one of those is a bare transport close, and the Stopped
		/// handler's job is to hand the controls back, so the player clicked Sign In and watched
		/// the form reset with no message at all. Losing the login server mid-flow looked
		/// identical.
		/// <para>
		/// This distinguishes "the server said no" from "the server said nothing": a result of
		/// any kind means the specific message has already been shown and the generic one would
		/// only contradict it. Reset when a new attempt starts.
		/// </para>
		/// </remarks>
		private bool authResultSeen;

		/// <summary>
		/// Resolves and caches visual elements and wires up button callbacks.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			username = Root.Q<TextField>(USERNAME_NAME);
			password = Root.Q<TextField>(PASSWORD_NAME);
			registerButton = Root.Q<Button>(REGISTER_BUTTON_NAME);
			signInButton = Root.Q<Button>(SIGN_IN_BUTTON_NAME);
			handshakeMessage = Root.Q<Label>(HANDSHAKE_NAME);

			if (password != null)
			{
				password.isPasswordField = true;
			}

			if (registerButton != null)
			{
				registerButton.clicked += OnClick_OnRegister;
			}
			if (signInButton != null)
			{
				signInButton.clicked += OnClick_Login;
			}

			Button optionsButton = Root.Q<Button>(OPTIONS_BUTTON_NAME);
			if (optionsButton != null)
			{
				optionsButton.clicked += OnClick_OnOptions;
			}

			Button quitButton = Root.Q<Button>(QUIT_BUTTON_NAME);
			if (quitButton != null)
			{
				quitButton.clicked += OnClick_Quit;
			}

			/* Keyboard. There was none anywhere in the login tree, so the first thing a player
			 * does in this game — type a username and a password — ended with them reaching for
			 * the mouse. Enter signs in from any field; Escape clears the form rather than doing
			 * something destructive, because this is the screen the rest of the flow escapes back
			 * TO and there is nothing above it to close. */
			// Enter observes the same lock as the Sign In button it mirrors; see LoginKeys.Attach.
			LoginKeys.Attach(this, Root, OnClick_Login, OnEscape_ClearForm, () => !replyGuard.IsPending);
			LoginKeys.SetTabOrder(Root, username, password, signInButton, registerButton, optionsButton, quitButton);
		}

		/// <summary>
		/// Empties the credential fields.
		/// </summary>
		/// <remarks>
		/// Escape's job on the one screen with no screen behind it. Doubles as the fastest way to
		/// clear a password off a shared machine, which is why it clears rather than merely
		/// dropping focus.
		/// </remarks>
		private void OnEscape_ClearForm()
		{
			if (username != null) username.value = string.Empty;
			if (password != null) password.value = string.Empty;
			LoginKeys.FocusFirst(Root, username);
		}

		/// <summary>
		/// Puts the caret where the player is going to type, on controls whose lock state matches
		/// the request that is actually outstanding.
		/// </summary>
		protected override void OnAfterShow()
		{
			base.OnAfterShow();

			ReleaseControls(!replyGuard.IsPending);

			// Empty username means a fresh sign-in; otherwise they are most likely back here after
			// a rejected password and the username is already right.
			LoginKeys.FocusFirst(Root,
				username != null && string.IsNullOrEmpty(username.value) ? username : password);
		}

		/// <summary>
		/// Re-applies the sign-in lock after the visual tree was rebuilt.
		/// </summary>
		/// <remarks>
		/// <see cref="SetSignInLocked"/> writes into elements that the next hide/show replaces, so
		/// a panel that came back during a login still in flight — which is exactly what the
		/// verification and TOTP flows do, and what the reply timeout does — presented an enabled
		/// Sign In button over a request the client was still waiting on. Driven off the guard
		/// rather than off a second copy of the flag, so the two cannot disagree.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();

			ReleaseControls(!replyGuard.IsPending);
		}

		/// <summary>
		/// Subscribes to connection and authentication events when the client is injected.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.RegisterBroadcast<LoginQueuePositionBroadcast>(OnLoginQueuePosition);
			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
			Client.OnReconnectFailed += ClientManager_OnReconnectFailed;
		}

		/// <summary>
		/// Keeps the reply watchdog alive while this client is waiting in the login queue.
		/// </summary>
		/// <remarks>
		/// The queue is the one place a login legitimately takes longer than
		/// <see cref="PendingReplyGuard.DefaultTimeoutSeconds"/>, and its position updates are
		/// handled by <see cref="Client"/> rather than by this panel — so the watchdog saw a
		/// server that had said nothing for thirty seconds, announced that it had not responded,
		/// and handed the sign-in controls back while the queue dialog was still counting down
		/// beside it. Clicking sign-in from there only produced "connection already in progress".
		/// <para>
		/// A position update is proof the server is still working this login, so it buys the
		/// deadline again exactly like an intermediate auth result does. Positions above zero
		/// travel unreliably, but they are re-sent every couple of seconds, so a dropped one
		/// costs nothing.
		/// </para>
		/// </remarks>
		private void OnLoginQueuePosition(LoginQueuePositionBroadcast msg, Channel channel)
		{
			// -1 means the wait was abandoned; Client tears the session down and explains it,
			// and refreshing a deadline for a login that is over would only delay this panel
			// noticing.
			if (msg.QueuePosition >= 0)
			{
				replyGuard.Refresh();
			}
		}


		/// <summary>
		/// Unsubscribes from connection and authentication events when the client is cleared.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.UnregisterBroadcast<LoginQueuePositionBroadcast>(OnLoginQueuePosition);
			Client.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			Client.OnReconnectFailed -= ClientManager_OnReconnectFailed;
		}

		/// <summary>
		/// Shows the login panel and unlocks sign-in controls when quitting to login.
		/// </summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			Show();

			SetSignInLocked(false);
		}

		/// <summary>
		/// Hides the login panel and resets the handshake message.
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
				// The base refused the hide; the panel is still up, so keep what it is showing.
				return;
			}

			// A handshake message describes one attempt and must not greet the next one.
			if (handshakeMessage != null)
			{
				handshakeMessage.text = "";
			}
		}

		/// <summary>
		/// Handles client connection state changes. Resets handshake message and unlocks sign-in controls when disconnected.
		/// </summary>
		/// <param name="obj">Connection state arguments.</param>
		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				// Read before SetSignInLocked below, which clears isAuthFlowActive.
				bool droppedWithoutExplanation = isAuthFlowActive && !authResultSeen;

				if (handshakeMessage != null)
				{
					handshakeMessage.text = "";
				}
				SetSignInLocked(false);
				pendingVerifyUsername = null;

				if (droppedWithoutExplanation)
				{
					ShowUnexplainedDisconnect();
				}
			}
		}

		/// <summary>
		/// Tells the player that sign-in ended without the server saying why.
		/// </summary>
		/// <remarks>See <see cref="authResultSeen"/> for when this applies.</remarks>
		private void ShowUnexplainedDisconnect()
		{
			const string message = "Could not sign in.\nThe connection to the login server was closed before it answered.\n" +
				"Please check your connection and try again, and make sure your client is up to date.";

			Show();
			if (handshakeMessage != null)
			{
				handshakeMessage.text = "Sign-in failed. The server closed the connection.";
			}

			/* Through the queue, not straight at the dialog. The shared dialog now refuses an
			 * Open while another question is on screen rather than hijacking it, and this message
			 * is raised on a path that has already disconnected — there is no second chance to
			 * say it. See LoginNotice. */
			LoginNotice.Show(message);
		}

		/// <summary>
		/// Handles reconnect failure. Shows login panel and unlocks sign-in controls.
		/// </summary>
		private void ClientManager_OnReconnectFailed()
		{
			Show();
			SetSignInLocked(false);
		}

		/// <summary>
		/// Handles authentication results and displays appropriate dialogs or proceeds with login success.
		/// </summary>
		/// <param name="result">The result of client authentication.</param>
		private void Authenticator_OnClientAuthenticationResult(ClientAuthenticationResult result)
		{
			/* Any result is the server telling us it is still working this request —
			 * the SRP exchange and the two-factor prompt both report progress before
			 * they finish, and a client can sit in the login queue for minutes. Each
			 * one buys the reply deadline again rather than counting against it. */
			replyGuard.Refresh();

			// Only process auth results when this panel owns the active flow. See
			// isAuthFlowActive for why panel visibility is not a usable substitute.
			if (!isAuthFlowActive) return;

			// The server answered, so whatever happens next has an explanation of its own.
			authResultSeen = true;

			switch (result)
			{
				case ClientAuthenticationResult.AccountCreated:
					OnLoginAuthenticationDialog("Your account has been created!");
					break;
				case ClientAuthenticationResult.InvalidUsernameOrPassword:
					OnLoginAuthenticationDialog("Invalid Username or Password.");
					break;
				case ClientAuthenticationResult.AlreadyOnline:
					OnLoginAuthenticationDialog("Account is already online.");
					break;
				case ClientAuthenticationResult.Banned:
					OnLoginAuthenticationDialog("Account is banned. Please contact the system administrator.");
					break;
				case ClientAuthenticationResult.AccountUnverified:
					OnAccountUnverified();
					break;
				case ClientAuthenticationResult.AccountVerified:
					OnAccountVerified();
					break;
				case ClientAuthenticationResult.TwoFactorRequired:
					OnTwoFactorRequired();
					break;
				case ClientAuthenticationResult.TwoFactorInvalid:
					OnTwoFactorInvalid();
					break;
				case ClientAuthenticationResult.LoginSuccess:
					OnLoginSuccess();
					break;
				case ClientAuthenticationResult.ServerFull:
					OnLoginAuthenticationDialog("Server is currently full please wait a while and try again.");
					break;
				case ClientAuthenticationResult.ServerLocked:
					OnLoginAuthenticationDialog("This world is closed for maintenance. Please try again later.");
					break;
				case ClientAuthenticationResult.ServerBusy:
					OnLoginAuthenticationDialog("Server is busy. Please try again.");
					break;
				case ClientAuthenticationResult.VersionMismatch:
					OnVersionMismatch();
					break;
				/* A rejected session token is not a version problem. Reporting it as one told
				 * the player to update a client that is perfectly current, and hid the real
				 * cause — an expired or revoked session — behind advice that cannot fix it. */
				case ClientAuthenticationResult.TokenInvalid:
				case ClientAuthenticationResult.TokenExpired:
				case ClientAuthenticationResult.TokenRevoked:
				case ClientAuthenticationResult.TokenDecryptFailed:
					OnLoginAuthenticationDialog("Your session has expired. Please log in again.");
					break;
				// Not applicable during login flow.
				case ClientAuthenticationResult.SrpVerify:
				case ClientAuthenticationResult.SrpProof:
				case ClientAuthenticationResult.WorldLoginSuccess:
				case ClientAuthenticationResult.SceneLoginSuccess:
				case ClientAuthenticationResult.NoCharacterSelected:
					break;
			}
		}

		/// <summary>
		/// Handles AccountUnverified: stays connected and opens the verification code input dialog.
		/// Sets <see cref="pendingVerifyUsername"/> only now — not eagerly in OnClick_Login —
		/// so the identifier doesn't linger in memory if the connection drops before this point.
		/// </summary>
		private void OnAccountUnverified()
		{
			// Capture identifier from authenticator (still set at this point; cleared only after SRP proof).
			string identifier = Client.LoginAuthenticator.PendingLoginIdentifier;
			if (string.IsNullOrEmpty(identifier))
			{
				// Identifier already cleared — can't verify. Return to login.
				Client.ForceDisconnect();
				SetSignInLocked(false);
				return;
			}
			pendingVerifyUsername = identifier;
			SetSignInLocked(true);
			Hide();

			/* Stop the reply clock. SetSignInLocked arms it, but from here the client is waiting
			 * on a person going to fetch a code out of their email — which routinely takes longer
			 * than the thirty seconds the watchdog allows. Leaving it running meant the watchdog
			 * fired mid-typing and, before this, wrote its explanation into the panel it had just
			 * hidden. It is re-armed the instant a code is actually sent. */
			replyGuard.Clear();

			if (UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox uiDialogInputBox))
			{
				uiDialogInputBox.Open(
					"Your account has not been verified. Please enter the verification code sent to your email.",
					(code) =>
					{
						if (!string.IsNullOrWhiteSpace(pendingVerifyUsername) && !string.IsNullOrWhiteSpace(code))
						{
							Client.LoginAuthenticator.SendVerifyCode(pendingVerifyUsername, code.Trim());

							// A request is outstanding again; restart the clock.
							replyGuard.Begin();
						}
					},
					() =>
					{
						pendingVerifyUsername = null;
						Client.ForceDisconnect();
						SetSignInLocked(false);
						Show();
					});
			}
		}

		/// <summary>
		/// Handles successful account verification: disconnects and returns to the login screen.
		/// </summary>
		private void OnAccountVerified()
		{
			pendingVerifyUsername = null;
			Client.ForceDisconnect();
			SetSignInLocked(false);

			LoginNotice.Show("Your account has been verified! You may now log in.");
			Show();
		}

		/// <summary>
		/// Handles TwoFactorRequired: opens a TOTP code input dialog.
		/// </summary>
		private void OnTwoFactorRequired()
		{
			SetSignInLocked(true);
			Hide();
			OpenTotpDialog("Enter the 6-digit code from your authenticator app.");
		}

		/// <summary>
		/// Handles TwoFactorInvalid: re-opens the TOTP dialog with an error message.
		/// </summary>
		private void OnTwoFactorInvalid()
		{
			OpenTotpDialog("Invalid code. Please try again.");
		}

		/// <summary>
		/// Opens the TOTP code input dialog with the specified prompt.
		/// </summary>
		/// <param name="message">The prompt to display.</param>
		private void OpenTotpDialog(string message)
		{
			// Waiting on the player and their authenticator app, not on the server.
			// See OnAccountUnverified.
			replyGuard.Clear();

			if (UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox uiDialogInputBox))
			{
				uiDialogInputBox.Open(
					message,
					(code) =>
					{
						if (!string.IsNullOrWhiteSpace(code))
						{
							Client.LoginAuthenticator.SendTotpCode(code.Trim());

							// A request is outstanding again; restart the clock.
							replyGuard.Begin();
						}
					},
					() =>
					{
						pendingVerifyUsername = null;
						Client.ForceDisconnect();
						SetSignInLocked(false);
						Show();
					});
			}
		}

		/// <summary>
		/// Called when the server rejects the client due to a game version mismatch.
		/// Shows a dialog and disconnects.
		/// </summary>
		private void OnVersionMismatch()
		{
			string myVersion = MainBootstrapSystem.GameVersion ?? "unknown";
			// Report the mismatch before forcing the disconnect. A bare disconnect here leaves
			// the player staring at a login screen with no indication of why they were rejected,
			// which is how this presented when the dialog lookup was silently failing.
			LoginNotice.Show($"Game version mismatch.\n\nYour client is version {myVersion}.\nThe server expects a different version.\n\nPlease update your client to match the server.");
			Client.ForceDisconnect();
			SetSignInLocked(false);
			Show();
		}

		/// <summary>
		/// Shows a dialog for authentication errors and disconnects the client.
			/// </summary>
			/// <param name="errorMsg">The error message to display.</param>
			private void OnLoginAuthenticationDialog(string errorMsg)
		{
			LoginNotice.Show(errorMsg);
			Client.ForceDisconnect();
			SetSignInLocked(false);

			/* Put the panel back. Every caller of this method has already hidden something —
			 * either this panel (the TOTP and verification flows) or, on a refusal that arrives
			 * after login success, everything — and a dismissed dialog would otherwise leave the
			 * player looking at an empty scene. */
			Show();
		}

		/// <summary>
		/// Handles successful login, updates handshake message, and starts post-login coroutine.
		/// </summary>
		private void OnLoginSuccess()
		{
			if (handshakeMessage != null)
			{
				handshakeMessage.text = "Connected";
			}

			OnLoginSuccessStart?.Invoke();

			Client.StartCoroutine(OnProcessLoginSuccess());
		}

		/// <summary>
		/// Coroutine for post-login processing, requests character list after delay.
		/// </summary>
		/// <returns>IEnumerator for coroutine.</returns>
		private IEnumerator OnProcessLoginSuccess()
		{
			// Wait 1 second before requesting the character list.
			yield return new WaitForSeconds(1.0f);

			Hide();

			/* Through the character-select panel, which arms a reply deadline and knows how to ask
			 * again. Broadcasting bare from here is what made this the worst moment in the whole
			 * flow to lose a message: this panel has just hidden itself on the line above and the
			 * character-select panel only shows when a list arrives, so an unanswered request left
			 * the player with no panel on screen at all. */
			if (UIManager.TryGetTK("UICharacterSelect", out UITKCharacterSelect characterSelect))
			{
				characterSelect.RequestCharacterList();
			}
			else
			{
				// No character-select panel in this scene; the request is still worth making.
				Client.Broadcast(new CharacterRequestListBroadcast(), Channel.Reliable);
			}

			OnLoginSuccessEnd?.Invoke();

			SetSignInLocked(false);
		}

		/// <summary>
		/// Hides the login panel and shows the registration panel.
		/// </summary>
		public void OnClick_OnRegister()
		{
			if (UIManager.TryGetTK("UIRegister", out UITKRegister uiRegister))
			{
				Hide();
				uiRegister.Show();
			}
		}

		/// <summary>
		/// Shows the options panel, which lives in ClientPreboot and is therefore shared with
		/// every scene loaded after it rather than owned by this one.
		/// </summary>
		public void OnClick_OnOptions()
		{
			if (UIManager.TryGetTK("UIOptions", out UITKOptions uiOptions))
			{
				uiOptions.Show();
			}
		}

		/// <summary>
		/// Validates input and initiates the login process.
		/// </summary>
		public void OnClick_Login()
		{
			/* Refuse re-entry while a sign-in is already outstanding. The button is disabled for
			 * the whole of that wait, but Enter does not go through the button — see
			 * LoginKeys.Attach — so a second press ran this method again, fell down a validation
			 * path (the identifier and password having been taken already), and unlocked the form.
			 * Unlocking clears isAuthFlowActive, which is what gates the auth-result handler, so
			 * the answer to the login that was genuinely in flight was then dropped. */
			if (replyGuard.IsPending)
			{
				return;
			}

			string identifier = username != null ? username.value : null;

			if (string.IsNullOrWhiteSpace(identifier))
			{
				// Say which of the two things is missing rather than that "login failed".
				if (handshakeMessage != null)
				{
					handshakeMessage.text = "Please enter your username or email address.";
				}
				Log.Warning("UITKLogin", "Login validation failed: no identifier entered.");
				return;
			}

			/* One field, so the text decides which kind of credential it holds. The two regexes
			 * behind these validators are disjoint — a username is `^[a-zA-Z0-9_]+$` and cannot
			 * contain '@', while an email address must — so testing for '@' picks exactly the
			 * validator that could match, and the same split is what AccountService uses to choose
			 * between its by-email and by-name lookups. Classifying first is what lets the failure
			 * message describe what the player actually typed: validating an address as a username
			 * would reject it for containing the one character that makes it an address, and then
			 * complain about username format. */
			bool looksLikeEmail = identifier.IndexOf('@') >= 0;
			bool identifierValid = looksLikeEmail
				? Authentication.IsAllowedEmailUsername(identifier)
				: Authentication.IsAllowedUsername(identifier);

			if (!identifierValid)
			{
				if (handshakeMessage != null)
				{
					handshakeMessage.text = looksLikeEmail
						? "That does not look like a valid email address."
						: "Invalid username format. Use 3-32 characters (letters, numbers, underscores).";
				}
				Log.Warning("UITKLogin", "Login validation failed: invalid identifier.");
				return;
			}

			string passwordText = password != null ? password.value : null;
			if (!Authentication.IsAllowedPassword(passwordText))
			{
				if (string.IsNullOrWhiteSpace(passwordText))
				{
					if (handshakeMessage != null)
					{
						handshakeMessage.text = "Please enter a password.";
					}
				}
				else
				{
					if (handshakeMessage != null)
					{
						handshakeMessage.text = "Invalid password. Must be 8-32 characters with allowed symbols.";
					}
				}
				Log.Warning("UITKLogin", "Login validation failed: invalid password.");
				return;
			}

			authResultSeen = false;
			SetSignInLocked(true);

			/* The credentials are handed over in a holder the closures below can empty, rather
			 * than captured directly. A lambda that closes over the password string keeps that
			 * string reachable for the whole lifetime of the coroutine — the HTTP round trip to
			 * the login-server list, which is seconds, plus however long the coroutine object
			 * itself survives — and nothing ever dropped the reference afterwards, so the plaintext
			 * password sat in the managed heap until an unrelated GC happened to reclaim it. .NET
			 * strings cannot be zeroed, so releasing the last reference as early as possible is the
			 * whole of the available mitigation. */
			PendingCredentials credentials = new PendingCredentials(identifier, passwordText);
			passwordText = null;

			/* And out of the field, which is the copy the holder above cannot reach. A TextField
			 * keeps whatever was typed into it for as long as the tree lives, so the plaintext
			 * password sat there for the whole session — and was still there, pre-filled, whenever
			 * this panel was shown again, which is every rejected sign-in and every quit to login.
			 * That defeats the point of the holder: the password was handed to the authenticator
			 * on the line below and this panel has no further use for it. The identifier is
			 * deliberately left alone — OnAfterShow relies on it to decide where the caret goes,
			 * and it is not a secret. */
			if (password != null)
			{
				password.value = string.Empty;
			}

			StartCoroutine(Client.GetLoginServerList((e) =>
			{
				credentials.Clear();
				LoginNotice.Show(e);
				Log.Warning("UITKLogin", e);
				SetSignInLocked(false);
			},
			(servers, token) =>
			{
				if (!string.IsNullOrEmpty(token)) Client.LoginAuthenticator.ConnectionToken = token;
				try
				{
					Connect("Connecting...", credentials.Identifier, credentials.Password);
				}
				finally
				{
					// Either way. The authenticator owns the credentials from here.
					credentials.Clear();
				}
			}));
		}

		/// <summary>
		/// Carries one login attempt's credentials across the login-server-list round trip.
		/// </summary>
		/// <remarks>
		/// A mutable holder rather than captured locals, so both continuations can drop the
		/// references. See the call site for why that matters.
		/// </remarks>
		private sealed class PendingCredentials
		{
			public PendingCredentials(string identifier, string password)
			{
				Identifier = identifier;
				Password = password;
			}

			/// <summary>The username or email the attempt was made with.</summary>
			public string Identifier { get; private set; }

			/// <summary>The password the attempt was made with.</summary>
			public string Password { get; private set; }

			/// <summary>Releases both references.</summary>
			public void Clear()
			{
				Identifier = null;
				Password = null;
			}
		}

		/// <summary>
		/// Attempts to connect to the login server with the provided credentials.
		/// </summary>
		/// <param name="handshakeMsg">Message to display during handshake.</param>
		/// <param name="identifier">Login identifier (username or email).</param>
		/// <param name="passwordText">Password to use.</param>
		/// <param name="address">Optional server address.</param>
		/// <param name="port">Optional server port.</param>
		private void Connect(string handshakeMsg, string identifier, string passwordText, string address = null, ushort port = 0)
		{
			// Validate preconditions individually so the player gets a specific error
			// message instead of the button silently unlocking with no feedback.
			if (!Client.IsConnectionReady(LocalConnectionState.Stopped))
			{
				if (handshakeMessage != null)
				{
					handshakeMessage.text = "Connection already in progress. Please wait.";
				}
				Log.Warning("UITKLogin", "Connect failed: connection is not in Stopped state.");
				SetSignInLocked(false);
				return;
			}

			if (!Authentication.IsAllowedUsername(identifier) && !Authentication.IsAllowedEmailUsername(identifier))
			{
				if (handshakeMessage != null)
				{
					handshakeMessage.text = "Invalid username or email format.";
				}
				Log.Warning("UITKLogin", "Connect failed: identifier validation failed.");
				SetSignInLocked(false);
				return;
			}

			if (!Authentication.IsAllowedPassword(passwordText))
			{
				if (handshakeMessage != null)
				{
					handshakeMessage.text = "Invalid password format.";
				}
				Log.Warning("UITKLogin", "Connect failed: password validation failed.");
				SetSignInLocked(false);
				return;
			}

			if (!Client.TryGetRandomLoginServerPort(out ushort serverPort))
			{
				if (handshakeMessage != null)
				{
					handshakeMessage.text = "No login servers available. Check your internet connection.";
				}
				Log.Warning("UITKLogin", "Connect failed: no login server addresses available.");
				SetSignInLocked(false);
				return;
			}

			if (handshakeMessage != null)
			{
				handshakeMessage.text = handshakeMsg;
			}
			Client.LoginAuthenticator.SetLoginCredentials(identifier, passwordText);
			Client.ConnectToServer(serverPort);
		}

		/// <summary>
		/// Quits the client application.
		/// </summary>
		public void OnClick_Quit()
		{
			Client.Quit();
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

			TickVisiblePanelInvariant();
		}

		/// <summary>
		/// Hands the controls back when the server stops answering, and puts them somewhere the
		/// player can see them.
		/// </summary>
		/// <remarks>
		/// The timeout used to write "the server did not respond" into <c>handshakeMessage</c> and
		/// stop there. That is a label on this panel, and both multi-step flows — account
		/// verification and TOTP — <see cref="Hide"/> this panel before opening their modal
		/// prompt. So the one case the watchdog exists for, a server that goes silent while a code
		/// dialog is on screen, wrote its explanation into a tree nobody was looking at and left
		/// the player on a prompt whose answer would never be acted on.
		/// <para>
		/// Cancelling the prompt is what makes this recoverable: the input dialog's cancel
		/// callback force-disconnects, unlocks sign-in and shows this panel, which is exactly the
		/// state a timed-out login should end in. Under the shared dialog contract a
		/// <c>Hide()</c> on an armed dialog resolves it down its cancel path, so one call does it.
		/// </para>
		/// </remarks>
		private void OnReplyTimedOut()
		{
			ReleaseControls(true);

			/* Cancel first. Its callback shows this panel and unlocks sign-in, and doing it before
			 * the message below means the message lands on a panel that is already on screen. */
			if (UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox inputBox) && inputBox.Visible)
			{
				inputBox.Hide();
			}

			Show();

			if (handshakeMessage != null)
			{
				handshakeMessage.text = "The server did not respond. Please try again.";
			}
		}

		/// <summary>
		/// Unscaled time from which no login-flow panel has been visible, or -1 when one is.
		/// </summary>
		private float noPanelSinceUnscaled = -1.0f;

		/// <summary>
		/// How long the client may sit with nothing on screen before this panel asserts itself.
		/// </summary>
		/// <remarks>
		/// Long enough to cover the handful of frames where one panel has hidden and the next has
		/// not shown yet, which is normal for every transition in this flow. Short enough that a
		/// player never has time to conclude the client has died.
		/// </remarks>
		private const float NoVisiblePanelGraceSeconds = 2.0f;

		/// <summary>
		/// Enforces the invariant that no reachable state leaves the player with no visible,
		/// actionable UI.
		/// </summary>
		/// <remarks>
		/// The login flow is six panels that hand off to one another by hiding themselves and
		/// showing the next, and every handoff is conditional on a message arriving. Any message
		/// that does not arrive leaves nothing on screen at all: this panel hides itself a second
		/// after login success, character select hides on <c>Stopped</c> and on receiving a list,
		/// character create and server select hide on <c>Stopped</c> — and the overlay that used
		/// to cover for that has no panel underneath it either. Alt+F4 was the only exit.
		/// <para>
		/// Individual paths have been closed (the reply guards, the loading overlay's escape
		/// hatch, <c>Client.OnConnectionAttemptFailed -> QuitToLogin</c>), but enumerating paths is
		/// exactly the approach that produced the bug. This is the backstop that makes the
		/// invariant true by construction instead: it does not care <i>why</i> the screen is empty.
		/// </para>
		/// <para>
		/// Deliberately narrow about when it fires. It requires a stopped connection — a live one
		/// means a transition is genuinely in flight and interrupting it would be the fault, not
		/// the fix — and it defers to any panel that is up, including the full-screen overlays and
		/// the shared modals, since those are visible actionable UI in their own right.
		/// </para>
		/// </remarks>
		private void TickVisiblePanelInvariant()
		{
			/* Connection state first, and not only for cost. Anything other than Stopped is a
			 * transition in progress: its own panel is expected to be silent until the server
			 * answers, and that wait is what the reply guards are for, so this must not race them.
			 * It also short-circuits the panel sweep below for the whole time the player is in the
			 * world, which is the overwhelming majority of a session. */
			if (Client == null || !Client.IsConnectionReady(LocalConnectionState.Stopped))
			{
				this.noPanelSinceUnscaled = -1.0f;
				return;
			}

			if (AnyLoginFlowUIVisible())
			{
				this.noPanelSinceUnscaled = -1.0f;
				return;
			}

			if (this.noPanelSinceUnscaled < 0.0f)
			{
				this.noPanelSinceUnscaled = Time.unscaledTime;
				return;
			}

			if (Time.unscaledTime - this.noPanelSinceUnscaled < NoVisiblePanelGraceSeconds)
			{
				return;
			}

			this.noPanelSinceUnscaled = -1.0f;

			Log.Warning("UITKLogin", "No login-flow panel was visible on a stopped connection; restoring the login screen.");

			SetSignInLocked(false);
			Show();
		}

		/// <summary>
		/// Whether anything the player could act on is currently on screen.
		/// </summary>
		/// <remarks>
		/// Named panels rather than a sweep of every registered control: the HUD panels are
		/// present and visible for a while after a world session ends, and counting those would
		/// make the invariant above unable to fire in the one situation it matters most.
		/// </remarks>
		private bool AnyLoginFlowUIVisible()
		{
			if (Visible)
			{
				return true;
			}

			foreach (string name in LoginFlowPanelNames)
			{
				if (UIManager.TryGetTK(name, out UITKControl control) && control.Visible)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// The panels that count as "the player has something to look at" during the login flow.
		/// </summary>
		private static readonly string[] LoginFlowPanelNames =
		{
			"UIRegister",
			"UIServerSelect",
			"UICharacterSelect",
			"UICharacterCreate",
			"UILoadingScreen",
			"UIReconnectDisplay",
			"UIDialogBox",
			"UIDialogInputBox",
			"UIOptions",
		};

		/// <summary>
		/// Sets the locked state for signing in (enables/disables controls).
		/// </summary>
		/// <param name="locked">True to lock (disable) controls, false to unlock.</param>
		public void SetSignInLocked(bool locked)
		{
			// Locking means a request is outstanding; unlocking means it is not.
			// See PendingReplyGuard for why the wait needs a deadline.
			if (locked) { replyGuard.Begin(); } else { replyGuard.Clear(); }

			// Track auth-flow ownership: locking = start, unlocking = end. Every path that
			// begins a login or a verification step locks, and every terminal path unlocks.
			isAuthFlowActive = locked;

			ReleaseControls(!locked);
		}

		/// <summary>
		/// Enables or disables this panel's controls without touching the auth-flow flag.
		/// </summary>
		/// <remarks>
		/// Split out for the reply timeout. <c>SetSignInLocked(false)</c> also clears
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
			if (signInButton != null)
			{
				signInButton.SetEnabled(interactable);
			}
			if (username != null)
			{
				username.SetEnabled(interactable);
			}
			if (password != null)
			{
				password.SetEnabled(interactable);
			}
		}
	}
}
