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
		public Image Background;
		/// <summary>
		/// The title text of the launcher UI.
		/// </summary>
		public TMP_Text Title;
		/// <summary>
		/// The HTML view GameObject for displaying news or other content.
		/// </summary>
		public GameObject HTMLView;
		/// <summary>
		/// The group GameObject containing the progress bar UI.
		/// </summary>
		public GameObject ProgressBarGroup;
		/// <summary>
		/// The progress slider UI element.
		/// </summary>
		public Slider ProgressSlider;
		/// <summary>
		/// The progress text UI element.
		/// </summary>
		public TMP_Text ProgressText;
		/// <summary>
		/// The quit button UI element.
		/// </summary>
		public Button QuitButton;
		/// <summary>
		/// The play button UI element.
		/// </summary>
		public Button PlayButton;
		/// <summary>
		/// The play button text UI element.
		/// </summary>
		public TMP_Text PlayButtonText;
		/// <summary>
		/// The text UI element for displaying HTML content.
		/// </summary>
		public TMP_Text HtmlText;
		/// <summary>
		/// The handler for clickable links in the HTML text.
		/// </summary>
		public TMPro_TextLinkHandler HtmlTextLinkHandler;
		#endregion

		#region CONFIGURATION
		[Header("Configuration")]
		/// <summary>
		/// The URL to fetch HTML news content from.
		/// </summary>
		public string HtmlViewURL = "https://www.fishmmo.com/docs/introduction.html";
		/// <summary>
		/// The class name of the div to extract from the HTML content.
		/// </summary>
		public string DivClass = "content";
		/// <summary>
		/// The default screen width for the launcher window.
		/// </summary>
		public int DefaultScreenWidth = 1024;
		/// <summary>
		/// The default screen height for the launcher window.
		/// </summary>
		public int DefaultScreenHeight = 768;
		#endregion

		#region DEPENDENCIES (Injected via Inspector)
		[Header("Dependencies")]
		/// <summary>
		/// Service for handling Unity web requests.
		/// </summary>
		public UnityWebRequestService UnityWebRequestService;
		/// <summary>
		/// Service for fetching and processing HTML content.
		/// </summary>
		public UnityHtmlContentFetcher HtmlContentFetcher;
		/// <summary>
		/// Service for patch server communication and patch management.
		/// </summary>
		public HttpPatchServerService PatchServerService;
		/// <summary>
		/// The updater launcher used to start the external updater process.
		/// </summary>
		private IUpdaterLauncher updaterLauncher;
		#endregion

		#region INTERNAL STATE
		/// <summary>
		/// The patch server host URL.
		/// </summary>
		private string patcherHost;
		/// <summary>
		/// Stores the latest client version string fetched from the patch server.
		/// </summary>
		private string latestVersionString;
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

			public const string ErrorLoadingNews = "Error loading news: ";
			public const string ErrorNoNewsContent = "Could not display news content.";
			public const string ErrorParsingVersion = "Invalid version format: {0}. Expected Major.Minor.Patch[.PreRelease].";

			public const string LogErrorFetchHtml = "Error fetching HTML from {0}: {1}";
			public const string LogErrorExtractHtml = "Failed to extract text from div '{0}' in HTML from {1}.";
			public const string LogErrorPatchServerList = "Error fetching patch server list: {0}";
			public const string LogErrorLatestVersion = "Error fetching latest version: {0}";
			public const string LogErrorDownloadingPatch = "Error downloading patch: {0}";
			public const string LogErrorUpdaterStart = "Failed to start the updater process.";
			public const string LogErrorUpdaterExit = "Updater process exited with code {0}.";

			public const string LogDebugPatchDownloaded = "Patch downloaded and saved to {0}";
			public const string LogDebugPatchNotRequired = "Patch not required. Server reports client is already updated to {0}.";
			public const string LogDebugNewPatchServer = "New Patch Server Address: {0}, Port: {1}";
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
			updaterLauncher = new SystemUpdaterLauncher();

			// Basic null checks for dependencies
			if (UnityWebRequestService == null || HtmlContentFetcher == null || PatchServerService == null || updaterLauncher == null)
			{
				Log.Error("ClientLauncher", "One or more required service dependencies are not assigned in the Inspector or are missing!");
				PlayButtonText.text = "Fatal Error";
				PlayButton.interactable = false;
				enabled = false; // Disable this script if dependencies aren't met
				return;
			}
			// Ensure UnityWebRequestServiceMB is correctly assigned within its own script's Awake/Start
			// or confirm it's not null here. For example, within UnityHtmlContentFetcher and HttpPatchServerService,
			// they will check if their WebRequestService field is assigned.

			if (HtmlTextLinkHandler != null)
			{
				HtmlTextLinkHandler.OnLinkClicked += HandleHtmlLinkClicked;
			}

			SetLauncherState(LauncherState.LoadingNews);
			// Delegate HTML fetching to the dedicated service
			StartCoroutine(HtmlContentFetcher.FetchAndProcessHtml(
				HtmlViewURL,
				DivClass,
				onHtmlReady: (htmlContent) =>
				{
					HtmlText.text = htmlContent;
#if !UNITY_EDITOR
					SetLauncherState(LauncherState.Connecting);
#else
					SetLauncherState(LauncherState.ReadyToPlay);
#endif
				},
				onError: (error) =>
				{
					Log.Error("ClientLauncher", error);
					HtmlText.text = $"<color=red>{error}</color>";
					SetLauncherState(LauncherState.ConnectionFailed);
				}));

			// Construct the full path to the updater executable
			updaterPath = Path.Combine(Constants.GetWorkingDirectory(), Constants.Configuration.UpdaterExecutable);

#if !UNITY_EDITOR
			// Set screen resolution for non-editor builds
			Screen.SetResolution(DefaultScreenWidth, DefaultScreenHeight, FullScreenMode.Windowed, new RefreshRate()
			{
				numerator = 60,
				denominator = 1,
			});
#endif
			Title.text = $"{Constants.Configuration.ProjectName} v{MainBootstrapSystem.GameVersion}";
			ProgressBarGroup.SetActive(false); // Ensure progress bar is hidden initially.
		}

		/// <summary>
		/// Unity OnDestroy method. Cleans up event subscriptions.
		/// </summary>
		private void OnDestroy()
		{
			if (HtmlTextLinkHandler != null)
			{
				HtmlTextLinkHandler.OnLinkClicked -= HandleHtmlLinkClicked;
			}
		}
		#endregion

		#region UI STATE MANAGEMENT
		/// <summary>
		/// Sets the current launcher state and updates the UI accordingly.
		/// Centralizes all UI updates related to the launcher's operational state.
		/// </summary>
		/// <param name="newState">The new state for the launcher.</param>
		private void SetLauncherState(LauncherState newState)
		{
			currentLauncherState = newState;
			PlayButton.onClick.RemoveAllListeners(); // Always clear to ensure only one listener.

			bool isButtonInteractable = false;
			string buttonText = "";
			Action buttonAction = null;
			bool progressBarVisible = false;

			switch (currentLauncherState)
			{
				case LauncherState.LoadingNews:
					buttonText = UIText.StatusLoadingNews;
					break;
				case LauncherState.Connecting:
					buttonText = UIText.StatusConnecting;
					buttonAction = PlayButton_Connect;
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
					buttonAction = PlayButton_Launch;
					break;
				case LauncherState.ClientAhead:
					buttonText = UIText.StatusClientAhead;
					isButtonInteractable = true; // Allow playing even if client is ahead.
					buttonAction = PlayButton_Launch;
					break;
				case LauncherState.ConnectionFailed:
					buttonText = UIText.StatusConnectionFailed;
					isButtonInteractable = true;
					buttonAction = PlayButton_Connect; // Allow retry.
					break;
				case LauncherState.VersionCheckFailed:
					buttonText = UIText.StatusVersionCheckFailed;
					isButtonInteractable = true;
					buttonAction = PlayButton_Connect; // Allow retry.
					break;
				case LauncherState.PatchDownloadFailed:
					buttonText = UIText.StatusPatchDownloadFailed;
					isButtonInteractable = true;
					buttonAction = PlayButton_Update; // Allow retry.
					break;
				case LauncherState.UpdaterFailed:
					buttonText = UIText.StatusUpdaterFailed;
					isButtonInteractable = true;
					buttonAction = PlayButton_Connect; // Default to connect for retry.
					break;
				case LauncherState.LaunchFailed:
					buttonText = UIText.StatusLaunchFailed;
					isButtonInteractable = true;
					buttonAction = PlayButton_Launch;
					break;
				case LauncherState.VersionError:
					buttonText = UIText.StatusVersionError;
					isButtonInteractable = false; // Version error is often unrecoverable without manual intervention.
					break;
				default: // Fallback
					buttonText = UIText.ButtonConnect;
					isButtonInteractable = true;
					buttonAction = PlayButton_Connect;
					break;
			}

			PlayButtonText.text = buttonText;
			PlayButton.interactable = isButtonInteractable;
			ProgressBarGroup.SetActive(progressBarVisible);

			if (buttonAction != null)
			{
				PlayButton.onClick.AddListener(() => buttonAction.Invoke());
			}
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
			if (link.Contains("http") || link.Contains("www"))
			{
				Application.OpenURL(link);
			}
		}

		/// <summary>
		/// Initiates the connection process to fetch patch server list and check for game updates.
		/// This is the primary action when the launcher starts or after an error.
		/// </summary>
		public void PlayButton_Connect()
		{
			SetLauncherState(LauncherState.Connecting);

			// Delegate to patch server service
			StartCoroutine(PatchServerService.GetPatchServerAddress(
				Constants.Configuration.IPFetchHost,
				onComplete: (serverAddress) =>
				{
					patcherHost = $"http://{serverAddress.Address}:{serverAddress.Port}/"; // Use HTTPS if possible
					Log.Debug("ClientLauncher", string.Format(UIText.LogDebugNewPatchServer, serverAddress.Address, serverAddress.Port));
					StartCoroutine(GetLatestVersion()); // Proceed to get the latest version from this server.
				},
				onError: (error) =>
				{
					Log.Error("ClientLauncher", error);
					SetLauncherState(LauncherState.ConnectionFailed);
				}));
		}

		/// <summary>
		/// Launches the client after all version checks and updates are complete.
		/// </summary>
		public void PlayButton_Launch()
		{
			SetLauncherState(LauncherState.ReadyToPlay); // Set state, button will be disabled by this call.
			AddressableLoadProcessor.EnqueueLoad(new AddressableSceneLoadData("ClientPostboot", OnPostbootSceneLoaded));
			try
			{
				AddressableLoadProcessor.BeginProcessQueue();
			}
			catch (UnityException ex)
			{
				Log.Error("ClientLauncher", $"Failed to load preload scenes: {ex.Message}");
				SetLauncherState(LauncherState.LaunchFailed);
			}
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
		public void PlayButton_Update()
		{
			SetLauncherState(LauncherState.DownloadingPatch);

			// Delegate patch download to patch server service
			StartCoroutine(PatchServerService.DownloadPatch(
				$"{patcherHost}{MainBootstrapSystem.GameVersion}",
				Constants.GetTemporaryPath(),
				onComplete: () =>
				{
					SetLauncherState(LauncherState.ApplyingPatch);
					// Delegate updater launch to updater launcher service
					updaterLauncher.LaunchUpdater(
						updaterPath,
						MainBootstrapSystem.GameVersion,
						latestVersionString,
						onComplete: () => Quit(), // Updater completed, quit launcher
						onError: (error) =>
						{
							Log.Error("ClientLauncher", error);
							SetLauncherState(LauncherState.UpdaterFailed);
						});
				},
				onError: (error) =>
				{
					Log.Error("ClientLauncher", error);
					SetLauncherState(LauncherState.PatchDownloadFailed);

					// Attempt to clean up any partially downloaded file if an error occurs.
					string tempFilePath = Constants.GetTemporaryPath();
					if (File.Exists(tempFilePath))
					{
						try { File.Delete(tempFilePath); }
						catch (Exception ex) { Log.Error("ClientLauncher", $"Failed to delete temp patch file {tempFilePath}: {ex.Message}"); }
					}
				},
				onProgress: (progress, progressString) =>
				{
					ProgressSlider.value = progress;
					ProgressText.text = progressString;
				}));
		}

		/// <summary>
		/// Fetches the latest client version from the currently selected patch server.
		/// Compares it with the current client version and updates the UI accordingly.
		/// </summary>
		private IEnumerator GetLatestVersion()
		{
			SetLauncherState(LauncherState.CheckingVersion);

			// Delegate to patch server service
			yield return StartCoroutine(PatchServerService.GetLatestVersion(
				patcherHost,
				onComplete: (serverVersion) =>
				{
					latestVersionString = serverVersion.ToString(); // Store for updater launch
					Log.Debug("ClientLauncher", string.Format(UIText.LogDebugLatestServerVersion, latestVersionString));

					VersionConfig clientVersion;
					try
					{
						clientVersion = VersionConfig.Parse(MainBootstrapSystem.GameVersion);
					}
					catch (ArgumentException ex)
					{
						Log.Error("ClientLauncher", string.Format(UIText.ErrorParsingVersion, MainBootstrapSystem.GameVersion) + $" Exception: {ex.Message}");
						SetLauncherState(LauncherState.VersionError);
						return;
					}

					// Compare client and server versions to determine the appropriate action.
					if (clientVersion < serverVersion)
					{
						SetLauncherState(LauncherState.DownloadingPatch);
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
				},
				onError: (error) =>
				{
					Log.Error("ClientLauncher", error);
					SetLauncherState(LauncherState.VersionCheckFailed);
				}));
		}

		/// <summary>
		/// Quits the application.
		/// </summary>
		public void Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}
		#endregion
	}
}