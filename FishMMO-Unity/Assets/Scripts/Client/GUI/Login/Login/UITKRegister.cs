using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Logging;
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
		/// The name of the key TextField in the UI.
		/// </summary>
		private const string KEY_NAME = "register-key";
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
		private TextField key;
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
			key = Root.Q<TextField>(KEY_NAME);
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
				if (statusMessage != null)
				{
					statusMessage.text = "";
				}
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
				case ClientAuthenticationResult.VersionMismatch:
					OnVersionMismatch();
					break;
				case ClientAuthenticationResult.TokenDecryptFailed:
					break;
			}
		}

		/// <summary>
		/// Handles successful account creation: hides the form and waits for the 2FA setup broadcast.
		/// </summary>
		private void OnAccountCreated()
		{
			SetFormLocked(true);
			Hide();
			if (statusMessage != null)
			{
				statusMessage.text = "Setting up two-factor authentication...";
			}
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

			// Save recovery codes and otpauth URI to disk immediately so the user has a
			// persistent copy. The file is best-effort and is scrubbed on every terminal
			// exit of the registration flow.
			string savePath = Path.Combine(Application.persistentDataPath, $"2fa_setup_{pendingVerifyUsername}.txt");
			try
			{
				string saveContent = $"OTPAuth URI:\n{otpauthUri}\n\nRecovery Codes:\n{string.Join("\n", recoveryCodes)}\n";
				File.WriteAllText(savePath, saveContent);
				TryRestrictTwoFactorSetupFilePermissions(savePath);
				savedTwoFactorSetupPath = savePath;
				Log.Info("UITKRegister", $"2FA setup data saved to: {savePath}");
			}
			catch (System.Exception ex)
			{
				Log.Warning("UITKRegister", $"Failed to save 2FA setup data: {ex.Message}");
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

			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiDialogBox))
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
						if (UIManager.TryGetTK("UILogin", out UITKLogin uiLogin))
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
			if (UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox uiDialogInputBox))
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
						if (UIManager.TryGetTK("UILogin", out UITKLogin uiLogin))
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

			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiDialogBox))
			{
				uiDialogBox.Open("Your account has been verified! You may now log in.");
			}
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
			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiDialogBox))
			{
				uiDialogBox.Open($"Game version mismatch.\n\nYour client is version {myVersion}.\nThe server expects a different version.\n\nPlease update your client to match the server.");
			}
			pendingVerifyUsername = null;
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
			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiDialogBox))
			{
				uiDialogBox.Open(message);
			}
			pendingVerifyUsername = null;
			DeleteSavedTwoFactorSetupFile();
			Client.ForceDisconnect();
			SetFormLocked(false);
		}

		/// <summary>
		/// Deletes the on-disk copy of the 2FA setup payload, if any.
		/// </summary>
		private void DeleteSavedTwoFactorSetupFile()
		{
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
		/// Best-effort tightening of the 2FA setup file permissions so other local users cannot read it.
		/// </summary>
		/// <param name="path">The file path to restrict.</param>
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
		/// Validates all fields, clears sensitive input immediately, then initiates account creation.
		/// </summary>
		public void OnClick_Register()
		{
			string usernameText = username != null ? username.value : null;
			string emailText = email != null ? email.value : null;
			string passwordText = password != null ? password.value : null;
			int ageIndex = ageSelect != null ? ageSelect.index : 0;
			// Map dropdown index to actual age: index 0 = not selected, index 1 = age 13, etc.
			int age = ageIndex > 0 ? ageIndex + 12 : 0;

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

			StartCoroutine(Client.GetLoginServerList((error) =>
			{
				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiDialogBox))
				{
					uiDialogBox.Open(error);
				}
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
		/// Hides this panel and shows the login panel.
		/// </summary>
		public void OnClick_QuitToLogin()
		{
			ClearAllFields();
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
				Client.LoginAuthenticator.SetLoginCredentials(usernameText, passwordText, true, emailText, age);
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
			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiDialogBox))
			{
				uiDialogBox.Open(error);
			}
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
			if (key != null)
			{
				key.value = "";
			}
			if (ageSelect != null)
			{
				ageSelect.index = 0;
			}
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

			if (registerButton != null)
			{
				registerButton.SetEnabled(!locked);
			}
			if (quitToLoginButton != null)
			{
				quitToLoginButton.SetEnabled(!locked);
			}
			if (username != null)
			{
				username.SetEnabled(!locked);
			}
			if (email != null)
			{
				email.SetEnabled(!locked);
			}
			if (password != null)
			{
				password.SetEnabled(!locked);
			}
			if (key != null)
			{
				key.SetEnabled(!locked);
			}
			if (ageSelect != null)
			{
				ageSelect.SetEnabled(!locked);
			}
		}
	}
}
