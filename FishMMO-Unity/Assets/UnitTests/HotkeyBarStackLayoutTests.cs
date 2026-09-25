using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Stacked hotkey bars (issue #267) must clear the panels above them: every extra row lifts the
	/// resource bars, the buff strip and the cast bar by one row, and shortens the chat log from
	/// below, and nothing else moves.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Measured, not reasoned. The bottom-centre block is five separate documents positioned by
	/// fixed offsets against the first bar's top edge (see <c>UIResourceBar.uss</c>), and the lift
	/// is an inline <c>margin-bottom</c> on elements the stylesheets anchor with <c>bottom</c> — which
	/// only works if Yoga adds a margin to an absolute element's offset. That is exactly the kind of
	/// fact that reads right and renders wrong, so the real stylesheets are mounted in one panel at
	/// 1200x675, the real <c>UITKHotkeyBar.BuildBars</c> builds three rows, and the rects are read
	/// back.
	/// </para>
	/// <para>
	/// The shipped game has one bar, so the rows are built with an explicit count and the inset
	/// applied with an explicit value; both are the overloads production code calls with the
	/// game's own numbers.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class HotkeyBarStackLayoutTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string GuiRoot = "Assets/Scripts/Client/GUI/World/";
		private const float PanelWidth = 1200f;
		private const int PanelHeight = 675;

		/// <summary>The first bar's distance from the bottom edge: <c>.hotkey-bar</c>'s padding.</summary>
		private const float FirstBarBottom = 24f;

		private const int Bars = 3;

		private PanelSettings settings;
		private RenderTexture texture;
		private readonly List<GameObject> hosts = new List<GameObject>();

		private UIDocument hotbarDocument;
		private UITKHotkeyBar hotbar;
		private VisualElement resourceBar;
		private VisualElement castBar;
		private VisualElement buffStrip;
		private VisualElement chat;

		[SetUp]
		public void SetUp()
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");

			// A render target of the reference size: see WindowOverlapTests for why a hand-mounted
			// panel must be given one before any fixed offset can be measured.
			texture = new RenderTexture((int)PanelWidth, PanelHeight, 24, RenderTextureFormat.ARGB32);
			texture.Create();

			settings = Object.Instantiate(asset);
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;

			hotbarDocument = Mount("UIHotkeyBar", "HotkeyBar/UIHotkeyBar.uxml");
			hotbar = hotbarDocument.gameObject.AddComponent<UITKHotkeyBar>();

			resourceBar = Mount("UIHealthBar", "ResourceBar/UIResourceBar.uxml").rootVisualElement.Q("bar-root");
			// The panel adds its row class in OnStarting; without it the bar has no bottom offset.
			resourceBar.AddToClassList("res-bar--hp");

			castBar = Mount("UICastBar", "Ability/UICastBar.uxml").rootVisualElement.Q("castbar-root");
			buffStrip = Mount("UIBuff", "Buff/UIBuff.uxml").rootVisualElement.Q("buff-root");
			chat = Mount("UIChat", "Chat/UIChat.uxml").rootVisualElement.Q("chat-root");

			LogAssert.IsNotNull(resourceBar, "resource bar root");
			LogAssert.IsNotNull(castBar, "cast bar root");
			LogAssert.IsNotNull(buffStrip, "buff strip root");
			LogAssert.IsNotNull(chat, "chat root");
		}

		private UIDocument Mount(string name, string uxmlPath)
		{
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GuiRoot + uxmlPath);
			LogAssert.IsNotNull(uxml, $"the {name} UXML must exist at {GuiRoot + uxmlPath}");

			GameObject host = new GameObject(name);
			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = uxml;
			hosts.Add(host);
			return document;
		}

		[TearDown]
		public void TearDown()
		{
			foreach (GameObject host in hosts)
			{
				if (host != null)
				{
					Object.DestroyImmediate(host);
				}
			}
			hosts.Clear();

			if (settings != null)
			{
				Object.DestroyImmediate(settings);
			}
			if (texture != null)
			{
				texture.Release();
				Object.DestroyImmediate(texture);
			}
			settings = null;
			texture = null;
		}

		/// <summary>Builds the rows with the bar's own code, through its private seam.</summary>
		private List<VisualElement> BuildRows(int bars)
		{
			const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

			FieldInfo list = typeof(UITKHotkeyBar).GetField("list", Private);
			LogAssert.IsNotNull(list, "the bar must still keep its row container in `list`");
			list.SetValue(hotbar, hotbarDocument.rootVisualElement.Q("hotkey-list"));

			MethodInfo build = typeof(UITKHotkeyBar).GetMethod("BuildBars", Private);
			LogAssert.IsNotNull(build, "the bar must still build its rows through BuildBars(int)");
			build.Invoke(hotbar, new object[] { bars });

			FieldInfo rows = typeof(UITKHotkeyBar).GetField("rows", Private);
			LogAssert.IsNotNull(rows, "the bar must still keep its rows");
			return (List<VisualElement>)rows.GetValue(hotbar);
		}

		private static IEnumerator Settle()
		{
			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}
		}

		private static string Describe(string name, Rect rect) => $"{name} y{rect.yMin:0.#}..{rect.yMax:0.#}";

		[UnityTest]
		public IEnumerator ThreeStackedRows_LiftTheBlockAbove_ByExactlyTwoRows()
		{
			List<VisualElement> rows = BuildRows(Bars);
			LogAssert.AreEqual(Bars, rows.Count, "one row per bar");

			yield return Settle();

			// Before the lift: the single-bar layout every offset was measured against.
			Rect resourceBefore = resourceBar.worldBound;
			Rect castBefore = castBar.worldBound;
			Rect buffBefore = buffStrip.worldBound;
			Rect chatBefore = chat.worldBound;
			LogAssert.IsTrue(resourceBefore.height > 1f && chatBefore.height > 1f,
				$"the panels must be laid out before anything below means anything ({resourceBefore}, {chatBefore})");

			float inset = (Bars - 1) * UITKHudLayout.HotbarRowPitch;
			UITKHudLayout.ApplyStackInset(resourceBar, inset);
			UITKHudLayout.ApplyStackInset(castBar, inset);
			UITKHudLayout.ApplyStackInset(buffStrip, inset);
			UITKHudLayout.ApplyChatInset(chat, placed: false, inset);

			yield return Settle();

			// The rows: the first where the single strip always was, each next one pitch above it.
			Rect first = rows[0].worldBound;
			LogAssert.AreEqual(PanelHeight - FirstBarBottom, Mathf.Round(first.yMax),
				$"the first bar keeps the strip's place ({Describe("row 1", first)})");
			for (int bar = 1; bar < Bars; ++bar)
			{
				float pitch = rows[bar - 1].worldBound.yMax - rows[bar].worldBound.yMax;
				LogAssert.AreEqual(UITKHudLayout.HotbarRowPitch, Mathf.Round(pitch),
					$"row {bar + 1} sits one pitch above row {bar}");
			}
			Rect top = rows[Bars - 1].worldBound;

			// Everything above moved up by exactly the extra rows, and kept its clearance.
			foreach ((string name, Rect before, VisualElement element) in new[]
			{
				("resource bar", resourceBefore, resourceBar),
				("cast bar", castBefore, castBar),
				("buff strip", buffBefore, buffStrip),
			})
			{
				Rect after = element.worldBound;
				LogAssert.AreEqual(inset, Mathf.Round(before.yMax - after.yMax),
					$"the {name} rises by the extra rows ({Describe("before", before)}, {Describe("after", after)})");
				LogAssert.AreEqual(Mathf.Round(first.yMin - before.yMax), Mathf.Round(top.yMin - after.yMax),
					$"the {name}'s gap above the bar is the gap it had above one row");

				foreach (VisualElement row in rows)
				{
					LogAssert.IsFalse(row.worldBound.Overlaps(after),
						$"{Describe(name, after)} overlaps {Describe("a hotbar row", row.worldBound)}");
				}
			}

			// The chat log gives ground from below and keeps its top edge.
			Rect chatAfter = chat.worldBound;
			LogAssert.AreEqual(Mathf.Round(chatBefore.yMin), Mathf.Round(chatAfter.yMin),
				"the chat log's top edge, which the toast stack and party frame are measured against, does not move");
			LogAssert.AreEqual(inset, Mathf.Round(chatBefore.yMax - chatAfter.yMax), "its lower edge rises by the extra rows");
			foreach (VisualElement row in rows)
			{
				LogAssert.IsFalse(row.worldBound.Overlaps(chatAfter),
					$"{Describe("chat", chatAfter)} overlaps {Describe("a hotbar row", row.worldBound)}");
			}
		}

		[UnityTest]
		public IEnumerator TheInset_IsUndoneCompletely()
		{
			BuildRows(1);
			yield return Settle();
			Rect before = resourceBar.worldBound;

			UITKHudLayout.ApplyStackInset(resourceBar, UITKHudLayout.HotbarRowPitch);
			yield return Settle();
			UITKHudLayout.ApplyStackInset(resourceBar, 0f);
			yield return Settle();

			LogAssert.AreEqual(Mathf.Round(before.yMax), Mathf.Round(resourceBar.worldBound.yMax),
				"switching back to one visible row (Paged) returns the bar to its stylesheet position");
			LogAssert.AreEqual(StyleKeyword.Null, resourceBar.style.marginBottom.keyword,
				"and leaves no inline style behind");
		}
	}
}
