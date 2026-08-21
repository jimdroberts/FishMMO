using System;
using HtmlAgilityPack;

namespace FishMMO.Client
{
	/// <summary>
	/// The presentation surface <see cref="ClientLauncher"/> drives, keeping the launcher's state
	/// machine, version check and patch flow in exactly one place rather than coupled to a
	/// widget tree.
	/// </summary>
	/// <remarks>
	/// Members are expressed as intent ("show this status", "the progress bar is relevant now")
	/// rather than as widget manipulation, so an implementation is free to satisfy them however
	/// its technology prefers. <see cref="UITKClientLauncher"/> is currently the only one, but the
	/// boundary is worth keeping: it is what stopped the version-check and patch logic from being
	/// forked per view while a second implementation existed, and it is why replacing the renderer
	/// again would not touch any of it.
	/// </remarks>
	public interface ILauncherView
	{
		/// <summary>
		/// Sets the launcher window title (project name and version).
		/// </summary>
		void SetTitle(string title);

		/// <summary>
		/// Shows or hides the entire launcher UI.
		/// </summary>
		/// <param name="visible">True to show the launcher, false to hide it.</param>
		/// <remarks>
		/// Called the moment the game scene is ready, before the launcher scene is unloaded.
		/// The unload is asynchronous and, in the editor, may not happen at all — the launcher
		/// scene is opened directly there rather than through Addressables, so the Addressables
		/// unload has no handle for it and silently does nothing. Hiding is synchronous and
		/// works either way, so the launcher cannot be left sitting behind the login screen.
		/// </remarks>
		void SetVisible(bool visible);

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
		/// Shows or hides the news pane.
		/// </summary>
		/// <remarks>
		/// The launcher keeps the pane visible in every ordinary case and fills it with a
		/// built-in summary when there is no live feed — hiding it collapsed the panel into a
		/// header stacked directly on a footer, which reads as a broken window rather than as a
		/// launcher with no news today.
		/// </remarks>
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
		/// Takes the parsed node rather than a pre-formatted string because UI Toolkit's rich text
		/// has no equivalent of a link tag: a news link cannot be markup inside a label, it has to
		/// become a real element that can receive a click. Handing the view a formatted string
		/// would make that impossible, so the view walks the tree itself.
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
		/// Present because a view is not required to be a component with its own Unity lifecycle.
		/// A plain class owned by the launcher has no OnDestroy of its own, so something has to
		/// tell it when to let go. An implementation backed by a MonoBehaviour —
		/// <see cref="UITKClientLauncher"/> is one — may leave this empty and clean up in
		/// OnDestroy instead.
		/// </remarks>
		void Teardown();
	}
}
