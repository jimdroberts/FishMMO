using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client
{
	public class UIMinimap : UICharacterControl
	{
		public Camera MinimapCamera;
		
		public override void OnStarting()
		{
			if (MinimapCamera == null)
			{
				return;
			}
			MinimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
		}

		public override void OnDestroying()
		{
		}

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();
#if !UNITY_SERVER
			Vector3 newPosition = Character.MeshRoot.position;
			newPosition.y = 1000.0f;

			MinimapCamera.transform.position = newPosition;
#endif
		}

		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();
		}

		void LateUpdate()
		{
#if !UNITY_SERVER
			if (Character == null)
			{
				return;
			}
			Vector3 newPosition = Character.MeshRoot.position;
			newPosition.y = 1000.0f;

			MinimapCamera.transform.position = newPosition;
#endif
		}
	}
}