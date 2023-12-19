using System;
using FishNet.Transporting;
using FishNet.Object;
using UnityEngine;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(SceneObjectUID))]
	public abstract class Interactable : NetworkBehaviour, IInteractable
	{
		private const double INTERACT_RATE_LIMIT = 60.0f;

		private SceneObjectUID uid;

		public float InteractionRangeSqr = 5.0f;

		public int ID
		{
			get
			{
				return uid.ID;
			}
		}

		public Transform Transform { get; private set; }

		void Awake()
		{
			uid = gameObject.GetComponent<SceneObjectUID>();
			Transform = transform;
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
			if ((transform.position - Transform.position).sqrMagnitude < InteractionRangeSqr)
			{
				return true;
			}
			return false;
		}

		public virtual bool OnInteract(Character character)
		{
			if (character == null)
			{
				return false;
			}
			if (character.NextInteractTime < DateTime.UtcNow && InRange(character.Transform))
			{
				character.NextInteractTime = DateTime.UtcNow.AddMilliseconds(INTERACT_RATE_LIMIT);
#if !UNITY_SERVER
				ClientManager.Broadcast(new InteractableBroadcast()
				{
					InteractableID = ID,
				}, Channel.Reliable);
#endif
				return true;
			}
			return false;
		}
	}
}