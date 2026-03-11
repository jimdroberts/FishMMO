using FishNet.Transporting;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using FishMMO.Logging;

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
		/// Called when the client is set. Subscribes to connection and authentication events.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
		}

		/// <summary>
		/// Called when the client is unset. Unsubscribes from connection and authentication events.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			Client.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
		}

		/// <summary>
		/// Called when quitting to login. Shows and unlocks the registration panel.
		/// </summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			pendingVerifyUsername = null;
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
				default:
					break;
			}
		}

		/// <summary>
		/// Handles successful account creation: stays connected and opens the verification code input dialog.
		/// </summary>
		private void OnAccountCreated()
		{
			SetFormLocked(true);
			Hide();

			if (UIManager.TryGet("UIDialogInputBox", out UIDialogInputBox uiDialogInputBox))
			{
				uiDialogInputBox.Open(
					"Your account has been created! Please enter the verification code sent to your email.",
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
			Client.ForceDisconnect();
			SetFormLocked(false);
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
			if (Client.IsConnectionReady(LocalConnectionState.Stopped) &&
				Client.TryGetRandomLoginServerAddress(out ServerAddress serverAddress) &&
				Authentication.IsAddressValid(serverAddress.Address))
			{
				StatusMessage.text = "Creating account...";
				Client.LoginAuthenticator.SetLoginCredentials(username, password, true, email, age);
				Client.ConnectToServer(serverAddress.Address, serverAddress.Port);
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