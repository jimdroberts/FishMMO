using System;
using System.Collections.Generic;

namespace MeshyAI
{
	[Serializable]
	public class MeshyTaskDetailsResponse
	{
		public string id;
		public MeshyModelUrls model_urls;
		public string thumbnail_url;
		public string prompt;
		public string art_style;
		public float progress;
		public long started_at;
		public long created_at;
		public long finished_at;
		public string status;
		public List<MeshyTextureUrlsObject> texture_urls;
		public int preceding_tasks;
		public MeshyTaskError task_error;
	}
}