namespace FishMMO.Client
{
	/// <summary>
	/// Defines the possible states of the launcher's main action button and overall UI.
	/// </summary>
	public enum LauncherState
	{
		/// <summary>
		/// The launcher is loading news content.
		/// </summary>
		LoadingNews,
		/// <summary>
		/// The launcher is connecting to the server.
		/// </summary>
		Connecting,
		/// <summary>
		/// The launcher is checking the client version against the server.
		/// </summary>
		CheckingVersion,
		/// <summary>
		/// The launcher is downloading a patch.
		/// </summary>
		DownloadingPatch,
		/// <summary>
		/// The launcher is applying a downloaded patch.
		/// </summary>
		ApplyingPatch,
		/// <summary>
		/// The launcher is ready for the user to play.
		/// </summary>
		ReadyToPlay,
		/// <summary>
		/// The client version is ahead of the server version.
		/// </summary>
		ClientAhead,
		/// <summary>
		/// The launcher failed to connect to the server.
		/// </summary>
		ConnectionFailed,
		/// <summary>
		/// The launcher failed to check the client version.
		/// </summary>
		VersionCheckFailed,
		/// <summary>
		/// The launcher failed to download the patch.
		/// </summary>
		PatchDownloadFailed,
		/// <summary>
		/// The updater process failed.
		/// </summary>
		UpdaterFailed,
		/// <summary>
		/// The launcher failed to launch the game client.
		/// </summary>
		LaunchFailed,
		/// <summary>
		/// There was an error with the client or server version.
		/// </summary>
		VersionError,
	}
}