using System.Collections.Generic;
using FishMMO.Shared;
using UnityEditor;

namespace FishMMO.RenderScratch
{
	/// <summary>The Commerce module's part of the panel render rig: the trade window in the all-panels set and the grid fit probe, and the premade ability tooltip.</summary>
	public static class CommerceRenderScratch
	{
		/// <summary>The trade window's layout.</summary>
		public const string TradeUxml = "Assets/FishMMO/Modules/Commerce/Client/UITrade.uxml";

		[InitializeOnLoadMethod]
		private static void Install()
		{
			AllPanelsRender.ExtraPopulators["UITrade"] = (h, d) => TradePanelPopulator.Trade(h, d);
			if (!GridFitProbe.Windows.Exists(w => w.uxml == TradeUxml))
			{
				GridFitProbe.Windows.Add((TradeUxml, (h, d) => TradePanelPopulator.Trade(h, d)));
			}
			if (!TooltipGalleryRender.ExtraPages.Exists(p => p.label == "Tooltips-commerce"))
			{
				TooltipGalleryRender.ExtraPages.Add(("Tooltips-commerce", Tooltips));
			}
		}

		/// <summary>The merchant's premade ability, as its tooltip reads.</summary>
		private static List<(string, TooltipContent)> Tooltips()
		{
			TooltipSubjects.Load();
			TooltipSubjects.RegisterAll<PremadeAbilityTemplate>("Assets/FishMMO/Modules/Commerce/Templates/Premade");
			var cards = new List<(string, TooltipContent)>();
			PremadeAbilityTemplate premade = TooltipSubjects.First<PremadeAbilityTemplate>();
			if (premade != null)
			{
				cards.Add(($"Premade ability (merchant) — {premade.name}", premade.BuildContent()));
			}
			return cards;
		}
	}
}
