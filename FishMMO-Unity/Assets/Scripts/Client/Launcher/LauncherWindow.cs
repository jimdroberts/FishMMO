using System;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Owns the launcher's window mode for the lifetime of the process: puts the window into the
	/// launcher's windowed size before the first scene loads, keeps the display's native mode for
	/// the hand-off to the game, and returns the window to the launcher's size before a clean
	/// exit so the next launch opens in it.
	/// </summary>
	/// <remarks>
	/// <para><b>Why this is not done in the launcher's Awake.</b> A standalone player creates its
	/// window before any script runs, in the mode Unity remembered from the previous exit. The
	/// launcher and the game share one process and the process always exits from the game, in
	/// fullscreen, so every launch after the first opened fullscreen, played the Unity splash
	/// screen fullscreen, loaded two addressable scenes, and only then shrank to the launcher's
	/// size when <c>ClientLauncher.Awake</c> ran (issue #221). Requesting the launcher size from a
	/// <see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/> hook corrects it during the splash
	/// screen's first frame instead, and does so whatever mode the window was created in — so it
	/// holds after a crash, which the exit-time restore below cannot.</para>
	///
	/// <para><b>The exit-time restore</b> is what makes a normal launch flash-free rather than
	/// one-frame-flash: Unity records the mode in force at a clean quit, so returning to the
	/// launcher's windowed size a frame before the process ends means the next window is created
	/// in it. A crash skips this and the boot-time correction above carries that launch.</para>
	///
	/// <para><b>Standalone builds only.</b> The editor and WebGL boot straight into the post-boot
	/// scene, so there is no launcher to size for; a headless server has no window at all. On
	/// those paths <see cref="IsActive"/> stays false and the saved game display mode is applied
	/// at boot as before.</para>
	/// </remarks>
	public static class LauncherWindow
	{
		/// <summary>Launcher window width the first time it opens on a machine.</summary>
		/// <remarks>
		/// Matches the Player Settings default window so a fresh install's first window, splash
		/// screen included, is already the launcher's size.
		/// </remarks>
		public const int DefaultWidth = 1024;
		/// <summary>Launcher window height the first time it opens on a machine.</summary>
		public const int DefaultHeight = 768;
		/// <summary>
		/// Smallest window the launcher layout stays usable at. Mirrors the min-width and
		/// min-height in UILauncher.uss — below this the footer buttons start to be squeezed.
		/// </summary>
		public const int MinWidth = 480;
		public const int MinHeight = 360;

		/// <summary>
		/// True when this process booted through the launcher and this class has sized the window
		/// for it. False in the editor, on WebGL and on a server.
		/// </summary>
		public static bool IsActive { get; private set; }

		/// <summary>
		/// True when the display's own mode was read successfully at boot.
		/// </summary>
		public static bool NativeModeCaptured { get; private set; }
		/// <summary>
		/// The display's resolution as reported before the window was first resized.
		/// </summary>
		public static Vector2Int NativeSize { get; private set; }
		/// <summary>
		/// The display's refresh rate as reported before the window was first resized.
		/// </summary>
		public static RefreshRate NativeRefreshRate { get; private set; }

		/// <summary>
		/// Clears the per-session state. Statics survive a play-mode session when domain reload
		/// is disabled; nothing here is ever set in the editor, but the reset is cheap and keeps
		/// the class honest if that changes.
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState()
		{
			IsActive = false;
			NativeModeCaptured = false;
			NativeSize = Vector2Int.zero;
			NativeRefreshRate = default;
		}

#if !UNITY_EDITOR && !UNITY_WEBGL && !UNITY_SERVER
		/// <summary>
		/// Sizes the window for the launcher before the first scene loads.
		/// </summary>
		/// <remarks>
		/// Runs before <c>ClientSettingsBootstrap</c> may or may not have loaded the configuration
		/// — the order of two <c>BeforeSceneLoad</c> hooks is unspecified — so the store is loaded
		/// here as well. <c>EnsureLoaded</c> is idempotent and both go through
		/// <c>ClientSettings</c>, so there is still exactly one store.
		/// <para>
		/// <c>UnityEngine.Debug</c> rather than <c>FishMMO.Logging</c>: the logging system is
		/// initialised by <c>MainBootstrapSystem</c> during the first scene's Awake, which has not
		/// happened yet.
		/// </para>
		/// </remarks>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void ApplyAtBoot()
		{
			try
			{
				CaptureNativeMode();
				LauncherSettings.EnsureLoaded();
				ApplyLauncherMode();
				IsActive = true;

				/* Subscribed before unsubscribed so a stale subscription from a previous session
				 * without a domain reload cannot double up. Removing an absent handler is a no-op. */
				MainBootstrapSystem.OnClientShutdownStarting -= RestoreForNextLaunch;
				MainBootstrapSystem.OnClientShutdownStarting += RestoreForNextLaunch;
			}
			catch (Exception ex)
			{
				// The launcher's Awake retries the resize; a failure here must not stop boot.
				Debug.LogWarning($"[LauncherWindow] Sizing the window for the launcher at boot failed: {ex.Message}");
			}
		}
#endif

		/// <summary>
		/// Reads the display's own resolution and refresh rate.
		/// </summary>
		/// <remarks>
		/// Taken before the window is resized, not after. This is the mode the game is given at
		/// hand-off when the player has no saved display mode of their own, so it has to be the
		/// display's, not whatever the windowed launcher leaves the window at.
		/// </remarks>
		private static void CaptureNativeMode()
		{
			Resolution native = Screen.currentResolution;
			NativeSize = new Vector2Int(native.width, native.height);
			NativeRefreshRate = native.refreshRateRatio;
			NativeModeCaptured = NativeSize.x > 0 && NativeSize.y > 0;
		}

		/// <summary>
		/// The size the launcher window should open at: the size the player last used, or the
		/// default the first time.
		/// </summary>
		/// <remarks>
		/// The window is resizable, so pinning it to a fixed size on every launch would undo the
		/// player's choice each time. The stored size is clamped against the current display by
		/// <see cref="LauncherSettings.GetWindowSize"/>, which matters when a window saved on a
		/// larger monitor is restored on a smaller one.
		/// </remarks>
		public static Vector2Int ResolveWindowSize()
		{
			Vector2Int stored = LauncherSettings.GetWindowSize(MinWidth, MinHeight);
			return new Vector2Int(
				stored.x > 0 ? stored.x : DefaultWidth,
				stored.y > 0 ? stored.y : DefaultHeight);
		}

		/// <summary>
		/// Whether the window is already windowed at the launcher's size, so a resize would be a
		/// no-op that still costs a mode switch.
		/// </summary>
		private static bool IsInLauncherMode(Vector2Int size)
		{
			return Screen.fullScreenMode == FullScreenMode.Windowed
				&& Screen.width == size.x
				&& Screen.height == size.y;
		}

		/// <summary>
		/// Puts the window into the launcher's windowed size, unless it is already there.
		/// </summary>
		/// <remarks>
		/// Idempotent so it can be requested at boot and again from the launcher's Awake as a
		/// fallback: the second call finds the mode already in force and issues nothing, so the
		/// player never sees a second resize.
		/// </remarks>
		public static void ApplyLauncherMode()
		{
			Vector2Int size = ResolveWindowSize();
			if (IsInLauncherMode(size))
			{
				return;
			}
			SetScreenResolution(size.x, size.y, FullScreenMode.Windowed, Screen.currentResolution.refreshRateRatio);
		}

		/// <summary>
		/// Returns the window to the launcher's size before the process exits, so the next
		/// launch is created in it.
		/// </summary>
		/// <remarks>
		/// Raised by <see cref="MainBootstrapSystem"/> at the start of shutdown, which then lets a
		/// frame pass before quitting: <c>Screen.SetResolution</c> takes effect at the end of the
		/// frame it is requested in, and Unity records the mode in force when it quits. A quit
		/// from inside the launcher finds the window already in this mode and does nothing.
		/// </remarks>
		private static void RestoreForNextLaunch()
		{
			if (!IsActive)
			{
				return;
			}
			try
			{
				ApplyLauncherMode();
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[LauncherWindow] Restoring the launcher window size before exit failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Applies a display mode, tolerating a refresh rate this display will not accept.
		/// </summary>
		/// <remarks>
		/// Some display configurations report a refresh rate <c>Screen.SetResolution</c> then
		/// rejects. The resolution and mode are what matter here, so a rejected rate falls back
		/// to 60 Hz rather than leaving the window at whatever it happened to be.
		/// </remarks>
		public static void SetScreenResolution(int width, int height, FullScreenMode mode, RefreshRate rate)
		{
			try
			{
				Screen.SetResolution(width, height, mode, rate);
			}
			catch
			{
				Screen.SetResolution(width, height, mode, new RefreshRate() { numerator = 60, denominator = 1 });
			}
		}
	}
}
