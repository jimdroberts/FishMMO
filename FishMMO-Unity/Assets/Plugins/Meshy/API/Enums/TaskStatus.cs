#if UNITY_EDITOR
namespace MeshyAI
{
	/// <summary>
	/// Represents the status of a Meshy API task operation.
	/// </summary>
	public enum TaskStatus
	{
		/// <summary>Task is idle.</summary>
		Idle,
		/// <summary>Checking user balance.</summary>
		CheckingBalance,
		/// <summary>Starting preview generation.</summary>
		StartingPreview,
		/// <summary>Starting refine operation.</summary>
		StartingRefine,
		/// <summary>Model is being generated.</summary>
		Generating,
		/// <summary>Model is being refined.</summary>
		Refining,
		/// <summary>Model or textures are being downloaded.</summary>
		Downloading,
		/// <summary>Task succeeded.</summary>
		Succeeded,
		/// <summary>Task failed.</summary>
		Failed,
		/// <summary>Task was canceled.</summary>
		Canceled,
		/// <summary>Refine operation succeeded.</summary>
		RefineSucceeded,
		/// <summary>Preview operation succeeded.</summary>
		PreviewSucceeded
	}
}
#endif