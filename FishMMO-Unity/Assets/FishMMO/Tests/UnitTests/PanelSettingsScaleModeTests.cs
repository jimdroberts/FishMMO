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
	/// The interface is laid out for a 1280x960 (4:3) screen and fitted to the window (scaled to fit
	/// 4:3; 1280x960 is the default size for everything): ScaleWithScreenSize, reference 1280x960, Expand. The
	/// whole 1280x960 area is always on screen, and a wider (or taller) window adds room rather than enlarging everything.
	/// It replaced matching a 1200x800 reference on WIDTH, which grew the interface with a wide browser window — too large on
	/// the web even at 75%. Every panel was authored for 1200x800, which fits inside 1280x960 at every aspect ratio.
	/// <para>
	/// The asset shipped as ConstantPhysicalSize for a while, which silently disabled two things at once: the interface
	/// stopped scaling with resolution, and the Interface Scale option, which works by rewriting the reference resolution,
	/// did nothing at all.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PanelSettingsScaleModeTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";

		private const string Why =
			"the interface is laid out for 1280x960 and fitted to the window (ScaleWithScreenSize, 1280x960 reference, " +
			"Expand); ConstantPhysicalSize silently disabled both resolution scaling and the Interface Scale setting, which " +
			"rewrites the reference resolution";

		private static PanelSettings Load()
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(ProjectFiles.Path(PanelSettingsPath));
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
		public void ExpandsToFitTheReference()
		{
			LogAssert.AreEqual(PanelScreenMatchMode.Expand, Load().screenMatchMode,
				$"PanelSettings.screenMatchMode must be Expand, so the whole 1280x960 always fits: {Why}");
		}

		[Test]
		public void ReferenceResolutionIs1280By960()
		{
			LogAssert.AreEqual(new Vector2Int(1280, 960), Load().referenceResolution,
				$"PanelSettings.referenceResolution must be the authored 1280x960 — a different value " +
				$"in the asset usually means UITKPanelScale's play-mode write leaked into it: {Why}");
		}
	}
}
