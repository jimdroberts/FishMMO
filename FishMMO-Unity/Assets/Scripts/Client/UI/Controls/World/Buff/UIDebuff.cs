using UnityEngine;
using FishMMO.Shared;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class UIDebuff : UICharacterControl
	{
		/// <summary>
		/// The parent RectTransform for debuff UI elements.
		/// </summary>
		public RectTransform DebuffParent;
		/// <summary>
		/// The prefab used to instantiate debuff buttons/groups.
		/// </summary>
		public UIBuffGroup DebuffButtonPrefab;

		/// <summary>
		/// Dictionary mapping debuff template IDs to their UI group representations.
		/// </summary>
		public Dictionary<int, UIBuffGroup> Debuffs = new Dictionary<int, UIBuffGroup>();

		/// <summary>
		/// Called when the UI is starting. Subscribes to debuff-related events.
		/// </summary>
		public override void OnStarting()
		{
			// Subscribe to debuff events for updating UI.
			IBuffController.OnSubtractTime += BuffController_OnSubtractTime;
			IBuffController.OnAddDebuff += BuffController_OnAddDebuff;
			IBuffController.OnRemoveDebuff += BuffController_OnRemoveDebuff;

			// Clear all debuffs when the local client stops.
			IPlayerCharacter.OnStopLocalClient += (c) => ClearAllDebuffs();
		}

		/// <summary>
		/// Called when the UI is being destroyed. Unsubscribes from debuff events and clears debuffs.
		/// </summary>
		public override void OnDestroying()
		{
			// Unsubscribe from debuff events and clear all debuffs.
			IBuffController.OnSubtractTime -= BuffController_OnSubtractTime;
			IBuffController.OnAddDebuff -= BuffController_OnAddDebuff;
			IBuffController.OnRemoveDebuff -= BuffController_OnRemoveDebuff;

			IPlayerCharacter.OnStopLocalClient -= (c) => ClearAllDebuffs();

			ClearAllDebuffs();
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
		/// Called when quitting to login. Clears all debuffs.
		/// </summary>
		public override void OnQuitToLogin()
		{
			ClearAllDebuffs();
		}

		/// <summary>
		/// Event handler for subtracting time from a debuff. Updates the duration slider in the UI.
		/// </summary>
		/// <param name="buff">The debuff to update.</param>
		private void BuffController_OnSubtractTime(Buff buff)
		{
			// Validate buff and template, and ensure it's a debuff before updating UI.
			if (buff == null)
			{
				return;
			}
			if (buff.Template == null)
			{
				return;
			}
			if (!buff.Template.IsDebuff)
			{
				return;
			}
			if (Debuffs.TryGetValue(buff.Template.ID, out UIBuffGroup buffGroup))
			{
				buffGroup.DurationSlider.value = buff.RemainingTime / buff.Template.Duration;
			}
		}

		/// <summary>
		/// Event handler for adding a debuff. Instantiates and initializes the debuff UI group.
		/// </summary>
		/// <param name="buff">The debuff to add.</param>
		private void BuffController_OnAddDebuff(Buff buff)
		{
			// Validate buff and template, and ensure it's a debuff before adding to UI.
			if (buff == null)
			{
				return;
			}
			if (buff.Template == null)
			{
				return;
			}
			if (!buff.Template.IsDebuff)
			{
				return;
			}
			if (Debuffs.ContainsKey(buff.Template.ID))
			{
				return;
			}
			// Instantiate and initialize the debuff UI group.
			UIBuffGroup buffGroup = Instantiate(DebuffButtonPrefab, DebuffParent);
			if (buffGroup.ButtonText != null)
			{
				buffGroup.ButtonText.text = buff.Template.Name;
			}
			if (buffGroup.TooltipButton != null)
			{
				buffGroup.TooltipButton.Initialize(buff.Template.ID, null, null, buff.Template);
			}
			if (buffGroup.Icon != null)
			{
				buffGroup.Icon.sprite = buff.Template.Icon;
			}
			if (Debuffs == null)
			{
				Debuffs = new Dictionary<int, UIBuffGroup>();
			}
			Debuffs.Add(buff.Template.ID, buffGroup);
			buffGroup.gameObject.SetActive(true);
		}

		/// <summary>
		/// Event handler for removing a debuff. Destroys the debuff UI group.
		/// </summary>
		/// <param name="buff">The debuff to remove.</param>
		private void BuffController_OnRemoveDebuff(Buff buff)
		{
			// Validate buff and template, and ensure it's a debuff before removing from UI.
			if (buff == null)
			{
				return;
			}
			if (buff.Template == null)
			{
				return;
			}
			if (!buff.Template.IsDebuff)
			{
				return;
			}
			if (Debuffs.TryGetValue(buff.Template.ID, out UIBuffGroup group))
			{
				Destroy(group.gameObject);
				Debuffs.Remove(buff.Template.ID);
			}
		}

		/// <summary>
		/// Clears all debuffs from the UI and destroys their GameObjects.
		/// </summary>
		public void ClearAllDebuffs()
		{
			// If there are no debuffs, nothing to clear.
			if (Debuffs == null || Debuffs.Count == 0)
			{
				return;
			}
			// Destroy all debuff UI groups and clear the dictionary.
			foreach (UIBuffGroup group in Debuffs.Values)
			{
				Destroy(group.gameObject);
			}
			Debuffs.Clear();
		}
	}
}