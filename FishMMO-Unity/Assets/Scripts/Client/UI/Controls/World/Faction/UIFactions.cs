using UnityEngine;
using FishMMO.Shared;
using System.Collections.Generic;

namespace FishMMO.Client
{
	/// <summary>
	/// UI control for managing and displaying player faction information.
	/// </summary>
	public class UIFactions : UICharacterControl
	{
		/// <summary>
		/// The parent transform for faction description UI elements.
		/// </summary>
		public RectTransform FactionDescriptionParent;
		/// <summary>
		/// Prefab used to instantiate faction description UI elements.
		/// </summary>
		public UIFactionDescription FactionDescriptionPrefab;

		/// <summary>
		/// Dictionary of faction descriptions by faction template ID.
		/// </summary>
		private Dictionary<int, UIFactionDescription> factions = new Dictionary<int, UIFactionDescription>();


		/// <summary>
		/// Called when the UI is starting. Subscribes to character set and local client stop events.
		/// </summary>
		public override void OnStarting()
		{
			OnSetCharacter += CharacterControl_OnSetCharacter;
			IPlayerCharacter.OnStopLocalClient += (c) => ClearAll();
		}

		/// <summary>
		/// Called when the UI is being destroyed. Unsubscribes from events and clears all faction UI elements.
		/// </summary>
		public override void OnDestroying()
		{
			IPlayerCharacter.OnStopLocalClient -= (c) => ClearAll();
			OnSetCharacter -= CharacterControl_OnSetCharacter;

			ClearAll();
		}

		/// <summary>
		/// Called after setting the character reference. Subscribes to faction update events.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IFactionController factionController))
			{
				IFactionController.OnUpdateFaction += FactionController_OnUpdateFaction;
			}
		}

		/// <summary>
		/// Called before unsetting the character reference. Unsubscribes from faction update events and clears all faction UI elements.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			if (Character.TryGet(out IFactionController factionController))
			{
				IFactionController.OnUpdateFaction -= FactionController_OnUpdateFaction;
			}
			ClearAll();
		}

		/// <summary>
		/// Called when quitting to login. Clears all faction UI elements.
		/// </summary>
		public override void OnQuitToLogin()
		{
			ClearAll();
		}

		/// <summary>
		/// Handles character set event. Updates faction UI for all factions of the character.
		/// </summary>
		/// <param name="character">The player character.</param>
		private void CharacterControl_OnSetCharacter(IPlayerCharacter character)
		{
			if (character.TryGet(out IFactionController factionController))
			{
				if (factionController.Factions == null)
				{
					return;
				}
				foreach (Faction faction in factionController.Factions.Values)
				{
					FactionController_OnUpdateFaction(character, faction);
				}
			}
		}

		/// <summary>
		/// Handles faction update event. Updates or creates the faction description UI element.
		/// </summary>
		/// <param name="character">The character whose faction is updated.</param>
		/// <param name="faction">The faction data.</param>
		public void FactionController_OnUpdateFaction(ICharacter character, Faction faction)
		{
			if (faction == null)
			{
				return;
			}

			string hexColor;
			if (faction.Value > 0)
			{
				hexColor = "00FF00FF";
			}
			else if (faction.Value < 0)
			{
				hexColor = "FF0000FF";
			}
			else
			{
				hexColor = "87CEFAFF";
			}

			// Instantiate the Faction
			if (!factions.TryGetValue(faction.Template.ID, out UIFactionDescription description))
			{
				description = Instantiate(FactionDescriptionPrefab, FactionDescriptionParent);

				if (description.Label != null)
				{
					description.Label.text = $"<size=125%><color=#{hexColor}>{faction.Template.Name}</color></size>\r\n{faction.Template.Description}";
				}
				if (description.Image != null)
				{
					description.Image.sprite = faction.Template.Icon;
				}

				description.gameObject.SetActive(true);
				factions.Add(faction.Template.ID, description);
			}

			if (description != null)
			{
				if (description.Progress != null)
				{
					float progress = Normalize(faction.Value, FactionTemplate.Minimum, FactionTemplate.Maximum);

					description.Progress.value = progress;

					if (description.ProgressFillImage != null)
					{
						description.ProgressFillImage.color = Hex.ToColor(hexColor);
					}
				}
				if (description.Value != null)
				{
					description.Value.text = faction.Value.ToString();
				}
			}
		}

		/// <summary>
		/// Normalizes a value between a minimum and maximum range.
		/// </summary>
		/// <param name="x">The value to normalize.</param>
		/// <param name="min">The minimum value.</param>
		/// <param name="max">The maximum value.</param>
		/// <returns>The normalized value.</returns>
		private float Normalize(float x, float min, float max)
		{
			return (x - min) / (max - min);
		}

		/// <summary>
		/// Clears all faction description UI elements.
		/// </summary>
		public void ClearAll()
		{
			foreach (UIFactionDescription faction in new List<UIFactionDescription>(factions.Values))
			{
				Destroy(faction.gameObject);
			}
			factions.Clear();
		}
	}
}