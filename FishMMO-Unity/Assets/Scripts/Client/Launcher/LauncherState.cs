namespace FishMMO.Client
{
	/// <summary>
	/// Defines the possible states of the launcher's main action button and overall UI.
	/// </summary>
	public enum LauncherState
	{
		LoadingNews,
		Connecting,
		CheckingVersion,
		DownloadingPatch,
		ApplyingPatch,
		ReadyToPlay,
		ClientAhead,
		ConnectionFailed,
		VersionCheckFailed,
		PatchDownloadFailed,
		UpdaterFailed,
		LaunchFailed,
		VersionError,
	}
}