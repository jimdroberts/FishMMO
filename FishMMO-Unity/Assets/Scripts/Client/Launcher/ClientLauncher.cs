using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
using TMPro;
using FishMMO.Shared;
using FishMMO.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace FishMMO.Client
{
	/// <summary>
	/// Orchestrates the client launcher's UI, news display, version checking, and patching process
	/// by delegating responsibilities to specialized services.
	/// </summary>
	public class ClientLauncher : MonoBehaviour
	{
		#region UI ELEMENTS
		[Header("UI Elements")]
		/// <summary>
		/// The background image of the launcher UI.
		/// </summary>
		[SerializeField]
		private Image background;
		/// <summary>
		/// The background image of the launcher UI.
		/// </summary>
		public Image Background => background;
		/// <summary>
		/// The title text of the launcher UI.
		/// </summary>
		[SerializeField]
		private TMP_Text title;
		/// <summary>
		/// The title text of the launcher UI.
		/// </summary>
		public TMP_Text Title => title;
		/// <summary>
		/// The HTML view GameObject for displaying news or other content.
		/// </summary>
		[SerializeField]
		private GameObject htmlView;
		/// <summary>
		/// The HTML view GameObject for displaying news or other content.
		/// </summary>
		public GameObject HTMLView => htmlView;
		/// <summary>
		/// The group GameObject containing the progress bar UI.
		/// </summary>
		[SerializeField]
		private GameObject progressBarGroup;
		/// <summary>
		/// The group GameObject containing the progress bar UI.
		/// </summary>
		public GameObject ProgressBarGroup => progressBarGroup;
		/// <summary>
		/// The progress slider UI element.
		/// </summary>
		[SerializeField]
		private Slider progressSlider;
		/// <summary>
		/// The progress slider UI element.
		/// </summary>
		public Slider ProgressSlider => progressSlider;
		/// <summary>
		/// The progress text UI element. Also used as a status/error message display
		/// so the player can see what went wrong, not just the button label.
		/// </summary>
		[SerializeField]
		private TMP_Text progressText;
		/// <summary>
		/// The progress text UI element.
		/// </summary>
		public TMP_Text ProgressText => progressText;
		/// <summary>
		/// The quit button UI element.
		/// </summary>
		[SerializeField]
		private Button quitButton;
		/// <summary>
		/// The quit button UI element.
		/// </summary>
		public Button QuitButton => quitButton;
		/// <summary>
		/// The play button UI element.
		/// </summary>
		[SerializeField]
		private Button playButton;
		/// <summary>
		/// The play button UI element.
		/// </summary>
		public Button PlayButton => playButton;
		/// <summary>
		/// The play button text UI element.
		/// </summary>
		[SerializeField]
		private TMP_Text playButtonText;
		/// <summary>
		/// The play button text UI element.
		/// </summary>
		public TMP_Text PlayButtonText => playButtonText;
		/// <summary>
		/// The text UI element for displaying HTML content.
		/// </summary>
		[SerializeField]
		private TMP_Text htmlText;
		/// <summary>
		/// The text UI element for displaying HTML content.
		/// </summary>
		public TMP_Text HtmlText => htmlText;
		/// <summary>
		/// The handler for clickable links in the HTML text.
		/// </summary>
		[SerializeField]
		private TMPro_TextLinkHandler htmlTextLinkHandler;
		/// <summary>
		/// The handler for clickable links in the HTML text.
		/// </summary>
		public TMPro_TextLinkHandler HtmlTextLinkHandler => htmlTextLinkHandler;
		/// <summary>
		/// Optional status text element for displaying error/status messages to the user.
		/// When assigned, the launcher writes human-readable feedback here so players
		/// can see what went wrong without needing access to the log file.
		/// When null, the ProgressText element is used as a fallback.
		/// </summary>
		[SerializeField]
		private TMP_Text statusText;
		/// <summary>
		/// Optional status text element for displaying error/status messages to the user.
		/// </summary>
		public TMP_Text StatusText => statusText;
		#endregion

		#region CONFIGURATION
		[Header("Configuration")]
		/// <summary>
		/// The URL to fetch HTML news content from.
		/// </summary>
		[SerializeField]
		private string htmlViewURL = GeneratedHostConfig.LauncherHtmlUrl;
		/// <summary>
		/// The URL to fetch HTML news content from.
		/// </summary>
		public string HtmlViewURL => htmlViewURL;
		/// <summary>
		/// The class name of the div to extract from the HTML content.
		/// </summary>
		[SerializeField]
		private string divClass = "content";
		/// <summary>
		/// The class name of the div to extract from the HTML content.
		/// </summary>
		public string DivClass => divClass;
		/// <summary>
		/// The default screen width for the launcher window.
		/// </summary>
		[SerializeField]
		private int defaultScreenWidth = 1024;
		/// <summary>
		/// The default screen width for the launcher window.
		/// </summary>
		public int DefaultScreenWidth => defaultScreenWidth;
		/// <summary>
		/// The default screen height for the launcher window.
		/// </summary>
		[SerializeField]
		private int defaultScreenHeight = 768;
		/// <summary>
		/// The default screen height for the launcher window.
		/// </summary>
		public int DefaultScreenHeight => defaultScreenHeight;
		/// <summary>
		/// Timeout in seconds for the addressable scene load watchdog.
		/// If the scene load takes longer than this, the Play button is re-enabled so the player can retry.
		/// Can be overridden via configuration (e.g., Constants or a settings file) if a longer or shorter
		/// timeout is needed for specific deployment scenarios (modded clients, slow patch servers, etc.).
		/// </summary>
		[Tooltip("Timeout in seconds for the addressable scene load watchdog. Override via Constants or config for deployment-specific needs.")]
		[SerializeField]
		private float launchWatchdogTimeoutSeconds = 30f;
		#endregion

		#region DEPENDENCIES (Injected via Inspector)
		[Header("Dependencies")]
		/// <summary>
		/// Service for handling Unity web requests.
		/// </summary>
		[SerializeField]
		private UnityWebRequestService unityWebRequestService;
		/// <summary>
		/// Service for handling Unity web requests.
		/// </summary>
		public UnityWebRequestService UnityWebRequestService => unityWebRequestService;
		/// <summary>
		/// Service for fetching and processing HTML content.
		/// </summary>
		[SerializeField]
		private UnityHtmlContentFetcher htmlContentFetcher;
		/// <summary>
		/// Service for fetching and processing HTML content.
		/// </summary>
		public UnityHtmlContentFetcher HtmlContentFetcher => htmlContentFetcher;
		/// <summary>
		/// Service for patch server communication and patch management.
		/// </summary>
		[SerializeField]
		private HttpPatchServerService patchServerService;
		/// <summary>
		/// Service for patch server communication and patch management.
		/// </summary>
		public HttpPatchServerService PatchServerService => patchServerService;
		/// <summary>
		/// The updater launcher used to start the external updater process.
		/// </summary>
		private IUpdaterLauncher updaterLauncher;
		#endregion

		#region INTERNAL STATE
		/// <summary>
		/// Guards against re-entering PlayButtonConnect while a connection is in progress.
		/// </summary>
		private bool isConnecting = false;
		/// <summary>
		/// Guards against re-entering PlayButtonLaunch while a launch is in progress.
		/// </summary>
		private bool isLaunching = false;
		/// <summary>
		/// Stores the latest client version string fetched from the patch server.
		/// </summary>
		private string latestVersionString;
		/// <summary>
		/// Expected SHA-256 (lowercase hex) of the patch zip for the current client
		/// version, as reported by the patch server. Empty when the server did not
		/// supply one; in that case the download is not integrity-checked.
		/// </summary>
		private string expectedPatchSha256;
		/// <summary>
		/// Base URL of the APIHost candidate that responded successfully during the
		/// most recent version check. Used so that the subsequent patch download
		/// targets the same endpoint (instead of re-randomizing and potentially
		/// hitting a different mirror with a different patch).
		/// </summary>
		private string selectedApiHost;
		/// <summary>
		/// Full path to the external updater executable.
		/// </summary>
		private string updaterPath;

		/// <summary>
		/// The current state of the launcher UI and process.
		/// </summary>
		private LauncherState currentLauncherState;
		#endregion

		#region UI TEXT CONSTANTS
		/// <summary>
		/// Contains constant strings for UI text and log messages.
		/// </summary>
		private static class UIText
		{
			public const string ButtonConnect = "Connect";
			public const string ButtonPlay = "Play";
			public const string ButtonUpdate = "Update";
			public const string StatusLoadingNews = "Loading News...";
			public const string StatusConnecting = "Connecting...";
			public const string StatusCheckingVersion = "Checking Version...";
			public const string StatusDownloadingPatch = "Downloading Patch...";
			public const string StatusApplyingPatch = "Applying Patch...";
			public const string StatusConnectionFailed = "Connection Failed";
			public const string StatusVersionCheckFailed = "Version Check Failed";
			public const string StatusPatchDownloadFailed = "Patch Download Failed";
			public const string StatusUpdaterFailed = "Updater Failed";
			public const string StatusLaunchFailed = "Launch Failed";
			public const string StatusVersionError = "Version Error";
			public const string StatusClientAhead = "Client Version Ahead";
				public const string StatusServerRejectedVersion = "Version Rejected by Server";

			public const string ErrorLoadingNews = "Error loading news: ";
			public const string ErrorNoNewsContent = "Could not display news content.";
			public const string ErrorParsingVersion = "Invalid version format: {0}. Expected Major.Minor.Patch[.PreRelease].";

			public const string LogErrorFetchHtml = "Error fetching HTML from {0}: {1}";
			public const string LogErrorExtractHtml = "Failed to extract text from div '{0}' in HTML from {1}.";
			public const string LogErrorLatestVersion = "Error fetching latest version: {0}";
			public const string LogErrorDownloadingPatch = "Error downloading patch: {0}";
			public const string LogErrorUpdaterStart = "Failed to start the updater process.";
			public const string LogErrorUpdaterExit = "Updater process exited with code {0}.";

			public const string LogDebugPatchDownloaded = "Patch downloaded and saved to {0}";
			public const string LogDebugPatchNotRequired = "Patch not required. Server reports client is already updated to {0}.";
			public const string LogDebugLatestServerVersion = "Latest server version: {0}";
			public const string LogDebugClientVersionAhead = "Client version {0} is ahead of server version {1}.";
		}
		#endregion

		#region UNITY LIFECYCLE METHODS
		/// <summary>
		/// Unity Awake method. Initializes dependencies, sets up UI, and starts HTML/news fetch.
		/// </summary>
		private void Awake()
		{
			// SystemUpdaterLauncher is a plain C# class, directly instantiate it
			this.updaterLauncher = new SystemUpdaterLauncher();

			// Basic null checks for dependencies
			if (this.unityWebRequestService == null || this.htmlContentFetcher == null || this.patchServerService == null)
			{
				Log.Error("ClientLauncher", "One or more required service dependencies are not assigned in the Inspector or are missing!");
				if (this.PlayButtonText != null)
				{
					this.PlayButtonText.text = "Fatal Error";
				}
				if (this.PlayButton != null)
				{
					this.PlayButton.interactable = false;
				}
				enabled = false; // Disable this script if dependencies aren't met
				return;
			}
			// Ensure UnityWebRequestServiceMB is correctly assigned within its own script's Awake/Start
			// or confirm it's not null here. For example, within UnityHtmlContentFetcher and HttpPatchServerService,
			// they will check if their WebRequestService field is assigned.

			if (this.HtmlTextLinkHandler != null)
			{
				this.HtmlTextLinkHandler.OnLinkClicked += HandleHtmlLinkClicked;
			}

			SetLauncherState(LauncherState.LoadingNews);
			// Delegate HTML fetching to the dedicated service
			StartCoroutine(this.htmlContentFetcher.FetchAndProcessHtml(
				this.HtmlViewURL,
				this.DivClass,
				onHtmlReady: (htmlContent) =>
				{
					this.HtmlText.text = htmlContent;
#if !UNITY_EDITOR
					PlayButtonConnect();
#else
					SetLauncherState(LauncherState.ReadyToPlay);
#endif
				},
				onError: (error) =>
				{
					SetLauncherState(LauncherState.ConnectionFailed, error);
				}));

			// Construct the full path to the updater executable
			this.updaterPath = Path.Combine(Constants.GetWorkingDirectory(), Constants.Configuration.UpdaterExecutable);

#if !UNITY_EDITOR
			// Set screen resolution for non-editor builds
			try { Screen.SetResolution(this.DefaultScreenWidth, this.DefaultScreenHeight, FullScreenMode.Windowed, Screen.currentResolution.refreshRateRatio); }
			catch { Screen.SetResolution(this.DefaultScreenWidth, this.DefaultScreenHeight, FullScreenMode.Windowed, new RefreshRate() { numerator = 60, denominator = 1 }); }
#endif
			string versionString = !string.IsNullOrEmpty(MainBootstrapSystem.GameVersion)
				? MainBootstrapSystem.GameVersion
				: "0.0.0-unknown";
			this.Title.text = $"{Constants.Configuration.ProjectName} v{versionString}";
			this.ProgressBarGroup.SetActive(false); // Ensure progress bar is hidden initially.
		}

		/// <summary>
		/// Unity OnDestroy method. Cleans up event subscriptions.
		/// </summary>
		private void OnDestroy()
		{
			if (this.HtmlTextLinkHandler != null)
			{
				this.HtmlTextLinkHandler.OnLinkClicked -= HandleHtmlLinkClicked;
			}
		}
		#endregion

		#region UI STATE MANAGEMENT
		/// <summary>
		/// Writes a human-readable status or error message to the player-facing UI.
		/// Prefers <see cref="StatusText"/> when assigned; falls back to
		/// <see cref="ProgressText"/> so messages are visible even without a
		/// dedicated status element. Also logs the message.
		/// </summary>
		private void SetStatus(string message, LogLevel level = LogLevel.Info)
		{
			// Always log so operators can correlate.
			switch (level)
			{
				case LogLevel.Warning:
					Log.Warning("ClientLauncher", message);
					break;
				case LogLevel.Error:
				case LogLevel.Critical:
					Log.Error("ClientLauncher", message);
					break;
				default:
					Log.Info("ClientLauncher", message);
					break;
			}

			// Player-facing display.
			TMP_Text target = this.StatusText != null ? this.StatusText : this.ProgressText;
			if (target != null)
			{
				target.text = message;
				// Only show the progress bar group for actual progress updates.
				// Status messages use ProgressText as a convenience label.
			}
		}

		/// <summary>
		/// Clears the status text so stale error messages don't linger.
		/// </summary>
		private void ClearStatus()
		{
			TMP_Text target = this.StatusText != null ? this.StatusText : this.ProgressText;
			if (target != null)
			{
				target.text = string.Empty;
			}
		}

		/// <summary>
		/// Sets the current launcher state and updates the UI accordingly.
		/// Centralizes all UI updates related to the launcher's operational state.
		/// </summary>
		/// <param name="newState">The new state for the launcher.</param>
		/// <param name="errorDetail">Optional human-readable error detail displayed to the player.</param>
		private void SetLauncherState(LauncherState newState, string errorDetail = null)
		{
			this.currentLauncherState = newState;
			this.PlayButton.onClick.RemoveAllListeners(); // Always clear to ensure only one listener.

			bool isButtonInteractable = false;
			string buttonText = "";
			Action buttonAction = null;
			bool progressBarVisible = false;

			switch (this.currentLauncherState)
			{
				case LauncherState.LoadingNews:
					buttonText = UIText.StatusLoadingNews;
					break;
				case LauncherState.Connecting:
					buttonText = UIText.StatusConnecting;
					buttonAction = PlayButtonConnect;
					break;
				case LauncherState.CheckingVersion:
					buttonText = UIText.StatusCheckingVersion;
					break;
				case LauncherState.DownloadingPatch:
					buttonText = UIText.StatusDownloadingPatch;
					progressBarVisible = true;
					break;
				case LauncherState.ApplyingPatch:
					buttonText = UIText.StatusApplyingPatch;
					break;
				case LauncherState.ReadyToPlay:
					buttonText = UIText.ButtonPlay;
					isButtonInteractable = true;
					buttonAction = PlayButtonLaunch;
					break;
				case LauncherState.ClientAhead:
					buttonText = UIText.StatusClientAhead;
					isButtonInteractable = true;
					buttonAction = PlayButtonConnect; // Re-check version — don't allow Play when client is ahead.
					break;
				case LauncherState.ServerRejectedVersion:
					// NOTE: ServerRejectedVersion state is defined here and wired into
					// the UI state machine for future use. The server-side authentication
					// protocol currently handles version rejection during the handshake
					// (returning ClientAuthenticationResult.VersionMismatch), which is
					// rendered through a separate UI path. In a future version, the
					// version-check endpoint may explicitly reject a client version
					// without offering a patch, at which point this state should be
					// activated with a user-facing message explaining that their
					// client version is not supported.
					buttonText = UIText.StatusServerRejectedVersion;
					isButtonInteractable = true;
					buttonAction = PlayButtonConnect; // Re-check version.
					break;
				case LauncherState.ConnectionFailed:
					buttonText = UIText.StatusConnectionFailed;
					isButtonInteractable = true;
					buttonAction = PlayButtonConnect; // Allow retry.
					break;
				case LauncherState.VersionCheckFailed:
					buttonText = UIText.StatusVersionCheckFailed;
					isButtonInteractable = true;
					buttonAction = PlayButtonConnect; // Allow retry.
					break;
				case LauncherState.PatchDownloadFailed:
					buttonText = UIText.StatusPatchDownloadFailed;
					isButtonInteractable = true;
					buttonAction = PlayButtonUpdate; // Allow retry.
					break;
				case LauncherState.UpdaterFailed:
					buttonText = UIText.StatusUpdaterFailed;
					isButtonInteractable = true;
					buttonAction = PlayButtonConnect; // Default to connect for retry.
					break;
				case LauncherState.LaunchFailed:
					buttonText = UIText.StatusLaunchFailed;
					isButtonInteractable = true;
					// Allow the player to go back to the version-check/connect flow
					// instead of retrying the same failing launch path. A failing
					// scene load is often recoverable by re-downloading the patch.
					buttonAction = PlayButtonConnect;
					break;
				case LauncherState.VersionError:
					buttonText = UIText.StatusVersionError;
					isButtonInteractable = true;
					// Previously unrecoverable — now allows retry. Version parse
					// failures can be transient (e.g. malformed version.txt after
					// a partial patch). Let the player retry the version check.
					buttonAction = PlayButtonConnect;
					break;
				default: // Fallback
					buttonText = UIText.ButtonConnect;
					isButtonInteractable = true;
					buttonAction = PlayButtonConnect;
					break;
			}

			this.PlayButtonText.text = buttonText;
			this.PlayButton.interactable = isButtonInteractable;
			this.ProgressBarGroup.SetActive(progressBarVisible);

			// Display error/status detail to the player when available.
			if (!string.IsNullOrEmpty(errorDetail))
			{
				SetStatus(errorDetail, level: IsErrorState(newState) ? LogLevel.Error : LogLevel.Info);
			}
			else if (!IsErrorState(newState))
			{
				ClearStatus();
			}

			if (buttonAction != null)
			{
				this.PlayButton.onClick.AddListener(() => buttonAction.Invoke());
			}
		}

		/// <summary>
		/// Returns true for launcher states that represent a failure the player needs to act on.
		/// </summary>
		private static bool IsErrorState(LauncherState state)
		{
			return state == LauncherState.ConnectionFailed ||
			       state == LauncherState.VersionCheckFailed ||
			       state == LauncherState.PatchDownloadFailed ||
			       state == LauncherState.UpdaterFailed ||
			       state == LauncherState.LaunchFailed ||
			       state == LauncherState.VersionError;
		}
		#endregion

		#region UI INTERACTION HANDLERS (Delegate actions to services)
		/// <summary>
		/// Handles clicks on links embedded within the HTML news text.
		/// Opens external URLs in the default web browser.
		/// </summary>
		/// <param name="link">The URL string extracted from the clicked link.</param>
		private void HandleHtmlLinkClicked(string link)
		{
			// Strict URI parsing + scheme allowlist. The previous substring check would
			// happily open "javascript:...", "file://...", or anything containing the
			// literal string "http" (e.g. "chrome-http-pwn://...") through the OS URL
			// handler. Restrict to absolute http(s) URIs only.
			if (string.IsNullOrWhiteSpace(link))
			{
				return;
			}
			if (!Uri.TryCreate(link, UriKind.Absolute, out Uri uri))
			{
				Log.Warning("ClientLauncher", $"Refusing to open non-absolute link: {link}");
				return;
			}
			if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
			{
				Log.Warning("ClientLauncher", $"Refusing to open link with disallowed scheme '{uri.Scheme}': {link}");
				return;
			}
			Application.OpenURL(uri.AbsoluteUri);
		}

		/// <summary>
		/// Initiates the connection process to check for game updates.
		/// All requests go through the unified API gateway (Constants.Configuration.APIHost).
		/// </summary>
		public void PlayButtonConnect()
		{
			if (this.isConnecting) return;
			this.isConnecting = true;
			SetLauncherState(LauncherState.Connecting);
			StartCoroutine(GetLatestVersion());
		}

		/// <summary>
		/// Launches the client after all version checks and updates are complete.
		/// </summary>
		public void PlayButtonLaunch()
		{
			if (this.isLaunching) return;
			try
			{
				this.isLaunching = true;
				SetLauncherState(LauncherState.ReadyToPlay);
				if (this.PlayButton != null)
				{
					this.PlayButton.interactable = false;
				}

				AddressableLoadProcessor.EnqueueLoad(new AddressableSceneLoadData("ClientPostboot", OnPostbootSceneLoaded));
				try
				{
					AddressableLoadProcessor.BeginProcessQueue();
				}
				catch (UnityException ex)
				{
					this.isLaunching = false;
					SetLauncherState(LauncherState.LaunchFailed,
						$"Failed to load game scene: {ex.Message}. Check that Addressable bundles are built.");
					return;
				}
			}
			catch (Exception ex)
			{
				this.isLaunching = false;
				Log.Error("ClientLauncher", $"Unexpected error in PlayButtonLaunch: {ex}");
				SetLauncherState(LauncherState.LaunchFailed,
					$"Unexpected error: {ex.Message}");
				return;
			}

			// Start a watchdog — BeginProcessQueue() starts async work and returns immediately.
			// If the Addressable scene load fails asynchronously (no synchronous throw),
			// OnPostbootSceneLoaded never fires and the button stays permanently disabled.
			// The watchdog re-enables the button after a generous timeout so the player can retry.
			StartCoroutine(LaunchWatchdog());
		}

		/// <summary>
		/// Watchdog coroutine that re-enables the Play button if the scene load takes too long.
		/// On success, OnPostbootSceneLoaded unloads the launcher scene, destroying this
		/// MonoBehaviour and stopping the coroutine automatically.
		/// </summary>
		private System.Collections.IEnumerator LaunchWatchdog()
		{
			yield return new WaitForSeconds(this.launchWatchdogTimeoutSeconds);
			/* Watchdog reset: isLaunching is set back to false on timeout so that
			 * the Play button is re-enabled and the player can retry.  In the normal
			 * (success) path, OnPostbootSceneLoaded unloads the launcher scene, which
			 * destroys this MonoBehaviour and stops the coroutine automatically —
			 * isLaunching is never explicitly set back to false on success; the
			 * GameObject simply goes away.  This asymmetry (timeout path resets,
			 * success path destroys) is intentional and expected.
			 *
			 * NOTE: isLaunching is reset but isConnecting is not. The next
			 * PlayButtonConnect() call checks isConnecting which should already
			 * be false from the connection failure path.
			 */
			this.isLaunching = false;
			SetLauncherState(LauncherState.LaunchFailed,
				"Scene load timed out. Check that Addressable bundles are built and up to date.");
		}

		/// <summary>
		/// Callback when the ClientPostboot scene is loaded.
		/// </summary>
		/// <param name="scene">The loaded scene.</param>
		private void OnPostbootSceneLoaded(UnityEngine.SceneManagement.Scene scene)
		{
			Log.Debug("ClientLauncher", "ClientPostboot scene loaded.");
			AddressableLoadProcessor.UnloadSceneByLabelAsync("ClientLauncher");

			// Find the ClientPostbootSystem in the loaded scene and start its bootstrap process.
			foreach (var rootGO in scene.GetRootGameObjects())
			{
				rootGO.GetComponent<ClientPostbootSystem>()?.StartBootstrap();
			}
		}

		/// <summary>
		/// Initiates the update process by attempting to download and apply the patch.
		/// </summary>
		public void PlayButtonUpdate()
		{
			SetLauncherState(LauncherState.DownloadingPatch);

			string tempFilePath = Constants.GetTemporaryPath();

			// Use the APIHost that succeeded during the version check so the patch we
			// download corresponds to the version we were told about. Fall back to the
			// hard-coded default only if no candidate has been selected yet (shouldn't
			// happen in normal flow because Update is gated by a successful version check).
			string patchApiHost = !string.IsNullOrEmpty(this.selectedApiHost)
				? this.selectedApiHost
				: Constants.Configuration.APIHost;

			// Delegate patch download to patch server service
			StartCoroutine(this.patchServerService.DownloadPatch(
				$"{patchApiHost}{MainBootstrapSystem.GameVersion}",
				tempFilePath,
				this.expectedPatchSha256,
				onComplete: () =>
				{
					SetLauncherState(LauncherState.ApplyingPatch, "Applying patch to game files. This may take a few minutes.");
					// Delegate updater launch to updater launcher service
					StartCoroutine(this.updaterLauncher.LaunchUpdater(
						this.updaterPath,
						MainBootstrapSystem.GameVersion,
						this.latestVersionString,
						onComplete: () => {
						// Clean up temp patch file after successful update.
						if (File.Exists(tempFilePath))
						{
							try { File.Delete(tempFilePath); }
							catch (Exception ex) { Log.Error("ClientLauncher", $"Failed to delete temp patch file {tempFilePath}: {ex.Message}"); }
						}
						Quit(); // Updater completed, quit launcher
					},
						onError: (error) =>
						{
							SetLauncherState(LauncherState.UpdaterFailed, error);
						}));
				},
				onError: (error) =>
				{
					SetLauncherState(LauncherState.PatchDownloadFailed, error);

					// Attempt to clean up any partially downloaded file if an error occurs.
					if (File.Exists(tempFilePath))
					{
						try { File.Delete(tempFilePath); }
						catch (Exception ex) { Log.Error("ClientLauncher", $"Failed to delete temp patch file {tempFilePath}: {ex.Message}"); }
					}
				},
				onProgress: (progress, progressString) =>
				{
					this.ProgressSlider.value = progress;
					this.ProgressText.text = progressString;
				}));
		}

		/// <summary>
		/// Fetches the latest client version from the currently selected patch server.
		/// Compares it with the current client version and updates the UI accordingly.
		/// </summary>
		private IEnumerator GetLatestVersion()
		{
			try
			{
				SetLauncherState(LauncherState.CheckingVersion);
				this.isConnecting = true;

				List<string> candidates = ApiHostResolver.GetCandidates();
				if (candidates.Count == 0)
				{
					SetLauncherState(LauncherState.VersionCheckFailed, "No API host configured. Check Constants.cs Configuration.APIHost.");
					yield break;
				}

				string lastError = null;
				VersionConfig serverVersion = default;
				PatchInfo patchInfo = default;
				bool succeeded = false;
				string successfulHost = null;

				for (int i = 0; i < candidates.Count && !succeeded; i++)
				{
					string host = candidates[i];
					bool callbackFired = false;
					string attemptError = null;

					yield return StartCoroutine(this.patchServerService.GetLatestVersion(
						host,
						MainBootstrapSystem.GameVersion,
						onComplete: (sv, info) =>
						{
							callbackFired = true;
							serverVersion = sv;
							patchInfo = info;
							successfulHost = host;
							succeeded = true;
						},
						onError: (error) =>
						{
							callbackFired = true;
							attemptError = error;
						}));

					if (!succeeded)
					{
						lastError = attemptError ?? (callbackFired ? "Unknown error" : "No response");
						Log.Debug("ClientLauncher", $"APIHost {host} version check failed ({lastError}); trying next.");
					}
				}

				if (!succeeded)
				{
					SetLauncherState(LauncherState.VersionCheckFailed,
						lastError ?? "All API hosts failed. Check your internet connection and firewall.");
					yield break;
				}

				this.selectedApiHost = successfulHost;
				this.latestVersionString = serverVersion.ToString(); // Store for updater launch
				this.expectedPatchSha256 = patchInfo.Sha256; // May be null/empty when not provided.
				Log.Debug("ClientLauncher", string.Format(UIText.LogDebugLatestServerVersion, latestVersionString));

				VersionConfig clientVersion;
				try
				{
					clientVersion = VersionConfig.Parse(MainBootstrapSystem.GameVersion);
				}
				catch (ArgumentException)
				{
					SetLauncherState(LauncherState.VersionError,
						$"Client version '{MainBootstrapSystem.GameVersion}' is not valid. Reinstall or delete version.txt.");
					yield break;
				}

				// Compare client and server versions to determine the appropriate action.
				if (clientVersion < serverVersion)
				{
#if UNITY_WEBGL && !UNITY_EDITOR
					// WebGL builds are always deployed at the server-expected version;
					// there is no patch/updater mechanism in the browser sandbox.
					// If this code path executes, the deployed WebGL build is outdated.
					SetLauncherState(LauncherState.VersionError,
						"Your browser client is outdated. Please refresh the page (Ctrl+F5) to get the latest version.");
					yield break;
#else
					PlayButtonUpdate();
#endif
				}
				else if (clientVersion > serverVersion)
				{
					Log.Warning("ClientLauncher", string.Format(UIText.LogDebugClientVersionAhead, MainBootstrapSystem.GameVersion, latestVersionString));
					SetLauncherState(LauncherState.ClientAhead);
				}
				else
				{
					SetLauncherState(LauncherState.ReadyToPlay);
				}
			}
			finally
			{
				this.isConnecting = false;
			}
		}

		/// <summary>
		/// Quits the application.
		/// </summary>
		public void Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#elif UNITY_WEBGL && !UNITY_EDITOR
			// Application.Quit() is a no-op in WebGL.
			// Application.ExternalEval was removed in Unity 2022+; use Application.Quit which
			// is intentionally a no-op in WebGL (the browser tab stays open).
			Application.Quit();
			Log.Info("ClientLauncher", "WebGL quit requested.");
#else
			Application.Quit();
#endif
		}
		#endregion
	}
}