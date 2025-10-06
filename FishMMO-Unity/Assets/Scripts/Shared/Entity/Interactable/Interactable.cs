using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for interactable objects in the game world. Handles interaction logic, network payloads, and UI display.
	/// Implements IInteractable and ISpawnable for scene registration and spawning.
	/// </summary>
	public abstract class Interactable : NetworkBehaviour, IInteractable, ISpawnable
	{
		/// <summary>
		/// The default rate limit (in milliseconds) between allowed interactions.
		/// </summary>
		private const double INTERACT_RATE_LIMIT = 60.0f;

		/// <summary>
		/// The maximum distance (in units) at which a player can interact with this object.
		/// </summary>
		public float InteractionRange = 3.5f;

		/// <summary>
		/// The squared interaction range, used for efficient distance checks.
		/// </summary>
		private float interactionRangeSqr;

		/// <summary>
		/// Event invoked when this object is despawned.
		/// </summary>
#pragma warning disable CS0414
		public event Action<ISpawnable> OnDespawn;
#pragma warning restore CS0414

		/// <summary>
		/// Reference to the object spawner responsible for spawning/despawning this object.
		/// </summary>
		public ObjectSpawner ObjectSpawner { get; set; }

		/// <summary>
		/// Settings for spawning this object (e.g., prefab, spawn rules).
		/// </summary>
		public SpawnableSettings SpawnableSettings { get; set; }

		/// <summary>
		/// Unique ID for this interactable object (used for network sync).
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// The transform of this object in the scene.
		/// </summary>
		public Transform Transform { get; private set; }

		/// <summary>
		/// The GameObject associated with this interactable.
		/// </summary>
		public GameObject GameObject { get => this.gameObject; }

		/// <summary>
		/// The name of this interactable object (defaults to GameObject name).
		/// </summary>
		public virtual string Name { get { return GameObject.name; } }

		/// <summary>
		/// The display title for this interactable, shown in the UI.
		/// </summary>
		public virtual string Title { get { return "Interactable"; } }

		/// <summary>
		/// The color of the title displayed for this interactable in the UI.
		/// </summary>
		public virtual Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.forestGreen); } }

		/// <summary>
		/// The rate limit (in milliseconds) between allowed interactions for this object.
		/// </summary>
		public virtual double InteractRateLimit { get { return INTERACT_RATE_LIMIT; } }

		void Awake()
		{
			Transform = transform;
			interactionRangeSqr = InteractionRange * InteractionRange;

			OnAwake();
#if !UNITY_SERVER
			GameObject.name = GameObject.name.Replace("(Clone)", "");
			ICharacter character = Transform.GetComponent<ICharacter>();
			if (character != null &&
				!string.IsNullOrWhiteSpace(Title))
			{
				string hex = TitleColor.ToHex();
				if (!string.IsNullOrWhiteSpace(hex))
				{
					character.CharacterGuildLabel.text = $"<<color=#{hex}>{Title}</color>>";
				}
			}
		}
#else
			SceneObject.Register(this);
		}
#endif

		/// <summary>
		/// Called when the object is destroyed. Unregisters this interactable from the scene.
		/// </summary>
		void OnDestroy()
		{
			SceneObject.Unregister(this);
		}

		/// <summary>
		/// Reads network payload data for this interactable, setting its ID and registering it in the scene.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="reader">The network reader.</param>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			ID = reader.ReadInt64();
			SceneObject.Register(this, true);
		}

		/// <summary>
		/// Writes network payload data for this interactable, sending its ID to the writer.
		/// </summary>
		/// <param name="connection">The network connection.</param>
		/// <param name="writer">The network writer.</param>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);
		}

		/// <summary>
		/// Called when the object is awakened. Override to implement custom initialization logic.
		/// </summary>
		public virtual void OnAwake() { }

		/// <summary>
		/// Despawns this interactable using the assigned ObjectSpawner.
		/// </summary>
		public void Despawn()
		{
			ObjectSpawner?.Despawn(this);
		}

		/// <summary>
		/// Resets the state of this interactable, clearing event handlers and spawn settings.
		/// </summary>
		/// <param name="asServer">True if called on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			OnDespawn = null;
			SpawnableSettings = null;
		}

		/// <summary>
		/// Returns true if the specified transform is within interaction range of this object.
		/// Uses squared distance for efficiency.
		/// </summary>
		/// <param name="transform">The transform to check range against.</param>
		/// <returns>True if in range, false otherwise.</returns>
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

		/// <summary>
		/// Returns true if the specified player character can interact with this object.
		/// Checks rate limiting and range before allowing interaction.
		/// </summary>
		/// <param name="character">The player character attempting to interact.</param>
		/// <returns>True if interaction is allowed, false otherwise.</returns>
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