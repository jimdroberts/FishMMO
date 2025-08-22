#if UNITY_EDITOR
using System;

namespace MeshyAI
{
	/// <summary>
	/// Represents a simple response from the Meshy API for a v2 task, containing a result string.
	/// </summary>
	[Serializable]
	public class MeshyV2TaskResponse
	{
		/// <summary>
		/// The result string returned by the API.
		/// </summary>
		public string result;
	}
}
#endif