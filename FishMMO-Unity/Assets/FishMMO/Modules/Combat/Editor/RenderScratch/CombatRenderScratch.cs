using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// The Combat module's part of the panel render rig (moved with the module): the death dialog in the
	/// all-panels render.
	/// </summary>
	public static class CombatRenderScratch
	{
		[InitializeOnLoadMethod]
		private static void Install()
		{
			AllPanelsRender.ExtraPopulators["UIDeathDialog"] = DeathDialog;
		}

		/// <summary>Populates the death dialog, shown as a death shows it.</summary>
		public static void DeathDialog(GameObject h, UIDocument d)
		{
			UITKDeathDialog dd = h.AddComponent<UITKDeathDialog>();
			dd.Document = d;
			dd.OnStarting();
			dd.ShowDeathDialog();
		}
	}
}
