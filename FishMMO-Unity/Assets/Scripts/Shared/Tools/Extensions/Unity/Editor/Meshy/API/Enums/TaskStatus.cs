namespace MeshyAI
{
	/// <summary>
	/// Task status for Meshy API operations.
	/// </summary>
	public enum TaskStatus
	{
		Idle,
		CheckingBalance,
		StartingPreview,
		StartingRefine,
		Generating,
		Refining,
		Downloading,
		Succeeded,
		Failed,
		Canceled,
		RefineSucceeded,
		PreviewSucceeded
	}
}