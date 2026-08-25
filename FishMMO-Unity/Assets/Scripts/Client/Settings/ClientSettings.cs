using System;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// The client's single owner of <see cref="Configuration.GlobalSettings"/>: it creates the
	/// store, names every key exactly once, clamps everything read out of it, and owns the one
	/// debounced write that puts it back on disk.
	/// </summary>
	/// <remarks>
	/// <para><b>Why one owner.</b> The store used to be created lazily by whichever of two
	/// unrelated places asked for it first — <c>LauncherSettings.EnsureLoaded</c> in the launcher
	/// scene, and <c>UITKOptions.EnsureConfigurationLoaded</c> the first time the player opened
	/// the settings panel. In a client started past the launcher, neither ran, so
	/// <c>Configuration.GlobalSettings</c> stayed null through the whole of boot: keybinding
	/// overrides were skipped without a word (<c>PlayerInputController.LoadBindingOverrides</c>
	/// returns early on a null store), panel positions were never restored, and the theme loaded
	/// from nothing. Every one of those failures looked like a setting that had not been saved
	/// rather than one that had never been read.</para>
	///
	/// <para><b>Why the keys live here.</b> A configuration key is a string shared between the
	/// control that writes it and the code that applies it, and the two are usually in different
	/// files. Naming them in one place is what makes it impossible for a panel to write
	/// <c>"Brightness"</c> while the applier reads <c>"brightness"</c> — and lets a setting be
	/// applied at boot by code that has never heard of the options panel.</para>
	///
	/// <para><b>Why every read clamps.</b> Configuration.cfg is a plain text file: a player can
	/// edit it, a crash can truncate it mid-write, and a build from a different machine can leave
	/// values this one cannot honour. Values reach <c>RenderSettings.ambientLight</c>,
	/// <c>Screen.fullScreenMode</c> and <c>AudioListener.volume</c>, none of which validate what
	/// they are given. Clamping on the way in is the only place it can be done once.</para>
	///
	/// <para><b>Why writes are debounced.</b> <see cref="Configuration.Save"/> serialises and
	/// rewrites the entire file. A slider bound straight to it rewrites the file once per frame
	/// for as long as it is held. Everything here coalesces onto a short quiet period and is
	/// flushed on panel close, on quit, and on demand.</para>
	/// </remarks>
	public static class ClientSettings
	{
		// ── Display ─────────────────────────────────────────────────

		/// <summary>Configuration key for the VSync setting.</summary>
		public const string VSyncKey = "VSync";
		/// <summary>Configuration key for the render frame rate cap, in frames per second.</summary>
		public const string FrameRateKey = "Frame Rate Limit";
		/// <summary>Configuration key for the brightness setting.</summary>
		public const string BrightnessKey = "Brightness";
		/// <summary>Configuration key for the resolution width setting.</summary>
		public const string ResolutionWidthKey = "Resolution Width";
		/// <summary>Configuration key for the resolution height setting.</summary>
		public const string ResolutionHeightKey = "Resolution Height";
		/// <summary>Configuration key for the display refresh rate, in hertz.</summary>
		public const string RefreshRateKey = "Refresh Rate";
		/// <summary>Configuration key for the fullscreen mode, stored as the <see cref="FullScreenMode"/> value.</summary>
		public const string FullscreenKey = "Fullscreen";
		/// <summary>Configuration key for the quality level, stored as its name rather than its index.</summary>
		/// <remarks>
		/// The name and not the index: <c>QualitySettings</c> levels can be reordered or inserted
		/// between builds, and an index saved against the old order silently selects a different
		/// level. A name that no longer exists is simply ignored.
		/// </remarks>
		public const string QualityLevelKey = "Quality Level";

		// ── Audio ───────────────────────────────────────────────────

		/// <summary>Configuration key prefix for the per-channel audio volumes.</summary>
		/// <remarks>The full key is this prefix followed by the <see cref="AudioChannel"/> name.</remarks>
		public const string AudioVolumePrefix = "Audio.Volume.";
		/// <summary>Configuration key for muting the client while its window is not focused.</summary>
		public const string AudioMuteUnfocusedKey = "Audio.MuteWhenUnfocused";

		// ── Gameplay ────────────────────────────────────────────────

		/// <summary>Configuration key for showing floating damage numbers.</summary>
		public const string ShowDamageKey = "ShowDamage";
		/// <summary>Configuration key for showing floating healing numbers.</summary>
		public const string ShowHealsKey = "ShowHeals";
		/// <summary>Configuration key for showing achievement completion popups.</summary>
		/// <remarks>
		/// The key is <c>ShowAchievementCompletion</c> and not <c>ShowAchievements</c>, which is
		/// what the options panel used to write. Nothing read that name: the only consumer,
		/// <see cref="ClientCombatDisplay"/>, has always read <c>ShowAchievementCompletion</c>, so
		/// the toggle wrote one key and the popups read another and the setting did nothing at all.
		/// Naming it once, here, is what stops that recurring.
		/// </remarks>
		public const string ShowAchievementsKey = "ShowAchievementCompletion";
		/// <summary>Configuration key for suppressing party invitations.</summary>
		public const string IgnorePartyInvitesKey = "IgnorePartyInvites";
		/// <summary>Configuration key for suppressing guild invitations.</summary>
		public const string IgnoreGuildInvitesKey = "IgnoreGuildInvites";

		/// <summary>
		/// Every gameplay toggle: its configuration key, the label the options panel shows, and
		/// the value a fresh install gets.
		/// </summary>
		/// <remarks>
		/// The default belongs here rather than at each call site, because the panel and the code
		/// that acts on a setting have to agree about it. They did not:
		/// <see cref="ClientCombatDisplay"/> treated a missing key as <c>false</c> while the panel
		/// showed the same missing key as a ticked box, so a fresh install displayed no damage
		/// numbers while its own settings screen said it did — and ticking the box off and on again
		/// was the only way to make the display match the UI.
		/// </remarks>
		public static readonly (string Key, string Label, bool Default)[] GameplayToggles =
		{
			(ShowDamageKey,         "Show Damage Numbers",     true),
			(ShowHealsKey,          "Show Healing Numbers",    true),
			(ShowAchievementsKey,   "Show Achievement Popups", true),
			(IgnorePartyInvitesKey, "Ignore Party Invites",    false),
			(IgnoreGuildInvitesKey, "Ignore Guild Invites",    false),
		};

		/// <summary>
		/// Raised when any gameplay toggle changes.
		/// </summary>
		/// <remarks>
		/// Consumers cache these values — they are read on the client's hottest path, once per
		/// damage event per character in view — and a cache with nothing to invalidate it is a
		/// setting that appears not to work until the client is restarted.
		/// </remarks>
		public static event Action OnGameplayChanged;

		/// <summary>Reads a gameplay toggle, using the default declared in <see cref="GameplayToggles"/>.</summary>
		/// <param name="key">One of the keys in <see cref="GameplayToggles"/>.</param>
		public static bool GetGameplayToggle(string key)
		{
			for (int i = 0; i < GameplayToggles.Length; ++i)
			{
				if (string.Equals(GameplayToggles[i].Key, key, StringComparison.OrdinalIgnoreCase))
				{
					return GetBool(key, GameplayToggles[i].Default);
				}
			}

			/* An unknown key is a programming error, not a player one — every gameplay toggle is
			 * declared in the table above. False is the conservative reading: it suppresses a
			 * display rather than enabling behaviour nobody asked for. */
			Log.Warning("ClientSettings", $"'{key}' is not a declared gameplay toggle; treating it as off.");
			return false;
		}

		/// <summary>Writes a gameplay toggle, schedules a save, and notifies consumers.</summary>
		public static void SetGameplayToggle(string key, bool value)
		{
			Set(key, value);

			try
			{
				OnGameplayChanged?.Invoke();
			}
			catch (Exception ex)
			{
				Log.Error("ClientSettings", "A gameplay-settings subscriber threw.", ex);
			}
		}

		// ── Interface ───────────────────────────────────────────────

		/// <summary>Configuration key for the interface scale multiplier.</summary>
		public const string UIScaleKey = "UI.Scale";

		/// <summary>Configuration key for the keybinding override blob written by the input system.</summary>
		public const string InputBindingOverridesKey = "InputBindingOverrides";

		// ── Bounds ──────────────────────────────────────────────────

		/// <summary>Smallest interface scale offered, as a multiplier of the authored size.</summary>
		public const float MinimumUIScale = 0.75f;
		/// <summary>Largest interface scale offered, as a multiplier of the authored size.</summary>
		public const float MaximumUIScale = 1.5f;

		/// <summary>Seconds of quiet before a pending configuration write is flushed to disk.</summary>
		private const float SaveDebounceSeconds = 0.75f;

		/// <summary>True when the configuration store has been created and loaded.</summary>
		public static bool IsLoaded => Configuration.GlobalSettings != null;

		/// <summary>True while a write is owed to disk.</summary>
		private static bool savePending;

		/// <summary>Unscaled time at which the owed write is due.</summary>
		private static float saveDeadline;

		/// <summary>
		/// Creates and loads <see cref="Configuration.GlobalSettings"/> if nothing has yet.
		/// </summary>
		/// <returns>True when a usable store exists afterwards.</returns>
		/// <remarks>
		/// Safe to call from anywhere, at any point, any number of times — which is the point.
		/// Callers that merely need the store to exist should call this rather than constructing
		/// one, because a second <see cref="Configuration"/> pointed at the same file is how two
		/// halves of the client end up disagreeing about what the player chose.
		/// <para>
		/// A file that is present but unreadable is deliberately <b>not</b> overwritten. Only a
		/// genuinely absent file gets defaults written back; a load that failed for any other
		/// reason — a locked file, a permissions problem — leaves whatever is on disk alone, so a
		/// transient error cannot destroy a player's settings.
		/// </para>
		/// </remarks>
		public static bool EnsureLoaded()
		{
			if (Configuration.GlobalSettings != null)
			{
				return true;
			}

			try
			{
				string workingDirectory = Constants.GetWorkingDirectory();
				Configuration configuration = new Configuration(workingDirectory);

				bool loaded = configuration.Load(Configuration.DEFAULT_FILENAME);
				Configuration.SetGlobalSettings(configuration);

				if (!loaded)
				{
					string path = System.IO.Path.Combine(workingDirectory, Configuration.FULL_NAME);
					bool exists = false;
					try
					{
						exists = System.IO.File.Exists(path);
					}
					catch
					{
						/* Probing the path is itself best-effort. If even File.Exists throws we
						 * treat the file as present, which is the conservative reading: it means
						 * "do not write defaults over it". */
						exists = true;
					}

					if (exists)
					{
						Log.Warning("ClientSettings",
							$"'{Configuration.FULL_NAME}' exists but could not be read; running with defaults " +
							"and leaving the file untouched.");
					}
					else
					{
						configuration.Set("APIHost", Constants.Configuration.APIHost);

						/* Written now, not debounced. The debounce is pumped from a UI panel's
						 * per-frame hook, and this runs before any panel exists — a first launch
						 * that got no further than the launcher would have left the file unwritten. */
						RequestSave();
						Flush();
					}
				}

				/* The snap grid may already have been read — and cached — from a store that did
				 * not exist. Panels can be dragged before anything asked for configuration. */
				UITKPanelPositions.InvalidateSnapGrid();
				return true;
			}
			catch (Exception ex)
			{
				/* A settings file that cannot be loaded must never stop the client from starting.
				 * Every accessor below falls back to its default when the store is missing. */
				Log.Error("ClientSettings", "Could not load the client configuration; defaults are in effect.", ex);
				return false;
			}
		}

		/// <summary>
		/// Marks the configuration as owing a write, to be flushed once the player settles.
		/// </summary>
		public static void RequestSave()
		{
			savePending = true;
			saveDeadline = Time.unscaledTime + SaveDebounceSeconds;
		}

		/// <summary>
		/// Flushes an owed write once its quiet period has elapsed.
		/// </summary>
		/// <remarks>
		/// Driven from <see cref="UITKControl"/>'s per-frame hook, which every panel already runs.
		/// The early-out is a single bool read.
		/// </remarks>
		public static void Pump()
		{
			if (!savePending || Time.unscaledTime < saveDeadline)
			{
				return;
			}
			Flush();
		}

		/// <summary>
		/// Writes the configuration to disk immediately, if anything is owed.
		/// </summary>
		/// <remarks>
		/// Not in the editor: <see cref="Constants.GetWorkingDirectory"/> resolves to the
		/// repository root there rather than to an install directory, so a play-mode session
		/// would rewrite the developer's checked-out configuration. The in-memory values still
		/// apply, so settings behave normally while playing in the editor; only the cross-session
		/// part is skipped.
		/// </remarks>
		public static void Flush()
		{
			if (!savePending)
			{
				return;
			}
			savePending = false;

			Configuration configuration = Configuration.GlobalSettings;
			if (configuration == null)
			{
				return;
			}

#if !UNITY_EDITOR && !UNITY_WEBGL
			try
			{
				configuration.Save();
			}
			catch (Exception ex)
			{
				Log.Warning("ClientSettings", $"Saving the client configuration failed: {ex.Message}");
			}
#endif
		}

		// ── Typed access ────────────────────────────────────────────

		/// <summary>Reads a boolean setting, or its fallback when the store is unavailable.</summary>
		public static bool GetBool(string key, bool fallback)
		{
			Configuration configuration = Configuration.GlobalSettings;
			if (configuration == null)
			{
				return fallback;
			}
			configuration.TryGetBool(key, out bool value, fallback);
			return value;
		}

		/// <summary>Reads an integer setting, or its fallback when the store is unavailable.</summary>
		public static int GetInt(string key, int fallback)
		{
			Configuration configuration = Configuration.GlobalSettings;
			if (configuration == null)
			{
				return fallback;
			}
			configuration.TryGetInt(key, out int value, fallback);
			return value;
		}

		/// <summary>
		/// Reads a float setting, clamped into range and with non-finite values rejected.
		/// </summary>
		/// <remarks>
		/// NaN is checked separately because it compares false against every bound —
		/// <c>Mathf.Clamp</c> passes it straight through, and a NaN reaching a slider or a colour
		/// channel corrupts everything downstream of it.
		/// </remarks>
		public static float GetFloat(string key, float fallback, float minimum, float maximum)
		{
			Configuration configuration = Configuration.GlobalSettings;
			if (configuration == null)
			{
				return Mathf.Clamp(fallback, minimum, maximum);
			}

			configuration.TryGetFloat(key, out float value, fallback);
			if (float.IsNaN(value) || float.IsInfinity(value))
			{
				value = fallback;
			}
			return Mathf.Clamp(value, minimum, maximum);
		}

		/// <summary>Reads a string setting, or its fallback when the store is unavailable.</summary>
		public static string GetString(string key, string fallback)
		{
			Configuration configuration = Configuration.GlobalSettings;
			if (configuration == null)
			{
				return fallback;
			}
			configuration.TryGetString(key, out string value, fallback);
			return value ?? fallback;
		}

		/// <summary>Writes a setting and schedules a debounced save.</summary>
		public static void Set<T>(string key, T value)
		{
			Configuration configuration = Configuration.GlobalSettings;
			if (configuration == null)
			{
				return;
			}
			configuration.Set(key, value);
			RequestSave();
		}

		/// <summary>Writes a string setting and schedules a debounced save.</summary>
		public static void SetString(string key, string value)
		{
			Configuration configuration = Configuration.GlobalSettings;
			if (configuration == null)
			{
				return;
			}
			configuration.Set(key, value ?? string.Empty);
			RequestSave();
		}

		// ── Interface scale ─────────────────────────────────────────

		/// <summary>The interface scale multiplier the player has chosen.</summary>
		public static float UIScale
		{
			get => GetFloat(UIScaleKey, 1.0f, MinimumUIScale, MaximumUIScale);
			set
			{
				float clamped = Mathf.Clamp(value, MinimumUIScale, MaximumUIScale);
				Set(UIScaleKey, clamped);
				UITKPanelScale.Apply(clamped);
			}
		}

		/// <summary>
		/// Applies every setting that has a global effect, from the client's boot phase.
		/// </summary>
		/// <remarks>
		/// This is what makes a saved setting take effect without the player opening the options
		/// panel. Before it existed, the panel's own <c>OnStarting</c> was the only code that
		/// applied VSync, brightness or the frame-rate cap — and that panel ships closed, so its
		/// <c>OnStarting</c> did not run until the player opened it. A player who had capped their
		/// frame rate got the bootstrap default every session until they visited the menu.
		/// <para>
		/// Each step is isolated. Boot must complete even if one setting cannot be applied on this
		/// hardware; a client that fails to start because of a display mode is unrecoverable
		/// without hand-editing the file it failed on.
		/// </para>
		/// </remarks>
		public static void ApplyAll()
		{
			if (!EnsureLoaded())
			{
				return;
			}

			Apply("display", ClientDisplaySettings.ApplySaved);
			Apply("audio", ClientAudioSettings.ApplySaved);
			Apply("interface", () => UITKPanelScale.Apply(GetFloat(UIScaleKey, 1.0f, MinimumUIScale, MaximumUIScale)));
		}

		/// <summary>Runs one apply step, reporting rather than propagating a failure.</summary>
		private static void Apply(string label, Action step)
		{
			try
			{
				step();
			}
			catch (Exception ex)
			{
				Log.Error("ClientSettings", $"Applying {label} settings failed; that group keeps its defaults.", ex);
			}
		}
	}
}
