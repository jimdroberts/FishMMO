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
		/// Dropdown for age selection. The user must confirm they meet the minimum age requirement.
		/// </summary>
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
		/// Temporarily stores the username after account creation for verification code submission.
		/// Cleared after verification completes or the connection is lost.
		/// </summary>
		private string pendingVerifyUsername;

		/// <summary>
		/// Absolute path of the on-disk copy of the 2FA setup payload (OTPAuth URI + recovery
		/// codes) written during the registration flow. Tracked here so every terminal exit
		/// from the flow (success, cancel, disconnect, server error) can scrub the file. The
		/// payload is recoverable from server-side state until first-login is complete, so
		/// keeping it on disk past the registration session is pure liability.
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
		/// The verify code dialog is opened later by <see cref="OnTwoFactorSetupReceived"/>.
		/// </summary>
		private void OnAccountCreated()
		{
			SetFormLocked(true);
			Hide();
			StatusMessage.text = "Setting up two-factor authentication...";
		}

		/// <summary>
		/// Handles the 2FA setup data received from the server after account creation.
		/// Displays the otpauth URI and recovery codes, then opens the verify code dialog.
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
			// savedTwoFactorSetupPath so every terminal exit from this flow can
			// delete it — leaving the file behind would expose recovery codes to
			// any local process that can read the persistent data folder.
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
					? $"A temporary copy was written to:\n{savePath}\n(It will be deleted once registration completes.)\n\n"
					: "") +
				"Press Confirm to continue to email verification.";

			if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
			{
				uiDialogBox.Open(
					message,
					() =>
					{
						OpenVerifyCodeDialog();
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
		/// Opens the verification code input dialog for email verification.
		/// </summary>
		private void OpenVerifyCodeDialog()
		{
			if (UIManager.TryGet("UIDialogInputBox", out UIDialogInputBox uiDialogInputBox))
			{
				uiDialogInputBox.Open(
					"Please enter the verification code sent to your email.",
					(code) =>
					{
						if (!string.IsNullOrWhiteSpace(pendingVerifyUsername) && !string.IsNullOrWhiteSpace(code))
						{
							Client.LoginAuthenticator.SendVerifyCode(pendingVerifyUsername, code.Trim());
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
			DeleteSavedTwoFactorSetupFile();
			Client.ForceDisconnect();
			SetFormLocked(false);

			if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
			{
				uiDialogBox.Open("Your account has been verified! You may now log in.");
			}
			if (UIManager.TryGet("UILogin", out UILogin uiLogin))
			{
				Hide();
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
		/// Deletes the on-disk copy of the 2FA setup payload, if any. The file contains
		/// recovery codes and the TOTP seed (otpauth URI) for the account just created;
		/// it must not survive past the registration session. Called from every terminal
		/// exit of the flow: verification success, user-cancel from either dialog, server-
		/// reported failure, connection loss, and explicit quit-to-login. Failures are
		/// swallowed because the file may not exist or may already have been cleaned.
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
		/// are non-fatal — the file will still be deleted at the end of the flow.
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
			string key = Key.text;
			int ageIndex = AgeSelect.value;

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
			(servers) =>
			{
				pendingVerifyUsername = username;
				Connect(username, password, email, ageIndex);
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

			if (!Client.TryGetRandomLoginServerAddress(out ServerAddress serverAddress))
			{
				ShowValidationError("No login servers available. The LoginServer may not be registered yet — ensure it is running and connected to the database.");
				return;
			}

			if (!Authentication.IsAddressValid(serverAddress.Address))
			{
				ShowValidationError("Invalid server address. Please try again.");
				return;
			}

			StatusMessage.text = "Creating account...";
			Client.LoginAuthenticator.SetLoginCredentials(username, password, true, email, age);
			Client.ConnectToServer(serverAddress.Address, serverAddress.Port);
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
		/// </summary>
		/// <param name="locked">True to lock (disable) controls, false to unlock.</param>
		public void SetFormLocked(bool locked)
		{
			RegisterButton.interactable = !locked;
			QuitToLoginButton.interactable = !locked;
			Username.enabled = !locked;
			Email.enabled = !locked;
			Password.enabled = !locked;
			Key.enabled = !locked;
			AgeSelect.interactable = !locked;
		}
	}
}