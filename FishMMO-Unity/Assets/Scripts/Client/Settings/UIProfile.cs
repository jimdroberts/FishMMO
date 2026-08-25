using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Reads and writes a <b>UI profile</b>: the player's window layout, theme colours and
	/// interface scale, in a file of its own that can be handed to somebody else.
	/// </summary>
	/// <remarks>
	/// <para><b>Why a separate file.</b> Configuration.cfg holds the whole client's settings —
	/// including its API host, launcher state and the machine's display mode. None of that is
	/// meaningful on another player's computer and some of it is actively wrong there, so it is not
	/// something to hand around. A profile carries only the parts that describe how the interface
	/// looks and where its windows are, which are exactly the parts worth sharing.</para>
	///
	/// <para><b>Configuration.cfg stays the source of truth.</b> Loading a profile writes its keys
	/// into <see cref="Configuration.GlobalSettings"/> and saves; nothing reads a profile at
	/// runtime. That keeps one store behind every setting — a second live store is how two halves
	/// of a client end up disagreeing about what the player chose — and means a profile that is
	/// later deleted cannot take the player's interface with it.</para>
	///
	/// <para><b>Every key is validated on the way in.</b> A profile is a text file written by a
	/// stranger. Panel coordinates are re-clamped into the viewport by
	/// <see cref="UITKControl"/> when they are applied, the scale is clamped to the range the
	/// slider offers, and colour channels are bytes that cannot be out of range by construction.
	/// A key the profile format does not define is ignored rather than copied through, so a
	/// hand-edited profile cannot reach any other part of the configuration.</para>
	/// </remarks>
	public static class UIProfile
	{
		/// <summary>Folder, under the install directory, that profiles are read from and written to.</summary>
		public const string DirectoryName = "UIProfiles";

		/// <summary>Key recording which version of this format wrote a profile.</summary>
		private const string VersionKey = "UIProfile.Version";

		/// <summary>Version written into new profiles.</summary>
		private const int CurrentVersion = 1;

		/// <summary>Key holding the profile's player-facing description.</summary>
		private const string DescriptionKey = "UIProfile.Description";

		/// <summary>Prefix shared by every per-panel position key.</summary>
		private const string PanelKeyPrefix = "UI.Panel.";

		/// <summary>Longest profile name accepted, so a name cannot overflow a path.</summary>
		private const int MaximumNameLength = 48;

		/// <summary>The folder profiles live in.</summary>
		public static string ProfileDirectory => Path.Combine(Constants.GetWorkingDirectory(), DirectoryName);

		/// <summary>
		/// Whether a name is usable as a profile file name.
		/// </summary>
		/// <param name="name">The candidate name.</param>
		/// <param name="reason">A player-facing explanation when it is not.</param>
		/// <returns>True when the name is safe to use.</returns>
		/// <remarks>
		/// Rejects rather than sanitises. A name silently rewritten to something else does not
		/// match what the player typed, so the profile they saved is not the one they look for —
		/// and a sanitiser that strips separators is one missed case away from writing outside the
		/// profile folder.
		/// </remarks>
		public static bool IsValidName(string name, out string reason)
		{
			reason = null;

			if (string.IsNullOrWhiteSpace(name))
			{
				reason = "Enter a name for this layout.";
				return false;
			}

			if (name.Length > MaximumNameLength)
			{
				reason = $"Names are limited to {MaximumNameLength} characters.";
				return false;
			}

			if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
				name.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
				name.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
				name.IndexOf("..", StringComparison.Ordinal) >= 0)
			{
				reason = "That name contains characters that cannot be used in a file name.";
				return false;
			}

			/* Trailing dots and spaces are legal in the string and illegal at the end of a Windows
			 * file name; the file system silently trims them, so "Layout." and "Layout" become the
			 * same file and one silently overwrites the other. */
			if (name != name.Trim() || name.EndsWith(".", StringComparison.Ordinal))
			{
				reason = "Names cannot begin or end with a space or a dot.";
				return false;
			}

			return true;
		}

		/// <summary>
		/// Every profile currently on disk, by name, alphabetically.
		/// </summary>
		public static List<string> List()
		{
			List<string> names = new List<string>();

			try
			{
				string directory = ProfileDirectory;
				if (!Directory.Exists(directory))
				{
					return names;
				}

				string[] files = Directory.GetFiles(directory, "*" + Configuration.EXTENSION);
				for (int i = 0; i < files.Length; ++i)
				{
					string name = Path.GetFileNameWithoutExtension(files[i]);
					if (!string.IsNullOrEmpty(name))
					{
						names.Add(name);
					}
				}

				names.Sort(StringComparer.OrdinalIgnoreCase);
			}
			catch (Exception ex)
			{
				Log.Warning("UIProfile", $"Could not list UI profiles: {ex.Message}");
			}

			return names;
		}

		/// <summary>Whether a profile of that name exists.</summary>
		public static bool Exists(string name)
		{
			if (!IsValidName(name, out _))
			{
				return false;
			}

			try
			{
				return File.Exists(PathFor(name));
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Writes the interface's current state to a profile.
		/// </summary>
		/// <param name="name">Profile name, already validated by <see cref="IsValidName"/>.</param>
		/// <param name="error">A player-facing explanation when the save fails.</param>
		/// <returns>True when the file was written.</returns>
		/// <remarks>
		/// Panel positions are collected from the stored configuration keys, not from the panels
		/// currently registered: the settings panel is reachable from the login screen, where the
		/// world's windows do not exist yet.
		/// </remarks>
		public static bool Save(string name, out string error)
		{
			error = null;

			if (!IsValidName(name, out error))
			{
				return false;
			}

			Configuration source = Configuration.GlobalSettings;
			if (source == null)
			{
				error = "Settings are not loaded yet.";
				return false;
			}

			try
			{
				string directory = ProfileDirectory;
				Configuration profile = new Configuration(directory);

				profile.Set(VersionKey, CurrentVersion);
				profile.Set(DescriptionKey, name);

				// Interface
				CopyIfPresent(source, profile, UITKPanelPositions.SnapGridKey);
				CopyIfPresent(source, profile, ClientSettings.UIScaleKey);

				// Theme colours
				for (int i = 0; i < UITKTheme.ColorNames.Length; ++i)
				{
					string colour = UITKTheme.ColorNames[i];
					CopyIfPresent(source, profile, colour + "ColorR");
					CopyIfPresent(source, profile, colour + "ColorG");
					CopyIfPresent(source, profile, colour + "ColorB");
					CopyIfPresent(source, profile, colour + "ColorA");
				}

				/* Window layout, taken from the stored keys rather than from the panels that
				 * happen to be registered right now. The settings panel can be opened from the
				 * login screen, where none of the world's forty-odd windows exist yet — so
				 * enumerating live panels there would save a "layout" containing almost nothing,
				 * and loading it back would reset every world window the player had arranged. */
				foreach (string key in source.GetKeys(PanelKeyPrefix))
				{
					CopyIfPresent(source, profile, key);
				}

				profile.Save(directory, name + Configuration.EXTENSION);
				profile.Dispose();

				/* Configuration.Save reports failures to the console and returns void, so a save
				 * that could not write — a read-only install folder is the usual reason — would
				 * otherwise be reported to the player as success. */
				if (!File.Exists(PathFor(name)))
				{
					error = "The profile could not be written. Check that the game folder is writable.";
					return false;
				}

				return true;
			}
			catch (Exception ex)
			{
				Log.Error("UIProfile", $"Saving UI profile '{name}' failed.", ex);
				error = "The profile could not be written.";
				return false;
			}
		}

		/// <summary>
		/// Applies a profile to the running client and writes it into the global configuration.
		/// </summary>
		/// <param name="name">Profile name.</param>
		/// <param name="error">A player-facing explanation when the load fails.</param>
		/// <returns>True when the profile was applied.</returns>
		/// <remarks>
		/// <para>A profile is applied wholesale, including the absence of a key. A profile with no
		/// entry for a panel means "that panel sits where the stylesheet puts it", not "leave the
		/// panel where the player last dragged it" — otherwise loading somebody else's layout
		/// leaves a mixture of theirs and yours, which is not a layout either of you has ever
		/// seen.</para>
		/// <para>Colours work the same way: a colour the profile does not set is cleared rather
		/// than left, so a shared colour scheme arrives whole.</para>
		/// </remarks>
		public static bool Load(string name, out string error)
		{
			error = null;

			if (!IsValidName(name, out error))
			{
				return false;
			}

			Configuration target = Configuration.GlobalSettings;
			if (target == null)
			{
				error = "Settings are not loaded yet.";
				return false;
			}

			Configuration profile = null;
			try
			{
				string directory = ProfileDirectory;
				profile = new Configuration(directory);

				if (!profile.Load(directory, name + Configuration.EXTENSION))
				{
					error = "That profile could not be read.";
					return false;
				}

				/* Version-gated. A profile from a future build may store a key this one would
				 * misread; refusing it is better than applying half of it. Older versions are
				 * accepted — every key this format has ever defined is still read the same way. */
				profile.TryGetInt(VersionKey, out int version, 0);
				if (version > CurrentVersion)
				{
					error = "That profile was written by a newer version of the game.";
					return false;
				}

				ApplyInterface(profile);
				ApplyColors(profile, target);
				ApplyLayout(profile, target);

				ClientSettings.RequestSave();
				ClientSettings.Flush();
				return true;
			}
			catch (Exception ex)
			{
				Log.Error("UIProfile", $"Loading UI profile '{name}' failed.", ex);
				error = "That profile could not be read.";
				return false;
			}
			finally
			{
				profile?.Dispose();
			}
		}

		/// <summary>
		/// Deletes a profile.
		/// </summary>
		/// <param name="name">Profile name.</param>
		/// <param name="error">A player-facing explanation when the delete fails.</param>
		/// <returns>True when the file is gone afterwards.</returns>
		public static bool Delete(string name, out string error)
		{
			error = null;

			if (!IsValidName(name, out error))
			{
				return false;
			}

			try
			{
				string path = PathFor(name);
				if (File.Exists(path))
				{
					File.Delete(path);
				}
				return true;
			}
			catch (Exception ex)
			{
				Log.Warning("UIProfile", $"Deleting UI profile '{name}' failed: {ex.Message}");
				error = "The profile could not be deleted.";
				return false;
			}
		}

		/// <summary>The full path a profile is stored at.</summary>
		private static string PathFor(string name)
		{
			return Path.Combine(ProfileDirectory, name + Configuration.EXTENSION);
		}

		/// <summary>Copies one key, if the source has it, leaving the target untouched otherwise.</summary>
		private static void CopyIfPresent(Configuration source, Configuration destination, string key)
		{
			if (!source.TryGetString(key, out string value) || string.IsNullOrEmpty(value))
			{
				return;
			}
			destination.Set(key, value);
		}

		/// <summary>
		/// Applies the profile's interface settings — snap grid and scale.
		/// </summary>
		/// <remarks>
		/// Written through the live properties rather than into the store directly, because both
		/// have an effect beyond the value: the snap grid keeps a parsed cache, and the interface
		/// scale has to reach the shared <c>PanelSettings</c>. Both properties persist as a side
		/// effect, so the destination store is not needed here.
		/// </remarks>
		private static void ApplyInterface(Configuration profile)
		{
			if (profile.TryGetFloat(UITKPanelPositions.SnapGridKey, out float snap))
			{
				// Through the property, which clamps and refreshes the cached value.
				UITKPanelPositions.SnapGridSize = snap;
			}

			if (profile.TryGetFloat(ClientSettings.UIScaleKey, out float scale))
			{
				ClientSettings.UIScale = scale;
			}
		}

		/// <summary>Applies the profile's colour scheme, clearing anything it does not set.</summary>
		private static void ApplyColors(Configuration profile, Configuration target)
		{
			for (int i = 0; i < UITKTheme.ColorNames.Length; ++i)
			{
				string colour = UITKTheme.ColorNames[i];

				/* Presence is decided by the R channel, the same rule UITKTheme.Parse uses when
				 * reading a colour back — so "the profile sets this colour" means the same thing
				 * on both sides. */
				if (!profile.TryGetByte(colour + "ColorR", out byte r))
				{
					UITKTheme.Clear(target, colour);
					continue;
				}

				profile.TryGetByte(colour + "ColorG", out byte g);
				profile.TryGetByte(colour + "ColorB", out byte b);
				if (!profile.TryGetByte(colour + "ColorA", out byte a))
				{
					a = 255;
				}

				UITKTheme.Write(target, colour, new Color32(r, g, b, a));
			}

			UITKThemeManager.Reload();
		}

		/// <summary>
		/// Applies the profile's window layout to the live panels.
		/// </summary>
		/// <remarks>
		/// Every stored panel position is cleared first. A profile is a complete layout, so a
		/// window it says nothing about belongs where the stylesheet puts it — merging the
		/// profile's entries over the player's own would produce an arrangement neither of them
		/// has ever seen, made of half of each.
		/// </remarks>
		private static void ApplyLayout(Configuration profile, Configuration target)
		{
			foreach (string key in target.GetKeys(PanelKeyPrefix))
			{
				target.Remove(key);
			}

			/* Names, not keys, because a position is two keys that have to arrive together. A
			 * profile holding only X — a truncated write, or a hand edit — would otherwise pin the
			 * panel to the top of the screen, which for a window the player had moved to the
			 * bottom reads as the layout having been remembered wrongly rather than not at all. */
			HashSet<string> panels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string key in profile.GetKeys(PanelKeyPrefix))
			{
				if (key.Length <= PanelKeyPrefix.Length + 2)
				{
					continue;
				}
				if (!key.EndsWith(".X", StringComparison.OrdinalIgnoreCase) &&
					!key.EndsWith(".Y", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				panels.Add(key.Substring(PanelKeyPrefix.Length, key.Length - PanelKeyPrefix.Length - 2));
			}

			foreach (string panel in panels)
			{
				string xKey = PanelKeyPrefix + panel + ".X";
				string yKey = PanelKeyPrefix + panel + ".Y";

				if (!profile.TryGetFloat(xKey, out float x) ||
					!profile.TryGetFloat(yKey, out float y) ||
					float.IsNaN(x) || float.IsNaN(y) ||
					float.IsInfinity(x) || float.IsInfinity(y))
				{
					continue;
				}

				target.Set(xKey, x);
				target.Set(yKey, y);
			}

			/* Applied to the live panels, not merely stored. Panels read their position once, on
			 * first layout, so a profile written into configuration alone would not be visible
			 * until the client was restarted. Panels that do not exist yet — the world's windows,
			 * when a profile is loaded from the login screen — pick their new position up from the
			 * configuration when they are first laid out, which is the same path a restart uses. */
			UIManager.ReloadAllPanelPositions();
		}
	}
}
