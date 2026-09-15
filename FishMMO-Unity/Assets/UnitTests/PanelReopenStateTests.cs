using System;
using System.Collections;
using System.IO;
using System.Text;
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
	/// Panels keep their visual tree across hide and show, so what used to be reset by a fresh
	/// clone on every open has to be reset, or refreshed, on purpose.
	/// </summary>
	/// <remarks>
	/// <para>Companion to <see cref="PanelHideKeepsTreeTests"/>, which pins that the tree survives.
	/// Every pin here is a consequence of that: state the clone used to wipe for free — a typed
	/// password, a scroll offset, a stale size measured by a menu clamp — and work that used to
	/// wait for the first open and now runs at scene load while the panel is hidden.</para>
	/// <para>The clamp is pinned by behaviour. The rest are source scans in the style of
	/// <see cref="SlotPanelSharingTests"/>: each one names a single line whose removal would put a
	/// reopen defect back without failing any behavioural test.</para>
	/// </remarks>
	[TestFixture]
	public class PanelReopenStateTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/Toast/UIToast.uxml";
		private const string Gui = "Assets/Scripts/Client/GUI/";

		private PanelSettings settings;
		private RenderTexture texture;
		private GameObject host;

		[TearDown]
		public void TearDown()
		{
			if (host != null)
			{
				UnityEngine.Object.DestroyImmediate(host);
			}
			if (settings != null)
			{
				UnityEngine.Object.DestroyImmediate(settings);
			}
			if (texture != null)
			{
				texture.Release();
				UnityEngine.Object.DestroyImmediate(texture);
			}
			host = null;
			settings = null;
			texture = null;
		}

		/// <summary>
		/// A reused menu is measurable at once — at the size its previous entries gave it. The clamp
		/// must follow the size the element settles at, or a menu opened near an edge hangs off it.
		/// </summary>
		[UnityTest]
		public IEnumerator AMenuThatGrowsAfterPlacementIsClampedAgainstItsSettledSize()
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"markup must exist at {UxmlPath}");

			texture = new RenderTexture(1200, 800, 24, RenderTextureFormat.ARGB32);
			texture.Create();
			settings = UnityEngine.Object.Instantiate(asset);
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;

			host = new GameObject("PanelReopenStateTests");
			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = uxml;

			VisualElement root = document.rootVisualElement;
			VisualElement menu = new VisualElement { name = "probe-menu" };
			menu.style.position = Position.Absolute;
			menu.style.width = 100;
			menu.style.height = 50;
			root.Add(menu);

			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}

			float containerWidth = root.contentRect.width;
			LogAssert.IsTrue(containerWidth > 400f, $"the panel must be laid out; its content resolved to {containerWidth} wide");
			LogAssert.IsTrue(Mathf.Abs(menu.resolvedStyle.width - 100f) < 0.5f, $"the menu is laid out at its first size; it resolved to {menu.resolvedStyle.width}");

			// Exactly the reopen: new entries make it wider in the same frame it is placed near the edge.
			menu.style.width = 400;
			Vector2 anchor = new Vector2(containerWidth - 120f, 10f);
			UITKScreenSpace.PlaceClamped(root, menu, anchor);

			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}

			Rect settled = menu.layout;
			LogAssert.IsTrue(Mathf.Abs(settled.width - 400f) < 0.5f, $"the menu settled at its new size; it resolved to {settled.width}");
			LogAssert.IsTrue(settled.xMax <= containerWidth + 0.5f,
				$"a menu that grew after it was placed is clamped inside the panel against the size it settled at (right edge {settled.xMax}, panel {containerWidth})");
		}

		[Test]
		public void TheToastPanelUsesTheBasePerFrameHook()
		{
			string code = CodeOnly(Read(Gui + "World/Toast/UITKToast.cs"));
			LogAssert.IsFalse(code.Contains("void Update()"), "UITKToast must not declare Update: Unity binds only the most-derived one, which silently disabled UITKControl.Update and its root visibility check");
			LogAssert.IsTrue(code.Contains("protected override void OnTick()"), "the toast ages its entries from the base per-frame hook");
		}

		[Test]
		public void TheMailPanelWiresItsButtonsOnce()
		{
			string code = CodeOnly(Read(Gui + "World/Mail/UITKMail.cs"));
			LogAssert.AreEqual(1, CountOccurrences(code, "WireControls();"), "WireControls is called from exactly one place; every call adds another handler to the same buttons");
			LogAssert.IsTrue(MethodBody(code, "public override void OnStarting()").Contains("WireControls();"), "and that place is OnStarting, which runs once per tree");
		}

		[Test]
		public void ReopeningThePartyPanelRefreshesRowsInPlace()
		{
			string code = CodeOnly(Read(Gui + "World/Party/UITKParty.cs"));
			string show = MethodBody(code, "protected override void OnAfterShow()");
			LogAssert.IsFalse(show.Contains("RebuildRosterView"), "OnAfterShow must not rebuild the roster: it throws away every row and buff icon on every open");
			LogAssert.IsTrue(show.Contains("ApplyModelToRow(model)"), "it re-reads each row from its model instead");
		}

		[Test]
		public void AHiddenReconnectDisplayDoesNotClaimTheCursor()
		{
			string code = CodeOnly(Read(Gui + "Login/UITKReconnectDisplay.cs"));
			string body = MethodBody(code, "protected override void OnAfterStarting()");
			LogAssert.IsTrue(body.Contains("if (Visible)"), "OnAfterStarting runs at scene load for a hidden panel; ApplyState claims the cursor and must wait for the panel to be shown");
		}

		[Test]
		public void TheDungeonFinderPublicToggleIsResetForEveryEntrance()
		{
			string code = CodeOnly(Read(Gui + "World/DungeonFinder/UITKDungeonFinder.cs"));
			LogAssert.AreEqual(2, CountOccurrences(code, "publicToggle?.SetValueWithoutNotify(false);"), "the toggle persists with the tree, so both a fresh open and a cleared dungeon put it back to private");
			LogAssert.IsTrue(MethodBody(code, "private void ClearDungeon()").Contains("publicToggle?.SetValueWithoutNotify(false);"), "ClearDungeon resets it");
		}

		[Test]
		public void TheOptionsPanelReadsTheDisplayModeOnEveryOpen()
		{
			string code = CodeOnly(Read(Gui + "World/Options/UITKOptions.cs"));
			string show = MethodBody(code, "protected override void OnAfterShow()");
			LogAssert.IsTrue(show.Contains("InitializeDisplaySettings();"), "the display mode is re-read on open; read only at scene load it captured the launcher's window and Revert restored it");
			LogAssert.IsTrue(show.Contains("if (!displayRevertArmed)"), "except while a revert is armed, which must keep the mode it will restore");
			LogAssert.IsFalse(MethodBody(code, "public override void OnStarting()").Contains("InitializeControlsSection();"), "the binding rows are built by OnAfterShow; building them at scene load was thrown away unseen");
		}

		[Test]
		public void ATooltipClosesWhenItsOwnersPanelIsHidden()
		{
			string code = CodeOnly(Read(Gui + "Shared/Tooltip/UITKTooltip.cs"));
			LogAssert.IsTrue(MethodBody(code, "private bool IsOwnerAlive()").Contains("Visibility.Hidden"), "a hidden panel keeps its tree and its panel, so only its inherited visibility says the owner is gone");
		}

		[Test]
		public void LoginFormsDropATypedPasswordWhenHidden()
		{
			foreach (string path in new[] { Gui + "Login/Login/UITKLogin.cs", Gui + "Login/Login/UITKRegister.cs" })
			{
				string body = MethodBody(CodeOnly(Read(path)), "public override void Hide(bool overrideIsAlwaysOpen)");
				LogAssert.IsTrue(body.Contains("password?.SetValueWithoutNotify(string.Empty);"), $"{Path.GetFileName(path)} clears the password on hide; the field persists with the tree");
			}
		}

		// ── helpers ───────────────────────────────────────────────────────────

		private static string Read(string path)
		{
			LogAssert.IsTrue(File.Exists(path), path + " exists");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static int CountOccurrences(string text, string needle)
		{
			int count = 0;
			for (int i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
			{
				++count;
			}
			return count;
		}

		/// <summary>Brace-matches the body following the first occurrence of <paramref name="signature"/>.</summary>
		private static string MethodBody(string source, string signature)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "found: " + signature);
			int open = source.IndexOf('{', start);
			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{') ++depth;
				else if (source[i] == '}' && --depth == 0)
				{
					return source.Substring(open, i - open + 1);
				}
			}
			LogAssert.Fail("unterminated body for " + signature);
			return string.Empty;
		}

		/// <summary>Strips block comments and comment lines so prose cannot trip a code scan.</summary>
		private static string CodeOnly(string source)
		{
			while (true)
			{
				int open = source.IndexOf("/*", StringComparison.Ordinal);
				if (open < 0)
				{
					break;
				}

				int close = source.IndexOf("*/", open + 2, StringComparison.Ordinal);
				source = close < 0 ? source.Substring(0, open) : source.Remove(open, close - open + 2);
			}

			StringBuilder kept = new StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}

				kept.Append(line).Append('\n');
			}

			return kept.ToString();
		}
	}
}
