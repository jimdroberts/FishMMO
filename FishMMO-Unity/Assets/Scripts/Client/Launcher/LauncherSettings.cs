using System;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Typed access to the launcher's persisted settings, stored in the shared
	/// <see cref="Configuration.GlobalSettings"/> file alongside the game's other options.
	/// </summary>
	/// <remarks>
	/// The launcher has to load the configuration itself. Only the in-game Options panels did
	/// so previously, and those live in the world scene — by the time either runs, the launcher
	/// has long since finished. Nothing had read a settings file at launcher time before this.
	/// <para>
	/// Every getter clamps. These values reach the network layer, and the file is plain text a
	/// player can edit: a timeout of 0 or a retry count of 100000 would otherwise be honoured
	/// literally, and the failure would look like a launcher bug rather than a bad config.
	/// </para>
	/// </remarks>
	public static class LauncherSettings
	{
		/// <summary>Start downloading an available update without asking first.</summary>
		public const string KeyAutoUpdate = "Launcher.AutoUpdate";
		/// <summary>Per-request timeout, in seconds.</summary>
		public const string KeyRequestTimeout = "Launcher.RequestTimeout";
		/// <summary>Retry attempts after an initial request failure.</summary>
		public const string KeyMaxRetries = "Launcher.MaxRetries";
		/// <summary>Delay between retries, in seconds.</summary>
		public const string KeyRetryDelay = "Launcher.RetryDelay";
		/// <summary>Last window width the player used.</summary>
		public const string KeyWindowWidth = "Launcher.WindowWidth";
		/// <summary>Last window height the player used.</summary>
		public const string KeyWindowHeight = "Launcher.WindowHeight";
		/// <summary>Override for where patch archives are downloaded to and read from.</summary>
		public const string KeyPatchDirectory = "Launcher.PatchDirectory";
		/// <summary>Target version of the update currently being retried.</summary>
		public const string KeyUpdateAttemptVersion = "Launcher.UpdateAttemptVersion";
		/// <summary>How many times that update has been handed to the updater.</summary>
		public const string KeyUpdateAttemptCount = "Launcher.UpdateAttemptCount";

		/// <summary>
		/// Where patch archives are stored, or empty to use the install's own Patches folder.
		/// </summary>
		/// <remarks>
		/// This is not a "move the game" setting and cannot be one: the updater patches files
		/// relative to its own location and ships beside the client binaries, so the install
		/// root is fixed by construction. What it does allow is keeping the archives — which
		/// are large and transient — off a small system drive.
		/// <para>
		/// Whatever this resolves to has to be the same folder on both sides. The launcher
		/// downloads here and passes the resolved path to the updater explicitly rather than
		/// letting both derive it, because a disagreement is silent: the updater finds no
		/// archive, does nothing, and relaunches the client at the same version forever.
		/// </para>
		/// </remarks>
		public static string PatchDirectoryOverride
		{
			get
			{
				if (Configuration.GlobalSettings == null)
				{
					return string.Empty;
				}
				Configuration.GlobalSettings.TryGetString(KeyPatchDirectory, out string value, string.Empty);
				return value ?? string.Empty;
			}
			set => SetValue(KeyPatchDirectory, value ?? string.Empty);
		}

		/// <summary>
		/// Returns the directory patch archives should be written to and read from.
		/// </summary>
		/// <param name="defaultDirectory">
		/// The install's own Patches folder, used when no override is set or the override is
		/// unusable.
		/// </param>
		/// <remarks>
		/// Creates the directory if it does not exist — the player is naming a location, not
		/// promising to have made it. Any failure falls back to the default rather than
		/// propagating: the override is a convenience, and an unusable one should cost the
		/// convenience, not the update. The updater applies the same rule, so both sides land
		/// on the default together.
		/// </remarks>
		public static string ResolvePatchDirectory(string defaultDirectory)
		{
			string configured = PatchDirectoryOverride;
			if (string.IsNullOrWhiteSpace(configured))
			{
				return defaultDirectory;
			}

			try
			{
				// Rooted only, matching the updater. A relative path resolves against the
				// working directory of whichever process reads it, so the same string could
				// name two different folders across the handoff.
				if (!System.IO.Path.IsPathRooted(configured))
				{
					Log.Warning("LauncherSettings", $"Ignoring patch directory '{configured}': it must be an absolute path.");
					return defaultDirectory;
				}

				string full = System.IO.Path.GetFullPath(configured);
				System.IO.Directory.CreateDirectory(full);
				return full;
			}
			catch (Exception ex)
			{
				Log.Warning("LauncherSettings", $"Ignoring patch directory '{configured}' ({ex.Message}). Using '{defaultDirectory}'.");
				return defaultDirectory;
			}
		}

		/// <summary>
		/// How many automatic attempts at the same update are made before the launcher stops
		/// starting them on its own.
		/// </summary>
		/// <remarks>
		/// Three: enough to ride out a transient failure (a half-written archive, a file the
		/// antivirus had open, a machine that lost power mid-apply), few enough that a genuinely
		/// broken upgrade path does not cost the player an unbounded number of multi-gigabyte
		/// downloads.
		/// </remarks>
		public const int MaxConsecutiveUpdateAttempts = 3;

		/// <summary>
		/// Records that an update to <paramref name="targetVersion"/> is about to be handed to
		/// the updater, and returns which attempt this is (1 for the first).
		/// </summary>
		/// <remarks>
		/// <para>
		/// This exists because the failure it guards against is invisible from inside a single
		/// run. The updater's first action is to terminate this process, so when a patch fails
		/// AFTER that point there is no launcher left to be told: the updater rolls back,
		/// relaunches the client at the unchanged version, the launcher checks again, finds the
		/// same mismatch, and — with auto-update on — downloads and applies the same archive
		/// again. Nothing in that cycle is an error from any single participant's point of
		/// view, and it repeats forever.
		/// </para>
		/// <para>
		/// Written and flushed BEFORE the handoff for the same reason: anything recorded after
		/// it never gets written, because the process is gone.
		/// </para>
		/// </remarks>
		public static int RecordUpdateAttempt(string targetVersion)
		{
			if (string.IsNullOrEmpty(targetVersion))
			{
				return 0;
			}

			string tracked = GetString(KeyUpdateAttemptVersion, string.Empty);
			int count = string.Equals(tracked, targetVersion, StringComparison.Ordinal)
				? GetInt(KeyUpdateAttemptCount, 0)
				: 0;

			count += 1;
			SetValue(KeyUpdateAttemptVersion, targetVersion);
			SetValue(KeyUpdateAttemptCount, count);
			Save();
			return count;
		}

		/// <summary>
		/// True when <paramref name="targetVersion"/> has already been attempted
		/// <see cref="MaxConsecutiveUpdateAttempts"/> times without the client reaching it.
		/// </summary>
		/// <remarks>
		/// Only automatic updates are gated on this. A player who presses Update anyway has
		/// made a decision, and one more attempt at their request is not a loop.
		/// </remarks>
		public static bool HasExhaustedUpdateAttempts(string targetVersion)
		{
			if (string.IsNullOrEmpty(targetVersion))
			{
				return false;
			}
			if (!string.Equals(GetString(KeyUpdateAttemptVersion, string.Empty), targetVersion, StringComparison.Ordinal))
			{
				return false;
			}
			return GetInt(KeyUpdateAttemptCount, 0) >= MaxConsecutiveUpdateAttempts;
		}

		/// <summary>
		/// Clears the attempt counter. Called once the client is confirmed to be at the
		/// server's version — the only evidence that an update actually landed.
		/// </summary>
		public static void ClearUpdateAttempts()
		{
			if (GetInt(KeyUpdateAttemptCount, 0) == 0 && string.IsNullOrEmpty(GetString(KeyUpdateAttemptVersion, string.Empty)))
			{
				return;
			}
			SetValue(KeyUpdateAttemptVersion, string.Empty);
			SetValue(KeyUpdateAttemptCount, 0);
			Save();
		}

		/// <summary>
		/// Whether an available update starts downloading automatically.
		/// </summary>
		/// <remarks>
		/// Defaults to true because that is what the launcher has always done — an out-of-date
		/// client begins patching the moment the version check finishes. Turning it off makes
		/// the launcher stop and offer an Update button instead. Changing the default would
		/// silently alter behaviour for every existing install.
		/// </remarks>
		public static bool AutoUpdate
		{
			get => GetBool(KeyAutoUpdate, true);
			set => SetValue(KeyAutoUpdate, value);
		}

		/// <summary>Bounds for <see cref="GetRequestTimeout"/>, in seconds.</summary>
		public const int MinRequestTimeout = 5;
		public const int MaxRequestTimeout = 300;
		/// <summary>Bounds for <see cref="GetMaxRetries"/>.</summary>
		public const int MinRetries = 0;
		public const int MaxRetriesLimit = 10;
		/// <summary>Bounds for <see cref="GetRetryDelay"/>, in seconds.</summary>
		public const float MinRetryDelay = 0f;
		public const float MaxRetryDelay = 30f;

		/*
		 * The transfer tunables take the caller's configured value as their fallback rather
		 * than a constant of their own. The services that use these already carry the values
		 * as serialized fields, so an install with nothing stored keeps behaving exactly as it
		 * did — the setting only takes over once the player has actually chosen one.
		 */

		/// <summary>
		/// Per-request timeout in seconds, or <paramref name="fallback"/> when unset.
		/// </summary>
		public static int GetRequestTimeout(int fallback)
			=> Mathf.Clamp(GetInt(KeyRequestTimeout, fallback), MinRequestTimeout, MaxRequestTimeout);

		public static void SetRequestTimeout(int value)
			=> SetValue(KeyRequestTimeout, Mathf.Clamp(value, MinRequestTimeout, MaxRequestTimeout));

		/// <summary>
		/// Retry attempts after an initial failure, or <paramref name="fallback"/> when unset.
		/// </summary>
		public static int GetMaxRetries(int fallback)
			=> Mathf.Clamp(GetInt(KeyMaxRetries, fallback), MinRetries, MaxRetriesLimit);

		public static void SetMaxRetries(int value)
			=> SetValue(KeyMaxRetries, Mathf.Clamp(value, MinRetries, MaxRetriesLimit));

		/// <summary>
		/// Delay between retries in seconds, or <paramref name="fallback"/> when unset.
		/// </summary>
		public static float GetRetryDelay(float fallback)
			=> Mathf.Clamp(GetFloat(KeyRetryDelay, fallback), MinRetryDelay, MaxRetryDelay);

		public static void SetRetryDelay(float value)
			=> SetValue(KeyRetryDelay, Mathf.Clamp(value, MinRetryDelay, MaxRetryDelay));

		/// <summary>
		/// Remembers the launcher window size, or (0, 0) when the player has not resized it.
		/// </summary>
		/// <remarks>
		/// Clamped against the current display so a window saved on a larger monitor cannot
		/// come back bigger than the screen it is now opening on — which would put the footer
		/// buttons off the bottom with no way to reach them.
		/// </remarks>
		public static Vector2Int GetWindowSize(int minWidth, int minHeight)
		{
			int width = GetInt(KeyWindowWidth, 0);
			int height = GetInt(KeyWindowHeight, 0);

			if (width <= 0 || height <= 0)
			{
				return Vector2Int.zero;
			}

			int maxWidth = Screen.currentResolution.width;
			int maxHeight = Screen.currentResolution.height;

			return new Vector2Int(
				Mathf.Clamp(width, minWidth, Mathf.Max(minWidth, maxWidth)),
				Mathf.Clamp(height, minHeight, Mathf.Max(minHeight, maxHeight)));
		}

		/// <summary>
		/// Records the launcher window size.
		/// </summary>
		public static void SetWindowSize(int width, int height)
		{
			if (width <= 0 || height <= 0)
			{
				return;
			}
			SetValue(KeyWindowWidth, width);
			SetValue(KeyWindowHeight, height);
		}

		/// <summary>
		/// Loads the global configuration if nothing has loaded it yet.
		/// </summary>
		/// <remarks>
		/// Mirrors <c>UITKOptions.EnsureConfigurationLoaded</c> so the launcher and the in-game
		/// Options panel agree about where settings live and cannot end up with two stores.
		/// </remarks>
		public static void EnsureLoaded()
		{
			/* Delegated rather than duplicated. Two places that each construct a Configuration
			 * over the same file is how the launcher and the game end up with separate stores —
			 * whichever saves last wins, and the other half's changes vanish. ClientSettings owns
			 * the single instance and every caller asks it. */
			ClientSettings.EnsureLoaded();
		}

		/// <summary>
		/// Persists settings to disk. Best-effort — a read-only install directory must not be
		/// a fatal error for a launcher that otherwise works.
		/// </summary>
		public static void Save()
		{
			if (Configuration.GlobalSettings == null)
			{
				return;
			}

			try
			{
				Configuration.GlobalSettings.Save();
			}
			catch (Exception ex)
			{
				Log.Warning("LauncherSettings", $"Could not save launcher configuration: {ex.Message}");
			}
		}

		private static string GetString(string key, string fallback)
		{
			if (Configuration.GlobalSettings == null)
			{
				return fallback;
			}
			Configuration.GlobalSettings.TryGetString(key, out string value, fallback);
			return value ?? fallback;
		}

		private static bool GetBool(string key, bool fallback)
		{
			if (Configuration.GlobalSettings == null)
			{
				return fallback;
			}
			Configuration.GlobalSettings.TryGetBool(key, out bool value, fallback);
			return value;
		}

		private static int GetInt(string key, int fallback)
		{
			if (Configuration.GlobalSettings == null)
			{
				return fallback;
			}
			Configuration.GlobalSettings.TryGetInt(key, out int value, fallback);
			return value;
		}

		private static float GetFloat(string key, float fallback)
		{
			if (Configuration.GlobalSettings == null)
			{
				return fallback;
			}
			Configuration.GlobalSettings.TryGetFloat(key, out float value, fallback);
			return value;
		}

		private static void SetValue<T>(string key, T value)
		{
			if (Configuration.GlobalSettings == null)
			{
				Log.Warning("LauncherSettings", $"Cannot store '{key}': no configuration is loaded.");
				return;
			}
			Configuration.GlobalSettings.Set(key, value);
		}
	}
}
