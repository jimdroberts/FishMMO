using UnityEngine;

namespace FishMMO.Client
{
	public class LocalInputController : MonoBehaviour
	{
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
			if (UIManager.InputControlHasFocus())
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
				Character character = Character.localCharacter;
				if (character != null)
				{
					Transform target = character.TargetController.Current.Target;
					if (target != null)
					{
						IInteractable interactable = target.GetComponent<IInteractable>();
						if (interactable != null)
						{
							interactable.OnInteract(character);
						}
					}
				}
			}
			else // UI windows should be able to open/close freely
			{
				if (InputManager.GetKeyDown("Inventory"))
				{
					if (UIManager.TryGet("UIInventory", out UIInventory uiInventory))
					{
						uiInventory.Visible = !uiInventory.Visible;
					}
				}

				if (InputManager.GetKeyDown("Abilities"))
				{
					//if (UIManager.TryGet("UIAbilities", out UIAbilities uiAbilities))
					//{
					//	uiAbilities.Visible = !uiAbilities.Visible;
					//}
				}

				if (InputManager.GetKeyDown("Equipment"))
				{
					if (UIManager.TryGet("UIEquipment", out UIEquipment uiEquipment))
					{
						uiEquipment.Visible = !uiEquipment.Visible;
					}
				}

				if (InputManager.GetKeyDown("Guild"))
				{
					if (UIManager.TryGet("UIGuild", out UIGuild uiGuild))
					{
						uiGuild.Visible = !uiGuild.Visible;
					}
				}

				if (InputManager.GetKeyDown("Party"))
				{
					if (UIManager.TryGet("UIParty", out UIParty uiParty))
					{
						uiParty.Visible = !uiParty.Visible;
					}
				}

				if (InputManager.GetKeyDown("Friends"))
				{
					if (UIManager.TryGet("UIFriendList", out UIFriendList uiFriendList))
					{
						uiFriendList.Visible = !uiFriendList.Visible;
					}

				}
				if (InputManager.GetKeyDown("Menu"))
				{
					//if (UIManager.TryGet("UIMenu", out UIMenu uiMenu))
					//{
					//	uiMenu.Visible = !uiMenu.Visible;
					//}
				}
			}
		}
	}
}