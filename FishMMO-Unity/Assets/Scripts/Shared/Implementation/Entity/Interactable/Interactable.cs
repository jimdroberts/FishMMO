using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

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

		[Header("ECA - Interaction")]
		[Tooltip("Triggers invoked server-side when a player successfully interacts with this object.")]
		[SerializeField]
		private List<Trigger> onInteractTriggers = new List<Trigger>();

		/// <inheritdoc />
		public List<Trigger> OnInteractTriggers => onInteractTriggers;

		/// <inheritdoc />
		/// <remarks>
		/// <para>
		/// Every entry is null-checked. A list element left empty in the inspector is an easy
		/// authoring slip and used to take the whole interaction down with a
		/// NullReferenceException, losing the triggers after it as well as the one that was blank.
		/// </para>
		/// <para>
		/// An interactable with no triggers at all is warned about once per interaction rather
		/// than passing silently. There is no default behaviour left in these classes — the ECA
		/// list is the entire implementation — so an empty list means the object does nothing when
		/// used, and that is indistinguishable from a broken object unless it says so.
		/// </para>
		/// </remarks>
		public bool ExecuteOnInteract(EventData eventData)
		{
			if (onInteractTriggers == null || onInteractTriggers.Count < 1)
			{
				Log.Warning("Interactable",
					$"'{Name}' ({GetType().Name}) was interacted with but has no OnInteract triggers configured, so nothing happened. " +
					"Interaction behaviour is defined entirely by ECA triggers; assign one on the prefab or scene object.");
				return false;
			}

			bool fired = false;
			for (int i = 0; i < onInteractTriggers.Count; ++i)
			{
				Trigger trigger = onInteractTriggers[i];
				if (trigger == null)
				{
					Log.Warning("Interactable",
						$"'{Name}' ({GetType().Name}) has an empty entry at index {i} of its OnInteract triggers; skipping it.");
					continue;
				}
				trigger.Execute(eventData);
				fired = true;
			}
			return fired;
		}

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

			/* Every process, not just the client. The name is a lookup key on the server: a
			 * cross-scene teleport is resolved by the GameObject name the world scene details
			 * cache was baked with, and the bake never sees Unity's "(Clone)" suffix. Stripping
			 * it only under !UNITY_SERVER made a prefab-spawned teleporter resolve in the editor
			 * and miss in a server build. */
			GameObject.name = TeleporterKey.Normalize(GameObject.name);

#if !UNITY_SERVER
			/* A plate is optional here, and the two ways of not having one are different. A
			 * character's plate is authored on its prefab and this only writes a row onto it. A
			 * chest or a harvest node has no plate at all until something asks to see it, and
			 * building one for every interactable in a zone on the chance that a player eventually
			 * points at it would be a GameObject per crate. So Awake writes to what is already
			 * there, and EnsureNameplate builds one for the rest at the moment the target frame
			 * needs it. */
			overheadNameplate = ResolveAuthoredNameplate();
			ApplyNameplateRows();
#endif
		}

#if !UNITY_SERVER
		/// <summary>
		/// Backing field for <see cref="OverheadNameplate"/>.
		/// </summary>
		private Nameplate overheadNameplate;

		/// <summary>
		/// The overhead plate this interactable's name and title are written onto, or null when it
		/// has none yet.
		/// </summary>
		/// <remarks>
		/// For an interactable that is also a character — a banker, a merchant — this is the
		/// character's own plate, so the title stacks under the name the naming system resolved
		/// rather than fighting it for the same anchor.
		/// </remarks>
		public Nameplate OverheadNameplate => overheadNameplate;

		/// <summary>
		/// Returns this interactable's plate, building one over it if it has none.
		/// </summary>
		/// <returns>The plate, or null when there is nothing to hang one on.</returns>
		/// <remarks>
		/// The built plate hangs at the top of the object's collider, which is where a caption over
		/// a chest belongs and what the target frame used to compute for its pooled label. It starts
		/// hidden, like every authored plate: being able to see a thing's name is the client's
		/// decision, not the thing's.
		/// </remarks>
		public Nameplate EnsureNameplate()
		{
			if (overheadNameplate != null)
			{
				return overheadNameplate;
			}

			float height = 1.0f;
			Collider collider = Transform != null ? Transform.GetComponent<Collider>() : null;
			if (collider != null)
			{
				collider.TryGetDimensions(out height, out float _);
			}

			overheadNameplate = Nameplate.GetOrCreate(Transform, new Vector3(0.0f, height, 0.0f));
			ApplyNameplateRows();
			return overheadNameplate;
		}

		/// <summary>The plate already on this object, if any.</summary>
		private Nameplate ResolveAuthoredNameplate()
		{
			ICharacter character = Transform != null ? Transform.GetComponent<ICharacter>() : null;
			if (character != null)
			{
				return character.CharacterNameplate;
			}
			return Transform != null ? Transform.GetComponentInChildren<Nameplate>(true) : null;
		}

		/// <summary>
		/// Writes this interactable's rows onto its plate.
		/// </summary>
		/// <remarks>
		/// The name row is written only for objects that are not characters. A character's name is
		/// the naming system's to resolve — it arrives from the server, asynchronously, and may
		/// replace the prefab name long after this runs — so writing the GameObject's name here
		/// would race it and sometimes win.
		/// </remarks>
		private void ApplyNameplateRows()
		{
			if (overheadNameplate == null)
			{
				return;
			}

			if (Transform.GetComponent<ICharacter>() == null)
			{
				overheadNameplate.SetLine(NameplateSlot.Name, Name);
			}

			if (!string.IsNullOrWhiteSpace(Title))
			{
				overheadNameplate.SetLine(NameplateSlot.InteractableType, $"<{Title}>", TitleColor);
			}
		}
#endif

		/// <summary>
		/// Registers this interactable in the scene object registry and assigns its ID.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Deliberately not in <c>Awake</c> under <c>#if UNITY_SERVER</c>, which is where it used
		/// to live. <c>UNITY_SERVER</c> is a build-target define, and the scene server also runs
		/// from the editor — where it is undefined. Every interactable therefore skipped
		/// registration there, kept ID 0, and sent 0 to every client in <see cref="WritePayload"/>;
		/// the server's own registry stayed empty, so <c>ValidateSceneObject</c> refused every
		/// interaction in the world. Nothing could be talked to, looted, or bound to outside a
		/// dedicated server build. This is the same gate that had already been found and removed
		/// from <c>NPC.OnStartServer</c>, for the same reason.
		/// </para>
		/// <para>
		/// <see cref="OnStartServer"/> is the correct home: FishNet invokes it only on a peer that
		/// is actually running a server, and it runs before the spawn message — and therefore
		/// before <see cref="WritePayload"/> — is built, so the ID a client receives is the one
		/// assigned here. Re-registration on pool reuse is a no-op that preserves the existing ID.
		/// </para>
		/// </remarks>
		public override void OnStartServer()
		{
			base.OnStartServer();

			SceneObject.Register(this);
		}

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
		/// Despawns this interactable, returning it to the object pool.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Routes through the owning <see cref="ObjectSpawner"/> when there is one, so the spawner
		/// can schedule a respawn. Falls back to despawning directly otherwise.
		/// </para>
		/// <para>
		/// The fallback is the important half. An interactable with no spawner — ground loot
		/// dropped by a kill, a container placed by script — used to hit a null-conditional and do
		/// nothing at all, so picking it up left the object spawned in the world forever: an
		/// invisible, already-looted pickup that every client kept observing. Pooled despawn is
		/// also what keeps world items inside the map's fixed memory budget rather than being
		/// destroyed and re-instantiated on every drop.
		/// </para>
		/// </remarks>
		public void Despawn()
		{
			ObjectSpawner spawner = ObjectSpawner;
			if (spawner != null)
			{
				spawner.Despawn(this);
				return;
			}

			if (base.IsServerStarted && NetworkObject != null && NetworkObject.IsSpawned)
			{
				NetworkManager.ServerManager.Despawn(NetworkObject, FishNet.Object.DespawnType.Pool);
			}
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

		/// <inheritdoc />
		/// <remarks>
		/// Range, and not being a corpse. The rate limit is no longer spent here — see
		/// <see cref="IInteractable.CanInteract"/> for why a question that answered by consuming a
		/// budget could not be asked by three callers at once.
		/// </remarks>
		/// <param name="character">The player character attempting to interact.</param>
		/// <returns>True if interaction is allowed, false otherwise.</returns>
		public virtual bool CanInteract(IPlayerCharacter character)
		{
			if (character == null)
			{
				return false;
			}

			/* A dead body does not trade, bank, or hand out work. This component may share its
			 * GameObject with an NPC that is currently a corpse, and while it is, the corpse is
			 * the only thing on that object anyone can interact with — which is the same rule the
			 * target resolver uses to decide which component a player meant. Without it, a player
			 * could open a shop on the merchant they had just killed. */
			if (InteractableResolver.IsCorpse(GameObject))
			{
				return false;
			}

			return InRange(character.Transform);
		}

		/// <inheritdoc />
		public bool TryConsumeInteractRateLimit(IPlayerCharacter character)
		{
			if (character == null)
			{
				return false;
			}
			if (character.NextInteractTime >= DateTime.UtcNow)
			{
				return false;
			}
			character.NextInteractTime = DateTime.UtcNow.AddMilliseconds(InteractRateLimit);
			return true;
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