using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins how the one shared <see cref="PanelSettings"/> asset scales the interface.
	/// </summary>
	/// <remarks>
	/// Every layout in the client is authored in a panel exactly 1200 units wide whose height
	/// follows the screen — 675 at 16:9, 800 at 3:2, 506 at 21:9. The HUD's fixed offsets, the
	/// tiled item windows and every panel width were measured in that space. It is what
	/// ScaleWithScreenSize against a 1200x800 reference matched on width produces, and nothing else
	/// does.
	/// <para>
	/// The asset shipped as ConstantPhysicalSize for a while, which nobody saw because every render
	/// probe targets a 1200-wide texture where the two modes agree. On a real display it silently
	/// disabled two things at once: the interface stopped scaling with resolution (a 720-unit window
	/// was 720 pixels at 2560x1440), and the Interface Scale option, which works by rewriting the
	/// reference resolution, did nothing at all.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PanelSettingsScaleModeTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";

		private const string Why =
			"every layout is authored in 1200-unit-wide panel space (ScaleWithScreenSize, 1200x800 " +
			"reference, match width); ConstantPhysicalSize silently disabled both resolution scaling " +
			"and the Interface Scale setting, which rewrites the reference resolution";

		private static PanelSettings Load()
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");
			return asset;
		}

		[Test]
		public void ScalesWithScreenSize()
		{
			LogAssert.AreEqual(PanelScaleMode.ScaleWithScreenSize, Load().scaleMode,
				$"PanelSettings.scaleMode must be ScaleWithScreenSize: {Why}");
		}

		[Test]
		public void MatchesOnWidth()
		{
			PanelSettings asset = Load();
			LogAssert.AreEqual(PanelScreenMatchMode.MatchWidthOrHeight, asset.screenMatchMode,
				$"PanelSettings.screenMatchMode must be MatchWidthOrHeight: {Why}");
			LogAssert.AreEqual(0.0f, asset.match,
				$"PanelSettings.match must be 0 (width), so the panel is always 1200 units wide: {Why}");
		}

		[Test]
		public void ReferenceResolutionIs1200By800()
		{
			LogAssert.AreEqual(new Vector2Int(1200, 800), Load().referenceResolution,
				$"PanelSettings.referenceResolution must be the authored 1200x800 — a different value " +
				$"in the asset usually means UITKPanelScale's play-mode write leaked into it: {Why}");
		}
	}
}
