using System;
using FishNet.Object;
using UnityEngine;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(SceneObjectUID))]
	public abstract class Interactable : NetworkBehaviour, IInteractable
	{
		private const double INTERACT_RATE_LIMIT = 60.0f;

		private SceneObjectUID uid;

		public float InteractionRange = 3.5f;

		private float interactionRangeSqr;

		public int ID
		{
			get
			{
				return uid.ID;
			}
		}

		public Transform Transform { get; private set; }

		public virtual string Title { get { return "Interactable"; } }

		void Awake()
		{
			uid = gameObject.GetComponent<SceneObjectUID>();
			Transform = transform;

			interactionRangeSqr = InteractionRange * InteractionRange;

			OnStarting();
		}

		public virtual void OnStarting() { }

		public bool InRange(Transform transform)
		{
			if (transform == null)
			{
				return false;
			}
			if (Transform == null)
			{
				return false;
			}
			if ((Transform.position - transform.position).sqrMagnitude < interactionRangeSqr)
			{
				return true;
			}
			return false;
		}

		public virtual bool CanInteract(Character character)
		{
			if (character != null &&
				character.NextInteractTime < DateTime.UtcNow && InRange(character.Transform))
			{
				character.NextInteractTime = DateTime.UtcNow.AddMilliseconds(INTERACT_RATE_LIMIT);

				return true;
			}
			return false;
		}

#if UNITY_EDITOR
		public Color GizmoColor = Color.green;

		void OnDrawGizmos()
		{
			Collider collider = gameObject.GetComponent<Collider>();
			if (collider != null)
			{
				collider.DrawGizmo(GizmoColor);
			}
		}
#endif
	}
}