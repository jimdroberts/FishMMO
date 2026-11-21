using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// The Abilities module's part of the render rig (2026-11-20): the cast bar in the interface scaling render and the HUD
	/// overlap probe, and the ability tooltips in the tooltip gallery. Moved out of the core rig with the module.
	/// </summary>
	public static class AbilitiesRenderScratch
	{
		/// <summary>The cast bar's layout.</summary>
		public const string CastBarUxml = "Assets/FishMMO/Modules/Abilities/Client/Ability/UICastBar.uxml";

		[InitializeOnLoadMethod]
		private static void Install()
		{
			if (!InterfaceScalingRender.Hud.Exists(m => m.Uxml == CastBarUxml))
			{
				int crosshair = InterfaceScalingRender.Hud.FindIndex(m => m.PanelType == typeof(UITKCrosshair));
				InterfaceScalingRender.Hud.Insert(crosshair < 0 ? InterfaceScalingRender.Hud.Count : crosshair,
					new InterfaceScalingRender.HudMount { Uxml = CastBarUxml, PanelType = typeof(UITKCastBar) });
			}
			if (!HudOverlapProbe.Mounts.Exists(m => m.Name == "UICastBar"))
			{
				int crosshair = HudOverlapProbe.Mounts.FindIndex(m => m.Name == "UICrosshair");
				HudOverlapProbe.Mounts.Insert(crosshair < 0 ? HudOverlapProbe.Mounts.Count : crosshair,
					new HudOverlapProbe.Mount { Name = "UICastBar", Uxml = CastBarUxml, Selector = "castbar-root", PanelType = typeof(UITKCastBar) });
			}
			if (!TooltipGalleryRender.ExtraPages.Exists(p => p.label == "Tooltips-abilities"))
			{
				TooltipGalleryRender.ExtraPages.Add(("Tooltips-abilities", () =>
				{
					AbilityFixtures.Load();
					TooltipSubjects.Load();
					TooltipSubjects.RegisterAll<AbilityEvent>("Assets/FishMMO/Modules/Abilities/Templates");
					return Abilities();
				}));
			}
		}

		/// <summary>Every ability-side tooltip: base ability, effect, crafted ability, override, knowledge scrolls.</summary>
		public static List<(string, TooltipContent)> Abilities()
		{
			List<(string, TooltipContent)> cards = new List<(string, TooltipContent)>();

			AbilityTemplate fireball = AbilityFixtures.Base("Lesser Fireball");
			if (fireball != null)
			{
				cards.Add(($"Base ability — {fireball.name}", fireball.BuildContent()));

				Ability crafted = new Ability(9101, fireball, new List<int>
				{
					AbilityFixtures.Effect("Giant")?.ID ?? 0,
					AbilityFixtures.Effect("Swiftfire")?.ID ?? 0,
				});
				cards.Add(($"Crafted ability — {crafted.Name}", crafted.BuildContent()));

				/* The crafting preview: the same composition, with the deltas the panel draws. */
				AbilitySummary summary = AbilitySummary.Compose(fireball, new List<ITooltip>
				{
					AbilityFixtures.Effect("Giant"),
					AbilityFixtures.Effect("Swiftfire"),
				});
				TooltipContent preview = new TooltipContent();
				summary.BuildTooltip(preview, showDeltas: true);
				cards.Add(("Crafting preview (with deltas)", preview));
			}

			AbilityEvent effect = AbilityFixtures.Effect("Lesser Fire Damage");
			if (effect != null)
			{
				cards.Add(($"Ability effect — {effect.name}", effect.BuildContent()));
			}

			AbilityEvent modifier = AbilityFixtures.Effect("Giant");
			if (modifier != null)
			{
				cards.Add(($"Ability effect (modifier) — {modifier.name}", modifier.BuildContent()));
			}

			if (AbilityFixtures.Overrides.Count > 0)
			{
				AbilityTypeOverrideEventType typeOverride = AbilityFixtures.Overrides[0];
				cards.Add(($"Type override — {typeOverride.name}", typeOverride.BuildContent()));
			}

			KnowledgeScrollTemplate abilityScroll = TooltipSubjects.Find<KnowledgeScrollTemplate>("Scroll of Lesser Fireball");
			if (abilityScroll != null)
			{
				cards.Add(("Knowledge scroll (ability) — " + abilityScroll.name, abilityScroll.BuildContent()));
			}

			KnowledgeScrollTemplate effectScroll = TooltipSubjects.Find<KnowledgeScrollTemplate>("Scroll of Flame Impact");
			if (effectScroll != null)
			{
				cards.Add(("Knowledge scroll (effects) — " + effectScroll.name, effectScroll.BuildContent()));
			}

			KnowledgeScrollTemplate codex = TooltipSubjects.Find<KnowledgeScrollTemplate>("Codex of the Burning Sphere");
			if (codex != null)
			{
				cards.Add(("Knowledge scroll (both) — " + codex.name, codex.BuildContent()));
			}

			CharacterAttributeTemplate attribute = TooltipSubjects.FirstWhere<CharacterAttributeTemplate>(t => t.name == "Health")
				?? TooltipSubjects.First<CharacterAttributeTemplate>();
			if (attribute != null)
			{
				cards.Add(($"Character attribute — {attribute.name}", attribute.BuildContent()));
			}

			return cards;
		}
	}
}
