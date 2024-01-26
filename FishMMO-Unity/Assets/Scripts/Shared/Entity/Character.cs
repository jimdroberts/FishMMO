#if !UNITY_SERVER
using FishMMO.Client;
using TMPro;
#endif
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using KinematicCharacterController;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Character contains references to all of the controllers associated with the character.
	/// </summary>
	#region KCC
	[RequireComponent(typeof(Rigidbody))]
	[RequireComponent(typeof(KinematicCharacterMotor))]
	[RequireComponent(typeof(KCCController))]
	[RequireComponent(typeof(KCCPlayer))]
	#endregion
	[RequireComponent(typeof(CharacterAttributeController))]
	[RequireComponent(typeof(TargetController))]
	[RequireComponent(typeof(CooldownController))]
	[RequireComponent(typeof(InventoryController))]
	[RequireComponent(typeof(EquipmentController))]
	[RequireComponent(typeof(BankController))]
	[RequireComponent(typeof(AbilityController))]
	[RequireComponent(typeof(AchievementController))]
	[RequireComponent(typeof(BuffController))]
	[RequireComponent(typeof(QuestController))]
	[RequireComponent(typeof(CharacterDamageController))]
	[RequireComponent(typeof(GuildController))]
	[RequireComponent(typeof(PartyController))]
	[RequireComponent(typeof(FriendController))]
	public class Character : NetworkBehaviour, IPooledResettable
	{
		private static Dictionary<Type, CharacterBehaviour> behaviours = new Dictionary<Type, CharacterBehaviour>();

		public Transform Transform { get; private set; }

		#region KCC
		public KinematicCharacterMotor Motor { get; private set; }
		public KCCController CharacterController { get; private set; }
		public KCCPlayer KCCPlayer { get; private set; }
		#endregion

#if !UNITY_SERVER
		public LocalInputController LocalInputController { get; private set; }
		public TextMeshPro CharacterNameLabel;
		public TextMeshPro CharacterGuildLabel;
		public Camera EquipmentViewCamera;
#endif

		// accountID for reference
		public readonly SyncVar<long> ID = new SyncVar<long>(new SyncTypeSetting()
		{
			SendRate = 0.0f,
			Channel = Channel.Reliable,
			ReadPermission = ReadPermission.Observers,
			WritePermission = WritePermission.ServerOnly,
		});
		private void OnCharacterIDChanged(long prev, long next, bool asServer)
		{
#if !UNITY_SERVER
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, next, (n) =>
			{
				gameObject.name = n;
				CharacterName = n;
				CharacterNameLower = n.ToLower();

				if (CharacterNameLabel != null)
					CharacterNameLabel.text = n;
			});
#endif
		}

		/// <summary>
		/// The characters real name. Use this if you are referencing a character by name. Avoid character.name unless you want the name of the game object.
		/// </summary>
		public string CharacterName;
		public string CharacterNameLower;
		public string Account;
		public long WorldServerID;
		public AccessLevel AccessLevel = AccessLevel.Player;
		public bool IsTeleporting = false;
		public readonly SyncVar<long> Currency = new SyncVar<long>(new SyncTypeSetting()
		{
			SendRate = 0.0f,
			Channel = Channel.Unreliable,
			ReadPermission = ReadPermission.OwnerOnly,
			WritePermission = WritePermission.ServerOnly,
		});
		public readonly SyncVar<int> RaceID = new SyncVar<int>(new SyncTypeSetting()
		{
			SendRate = 0.0f,
			Channel = Channel.Unreliable,
			ReadPermission = ReadPermission.OwnerOnly,
			WritePermission = WritePermission.ServerOnly,
		});
		public readonly SyncVar<string> RaceName = new SyncVar<string>(new SyncTypeSetting()
		{
			SendRate = 0.0f,
			Channel = Channel.Unreliable,
			ReadPermission = ReadPermission.OwnerOnly,
			WritePermission = WritePermission.ServerOnly,
		});
		public readonly SyncVar<string> SceneName = new SyncVar<string>(new SyncTypeSetting()
		{
			SendRate = 0.0f,
			Channel = Channel.Unreliable,
			ReadPermission = ReadPermission.OwnerOnly,
			WritePermission = WritePermission.ServerOnly,
		});
		public int SceneHandle;
		public string LastChatMessage = "";
		public DateTime NextChatMessageTime = DateTime.UtcNow;
		public DateTime NextInteractTime = DateTime.UtcNow;

		void Awake()
		{
			ID.OnChange += OnCharacterIDChanged;

			Transform = transform;

			#region KCC
			Motor = gameObject.GetComponent<KinematicCharacterMotor>();

			CharacterController = gameObject.GetComponent<KCCController>();
			CharacterController.Character = this;
			CharacterController.Motor = Motor;
			Motor.CharacterController = CharacterController;

			KCCPlayer = gameObject.GetComponent<KCCPlayer>();
			KCCPlayer.CharacterController = CharacterController;
			KCCPlayer.Motor = Motor;
			#endregion

			CharacterBehaviour[] c = gameObject.GetComponents<CharacterBehaviour>();
			if (c != null)
			{
				for (int i = 0; i < c.Length; ++i)
				{
					CharacterBehaviour behaviour = c[i];
					if (behaviour == null)
					{
						continue;
					}

					behaviour.InitializeOnce(this);
				}
			}
		}

		void OnDestroy()
		{
			ID.OnChange -= OnCharacterIDChanged;
		}

#if !UNITY_SERVER
		public override void OnStartClient()
		{
			base.OnStartClient();
			if (base.IsOwner)
			{
				InitializeLocal(true);
			}
		}

		public override void OnStopClient()
		{
			base.OnStopClient();
			if (base.IsOwner)
			{
				InitializeLocal(false);
			}
		}

		private void InitializeLocal(bool initializing)
		{
			InputManager.MouseMode = false;

			LocalInputController = gameObject.GetComponent<LocalInputController>();
			if (LocalInputController == null)
			{
				LocalInputController = gameObject.AddComponent<LocalInputController>();
			}
			LocalInputController.Initialize(this);

			InitializeUI(initializing);
		}

		private void InitializeUI(bool initializing)
		{
			if (initializing)
			{
				UIManager.SetCharacter(this);

				if (this.TryGet(out TargetController targetController) &&
					UIManager.TryGet("UITarget", out UITarget uiTarget))
				{
					targetController.OnChangeTarget += uiTarget.OnChangeTarget;
					targetController.OnUpdateTarget += uiTarget.OnUpdateTarget;
				}

				gameObject.layer = Constants.Layers.LocalEntity;
				CharacterController.MeshRoot.gameObject.layer = Constants.Layers.LocalEntity;
			}
			else
			{
				UIManager.SetCharacter(null);

				if (this.TryGet(out TargetController targetController) &&
					UIManager.TryGet("UITarget", out UITarget uiTarget))
				{
					targetController.OnChangeTarget -= uiTarget.OnChangeTarget;
					targetController.OnUpdateTarget -= uiTarget.OnUpdateTarget;
				}

				gameObject.layer = Constants.Layers.Default;
				CharacterController.MeshRoot.gameObject.layer = Constants.Layers.Default;
			}
		}
#endif

		public void RegisterCharacterBehaviour(CharacterBehaviour behaviour)
		{
			if (behaviour == null)
			{
				return;
			}
			Type type = behaviour.GetType();
			if (behaviours.ContainsKey(type))
			{
				return;
			}
			Debug.Log(CharacterName + ": Registered " + type.Name);
			behaviours.Add(type, behaviour);
		}

		public void Unregister<T>(T behaviour) where T : CharacterBehaviour
		{
			if (behaviour == null)
			{
				return;
			}
			else
			{
				Type type = behaviour.GetType();
				Debug.Log(CharacterName + ": Unregistered " + type.Name);
				behaviours.Remove(type);
			}
		}

		public bool TryGet<T>(out T control) where T : CharacterBehaviour
		{
			if (behaviours.TryGetValue(typeof(T), out CharacterBehaviour result))
			{
				if ((control = result as T) != null)
				{
					return true;
				}
			}
			control = null;
			return false;
		}

		public T Get<T>() where T : CharacterBehaviour
		{
			if (behaviours.TryGetValue(typeof(T), out CharacterBehaviour result))
			{
				return result as T;
			}
			return null;
		}

		/// <summary>
		/// Resets the Character values to default for pooling.
		/// </summary>
		public void OnPooledReset()
		{
			ID.Value = -1;
			CharacterName = "";
			Account = "";
			WorldServerID = 0;
			AccessLevel = AccessLevel.Player;
			IsTeleporting = false;
			Currency.Value = 0;
			RaceID.Value = 0;
			RaceName.Value = "";
			SceneName.Value = "";
			SceneHandle = 0;
			LastChatMessage = "";
			NextChatMessageTime = DateTime.UtcNow;
			NextInteractTime = DateTime.UtcNow;
			Motor.SetPositionAndRotationAndVelocity(Vector3.zero, Quaternion.identity, Vector3.zero);
		}

		public void SetGuildName(string guildName)
		{
#if !UNITY_SERVER
			if (CharacterGuildLabel != null)
			{
				CharacterGuildLabel.text = !string.IsNullOrWhiteSpace(guildName) ? "[" + guildName + "]" : "";
			}
#endif
		}
	}
}