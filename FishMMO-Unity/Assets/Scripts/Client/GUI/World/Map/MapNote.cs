using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// A marker the player placed on the world map themselves: a title, an optional line of text,
	/// a colour, and a place.
	/// </summary>
	/// <remarks>
	/// Purely local. Notes are stored beside the explored map in the character's own folder and
	/// are never sent anywhere — see <see cref="MapNoteStore"/> for why that is the whole design
	/// rather than a first step.
	/// </remarks>
	public sealed class MapNote
	{
		/// <summary>Longest title accepted, in characters.</summary>
		public const int MaximumTitleLength = 48;

		/// <summary>Longest body accepted, in characters.</summary>
		public const int MaximumTextLength = 256;

		/// <summary>Identifier, unique within the scene, used to edit and delete the note.</summary>
		public long ID;

		/// <summary>Where the note sits, in world space. Y is the terrain height at placement.</summary>
		public Vector3 Position;

		/// <summary>The note's title, drawn beside its pin.</summary>
		public string Title;

		/// <summary>The note's body, shown in the tooltip.</summary>
		public string Text;

		/// <summary>Which of the note palette's colours the pin is drawn in.</summary>
		public int ColorIndex;

		/// <summary>Whether the note is also drawn on the minimap.</summary>
		public bool ShowOnMinimap = true;

		/// <summary>
		/// Trims a title and body to the lengths the store will accept.
		/// </summary>
		/// <param name="title">The title to clamp.</param>
		/// <param name="text">The body to clamp.</param>
		/// <remarks>
		/// Applied when the note is created and again when it is loaded. A file edited by hand can
		/// carry a title of any length, and a note whose title is a megabyte of text would be laid
		/// out and drawn every frame the map is open.
		/// </remarks>
		public void SetContent(string title, string text)
		{
			Title = Clamp(title, MaximumTitleLength);
			Text = Clamp(text, MaximumTextLength);
		}

		/// <summary>
		/// Trims a string to a maximum length, treating null as empty.
		/// </summary>
		/// <param name="value">The string to trim.</param>
		/// <param name="maximum">The longest result allowed.</param>
		/// <returns>The trimmed string.</returns>
		private static string Clamp(string value, int maximum)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}

			value = value.Trim();
			return value.Length <= maximum ? value : value.Substring(0, maximum);
		}
	}
}
