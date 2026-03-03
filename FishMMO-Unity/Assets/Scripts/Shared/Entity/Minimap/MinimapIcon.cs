using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Spawns a flat sprite indicator on the Minimap layer so it is visible to the overhead minimap camera.
	/// Attach this component to any object (interactable, player, NPC) that should appear on the minimap.
	/// The icon is a child SpriteRenderer placed at a fixed Y height so only the minimap camera can see it.
	/// </summary>
	public class MinimapIcon : MonoBehaviour
	{
		/// <summary>
		/// Height at which the minimap icon is positioned. Must be below the minimap camera (Y = 1000).
		/// </summary>
		private const float ICON_HEIGHT = 999.0f;

		/// <summary>
		/// Name of the Unity layer used for minimap icons.
		/// This layer must exist in the project settings and be included in the minimap camera culling mask.
		/// </summary>
		public const string MINIMAP_LAYER = "Minimap";

		/// <summary>
		/// The sprite displayed on the minimap for this object.
		/// </summary>
		[Tooltip("Sprite displayed on the minimap for this object.")]
		public Sprite Icon;

		/// <summary>
		/// Uniform scale of the icon on the minimap.
		/// </summary>
		[Tooltip("Uniform scale of the icon on the minimap.")]
		public float IconScale = 1.0f;

		/// <summary>
		/// Tint color of the minimap icon.
		/// </summary>
		[Tooltip("Tint color of the minimap icon.")]
		public Color IconColor = Color.white;

		/// <summary>
		/// The child GameObject that holds the SpriteRenderer for the minimap icon.
		/// </summary>
		private GameObject iconObject;

		void Start()
		{
#if !UNITY_SERVER
			if (Icon == null)
			{
				return;
			}

			int layer = LayerMask.NameToLayer(MINIMAP_LAYER);
			if (layer < 0)
			{
				return;
			}

			iconObject = new GameObject("MinimapIcon");
			iconObject.transform.SetParent(transform, false);
			iconObject.layer = layer;

			// Face the minimap camera (looking straight down from Y = 1000)
			iconObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
			iconObject.transform.localScale = Vector3.one * IconScale;

			SpriteRenderer sr = iconObject.AddComponent<SpriteRenderer>();
			sr.sprite = Icon;
			sr.color = IconColor;
			sr.sortingOrder = 100;
#endif
		}

#if !UNITY_SERVER
		void LateUpdate()
		{
			if (iconObject == null)
			{
				return;
			}

			// Keep the icon at a fixed world-space height so the minimap camera can see it
			Vector3 pos = transform.position;
			pos.y = ICON_HEIGHT;
			iconObject.transform.position = pos;

			// Keep the icon facing up regardless of parent rotation
			iconObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
		}
#endif

		void OnDestroy()
		{
#if !UNITY_SERVER
			if (iconObject != null)
			{
				Destroy(iconObject);
				iconObject = null;
			}
#endif
		}
	}
}