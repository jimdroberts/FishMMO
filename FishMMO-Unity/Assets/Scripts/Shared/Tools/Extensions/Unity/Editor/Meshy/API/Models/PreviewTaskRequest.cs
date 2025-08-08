using System;

namespace MeshyAI
{
	[Serializable]
	public class PreviewTaskRequest
	{
		public string mode;
		public string prompt;
		public string art_style;
		public string negative_prompt;
		public int seed;
		public string ai_model;
		public string topology;
		public int target_polycount;
		public bool should_remesh;
		public string symmetry_mode;
		public bool moderation;
	}
}