using FishNet.Object;
using FishNet.Utility.Extension;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
#if !UNITY_SERVER
using TMPro;
#endif
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for all networked character entities in the game.
	/// Provides core properties, behaviour registration, flag management, and prefab/model instantiation.
	/// </summary>
	public abstract class BaseCharacter : NetworkBehaviour, ICharacter
	{
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

#if !UNITY_SERVER
		[SerializeField]
		private Transform meshRoot;
		/// <summary>
		/// The root transform for the character's mesh/model hierarchy.
		/// </summary>
		public Transform MeshRoot { get { return this.meshRoot; } }
		[SerializeField]
		private TextMeshPro characterNameLabel;
		/// <summary>
		/// The TextMeshPro label displaying the character's name above their model.
		/// </summary>
		public TextMeshPro CharacterNameLabel { get { return this.characterNameLabel; } set { this.characterNameLabel = value; } }
		[SerializeField]
		private TextMeshPro characterGuildLabel;
		/// <summary>
		/// The TextMeshPro label displaying the character's guild above their model.
		/// </summary>
		public TextMeshPro CharacterGuildLabel { get { return this.characterGuildLabel; } set { this.characterGuildLabel = value; } }

		/// <summary>
		/// Instantiates the character's race model prefab at the specified index and attaches it to the mesh root.
		/// Removes any previous child models except for labels and special points.
		/// </summary>
		/// <param name="raceTemplate">The race template containing model references.</param>
		/// <param name="modelIndex">The index of the model to instantiate.</param>
		public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex)
		{
			if (raceTemplate == null || MeshRoot == null)
			{
				return;
			}

			AddressableLoadProcessor.LoadPrefabAsync(raceTemplate.GetModelReference(modelIndex), (go) =>
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
			gameObject.layer = Constants.Layers.Player;

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
		public virtual void OnAwake() { }

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

			// Find all interfaces implemented by the behaviour that derive from ICharacterBehaviour.
			List<Type> interfaces = behaviour.GetType()
											 .GetInterfaces()
											 .Where(x => x != typeof(ICharacterBehaviour) &&
														 typeof(ICharacterBehaviour).IsAssignableFrom(x)).ToList();

			for (int i = 0; i < interfaces.Count; ++i)
			{
				Type interfaceType = interfaces[i];

				if (!Behaviours.ContainsKey(interfaceType))
				{
					// Register the behaviour for this interface type.
					Behaviours.Add(interfaceType, behaviour);
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

			// Find all interfaces implemented by the behaviour that derive from ICharacterBehaviour.
			List<Type> interfaces = behaviour.GetType()
											 .GetInterfaces()
											 .Where(x => x != typeof(ICharacterBehaviour) &&
														 typeof(ICharacterBehaviour).IsAssignableFrom(x)).ToList();

			for (int i = 0; i < interfaces.Count; ++i)
			{
				Type interfaceType = interfaces[i];

				// Remove the behaviour for this interface type.
				Behaviours.Remove(interfaceType);
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