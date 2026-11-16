using UnityEngine;
#if !UNITY_SERVER
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#endif

namespace FishMMO.Shared
{
	/// <summary>
	/// The face of a scene teleporter: a quad in its opening that shows the place it leads to. Both scenes cannot be loaded
	/// at once, so the view is a cubemap baked at the destination (PortalViewBaker, kept in Assets/Generated and shipped in
	/// a build-only Addressables group), looked up through the quad by view direction in the FishMMO/Portal shader: a
	/// window onto the far side with the right perspective for anything past a few metres, a swirl and a glowing rim over
	/// it. With no baked view (an editor that has not baked, a destination not yet set) only the glow shows.
	/// </summary>
	/// <remarks>
	/// Set up in the scene by the cut, the gate tool or the Dashboard's "Bake portal views": the destination id is the
	/// teleporter's, and <see cref="DestinationYaw"/> is the arrival's facing, so stepping through the quad looks the way the
	/// traveller will face on arrival. Client only; on the server it does nothing.
	/// </remarks>
	public class PortalSurface : MonoBehaviour
	{
		/// <summary>Addressables key prefix of a baked view: the destination id follows.</summary>
		public const string AddressPrefix = "PortalView/";

		[Tooltip("The destination this portal leads to (its TeleporterDestination id); empty to take the parent SceneTeleporter's.")]
		[SerializeField] private string destinationID;

		[Tooltip("The arrival's facing at the destination, degrees about up: what lies ahead on arrival is what shows ahead.")]
		[SerializeField] private float destinationYaw;

		public string DestinationID { get => destinationID; set => destinationID = value; }
		public float DestinationYaw { get => destinationYaw; set => destinationYaw = value; }

		/// <summary>The Addressables key of a destination's baked view.</summary>
		public static string AddressOf(string destination) => AddressPrefix + destination;

#if !UNITY_SERVER
		private static readonly int CubeId = Shader.PropertyToID("_PortalCube");
		private static readonly int YawId = Shader.PropertyToID("_PortalYaw");
		private static readonly int HasViewId = Shader.PropertyToID("_PortalHasView");

		private MeshRenderer surface;
		private MaterialPropertyBlock block;
		private AsyncOperationHandle<Cubemap> view;

		private void OnEnable()
		{
			surface = GetComponent<MeshRenderer>();
			if (surface == null)
			{
				return;
			}
			block ??= new MaterialPropertyBlock();
			// Walking through the quad from the side it faces carries the traveller along -forward; on arrival they face
			// destinationYaw. The view is turned by the difference, so ahead through the portal is ahead on arrival.
			float entryYaw = transform.eulerAngles.y + 180f;
			block.SetFloat(YawId, Mathf.DeltaAngle(entryYaw, destinationYaw) * Mathf.Deg2Rad);
			block.SetFloat(HasViewId, 0f);
			surface.SetPropertyBlock(block);

			string id = ResolveDestination();
			if (string.IsNullOrEmpty(id))
			{
				return;
			}
			string key = AddressOf(id);
			Addressables.LoadResourceLocationsAsync(key, typeof(Cubemap)).Completed += locations =>
			{
				bool found = locations.Status == AsyncOperationStatus.Succeeded && locations.Result != null && locations.Result.Count > 0;
				Addressables.Release(locations);
				if (!found || this == null || !isActiveAndEnabled)
				{
					return;
				}
				view = Addressables.LoadAssetAsync<Cubemap>(key);
				view.Completed += loaded =>
				{
					if (loaded.Status != AsyncOperationStatus.Succeeded || loaded.Result == null || surface == null)
					{
						return;
					}
					block.SetTexture(CubeId, loaded.Result);
					block.SetFloat(HasViewId, 1f);
					surface.SetPropertyBlock(block);
				};
			};
		}

		private void OnDisable()
		{
			if (view.IsValid())
			{
				Addressables.Release(view);
			}
			view = default;
		}

		private string ResolveDestination()
		{
			if (!string.IsNullOrEmpty(destinationID))
			{
				return destinationID;
			}
			SceneTeleporter teleporter = GetComponentInParent<SceneTeleporter>();
			return teleporter != null ? teleporter.DestinationID : null;
		}
#endif
	}
}
