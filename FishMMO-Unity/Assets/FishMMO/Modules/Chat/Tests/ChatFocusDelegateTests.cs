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
	/// Enter opening chat must focus the field although the chat keeps every element out of the Tab ring.
	/// </summary>
	/// <remarks>
	/// The browser log: canGrabFocus True, chat panel selected in the EventSystem, yet focus on nothing after every
	/// <c>Focus()</c> and never a FocusOut. A TextField delegates focus to its first child that can grab focus AND has
	/// <c>tabIndex &gt;= 0</c>; the chat sets -1 on every focusable element, so the delegate was null and
	/// <c>SwitchFocus(null)</c> did nothing. A click lands on the inner text element and needs no delegate.
	/// </remarks>
	[TestFixture]
	public class ChatFocusDelegateTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";

		private PanelSettings settings;
		private RenderTexture texture;
		private GameObject host;
		private UIDocument document;

		[SetUp]
		public void SetUp()
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(ProjectFiles.Path(PanelSettingsPath));
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");
			texture = new RenderTexture(800, 600, 24, RenderTextureFormat.ARGB32);
			texture.Create();
			settings = Object.Instantiate(asset);
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;

			host = new GameObject("ChatFocusDelegateTests");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
		}

		[TearDown]
		public void TearDown()
		{
			if (host != null) Object.DestroyImmediate(host);
			if (settings != null) Object.DestroyImmediate(settings);
			if (texture != null)
			{
				texture.Release();
				Object.DestroyImmediate(texture);
			}
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
		}

		private IEnumerator FieldOutOfTheTabRing(TextField field)
		{
			VisualElement root = document.rootVisualElement;
			field.style.position = Position.Absolute;
			field.style.left = 50;
			field.style.top = 50;
			field.style.width = 300;
			field.style.height = 40;
			root.Add(field);
			for (int frame = 0; frame < 5; ++frame)
			{
				yield return null;
			}
			// What UITKChat.ExcludeFromTabNavigation does to the whole chat panel.
			root.Query<VisualElement>().ForEach(e =>
			{
				if (e.focusable)
				{
					e.tabIndex = -1;
				}
			});
		}

		private static bool FocusIsIn(TextField field)
		{
			VisualElement focused = field.panel.focusController.focusedElement as VisualElement;
			return focused != null && (focused == field || field.Contains(focused));
		}

		/// <summary>Pins the UI Toolkit behaviour the bug came from, so a change to it is noticed.</summary>
		[UnityTest]
		public IEnumerator AFieldOutOfTheTabRing_CannotBeFocusedThroughItself()
		{
			TextField field = new TextField { name = "chat-input" };
			yield return FieldOutOfTheTabRing(field);

			field.Focus();

			LogAssert.IsFalse(FocusIsIn(field),
				"UI Toolkit's delegate search skips tab index -1; if this now focuses, the leaf workaround is no longer needed");
		}

		[UnityTest]
		public IEnumerator TheChatFocusesTheFieldsInnerElement_DespiteTheTabRingExclusion()
		{
			TextField field = new TextField { name = "chat-input" };
			yield return FieldOutOfTheTabRing(field);

			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			UITKChat chat = host.AddComponent<UITKChat>();
			FieldInfo input = typeof(UITKChat).GetField("inputField", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodInfo leaf = typeof(UITKChat).GetMethod("FocusLeaf", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(input, "UITKChat keeps its input field");
			LogAssert.IsNotNull(leaf, "UITKChat focuses the field through its inner element");
			input.SetValue(chat, field);

			Focusable target = (Focusable)leaf.Invoke(chat, null);
			LogAssert.IsTrue(target != null && target != field, "the target is the field's inner element, not the field");
			target.Focus();

			LogAssert.IsTrue(FocusIsIn(field), "Enter must leave the chat field focused and ready to type into");
		}
	}
}
