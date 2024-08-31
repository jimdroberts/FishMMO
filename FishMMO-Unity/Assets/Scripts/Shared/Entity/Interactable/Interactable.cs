using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class Interactable : NetworkBehaviour, IInteractable, ISpawnable
	{
		private const double INTERACT_RATE_LIMIT = 60.0f;

		public float InteractionRange = 3.5f;

		private float interactionRangeSqr;

#pragma warning disable CS0414
		public event Action<ISpawnable> OnDespawn;
#pragma warning restore CS0414

		public ObjectSpawner ObjectSpawner { get; set; }
		public Spawnable SpawnTemplate { get; set; }

		public long ID { get; set; }

		public Transform Transform { get; private set; }
		public GameObject GameObject { get => this.gameObject; }

		public virtual string Title { get { return "Interactable"; } }
		public virtual Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.forestGreen); } }

		public virtual double InteractRateLimit { get { return INTERACT_RATE_LIMIT; } }

		void Awake()
		{
			Transform = transform;
			interactionRangeSqr = InteractionRange * InteractionRange;

			OnStarting();
#if !UNITY_SERVER
		}
#else
			SceneObject.Register(this);
		}

		void OnDestroy()
		{
			SceneObject.Unregister(this);
		}
#endif

		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			ID = reader.ReadInt64();
		}

		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);
		}

		public virtual void OnStarting() { }

		public void Despawn()
		{
			ObjectSpawner?.Despawn(this);
		}

		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			OnDespawn = null;
			SpawnTemplate = null;
		}

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

		public virtual bool CanInteract(IPlayerCharacter character)
		{
			if (character != null &&
				character.NextInteractTime < DateTime.UtcNow && InRange(character.Transform))
			{
				character.NextInteractTime = DateTime.UtcNow.AddMilliseconds(InteractRateLimit);

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