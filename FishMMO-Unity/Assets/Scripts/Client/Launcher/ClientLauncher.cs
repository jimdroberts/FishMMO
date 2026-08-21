using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif
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
		[Header("Configuration")]
		/// <summary>
		/// Optional per-scene override for the news URL. Leave empty in normal use.
		/// </summary>
		/// <remarks>
		/// This deliberately defaults to empty rather than to
		/// <see cref="Constants.Configuration.LauncherHtmlUrl"/>. A non-empty default gets
		/// baked into the scene the first time it is saved, and the serialized copy then
		/// silently wins over the build-time configured value for every subsequent build —
		/// which is exactly how a stale hard-coded URL ended up shipping here. Resolution
		/// happens at read time in <see cref="HtmlViewURL"/> instead.
		/// </remarks>
		[Tooltip("Leave empty to use the build-time configured launcher news URL. Set only to override it for local testing.")]
		[SerializeField]
		private string htmlViewURL = "";
		/// <summary>
		/// The URL to fetch HTML news content from. Falls back to the build-time
		/// configured <see cref="Constants.Configuration.LauncherHtmlUrl"/> when no
		/// per-scene override is set.
		/// </summary>
		public string HtmlViewURL => string.IsNullOrWhiteSpace(htmlViewURL)
			? Constants.Configuration.LauncherHtmlUrl
			: htmlViewURL;
		/// <summary>
		/// The class name of the div to extract from the HTML content.
		/// </summary>
		[SerializeField]
		private string divClass = "content";

		/// <summary>
		/// Text shown in the news pane when no live feed is available.
		/// </summary>
		/// <remarks>
		/// Used both when no feed is configured and when the fetch fails. The pane is filled
		/// rather than hidden: hiding it collapsed the panel down to a header stacked directly on
		/// a footer, which reads as a broken window rather than as a launcher with no news today.
		/// <para>
		/// Serialized and multi-line so it can be rewritten per deployment without a code change —
		/// a shard running a private build wants its own words here.
		/// </para>
		/// </remarks>
		[Tooltip("Shown in the news pane when no feed is configured or the fetch fails.")]
		[TextArea(6, 16)]
		[SerializeField]
		private string newsFallbackSummary =
			"Welcome to FishMMO.\n\n" +
			"FishMMO is an open source MMO framework built on Unity and FishNet. It ships the " +
			"parts an online world actually needs and leaves the game itself to you: an " +
			"authoritative server with client-side prediction, a login and world server split " +
			"that scales to multiple scene servers, and a persistence layer backed by PostgreSQL.\n\n" +
			"Characters carry inventories, equipment, banks, abilities, buffs, factions, guilds " +
			"and parties, all synchronised through the same broadcast pipeline. Content is " +
			"authored as scriptable templates — items, abilities, NPCs, dungeons and quests — so " +
			"designers can build without touching networking code.\n\n" +
			"This launcher keeps your install patched. It checks the configured patch server on " +
			"start, downloads what has changed, and enables Play once you are up to date. Use " +
			"Settings to control automatic updates, request timeouts and where patches are " +
			"downloaded.\n\n" +
			"No news feed is configured for this build, so you are seeing this summary instead.";

		/// <summary>
		/// Text shown in the news pane when no live feed is available.
		/// </summary>
		public string NewsFallbackSummary => this.newsFallbackSummary;
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
		/// <summary>
		/// Seconds the launcher may sit in a state with no interactive button before it is
		/// forced back to a recoverable one.
		/// </summary>
		/// <remarks>
		/// A catch-all, not a substitute for per-operation error handling. Every transient
		/// state is driven by a coroutine and reports its own failures; this exists because
		/// a coroutine that dies (Unity logs the exception and silently stops it) leaves the
		/// player facing a dead button with no message and no way to retry. Download
		/// progress resets the timer, so a slow patch is never interrupted.
		/// </remarks>
		[Tooltip("Seconds the launcher may sit in a non-interactive state before recovering. Download progress resets the timer.")]
		[SerializeField]
		private float transientStateTimeoutSeconds = 120f;
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
		/// Optional view component that renders this launcher. Must implement
		/// <see cref="ILauncherView"/>.
		/// </summary>
		/// <remarks>
		/// Typed as <see cref="MonoBehaviour"/> because Unity cannot serialize an interface
		/// reference. Required despite the name: <see cref="ResolveView"/> has nothing to fall
		/// back to, so a scene that leaves this empty gets a launcher that cannot draw itself and
		/// says so. <see cref="UITKClientLauncher"/> is what belongs here.
		/// </remarks>
		[Tooltip("Required. A component implementing ILauncherView, normally UITKClientLauncher.")]
		[SerializeField]
		private MonoBehaviour launcherViewComponent;
		/// <summary>
		/// The active view. Never null after <see cref="Awake"/>.
		/// </summary>
		private ILauncherView view;
		/// <summary>
		/// The updater launcher used to start the external updater process.
		/// </summary>
		private IUpdaterLauncher updaterLauncher;
		#endregion

		#region INTERNAL STATE
		/// <summary>
		/// Addressable scene name of the post-boot scene the launcher hands off to.
		/// </summary>
		/// <remarks>
		/// Internal rather than private so <see cref="UITKClientLauncher"/> can watch for this
		/// scene and dismiss itself, without duplicating the name in a second place where the two
		/// could drift apart.
		/// </remarks>
		internal const string PostbootSceneName = "ClientPostboot";
		/// <summary>
		/// Addressable scene name of this launcher scene, unloaded once post-boot is up.
		/// </summary>
		private const string LauncherSceneName = "ClientLauncher";

		/// <summary>
		/// Guards against re-entering PlayButtonConnect while a connection is in progress.
		/// </summary>
		private bool isConnecting = false;
		/// <summary>
		/// Guards against re-entering PlayButtonLaunch while a launch is in progress.
		/// </summary>
		private bool isLaunching = false;
		/// <summary>
		/// Guards against re-entering PlayButtonUpdate while a download/patch is in
		/// progress. The DownloadingPatch/ApplyingPatch states disable the button, but the
		/// update flow is also entered directly from the version check, so the button
		/// state alone is not a sufficient interlock.
		/// </summary>
		private bool isUpdating = false;
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
		/// Size in bytes of the patch for the current client version, as reported by the patch
		/// server, or 0 when it did not supply one.
		/// </summary>
		/// <remarks>
		/// Captured from the version check so the download can show a total from its first
		/// frame instead of waiting on response headers.
		/// </remarks>
		private long expectedPatchSize;
		/// <summary>
		/// Guards the install-size measurement so it runs at most once per launcher session.
		/// </summary>
		private bool installSizeRequested;
		/// <summary>
		/// Window dimensions observed on the previous frame, used to detect a resize.
		/// </summary>
		private int lastKnownWidth;
		private int lastKnownHeight;
		/// <summary>
		/// False until the first observed window size has been discarded as the one the
		/// launcher itself requested rather than one the player chose.
		/// </summary>
		private bool windowSizeInitialised;
		/// <summary>
		/// True when a resize has been observed but not yet written to settings.
		/// </summary>
		private bool windowSizeDirty;
		/// <summary>
		/// <see cref="Time.realtimeSinceStartup"/> of the most recent resize.
		/// </summary>
		private float lastResizeTime;
		/// <summary>
		/// Seconds a window size must hold steady before it is persisted, so one drag-resize
		/// costs one write rather than one per frame.
		/// </summary>
		private const float WindowSizeSaveDelay = 0.75f;
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
		/// <summary>
		/// <see cref="Time.realtimeSinceStartup"/> of the last state change or activity
		/// heartbeat, used by the transient-state watchdog.
		/// </summary>
		private float lastStateActivityTime;
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
			public const string StatusPatchUnavailable = "Update Unavailable";
			public const string StatusServerRejectedVersion = "Version Rejected by Server";

			// Default player-facing detail for states that would otherwise show only a
			// two-word button label with no explanation of what to do next.
			public const string DetailConnectionFailed = "Could not reach the update server. Check your internet connection and firewall, then try again.";
			public const string DetailVersionCheckFailed = "Could not determine the current game version. Press the button to try again.";
			public const string DetailPatchDownloadFailed = "The update could not be downloaded. Press the button to retry.";
			public const string DetailUpdaterFailed = "The updater could not run. Verify your installation is intact, then try again.";
			public const string DetailLaunchFailed = "The game could not be started. Press the button to re-check for updates.";
			public const string DetailVersionError = "The installed version could not be read. Press the button to try again, or reinstall the client.";
			public const string DetailClientAhead = "This client is newer than the server. Press the button to re-check once the server has been updated.";
			public const string DetailPatchUnavailable = "No update path exists from this client version. Please download and install the latest full client.";
			public const string DetailApplyingPatch = "Applying the update to your game files. The client will restart automatically — do not close this window.";

			public const string ErrorLoadingNews = "Error loading news: ";
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
			// Before anything reads a setting. The launcher is the first thing to run, so
			// nothing else has loaded the configuration file by this point.
			LauncherSettings.EnsureLoaded();

			// Resolved first so that even the fatal-dependency path below has somewhere to
			// report to. A player who hits that path has no access to the log file.
			this.view = ResolveView();

			// SystemUpdaterLauncher is a plain C# class, directly instantiate it
			this.updaterLauncher = new SystemUpdaterLauncher();

			// Basic null checks for dependencies, including the shared web request service
			// that the two sub-services depend on. Those services null-check it themselves
			// and disable, but validating here lets us report one clear fatal message
			// instead of leaving the UI on "Loading News..." with a dead coroutine.
			if (this.unityWebRequestService == null || this.htmlContentFetcher == null || this.patchServerService == null ||
				this.htmlContentFetcher.WebRequestService == null || this.patchServerService.WebRequestService == null)
			{
				Log.Error("ClientLauncher", "One or more required service dependencies are not assigned in the Inspector or are missing!");
				this.view.SetButtonText("Fatal Error");
				this.view.SetButtonInteractable(false);
				this.view.SetProgressVisible(false);
				this.view.ShowStatus("The launcher is missing required components and cannot start. Please reinstall the client.");
				enabled = false; // Disable this script if dependencies aren't met
				return;
			}

			// Wire Quit in code. The scene's persistent UnityEvent listener had a null
			// target, so the button silently did nothing; a code-side listener cannot be
			// broken by a scene re-save.
			this.view.SetQuitAction(Quit);

			// Catch-all so no async failure can leave the player on a dead button.
			StartCoroutine(TransientStateWatchdog());

			/* News and the version check run concurrently.
			 *
			 * The news pane is cosmetic, but it used to sit on the critical path: the version
			 * check only began once this request settled, so every startup paid the full news
			 * round trip — and, when the host was unreachable, its timeout as well. Against a
			 * host that drops packets rather than refusing, that was the request timeout in
			 * front of every launch with the Play button disabled and nothing to look at.
			 *
			 * Neither depends on the other, so neither waits for the other. The fetch now only
			 * writes into the news pane whenever it lands, and startup proceeds immediately. */
			/* No feed configured is a valid deployment, not a failure. Dispatching the fetch
			 * anyway reaches UnityWebRequestService's empty-URL branch, which logs an error and
			 * raises OnFailure — so a launcher with news deliberately switched off reported a
			 * fetch failure to the player and left "Could not display news content" sitting in
			 * the pane. Hide the pane instead and never issue the request. */
			if (!IsNewsUrlConfigured(this.HtmlViewURL))
			{
				Log.Debug("ClientLauncher", "No launcher news URL configured; showing the built-in summary.");
				this.view.SetNewsVisible(true);
				this.view.SetNewsMessage(this.NewsFallbackSummary);
			}
			else
			{
				this.view.SetNewsVisible(true);
				this.view.SetNewsMessage(UIText.StatusLoadingNews);

				StartCoroutine(this.htmlContentFetcher.FetchAndExtract(
					this.HtmlViewURL,
					this.DivClass,
					onContentReady: (content) =>
					{
						this.view.SetNewsContent(content);
					},
					onError: (error) =>
					{
						// Reporting this as "Connection Failed" would also mislead the player into
						// thinking the game servers are down. If the network really is down, the
						// version check reports that accurately on its own.
						/* The player gets the summary rather than an error string. A feed that is
						 * down is the operator's problem, not something the person trying to play
						 * can act on, and an empty pane with an apology in it is worse than the
						 * same pane carrying something worth reading. The reason still goes to
						 * the log for whoever can actually fix it. */
						Log.Warning("ClientLauncher", $"{UIText.ErrorLoadingNews}{error}");
						this.view.SetNewsMessage(this.NewsFallbackSummary);
					}));
			}

			// Construct the full path to the updater executable
			this.updaterPath = Path.Combine(Constants.GetWorkingDirectory(), Constants.Configuration.UpdaterExecutable);

#if !UNITY_EDITOR
			ApplyWindowSize();
#endif
			string versionString = !string.IsNullOrEmpty(MainBootstrapSystem.GameVersion)
				? MainBootstrapSystem.GameVersion
				: "0.0.0-unknown";
			this.view.SetTitle($"{Constants.Configuration.ProjectName} v{versionString}");
			this.view.SetProgressVisible(false); // Ensure progress bar is hidden initially.

			/* Last, so the version check never observes a half-initialised launcher.
			 *
			 * PlayButtonConnect runs synchronously up to GetLatestVersion's first yield, and
			 * SetLauncherState drives the UI immediately — so starting it earlier in Awake put
			 * that work ahead of updaterPath being assigned and ahead of the line above that
			 * hides the progress bar, letting later initialisation quietly undo what the state
			 * change had just set up. Nothing downstream needs it to start sooner: the point of
			 * decoupling it from the news fetch was to stop waiting on a network round trip, not
			 * to run before Awake has finished. */
			BeginStartupFlow();
		}

		/// <summary>
		/// Smallest window the launcher layout stays usable at. Mirrors the min-width and
		/// min-height in UILauncher.uss — below this the footer buttons start to be squeezed.
		/// </summary>
		private const int MinWindowWidth = 480;
		private const int MinWindowHeight = 360;

		/// <summary>
		/// Opens the launcher at the size the player last used, or the configured default the
		/// first time.
		/// </summary>
		/// <remarks>
		/// The window is resizable, so pinning it to a fixed size on every launch would undo
		/// the player's choice each time. The stored size is clamped against the current
		/// display by <see cref="LauncherSettings.GetWindowSize"/>, which matters when a window
		/// saved on a larger monitor is restored on a smaller one.
		/// </remarks>
		private void ApplyWindowSize()
		{
			Vector2Int stored = LauncherSettings.GetWindowSize(MinWindowWidth, MinWindowHeight);
			int width = stored.x > 0 ? stored.x : this.DefaultScreenWidth;
			int height = stored.y > 0 ? stored.y : this.DefaultScreenHeight;

			try
			{
				Screen.SetResolution(width, height, FullScreenMode.Windowed, Screen.currentResolution.refreshRateRatio);
			}
			catch
			{
				// Some display configurations report a refresh rate SetResolution rejects.
				Screen.SetResolution(width, height, FullScreenMode.Windowed, new RefreshRate() { numerator = 60, denominator = 1 });
			}
		}

		/// <summary>
		/// Records the window size when the player changes it, so the next launch reopens at
		/// the same dimensions.
		/// </summary>
		/// <remarks>
		/// Written shortly after the resize settles rather than on quit: the updater terminates
		/// this process to apply a patch, so a launcher that is killed rather than closed would
		/// never persist anything saved at shutdown.
		/// <para>
		/// Debounced because a drag-resize changes the window size every frame, and writing the
		/// configuration file per frame would mean hundreds of disk writes for one gesture.
		/// </para>
		/// </remarks>
		private void Update()
		{
			if (Screen.width != this.lastKnownWidth || Screen.height != this.lastKnownHeight)
			{
				this.lastKnownWidth = Screen.width;
				this.lastKnownHeight = Screen.height;

				// The first observed size is the one this launcher just asked for, not one the
				// player chose. Recording it would overwrite a stored size with the default.
				if (!this.windowSizeInitialised)
				{
					this.windowSizeInitialised = true;
					return;
				}

				this.windowSizeDirty = true;
				this.lastResizeTime = Time.realtimeSinceStartup;
				return;
			}

			if (!this.windowSizeDirty)
			{
				return;
			}
			if (Time.realtimeSinceStartup - this.lastResizeTime < WindowSizeSaveDelay)
			{
				return;
			}

			this.windowSizeDirty = false;

			// Only windowed dimensions are worth remembering; a maximised or full-screen size
			// is the display's, not a size the player picked for this window.
			if (Screen.fullScreenMode != FullScreenMode.Windowed)
			{
				return;
			}

			LauncherSettings.SetWindowSize(this.lastKnownWidth, this.lastKnownHeight);
			LauncherSettings.Save();
		}

		/// <summary>
		/// Whether a news URL is real, as opposed to absent or an unsubstituted build placeholder.
		/// </summary>
		/// <param name="url">The configured URL.</param>
		/// <returns>True when a fetch is worth issuing.</returns>
		/// <remarks>
		/// <c>HostConfig.generated.cs</c> ships the URL as
		/// <c>https://www.FISHMMO_SENTINEL_PLACEHOLDER_ROOT_DOMAIN/...</c>, and CI rewrites the
		/// sentinel from FISHMMO_ROOT_DOMAIN at build time. In a working copy that substitution
		/// has not happened, so the URL is non-empty but points at a host that cannot resolve —
		/// the fetch fails and the launcher tells the developer "Could not display news content"
		/// on every run, for a feed that was never configured.
		///
		/// An unsubstituted sentinel means the same thing as an empty string: no feed. Treating
		/// them alike hides the pane instead of reporting a failure, and matches how
		/// <c>ClientCertificatePinning</c> already screens sentinel values out of the pin set.
		/// </remarks>
		private static bool IsNewsUrlConfigured(string url)
		{
			if (string.IsNullOrWhiteSpace(url))
			{
				return false;
			}
			return !url.Contains(FishMMO.Client.Security.GeneratedPinSet.SentinelMarker);
		}

		/// <summary>
		/// Returns the view this launcher renders through.
		/// </summary>
		/// <returns>The assigned view, or null when none is usable.</returns>
		/// <remarks>
		/// There is no longer a fallback. The uGUI adapter this used to drop back to rendered
		/// through TextMeshPro, and both went with the UI Toolkit conversion — so a missing or
		/// wrongly-typed assignment now means the launcher has no way to draw itself, and saying so
		/// beats silently constructing a view over serialized fields that no longer exist.
		/// </remarks>
		private ILauncherView ResolveView()
		{
			if (this.launcherViewComponent is ILauncherView assigned)
			{
				return assigned;
			}

			if (this.launcherViewComponent != null)
			{
				Log.Error("ClientLauncher", $"Assigned launcher view '{this.launcherViewComponent.GetType().Name}' does not implement ILauncherView.");
			}
			else
			{
				Log.Error("ClientLauncher", "No launcher view is assigned; the launcher cannot render.");
			}
			return null;
		}

		/// <summary>
		/// Starts the version check that gates launching. Runs immediately at startup,
		/// independently of the news fetch.
		/// </summary>
		private void BeginStartupFlow()
		{
#if !UNITY_EDITOR
			PlayButtonConnect();
#else
			SetLauncherState(LauncherState.ReadyToPlay);
#endif
		}

		/// <summary>
		/// Unity OnDestroy method. Cleans up event subscriptions.
		/// </summary>
		private void OnDestroy()
		{
			// Null when Awake bailed before resolving a view.
			this.view?.Teardown();
		}
		#endregion

		#region UI STATE MANAGEMENT
		/// <summary>
		/// Logs a human-readable status or error message and hands it to the view to display.
		/// </summary>
		/// <remarks>
		/// Always logs so operators can correlate what the player saw with the log file. How
		/// the message is made visible is the view's problem — that used to be decided here,
		/// which meant one view's quirk (no dedicated status element, so the progress label
		/// gets borrowed and its parent group forced active) was baked into shared logic.
		/// </remarks>
		private void SetStatus(string message, LogLevel level = LogLevel.Info)
		{
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

			this.view.ShowStatus(message);
		}

		/// <summary>
		/// Returns the default player-facing explanation for <paramref name="state"/>, or
		/// null when the state needs no detail line. Used when a caller does not supply a
		/// more specific message, so no state can present a bare button label with no
		/// indication of what happened or what to do next.
		/// </summary>
		private static string GetDefaultDetail(LauncherState state)
		{
			switch (state)
			{
				case LauncherState.ConnectionFailed: return UIText.DetailConnectionFailed;
				case LauncherState.VersionCheckFailed: return UIText.DetailVersionCheckFailed;
				case LauncherState.PatchDownloadFailed: return UIText.DetailPatchDownloadFailed;
				case LauncherState.UpdaterFailed: return UIText.DetailUpdaterFailed;
				case LauncherState.LaunchFailed: return UIText.DetailLaunchFailed;
				case LauncherState.VersionError: return UIText.DetailVersionError;
				case LauncherState.ClientAhead: return UIText.DetailClientAhead;
				case LauncherState.PatchUnavailable: return UIText.DetailPatchUnavailable;
				case LauncherState.ApplyingPatch: return UIText.DetailApplyingPatch;
				default: return null;
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
			this.lastStateActivityTime = Time.realtimeSinceStartup;

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
				case LauncherState.UpdateAvailable:
					buttonText = UIText.ButtonUpdate;
					isButtonInteractable = true;
					buttonAction = PlayButtonUpdate;
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
				case LauncherState.PatchUnavailable:
					// The server has no patch from this specific version to the latest.
					// Retrying the download can only 404 again, so the button re-runs the
					// version check (which may succeed after the server publishes a patch)
					// rather than looping on a request that cannot succeed.
					buttonText = UIText.StatusPatchUnavailable;
					isButtonInteractable = true;
					buttonAction = PlayButtonConnect;
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

			this.view.SetButtonText(buttonText);
			this.view.SetButtonInteractable(isButtonInteractable);
			// Replaces whatever the previous state attached, so the button never carries more
			// than the action belonging to the state currently displayed.
			this.view.SetButtonAction(buttonAction);

			// Fall back to a standard explanation so no state can present a bare label.
			string detail = !string.IsNullOrEmpty(errorDetail) ? errorDetail : GetDefaultDetail(newState);

			this.view.SetProgressVisible(progressBarVisible);

			// Display error/status detail to the player when available.
			if (!string.IsNullOrEmpty(detail))
			{
				SetStatus(detail, level: IsErrorState(newState) ? LogLevel.Error : LogLevel.Info);
			}
			else
			{
				this.view.ClearStatus();
			}

			MeasureInstallSizeWhenIdle(newState);
		}

		/// <summary>
		/// Kicks off the install-size measurement once the launcher has settled.
		/// </summary>
		/// <remarks>
		/// Deliberately not started in Awake. Walking a full client install is tens of
		/// thousands of stat calls, and doing it while the version check or a patch download is
		/// in flight puts it in direct contention with them for disk — slowing the thing the
		/// player is actually waiting on to populate a readout they are not yet looking at.
		/// Idle states only, and only once per run.
		/// </remarks>
		private void MeasureInstallSizeWhenIdle(LauncherState state)
		{
			if (this.installSizeRequested)
			{
				return;
			}
			if (state != LauncherState.ReadyToPlay && state != LauncherState.UpdateAvailable)
			{
				return;
			}

			this.installSizeRequested = true;
			StartCoroutine(InstallSizeProbe.Measure(
				Constants.GetWorkingDirectory(),
				sizeBytes => this.view.SetInstallSize(sizeBytes)));
		}

		/// <summary>
		/// True for states that are waiting on an asynchronous operation and offer the
		/// player no way to act. These are the states the watchdog guards.
		/// </summary>
		private static bool IsTransientState(LauncherState state)
		{
			return state == LauncherState.LoadingNews ||
			       state == LauncherState.Connecting ||
			       state == LauncherState.CheckingVersion ||
			       state == LauncherState.DownloadingPatch ||
			       state == LauncherState.ApplyingPatch;
		}

		/// <summary>
		/// Forces the launcher out of a transient state that has stopped making progress,
		/// so a dead coroutine cannot leave the player with a disabled button forever.
		/// </summary>
		private IEnumerator TransientStateWatchdog()
		{
			WaitForSeconds tick = new WaitForSeconds(1f);

			while (true)
			{
				yield return tick;

				if (!IsTransientState(this.currentLauncherState))
				{
					continue;
				}
				if (Time.realtimeSinceStartup - this.lastStateActivityTime <= this.transientStateTimeoutSeconds)
				{
					continue;
				}

				LauncherState stalled = this.currentLauncherState;
				Log.Error("ClientLauncher", $"Launcher stalled in {stalled} for over {this.transientStateTimeoutSeconds}s with no activity. Recovering.");

				// Clear the in-flight guards; whatever owned them is not coming back.
				this.isConnecting = false;
				this.isUpdating = false;

				SetLauncherState(
					stalled == LauncherState.DownloadingPatch ? LauncherState.PatchDownloadFailed : LauncherState.ConnectionFailed,
					$"The launcher stopped responding while it was busy ({stalled}). Press the button to try again.");
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
			       state == LauncherState.PatchUnavailable ||
			       state == LauncherState.VersionError;
		}
		#endregion

		#region UI INTERACTION HANDLERS (Delegate actions to services)
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
				// ReadyToPlay leaves the button enabled; disable it now that the launch is
				// actually under way so it cannot be pressed twice.
				this.view.SetButtonInteractable(false);

				// A retry after a failed launch would otherwise dead-end: EnqueueLoad
				// silently no-ops when the scene is already tracked as loaded, so
				// OnPostbootSceneLoaded would never fire and the watchdog would report
				// another launch failure with the launcher still sitting on top.
				if (AddressableLoadProcessor.IsSceneLoaded(PostbootSceneName))
				{
					Log.Debug("ClientLauncher", $"{PostbootSceneName} is already loaded; completing the launch directly.");
					OnPostbootSceneLoaded(SceneManager.GetSceneByName(PostbootSceneName));
					return;
				}

				AddressableLoadProcessor.EnqueueLoad(new AddressableSceneLoadData(PostbootSceneName, OnPostbootSceneLoaded));
				try
				{
					// Watch the batch as well as the scene callback. An Addressable load that
					// fails asynchronously never invokes OnPostbootSceneLoaded, and without
					// this the only signal was the 30s watchdog timing out with a generic
					// message. The batch reports the failure the moment it happens.
					AddressableLoadBatch batch = AddressableLoadProcessor.BeginProcessQueue();
					batch.Completed += OnLaunchBatchCompleted;
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
		/// Reports a failed launch as soon as the load batch settles.
		/// </summary>
		/// <remarks>
		/// Only handles the failure case. On success <see cref="OnPostbootSceneLoaded"/> has
		/// already run from the scene's own callback and unloaded this scene.
		/// </remarks>
		/// <param name="batch">The completed launch batch.</param>
		private void OnLaunchBatchCompleted(AddressableLoadBatch batch)
		{
			if (batch == null || !batch.HasFailures)
			{
				return;
			}

			this.isLaunching = false;
			SetLauncherState(LauncherState.LaunchFailed,
				$"Could not load the game scene ({string.Join(", ", batch.FailedItems)}). Check that Addressable bundles are built and up to date.");
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
		private void OnPostbootSceneLoaded(Scene scene)
		{
			Log.Debug("ClientLauncher", $"{PostbootSceneName} scene loaded.");

			if (!scene.IsValid() || !scene.isLoaded)
			{
				// Bail out rather than unloading ourselves and leaving the player with no
				// UI at all. The watchdog will surface this as a launch failure.
				this.isLaunching = false;
				SetLauncherState(LauncherState.LaunchFailed,
					$"The game scene '{PostbootSceneName}' reported as loaded but is not usable. Check that Addressable bundles are built and up to date.");
				return;
			}

			/* Hidden before the unload is attempted, and independently of whether it succeeds.
			 * The unload below only works when Addressables owns the scene; in the editor the
			 * launcher scene is usually opened directly, so Addressables has no handle for it
			 * and the call is a silent no-op — which left the launcher UI on screen behind the
			 * login screen for the rest of the session.
			 *
			 * Unloading the scene is not what removes the UI either way. The launcher draws into
			 * a panel owned by a PanelSettings asset shared with the login and world scenes, and
			 * that panel outlives this scene, so the view has to detach itself. This call is the
			 * one that does it; everything below is housekeeping. */
			/* UnityEngine.Debug rather than FishMMO.Logging. Log.Initialize runs in
			 * MainBootstrapSystem, which never executes when the editor opens ClientLauncher
			 * directly through FishMMO/QuickStart — the console formatter is null on that path
			 * and Log output does not reach the console at all. A diagnostic that only prints
			 * on the entry path that already works is not a diagnostic. */
			Debug.Log("[ClientLauncher] Hiding the launcher UI and unloading the launcher scene.");
			this.view.SetVisible(false);

			UnloadLauncherScene();

			// Find the ClientPostbootSystem in the loaded scene and start its bootstrap process.
			foreach (var rootGO in scene.GetRootGameObjects())
			{
				rootGO.GetComponent<ClientPostbootSystem>()?.StartBootstrap();
			}
		}

		/// <summary>
		/// Unloads the launcher scene, whichever way it was loaded.
		/// </summary>
		/// <remarks>
		/// Addressables is asked first, because that is how a shipped build loads this scene and
		/// its handle has to be released to free the bundle. It keeps its own dictionary of
		/// scenes it loaded and does nothing for a scene missing from it, so the editor — where
		/// the scene is opened directly or through the QuickStart menu — falls through to
		/// <c>SceneManager</c>.
		///
		/// Unloading destroys this component, which is why nothing after the call may depend on
		/// it; <c>UnloadSceneAsync</c> completes on a later frame, so the rest of the calling
		/// method still runs.
		/// </remarks>
		private void UnloadLauncherScene()
		{
			AddressableLoadProcessor.UnloadSceneByLabelAsync(LauncherSceneName);

			Scene launcherScene = SceneManager.GetSceneByName(LauncherSceneName);
			if (!launcherScene.IsValid() || !launcherScene.isLoaded)
			{
				// Addressables already took it, or it was never a separate scene.
				return;
			}

			/* Unloading the last remaining scene is an error in Unity, and would leave the client
			 * with nothing loaded at all. The postboot scene is in by this point, so this is a
			 * guard against an unexpected ordering rather than an expected branch. */
			if (SceneManager.sceneCount <= 1)
			{
				Log.Warning("ClientLauncher",
					$"{LauncherSceneName} is the only loaded scene; leaving it loaded and relying on the hidden UI.");
				return;
			}

			/* Deactivated before the unload is requested. UnloadSceneAsync completes on a later
			 * frame, and the login screen can finish loading inside that window — which is long
			 * enough for the launcher to still be drawing over it. Deactivating the roots is
			 * immediate and makes the unload's timing irrelevant. */
			foreach (GameObject rootGO in launcherScene.GetRootGameObjects())
			{
				if (rootGO != null)
				{
					rootGO.SetActive(false);
				}
			}

			Debug.Log($"[ClientLauncher] Unloading {LauncherSceneName} through SceneManager. Loaded scenes: {DescribeLoadedScenes()}");

			/* The handle is kept and its outcome reported.
			 *
			 * UnloadSceneAsync returns null when Unity refuses the request outright rather than
			 * throwing, and this one is issued from inside the postboot scene's own load
			 * callback — the window in which Unity is least willing to start another scene
			 * operation. A null went unnoticed before, so a launcher scene that was never going
			 * to unload looked identical to one whose unload had simply not finished yet.
			 *
			 * Nothing on screen depends on the answer: the roots above are already deactivated
			 * and the view has already detached itself from the shared UI panel. This reports
			 * whether the scene also went away, which is the difference between a leak and a
			 * visible launcher. */
			AsyncOperation unload = SceneManager.UnloadSceneAsync(launcherScene);
			if (unload == null)
			{
				Debug.LogError($"[ClientLauncher] SceneManager refused to unload {LauncherSceneName}. Its roots are deactivated, so it stays loaded but inert.");
				return;
			}

			// Static-only body: the unload this waits on destroys the component that started it.
			unload.completed += _ =>
			{
				Debug.Log($"[ClientLauncher] {LauncherSceneName} unload completed. Loaded scenes: {DescribeLoadedScenes()}");
			};
		}

		/// <summary>
		/// Comma-separated names of every currently loaded scene, for handoff diagnostics.
		/// </summary>
		/// <remarks>
		/// Reported either side of the unload so the console shows whether the launcher scene
		/// actually left, rather than only that an unload was requested.
		/// </remarks>
		private static string DescribeLoadedScenes()
		{
			string[] names = new string[SceneManager.sceneCount];
			for (int i = 0; i < names.Length; ++i)
			{
				names[i] = SceneManager.GetSceneAt(i).name;
			}
			return string.Join(", ", names);
		}

		/// <summary>
		/// Initiates the update process by attempting to download and apply the patch.
		/// </summary>
		public void PlayButtonUpdate()
		{
			if (this.isUpdating)
			{
				return;
			}

			// The patch file name is derived from the target version, so an update cannot
			// proceed without one. Reachable if the retry button is pressed after state was
			// lost; re-run the version check rather than downloading to a malformed path.
			if (string.IsNullOrEmpty(this.latestVersionString))
			{
				Log.Warning("ClientLauncher", "Update requested before a successful version check. Re-checking version.");
				PlayButtonConnect();
				return;
			}

			this.isUpdating = true;

			SetLauncherState(LauncherState.DownloadingPatch);

			/* The patch must land where the Updater will look for it. Both sides used to
			 * derive <install root>/Patches independently and agree only by convention; the
			 * directory is now resolved once here and handed to the Updater explicitly,
			 * because a disagreement fails silently — the Updater reports "patch file not
			 * found", relaunches the client unchanged, and the launcher detects the same
			 * version mismatch again on the next run, forever. */
			string patchesDirectory = LauncherSettings.ResolvePatchDirectory(Constants.GetPatchesDirectory());
			string patchFilePath = Path.Combine(
				patchesDirectory,
				Constants.GetPatchFileName(MainBootstrapSystem.GameVersion, this.latestVersionString));

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
				patchFilePath,
				this.expectedPatchSha256,
				this.expectedPatchSize,
				onComplete: (patchWritten) =>
				{
					if (!patchWritten)
					{
						// Server answered "already up to date" mid-flight (the version we
						// checked against was superseded, or we raced a deployment).
						// Nothing to apply — go straight to Play instead of invoking the
						// updater on a patch that does not exist.
						this.isUpdating = false;
						SetLauncherState(LauncherState.ReadyToPlay);
						return;
					}

					SetLauncherState(LauncherState.ApplyingPatch);

					// Delegate updater launch to updater launcher service. NOTE: the patch
					// archive is deliberately NOT deleted here — the Updater is about to
					// read it, and it removes the archive itself once applied.
					StartCoroutine(this.updaterLauncher.LaunchUpdater(
						this.updaterPath,
						MainBootstrapSystem.GameVersion,
						this.latestVersionString,
						patchesDirectory,
						onComplete: () =>
						{
							// The updater is running and owns the install; it will terminate
							// this process and relaunch the client. Quit promptly so the
							// client binaries are released.
							Log.Info("ClientLauncher", "Updater has taken over. Shutting down the launcher.");
							Quit();
						},
						onError: (error) =>
						{
							this.isUpdating = false;
							SetLauncherState(LauncherState.UpdaterFailed, error);
						}));
				},
				onError: (error) =>
				{
					this.isUpdating = false;
					SetLauncherState(LauncherState.PatchDownloadFailed, error);

					// Attempt to clean up any partially downloaded file if an error occurs.
					TryDeletePatchFile(patchFilePath);
				},
				onProgress: (stats) =>
				{
					// Heartbeat: a large patch on a slow link must not trip the transient
					// state watchdog while it is genuinely still downloading.
					this.lastStateActivityTime = Time.realtimeSinceStartup;

					this.view.SetProgress(stats);
				}));
		}

		/// <summary>
		/// Best-effort removal of a partial or rejected patch archive. Failures are logged
		/// but never propagated — a leftover file is recoverable, an exception here is not.
		/// </summary>
		private static void TryDeletePatchFile(string path)
		{
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
			catch (Exception ex)
			{
				Log.Error("ClientLauncher", $"Failed to delete patch file {path}: {ex.Message}");
			}
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

					/* Heartbeat, and the only feedback this state offers.
					 *
					 * Each candidate costs up to the request timeout times its retry count
					 * before the next is tried, so a check across several unreachable hosts
					 * runs for minutes. Nothing here refreshed lastStateActivityTime, so the
					 * transient watchdog measured the whole sweep as a single stall and could
					 * abort a check that was still working through its candidates. Marking
					 * activity per attempt scopes that to one host's budget.
					 *
					 * The player otherwise sees a frozen "Checking Version..." for the whole
					 * sweep, which is indistinguishable from a hang; the counter shows it is
					 * still working through hosts. */
					this.lastStateActivityTime = Time.realtimeSinceStartup;

					/* Written to the button label rather than through SetStatus, because a
					 * view is free to route status wherever it has room — including a surface
					 * this state has hidden. The button label is the one status surface every
					 * view is guaranteed to be showing here. */
					if (candidates.Count > 1)
					{
						this.view.SetButtonText($"{UIText.StatusCheckingVersion} ({i + 1}/{candidates.Count})");
					}

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

				// Defensive: the service already rejects an unparseable server version, but
				// a null here would NRE below and strand the UI on "Checking Version..."
				// with the button disabled and nothing logged to the player.
				if (serverVersion == null)
				{
					SetLauncherState(LauncherState.VersionCheckFailed,
						"The update server returned an unreadable version. Please try again later.");
					yield break;
				}

				this.selectedApiHost = successfulHost;
				this.latestVersionString = serverVersion.FullVersion; // Store for updater launch
				this.expectedPatchSha256 = patchInfo.Sha256; // May be null/empty when not provided.
				this.expectedPatchSize = patchInfo.Size; // May be 0 when not provided.
				Log.Debug("ClientLauncher", string.Format(UIText.LogDebugLatestServerVersion, latestVersionString));

				// VersionConfig.Parse returns null for malformed input rather than throwing,
				// so this must be a null check. A null client version silently compares as
				// "older than everything" and would kick off a patch download for a version
				// the server has never heard of.
				VersionConfig clientVersion = VersionConfig.Parse(MainBootstrapSystem.GameVersion);
				if (clientVersion == null)
				{
					SetLauncherState(LauncherState.VersionError,
						$"The installed client version '{MainBootstrapSystem.GameVersion}' is not valid. Please reinstall the client.");
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
					// The server tells us up front whether it holds a patch for this exact
					// version. Without this check an outdated client with no upgrade path
					// downloads, 404s, lands in PatchDownloadFailed, and its retry button
					// repeats the same impossible request forever.
					if (!patchInfo.PatchAvailable)
					{
						Log.Warning("ClientLauncher",
							$"Server has no patch from {MainBootstrapSystem.GameVersion} to {this.latestVersionString}.");
						SetLauncherState(LauncherState.PatchUnavailable,
							$"This client (v{MainBootstrapSystem.GameVersion}) cannot be updated to v{this.latestVersionString} automatically. Please download and install the latest full client.");
						yield break;
					}

					if (LauncherSettings.AutoUpdate)
					{
						PlayButtonUpdate();
					}
					else
					{
						// The player asked to be consulted. Park on an Update button rather
						// than starting a download they have not agreed to — which on a metered
						// connection is the difference between a convenience and a cost.
						string sizeNote = this.expectedPatchSize > 0
							? $" ({DownloadStats.FormatBytes((ulong)this.expectedPatchSize)})"
							: string.Empty;
						SetLauncherState(LauncherState.UpdateAvailable,
							$"Version {this.latestVersionString} is available{sizeNote}. Press Update to download it.");
					}
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