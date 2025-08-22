#if UNITY_EDITOR
using System;

namespace MeshyAI
{
	/// <summary>
	/// Represents the request body for starting a refine task with the Meshy API.
	/// </summary>
	[Serializable]
	public class RefineTaskRequest
	{
		/// <summary>Refine mode (e.g., "refine").</summary>
		public string mode;
		/// <summary>ID of the preview task to refine.</summary>
		public string preview_task_id;
		/// <summary>Whether to enable physically based rendering (PBR).</summary>
		public bool enable_pbr;
		/// <summary>AI model version to use.</summary>
		public string ai_model;
		/// <summary>Whether to enable moderation for the prompt.</summary>
		public bool moderation;
	}
}
#endif