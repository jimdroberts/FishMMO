using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>The Ability Crafting module's part of the ability panel renders: the crafter, populated and empty.</summary>
	public static class AbilityCraftingRenderScratch
	{
		/// <summary>The crafting window's layout.</summary>
		public const string CraftUxml = "Assets/Modules/AbilityCrafting/Client/UIAbilityCraft.uxml";

		[InitializeOnLoadMethod]
		private static void Install()
		{
			AbilityPanelsRender.ExtraJobs.RemoveAll(j => j.uxml == CraftUxml);
			AbilityPanelsRender.ExtraJobs.Add(("UIAbilityCraft-populated", CraftUxml, (h, d) => Craft(h, d, populated: true)));
			AbilityPanelsRender.ExtraJobs.Add(("UIAbilityCraft-empty", CraftUxml, (h, d) => Craft(h, d, populated: false)));
		}

		/// <summary>
		/// The crafting panel, optionally with a recipe already assembled.
		/// </summary>
		/// <param name="populated">
		/// True to build a fireball with a movement effect and a fire-damage effect, which is what
		/// the preview and its deltas exist to show.
		/// </param>
		public static void Craft(GameObject h, UIDocument d, bool populated)
		{
			PlayerCharacter character = AbilityPanels.BuildCharacter(h);

			UITKAbilityCraft panel = h.AddComponent<UITKAbilityCraft>();
			panel.Document = d;
			panel.StartOpen = false;
			panel.CurrencyTemplateID = AbilityFixtures.CurrencyTemplateID();
			AbilityPanels.Awake(panel);

			panel.Show();
			panel.SetCharacter(character);

			if (populated)
			{
				AbilityTemplate fireball = AbilityFixtures.Base("Lesser Fireball");
				List<ITooltip> chosen = new List<ITooltip>
				{
					AbilityFixtures.Effect("Giant"),
					AbilityFixtures.Effect("Swiftfire"),
					null,
				};

				/* Reflection, because the recipe is private state the panel owns and the shipped
				 * way in is a modal selector this harness cannot drive. The panel is then asked to
				 * redraw itself exactly as it would after a selection. */
				AbilityPanels.Set(panel, "selectedMain", fireball);
				AbilityPanels.Set(panel, "selectedSlotCount", fireball != null ? (int)fireball.AdditionalEventSlots : 0);

				var events = (List<ITooltip>)AbilityPanels.Field(panel, "selectedEvents").GetValue(panel);
				events.Clear();
				events.AddRange(chosen);
			}

			AbilityPanels.Invoke(panel, "ApplySelection");
		}
	}
}
