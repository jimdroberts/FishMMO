using System;

namespace MeshyAI
{
	[Serializable]
	public class RefineTaskRequest
	{
		public string mode;
		public string preview_task_id;
		public bool enable_pbr;
		public string ai_model;
		public bool moderation;
	}
}