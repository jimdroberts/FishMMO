using System.Collections;
using System.IO;
using System.Reflection;
using FishMMO.Client;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Optional nameplate icons (issue #259): off unless a style or a plate asks for one, and
	/// drawn beside or above the rows when one does.
	/// </summary>
	/// <remarks>
	/// The layer half mounts the real world-label UXML and drives the real
	/// <see cref="UITKNameplateLayer"/> against a real camera. Nothing on a MonoBehaviour runs by
	/// itself in edit mode, so the plate's and the layer's OnEnable/LateUpdate/OnDisable are
	/// invoked by reflection — the same calls Unity makes in play.
	/// </remarks>
	[TestFixture]
	public class NameplateIconTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/WorldLabels/UIWorldLabels.uxml";
		private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

		private GameObject plateHost;
		private Nameplate plate;
		private Texture2D texture;
		private Sprite sprite;

		private GameObject layerHost;
		private GameObject cameraHost;
		private PanelSettings settings;
		private UITKNameplateLayer layer;

		[SetUp]
		public void SetUp()
		{
			plateHost = new GameObject("NameplateIconHost");
			plate = plateHost.AddComponent<Nameplate>();

			texture = new Texture2D(8, 8);
			sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f));
		}

		[TearDown]
		public void TearDown()
		{
			if (layer != null)
			{
				Invoke(layer, "OnDisable");
			}
			if (plate != null && ContainsPlate(plate))
			{
				// Takes it back out of the static registry the layer walks.
				Invoke(plate, "OnDisable");
			}
			if (layerHost != null) Object.DestroyImmediate(layerHost);
			if (cameraHost != null) Object.DestroyImmediate(cameraHost);
			if (settings != null) Object.DestroyImmediate(settings);
			if (plateHost != null) Object.DestroyImmediate(plateHost);
			if (sprite != null) Object.DestroyImmediate(sprite);
			if (texture != null) Object.DestroyImmediate(texture);
			layer = null;
		}

		private static bool ContainsPlate(Nameplate target)
		{
			for (int i = 0; i < Nameplate.Active.Count; ++i)
			{
				if (ReferenceEquals(Nameplate.Active[i], target))
				{
					return true;
				}
			}
			return false;
		}

		private static void Invoke(object target, string method)
		{
			MethodInfo info = target.GetType().GetMethod(method, Hidden);
			LogAssert.IsNotNull(info, $"{target.GetType().Name}.{method} exists");
			info.Invoke(target, null);
		}

		// --- Model ------------------------------------------------------------------------

		[Test]
		public void APlate_HasNoIconByDefault()
		{
			LogAssert.IsNull(NameplateStyle.Default.Icon, "the default style carries no icon");
			LogAssert.IsNull(NameplateStylePresets.Boss.Icon, "and neither do the presets");
			LogAssert.IsNull(plate.Icon, "so an unconfigured plate draws none");
			LogAssert.IsFalse(plate.HasIconOverride, "and has no runtime icon");
		}

		[Test]
		public void AStyleIcon_IsThePlatesIcon()
		{
			NameplateStyleAsset asset = ScriptableObject.CreateInstance<NameplateStyleAsset>();
			try
			{
				NameplateStyle boss = NameplateStylePresets.Boss;
				boss.Icon = sprite;
				asset.SetStyle(boss);
				plate.SetStyle(asset);

				LogAssert.AreEqual(sprite, plate.Icon, "a shared style gives every plate using it the icon");
			}
			finally
			{
				Object.DestroyImmediate(asset);
			}
		}

		[Test]
		public void ARuntimeIcon_OverridesTheStyleAndClearingItRestoresTheStyle()
		{
			NameplateStyle style = NameplateStyle.Default;
			style.Icon = sprite;
			plate.SetStyle(style);

			int revision = plate.StyleRevision;
			plate.SetIcon(null);
			LogAssert.IsNull(plate.Icon, "an explicit null hides the style's icon");
			LogAssert.IsTrue(plate.StyleRevision != revision, "the renderer is told to re-draw");

			revision = plate.StyleRevision;
			plate.SetIcon(null);
			LogAssert.AreEqual(revision, plate.StyleRevision, "an unchanged write moves nothing");

			plate.ClearIcon();
			LogAssert.AreEqual(sprite, plate.Icon, "clearing the override brings the style's icon back");
			LogAssert.IsFalse(plate.HasIconOverride, "and leaves no override behind");
		}

		[Test]
		public void ARuntimeIcon_DoesNotSurviveThePool()
		{
			// A pooled character comes back as someone else; a quest marker must not come with it.
			Invoke(plate, "OnEnable");
			plate.SetIcon(sprite);
			Invoke(plate, "OnDisable");

			LogAssert.IsFalse(plate.HasIconOverride, "disabling the plate drops the runtime icon");
			LogAssert.IsNull(plate.Icon, "so the next life starts from its style");
		}

		[Test]
		public void TheIconTint_IsTheSpritesOwnColoursUnlessAskedForAlliance()
		{
			NameplateStyle style = NameplateStyle.Default;
			LogAssert.AreEqual(Color.white, style.ResolveIconTint(Color.red), "a fixed icon is drawn as authored");

			style.IconTint = NameplateTint.Alliance;
			style.IconBlend = 1.0f;
			Color tinted = style.ResolveIconTint(Color.red);
			LogAssert.AreEqual(Color.red, tinted, "a full alliance blend is the standing colour");
		}

		// --- Layer ------------------------------------------------------------------------

		/// <summary>Mounts the real layer over a camera looking at the plate.</summary>
		private void MountLayer()
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the world label UXML must exist at {UxmlPath}");
			settings = Object.Instantiate(asset);

			cameraHost = new GameObject("NameplateIconCamera");
			cameraHost.transform.position = new Vector3(0.0f, 0.0f, -5.0f);
			Camera camera = cameraHost.AddComponent<Camera>();

			layerHost = new GameObject("NameplateIconLayer");
			UIDocument document = layerHost.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = uxml;
			layer = layerHost.AddComponent<UITKNameplateLayer>();
			layer.ProjectionCamera = camera;

			plate.Visible = true;
			Invoke(plate, "OnEnable");
			Invoke(layer, "OnEnable");
		}

		/// <summary>Runs the layer and lets Yoga place what it wrote.</summary>
		private IEnumerator Settle()
		{
			for (int frame = 0; frame < 8; ++frame)
			{
				Invoke(layer, "LateUpdate");
				yield return null;
			}
		}

		private VisualElement Root => layerHost.GetComponent<UIDocument>().rootVisualElement;

		private VisualElement Icon => Root.Q(className: "nameplate-icon");

		private VisualElement Body => Root.Q(className: "nameplate-body");

		private VisualElement Anchor => Root.Q(className: "nameplate-anchor");

		[UnityTest]
		public IEnumerator APlateWithoutAnIcon_DrawsNone()
		{
			MountLayer();
			plate.SetLine(NameplateSlot.Name, "Bram");
			yield return Settle();

			LogAssert.IsTrue(Anchor.resolvedStyle.display == DisplayStyle.Flex, "the plate itself is drawn");
			LogAssert.IsNotNull(Icon, "every plate has an icon element to fill");
			LogAssert.IsTrue(Icon.resolvedStyle.display == DisplayStyle.None, "and it stays hidden without an icon");
			LogAssert.IsNotNull(Body.Q<Label>(className: "nameplate-line"), "the rows live in the body");
		}

		[UnityTest]
		public IEnumerator AnIcon_SitsWhereTheStylePlacesIt()
		{
			MountLayer();
			plate.SetLine(NameplateSlot.Name, "Bram");
			plate.SetIcon(sprite);
			NameplateStyle style = NameplateStyle.Default;

			style.IconPlacement = NameplateIconPlacement.Left;
			plate.SetStyle(style);
			yield return Settle();

			LogAssert.IsTrue(Icon.resolvedStyle.display == DisplayStyle.Flex, "the icon is drawn");
			LogAssert.AreEqual(sprite, Icon.resolvedStyle.backgroundImage.sprite, "with the plate's sprite");
			LogAssert.IsTrue(Icon.layout.width > 0.0f && Icon.layout.height > 0.0f,
				$"at a real size (got {Icon.layout.width}x{Icon.layout.height})");
			LogAssert.IsTrue(Icon.layout.xMax <= Body.layout.xMin,
				$"left: icon ends at {Icon.layout.xMax}, rows start at {Body.layout.xMin}");

			style.IconPlacement = NameplateIconPlacement.Right;
			plate.SetStyle(style);
			yield return Settle();
			LogAssert.IsTrue(Icon.layout.xMin >= Body.layout.xMax,
				$"right: icon starts at {Icon.layout.xMin}, rows end at {Body.layout.xMax}");

			style.IconPlacement = NameplateIconPlacement.Above;
			plate.SetStyle(style);
			yield return Settle();
			LogAssert.IsTrue(Icon.layout.yMax <= Body.layout.yMin,
				$"above: icon ends at y {Icon.layout.yMax}, rows start at y {Body.layout.yMin}");

			plate.SetIcon(null);
			yield return Settle();
			LogAssert.IsTrue(Icon.resolvedStyle.display == DisplayStyle.None, "removing the icon hides it again");
		}

		[UnityTest]
		public IEnumerator AnIconWithNoRows_IsStillDrawn()
		{
			// A marker over something with no name is a plate worth drawing; an empty box is not.
			MountLayer();
			yield return Settle();
			LogAssert.IsTrue(Anchor.resolvedStyle.display == DisplayStyle.None, "no rows and no icon: nothing");

			plate.SetIcon(sprite);
			yield return Settle();
			LogAssert.IsTrue(Anchor.resolvedStyle.display == DisplayStyle.Flex, "an icon alone brings the plate up");
			LogAssert.AreEqual(0.0f, Icon.resolvedStyle.marginRight, "with no gap pushing it off-centre");
		}

		[UnityTest]
		public IEnumerator ThePlayersIconToggle_HidesEveryIcon()
		{
			// A scratch store, so the toggle never touches the developer's own settings.
			FieldInfo store = typeof(Configuration).GetField("globalSettings", BindingFlags.NonPublic | BindingFlags.Static);
			LogAssert.IsNotNull(store, "Configuration keeps its global store in a static field");
			Configuration previous = Configuration.GlobalSettings;
			Configuration.SetGlobalSettings(new Configuration(
				Path.Combine(Path.GetTempPath(), "FishMMO-NameplateIconTests")));
			try
			{
				MountLayer();
				plate.SetLine(NameplateSlot.Name, "Bram");
				plate.SetIcon(sprite);
				yield return Settle();
				LogAssert.IsTrue(Icon.resolvedStyle.display == DisplayStyle.Flex, "icons are drawn by default");

				ClientNameplateSettings.SetShowIcons(false);
				yield return Settle();
				LogAssert.IsTrue(Icon.resolvedStyle.display == DisplayStyle.None, "turning icons off hides a drawn one");
				LogAssert.IsTrue(Anchor.resolvedStyle.display == DisplayStyle.Flex, "and leaves the name up");

				plate.ClearLine(NameplateSlot.Name);
				yield return Settle();
				LogAssert.IsTrue(Anchor.resolvedStyle.display == DisplayStyle.None,
					"an icon-only plate goes away with its icon");

				ClientNameplateSettings.SetShowIcons(true);
				yield return Settle();
				LogAssert.IsTrue(Anchor.resolvedStyle.display == DisplayStyle.Flex, "and comes back with it");
			}
			finally
			{
				store.SetValue(null, previous);
			}
		}
	}
}
