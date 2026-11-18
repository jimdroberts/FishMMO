using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FishMMO.Shared
{
	/// <summary>
	/// Template defining a capture point's parameters for PvP or general objective capture.
	/// </summary>
	[CreateAssetMenu(fileName = "New Capture Point", menuName = "FishMMO/Interactable/Capture Point", order = 1)]
	public class CapturePointTemplate : CachedScriptableObject<CapturePointTemplate>, ICachedObject
	{
		/// <summary>
		/// Addressable reference to the icon sprite for this capture point.
		/// </summary>
		public AssetReferenceSprite IconReference;

		/// <summary>
		/// The loaded icon sprite. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Sprite loadedIcon;

		/// <summary>
		/// The icon for this capture point (loaded at runtime on client).
		/// </summary>
		public Sprite Icon { get { return this.loadedIcon; } }

		/// <summary>
		/// Description displayed in tooltips or UI.
		/// </summary>
		[TextArea(2, 4)]
		public string Description;

		/// <summary>
		/// Score value awarded when this point is captured.
		/// </summary>
		[Min(1)]
		public int PointValue = 1;

		/// <summary>
		/// Number of interactions required to capture this point.
		/// </summary>
		[Min(1)]
		public int InteractionsToCapture = 1;

		/// <summary>
		/// Seconds of inactivity after which an unfinished capture resets. 0 disables decay.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Without decay a partial capture is permanent: a player who applies four of five
		/// interactions and walks away leaves the point sitting at four forever, so the next person
		/// to touch it — possibly hours later, possibly an enemy — completes a capture they did
		/// almost none of. It also makes <see cref="InteractionsToCapture"/> meaningless as a cost,
		/// since the total can be paid in arbitrarily small instalments across a whole session.
		/// </para>
		/// <para>
		/// The timer restarts on every interaction that adds progress, so a player who keeps
		/// working at the point never loses it; it only expires once they stop.
		/// </para>
		/// </remarks>
		[Tooltip("Seconds of inactivity before an unfinished capture resets to zero. 0 = never decays.")]
		[Min(0.0f)]
		public float ProgressDecaySeconds = 30.0f;

		/// <summary>
		/// The display name of this capture point template.
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Called when the capture point template is loaded into cache. Loads the icon on the client.
		/// </summary>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(CapturePointTemplate))
				return;

#if !UNITY_SERVER
			if (IconReference != null && IconReference.RuntimeKeyIsValid())
			{
				IconReference.LoadAssetAsync<Sprite>().Completed += (handle) =>
				{
					if (handle.Status == AsyncOperationStatus.Succeeded)
						loadedIcon = handle.Result;
				};
			}
#endif
		}

		/// <summary>
		/// Called when the capture point template is unloaded from cache. Releases the icon on the client.
		/// </summary>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			if (typeName == nameof(CapturePointTemplate))
			{
#if !UNITY_SERVER
				if (IconReference != null && IconReference.IsValid())
				{
					IconReference.ReleaseAsset();
				}
				loadedIcon = null;
#endif
			}

			base.OnUnload(typeName, resourceName, resourceID);
		}
	}
}