using FishNet.Transporting;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Logging;
using System.IO;

namespace FishMMO.Client
{
	/// <summary>
	/// Registration UI control providing username, email, password, age confirmation,
	/// and an optional key field for account creation.
	/// </summary>
	/// <remarks>
	/// <para><b>Field clearing:</b> All input fields are cleared immediately after the
	/// register button is pressed, before the network round-trip begins. This minimises
	/// the window during which sensitive text (password, key) resides in the managed heap
	/// via <see cref="TMP_InputField.text"/>. See the design note on
	/// <c>ClientLoginAuthenticator.password</c> for inherent .NET string limitations.</para>
	/// </remarks>
	public class UIRegister : UIControl
	{
		/// <summary>
		/// Input field for the account username.
		/// </summary>
		public TMP_InputField Username;

		/// <summary>
		/// Input field for the account email address.
		/// </summary>
		public TMP_InputField Email;

		/// <summary>
		/// Input field for the account password.
		/// </summary>
		public TMP_InputField Password;

		/// <summary>
		/// Dropdown for age selection. Options must map: index 0 = "Select your age",
		/// index 1 = "13", index 2 = "14", ..., index 108 = "120".
		/// </summary>
		[Tooltip("Dropdown options: 0=Select Age, 1=13, 2=14, ..., 108=120. Index is mapped to actual age (index + 12).")]
		public TMP_Dropdown AgeSelect;

		/// <summary>
		/// Input field for an optional registration key (reserved for future use).
		/// </summary>
		public TMP_InputField Key;

		/// <summary>
		/// Button to submit the registration form.
		/// </summary>
		public Button RegisterButton;

		/// <summary>
		/// Button to return to the login screen.
		/// </summary>
		public Button QuitToLoginButton;

		/// <summary>
		/// Text field for displaying status messages during registration.
		/// </summary>
		public TMP_Text StatusMessage;

		/// <summary>
		/// Minimum age index required to register (index into <see cref="AgeSelect"/> options).
		/// Index 0 is treated as "not selected" / under-age.
		/// </summary>
		private const int MinAgeSelectIndex = 1;

		/// <summary>
		/// True when this panel has an active registration/verification flow.
		/// Used to gate auth-result handling and prevent cross-talk with UILogin,
		/// which shares the same <see cref="ClientLoginAuthenticator.OnClientAuthenticationResult"/> event.
		/// </summary>
		private bool isAuthFlowActive;

		/// <summary>
		/// Temporarily stores the username after account creation for verification code submission.
		/// Cleared after verification completes or the connection is lost.
		/// </summary>
		private string pendingVerifyUsername;

		/// <summary>
		/// Absolute path of the on-disk copy of the 2FA setup payload (OTPAuth URI + recovery
		/// codes) written during the registration flow. Tracked here so every terminal exit
		/// from the flow (success, cancel, disconnect, server error) can scrub the file. The
		/// payload is recoverable from server-side state until first-login is complete, so
		/// keeping it on disk is intentional — the user keeps it as a persistent backup.
		/// </summary>
		private string savedTwoFactorSetupPath;

		/// <summary>
		/// Called when the client is set. Subscribes to connection and authentication events.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
			Client.LoginAuthenticator.OnTwoFactorSetupReceived += OnTwoFactorSetupReceived;
		}

		/// <summary>
		/// Called when the client is unset. Unsubscribes from connection and authentication events.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			Client.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			Client.LoginAuthenticator.OnTwoFactorSetupReceived -= OnTwoFactorSetupReceived;
		}

		/// <summary>
		/// Called when quitting to login. Shows and unlocks the registration panel.
		/// </summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			pendingVerifyUsername = null;
			DeleteSavedTwoFactorSetupFile();
			ClearAllFields();
			SetFormLocked(false);
		}

		/// <summary>
		/// Hides the registration panel and resets the status message.
		/// </summary>
		public override void Hide()
		{
			base.Hide();

			StatusMessage.text = "";
		}

		/// <summary>
		/// Handles client connection state changes. Unlocks the form when disconnected.
		/// </summary>
		/// <param name="args">Connection state arguments.</param>
		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				StatusMessage.text = "";
				SetFormLocked(false);
				pendingVerifyUsername = null;
				DeleteSavedTwoFactorSetupFile();
			}
		}

		/// <summary>
		/// Handles authentication results from the server and displays appropriate feedback.
		/// </summary>
		/// <param name="result">The result of client authentication.</param>
		private void Authenticator_OnClientAuthenticationResult(ClientAuthenticationResult result)
		{
			// Only process auth results when this panel owns the active flow.
			// Without this guard, hidden panels would still react to auth results
			// intended for the other panel (e.g., UIRegister force-disconnecting
			// on InvalidUsernameOrPassword during UILogin's login attempt).
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
				// Not applicable during registration flow.
				case ClientAuthenticationResult.SrpVerify:
				case ClientAuthenticationResult.SrpProof:
				case ClientAuthenticationResult.AlreadyOnline:
				case ClientAuthenticationResult.LoginSuccess:
				case ClientAuthenticationResult.WorldLoginSuccess:
				case ClientAuthenticationResult.SceneLoginSuccess:
				case ClientAuthenticationResult.ServerFull:
				case ClientAuthenticationResult.NoCharacterSelected:
				case ClientAuthenticationResult.TokenInvalid:
				case ClientAuthenticationResult.TokenExpired:
				case ClientAuthenticationResult.TokenRevoked:
				case ClientAuthenticationResult.AccountUnverified:
				case ClientAuthenticationResult.TwoFactorRequired:
				case ClientAuthenticationResult.TwoFactorInvalid:
				case ClientAuthenticationResult.TokenDecryptFailed:
					break;
			}
		}

		/// <summary>
		/// Handles successful account creation: hides the form and waits for the 2FA setup broadcast.
		/// The 2FA setup data is displayed by <see cref="OnTwoFactorSetupReceived"/>.
		/// </summary>
		private void OnAccountCreated()
		{
			SetFormLocked(true);
			Hide();
			StatusMessage.text = "Setting up two-factor authentication...";
		}

		/// <summary>
		/// Handles the 2FA setup data received from the server after account creation.
		/// Displays the otpauth URI and recovery codes. The verification code is delivered via email (SMTP).
		/// </summary>
		private void OnTwoFactorSetupReceived(string otpauthUri, string[] recoveryCodes)
		{
			if (string.IsNullOrEmpty(pendingVerifyUsername))
			{
				return;
			}

			// Save recovery codes and otpauth URI to disk immediately so the user
			// has a persistent copy even if they close the dialog or lose the codes.
			// The file is best-effort: any failure here is non-fatal because the
			// codes are also shown in the dialog. The path is tracked in
			// savedTwoFactorSetupPath. Successful registration keeps the file so the
			// user has a persistent backup; error/cancel/disconnect paths still scrub it.
			string savePath = Path.Combine(Application.persistentDataPath, $"2fa_setup_{pendingVerifyUsername}.txt");
			try
			{
				string saveContent = $"OTPAuth URI:\n{otpauthUri}\n\nRecovery Codes:\n{string.Join("\n", recoveryCodes)}\n";
				File.WriteAllText(savePath, saveContent);
				TryRestrictTwoFactorSetupFilePermissions(savePath);
				savedTwoFactorSetupPath = savePath;
				Log.Info("UIRegister", $"2FA setup data saved to: {savePath}");
			}
			catch (System.Exception ex)
			{
				Log.Warning("UIRegister", $"Failed to save 2FA setup data: {ex.Message}");
				savePath = null;
				savedTwoFactorSetupPath = null;
			}

			string codesDisplay = string.Join("\n", recoveryCodes);
			string message = "Two-Factor Authentication Setup\n\n" +
				"Scan the following URI with your authenticator app (e.g. Google Authenticator):\n\n" +
				otpauthUri + "\n\n" +
				"Recovery Codes (save these somewhere safe!):\n\n" +
				codesDisplay + "\n\n" +
				(savePath != null
					? $"A temporary copy was written to:\n{savePath}\n(Keep this file as a backup — you can delete it manually when ready.)\n\n"
					: "") +
				"Press Confirm to finish registration. A verification email will be sent to your inbox.";

			if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
			{
				uiDialogBox.Open(
					message,
					() =>
					{
						// Disconnect and prompt user to check email for verification code.
						Client.ForceDisconnect();
						SetFormLocked(false);
						pendingVerifyUsername = null;
						// Keep the 2FA setup file — user may want it as safe storage.
						if (UIManager.TryGet("UIDialogBox", out UIDialogBox verifySentDialog))
						{
							verifySentDialog.Open("Your account has been created! A verification email has been sent to your inbox. Please check your email and enter the verification code when you log in.\n\nThe 2FA setup file has been kept for your records. Please delete it manually when you no longer need it.");
						}
						Hide();
						if (UIManager.TryGet("UILogin", out UILogin uiLogin))
						{
							uiLogin.HandshakeMSG.text = "Account created! Check your email for the verification code.";
							uiLogin.Show();
						}
					},
					() =>
					{
						pendingVerifyUsername = null;
						DeleteSavedTwoFactorSetupFile();
						Client.ForceDisconnect();
						SetFormLocked(false);
						if (UIManager.TryGet("UILogin", out UILogin uiLogin))
						{
							uiLogin.Show();
						}
					});
			}
		}

		/// <summary>
		/// Handles successful account verification: disconnects and returns to the login screen.
		/// </summary>
		private void OnAccountVerified()
		{
			pendingVerifyUsername = null;
			// Keep the 2FA setup file — user may want it as safe storage.
			// They are prompted in the dialog to delete it manually when ready.
			Client.ForceDisconnect();
			SetFormLocked(false);

			if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
			{
				uiDialogBox.Open("Your account has been verified! You may now log in.\n\nThe 2FA setup file has been kept for your records. Please delete it manually when you no longer need it.");
			}
			if (UIManager.TryGet("UILogin", out UILogin uiLogin))
			{
				Hide();
				uiLogin.HandshakeMSG.text = "Account verified! You may now log in.";
				uiLogin.Show();
			}
		}

		/// <summary>
		/// Shows a dialog box for registration feedback and disconnects the client.
		/// </summary>
		/// <param name="message">The message to display.</param>
		private void OnRegistrationDialog(string message)
		{
			if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
			{
				uiDialogBox.Open(message);
			}
			pendingVerifyUsername = null;
			DeleteSavedTwoFactorSetupFile();
			Client.ForceDisconnect();
			SetFormLocked(false);
		}

		/// <summary>
		/// Deletes the on-disk copy of the 2FA setup payload, if any. Called from error,
		/// cancel, disconnect, and quit-to-login paths to scrub sensitive data. Success
		/// paths intentionally keep the file so the user has a persistent backup; the
		/// user is prompted to delete it manually when ready. Failures are swallowed
		/// because the file may not exist or may already have been cleaned.
		/// </summary>
		private void DeleteSavedTwoFactorSetupFile()
		{
			string path = savedTwoFactorSetupPath;
			savedTwoFactorSetupPath = null;
			if (string.IsNullOrEmpty(path)) return;
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch (System.Exception ex)
			{
				Log.Warning("UIRegister", $"Failed to delete 2FA setup file '{path}': {ex.Message}");
			}
		}

		/// <summary>
		/// Best-effort tightening of the 2FA setup file permissions so other local users
		/// cannot read it. On Unix-style platforms we shell out to <c>chmod 600</c>; on
		/// Windows we rely on the default per-user persistentDataPath ACL. All failures
		/// are non-fatal — the file is intentionally kept on success paths as a user backup.
		/// </summary>
		private static void TryRestrictTwoFactorSetupFilePermissions(string path)
		{
#if UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID || UNITY_EDITOR_LINUX || UNITY_EDITOR_OSX
			try
			{
				var psi = new System.Diagnostics.ProcessStartInfo("/bin/chmod", $"600 \"{path}\"")
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardError = true,
					RedirectStandardOutput = true,
				};
				using var p = System.Diagnostics.Process.Start(psi);
				p?.WaitForExit(500);
			}
			catch { /* best effort */ }
#endif
		}

		/// <summary>
		/// Called when the register button is clicked. Validates all fields, clears sensitive
		/// input immediately, then initiates the account creation flow.
		/// </summary>
		public void OnClick_Register()
		{
			string username = Username.text;
			string email = Email.text;
			string password = Password.text;
			// Reserved for future invite-key feature. Captured here so the field
			// can be cleared immediately (see ClearAllFields). Not yet wired to backend.
			string key = Key != null ? Key.text : string.Empty;
			// Map dropdown index to actual age: index 0 = not selected, index 1 = age 13, etc.
			int ageIndex = AgeSelect.value;
			int age = ageIndex > 0 ? ageIndex + 12 : 0;

			ClearAllFields();

			if (!Authentication.IsAllowedUsername(username))
			{
				ShowValidationError(Authentication.InvalidUsernameError);
				return;
			}

			if (!Authentication.IsAllowedPassword(password))
			{
				ShowValidationError(Authentication.InvalidPasswordError);
				return;
			}

			if (!Authentication.IsAllowedEmailUsername(email))
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

			StartCoroutine(Client.GetLoginServerList((error) =>
			{
				if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
				{
					uiDialogBox.Open(error);
				}
				Log.Error("UIRegister", error);
				SetFormLocked(false);
			},
			(servers, token) =>
			{
				if (!string.IsNullOrEmpty(token)) Client.LoginAuthenticator.ConnectionToken = token;
				pendingVerifyUsername = username;
				Connect(username, password, email, age);
			}));
		}

		/// <summary>
		/// Called when the quit-to-login button is clicked. Hides this panel and shows the login panel.
		/// </summary>
		public void OnClick_QuitToLogin()
		{
			ClearAllFields();
			Hide();

			if (UIManager.TryGet("UILogin", out UILogin uiLogin))
			{
				uiLogin.Show();
			}
		}

		/// <summary>
		/// Attempts to connect to the login server with registration credentials.
		/// </summary>
		/// <param name="username">The validated username.</param>
		/// <param name="password">The validated password.</param>
		/// <param name="email">The validated email address.</param>
		/// <param name="age">The selected age value.</param>
		private void Connect(string username, string password, string email, int age)
		{
			SetFormLocked(true);

			if (!Client.IsConnectionReady(LocalConnectionState.Stopped))
			{
				ShowValidationError("Connection already in progress. Please wait.");
				return;
			}

			if (!Client.TryGetRandomLoginServerPort(out ushort serverPort))
			{
				ShowValidationError("No login servers available. The LoginServer may not be registered yet — ensure it is running and connected to the database.");
				return;
			}

			StatusMessage.text = "Creating account...";

		}

		/// <summary>
		/// Displays a client-side validation error via dialog box.
		/// </summary>
		/// <param name="error">The validation error message.</param>
		private void ShowValidationError(string error)
		{
			if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
			{
				uiDialogBox.Open(error);
			}
			SetFormLocked(false);
		}

		/// <summary>
		/// Clears all input fields and resets the age dropdown to prevent sensitive data
		/// from lingering in the managed heap longer than necessary.
		/// </summary>
		private void ClearAllFields()
		{
			if (Username != null) Username.text = "";
			if (Email != null) Email.text = "";
			if (Password != null) Password.text = "";
			if (Key != null) Key.text = "";
			if (AgeSelect != null) AgeSelect.value = 0;
		}

		/// <summary>
		/// Sets the locked state of all form controls (enables/disables interactivity).
		/// Also manages the <see cref="isAuthFlowActive"/> flag: locking marks
		/// the start of a registration flow; unlocking marks its termination.
		/// </summary>
		/// <param name="locked">True to lock (disable) controls, false to unlock.</param>
		public void SetFormLocked(bool locked)
		{
			// Track auth-flow ownership: locking = start of flow, unlocking = end.
			if (locked) isAuthFlowActive = true;
			else isAuthFlowActive = false;

			if (RegisterButton != null) RegisterButton.interactable = !locked;
			if (QuitToLoginButton != null) QuitToLoginButton.interactable = !locked;
			if (Username != null) Username.enabled = !locked;
			if (Email != null) Email.enabled = !locked;
			if (Password != null) Password.enabled = !locked;
			if (Key != null) Key.enabled = !locked;
			if (AgeSelect != null) AgeSelect.interactable = !locked;
		}
	}
}