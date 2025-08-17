using UnityEngine;

namespace MeshyAI
{
	/// <summary>
	/// This ScriptableObject holds the API key for the Meshy.ai service,
	/// separating it from the editor window code. This makes the key
	/// easy to manage, share, and reuse across projects without editing
	/// the script itself.
	/// </summary>
	[CreateAssetMenu(fileName = "MeshySettings", menuName = "Meshy/Meshy API Settings")]
	public class MeshySettings : ScriptableObject
	{
		[Tooltip("Your API key from Meshy.ai. Get this from your Meshy account settings.")]
		public string meshyApiKey = "YOUR_API_KEY_HERE";
	}
}