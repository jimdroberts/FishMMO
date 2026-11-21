using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Reads and writes the notes a character has pinned to a scene's map.
	/// </summary>
	/// <remarks>
	/// <para><b>Local, permanently.</b> Notes stay on the machine that wrote them, in the same
	/// folder as the explored map. Sharing them would mean player-authored text travelling between
	/// clients, which needs server-side length limits, rate limits and a moderation story — a
	/// large amount of work in service of a feature nobody has asked for yet. A note is a
	/// reminder to yourself, and that is what this supports.</para>
	///
	/// <para><b>Plain text, not a serialiser.</b> One record per line, fields separated by an ASCII
	/// unit separator that no field may contain. A note is six small values; a JSON or binary
	/// format would buy nothing and would stop the player opening the file to see what is in it.
	/// Anything malformed is skipped rather than aborting the file, so one bad line costs one
	/// note.</para>
	///
	/// <para><b>No signature, unlike the explored map.</b> There is nothing to protect: a forged
	/// note draws a pin the player put there themselves. The fog file is signed because it is an
	/// input to a progression system; this is not.</para>
	/// </remarks>
	public static class MapNoteStore
	{
		/// <summary>Extension used for a scene's notes.</summary>
		private const string FileExtension = ".notes";

		/// <summary>
		/// Field separator: the ASCII unit separator, which no field may contain.
		/// </summary>
		/// <remarks>
		/// Written as a numeric cast rather than as a literal so the character never appears in
		/// the source file. A raw control byte in a .cs file survives most editors and no diff
		/// tool, which makes a format bug caused by one effectively invisible in review.
		/// </remarks>
		private const char Separator = (char)31;

		/// <summary>Format version written as the file's first line.</summary>
		private const string VersionLine = "FMNOTES 1";

		/// <summary>How many fields a well-formed record has.</summary>
		private const int FieldCount = 8;

		/// <summary>
		/// The file holding one character's notes for one scene.
		/// </summary>
		/// <param name="characterID">The character's ID.</param>
		/// <param name="sceneName">The scene's name.</param>
		/// <returns>An absolute path. The file may not exist yet.</returns>
		/// <remarks>
		/// Derived from the explored map's path so the two cannot disagree about which folder a
		/// character owns or how a scene name is made safe for a file system.
		/// </remarks>
		public static string FilePath(long characterID, string sceneName)
		{
			return Path.ChangeExtension(FogOfWarStore.FilePath(characterID, sceneName), FileExtension);
		}

		/// <summary>
		/// Loads a character's notes for a scene.
		/// </summary>
		/// <param name="characterID">The character's ID.</param>
		/// <param name="sceneName">The scene's name.</param>
		/// <returns>The notes. Empty when there are none.</returns>
		public static List<MapNote> Load(long characterID, string sceneName)
		{
			List<MapNote> notes = new List<MapNote>();
			string path = FilePath(characterID, sceneName);

			try
			{
				if (!File.Exists(path))
				{
					return notes;
				}

				string[] lines = File.ReadAllLines(path, Encoding.UTF8);
				for (int i = 0; i < lines.Length; ++i)
				{
					string line = lines[i];
					if (string.IsNullOrWhiteSpace(line) || line.StartsWith("FMNOTES", StringComparison.Ordinal))
					{
						continue;
					}

					MapNote note = Parse(line);
					if (note != null)
					{
						notes.Add(note);
					}
				}
			}
			catch (Exception exception)
			{
				Log.Warning("MapNoteStore", $"Could not read map notes '{path}': {exception.Message}.");
			}

			return notes;
		}

		/// <summary>
		/// Writes a character's notes for a scene.
		/// </summary>
		/// <param name="characterID">The character's ID.</param>
		/// <param name="sceneName">The scene's name.</param>
		/// <param name="notes">The notes to write. Null or empty deletes the file.</param>
		/// <returns>True when the store was updated.</returns>
		public static bool Save(long characterID, string sceneName, IReadOnlyList<MapNote> notes)
		{
			string path = FilePath(characterID, sceneName);

			try
			{
				if (notes == null || notes.Count < 1)
				{
					/* Deleted rather than written empty. A character who removes their last note
					 * should leave no file behind, and an empty file is indistinguishable from a
					 * truncated one. */
					if (File.Exists(path))
					{
						File.Delete(path);
					}
					return true;
				}

				Directory.CreateDirectory(Path.GetDirectoryName(path));

				StringBuilder builder = new StringBuilder();
				builder.AppendLine(VersionLine);

				for (int i = 0; i < notes.Count; ++i)
				{
					MapNote note = notes[i];
					if (note == null)
					{
						continue;
					}

					builder.Append(note.ID.ToString(CultureInfo.InvariantCulture)).Append(Separator);
					builder.Append(note.Position.x.ToString("R", CultureInfo.InvariantCulture)).Append(Separator);
					builder.Append(note.Position.y.ToString("R", CultureInfo.InvariantCulture)).Append(Separator);
					builder.Append(note.Position.z.ToString("R", CultureInfo.InvariantCulture)).Append(Separator);
					builder.Append(note.ColorIndex.ToString(CultureInfo.InvariantCulture)).Append(Separator);
					builder.Append(note.ShowOnMinimap ? '1' : '0').Append(Separator);
					builder.Append(Escape(note.Title)).Append(Separator);
					builder.Append(Escape(note.Text));
					builder.AppendLine();
				}

				string temporary = path + ".tmp";
				File.WriteAllText(temporary, builder.ToString(), Encoding.UTF8);
				if (File.Exists(path))
				{
					File.Delete(path);
				}
				File.Move(temporary, path);
				return true;
			}
			catch (Exception exception)
			{
				Log.Warning("MapNoteStore", $"Could not write map notes '{path}': {exception.Message}.");
				return false;
			}
		}

		/// <summary>
		/// Reads one record.
		/// </summary>
		/// <param name="line">The line to parse.</param>
		/// <returns>The note, or null when the line is malformed.</returns>
		private static MapNote Parse(string line)
		{
			string[] fields = line.Split(Separator);
			if (fields.Length < FieldCount)
			{
				return null;
			}

			if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ||
				!float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
				!float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
				!float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
				!int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int colorIndex))
			{
				return null;
			}

			/* Non-finite coordinates would place the note nowhere and, worse, poison every
			 * normalisation that touches it: a NaN reaching a UI element's style makes UI Toolkit
			 * drop the layout pass for that panel. Cheaper to reject the line. */
			if (float.IsNaN(x) || float.IsInfinity(x) ||
				float.IsNaN(y) || float.IsInfinity(y) ||
				float.IsNaN(z) || float.IsInfinity(z))
			{
				return null;
			}

			MapNote note = new MapNote()
			{
				ID = id,
				Position = new Vector3(x, y, z),
				ColorIndex = Mathf.Max(0, colorIndex),
				ShowOnMinimap = fields[5] == "1",
			};
			note.SetContent(Unescape(fields[6]), Unescape(fields[7]));
			return note;
		}

		/// <summary>
		/// Replaces the characters that would break the line format.
		/// </summary>
		/// <param name="value">The text to escape.</param>
		/// <returns>Text safe to write as a field.</returns>
		private static string Escape(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}

			/* Backslash first. Escaping it after the newline would turn a literal backslash-n the
			 * player typed into an escaped newline on the way back in. */
			return value.Replace("\\", "\\\\")
						.Replace("\r", string.Empty)
						.Replace("\n", "\\n")
						.Replace(Separator.ToString(), " ");
		}

		/// <summary>
		/// Restores escaped text.
		/// </summary>
		/// <param name="value">The escaped text.</param>
		/// <returns>The original text.</returns>
		private static string Unescape(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}

			return value.Replace("\\n", "\n").Replace("\\\\", "\\");
		}
	}
}
