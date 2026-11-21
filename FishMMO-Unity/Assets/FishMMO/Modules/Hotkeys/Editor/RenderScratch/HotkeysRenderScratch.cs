using UnityEditor;
using FishMMO.Client;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// The Hotkeys module's part of the panel render rig: the hotkey bar in the interface scaling render and the HUD
	/// overlap probe, where it always stood (after the debuff strip, before the cast bar). Moved with the module, 2026-11-20.
	/// </summary>
	public static class HotkeysRenderScratch
	{
		/// <summary>The hotkey bar's layout.</summary>
		public const string Uxml = "Assets/FishMMO/Modules/Hotkeys/Client/UIHotkeyBar.uxml";

		[InitializeOnLoadMethod]
		private static void Install()
		{
			if (!InterfaceScalingRender.Hud.Exists(m => m.Uxml == Uxml))
			{
				int cast = InterfaceScalingRender.Hud.FindIndex(m => m.PanelType != null && m.PanelType.Name == "UITKCastBar");
				InterfaceScalingRender.Hud.Insert(cast < 0 ? InterfaceScalingRender.Hud.Count : cast,
					new InterfaceScalingRender.HudMount { Uxml = Uxml, PanelType = typeof(UITKHotkeyBar) });
			}
			if (!HudOverlapProbe.Mounts.Exists(m => m.Name == "UIHotkeyBar"))
			{
				int cast = HudOverlapProbe.Mounts.FindIndex(m => m.Name == "UICastBar");
				HudOverlapProbe.Mounts.Insert(cast < 0 ? HudOverlapProbe.Mounts.Count : cast,
					new HudOverlapProbe.Mount { Name = "UIHotkeyBar", Uxml = Uxml, Selector = "hotkey-list", PanelType = typeof(UITKHotkeyBar) });
			}
		}
	}
}
