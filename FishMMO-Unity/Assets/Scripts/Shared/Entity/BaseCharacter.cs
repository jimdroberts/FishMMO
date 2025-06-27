using FishNet.Object;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
#if !UNITY_SERVER
using TMPro;
#endif

namespace FishMMO.Shared
{
	public abstract class BaseCharacter : NetworkBehaviour, ICharacter
	{
		protected Dictionary<Type, ICharacterBehaviour> Behaviours = new Dictionary<Type, ICharacterBehaviour>();

		public long ID { get; set; }
		public string Name { get { return gameObject.name; } }
		public Transform Transform { get; private set; }
		public GameObject GameObject { get; private set; }
		public Collider Collider { get; set; }
		public virtual bool IsTeleporting => false;
		public int Flags { get; set; }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void EnableFlags(CharacterFlags flags)
		{
			int characterFlags = Flags;
			characterFlags.EnableBit(flags);
			Flags = characterFlags;
		}
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
		public Transform MeshRoot { get { return this.meshRoot; } }
		[SerializeField]
		private TextMeshPro characterNameLabel;
		public TextMeshPro CharacterNameLabel { get { return this.characterNameLabel; } set { this.characterNameLabel = value; } }
		[SerializeField]
		private TextMeshPro characterGuildLabel;
		public TextMeshPro CharacterGuildLabel { get { return this.characterGuildLabel; } set { this.characterGuildLabel = value; } }

		public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex)
		{
			if (raceTemplate == null || MeshRoot == null)
			{
				return;
			}

			AddressableLoadProcessor.LoadPrefabAsync(raceTemplate.GetModelReference(modelIndex), (go) =>
			{
				if (MeshRoot.childCount > 0)
				{
					foreach (Transform child in MeshRoot)
					{
						child.gameObject.SetActive(false);
						Destroy(child.gameObject);
					}
				}
				Instantiate(go, Vector3.zero, Quaternion.identity, MeshRoot);
			});
		}
#endif

		void Awake()
		{
			Transform = transform;
			GameObject = this.gameObject;
			Collider = this.gameObject.GetComponent<Collider>();

			// Override default layer settings
			gameObject.layer = Constants.Layers.Player;

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
		/// Called after all CharacterBehaviours have called InitializeOnce.
		/// </summary>
		public virtual void OnAwake() { }

		public void RegisterCharacterBehaviour(ICharacterBehaviour behaviour)
		{
			if (behaviour == null)
			{
				return;
			}

			List<Type> interfaces = behaviour.GetType()
											 .GetInterfaces()
											 .Where(x => x != typeof(ICharacterBehaviour) &&
														 typeof(ICharacterBehaviour).IsAssignableFrom(x)).ToList();

			for (int i = 0; i < interfaces.Count; ++i)
			{
				Type interfaceType = interfaces[i];

				if (!Behaviours.ContainsKey(interfaceType))
				{
					//Log.Debug(CharacterName + ": Registered " + interfaceType.Name);
					Behaviours.Add(interfaceType, behaviour);
				}
			}
		}

		public void UnregisterCharacterBehaviour(ICharacterBehaviour behaviour)
		{
			if (behaviour == null)
			{
				return;
			}

			List<Type> interfaces = behaviour.GetType()
											 .GetInterfaces()
											 .Where(x => x != typeof(ICharacterBehaviour) &&
														 typeof(ICharacterBehaviour).IsAssignableFrom(x)).ToList();

			for (int i = 0; i < interfaces.Count; ++i)
			{
				Type interfaceType = interfaces[i];

				//Log.Debug(CharacterName + ": Unregistered " + interfaceType.Name);
				Behaviours.Remove(interfaceType);
			}
		}

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