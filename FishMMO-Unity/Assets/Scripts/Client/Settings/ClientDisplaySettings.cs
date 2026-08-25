using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Everything the client knows about display modes: which ones the hardware offers, which one
	/// the player saved, and how to put one into effect.
	/// </summary>
	/// <remarks>
	/// <para>Split out of the options panel so the two callers that need it can share one
	/// implementation. The panel needs the option lists to build its dropdowns; the boot phase
	/// needs the resolve-and-apply half and nothing else. While this logic lived inside the panel,
	/// the boot phase had no way to reach it — which is why a saved resolution was never applied
	/// at start-up, only ever by pressing Apply in a panel the player had to open first.</para>
	///
	/// <para><b>Brightness is re-applied on every scene load.</b>
	/// <see cref="RenderSettings.ambientLight"/> is per-scene state baked into the lighting
	/// settings of whichever scene is active, so loading a scene discards it. The client loads
	/// several — launcher, login, world, and one per world scene transfer — so a brightness set
	/// once at boot survived only until the first load.</para>
	/// </remarks>
	public static class ClientDisplaySettings
	{
		/// <summary>Lowest brightness the slider offers.</summary>
		public const float MinimumBrightness = 0.0f;
		/// <summary>Highest brightness the slider offers.</summary>
		public const float MaximumBrightness = 1.0f;
		/// <summary>Brightness used when the player has never chosen one.</summary>
		public const float DefaultBrightness = 1.0f;

		/// <summary>
		/// The brightness currently in force, kept so a scene load can put it back without
		/// re-reading and re-clamping the configuration file.
		/// </summary>
		private static float appliedBrightness = DefaultBrightness;

		// ── Applying ────────────────────────────────────────────────

		/// <summary>
		/// Applies every saved display setting: mode, VSync, frame-rate cap and brightness.
		/// </summary>
		/// <remarks>
		/// Order matters in one place only. VSync is written before the frame-rate cap because
		/// <see cref="Application.targetFrameRate"/> is ignored outright whenever the active
		/// quality level has vSync enabled — so a cap applied first is silently discarded by a
		/// VSync value applied second.
		/// </remarks>
		public static void ApplySaved()
		{
			ApplySavedQualityLevel();
			ApplySavedDisplayMode();
			ApplySavedVSync();
			ApplySavedFrameRate();
			ApplySavedBrightness();
			InstallSceneHook();
		}

		/// <summary>
		/// Puts the saved resolution, refresh rate and fullscreen mode into effect.
		/// </summary>
		/// <remarks>
		/// Nothing happens when no complete display mode has been saved. That distinction matters:
		/// a fresh install must keep whatever mode the player launched in — forcing one of the
		/// enumerated modes on first run would resize a window the player had already sized, and
		/// on a multi-monitor setup can move it to a different screen.
		/// </remarks>
		public static void ApplySavedDisplayMode()
		{
#if !UNITY_WEBGL
			if (!TryResolveSavedDisplayMode(out Vector2Int size, out RefreshRate rate, out FullScreenMode mode))
			{
				return;
			}

			/* One call, not three. Setting resolution, mode and refresh rate separately means each
			 * call reads back whatever the previous one left and they undo one another. */
			Screen.SetResolution(size.x, size.y, mode, rate);
#endif
		}

		/// <summary>
		/// Reads the saved display mode, rejecting anything this display cannot present.
		/// </summary>
		/// <param name="size">The saved resolution.</param>
		/// <param name="rate">The saved refresh rate, or the display's own when none was saved.</param>
		/// <param name="mode">The saved fullscreen mode.</param>
		/// <returns>True when a complete, supported mode was found.</returns>
		/// <remarks>
		/// A saved resolution the display no longer offers is refused rather than approximated.
		/// The most common way to get one is moving the install to a different machine, and
		/// forcing an unsupported mode there is exactly the failure the options panel's
		/// confirmation countdown exists to protect against — except at boot, where there is no
		/// countdown and no panel.
		/// </remarks>
		public static bool TryResolveSavedDisplayMode(out Vector2Int size, out RefreshRate rate, out FullScreenMode mode)
		{
			size = default;
			rate = default;
			mode = default;

			int width = ClientSettings.GetInt(ClientSettings.ResolutionWidthKey, 0);
			int height = ClientSettings.GetInt(ClientSettings.ResolutionHeightKey, 0);
			if (width <= 0 || height <= 0)
			{
				return false;
			}

			List<Vector2Int> resolutions = BuildResolutionOptions();
			size = new Vector2Int(width, height);
			if (!resolutions.Contains(size))
			{
				Log.Warning("ClientDisplaySettings",
					$"Saved resolution {width}x{height} is not offered by this display; keeping the current mode.");
				return false;
			}

			List<FullScreenMode> modes = BuildFullscreenOptions();
			mode = (FullScreenMode)ClientSettings.GetInt(ClientSettings.FullscreenKey, (int)FullScreenMode.FullScreenWindow);
			if (!modes.Contains(mode))
			{
				mode = modes[0];
			}

			List<RefreshRate> rates = BuildRefreshRateOptions(size);
			int savedHz = ClientSettings.GetInt(ClientSettings.RefreshRateKey, 0);
			rate = rates[rates.Count - 1];
			for (int i = 0; i < rates.Count; ++i)
			{
				if (Mathf.RoundToInt(ToHz(rates[i])) == savedHz)
				{
					rate = rates[i];
					break;
				}
			}

			return true;
		}

		/// <summary>Applies the saved VSync preference.</summary>
		public static void ApplySavedVSync()
		{
			QualitySettings.vSyncCount = ClientSettings.GetBool(ClientSettings.VSyncKey, false) ? 1 : 0;
		}

		/// <summary>
		/// Applies the saved render frame-rate cap.
		/// </summary>
		/// <remarks>
		/// Deliberately runs after the bootstrap system has installed its own menu-time cap. That
		/// cap is a default for a client with no preference; a player who has chosen one should
		/// get theirs, and getting the order backwards is indistinguishable from the setting not
		/// being saved at all.
		/// </remarks>
		public static void ApplySavedFrameRate()
		{
			List<int> choices = BuildFrameRateChoices();
			Client.ApplyTargetFrameRate(ResolveSavedFrameRate(choices));
		}

		/// <summary>Applies the saved brightness to the scene's ambient light.</summary>
		public static void ApplySavedBrightness()
		{
			ApplyBrightness(ClientSettings.GetFloat(
				ClientSettings.BrightnessKey, DefaultBrightness, MinimumBrightness, MaximumBrightness));
		}

		/// <summary>
		/// Writes a brightness level into the scene's ambient light and remembers it.
		/// </summary>
		/// <param name="value">Brightness in the range 0..1. Clamped.</param>
		public static void ApplyBrightness(float value)
		{
			float clamped = float.IsNaN(value) ? DefaultBrightness : Mathf.Clamp01(value);
			appliedBrightness = clamped;
			RenderSettings.ambientLight = new Color(clamped, clamped, clamped, clamped);
		}

		/// <summary>
		/// Applies the saved quality level, matched by name.
		/// </summary>
		public static void ApplySavedQualityLevel()
		{
			string saved = ClientSettings.GetString(ClientSettings.QualityLevelKey, string.Empty);
			if (string.IsNullOrEmpty(saved))
			{
				return;
			}

			string[] names = QualitySettings.names;
			for (int i = 0; i < names.Length; ++i)
			{
				if (!string.Equals(names[i], saved, System.StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				/* applyExpensiveChanges: false. The expensive half re-creates render targets and
				 * reloads textures, which stalls for long enough to be mistaken for a hang when it
				 * happens during boot. The level still changes; the caller applies it properly
				 * when the player picks one from the panel. */
				QualitySettings.SetQualityLevel(i, false);

				/* SetQualityLevel installs that level's own vSyncCount, which is authored per
				 * level and has nothing to do with what the player chose. Put theirs back. */
				ApplySavedVSync();
				return;
			}

			Log.Warning("ClientDisplaySettings",
				$"Saved quality level '{saved}' does not exist in this build; keeping the current level.");
		}

		/// <summary>
		/// Re-applies brightness after every scene load.
		/// </summary>
		/// <remarks>
		/// Idempotent, and installed rather than assumed: the client loads scenes from several
		/// places (bootstrap, login, world entry, world scene transfer) and none of them is a
		/// single choke point this could hang off instead.
		/// </remarks>
		private static void InstallSceneHook()
		{
			/* Unsubscribe first rather than track a bool. A bool guard is wrong in the editor with
			 * domain reload disabled: the flag survives into the next play session while the
			 * subscription itself does not, so the hook would silently never be installed again and
			 * brightness would stop surviving scene loads for the rest of the editor's life.
			 * Removing a handler that is not attached is a no-op, so this is exact in both cases. */
			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		/// <summary>Puts the player's brightness back over whatever the new scene baked in.</summary>
		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			RenderSettings.ambientLight = new Color(appliedBrightness, appliedBrightness, appliedBrightness, appliedBrightness);
		}

		// ── Option lists ────────────────────────────────────────────

		/// <summary>
		/// The distinct width/height pairs this display supports, smallest first.
		/// </summary>
		/// <remarks>
		/// Deduplicated. <c>Screen.resolutions</c> returns one entry per
		/// width/height/refresh-rate combination, so a monitor offering three refresh rates lists
		/// every resolution three times.
		/// </remarks>
		public static List<Vector2Int> BuildResolutionOptions()
		{
			List<Vector2Int> options = new List<Vector2Int>();

			Resolution[] resolutions = Screen.resolutions;
			for (int i = 0; i < resolutions.Length; ++i)
			{
				Vector2Int size = new Vector2Int(resolutions[i].width, resolutions[i].height);
				if (!options.Contains(size))
				{
					options.Add(size);
				}
			}

			if (options.Count == 0)
			{
				// Headless, or an unusual display: offer at least the current window size.
				options.Add(new Vector2Int(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height)));
			}

			return options;
		}

		/// <summary>
		/// The fullscreen modes this platform offers, in the order they are presented.
		/// </summary>
		/// <remarks>
		/// The list is built conditionally, so an index into it is not the
		/// <see cref="FullScreenMode"/> value — on a build without exclusive fullscreen the second
		/// entry is <c>MaximizedWindow</c>. The stored key is always the enum value.
		/// </remarks>
		public static List<FullScreenMode> BuildFullscreenOptions()
		{
			List<FullScreenMode> options = new List<FullScreenMode>();
#if !UNITY_WEBGL
			options.Add(FullScreenMode.FullScreenWindow);
#if UNITY_STANDALONE_WIN
			options.Add(FullScreenMode.ExclusiveFullScreen);
#endif
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
			options.Add(FullScreenMode.MaximizedWindow);
#endif
#if UNITY_STANDALONE || UNITY_EDITOR
			options.Add(FullScreenMode.Windowed);
#endif
#endif
			if (options.Count == 0)
			{
				options.Add(FullScreenMode.FullScreenWindow);
			}
			return options;
		}

		/// <summary>The refresh rates this display offers at a given resolution, slowest first.</summary>
		public static List<RefreshRate> BuildRefreshRateOptions(Vector2Int size)
		{
			List<RefreshRate> options = new List<RefreshRate>();

			Resolution[] resolutions = Screen.resolutions;
			for (int i = 0; i < resolutions.Length; ++i)
			{
				if (resolutions[i].width != size.x || resolutions[i].height != size.y)
				{
					continue;
				}

				RefreshRate rate = resolutions[i].refreshRateRatio;
				bool duplicate = false;
				for (int j = 0; j < options.Count; ++j)
				{
					if (Mathf.Approximately(ToHz(options[j]), ToHz(rate)))
					{
						duplicate = true;
						break;
					}
				}
				if (!duplicate)
				{
					options.Add(rate);
				}
			}

			options.Sort((a, b) => ToHz(a).CompareTo(ToHz(b)));

			if (options.Count == 0)
			{
				options.Add(Screen.currentResolution.refreshRateRatio);
			}

			return options;
		}

		/// <summary>Converts a refresh-rate ratio to hertz.</summary>
		public static float ToHz(RefreshRate rate)
		{
			return rate.denominator == 0 ? 0.0f : (float)rate.numerator / rate.denominator;
		}

		/// <summary>The full ladder of frame-rate caps, before the display and tick bounds.</summary>
		private static readonly int[] frameRateLadder =
		{
			30, 60, 75, 90, 120, 144, 165, 180, 240, 300, 360, 480, 500
		};

		/// <summary>
		/// The selectable frame-rate caps for this machine, ascending.
		/// </summary>
		/// <remarks>
		/// <para>The floor is the network tick rate. FishNet derives ticks from the update loop, so
		/// a frame rate below the tick rate cannot deliver them on schedule and the client falls
		/// behind the server's timeline — offering such a value lets a player break their own
		/// connection from a settings menu.</para>
		/// <para>The ceiling is the display's fastest mode; frames produced faster than the panel
		/// can present them are discarded at scan-out.</para>
		/// <para>The display's own rate is always included even when it is not a ladder value —
		/// 165 Hz and 59.94 Hz panels both exist.</para>
		/// </remarks>
		public static List<int> BuildFrameRateChoices()
		{
			int minimum = Client.ResolveMinimumFrameRate();
			int maximum = Mathf.Max(minimum, Client.ResolveMaximumFrameRate());

			List<int> choices = new List<int>(frameRateLadder.Length + 1);
			for (int i = 0; i < frameRateLadder.Length; ++i)
			{
				int rate = frameRateLadder[i];
				if (rate >= minimum && rate <= maximum)
				{
					choices.Add(rate);
				}
			}

			if (!choices.Contains(maximum))
			{
				choices.Add(maximum);
				choices.Sort();
			}

			if (choices.Count == 0)
			{
				choices.Add(Mathf.Clamp(minimum, Client.MinimumTargetFrameRate, Client.MaximumTargetFrameRate));
			}

			return choices;
		}

		/// <summary>
		/// The frame-rate cap to apply: the player's saved value, or the display's own rate.
		/// </summary>
		/// <remarks>
		/// A saved value that is no longer offered falls back to the fastest available rather than
		/// being honoured. That is the case where the player has moved the game to a different
		/// monitor or the tick rate has changed: the old number is meaningless on the new hardware.
		/// </remarks>
		public static int ResolveSavedFrameRate(List<int> choices)
		{
			int saved = ClientSettings.GetInt(ClientSettings.FrameRateKey, 0);
			if (saved > 0 && choices.Contains(saved))
			{
				return saved;
			}
			return choices[choices.Count - 1];
		}
	}
}
