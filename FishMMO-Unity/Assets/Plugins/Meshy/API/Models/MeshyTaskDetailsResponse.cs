using System;
using System.Collections.Generic;

namespace MeshyAI
{
	/// <summary>
	/// Represents the detailed response for a Meshy API task, including model and texture URLs, status, and metadata.
	/// </summary>
	[Serializable]
	public class MeshyTaskDetailsResponse
	{
		/// <summary>Unique identifier for the task.</summary>
		public string id;
		/// <summary>URLs for the generated 3D model files.</summary>
		public MeshyModelUrls model_urls;
		/// <summary>URL to the generated thumbnail image.</summary>
		public string thumbnail_url;
		/// <summary>The prompt used for generation.</summary>
		public string prompt;
		/// <summary>The art style used for generation.</summary>
		public string art_style;
		/// <summary>Progress of the task (0-1).</summary>
		public float progress;
		/// <summary>Unix timestamp when the task started.</summary>
		public long started_at;
		/// <summary>Unix timestamp when the task was created.</summary>
		public long created_at;
		/// <summary>Unix timestamp when the task finished.</summary>
		public long finished_at;
		/// <summary>Status string of the task.</summary>
		public string status;
		/// <summary>List of texture URLs for the generated model.</summary>
		public List<MeshyTextureUrlsObject> texture_urls;
		/// <summary>Number of preceding tasks in the queue.</summary>
		public int preceding_tasks;
		/// <summary>Error information if the task failed.</summary>
		public MeshyTaskError task_error;
	}
}