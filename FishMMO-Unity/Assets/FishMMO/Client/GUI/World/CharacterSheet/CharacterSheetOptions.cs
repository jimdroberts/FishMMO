namespace FishMMO.Client
{
	/// <summary>
	/// What a given viewer of a character sheet is allowed to see.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The panel decides, the sheet renders. That split is the whole point of this type: the two
	/// windows that mount the sheet — the player's own equipment and an inspected character — differ
	/// only in what may be shown, never in how it is drawn, and a future profession that reveals more
	/// of an inspected character changes one of these values rather than forking the markup.
	/// </para>
	/// <para>
	/// Read-only and defaulted by construction, so a caller cannot half-describe a sheet: there is no
	/// public setter and no parameterless constructor, which means every construction site says out
	/// loud which of the three regions it wants. The presets below are the two that exist today.
	/// </para>
	/// </remarks>
	public readonly struct CharacterSheetOptions
	{
		/// <summary>Whether the 3D character preview is drawn.</summary>
		public readonly bool ShowPreview;

		/// <summary>Whether the HP / MP / Stamina status bar is drawn.</summary>
		public readonly bool ShowStatusBar;

		/// <summary>
		/// Whether the attribute list is built and subscribed to.
		/// </summary>
		/// <remarks>
		/// False for an inspected character: their attributes are not theirs to show, and the panel
		/// the player compares against is already on screen beside it.
		/// </remarks>
		public readonly bool ShowAttributes;

		/// <summary>
		/// Describes a sheet.
		/// </summary>
		/// <param name="showPreview">Whether to draw the character preview.</param>
		/// <param name="showStatusBar">Whether to draw the status bar.</param>
		/// <param name="showAttributes">Whether to build the attribute list.</param>
		public CharacterSheetOptions(bool showPreview, bool showStatusBar, bool showAttributes)
		{
			ShowPreview = showPreview;
			ShowStatusBar = showStatusBar;
			ShowAttributes = showAttributes;
		}

		/// <summary>
		/// The player's own sheet: everything.
		/// </summary>
		public static CharacterSheetOptions Equipment => new CharacterSheetOptions(true, true, true);

		/// <summary>
		/// Another character's sheet: gear, preview and vitals, and no attributes.
		/// </summary>
		public static CharacterSheetOptions Inspect => new CharacterSheetOptions(true, true, false);
	}
}
