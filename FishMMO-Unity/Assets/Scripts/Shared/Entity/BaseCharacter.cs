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
		public Transform Transform { get; private set; }
		public GameObject GameObject { get; private set; }
		public Collider Collider { get; set; }
		public virtual bool IsTeleporting => false;

#if !UNITY_SERVER
		[SerializeField]
		private Transform meshRoot;
		public Transform MeshRoot { get { return this.meshRoot; }}
		[SerializeField]
		private TextMeshPro characterNameLabel;
		public TextMeshPro CharacterNameLabel { get { return this.characterNameLabel; } set { this.characterNameLabel = value; } }
		[SerializeField]
		private TextMeshPro characterGuildLabel;
		public TextMeshPro CharacterGuildLabel { get { return this.characterGuildLabel; } set { this.characterGuildLabel = value; } }
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

		public virtual void OnAwake()
		{
		}

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
					//Debug.Log(CharacterName + ": Registered " + interfaceType.Name);
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

				//Debug.Log(CharacterName + ": Unregistered " + interfaceType.Name);
				Behaviours.Remove(interfaceType);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
		{
			if (Behaviours.TryGetValue(typeof(T), out ICharacterBehaviour result))
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
			if (Behaviours.TryGetValue(typeof(T), out ICharacterBehaviour result))
			{
				return result as T;
			}
			return null;
		}
	}
}