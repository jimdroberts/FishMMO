using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
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
	///
	/// <para><b>Brightness drives two properties, because which one is read depends on the
	/// scene.</b> <see cref="RenderSettings.ambientLight"/> is consulted only under
	/// <c>AmbientMode.Flat</c>. Every world scene in this project is authored
	/// <c>AmbientMode.Skybox</c>, where ambient comes from the skybox's spherical harmonics scaled
	/// by <see cref="RenderSettings.ambientIntensity"/> and <c>ambientLight</c> is ignored outright
	/// — so a slider that wrote only <c>ambientLight</c> did nothing at all anywhere the player
	/// actually plays. It appeared to work while testing, because the login and preboot scenes are
	/// authored Flat. Both are written, so the setting has an effect whichever mode a scene uses
	/// and stays correct if one is re-authored.</para>
	///
	/// <para>This is an <em>ambient</em> control and not an exposure control: it scales indirect
	/// light, leaving direct lights and the skybox itself alone. A true gamma control would need a
	/// URP Volume carrying a Color Adjustments override, which is a rendering asset rather than a
	/// setting.</para>
	/// </remarks>
	public static class ClientDisplaySettings
	{
		/// <summary>Lowest brightness the slider offers.</summary>
		public const float MinimumBrightness = 0.0f;
		/// <summary>Highest brightness the slider offers.</summary>
		public const float MaximumBrightness = 1.0f;
		/// <summary>
		/// Brightness used when the player has never chosen one.
		/// </summary>
		/// <remarks>
		/// Mid-scale rather than maximum. This scales indirect light (see the class remarks), so
		/// 1.0 is the top of the slider with no headroom left to raise it — a player who finds the
		/// world too bright can only go down, and one who finds it too dark has nowhere to go. Half
		/// way leaves adjustment in both directions.
		///
		/// Only applies to a player who has never set a brightness; an existing saved value is
		/// read in preference to this and is not overwritten.
		/// </remarks>
		public const float DefaultBrightness = 0.5f;

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

			/* Not the display mode when the launcher owns the window. The launcher and the game
			 * share one process, and this runs during the first scene's Awake — before the launcher
			 * scene has even loaded. Applying the saved game mode here put the window into
			 * fullscreen behind the splash screen only for the launcher to shrink it again seconds
			 * later (issue #221). The launcher applies the same saved mode itself at hand-off, in
			 * ClientLauncher.RestoreGameDisplayMode, so nothing is lost by waiting. The editor and
			 * WebGL boot straight into the game, and keep the boot-time apply. */
			if (!LauncherWindow.IsActive)
			{
				ApplySavedDisplayMode();
			}

			ApplySavedVSync();
			ApplySavedAnisotropicFiltering();
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

		/// <summary>
		/// Texture filtering modes offered to the player, in the order the dropdown shows them.
		/// </summary>
		/// <remarks>
		/// Our own enum rather than Unity's <see cref="AnisotropicFiltering"/>, for the same reason
		/// the antialiasing setting keeps its own: the stored value is an ordinal in a save file,
		/// and persisting a framework enum means a reordering upstream silently changes what an
		/// existing player's setting means.
		/// </remarks>
		public enum AnisotropicOption
		{
			/// <summary>No anisotropic filtering. Ground textures blur at glancing angles.</summary>
			Off = 0,

			/// <summary>Whatever level each texture was imported with. The authored intent.</summary>
			PerTexture = 1,

			/// <summary>Forced on for every texture, regardless of how it was imported.</summary>
			Forced = 2,
		}

		/// <summary>
		/// Texture filtering when nothing has been chosen.
		/// </summary>
		/// <remarks>
		/// Per-texture, which respects what the art was imported with rather than overriding it.
		/// Forcing it on costs little on any modern GPU but is still a decision about someone
		/// else's art, so it is offered rather than imposed.
		/// </remarks>
		public const AnisotropicOption DefaultAnisotropicFiltering = AnisotropicOption.PerTexture;

		/// <summary>
		/// Applies the stored anisotropic filtering mode.
		/// </summary>
		public static void ApplySavedAnisotropicFiltering()
		{
			int stored = Mathf.Clamp(
				ClientSettings.GetInt(
					ClientSettings.AnisotropicFilteringKey,
					(int)DefaultAnisotropicFiltering),
				(int)AnisotropicOption.Off,
				(int)AnisotropicOption.Forced);

			ApplyAnisotropicFiltering((AnisotropicOption)stored);
		}

		/// <summary>
		/// Writes an anisotropic filtering mode into the quality settings.
		/// </summary>
		/// <param name="option">The mode to apply. An unrecognised value falls back to per-texture.</param>
		public static void ApplyAnisotropicFiltering(AnisotropicOption option)
		{
			switch (option)
			{
				case AnisotropicOption.Off:
					QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
					break;
				case AnisotropicOption.Forced:
					QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
					break;
				case AnisotropicOption.PerTexture:
				default:
					QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
					break;
			}
		}

		/// <summary>Applies the saved VSync preference.</summary>
		public static void ApplySavedVSync()
		{
			ApplyVSync(ClientSettings.GetBool(ClientSettings.VSyncKey, false));
		}

		/// <summary>
		/// Writes a VSync preference into the active quality level.
		/// </summary>
		/// <param name="enabled">True to wait for the display's refresh.</param>
		/// <remarks>
		/// Every VSync write goes through here so it cannot happen without the editor safeguard in
		/// <see cref="CaptureAuthoredQuality"/> having run first.
		/// </remarks>
		public static void ApplyVSync(bool enabled)
		{
			CaptureAuthoredQuality();
			QualitySettings.vSyncCount = enabled ? 1 : 0;
		}

		/// <summary>
		/// Switches to a quality level and puts the player's VSync preference back on top of it.
		/// </summary>
		/// <param name="index">Index into <see cref="QualitySettings.names"/>.</param>
		/// <param name="applyExpensiveChanges">
		/// True to let Unity re-create render targets and reload textures. Worth it when the player
		/// is watching and waiting for the result; not during boot, where the stall reads as a hang.
		/// </param>
		/// <remarks>
		/// <c>SetQualityLevel</c> installs that level's own authored <c>vSyncCount</c>, which is a
		/// property of the level and has nothing to do with what the player chose — so the
		/// preference is re-applied afterwards. Doing that here rather than at each call site is
		/// what stops the two from being forgotten separately.
		/// </remarks>
		public static void ApplyQualityLevel(int index, bool applyExpensiveChanges)
		{
			CaptureAuthoredQuality();
			QualitySettings.SetQualityLevel(index, applyExpensiveChanges);

			/* Both of these are authored per quality level, so switching level overwrites whatever
			 * the player chose. Restoring them here is what makes them preferences rather than
			 * things that silently revert the next time somebody touches the quality dropdown. */
			ApplySavedVSync();
			ApplySavedAnisotropicFiltering();
		}

#if UNITY_EDITOR
		/// <summary>The quality level and VSync count authored in the project, before any change.</summary>
		private static int authoredQualityLevel;
		private static int authoredVSyncCount;

		/// <summary>The anisotropic filtering mode authored in the project, before any change.</summary>
		private static AnisotropicFiltering authoredAnisotropicFiltering;

		/// <summary>True once the authored values above have been captured.</summary>
		private static bool hasAuthoredQuality;
#endif

		/// <summary>
		/// Remembers the project's authored quality settings so play mode can put them back.
		/// </summary>
		/// <remarks>
		/// <para><b>Editor only, and not optional there.</b> <c>QualitySettings</c> is a project
		/// asset, and a value written into it at play time stays written — exactly the trap
		/// <see cref="UITKPanelScale"/> already documents for <c>PanelSettings</c>. Running the
		/// client once was enough to leave <c>m_CurrentQuality</c> and the active level's
		/// <c>vSyncCount</c> modified in <c>ProjectSettings/QualitySettings.asset</c>, so a
		/// developer's checked-out project picked up a source-control change describing whatever
		/// the last person to press Play happened to have saved in their own Configuration.cfg —
		/// and committing it would ship one player's preference as everybody's default.</para>
		/// <para>Nothing happens in a build: a player changing their own quality level is the
		/// entire point, and there is no asset to protect.</para>
		/// <para><b>Called from the boot phase before anything writes.</b> The first write is not
		/// this class's — <c>MainBootstrapSystem</c> forces <c>vSyncCount</c> to zero during the
		/// first scene's Awake so its frame-rate cap is not ignored. Capturing lazily on the first
		/// write here would therefore record that zero as the authored value and "restore" it,
		/// which is worse than not restoring at all. <c>ClientSettingsBootstrap.Initialize</c>
		/// calls this at BeforeSceneLoad, ahead of both.</para>
		/// </remarks>
		internal static void CaptureAuthoredQuality()
		{
#if UNITY_EDITOR
			if (hasAuthoredQuality)
			{
				return;
			}

			authoredQualityLevel = QualitySettings.GetQualityLevel();
			authoredVSyncCount = QualitySettings.vSyncCount;
			authoredAnisotropicFiltering = QualitySettings.anisotropicFiltering;
			hasAuthoredQuality = true;

			UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
		}

#if UNITY_EDITOR
		/// <summary>Puts the authored quality settings back when play mode ends.</summary>
		private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
		{
			if (change != UnityEditor.PlayModeStateChange.ExitingPlayMode)
			{
				return;
			}

			if (hasAuthoredQuality)
			{
				QualitySettings.SetQualityLevel(authoredQualityLevel, false);
				QualitySettings.vSyncCount = authoredVSyncCount;
				QualitySettings.anisotropicFiltering = authoredAnisotropicFiltering;
			}

			UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

			// The statics survive a play-mode cycle when domain reload is disabled.
			hasAuthoredQuality = false;
		}
#endif

		/// <summary>
		/// Applies the saved render frame-rate cap.
		/// </summary>
		/// <remarks>
		/// Deliberately runs after the bootstrap system has installed its own menu-time cap. That
		/// cap is the default for a client with no preference — and
		/// <see cref="ResolveSavedFrameRate"/> resolves to the same number, so running afterwards
		/// re-applies it rather than replacing it. A player who has chosen a cap gets theirs, and
		/// getting the order backwards is indistinguishable from the setting not being saved at
		/// all.
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
		/// Writes a brightness level into the scene's ambient lighting and remembers it.
		/// </summary>
		/// <param name="value">Brightness in the range 0..1. Clamped.</param>
		public static void ApplyBrightness(float value)
		{
			appliedBrightness = float.IsNaN(value) ? DefaultBrightness : Mathf.Clamp01(value);
			WriteBrightnessToScene();
		}

		/// <summary>
		/// Pushes <see cref="appliedBrightness"/> onto the active scene's lighting.
		/// </summary>
		/// <remarks>
		/// Both properties, because the one that is read depends on the scene's ambient mode — see
		/// the class remarks. Shared between the setter and the scene hook so the two cannot drift:
		/// the hook used to write <c>ambientLight</c> alone, which meant that even once brightness
		/// worked it stopped working again after the first scene load.
		/// </remarks>
		private static void WriteBrightnessToScene()
		{
			float clamped = appliedBrightness;

			// AmbientMode.Flat reads this; every other mode ignores it.
			RenderSettings.ambientLight = new Color(clamped, clamped, clamped, clamped);

			// AmbientMode.Skybox and Trilight scale their ambient by this; Flat ignores it.
			RenderSettings.ambientIntensity = clamped;
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
				 * happens during boot. The level still changes; the panel applies it properly when
				 * the player picks one. ApplyQualityLevel puts the VSync preference back. */
				ApplyQualityLevel(i, false);
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
			WriteBrightnessToScene();
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
		/// The frame-rate cap to apply, given the caps this machine offers.
		/// </summary>
		/// <remarks>
		/// <para>Three cases, and they are deliberately different from one another.</para>
		/// <para><b>No preference at all</b> — a fresh install — keeps the boot-time menu cap of
		/// <see cref="MainBootstrapSystem.BootstrapTargetFrameRate"/>. This used to return the
		/// display's fastest mode instead, which made the bootstrap cap dead on arrival: it was
		/// installed before the first frame and replaced microseconds later by the settings apply,
		/// so a fresh install rendered its launcher and login screens as fast as the panel allowed
		/// and pegged a core drawing a static menu. Somebody with no opinion about frame rate
		/// should get the modest default, not the maximum.</para>
		/// <para><b>A saved value this machine offers</b> is honoured exactly.</para>
		/// <para><b>A saved value it does not offer</b> falls back to the fastest available, and
		/// not to the default: that is the player who moved the game to a different monitor, and
		/// they did express a preference — the old number is just meaningless on the new hardware,
		/// so the closest thing to "as fast as I asked for" is the most this display can do.</para>
		/// </remarks>
		public static int ResolveSavedFrameRate(List<int> choices)
		{
			int saved = ClientSettings.GetInt(ClientSettings.FrameRateKey, 0);
			if (saved <= 0)
			{
				return ResolveDefaultFrameRate(choices);
			}

			if (choices.Contains(saved))
			{
				return saved;
			}

			return choices[choices.Count - 1];
		}

		/// <summary>
		/// The default cap, snapped onto a rate this machine actually offers.
		/// </summary>
		/// <param name="choices">The selectable caps, ascending.</param>
		/// <remarks>
		/// The default cannot simply be returned as written. <see cref="BuildFrameRateChoices"/>
		/// bounds the ladder below by the network tick rate and above by the display's fastest
		/// mode, so on a 50 Hz panel — or a build whose tick rate is raised past 60 — the default
		/// is not among the offered values, and returning it would put a number in the dropdown
		/// that the dropdown cannot represent. The largest offered rate not exceeding the default
		/// is the honest answer: no faster than intended, and always something the player can see
		/// selected.
		/// </remarks>
		private static int ResolveDefaultFrameRate(List<int> choices)
		{
			int resolved = choices[0];
			for (int i = 0; i < choices.Count; ++i)
			{
				if (choices[i] > MainBootstrapSystem.BootstrapTargetFrameRate)
				{
					break;
				}
				resolved = choices[i];
			}
			return resolved;
		}
	}
}
