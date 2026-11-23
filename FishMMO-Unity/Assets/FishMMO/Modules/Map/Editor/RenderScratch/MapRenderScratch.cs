using UnityEditor;
using FishMMO.Client;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// The Map module's part of the panel render rig: the minimap in the interface scaling render and the HUD overlap
	/// probe, where it always stood (after the cast bar), and the minimap modes of the play-mode leak probe
	/// (<see cref="MinimapLeakModes"/>). Moved with the module.
	/// </summary>
	public static class MapRenderScratch
	{
		/// <summary>The minimap's layout.</summary>
		public const string MinimapUxml = "Assets/FishMMO/Modules/Map/Client/UIMinimap.uxml";

		[InitializeOnLoadMethod]
		private static void Install()
		{
			if (!InterfaceScalingRender.Hud.Exists(m => m.Uxml == MinimapUxml))
			{
				int cast = InterfaceScalingRender.Hud.FindIndex(m => m.PanelType == typeof(UITKCastBar));
				InterfaceScalingRender.Hud.Insert(cast < 0 ? InterfaceScalingRender.Hud.Count : cast + 1,
					new InterfaceScalingRender.HudMount { Uxml = MinimapUxml, PanelType = typeof(UITKMinimap) });
			}
			if (!HudOverlapProbe.Mounts.Exists(m => m.Name == "UIMinimap"))
			{
				int cast = HudOverlapProbe.Mounts.FindIndex(m => m.Name == "UICastBar");
				HudOverlapProbe.Mounts.Insert(cast < 0 ? HudOverlapProbe.Mounts.Count : cast + 1,
					new HudOverlapProbe.Mount { Name = "UIMinimap", Uxml = MinimapUxml, Selector = "minimap-frame", PanelType = typeof(UITKMinimap) });
			}
			if (!PlayLeakProbe.ModuleModes.Exists(m => m is MinimapLeakModes))
			{
				PlayLeakProbe.ModuleModes.Add(new MinimapLeakModes());
			}
		}
	}
}
