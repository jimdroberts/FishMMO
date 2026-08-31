using FishNet.Object;
using FishNet.Utility.Extension;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
#if !UNITY_SERVER
using FishNet.Component.Animating;
#endif
using FishMMO.Logging;
using FishMMO.Shared.Core;
namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for all networked character entities in the game.
	/// Provides core properties, behaviour registration, flag management, and prefab/model instantiation.
	/// </summary>
	/// <remarks>
	/// <b>Why the distance LOD is required.</b> State forwarding is off on every character, so a
	/// <c>NetworkTransform</c> is the only thing that carries a character's position to anyone
	/// observing it — and an unfiltered <c>NetworkTransform</c> sends every observer every update at
	/// full tick rate regardless of distance. <see cref="NetworkTransformDistanceLod"/> is what
	/// installs the per-observer send filter that bands those updates by distance; without it a
	/// character is the single largest line in every observer's budget. It in turn requires the
	/// <c>NetworkTransform</c> itself, so this one attribute pins the whole interpolated transport
	/// onto every character that ships.
	/// </remarks>
	[RequireComponent(typeof(NetworkObject))]
	[RequireComponent(typeof(NetworkTransformDistanceLod))]
	public abstract class BaseCharacter : NetworkBehaviour, ICharacter
	{
#if !UNITY_SERVER
		/// <summary>
		/// NetworkAnimator component for synchronizing animation parameters across the network.
		/// Configured at runtime after the character model is instantiated.
		/// </summary>
		public NetworkAnimator NetworkAnimator { get; private set; }
#endif
#if !UNITY_SERVER
		/// <summary>
		/// Client-side dictionary mapping character IDs to their instances for quick lookup.
		/// This is populated when characters are read from the network payload and cleared when they are destroyed.
		/// Characters are also removed from this dictionary when their state is reset, such as during despawning or scene transitions.
		/// </summary>
		public static Dictionary<long, ICharacter> ClientCharacters = new Dictionary<long, ICharacter>();
#endif
		/// <summary>
		/// Dictionary mapping behaviour interface types to their implementations for this character.
		/// </summary>
		protected Dictionary<Type, ICharacterBehaviour> Behaviours = new Dictionary<Type, ICharacterBehaviour>();

		/// <summary>
		/// Unique network identifier for this character.
		/// </summary>
		public long ID { get; set; }
		/// <summary>
		/// The name of the character, mapped to the GameObject's name.
		/// </summary>
		public string Name { get { return gameObject.name; } }
		/// <summary>
		/// Cached reference to the character's Transform component.
		/// </summary>
		public Transform Transform { get; private set; }
		/// <summary>
		/// Cached reference to the character's GameObject.
		/// </summary>
		public GameObject GameObject { get; private set; }

		/// <summary>
		/// The current local TimeManager tick for this character's context.
		/// Exposed as a convenience for callers that need the absolute tick.
		/// </summary>
		public uint LocalTick
		{
			get { return base.TimeManager != null ? base.TimeManager.LocalTick : 0u; }
		}
		/// <summary>
		/// Collider attached to the character, used for physics and interactions.
		/// </summary>
		public Collider Collider { get; set; }
		/// <summary>
		/// Indicates if the character is currently teleporting. Override in derived classes for custom logic.
		/// </summary>
		public virtual bool IsTeleporting => false;
		/// <summary>
		/// Bitwise flags representing character state and attributes.
		/// </summary>
		public int Flags { get; set; }

		/// <summary>
		/// Enables the specified character flags using bitwise operations.
		/// </summary>
		/// <param name="flags">Flags to enable.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void EnableFlags(CharacterFlags flags)
		{
			int characterFlags = Flags;
			characterFlags.EnableBit(flags);
			Flags = characterFlags;
		}
		/// <summary>
		/// Disables the specified character flags using bitwise operations.
		/// </summary>
		/// <param name="flags">Flags to disable.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void DisableFlags(CharacterFlags flags)
		{
			int characterFlags = Flags;
			characterFlags.DisableBit(flags);
			Flags = characterFlags;
		}

		/// <summary>
		/// Checks if the specified character flags are enabled.
		/// </summary>
		/// <param name="flags">Flags to check.</param>
		/// <returns>True if the flags are enabled; otherwise, false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsFlagged(CharacterFlags flags)
		{
			return Flags.IsFlagged(flags);
		}

		[SerializeField]
		private WorldLabel characterNameLabel;
		/// <summary>
		/// The label displaying the character's name above their model.
		/// </summary>
		public WorldLabel CharacterNameLabel { get { return this.characterNameLabel; } set { this.characterNameLabel = value; } }
		[SerializeField]
		private WorldLabel characterGuildLabel;
		/// <summary>
		/// The label displaying the character's guild above their model.
		/// </summary>
		public WorldLabel CharacterGuildLabel { get { return this.characterGuildLabel; } set { this.characterGuildLabel = value; } }

		[SerializeField]
		private Transform meshRoot;
		/// <summary>
		/// The root transform for the character's mesh/model hierarchy.
		/// </summary>
		public Transform MeshRoot { get { return this.meshRoot; } }

#if !UNITY_SERVER
		/// <summary>
		/// Instantiates the character's race model prefab at the specified index and attaches it to the mesh root.
		/// Removes any previous child models except for labels and special points.
		/// </summary>
		/// <param name="raceTemplate">The race template containing model references.</param>
		/// <param name="modelIndex">The index of the model to instantiate.</param>
		public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex)
		{
			InstantiateRaceModelFromIndex(raceTemplate, modelIndex, CharacterGender.Unspecified);
		}

		/// <summary>
		/// Instantiates the character's race model prefab at the specified gender and index and attaches it to the mesh root.
		/// Removes any previous child models except for labels and special points.
		/// </summary>
		/// <param name="raceTemplate">The race template containing model references.</param>
		/// <param name="modelIndex">The index of the model to instantiate.</param>
		/// <param name="gender">The model gender to request.</param>
		public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender)
		{
			if (raceTemplate == null || MeshRoot == null)
			{
				return;
			}

			AddressableLoadProcessor.LoadPrefabAsync(raceTemplate.GetModelReference(gender, modelIndex), (go) =>
			{
				// Remove previous child models except for labels and special points.
				if (MeshRoot.childCount > 0)
				{
					foreach (Transform child in MeshRoot)
					{
						if (child.gameObject.name.Contains("Labels") ||
							child.gameObject.name.Contains("FollowPoint") ||
							child.gameObject.name.Contains("SpawnPoint"))
						{
							continue;
						}
						child.gameObject.SetActive(false);
						Destroy(child.gameObject);
					}
				}
				// Instantiate and attach the new model prefab.
				GameObject modelInstance = Instantiate(go);
				modelInstance.transform.SetParent(MeshRoot);
				modelInstance.transform.SetLocalPositionRotationAndScale(Vector3.zero, Quaternion.identity, Vector3.one);

				// Wire the model's Animator to NetworkAnimator for network sync
				// NetworkAnimator auto-discovers the Animator on child GameObjects.

				// Notify visual behaviours that the model is ready (skeleton, body regions, animator)
				foreach (ICharacterBehaviour behaviour in Behaviours.Values)
				{
					if (behaviour is IModelReadyHandler handler)
					{
						handler.OnModelReady();
					}
				}

				Log.Debug("BaseCharacter", $"Setting Child model to identity. {modelInstance.transform.position}");
			});
		}
#endif

		/// <summary>
		/// Unity Awake callback. Initializes core references, sets layer, and initializes all attached character behaviours.
		/// </summary>
		void Awake()
		{
			Transform = transform;
			GameObject = this.gameObject;
			Collider = this.gameObject.GetComponent<Collider>();

			// Override default layer settings for player characters.
			// GameObject.layer takes a layer index, not the LayerMask bit mask that
			// Constants.Layers.Player holds — assigning the mask sets an invalid layer.
			if (Constants.Layers.Index.Player >= 0)
			{
				gameObject.layer = Constants.Layers.Index.Player;
			}

			// Initialize all attached character behaviours.
			ICharacterBehaviour[] c = gameObject.GetComponents<ICharacterBehaviour>();
			if (c != null)
			{
				for (int i = 0; i < c.Length; ++i)
				{
					ICharacterBehaviour behaviour = c[i];
					if (behaviour == null)
					{
						continue;
					}
					behaviour.InitializeOnce(this);
				}
			}

			OnAwake();
		}

		/// <summary>
		/// Called after all CharacterBehaviours have called InitializeOnce. Override for custom initialization logic.
		/// </summary>
		public virtual void OnAwake()
		{
#if !UNITY_SERVER
			// Ensure NetworkAnimator exists for animation sync
			NetworkAnimator = gameObject.GetComponent<NetworkAnimator>();
			if (NetworkAnimator == null)
			{
				NetworkAnimator = gameObject.AddComponent<NetworkAnimator>();
			}
#endif
		}

		/// <inheritdoc />
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Invoke(List<Trigger> triggers, EventData eventData)
		{
			if (triggers == null || eventData == null)
			{
				return;
			}

			for (int i = 0; i < triggers.Count; ++i)
			{
				if (triggers[i] != null)
				{
					triggers[i].Execute(eventData);
				}
			}
		}

		/// <summary>
		/// Unity OnDestroy callback. Calls OnDestroying for cleanup and removes the character from the client-side dictionary.
		/// </summary>
		void OnDestroy()
		{
			OnDestroying();

#if !UNITY_SERVER
			ClientCharacters.Remove(ID);
#endif
		}

		/// <summary>
		/// Called when the object is being destroyed. Override for custom cleanup logic.
		/// </summary>
		public virtual void OnDestroying() { }

		/// <summary>
		/// Drops the identity every character carries before the object returns to the pool.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why this lives on the base and not in each character.</b> <see cref="Flags"/> is
		/// per-spawn state — written by the load path, the spawn payload, and combat — and
		/// nothing rewrote it before the next occupant of a pool slot was already live. A
		/// recycled object that kept <see cref="CharacterFlags.IsDead"/> comes back dead, and one
		/// that lost <see cref="CharacterFlags.IsLoaded"/> is treated as still loading. <c>NPC</c>
		/// already cleared this for exactly that reason; players and pets did not, and the fix
		/// belongs where the field does.
		/// </para>
		/// <para>
		/// <b><see cref="ID"/> is deliberately left alone here.</b> For an <c>ISceneObject</c> —
		/// every NPC and pet — the ID is a <c>SceneObject</c> registry key that is meant to
		/// survive pool reuse: <c>SceneObject.Register</c> re-registering is a no-op precisely so
		/// a respawned NPC keeps the ID clients already hold, and the registry entry is only
		/// dropped on destroy. Zeroing it here would strand that entry and mint a second one on
		/// the next spawn. <c>PlayerCharacter</c>, whose ID is a database key issued per load and
		/// held in no such registry, clears its own.
		/// </para>
		/// <para>
		/// The client dictionary is keyed by <see cref="ID"/> and is pruned here for both — the
		/// same removal <c>OnDestroy</c> performs for the destroy path, and the reason subclasses
		/// no longer repeat it.
		/// </para>
		/// </remarks>
		/// <param name="asServer">True if called on the server, false if on the client.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

#if !UNITY_SERVER
			ClientCharacters.Remove(ID);
#endif

			Flags = 0;
		}

		/// <summary>
		/// Registers a character behaviour implementation for all supported interfaces.
		/// Only interfaces derived from ICharacterBehaviour are registered.
		/// </summary>
		/// <param name="behaviour">The behaviour instance to register.</param>
		public void RegisterCharacterBehaviour(ICharacterBehaviour behaviour)
		{
			if (behaviour == null)
			{
				return;
			}

			// Iterate interfaces directly to avoid LINQ allocations.
			Type[] interfaces = behaviour.GetType().GetInterfaces();

			for (int i = 0; i < interfaces.Length; ++i)
			{
				Type iface = interfaces[i];
				if (iface == typeof(ICharacterBehaviour))
				{
					continue;
				}
				if (!typeof(ICharacterBehaviour).IsAssignableFrom(iface))
				{
					continue;
				}
				if (!Behaviours.ContainsKey(iface))
				{
					// Register the behaviour for this interface type.
					Behaviours.Add(iface, behaviour);
				}
			}
		}

		/// <summary>
		/// Unregisters a character behaviour implementation for all supported interfaces.
		/// Removes the behaviour from the dictionary for each interface it implements.
		/// </summary>
		/// <param name="behaviour">The behaviour instance to unregister.</param>
		public void UnregisterCharacterBehaviour(ICharacterBehaviour behaviour)
		{
			if (behaviour == null)
			{
				return;
			}

			// Iterate interfaces directly to avoid LINQ allocations and only remove mappings
			// that still point to this behaviour (safer if mappings were overwritten).
			Type[] interfaces = behaviour.GetType().GetInterfaces();

			for (int i = 0; i < interfaces.Length; ++i)
			{
				Type iface = interfaces[i];
				if (iface == typeof(ICharacterBehaviour))
				{
					continue;
				}
				if (!typeof(ICharacterBehaviour).IsAssignableFrom(iface))
				{
					continue;
				}
				if (Behaviours.TryGetValue(iface, out ICharacterBehaviour existing) && existing == behaviour)
				{
					Behaviours.Remove(iface);
				}
			}
		}

		/// <summary>
		/// Attempts to retrieve a registered character behaviour for the specified interface type.
		/// </summary>
		/// <typeparam name="T">The interface type to retrieve.</typeparam>
		/// <param name="control">The behaviour instance if found, otherwise null.</param>
		/// <returns>True if the behaviour is found; otherwise, false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
		{
			Type type = typeof(T);
			if (!type.IsInterface)
			{
				throw new UnityException($"{type.Name} must be an interface.");
			}

			if (Behaviours.TryGetValue(type, out ICharacterBehaviour result))
			{
				if ((control = result as T) != null)
				{
					return true;
				}
			}
			control = null;
			return false;
		}

		/// <summary>
		/// Retrieves a registered character behaviour for the specified interface type, or null if not found.
		/// </summary>
		/// <typeparam name="T">The interface type to retrieve.</typeparam>
		/// <returns>The behaviour instance if found; otherwise, null.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T Get<T>() where T : class, ICharacterBehaviour
		{
			Type type = typeof(T);
			if (!type.IsInterface)
			{
				throw new UnityException($"{type.Name} must be an interface.");
			}

			if (Behaviours.TryGetValue(type, out ICharacterBehaviour result))
			{
				return result as T;
			}
			return null;
		}
	}
}