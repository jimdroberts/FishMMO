namespace FishMMO.Client
{
	/// <summary>
	/// Draw and input order for UI Toolkit panels, applied to <c>UIDocument.sortingOrder</c>.
	/// </summary>
	/// <remarks>
	/// Every panel in the project is its own <c>UIDocument</c> sharing one PanelSettings asset,
	/// and UI Toolkit orders those panels by sorting order alone. Panels that all sit at the
	/// same value fall back to the order they happened to register in — which is scene load
	/// order, so a panel in ClientPreboot ends up underneath anything in a scene loaded after
	/// it. Input follows the same order, so the loser is not merely behind: it receives no
	/// pointer events at all.
	///
	/// That is not a theoretical hazard. Options lives in ClientPreboot and Login lives in
	/// ClientLoginGUI, so opening Options from the login screen put it behind Login and made it
	/// impossible to click.
	///
	/// The layer is declared in code rather than set per scene so that it is versioned with the
	/// panel, reviewable in a diff, and impossible to forget: a newly written panel inherits
	/// <see cref="Window"/> from <c>UITKControl</c> rather than silently defaulting to zero and
	/// landing under the HUD.
	///
	/// Values are spaced by 100 so a tier can be inserted without renumbering the ones around it.
	/// </remarks>
	public enum UITKPanelLayer
	{
		/// <summary>
		/// Projected world content — nameplates, damage numbers.
		/// </summary>
		/// <remarks>
		/// Below everything. These are drawn in screen space but belong to the world, so a panel
		/// must always cover them; a nameplate showing through an open inventory reads as a bug.
		/// </remarks>
		WorldOverlay = -100,

		/// <summary>
		/// Persistent heads-up display — resource bars, hotkey bar, buffs, cast bar, crosshair,
		/// minimap, chat, target frame, pet frame.
		/// </summary>
		/// <remarks>Always visible, never covers a window the player deliberately opened.</remarks>
		Hud = 0,

		/// <summary>
		/// Ordinary windows the player opens and closes: inventory, equipment, bank, guild,
		/// party, friends, abilities, merchant, and the login-flow screens.
		/// </summary>
		/// <remarks>The default for any panel that does not say otherwise.</remarks>
		Window = 100,

		/// <summary>
		/// The game menu, which opens over any window.
		/// </summary>
		Menu = 200,

		/// <summary>
		/// Options, which opens from the menu and from the login screen, so it must clear both.
		/// </summary>
		Settings = 300,

		/// <summary>
		/// Transient popups raised from a window or from Options: dropdowns, list selectors,
		/// the right-click context menu, the chat channel picker.
		/// </summary>
		Popup = 400,

		/// <summary>
		/// Blocking dialogs and pickers: confirm boxes, input boxes, the colour picker, the
		/// death dialog.
		/// </summary>
		/// <remarks>
		/// Above <see cref="Settings"/> because the colour picker is opened from the Options
		/// panel and would otherwise appear behind the thing that raised it.
		/// </remarks>
		Modal = 500,

		/// <summary>
		/// The tooltip, which follows the cursor over any panel.
		/// </summary>
		Tooltip = 700,

		/// <summary>
		/// The drag ghost, above the tooltip because a tooltip has no business showing over an
		/// item the player is currently carrying.
		/// </summary>
		Drag = 800,

		/// <summary>
		/// Full-screen application state: the loading screen.
		/// </summary>
		/// <remarks>Covers everything, including modals — the session is not usable beneath it.</remarks>
		System = 900,

		/// <summary>
		/// The reconnect display, which is raised on top of the loading overlay.
		/// </summary>
		/// <remarks>
		/// These two used to share <see cref="System"/>, and they are on screen at the same time
		/// for the whole of a reconnect: <c>Client_OnReconnectPending</c> raises the loading
		/// overlay and <c>OnReconnectAttemptsChanged</c> raises this one over it. Panels at equal
		/// sorting order fall back to registration order, and — per the remarks at the top of this
		/// file — the loser receives no pointer events at all. Which of the two lost was decided by
		/// scene load order, so the reconnect Cancel button could be dead for the whole backoff,
		/// which is up to ten attempts.
		/// <para>
		/// The reconnect display wins deliberately: it is the one of the pair that has a control on
		/// it, and the loading overlay behind it is a backdrop. Its own "Return to login" button
		/// still works because it is only revealed when no reconnect display is up.
		/// </para>
		/// </remarks>
		SystemAlert = 1000,
	}
}
