using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class UIAbilities : UICharacterControl
	{
		/// <summary>
		/// The parent RectTransform for ability button UI elements.
		/// </summary>
		public RectTransform AbilityParent;
		/// <summary>
		/// The prefab used to instantiate ability buttons.
		/// </summary>
		public UIAbilityButton AbilityButtonPrefab;

		/// <summary>
		/// The button to show all abilities.
		/// </summary>
		public Button AbilitiesButton;
		/// <summary>
		/// The button to show known abilities.
		/// </summary>
		public Button KnownAbilitiesButton;
		/// <summary>
		/// The button to show known ability events.
		/// </summary>
		public Button KnownAbilityEventsButton;

		/// <summary>
		/// List of all ability buttons currently displayed.
		/// </summary>
		private List<UIAbilityButton> Abilities;
		/// <summary>
		/// List of all known ability buttons currently displayed.
		/// </summary>
		private List<UIAbilityButton> KnownAbilities;
		/// <summary>
		/// List of all known ability event buttons currently displayed.
		/// </summary>
		private List<UIAbilityButton> KnownAbilityEvents;

		/// <summary>
		/// The currently selected ability tab.
		/// </summary>
		private AbilityTabType CurrentTab = AbilityTabType.Ability;

		/// <summary>
		/// Called when the UI is starting. Subscribes to character and local client events.
		/// </summary>
		public override void OnStarting()
		{
			OnSetCharacter += CharacterControl_OnSetCharacter;
			IPlayerCharacter.OnStopLocalClient += (c) => ClearAllSlots();
		}

		/// <summary>
		/// Called when the UI is being destroyed. Unsubscribes from events and clears all ability slots.
		/// </summary>
		public override void OnDestroying()
		{
			IPlayerCharacter.OnStopLocalClient -= (c) => ClearAllSlots();
			OnSetCharacter -= CharacterControl_OnSetCharacter;

			ClearAllSlots();
		}

		/// <summary>
		/// Called after the character is set. Subscribes to ability controller events.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IAbilityController abilityController))
			{
				// Only allow manipulation if mouse mode is not active.
				abilityController.OnCanManipulate += () => { return !InputManager.MouseMode; };
				abilityController.OnAddAbility += AddAbility;
				abilityController.OnAddKnownAbility += AddKnownAbility;
				abilityController.OnAddKnownAbilityEvent += AddKnownAbilityEvent;
			}
		}

		/// <summary>
		/// Called before the character is unset. Unsubscribes from ability controller events and clears all ability slots.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			if (Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.OnCanManipulate -= () => { return !InputManager.MouseMode; };
				abilityController.OnAddAbility -= AddAbility;
				abilityController.OnAddKnownAbility -= AddKnownAbility;
				abilityController.OnAddKnownAbilityEvent -= AddKnownAbilityEvent;
			}
			ClearAllSlots();
		}

		/// <summary>
		/// Called when quitting to login. Clears all ability slots.
		/// </summary>
		public override void OnQuitToLogin()
		{
			ClearAllSlots();
		}

		/// <summary>
		/// Event handler for when the character is set. Updates character reference for all ability buttons.
		/// </summary>
		/// <param name="character">The player character.</param>
		private void CharacterControl_OnSetCharacter(IPlayerCharacter character)
		{
			if (Abilities != null)
			{
				foreach (UIAbilityButton ability in Abilities)
				{
					ability.Character = character;
				}
			}
			if (KnownAbilities != null)
			{
				foreach (UIAbilityButton ability in KnownAbilities)
				{
					ability.Character = character;
				}
			}
			if (KnownAbilityEvents != null)
			{
				foreach (UIAbilityButton ability in KnownAbilityEvents)
				{
					ability.Character = character;
				}
			}
		}

		/// <summary>
		/// Adds an ability button for the given ability.
		/// </summary>
		/// <param name="ability">The ability to add.</param>
		public void AddAbility(Ability ability)
		{
			if (ability == null)
			{
				return;
			}

			InstantiateButton(ability.ID, ability.Template.Icon, ReferenceButtonType.Ability, AbilityTabType.Ability, ability.Tooltip(), ref Abilities);
		}

		/// <summary>
		/// Adds a known ability button for the given ability template.
		/// </summary>
		/// <param name="template">The ability template to add.</param>
		public void AddKnownAbility(BaseAbilityTemplate template)
		{
			if (template == null)
			{
				return;
			}

			InstantiateButton(template.ID, template.Icon, ReferenceButtonType.None, AbilityTabType.KnownAbility, template.Tooltip(), ref KnownAbilities);
		}

		/// <summary>
		/// Adds a known ability event button for the given ability event.
		/// </summary>
		/// <param name="abilityEvent">The ability event to add.</param>
		public void AddKnownAbilityEvent(AbilityEvent abilityEvent)
		{
			if (abilityEvent == null)
			{
				return;
			}

			InstantiateButton(abilityEvent.ID, abilityEvent.Icon, ReferenceButtonType.None, AbilityTabType.KnownAbilityEvent, abilityEvent.Tooltip(), ref KnownAbilityEvents);
		}

		/// <summary>
		/// Instantiates an ability button and adds it to the specified container.
		/// </summary>
		/// <param name="id">The reference ID for the ability.</param>
		/// <param name="icon">The icon for the ability.</param>
		/// <param name="buttonType">The reference button type.</param>
		/// <param name="tabType">The tab type for the ability.</param>
		/// <param name="toolTip">The tooltip text for the ability.</param>
		/// <param name="container">The container to add the button to.</param>
		private void InstantiateButton(long id, Sprite icon, ReferenceButtonType buttonType, AbilityTabType tabType, string toolTip, ref List<UIAbilityButton> container)
		{
			UIAbilityButton button = Instantiate(AbilityButtonPrefab, AbilityParent);
			button.Character = Character;
			button.ReferenceID = id;
			button.Type = buttonType;
			if (button.DescriptionLabel != null)
			{
				button.DescriptionLabel.text = toolTip;
			}
			if (button.Icon != null)
			{
				button.Icon.sprite = icon;
			}
			if (container == null)
			{
				container = new List<UIAbilityButton>();
			}
			container.Add(button);
			button.gameObject.SetActive(CurrentTab == tabType ? true : false);
		}

		/// <summary>
		/// Clears all ability slots from the UI.
		/// </summary>
		public void ClearAllSlots()
		{
			ClearSlots(ref Abilities);
			ClearSlots(ref KnownAbilities);
			ClearSlots(ref KnownAbilityEvents);
		}

		/// <summary>
		/// Clears the specified list of ability buttons from the UI.
		/// </summary>
		/// <param name="slots">The list of ability buttons to clear.</param>
		private void ClearSlots(ref List<UIAbilityButton> slots)
		{
			if (slots != null)
			{
				for (int i = 0; i < slots.Count; ++i)
				{
					if (slots[i] == null)
					{
						continue;
					}
					if (slots[i].gameObject != null)
					{
						slots[i].Clear();
						Destroy(slots[i].gameObject);
					}
				}
				slots.Clear();
			}
		}

		/// <summary>
		/// Event handler for when an ability tab is clicked. Updates the visible ability entries.
		/// </summary>
		/// <param name="type">The tab type to display.</param>
		public void Tab_OnClick(int type)
		{
			CurrentTab = (AbilityTabType)type;
			switch (CurrentTab)
			{
				case AbilityTabType.Ability:
					ShowEntries(Abilities);
					ShowEntries(KnownAbilities, false);
					ShowEntries(KnownAbilityEvents, false);
					break;
				case AbilityTabType.KnownAbility:
					ShowEntries(Abilities, false);
					ShowEntries(KnownAbilities);
					ShowEntries(KnownAbilityEvents, false);
					break;
				case AbilityTabType.KnownAbilityEvent:
					ShowEntries(Abilities, false);
					ShowEntries(KnownAbilities, false);
					ShowEntries(KnownAbilityEvents);
					break;
				default: return;
			}
		}

		/// <summary>
		/// Shows or hides the specified list of ability buttons.
		/// </summary>
		/// <param name="buttons">The list of ability buttons to show or hide.</param>
		/// <param name="show">Whether to show (true) or hide (false) the buttons.</param>
		private void ShowEntries(List<UIAbilityButton> buttons, bool show = true)
		{
			if (buttons == null ||
				buttons.Count < 1)
			{
				return;
			}
			foreach (UIAbilityButton button in buttons)
			{
				button.gameObject.SetActive(show);
			}
		}
	}
}