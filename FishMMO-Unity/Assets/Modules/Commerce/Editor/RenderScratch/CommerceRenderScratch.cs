using UnityEditor;

namespace FishMMO.RenderScratch
{
	/// <summary>The Commerce module's part of the panel render rig: the trade window in the all-panels set and the grid fit probe.</summary>
	public static class CommerceRenderScratch
	{
		/// <summary>The trade window's layout.</summary>
		public const string TradeUxml = "Assets/Modules/Commerce/Client/UITrade.uxml";

		[InitializeOnLoadMethod]
		private static void Install()
		{
			AllPanelsRender.ExtraPopulators["UITrade"] = (h, d) => TradePanelPopulator.Trade(h, d);
			if (!GridFitProbe.Windows.Exists(w => w.uxml == TradeUxml))
			{
				GridFitProbe.Windows.Add((TradeUxml, (h, d) => TradePanelPopulator.Trade(h, d)));
			}
		}
	}
}
