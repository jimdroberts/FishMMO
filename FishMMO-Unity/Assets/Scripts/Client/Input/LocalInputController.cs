using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class LocalInputController : MonoBehaviour
	{
#if !UNITY_SERVER
		public Character Character { get; private set; }

		public void Initialize(Character character)
		{
			Character = character;
		}

		private void OnEnable()
		{
			UIManager.Show("UIHealthBar");
			UIManager.Show("UIManaBar");
			UIManager.Show("UIStaminaBar");
			UIManager.Show("UIHotkeyBar");
			UIManager.Show("UIChat");
		}

		private void OnDisable()
		{
			UIManager.Hide("UIHealthBar");
			UIManager.Hide("UIManaBar");
			UIManager.Hide("UIStaminaBar");
			UIManager.Hide("UIHotkeyBar");
			UIManager.Hide("UIChat");
		}

		private void Update()
		{
			UpdateInput();
		}

		/// <summary>
		/// We handle UI input here because we completely disable UI elements when toggling visibility.
		/// </summary>
		private void UpdateInput()
		{
			// if an input has focus we should skip input otherwise things will happen while we are typing!
			if (Character == null ||
				UIManager.InputControlHasFocus())
			{
				return;
			}

			// mouse mode can toggle at any time other than input focus
			if (InputManager.GetKeyDown("Mouse Mode"))
			{
				InputManager.ToggleMouseMode();
			}
			
			// we can interact with things as long as the UI doesn't have focus
			if (!UIManager.ControlHasFocus() && InputManager.GetKeyDown("Interact"))
			{
				Transform target = Character.TargetController.Current.Target;
				if (target != null)
				{
					IInteractable interactable = target.GetComponent<IInteractable>();
					if (interactable != null)
					{
						interactable.OnInteract(Character);
					}
				}
			}
			else // UI windows should be able to open/close freely
			{
				if (InputManager.GetKeyDown("Inventory"))
				{
					UIManager.ToggleVisibility("UIInventory");
				}

				if (InputManager.GetKeyDown("Abilities"))
				{
					UIManager.ToggleVisibility("UIAbilities");
				}

				if (InputManager.GetKeyDown("Equipment") &&
					UIManager.TryGet("UIEquipment", out UIEquipment uiEquipment))
				{
					uiEquipment.SetEquipmentViewCamera(Character.EquipmentViewCamera);
					uiEquipment.ToggleVisibility();
				}

				if (InputManager.GetKeyDown("Guild"))
				{
					UIManager.ToggleVisibility("UIGuild");
				}

				if (InputManager.GetKeyDown("Party"))
				{
					UIManager.ToggleVisibility("UIParty");
				}

				if (InputManager.GetKeyDown("Friends"))
				{
					UIManager.ToggleVisibility("UIFriendList");

				}

				if (InputManager.GetKeyDown("Menu"))
				{
					UIManager.ToggleVisibility("UIMenu");
				}

				if (InputManager.GetKeyDown("Close Last UI") && !UIManager.CloseNext())
				{
					if (InputManager.MouseMode)
					{
						InputManager.MouseMode = false;
					}
				}
			}
		}
#endif
	}
}