using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Several hotkey bars (issue #267): which bar a key lands on, how a press pairs with its
	/// release, the run-time actions that carry the bar modifiers, and the layout numbers the HUD
	/// panels above the bar are moved by.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The game ships ONE bar, so every multi-bar rule here is driven through the overloads that
	/// take an explicit bar count; a test that read the constant would test nothing but the
	/// single-bar game.
	/// </para>
	/// <para>
	/// The modifier scheme is Jim's rule from the issue: modifier combinations are the default for
	/// the extra bars only where they overlap nothing already bound. Shift is Sprint, so it is not
	/// one of them; <see cref="TheDefaultModifiers_OverlapNoAuthoredBinding"/> is the pin.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class HotkeyBarLayoutTests
	{
		private const string InputAssetPath = "Assets/Prefabs/Client/Input/PlayerControls.inputactions";
		private const string HotkeyBarUssPath = "Assets/Scripts/Client/GUI/World/HotkeyBar/UIHotkeyBar.uss";
		private const string ChatUssPath = "Assets/Scripts/Client/GUI/World/Chat/UIChat.uss";

		// ── Which bar a press lands on ──────────────────────────────

		[Test]
		public void AHeldModifier_PicksItsBar_InEitherLayout()
		{
			LogAssert.AreEqual(1, HotkeyBarLayout.ResolvePressBar(1, paged: false, restingPage: 0, barCount: 3), "Ctrl + key, stacked");
			LogAssert.AreEqual(2, HotkeyBarLayout.ResolvePressBar(2, paged: false, restingPage: 0, barCount: 3), "Alt + key, stacked");
			LogAssert.AreEqual(1, HotkeyBarLayout.ResolvePressBar(1, paged: true, restingPage: 2, barCount: 3),
				"a held modifier beats the page on screen: the hands do not have to know the layout");
		}

		[Test]
		public void NoModifier_IsTheFirstBarStacked_AndTheRestingPagePaged()
		{
			LogAssert.AreEqual(0, HotkeyBarLayout.ResolvePressBar(-1, paged: false, restingPage: 2, barCount: 3),
				"stacked: every bar is on screen and the unmodified keys are the first bar's");
			LogAssert.AreEqual(2, HotkeyBarLayout.ResolvePressBar(-1, paged: true, restingPage: 2, barCount: 3),
				"paged: the unmodified keys are the bar the player is looking at");
		}

		[Test]
		public void AModifierOrPageThatDoesNotExist_FallsBackToTheFirstBar()
		{
			LogAssert.AreEqual(0, HotkeyBarLayout.ResolvePressBar(5, paged: false, restingPage: 0, barCount: 3), "no fifth bar");
			LogAssert.AreEqual(0, HotkeyBarLayout.ResolvePressBar(-1, paged: true, restingPage: 7, barCount: 3), "a stale saved page");
			LogAssert.AreEqual(0, HotkeyBarLayout.ResolvePressBar(1, paged: true, restingPage: 1, barCount: 1),
				"a single-bar game has no modifier and no pages");
		}

		[Test]
		public void APagedBar_ShowsTheBarTheNextPressWillFire()
		{
			/* The whole table, not a sample: a paged bar that showed one bar while the next press
			 * fired another would be the worst kind of wrong — the player acting on what they see. */
			for (int held = -1; held < 3; ++held)
			{
				for (int resting = 0; resting < 3; ++resting)
				{
					LogAssert.AreEqual(
						HotkeyBarLayout.ResolvePressBar(held, paged: true, resting, barCount: 3),
						HotkeyBarLayout.ResolveShownPage(held, resting, barCount: 3),
						$"held {held}, resting {resting}");
				}
			}
			LogAssert.AreEqual(1, HotkeyBarLayout.ResolveShownPage(1, restingPage: 2, barCount: 3),
				"holding Ctrl shows the second bar over the resting third");
		}

		[Test]
		public void Paging_WrapsAtBothEnds()
		{
			LogAssert.AreEqual(1, HotkeyBarLayout.StepPage(0, +1, 3), "up from the first");
			LogAssert.AreEqual(0, HotkeyBarLayout.StepPage(2, +1, 3), "up from the last wraps to the first");
			LogAssert.AreEqual(2, HotkeyBarLayout.StepPage(0, -1, 3), "down from the first wraps to the last");
			LogAssert.AreEqual(0, HotkeyBarLayout.StepPage(0, +1, 1), "one bar has one page");
		}

		[Test]
		public void SlotIndices_AreBarMajor_AndRoundTrip()
		{
			int perBar = HotkeyBarLayout.SlotsPerBar;
			LogAssert.AreEqual(perBar + 2, HotkeyBarLayout.SlotIndex(1, 2), "the second bar starts after the first");
			for (int slot = 0; slot < perBar * 3; ++slot)
			{
				LogAssert.AreEqual(slot, HotkeyBarLayout.SlotIndex(HotkeyBarLayout.BarOf(slot), HotkeyBarLayout.PositionOf(slot)),
					$"slot {slot}");
			}
			LogAssert.AreEqual(Constants.Configuration.HotkeyBarCount * Constants.Configuration.HotkeySlotsPerBar,
				Constants.Configuration.MaximumPlayerHotkeys, "the server's slot count is every slot on every bar");
		}

		// ── A press and its release belong to one bar ───────────────

		[Test]
		public void AReleaseGoesToTheBarThePressStarted_EvenAfterTheModifierIsLetGo()
		{
			HotkeyPressRouter router = new HotkeyPressRouter(12);

			router.Step(2, pressed: true, targetBar: 1, out int pressed, out int released);
			LogAssert.AreEqual(1, pressed, "Ctrl+1 presses the second bar");
			LogAssert.AreEqual(-1, released, "nothing released on the press");

			// Ctrl comes up first; the key is still held, so nothing moves.
			router.Step(2, pressed: true, targetBar: 0, out pressed, out released);
			LogAssert.AreEqual(-1, pressed, "a held key does not re-press on the first bar");
			LogAssert.AreEqual(-1, released, "...nor release the second");

			router.Step(2, pressed: false, targetBar: 0, out pressed, out released);
			LogAssert.AreEqual(1, released, "the release reaches the slot that started, not the first bar's");
		}

		[Test]
		public void AModifierPressedMidHold_DoesNotMoveThePress()
		{
			HotkeyPressRouter router = new HotkeyPressRouter(12);

			router.Step(0, pressed: true, targetBar: 0, out int pressed, out _);
			LogAssert.AreEqual(0, pressed, "LMB alone presses the first bar");

			router.Step(0, pressed: true, targetBar: 1, out pressed, out int released);
			LogAssert.AreEqual(-1, pressed, "holding Ctrl down afterwards starts nothing on the second bar");
			LogAssert.AreEqual(-1, released, "...and does not cut the first bar's press short");

			router.Step(0, pressed: false, targetBar: 1, out _, out released);
			LogAssert.AreEqual(0, released, "the release is the first bar's");
		}

		[Test]
		public void Reset_ForgetsPressesWithoutReleasing()
		{
			HotkeyPressRouter router = new HotkeyPressRouter(12);
			router.Step(3, pressed: true, targetBar: 2, out _, out _);

			router.Reset();

			router.Step(3, pressed: false, targetBar: 0, out int pressed, out int released);
			LogAssert.AreEqual(-1, released, "a reset press has nothing left to release");
			LogAssert.AreEqual(-1, pressed, "and a key that is up starts nothing");

			router.Step(99, pressed: true, targetBar: 0, out pressed, out released);
			LogAssert.AreEqual(-1, pressed, "a position past the bar's width is ignored");
		}

		// ── The run-time bar actions ────────────────────────────────

		private static InputActionMap NewPlayerMap(out InputActionAsset asset)
		{
			asset = UnityEngine.ScriptableObject.CreateInstance<InputActionAsset>();
			return asset.AddActionMap("Player");
		}

		[Test]
		public void ExtraBars_GetOneModifierEach_CtrlThenAltThenUnbound()
		{
			InputActionMap map = NewPlayerMap(out InputActionAsset asset);
			try
			{
				HotkeyKeyMap.AddBarActions(map, 4);

				LogAssert.IsNull(map.FindAction(HotkeyKeyMap.ModifierActionName(0)), "the first bar has no modifier");
				LogAssert.AreEqual("<Keyboard>/ctrl", map.FindAction("Hotbar2Modifier").bindings[0].effectivePath, "second bar");
				LogAssert.AreEqual("<Keyboard>/alt", map.FindAction("Hotbar3Modifier").bindings[0].effectivePath, "third bar");

				InputAction fourth = map.FindAction("Hotbar4Modifier");
				LogAssert.IsNotNull(fourth, "a fourth bar still gets a modifier action...");
				LogAssert.AreEqual(1, fourth.bindings.Count, "...with a binding the Key Bindings tab can list as a row...");
				LogAssert.IsTrue(string.IsNullOrEmpty(fourth.bindings[0].effectivePath), "...that starts unbound");

				LogAssert.IsNotNull(map.FindAction(HotkeyKeyMap.PageUpActionName), "page up");
				LogAssert.IsNotNull(map.FindAction(HotkeyKeyMap.PageDownActionName), "page down");

				// Idempotent: a second call must neither throw on duplicate names nor add rows.
				int actions = map.actions.Count;
				HotkeyKeyMap.AddBarActions(map, 4);
				LogAssert.AreEqual(actions, map.actions.Count, "adding twice adds nothing");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(asset);
			}
		}

		[Test]
		public void ASingleBarGame_AddsNoActions()
		{
			InputActionMap map = NewPlayerMap(out InputActionAsset asset);
			try
			{
				HotkeyKeyMap.AddBarActions(map, 1);
				LogAssert.AreEqual(0, map.actions.Count, "one bar: nothing to hold and no page to turn");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(asset);
			}
		}

		/// <summary>
		/// A rebound modifier is still rebound at the next launch.
		/// </summary>
		/// <remarks>
		/// Saved overrides are matched to bindings by binding id alone, and a binding added at run
		/// time gets a new random id every launch unless it is given one — so without the stable ids
		/// the saved override finds nothing, the Input System logs "Could not override binding", and
		/// the player's key silently reverts. Two assets stand in for two launches.
		/// </remarks>
		[Test]
		public void ARebindOfAnExtraBarModifier_SurvivesARelaunch()
		{
			InputActionMap first = NewPlayerMap(out InputActionAsset firstAsset);
			InputActionMap second = NewPlayerMap(out InputActionAsset secondAsset);
			try
			{
				HotkeyKeyMap.AddBarActions(first, 4);
				first.FindAction("Hotbar4Modifier").ApplyBindingOverride(0, "<Keyboard>/capsLock");
				string saved = firstAsset.SaveBindingOverridesAsJson();

				HotkeyKeyMap.AddBarActions(second, 4);
				secondAsset.LoadBindingOverridesFromJson(saved);

				LogAssert.AreEqual("<Keyboard>/capsLock", second.FindAction("Hotbar4Modifier").bindings[0].effectivePath,
					"the override must find the same binding in the next launch's actions");
				LogAssert.AreEqual(first.FindAction("Hotbar2Modifier").bindings[0].id, second.FindAction("Hotbar2Modifier").bindings[0].id,
					"binding ids are derived from the action's name, not generated");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(firstAsset);
				UnityEngine.Object.DestroyImmediate(secondAsset);
			}
		}

		[Test]
		public void TheDefaultModifiers_OverlapNoAuthoredBinding()
		{
			string json = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), InputAssetPath));
			InputActionAsset authored = InputActionAsset.FromJson(json);
			try
			{
				InputActionMap player = authored.FindActionMap("Player", throwIfNotFound: true);
				foreach (InputBinding binding in player.bindings)
				{
					string path = binding.path ?? string.Empty;
					foreach (string modifier in new[] { "ctrl", "alt", "pageUp", "pageDown" })
					{
						LogAssert.IsFalse(path.IndexOf(modifier, StringComparison.OrdinalIgnoreCase) >= 0,
							$"'{binding.action}' is bound to {path}; the extra bars' default '{modifier}' would overlap it");
					}
				}

				/* The reason Shift is NOT a default: it is Sprint. If Sprint ever moves off Shift this
				 * fails, and Shift can be reconsidered for a fourth bar. */
				bool sprintIsShift = false;
				foreach (InputBinding binding in player.FindAction("Sprint", throwIfNotFound: true).bindings)
				{
					sprintIsShift |= (binding.path ?? string.Empty).IndexOf("shift", StringComparison.OrdinalIgnoreCase) >= 0;
				}
				LogAssert.IsTrue(sprintIsShift, "Shift is left out of the bar modifiers only because Sprint holds it");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(authored);
			}
		}

		[Test]
		public void TheCreatedActions_HavePlayerFacingCaptions()
		{
			LogAssert.IsTrue(HotkeyKeyMap.TryGetDisplayName("Hotbar2Modifier", out string caption), "second bar's modifier");
			LogAssert.AreEqual("Hotbar 2 Modifier", caption, "second bar's modifier");
			LogAssert.IsTrue(HotkeyKeyMap.TryGetDisplayName("Hotbar12Modifier", out caption), "two-digit bars");
			LogAssert.AreEqual("Hotbar 12 Modifier", caption, "two-digit bars");
			LogAssert.IsTrue(HotkeyKeyMap.TryGetDisplayName(HotkeyKeyMap.PageUpActionName, out caption), "page up");
			LogAssert.AreEqual("Hotbar Page Up", caption, "page up");

			LogAssert.IsFalse(HotkeyKeyMap.TryGetDisplayName("Hotbar1Modifier", out _), "the first bar has no modifier");
			LogAssert.IsFalse(HotkeyKeyMap.TryGetDisplayName("Hotkey1", out _), "authored actions are captioned by the Options table");
		}

		// ── The numbers the HUD moves by ────────────────────────────

		/// <summary>
		/// The row pitch the panels above the bar move by is the pitch the stylesheet draws.
		/// </summary>
		/// <remarks>
		/// Separate documents cannot be laid out against each other, so the pitch is written twice:
		/// in <c>UIHotkeyBar.uss</c>, which draws it, and in <see cref="UITKHudLayout"/>, which moves
		/// the resource bars, buffs, cast bar and chat by it. A slot resized in one and not the other
		/// puts the stacked rows under the resource bars.
		/// </remarks>
		[Test]
		public void TheRowPitch_MatchesTheStylesheet()
		{
			string uss = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), HotkeyBarUssPath)).Replace("\r\n", "\n");
			float slot = RuleValue(uss, ".hotkey-slot", "height");
			float gap = RuleValue(uss, ".hotkey-row", "margin-top");

			LogAssert.AreEqual(slot + gap, UITKHudLayout.HotbarRowPitch, "slot height + row gap");

			string chat = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), ChatUssPath)).Replace("\r\n", "\n");
			LogAssert.AreEqual(RuleValue(chat, ".chat-panel", "height"), UITKHudLayout.ChatAuthoredHeight,
				"the chat log gives up height from the height its stylesheet authors");
		}

		[Test]
		public void ASingleVisibleRow_LeavesEveryStylesheetInCharge()
		{
			VisualElement anchored = new VisualElement();
			anchored.style.marginBottom = 42.0f;

			if (ClientHotbarSettings.VisibleRows == 1)
			{
				UITKHudLayout.ApplyStackInset(anchored);
				LogAssert.AreEqual(StyleKeyword.Null, anchored.style.marginBottom.keyword,
					"one row: the inline margin is cleared, not written as zero");

				VisualElement chat = new VisualElement();
				chat.style.height = 100.0f;
				UITKHudLayout.ApplyChatInset(chat, placed: false);
				LogAssert.AreEqual(StyleKeyword.Null, chat.style.height.keyword, "one row: the chat log keeps its authored height");
			}

			VisualElement placedChat = new VisualElement();
			placedChat.style.height = 100.0f;
			UITKHudLayout.ApplyChatInset(placedChat, placed: true);
			LogAssert.AreEqual(StyleKeyword.Null, placedChat.style.height.keyword,
				"a chat log the player dragged away keeps its full height whatever the bar does");
		}

		/// <summary>The pixel value of one property inside one rule of a stylesheet.</summary>
		private static float RuleValue(string uss, string selector, string property)
		{
			Match rule = Regex.Match(uss, Regex.Escape(selector) + @"\s*\{(?<body>[^}]*)\}");
			LogAssert.IsTrue(rule.Success, $"{selector} must still be a rule of its own");
			Match value = Regex.Match(rule.Groups["body"].Value, @"(^|\s|;)" + Regex.Escape(property) + @"\s*:\s*(?<px>[0-9.]+)px");
			LogAssert.IsTrue(value.Success, $"{selector} must still set {property} in px");
			return float.Parse(value.Groups["px"].Value, System.Globalization.CultureInfo.InvariantCulture);
		}
	}
}
