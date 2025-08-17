using System;

namespace MeshyAI
{
	/// <summary>
	/// Represents an error returned by a Meshy API task.
	/// </summary>
	[Serializable]
	public class MeshyTaskError
	{
		/// <summary>
		/// The error message describing what went wrong.
		/// </summary>
		public string message;
	}
}