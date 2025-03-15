using UnityEngine;
using FishMMO.Shared;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class UIFactions : UICharacterControl
	{
		public RectTransform FactionDescriptionParent;
		public UIFactionDescription FactionDescriptionPrefab;

		private Dictionary<int, UIFactionDescription> factions = new Dictionary<int, UIFactionDescription>();


		public override void OnStarting()
		{
			OnSetCharacter += CharacterControl_OnSetCharacter;
			IPlayerCharacter.OnStopLocalClient += (c) => ClearAll();
		}

		public override void OnDestroying()
		{
			IPlayerCharacter.OnStopLocalClient -= (c) => ClearAll();
			OnSetCharacter -= CharacterControl_OnSetCharacter;

			ClearAll();
		}

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IFactionController factionController))
			{
				IFactionController.OnUpdateFaction += FactionController_OnUpdateFaction;
			}
		}

		public override void OnPreUnsetCharacter()
		{
			if (Character.TryGet(out IFactionController factionController))
			{
				IFactionController.OnUpdateFaction -= FactionController_OnUpdateFaction;
			}
			ClearAll();
		}

		public override void OnQuitToLogin()
		{
			ClearAll();
		}

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

		private float Normalize(float x, float min, float max)
		{
			return (x - min) / (max - min);
		}

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