using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using UnityEditor;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The registration panel, mounted on a real <see cref="UIDocument"/> at the 1200x800 reference
	/// resolution and measured the way it lays out.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The form became two columns of fourteen controls. Every defect this project's UI Toolkit
	/// panels keep producing is invisible to a source read and silent at runtime: a text field whose
	/// glyph box resolves to zero height draws nothing (issues #263/#277), and a row wider than its
	/// <c>.fish-panel</c> is clipped without a warning. These pin both against the resolved tree.
	/// </para>
	/// <para>
	/// The panel is laid out at a constant 1 point per pixel on a 1200x800 target, which is exactly
	/// the point space the game's match-width <c>ScaleWithScreenSize</c> produces on any 3:2 screen;
	/// the 16:9 case is pinned by height, since match-width only ever changes the height.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class RegisterPanelLayoutTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/Login/Login/UIRegister.uxml";

		/// <summary>Point-space height of a 16:9 screen under match-width scaling (1200 x 9 / 16).</summary>
		private const float WidescreenHeight = 675f;

		/// <summary>Every text field the form has, in reading order.</summary>
		private static readonly string[] TextFieldNames =
		{
			"register-username", "register-email", "register-password",
			"register-phone", "register-country", "register-realname",
			"register-address", "register-referral", "register-betacode",
		};

		/// <summary>The order Tab must visit the controls: down the left column, down the right, then the buttons.</summary>
		private static readonly string[] ReadingOrder =
		{
			"register-username", "register-email", "register-password", "register-age",
			"register-verify-email", "register-verify-sms",
			"register-phone", "register-country", "register-realname", "register-address",
			"register-referral", "register-betacode",
			"register-submit-btn", "register-quit-btn",
		};

		private GameObject host;
		private UIDocument document;
		private UITKRegister register;
		private PanelSettings settings;
		private RenderTexture target;

		[SetUp]
		public void SetUp()
		{
			PanelSettings authored = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(authored, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the register UXML must exist at {UxmlPath}");

			target = new RenderTexture(1200, 800, 24, RenderTextureFormat.ARGB32);
			target.Create();

			settings = Object.Instantiate(authored);
			settings.targetTexture = target;
			settings.scaleMode = PanelScaleMode.ConstantPixelSize;
			settings.scale = 1f;

			host = new GameObject("UIRegister");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = uxml;
			document.enabled = false;

			register = host.AddComponent<UITKRegister>();
			register.Document = document;
			register.StartOpen = false;
			register.IsAlwaysOpen = false;
			register.CloseOnQuitToMenu = true;
			register.ReleasesCursor = false;

			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake, "UITKControl must still declare Awake");
			awake.Invoke(register, null);

			register.Show();
			LogAssert.IsTrue(register.Visible, "the panel must be up before anything is asserted about its tree");
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
			if (target != null)
			{
				target.Release();
				Object.DestroyImmediate(target);
			}
		}

		private VisualElement Live => document.rootVisualElement;

		/// <summary>Yoga has not placed a freshly mounted tree; every size reads NaN until frames pass.</summary>
		private static IEnumerator Settle()
		{
			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}
		}

		[UnityTest]
		public IEnumerator EveryTextFieldHasRoomToDrawItsText()
		{
			yield return Settle();

			List<TextField> all = Live.Query<TextField>().ToList();
			LogAssert.AreEqual(TextFieldNames.Length, all.Count,
				"every text field on the form is pinned here; a new one must be added to TextFieldNames");

			foreach (string name in TextFieldNames)
			{
				TextField field = Live.Q<TextField>(name);
				LogAssert.IsNotNull(field, $"{name} is in the tree");

				VisualElement input = field.Q("unity-text-input");
				VisualElement glyph = input?.Q(className: "unity-text-element");
				LogAssert.IsNotNull(glyph, $"{name} has the element that draws its characters");

				LogAssert.IsTrue(glyph.layout.height > 0f && glyph.layout.width > 0f,
					$"{name} must have a glyph box to draw in; field {field.layout.width}x{field.layout.height}, " +
					$"input padding {input.resolvedStyle.paddingTop}/{input.resolvedStyle.paddingBottom}, " +
					$"glyph {glyph.layout.width}x{glyph.layout.height}");
			}
		}

		[UnityTest]
		public IEnumerator NothingOverflowsThePanel()
		{
			yield return Settle();

			VisualElement panel = Live.Q("register-panel");
			LogAssert.IsNotNull(panel, "register-panel is in the tree");
			Rect bounds = panel.worldBound;
			LogAssert.IsTrue(bounds.width > 0f && bounds.height > 0f, $"the panel laid out ({bounds})");

			foreach (VisualElement element in panel.Query<VisualElement>().Build())
			{
				if (element == panel || element.resolvedStyle.display == DisplayStyle.None)
				{
					continue;
				}
				Rect box = element.worldBound;
				if (box.width <= 0f && box.height <= 0f)
				{
					continue;
				}
				bool inside = box.xMin >= bounds.xMin - 0.5f && box.xMax <= bounds.xMax + 0.5f &&
					box.yMin >= bounds.yMin - 0.5f && box.yMax <= bounds.yMax + 0.5f;
				LogAssert.IsTrue(inside,
					$"'{Describe(element)}' at {box} is outside the panel at {bounds}; .fish-panel clips it silently");
			}

			VisualElement account = Live.Q("register-column-account");
			VisualElement optional = Live.Q("register-column-optional");
			LogAssert.IsTrue(account.worldBound.xMax <= optional.worldBound.xMin + 0.5f,
				$"the columns overlap: account ends at {account.worldBound.xMax}, optional starts at {optional.worldBound.xMin}");

			LogAssert.IsTrue(panel.layout.height <= WidescreenHeight,
				$"the panel is {panel.layout.height} tall and a 16:9 screen has {WidescreenHeight} points under match-width scaling");
		}

		[UnityTest]
		public IEnumerator RowsBesideEachOtherLineUp()
		{
			yield return Settle();

			string[,] pairs =
			{
				{ "register-username", "register-phone" },
				{ "register-email", "register-country" },
				{ "register-password", "register-realname" },
			};

			for (int i = 0; i < pairs.GetLength(0); ++i)
			{
				VisualElement left = Live.Q(pairs[i, 0]);
				VisualElement right = Live.Q(pairs[i, 1]);
				LogAssert.IsTrue(Mathf.Abs(left.worldBound.yMin - right.worldBound.yMin) <= 0.5f,
					$"{pairs[i, 0]} starts at y {left.worldBound.yMin} but {pairs[i, 1]} beside it starts at y {right.worldBound.yMin}");
			}
		}

		[Test]
		public void TabWalksTheFormInReadingOrder()
		{
			string[] order = Live.Query<VisualElement>().Build()
				.Where(element => element.focusable && element.tabIndex > 0 && !string.IsNullOrEmpty(element.name))
				.OrderBy(element => element.tabIndex)
				.Select(element => element.name)
				.ToArray();

			LogAssert.AreEqual(string.Join(" > ", ReadingOrder), string.Join(" > ", order),
				"Tab must follow the order the form reads in");

			/* UI Toolkit's focus ring admits every element with tabIndex >= 0, so a root left at 0 is
			 * the last Tab stop: Tab from Back went to the invisible panel root before wrapping. */
			LogAssert.IsTrue(Live.tabIndex < 0,
				$"the panel root must not be a Tab stop; its tabIndex is {Live.tabIndex}");
		}

		private static string Describe(VisualElement element)
		{
			VisualElement named = element;
			while (named != null && string.IsNullOrEmpty(named.name))
			{
				named = named.parent;
			}
			string self = string.IsNullOrEmpty(element.name) ? element.GetType().Name : element.name;
			return named == element || named == null ? self : named.name + "/" + self;
		}
	}
}
