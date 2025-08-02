using UnityEngine;
using FishMMO.Shared;
using FishMMO.Logging;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class UIBuff : UICharacterControl
	{
		/// <summary>
		/// The parent RectTransform for buff UI elements.
		/// </summary>
		public RectTransform BuffParent;
		/// <summary>
		/// The prefab used to instantiate buff buttons/groups.
		/// </summary>
		public UIBuffGroup BuffButtonPrefab;

		/// <summary>
		/// Dictionary mapping buff template IDs to their UI group representations.
		/// </summary>
		public Dictionary<int, UIBuffGroup> Buffs = new Dictionary<int, UIBuffGroup>();

		/// <summary>
		/// Called when the UI is starting. Subscribes to buff-related events.
		/// </summary>
		public override void OnStarting()
		{
			// Subscribe to buff events for updating UI.
			IBuffController.OnSubtractTime += BuffController_OnSubtractTime;
			IBuffController.OnAddBuff += BuffController_OnAddBuff;
			IBuffController.OnRemoveBuff += BuffController_OnRemoveBuff;

			// Clear all buffs when the local client stops.
			IPlayerCharacter.OnStopLocalClient += (c) => ClearAllBuffs();
		}

		/// <summary>
		/// Called when the UI is being destroyed. Unsubscribes from buff events and clears buffs.
		/// </summary>
		public override void OnDestroying()
		{
			// Unsubscribe from buff events and clear all buffs.
			IBuffController.OnSubtractTime -= BuffController_OnSubtractTime;
			IBuffController.OnAddBuff -= BuffController_OnAddBuff;
			IBuffController.OnRemoveBuff -= BuffController_OnRemoveBuff;

			IPlayerCharacter.OnStopLocalClient -= (c) => ClearAllBuffs();

			ClearAllBuffs();
		}

		/// <summary>
		/// Called after the character is set. Invokes base implementation.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();
		}

		/// <summary>
		/// Called before the character is unset. (No implementation)
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
		}

		/// <summary>
		/// Called when quitting to login. Clears all buffs.
		/// </summary>
		public override void OnQuitToLogin()
		{
			ClearAllBuffs();
		}

		/// <summary>
		/// Event handler for subtracting time from a buff. Updates the duration slider in the UI.
		/// </summary>
		/// <param name="buff">The buff to update.</param>
		private void BuffController_OnSubtractTime(Buff buff)
		{
			// Validate buff and template, and ensure it's not a debuff before updating UI.
			if (buff == null)
			{
				return;
			}
			if (buff.Template == null)
			{
				return;
			}
			if (buff.Template.IsDebuff)
			{
				return;
			}
			if (Buffs.TryGetValue(buff.Template.ID, out UIBuffGroup buffGroup))
			{
				buffGroup.DurationSlider.value = buff.RemainingTime / buff.Template.Duration;
			}
		}

		/// <summary>
		/// Event handler for adding a buff. Instantiates and initializes the buff UI group.
		/// </summary>
		/// <param name="buff">The buff to add.</param>
		private void BuffController_OnAddBuff(Buff buff)
		{
			// Validate buff and template, and ensure it's not a debuff before adding to UI.
			if (buff == null)
			{
				return;
			}
			if (buff.Template == null)
			{
				return;
			}
			if (buff.Template.IsDebuff)
			{
				return;
			}
			if (Buffs.ContainsKey(buff.Template.ID))
			{
				return;
			}
			// Instantiate and initialize the buff UI group.
			UIBuffGroup buffGroup = Instantiate(BuffButtonPrefab, BuffParent);
			if (buffGroup.ButtonText != null)
			{
				buffGroup.ButtonText.text = buff.Template.Name;
			}
			if (buffGroup.TooltipButton != null)
			{
				buffGroup.TooltipButton.Initialize(buff.Template.ID, Buff_OnLeftClick, null, buff.Template, "\r\n\r\nLeft Mouse Button to remove.");
			}
			if (buffGroup.Icon != null)
			{
				buffGroup.Icon.sprite = buff.Template.Icon;
			}
			if (Buffs == null)
			{
				Buffs = new Dictionary<int, UIBuffGroup>();
			}
			Buffs.Add(buff.Template.ID, buffGroup);
			buffGroup.gameObject.SetActive(true);
		}

		/// <summary>
		/// Event handler for removing a buff. Destroys the buff UI group.
		/// </summary>
		/// <param name="buff">The buff to remove.</param>
		private void BuffController_OnRemoveBuff(Buff buff)
		{
			// Validate buff and template, and ensure it's not a debuff before removing from UI.
			if (buff == null)
			{
				return;
			}
			if (buff.Template == null)
			{
				return;
			}
			if (buff.Template.IsDebuff)
			{
				return;
			}
			if (Buffs.TryGetValue(buff.Template.ID, out UIBuffGroup group))
			{
				Destroy(group.gameObject);
				Buffs.Remove(buff.Template.ID);
			}
		}

		/// <summary>
		/// Clears all buffs from the UI and destroys their GameObjects.
		/// </summary>
		public void ClearAllBuffs()
		{
			// If there are no buffs, nothing to clear.
			if (Buffs == null || Buffs.Count == 0)
			{
				return;
			}
			// Destroy all buff UI groups and clear the dictionary.
			foreach (UIBuffGroup group in Buffs.Values)
			{
				Destroy(group.gameObject);
			}
			Buffs.Clear();
		}

		/// <summary>
		/// Event handler for left-clicking a buff. Used for debugging or removing buffs.
		/// </summary>
		/// <param name="index">The buff index or ID.</param>
		/// <param name="optionalParams">Optional parameters for the click event.</param>
		public void Buff_OnLeftClick(int index, object[] optionalParams)
		{
			Log.Debug("UIBuff", $"Clicked buff {index} {optionalParams}");
		}
	}
}