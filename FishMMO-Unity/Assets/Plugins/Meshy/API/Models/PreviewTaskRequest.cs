using System;

namespace MeshyAI
{
	/// <summary>
	/// Represents the request body for starting a preview generation task with the Meshy API.
	/// </summary>
	[Serializable]
	public class PreviewTaskRequest
	{
		/// <summary>Generation mode (e.g., "preview").</summary>
		public string mode;
		/// <summary>Text prompt describing the desired 3D model.</summary>
		public string prompt;
		/// <summary>Art style to use for generation.</summary>
		public string art_style;
		/// <summary>Negative prompt to avoid certain features.</summary>
		public string negative_prompt;
		/// <summary>Random seed for generation.</summary>
		public int seed;
		/// <summary>AI model version to use.</summary>
		public string ai_model;
		/// <summary>Mesh topology (e.g., "quad" or "triangle").</summary>
		public string topology;
		/// <summary>Target polygon count for the generated model.</summary>
		public int target_polycount;
		/// <summary>Whether to remesh the model.</summary>
		public bool should_remesh;
		/// <summary>Symmetry mode for generation.</summary>
		public string symmetry_mode;
		/// <summary>Whether to enable moderation for the prompt.</summary>
		public bool moderation;
	}
}