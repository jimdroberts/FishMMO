using System.Collections;
using System.Reflection;
using UnityEngine.TestTools;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The shared colour picker, driven the way the Options panel drives it: through
	/// <see cref="UITKColorPicker.Open"/> on a panel that starts hidden.
	/// </summary>
	/// <remarks>
	/// Reported as "the picker shows no colour, no hex and every slider at zero". Every one of
	/// those is the UXML default, which means the writes went somewhere other than the tree on
	/// screen, or never happened at all. These tests mount the real UXML on a real UIDocument and
	/// check the tree the player would actually see.
	/// </remarks>
	[TestFixture]
	public class ColorPickerTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/Shared/ColorPicker/UIColorPicker.uxml";

		private GameObject host;
		private GameObject otherHost;
		private UITKColorPicker picker;
		private UIDocument document;
		private PanelSettings sharedSettings;

		[SetUp]
		public void SetUp()
		{
			PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(settings, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the picker UXML must exist at {UxmlPath}");

			sharedSettings = Object.Instantiate(settings);

			host = new GameObject("ColorPickerTest");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = sharedSettings;
			document.visualTreeAsset = uxml;

			// A start-hidden panel: the document is off until something shows it.
			document.enabled = false;

			picker = host.AddComponent<UITKColorPicker>();
			picker.Document = document;

			// As serialized on the ClientPreboot scene object.
			picker.StartOpen = false;
			picker.IsAlwaysOpen = false;
			picker.CloseOnQuitToMenu = true;
			picker.ReleasesCursor = true;
			picker.CloseOnEscape = true;
		}

		[TearDown]
		public void TearDown()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
			if (otherHost != null)
			{
				Object.DestroyImmediate(otherHost);
			}
		}

		/// <summary>Runs the scene lifecycle the way a loaded prefab would: Awake, which registers and hides.</summary>
		private void AwakeLikeTheScene()
		{
			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake, "UITKControl must still declare Awake");
			awake.Invoke(picker, null);
		}

		/// <summary>A button on a second document drawn through the same panel, like the Options panel is.</summary>
		private Button ButtonOnASiblingDocument()
		{
			otherHost = new GameObject("OptionsStandIn");
			UIDocument other = otherHost.AddComponent<UIDocument>();
			other.panelSettings = sharedSettings;
			Button button = new Button { text = "Change" };
			other.rootVisualElement.Add(button);
			return button;
		}

		private static void Click(Button button)
		{
			using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
			{
				evt.target = button;
				button.SendEvent(evt);
			}
		}

		private VisualElement Live => document.rootVisualElement;

		private T Cached<T>(string field) where T : class
		{
			FieldInfo info = typeof(UITKColorPicker).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(info, $"UITKColorPicker must still declare {field}");
			return info.GetValue(picker) as T;
		}

		private void AssertLiveTreeShows(Color colour, string because)
		{
			Slider r = Live.Q<Slider>("r-slider");
			Slider g = Live.Q<Slider>("g-slider");
			Slider b = Live.Q<Slider>("b-slider");
			Slider a = Live.Q<Slider>("a-slider");
			IntegerField rInput = Live.Q<IntegerField>("r-input");
			TextField hex = Live.Q<TextField>("hex-input");
			VisualElement swatch = Live.Q<VisualElement>("color-current");
			VisualElement square = Live.Q<VisualElement>("hsv-texture");
			VisualElement rBackground = Live.Q<VisualElement>("r-background");
			VisualElement vBackground = Live.Q<VisualElement>("v-background");

			LogAssert.IsNotNull(r, "the live tree carries the red slider");
			LogAssert.AreEqual(Mathf.RoundToInt(colour.r * 255f), Mathf.RoundToInt(r.value), $"red slider — {because}");
			LogAssert.AreEqual(Mathf.RoundToInt(colour.g * 255f), Mathf.RoundToInt(g.value), $"green slider — {because}");
			LogAssert.AreEqual(Mathf.RoundToInt(colour.b * 255f), Mathf.RoundToInt(b.value), $"blue slider — {because}");
			LogAssert.AreEqual(Mathf.RoundToInt(colour.a * 255f), Mathf.RoundToInt(a.value), $"alpha slider — {because}");
			LogAssert.AreEqual(Mathf.RoundToInt(colour.r * 255f), rInput.value, $"red input — {because}");
			LogAssert.AreEqual(colour.ToHex(), hex.value, $"hex field — {because}");

			LogAssert.IsTrue(swatch.style.backgroundColor.keyword == StyleKeyword.Undefined, $"swatch has an inline colour — {because}");
			LogAssert.AreEqual(colour, swatch.style.backgroundColor.value, $"swatch colour — {because}");
			LogAssert.IsNotNull(square.style.backgroundImage.value.texture, $"the saturation/value square is bound — {because}");
			LogAssert.IsNotNull(rBackground.style.backgroundImage.value.texture, $"the red strip is bound — {because}");
			LogAssert.IsNotNull(vBackground.style.backgroundImage.value.texture, $"the value strip is bound — {because}");

			// And the references the picker will drive from later are the live ones, not a dead tree.
			LogAssert.AreSame(r, Cached<Slider>("rSlider"), $"cached red slider is the live one — {because}");
			LogAssert.AreSame(hex, Cached<TextField>("hexInput"), $"cached hex field is the live one — {because}");
			LogAssert.AreSame(square, Cached<VisualElement>("hsvTexture"), $"cached square is the live one — {because}");
		}

		[Test]
		public void Open_OnAStartHiddenPicker_SeedsTheTreeThePlayerSees()
		{
			Color colour = new Color(0.2f, 0.4f, 0.6f, 1.0f);
			int reports = 0;
			picker.Open(colour, c => reports++);

			LogAssert.IsTrue(picker.Visible, "the picker is up");
			AssertLiveTreeShows(colour, "first open");
			LogAssert.AreEqual(0, reports, "seeding the picker is not a change the caller made");
		}

		[Test]
		public void Open_AfterAHide_SeedsTheRebuiltTree()
		{
			picker.Open(Color.red, c => { });
			picker.Hide();
			LogAssert.IsFalse(picker.Visible, "hidden between opens");

			Color colour = new Color(0.1f, 0.9f, 0.3f, 0.5f);
			int reports = 0;
			picker.Open(colour, c => reports++);

			AssertLiveTreeShows(colour, "second open after a hide");
			LogAssert.AreEqual(0, reports, "re-seeding is not a change either");
		}

		[Test]
		public void Open_WhileAlreadyVisible_ReseedsInPlace()
		{
			picker.Open(Color.red, c => { });

			Color colour = new Color(0.0f, 0.0f, 1.0f, 1.0f);
			picker.Open(colour, c => { });

			AssertLiveTreeShows(colour, "re-open while visible");
		}

		[Test]
		public void DraggingARgbSlider_ReportsTheColourToTheCaller()
		{
			Color reported = Color.clear;
			int reports = 0;
			picker.Open(Color.black, c => { reported = c; reports++; });

			Live.Q<Slider>("r-slider").value = 255f;

			LogAssert.AreEqual(1, reports, "one drag step, one report");
			LogAssert.AreEqual(255, Mathf.RoundToInt(reported.r * 255f), "the red channel the player set");
			LogAssert.AreEqual("FF0000FF", Live.Q<TextField>("hex-input").value, "and the hex follows");
		}

		[UnityTest]
		public IEnumerator Open_FromAClickOnAnotherPanel_SeedsTheTreeThePlayerSees()
		{
			AwakeLikeTheScene();
			LogAssert.IsFalse(picker.Visible, "starts hidden");
			LogAssert.IsFalse(document.enabled, "and its document is off");

			Button change = ButtonOnASiblingDocument();
			Color colour = new Color(0.2f, 0.4f, 0.6f, 1.0f);
			int reports = 0;
			change.clicked += () => picker.Open(colour, c => reports++);

			Click(change);

			LogAssert.IsTrue(picker.Visible, "the picker is up");
			AssertLiveTreeShows(colour, "opened from a click, same frame");

			for (int i = 0; i < 3; ++i)
			{
				yield return null;
			}

			AssertLiveTreeShows(colour, "opened from a click, three frames later");
			LogAssert.AreEqual(0, reports, "seeding from a click is not a change the caller made");
		}

		[UnityTest]
		public IEnumerator Open_FromAClick_ThenHide_ThenOpenFromAClickAgain()
		{
			AwakeLikeTheScene();
			Button change = ButtonOnASiblingDocument();
			Color first = Color.red;
			Color second = new Color(0.1f, 0.9f, 0.3f, 0.5f);
			Color next = first;
			int reports = 0;
			change.clicked += () => picker.Open(next, c => reports++);

			Click(change);
			yield return null;
			picker.Hide();
			yield return null;

			next = second;
			reports = 0;
			Click(change);
			AssertLiveTreeShows(second, "second open from a click, same frame");
			for (int i = 0; i < 3; ++i)
			{
				yield return null;
			}
			AssertLiveTreeShows(second, "second open from a click, three frames later");
			LogAssert.AreEqual(0, reports, "re-seeding from a click is not a change either");
		}

		[UnityTest]
		public IEnumerator DraggingInsideADispatch_ReportsOnceAndKeepsTheExactChannel()
		{
			AwakeLikeTheScene();
			Button change = ButtonOnASiblingDocument();
			change.clicked += () => picker.Open(new Color(0.5f, 0.25f, 0.125f, 1.0f), c => { });
			Click(change);
			yield return null;

			Color reported = Color.clear;
			int reports = 0;
			picker.OnColorChanged = c => { reported = c; reports++; };

			/* A drag step arrives as a pointer event, which UI Toolkit dispatches with the
			 * dispatcher gate closed, exactly like the button click above. */
			Button proxy = new Button();
			Live.Add(proxy);
			proxy.clicked += () => Live.Q<Slider>("g-slider").value = 200f;
			Click(proxy);
			yield return null;

			LogAssert.AreEqual(1, reports, "one drag step, one report");
			LogAssert.AreEqual(200, Mathf.RoundToInt(reported.g * 255f), "the green channel the player set survives the HSV round trip");
			LogAssert.AreEqual(200, Mathf.RoundToInt(Live.Q<Slider>("g-slider").value), "and the slider stays where it was put");
		}
	}
}
