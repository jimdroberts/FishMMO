using UnityEngine;

namespace FishMMO.Shared
{
	public class Teleporter : Interactable
	{
		public Transform Target;

		public override bool OnInteract(Character character)
		{
			if (!base.OnInteract(character))
			{
				return false;
			}

#if UNITY_SERVER
			if (character.IsTeleporting)
			{
				return false;
			}

			if (Target != null)
			{
				// move the character
				character.Motor.SetPositionAndRotationAndVelocity(Target.position, Target.rotation, Vector3.zero);
				return true;
			}

			character.Teleport(gameObject.name);
#endif
			return true;
		}
	}
}