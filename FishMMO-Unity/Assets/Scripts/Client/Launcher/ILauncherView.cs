using System;
using HtmlAgilityPack;

namespace FishMMO.Client
{
	/// <summary>
	/// The presentation surface <see cref="ClientLauncher"/> drives. Implemented once per UI
	/// technology so the launcher's state machine, version check, and patch flow exist in
	/// exactly one place regardless of which UI is rendering them.
	/// </summary>
	/// <remarks>
	/// Members are expressed as intent ("show this status", "the progress bar is relevant now")
	/// rather than as widget manipulation, so each view is free to satisfy them however its
	/// technology prefers. That distinction is load-bearing: the UGUI implementation has no
	/// dedicated status element and has to borrow the progress label and reveal its parent
	/// group, while the UI Toolkit implementation simply writes to a status label. Encoding
	/// that difference in the interface would export one view's limitation to the other.
	/// </remarks>
	public interface ILauncherView
	{
		/// <summary>
		/// Sets the launcher window title (project name and version).
		/// </summary>
		void SetTitle(string title);

		/// <summary>
		/// Sets the label on the primary (Play/Connect/Update) button.
		/// </summary>
		void SetButtonText(string text);

		/// <summary>
		/// Enables or disables interaction with the primary button.
		/// </summary>
		void SetButtonInteractable(bool interactable);

		/// <summary>
		/// Replaces the primary button's click handler. Passing null clears it.
		/// </summary>
		/// <remarks>
		/// Implementations must replace rather than add. The state machine calls this on every
		/// transition, and an implementation that accumulated handlers would fire every action
		/// the launcher had ever been in.
		/// </remarks>
		void SetButtonAction(Action action);

		/// <summary>
		/// Sets the quit button's click handler. Called once during initialisation.
		/// </summary>
		void SetQuitAction(Action action);

		/// <summary>
		/// Reports the state of an in-progress download.
		/// </summary>
		/// <remarks>
		/// Takes the whole snapshot rather than a progress fraction and a prepared string, so a
		/// view can present as much of it as it has room for. Use
		/// <see cref="DownloadStats.ToDisplayString"/> for the standard line unless there is a
		/// reason to differ — it already omits whatever is not yet known, which matters because
		/// a rate and a remaining time do not exist for the first second of any transfer.
		/// </remarks>
		void SetProgress(DownloadStats stats);

		/// <summary>
		/// Shows or hides the progress bar. Hiding also resets progress to zero, so a later
		/// download never briefly displays the previous one's final value.
		/// </summary>
		void SetProgressVisible(bool visible);

		/// <summary>
		/// Displays a human-readable status or error message to the player.
		/// </summary>
		/// <remarks>
		/// The implementation is responsible for ensuring the message is actually visible —
		/// including activating whatever container holds it. Writing to a deactivated element
		/// silently loses the message, which previously left players facing a two-word button
		/// label and no explanation.
		/// </remarks>
		void ShowStatus(string message);

		/// <summary>
		/// Clears any status message so a stale error does not linger into a new state.
		/// </summary>
		void ClearStatus();

		/// <summary>
		/// Shows or hides the news pane. Hidden when no news URL is configured, which is a
		/// valid deployment rather than a failure.
		/// </summary>
		void SetNewsVisible(bool visible);

		/// <summary>
		/// Displays plain text in the news pane, used for the loading placeholder and for
		/// fetch errors. Distinct from <see cref="SetNewsContent"/> because these messages are
		/// launcher-authored strings, not remote content.
		/// </summary>
		void SetNewsMessage(string message);

		/// <summary>
		/// Renders fetched news content into the news pane.
		/// </summary>
		/// <remarks>
		/// Takes the parsed node rather than a pre-formatted string because the two views have
		/// no common text format: TextMeshPro and UI Toolkit disagree on tag casing, on how
		/// alignment is written, and — decisively — UI Toolkit has no equivalent of TMP's
		/// <c>&lt;link&gt;</c> tag at all. Each view converts the tree on its own terms.
		/// <para>
		/// <paramref name="content"/> is untrusted remote content. Implementations must bound
		/// their traversal depth and must route any link activation through
		/// <see cref="LauncherLinkPolicy"/>.
		/// </para>
		/// </remarks>
		/// <param name="content">Root of the extracted news fragment.</param>
		void SetNewsContent(HtmlNode content);

		/// <summary>
		/// Reports how much disk space the installation occupies, or null when it could not be
		/// determined.
		/// </summary>
		/// <remarks>
		/// Null is a real and expected outcome — a permission-restricted install directory, or
		/// a measurement that has not run yet. Views must show "unavailable" rather than "0 B",
		/// which would read as an empty installation.
		/// </remarks>
		void SetInstallSize(long? sizeBytes);

		/// <summary>
		/// Releases anything the view subscribed to. Called from the launcher's OnDestroy.
		/// </summary>
		/// <remarks>
		/// Present because a view is not necessarily a component with its own Unity lifecycle —
		/// the UGUI implementation is a plain class owned by the launcher, so something has to
		/// tell it when to let go. Implementations backed by a MonoBehaviour may leave this
		/// empty and clean up in OnDestroy instead.
		/// </remarks>
		void Teardown();
	}
}
