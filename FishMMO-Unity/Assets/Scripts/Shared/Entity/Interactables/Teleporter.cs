using UnityEngine;

namespace FishMMO.Shared
{
	public class Teleporter : Interactable
	{
		public Transform Target;

		public override string Title { get { return "Teleporter"; } }

		public override bool CanInteract(Character character)
		{
			if (!base.CanInteract(character))
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

#if UNITY_EDITOR
		void OnDrawGizmos()
		{
			if (Target == null)
			{
				return;
			}

			Gizmos.color = GizmoColor;
			Gizmos.DrawWireCube(Target.position, Vector3.one);
			Gizmos.DrawLine(transform.position, Target.position);
		}
#endif
	}
}