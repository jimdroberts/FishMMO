using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using UnityEditor;
using NUnit.Framework;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Hiding a panel keeps its UIDocument, visual tree and initialisation; showing it again only
	/// makes it visible.
	/// </summary>
	/// <remarks>
	/// <para>UITKControl used to hide by disabling the UIDocument. Unity's UIDocument discards its
	/// whole tree on disable, so every show cloned the UXML again and re-ran OnStarting: about
	/// 77 KB of allocation per open of the target frame, which re-opens whenever the hovered target
	/// changes. The UGUI panels this replaced deactivated their GameObject, which kept the
	/// hierarchy, and that is the behaviour pinned here.</para>
	/// <para>Hiding is a visibility change on the root. Visibility is inherited and UI Toolkit only
	/// picks and focuses visible elements, so a hidden panel must also take no clicks and hold no
	/// focus; discarding the tree used to guarantee both as a side effect.</para>
	/// </remarks>
	[TestFixture]
	public class PanelHideKeepsTreeTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/Toast/UIToast.uxml";

		/// <summary>The smallest possible panel: counts its initialisation and nothing else.</summary>
		private sealed class ProbePanel : UITKControl
		{
			public int Starts;

			public override void OnStarting()
			{
				++Starts;
			}
		}

		private PanelSettings settings;
		private RenderTexture texture;
		private GameObject host;
		private UIDocument document;
		private ProbePanel panel;

		[SetUp]
		public void SetUp()
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"markup must exist at {UxmlPath}");

			texture = new RenderTexture(1200, 800, 24, RenderTextureFormat.ARGB32);
			texture.Create();
			settings = Object.Instantiate(asset);
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;

			host = new GameObject("PanelHideKeepsTreeTests");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = uxml;

			// A start-hidden panel exactly as a scene authors one: the document is left enabled.
			panel = host.AddComponent<ProbePanel>();
			panel.Document = document;
			panel.StartOpen = false;
			panel.IsAlwaysOpen = false;
			panel.CloseOnEscape = false;
			panel.ReleasesCursor = false;

			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake, "UITKControl must still declare Awake");
			awake.Invoke(panel, null);
		}

		[TearDown]
		public void TearDown()
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
			if (settings != null)
			{
				Object.DestroyImmediate(settings);
			}
			if (texture != null)
			{
				texture.Release();
				Object.DestroyImmediate(texture);
			}
			host = null;
			document = null;
			panel = null;
			settings = null;
			texture = null;
		}

		private static bool IsHidden(VisualElement root)
		{
			return root.style.visibility.keyword == StyleKeyword.Undefined && root.style.visibility.value == Visibility.Hidden;
		}

		[Test]
		public void HidingAndShowingKeepsTheDocumentTheTreeAndTheInitialisation()
		{
			VisualElement root = document.rootVisualElement;
			LogAssert.IsNotNull(root, "the document built its tree");
			LogAssert.IsTrue(root.childCount > 0, "and cloned the markup into it");
			VisualElement content = root[0];

			LogAssert.IsFalse(panel.Visible, "a start-hidden panel reports hidden");
			LogAssert.IsTrue(document.enabled, "without disabling its document");
			LogAssert.IsTrue(IsHidden(root), "its root is hidden by visibility");
			LogAssert.AreEqual(1, panel.Starts, "it initialised once, as soon as the tree existed");

			panel.Show();
			LogAssert.IsTrue(panel.Visible, "shown");
			LogAssert.IsTrue(root.style.visibility.keyword == StyleKeyword.Null, "the root inherits visibility again rather than pinning Visible");
			LogAssert.AreSame(root, document.rootVisualElement, "showing does not replace the root");
			LogAssert.AreSame(content, document.rootVisualElement[0], "or clone the markup again");

			panel.Hide();
			panel.Show();
			panel.Hide();
			LogAssert.IsTrue(document.enabled, "hiding never disables the document");
			LogAssert.AreSame(root, document.rootVisualElement, "the root survives repeated hiding and showing");
			LogAssert.AreSame(content, document.rootVisualElement[0], "and so does everything under it");
			LogAssert.AreEqual(1, panel.Starts, "initialisation never runs again");
			LogAssert.IsTrue(IsHidden(root), "hidden again");
		}

		[UnityTest]
		public IEnumerator AHiddenPanelTakesNoClicksAndHoldsNoFocus()
		{
			VisualElement root = document.rootVisualElement;
			TextField field = new TextField("probe") { name = "probe-field" };
			field.style.position = Position.Absolute;
			field.style.left = 100;
			field.style.top = 100;
			field.style.width = 300;
			field.style.height = 40;
			root.Add(field);

			panel.Show();
			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}

			Rect bounds = field.worldBound;
			LogAssert.IsTrue(bounds.width > 1f && bounds.height > 1f, $"the field must be laid out; it resolved to {bounds.width}x{bounds.height}");
			VisualElement pickedShown = root.panel.Pick(bounds.center);
			LogAssert.IsTrue(pickedShown != null && (pickedShown == field || field.Contains(pickedShown)),
				$"while shown, a click on the field reaches it; it reached {(pickedShown == null ? "nothing" : pickedShown.name)}");

			field.Focus();
			VisualElement focusedBefore = root.panel.focusController.focusedElement as VisualElement;
			LogAssert.IsTrue(focusedBefore != null && (focusedBefore == field || field.Contains(focusedBefore)),
				"the field takes focus while the panel is shown");

			panel.Hide();
			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}

			VisualElement focusedAfter = root.panel.focusController.focusedElement as VisualElement;
			LogAssert.IsTrue(focusedAfter == null || !root.Contains(focusedAfter),
				"hiding releases focus held inside the panel, or a hidden field would keep taking keystrokes");

			LogAssert.IsTrue(field.resolvedStyle.visibility == Visibility.Hidden, "the field inherits the hidden root");
			VisualElement pickedHidden = root.panel.Pick(bounds.center);
			LogAssert.IsTrue(pickedHidden == null || !root.Contains(pickedHidden),
				$"a click where a hidden panel's field sits must not reach anything in it; it reached {(pickedHidden == null ? "nothing" : pickedHidden.name)}");
		}
	}
}
