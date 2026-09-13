using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.UIElements;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// There is one implementation of what an item slot does, and every slot panel uses it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The bag, the bank and the equipment sockets all draw a row of item slots, and all three had
	/// written the same slot behaviour themselves. <see cref="UITKItemGridPanel"/> unified the
	/// first two; the equipment panel sat beside it with eighteen identically named members —
	/// <c>SubscribeTracker</c>, <c>OnTrackerSlotPendingChanged</c>, <c>IsSlotBlocked</c>,
	/// <c>SetSlotItem</c>, <c>ClearSlot</c>, <c>ApplySlotLockVisual</c>, <c>RefreshSlotTooltip</c>,
	/// the pointer enter/leave pair, <c>BeginDragFromSlot</c>, <c>ResolveContainer</c>,
	/// <c>ReleaseAndClearDrag</c> and the rest — doing the same jobs in its own words.
	/// </para>
	/// <para>
	/// It is not the duplication that these tests are about. It is that the copies HAD ALREADY COME
	/// APART, in three ways, and every one of them was invisible from inside either file:
	/// shift-click quick transfer (issue #197) reached the grid and not the socket, so a player who
	/// learned the gesture in their bag found it dead on what they were wearing; the grid refused a
	/// slot whose item had no database identity yet and the socket offered it, costing a round trip
	/// that ends in a refusal nobody asked for; and a slot update repainted the lock overlay in one
	/// and not the other. A second copy does not double the work of a fix, it halves the chance of
	/// one.
	/// </para>
	/// <para>
	/// So these tests are deliberately part reflection and part source scan rather than
	/// behavioural, for the reason <c>PendingSlotWatchdogTests</c> gives about its watchdog: what
	/// is worth preventing is not a wrong answer from a function, it is a FOURTH hand-rolled copy
	/// appearing in the next panel somebody writes, or — the specific accident this refactor can
	/// have — a panel that derives from the shared base and goes on declaring its own
	/// <c>SubscribeTracker</c> beside it, quietly SHADOWING the inherited one rather than using it.
	/// That compiles. It passes every behavioural test. It is strictly worse than the duplication
	/// it looks like it removed, because now the two copies appear to be one.
	/// </para>
	/// <para>
	/// What is deliberately NOT pinned here is slot creation. The sockets are authored in
	/// <c>UICharacterSheet.uxml</c> and found by class name; the grids build one element per container
	/// slot in code. That difference is real and the base must stay out of it, so one test asserts
	/// the base does not create slots and each panel still creates its own its own way.
	/// </para>
	/// <para>
	/// Sources are read with line endings normalised, because the tree is CRLF on Windows and CI
	/// has checked it out both ways.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class SlotPanelSharingTests
	{
		private const string BasePath =
			"Assets/Scripts/Client/GUI/World/ItemContainers/UITKSlotPanelBase.cs";
		private const string GridPath =
			"Assets/Scripts/Client/GUI/World/ItemContainers/UITKItemGridPanel.cs";
		private const string EquipmentPath =
			"Assets/Scripts/Client/GUI/World/Equipment/UITKEquipment.cs";
		private const string BankPath =
			"Assets/Scripts/Client/GUI/World/Bank/UITKBank.cs";
		private const string InventoryPath =
			"Assets/Scripts/Client/GUI/World/Inventory/UITKInventory.cs";
		private const string TradePath =
			"Assets/Scripts/Client/GUI/World/Trade/UITKTrade.cs";
		private const string HotkeyBarPath =
			"Assets/Scripts/Client/GUI/World/HotkeyBar/UITKHotkeyBar.cs";
		private const string ControlPath =
			"Assets/Scripts/Client/GUI/UITKControl.cs";

		/// <summary>
		/// Every panel that creates a slot element of its own.
		/// </summary>
		/// <remarks>
		/// Each of these has to mark what it creates, so a press on a slot can be told from a press on
		/// anything else. The equipment panel is the awkward one: its sockets are authored in
		/// <c>UICharacterSheet.uxml</c> rather than created in code, and the mark is applied to the authored
		/// element as it is found — the UXML carries the look, the class list carries the meaning.
		/// </remarks>
		private static readonly string[] SlotBuildingPaths =
		{
			GridPath, EquipmentPath, TradePath, HotkeyBarPath,
			"Assets/Scripts/Client/GUI/World/Loot/UITKLoot.cs",
			"Assets/Scripts/Client/GUI/World/Container/UITKContainer.cs",
			"Assets/Scripts/Client/GUI/World/Inspect/UITKInspect.cs",
		};

		/// <summary>Every panel that draws item slots, base excluded.</summary>
		private static readonly string[] PanelPaths =
		{
			GridPath, EquipmentPath, BankPath, InventoryPath,
		};

		/// <summary>
		/// Every panel that draws a slot an item can be dropped into, the shared base included.
		/// </summary>
		/// <remarks>
		/// Wider than <see cref="PanelPaths"/>: the trade table and the hotkey bar do not derive from
		/// <see cref="UITKSlotPanelBase"/> — one draws an offer rather than a container, the other
		/// holds bindings — but both accept a carried item and both are places a release could move
		/// one. The rule about releases is about the GESTURE, so it is checked wherever the gesture
		/// can land.
		/// </remarks>
		private static readonly string[] DropTargetPaths =
		{
			BasePath, GridPath, EquipmentPath, BankPath, InventoryPath, TradePath, HotkeyBarPath,
		};

		/// <summary>The same panels as types, for the reflection half.</summary>
		private static readonly Type[] PanelTypes =
		{
			typeof(UITKItemGridPanel), typeof(UITKEquipment), typeof(UITKBank), typeof(UITKInventory),
		};

		/// <summary>Every drop target as a type, the base included.</summary>
		private static readonly Type[] DropTargetTypes =
		{
			typeof(UITKSlotPanelBase), typeof(UITKItemGridPanel), typeof(UITKEquipment),
			typeof(UITKBank), typeof(UITKInventory), typeof(UITKTrade), typeof(UITKHotkeyBar),
		};

		/// <summary>
		/// Members whose one implementation belongs to the shared base and nowhere else.
		/// </summary>
		/// <remarks>
		/// Each of these existed twice before the base did. They are the acts a slot performs once
		/// it exists — join the tracker, decide whether it can be clicked, paint it, describe it,
		/// pick it up, let it go — as opposed to the acts that differ by panel, which are the DROP
		/// (a swap or a split in a grid, an equip in a socket) and the CREATION of the slot itself.
		/// Those two are deliberately absent from this list: what a drop MEANS is per-panel, and
		/// every panel's copy of it is reached from its own pointer-down router.
		/// </remarks>
		private static readonly string[] SharedMembers =
		{
			"SubscribeTracker",
			"UnsubscribeTracker",
			"OnTrackerSlotPendingChanged",
			"OnTrackerResyncRequested",
			"RefreshAllSlots",
			"IsSlotBlocked",
			"SetSlotItem",
			"ClearSlot",
			"RefreshSlotTooltip",
			"ApplySlotLockVisual",
			"OnSlotPointerEnter",
			"OnSlotPointerLeave",
			"BeginDragFromSlot",
			"ResolveContainer",
			"ReleaseAndClearDrag",
			"Notify",
		};

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>
		/// The source with comment lines dropped, so a scan sees code and not prose.
		/// </summary>
		/// <remarks>
		/// Every one of these files explains in a comment what moved to the base and names the
		/// members by name — that explanation is the point of them. Scanning raw text would make
		/// the refactor fail its own test and, worse, would teach the next person to delete the
		/// explanation to get green. Line-level stripping is enough: this is a grep with manners,
		/// not a parser, and a trailing comment after real code still leaves the code visible.
		/// </remarks>
		private static string CodeOnly(string source)
		{
			string[] lines = source.Split('\n');
			StringBuilder code = new StringBuilder(source.Length);

			for (int i = 0; i < lines.Length; ++i)
			{
				string trimmed = lines[i].TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
					trimmed.StartsWith("/*", StringComparison.Ordinal) ||
					trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}

				code.Append(lines[i]).Append('\n');
			}

			return code.ToString();
		}

		/// <summary>
		/// Whether a file DECLARES a method of this name, as opposed to calling one.
		/// </summary>
		/// <remarks>
		/// An access modifier on the same line is what separates the two. Every declaration in this
		/// codebase carries one — there are no implicitly private members in the panels — and no
		/// call site does.
		/// </remarks>
		private static bool Declares(string code, string member)
		{
			return Regex.IsMatch(code, @"\b(?:private|protected|public|internal)\b[^\n;=]*\b" +
				Regex.Escape(member) + @"\s*\(");
		}

		/// <summary>
		/// The body of a method, brace-matched from its declaration.
		/// </summary>
		/// <remarks>
		/// Brace counting, not parsing, so it would be fooled by a brace inside a string or a
		/// character literal. That is acceptable because it is only ever pointed at the pointer-down
		/// routers, which contain neither; anything more adventurous should extract by anchors the
		/// way the neighbouring fixtures do.
		/// </remarks>
		private static string MethodBody(string source, string signature)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int open = source.IndexOf('{', start);
			LogAssert.IsTrue(open > start, $"{signature} must have a body");

			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{')
				{
					++depth;
				}
				else if (source[i] == '}')
				{
					--depth;
					if (depth == 0)
					{
						return source.Substring(open, i - open + 1);
					}
				}
			}

			LogAssert.IsTrue(false, $"{signature}'s body must be balanced");
			return string.Empty;
		}

		// ── The invariant itself ──────────────────────────────────────────────

		[Test]
		public void EverySlotPanelDerivesFromTheSharedBase()
		{
			/* The whole point, stated as a type relationship rather than as a string: a panel that
			 * stops deriving from the base is a panel that has started keeping its own copy of slot
			 * behaviour again, whatever its source file happens to say. */
			for (int i = 0; i < PanelTypes.Length; ++i)
			{
				LogAssert.IsTrue(typeof(UITKSlotPanelBase).IsAssignableFrom(PanelTypes[i]),
					$"{PanelTypes[i].Name} must derive from UITKSlotPanelBase, or it is keeping " +
					"its own copy of the tracker, the lock overlay and the drag");
			}

			/* And the base must still be the shared thing it claims to be, rather than a marker
			 * class somebody emptied out to silence the test above. */
			LogAssert.IsTrue(typeof(UITKSlotPanelBase).IsAbstract,
				"UITKSlotPanelBase is a base class, not a panel");
		}

		[Test]
		public void NoPanelDeclaresAMemberTheSharedBaseAlreadyOwns()
		{
			/* THE ACCIDENT THIS REFACTOR CAN HAVE, and the reason this test is reflection rather
			 * than text. Reparenting a panel to the base and forgetting to delete its now-duplicate
			 * members compiles perfectly: `private void SubscribeTracker()` beside an inherited one
			 * simply SHADOWS it. The panel keeps running its own copy, the base's version never
			 * executes, and the file looks refactored. That is worse than the duplication it
			 * appears to have removed, because the two copies now look like one.
			 *
			 * Scoped to what the base itself declares, not to everything it inherits: panels are
			 * entitled to override OnStarting, Hide and the character hooks, and those come from
			 * UITKControl. */
			HashSet<string> owned = DeclaredMemberNames(typeof(UITKSlotPanelBase));
			List<string> offenders = new List<string>();

			for (int i = 0; i < PanelTypes.Length; ++i)
			{
				CollectShadowedMembers(PanelTypes[i], owned, offenders);
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"a member that shares a base member's name must be an override, never a second " +
				"declaration beside it: " + string.Join("; ", offenders.ToArray()));
		}

		[Test]
		public void TheTrackerIsSubscribedInExactlyOnePlace()
		{
			/* The subscription is the piece with the sharpest failure mode. It is a STATIC event
			 * that outlives the panel, so the -= before += and the matching Detach are not tidiness
			 * — a missed unsubscribe is a handler running forever on a destroyed panel. Both copies
			 * got that right, and both explained it in the same words, which is exactly how you
			 * know it was copied rather than reasoned about twice. */
			string sharedBase = CodeOnly(ReadSource(BasePath));

			LogAssert.IsTrue(sharedBase.Contains("ItemOperationTracker.SlotPendingChanged"),
				"the base must be the thing that subscribes to the tracker");
			LogAssert.IsTrue(sharedBase.Contains("ItemOperationTracker.Attach()") &&
				sharedBase.Contains("ItemOperationTracker.Detach()"),
				"and the thing that attaches and detaches it");

			for (int i = 0; i < PanelPaths.Length; ++i)
			{
				string code = CodeOnly(ReadSource(PanelPaths[i]));
				string name = Path.GetFileName(PanelPaths[i]);

				LogAssert.IsFalse(code.Contains("ItemOperationTracker.SlotPendingChanged"),
					$"{name} must not wire the tracker's events itself");
				LogAssert.IsFalse(code.Contains("ItemOperationTracker.ResyncRequested"),
					$"{name} must not wire the tracker's resync itself");
				LogAssert.IsFalse(Regex.IsMatch(code, @"ItemOperationTracker\.(?:Attach|Detach)\s*\("),
					$"{name} must not attach or detach the tracker itself");
			}
		}

		[Test]
		public void SlotBehaviourIsDeclaredOnlyByTheSharedBase()
		{
			/* The eighteen-member duplication, listed. A panel is welcome to CALL any of these; what
			 * it may not do is grow another one. */
			string sharedBase = CodeOnly(ReadSource(BasePath));

			for (int i = 0; i < SharedMembers.Length; ++i)
			{
				LogAssert.IsTrue(Declares(sharedBase, SharedMembers[i]),
					$"UITKSlotPanelBase must declare {SharedMembers[i]}");
			}

			for (int p = 0; p < PanelPaths.Length; ++p)
			{
				string code = CodeOnly(ReadSource(PanelPaths[p]));
				string name = Path.GetFileName(PanelPaths[p]);

				for (int i = 0; i < SharedMembers.Length; ++i)
				{
					LogAssert.IsFalse(Declares(code, SharedMembers[i]),
						$"{name} declares its own {SharedMembers[i]}; the shared one is inherited");
				}
			}
		}

		[Test]
		public void SlotCreationStaysWithThePanelThatKnowsHowItsSlotsAreMade()
		{
			/* The line the base must NOT cross, and the reason this is a test rather than a note.
			 * A base that also built the slots would have to choose one of the two ways they come
			 * into existence, and either choice breaks a panel: the sockets are authored in
			 * UICharacterSheet.uxml and queried by class name, so there is no eleventh one to create,
			 * while a grid's count follows the container and has to be rebuilt when it changes. So
			 * the base holds the list and neither fills it. */
			string sharedBase = CodeOnly(ReadSource(BasePath));

			LogAssert.IsTrue(sharedBase.Contains("slotViews"),
				"the base owns the slot list, which is what lets the shared painters be shared");
			LogAssert.IsFalse(sharedBase.Contains("slotViews.Add("),
				"but it must not populate the list; only a panel knows where its slots come from");
			LogAssert.IsFalse(sharedBase.Contains("fish-slot"),
				"and it must not know the markup, which is the grids' and the sockets' own business");

			string equipment = CodeOnly(ReadSource(EquipmentPath));
			LogAssert.IsTrue(equipment.Contains("className: \"fish-slot__icon\""),
				"the equipment panel finds its sockets in the UXML it was authored with");
			LogAssert.IsTrue(equipment.Contains("SlotElementNames"),
				"one authored element per ItemSlot, in enum order — that array is the contract");

			string grid = CodeOnly(ReadSource(GridPath));
			LogAssert.IsTrue(grid.Contains("slotGrid.Add("),
				"the grids build their slots in code, sized from the container");
		}

		// ── The divergences, pinned individually ──────────────────────────────

		[Test]
		public void BothPointerDownRoutersHonourShiftClick()
		{
			/* THE DIVERGENCE THAT WAS A PLAYER-VISIBLE BUG. Shift-click means "send this to the
			 * other container" (issue #197) in the bag and in the bank, and a socket's other
			 * container is the bag. The equipment panel's router was the same method under the same
			 * name with that branch missing, so the gesture was dead on everything the player was
			 * wearing — and a shift-click that does nothing is indistinguishable from one the game
			 * did not receive.
			 *
			 * The routers are the one part of the click path that could not be hoisted: each one
			 * dispatches to a completion that speaks a different half of the item protocol, a swap
			 * or a split in a grid and an equip in a socket. So this test stands in for the base
			 * class that cannot exist here. Two routers, one rule. */
			string grid = ReadSource(GridPath);
			string equipment = ReadSource(EquipmentPath);

			string gridRouter = MethodBody(grid, "private void OnSlotPointerDown");
			string socketRouter = MethodBody(equipment, "private void OnSlotPointerDown");

			LogAssert.IsTrue(gridRouter.Contains("evt.shiftKey"),
				"the grid router must read shift from the click that happened");
			LogAssert.IsTrue(socketRouter.Contains("evt.shiftKey"),
				"and so must the socket router, which is where this was missing");

			/* Read from the event, never polled. The modifier that matters is the one held when
			 * this click happened, and a poll can answer for a moment either side of it. */
			LogAssert.IsFalse(socketRouter.Contains("Keyboard.current"),
				"the modifier must come from the event, not from global input state");
		}

		[Test]
		public void NeitherPointerDownRouterStealsTheDropFromADragInFlight()
		{
			/* THE ONE REGRESSION THIS REFACTOR INTRODUCED, and the reason the shift branch needs a
			 * gate rather than a comment.
			 *
			 * Hoisting the shared slot behaviour into a base class had a side effect nobody was
			 * looking for: it made "shift-click" and "left-click" reachable in the same order in
			 * both routers, and that order is wrong while something is being carried. A left-click
			 * with a drag in flight is the DROP, and the drop is completed on pointer-UP, which
			 * reads no modifier. So a player holding shift out of habit while dropping — or simply
			 * with shift still down from the transfer they just made — had the press divert into
			 * "send this slot's item to the other container", claiming a socket for an operation
			 * they never asked for. The release's own claim on the same socket then failed, and the
			 * swap was lost in BOTH directions: the item meant for the socket stayed where it was,
			 * and the item already in the socket was sent to the bag.
			 *
			 * The false fix is the tempting one, and it is the bug: making the shift path clear the
			 * drag the way right-click does. Clearing the drag CANCELS the drop — the player is
			 * mid-gesture, and the press they just made would throw it away. Deferring to the drag
			 * is the fix, because the drop was going to complete anyway.
			 *
			 * The existing test above does NOT protect any of this: it asserts the routers read
			 * evt.shiftKey, which stays true whether the branch is gated or not. That is why this
			 * sibling exists rather than an extra line there. */
			AssertShiftDefersToADrag("UITKItemGridPanel",
				MethodBody(ReadSource(GridPath), "private void OnSlotPointerDown"));
			AssertShiftDefersToADrag("UITKEquipment",
				MethodBody(ReadSource(EquipmentPath), "private void OnSlotPointerDown"));

			/* One implementation, and it is the base's — the routers having a copy each of the
			 * click rule is how the two of them came apart in the first place, which is the whole
			 * subject of this fixture. */
			string predicate = MethodBody(CodeOnly(ReadSource(BasePath)),
				"protected static bool IsDragInFlight");
			LogAssert.IsTrue(predicate.Contains("TryGetTK(DRAG_OBJECT_NAME"),
				"the predicate must find the drag overlay by name, the one the drop itself consults");
			LogAssert.IsTrue(predicate.Contains("IsDragging"),
				"and must ask that overlay whether it is carrying something, not merely whether it exists");

			for (int p = 0; p < PanelPaths.Length; ++p)
			{
				LogAssert.IsFalse(Declares(CodeOnly(ReadSource(PanelPaths[p])), "IsDragInFlight"),
					$"{Path.GetFileName(PanelPaths[p])} must inherit IsDragInFlight rather than " +
					"declare its own; a per-panel copy is a per-panel click rule");
			}

			/* And the behaviour, where it is cheap to have. With no panel booted there is no drag
			 * overlay registered, so the predicate must answer false and the shift branch must stay
			 * reachable — a predicate that answered true unconditionally would kill shift-click
			 * outright, issue #197 all over again, and would look exactly like a working gate from
			 * inside either router. */
			MethodInfo inFlight = typeof(UITKSlotPanelBase).GetMethod("IsDragInFlight",
				BindingFlags.NonPublic | BindingFlags.Static);
			LogAssert.IsTrue(inFlight != null,
				"UITKSlotPanelBase must still declare IsDragInFlight");
			LogAssert.IsFalse((bool)inFlight.Invoke(null, null),
				"nothing is being carried when no drag overlay is registered, so the gate must open " +
				"and leave shift-click working for the ordinary click it is for");
		}

		/// <summary>
		/// Asserts one router's shift branch defers to a drag in flight, and does not cancel it.
		/// </summary>
		/// <remarks>
		/// Deliberately tolerant about operand order and spacing, because either way of writing the
		/// conjunction is the same rule. What it is NOT tolerant about is which call the branch
		/// makes, because "clear the drag instead" is the plausible wrong fix rather than a typo.
		/// </remarks>
		private static void AssertShiftDefersToADrag(string panelName, string router)
		{
			LogAssert.IsTrue(Regex.IsMatch(router, @"evt\.shiftKey\s*&&\s*!IsDragInFlight\(\)") ||
				Regex.IsMatch(router, @"!IsDragInFlight\(\)\s*&&\s*evt\.shiftKey"),
				$"{panelName}'s shift branch must be gated on nothing being carried, or a shift held " +
				"during a drop diverts the press and the swap is lost in both directions");

			/* The shift branch itself, whatever the surrounding formatting. */
			Match branch = Regex.Match(router, @"evt\.shiftKey[^{]*\{([^}]*)\}");
			LogAssert.IsTrue(branch.Success,
				$"{panelName}'s shift branch must still be a braced block this test can read");

			string body = branch.Groups[1].Value;

			LogAssert.IsTrue(body.Contains("TryQuick"),
				$"{panelName}'s shift branch must still be the quick transfer it was");

			LogAssert.IsFalse(body.Contains("ReleaseAndClearDrag") || body.Contains("dragObject"),
				$"{panelName}'s shift branch must DEFER to the drag, not clear it — clearing it " +
				"cancels the drop the player is in the middle of making, which is the bug and not " +
				"the fix");
		}

		[Test]
		public void ASlotHoldingAnItemWithNoIdentityIsTreatedAsBusy()
		{
			/* The second divergence. An item with ID <= 0 is one the database has not written yet;
			 * the server keeps its slot locked until the row lands and would refuse any request
			 * naming it. The grid knew that and the socket did not, so a socket offered a drag that
			 * was certain to be refused. Settled in the grid's favour in the one shared body — this
			 * pins the rule so that a later simplification of IsSlotBlocked cannot drop it again
			 * for both panels at once. */
			string body = MethodBody(ReadSource(BasePath), "protected bool IsSlotBlocked");

			LogAssert.IsTrue(body.Contains("IsPending("),
				"a slot waiting on this client's own request is busy");
			LogAssert.IsTrue(body.Contains("IsSlotLocked("),
				"and so is one the container has locked");
			LogAssert.IsTrue(body.Contains("item.ID <= 0"),
				"and so is one holding an item the database has not written yet");
		}

		[Test]
		public void ApplyingAServerSlotRepaintsItsLockOverlay()
		{
			/* The third divergence, and the subtlest: the grid repainted the lock overlay after
			 * applying an update and the socket did not. The item ITSELF can be the reason a slot
			 * is blocked — see the identity rule above — so the slot arriving with its identity is
			 * what unblocks it, and a panel that only listens for lock-change events misses that
			 * moment. */
			string body = MethodBody(ReadSource(BasePath), "protected void ApplySlotUpdate");

			LogAssert.IsTrue(body.Contains("ItemOperationTracker.Release("),
				"the slot arriving IS the acknowledgement, so the pending mark comes off here");
			LogAssert.IsTrue(body.Contains("ApplySlotLockVisual("),
				"and the overlay is repainted, which is what the socket copy never did");
			LogAssert.IsTrue(body.Contains("NotifySlotChanged("),
				"a drag started from this slot no longer refers to what it was started from");
		}

		// ── Cancelling a carried item ─────────────────────────────────────────

		[Test]
		public void APressThatMissesEverySlotCancelsTheCarriedItem()
		{
			/* An item is picked up by pressing a slot and stays on the cursor until something is done
			 * with it — which, now that a release completes nothing, means the interface needs a way
			 * to put it back down WITHOUT moving it. A press anywhere that is not a slot is that way.
			 *
			 * It lives on UITKControl rather than in the item panels because it is a rule about the
			 * whole interface: pressing the character sheet, the chat log or a dialog while carrying
			 * something has to cancel it too, and none of those panels knows what a slot is. That is
			 * also why it needs a test at all — a rule spread over the panels would be visible in
			 * each of them, and this one is invisible in all of them. */
			string control = CodeOnly(ReadSource(ControlPath));

			string cancel = MethodBody(control, "private void OnRootPointerDownCancelDrag");

			LogAssert.IsTrue(cancel.Contains("ClassListContains(SLOT_MARKER_CLASS)"),
				"the press is classified by the slot marker, not by whether it landed on a panel");
			LogAssert.IsTrue(cancel.Contains("element.parent"),
				"and the classification walks up from the pressed element, because a press on a slot's " +
				"icon or count badge is a press on the slot — the marker is on the root, not the parts");
			LogAssert.IsTrue(Regex.IsMatch(cancel, @"evt\.button\s*!=\s*0"),
				"left button only, matching the world-click cancel in UITKDragObject.OnTick; the right " +
				"button already means split, unequip or take-back wherever it lands, and none of those " +
				"is a cancel");
			LogAssert.IsTrue(cancel.Contains("IsDragging"),
				"nothing is carried before the press that starts a drag, so the test must be that a drag " +
				"is IN FLIGHT — otherwise the press that picks the item up cancels it on the way past");
			LogAssert.IsTrue(cancel.Contains("Clear()"),
				"and a press that misses every slot ends the drag");

			/* Registered in the trickle-down phase, like the focus handler beside it and for the same
			 * reason: a child that stops propagation — which several controls do — would otherwise
			 * swallow the press and the player would be left carrying an item they cannot put down.
			 * The phase is also what makes the ordering safe on a slot: this runs before the slot's
			 * own handler, but it returns on the target, so the slot still sees its press untouched. */
			LogAssert.IsTrue(Regex.IsMatch(control,
					@"RegisterCallback<PointerDownEvent>\(OnRootPointerDownCancelDrag, TrickleDown\.TrickleDown\)"),
				"the cancel must be re-registered with the root in the trickle-down phase");
			LogAssert.IsTrue(Regex.IsMatch(control,
					@"UnregisterCallback<PointerDownEvent>\(OnRootPointerDownCancelDrag, TrickleDown\.TrickleDown\)"),
				"and unregistered first, or a tree rebuilt on every show accumulates a handler per show");

			/* Every panel that builds a slot marks it. A slot that is not marked is a slot a press
			 * cancels a drag on — which is the one place the rule must not apply, because pressing a
			 * slot is how the carried item is put down. */
			for (int p = 0; p < SlotBuildingPaths.Length; ++p)
			{
				string name = Path.GetFileName(SlotBuildingPaths[p]);
				LogAssert.IsTrue(CodeOnly(ReadSource(SlotBuildingPaths[p]))
						.Contains("AddToClassList(SLOT_MARKER_CLASS)"),
					$"{name} builds slots and must mark them with UITKControl.SLOT_MARKER_CLASS, or a " +
					"press on one of its slots cancels the drag the player is trying to put down there");
			}

			/* And the marker must stay a marker. It exists only so a press can be classified, so any
			 * stylesheet naming it has turned it into an appearance as well — and, more to the point,
			 * the tempting tidy is to reuse `fish-slot`, which every slot but the hotkey bar already
			 * carries and which styles the whole slot. That change would restyle every hotkey slot,
			 * and this is the assertion that says so out loud. */
			LogAssert.IsFalse(UssFilesMention(UITKControl.SLOT_MARKER_CLASS),
				$"the marker class '{UITKControl.SLOT_MARKER_CLASS}' is styled by a stylesheet; it " +
				"must stay a marker with no appearance of its own, or every slot wearing it is restyled " +
				"by the act of being classified");
		}

		/// <summary>
		/// Whether any stylesheet in the client GUI mentions a USS class.
		/// </summary>
		/// <remarks>
		/// A literal search rather than a parse, because the question is whether the name appears at
		/// all: a class that is styled only in a descendant selector or only under a pseudo-state is
		/// still an element that changes how a slot looks, and the point of the marker is that it
		/// cannot.
		/// </remarks>
		private static bool UssFilesMention(string className)
		{
			string gui = Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Client/GUI");
			LogAssert.IsTrue(Directory.Exists(gui), $"the client GUI must be at {gui}");

			string[] sheets = Directory.GetFiles(gui, "*.uss", SearchOption.AllDirectories);
			LogAssert.IsTrue(sheets.Length > 0, "there must be stylesheets to check the marker against");

			for (int i = 0; i < sheets.Length; ++i)
			{
				if (File.ReadAllText(sheets[i]).Contains(className))
				{
					return true;
				}
			}

			return false;
		}

		// ── A release never moves an item ─────────────────────────────────────

		[Test]
		public void NoDropTargetActsOnARelease()
		{
			/* THE RULE, AND THE ONE THAT KEEPS BEING "FIXED" BACK.
			 *
			 * Moving an item is a PRESS to pick it up and a PRESS to put it down. A release does
			 * nothing, anywhere: not in the bag, not in the bank, not on a socket, not on the trade
			 * table, not on the hotkey bar.
			 *
			 * This was not always the case. The bag and the sockets completed a drop on the release
			 * as well as on the press, and the bank deliberately did not — so the bank's missing
			 * registration read as the bug, and the asymmetry was resolved the wrong way round by
			 * giving every panel one. What that bought was a second completion path arriving
			 * immediately after the first: a press on the destination already ends the drag, so the
			 * release that follows finds nothing in flight, and the two can only be made to coexist
			 * by weakening the guard that stops one gesture completing twice. See the note above
			 * Notify in UITKSlotPanelBase, which is where the rule is written down in the source.
			 *
			 * Green here is the ABSENCE of something, which is the hardest kind of thing to keep.
			 * So it is asserted twice over, and the two halves cover different ground. The source
			 * scan reads the file the registration would be written in, and knows nothing about
			 * names: the trade table's release handler was OnOwnSlotPointerUp and the bar's was
			 * OnSlotPointerUp, and the next one would be called something else again — so it looks
			 * for the event type itself, which no handler can avoid naming. The reflection walk
			 * reads the compiled types, including the nested ones a lambda is emitted onto, and so
			 * catches a handler registered through a helper this fixture does not know to read.
			 *
			 * They are not equal halves, and it is worth being exact about which one is load
			 * bearing. A registration written in a drop target's own file is caught by the scan
			 * alone — dropping `RegisterCallback<PointerUpEvent>(evt => { })` into UITKEquipment
			 * fails this test on the scan's message, and the walk never gets a word in. What the
			 * walk adds is the shapes the scan structurally cannot see: a handler on a partial part
			 * file that DropTargetPaths does not list, a registration made from another type, or a
			 * release handler that is declared and reached by a route this fixture knows nothing
			 * about. It is defence in depth, not a second opinion.
			 *
			 * The walk is only worth having because it descends. A DeclaredOnly sweep of the panel
			 * alone returns nothing for a lambda: the compiler emits one onto a generated nested
			 * type, so a control run with the walk stopping at the panel stayed green while the
			 * registration sat in the file. With the recursion it reports the handler by its
			 * generated name — `<>c.<BuildSlotViewsFromMarkup>b__30_1` — which is unreadable and
			 * entirely the point: it is the name no source scan could have been written to match. */
			for (int p = 0; p < DropTargetPaths.Length; ++p)
			{
				string name = Path.GetFileName(DropTargetPaths[p]);
				string code = CodeOnly(ReadSource(DropTargetPaths[p]));

				LogAssert.IsFalse(code.Contains("RegisterCallback<PointerUpEvent>"),
					$"{name} registers a pointer-up callback; a release must not move an item, so " +
					"there is nothing for one to do — the drag stays armed and the next PRESS is " +
					"what completes it");
				LogAssert.IsFalse(code.Contains("PointerUpEvent"),
					$"{name} handles a pointer-up event at all; the drop is completed by the press " +
					"that lands on the destination, never by letting go over it");
			}

			for (int t = 0; t < DropTargetTypes.Length; ++t)
			{
				AssertHasNoPointerUpHandler(DropTargetTypes[t]);
			}
		}

		[Test]
		public void ThePressThatPicksUpAndThePressThatPutsDownAreOneHandler()
		{
			/* The other half of the rule, and the half that makes it a gesture rather than a
			 * deletion. Because nothing acts on the release, the item stays ON THE CURSOR and the
			 * next press is the one that puts it down — which only works if the SAME handler that
			 * completes a drop also starts the drag. A panel that read "a press completes" and kept
			 * only the completion would be a panel nothing could be dragged out of.
			 *
			 * So both routers dispatch on whether something is being carried, and both branches are
			 * real: the completion, and the pick-up. */
			AssertPressPicksUpAndPutsDown("UITKItemGridPanel",
				MethodBody(ReadSource(GridPath), "protected virtual void HandleSlotLeftClick"));
			AssertPressPicksUpAndPutsDown("UITKEquipment",
				MethodBody(ReadSource(EquipmentPath), "private void HandleSlotLeftClick"));

			/* And pressing the slot the item came from is how a drag is ABANDONED, now that
			 * releasing no longer does anything — the item goes back and the drag ends, rather than
			 * the press being a no-op that leaves the player stuck carrying something they no longer
			 * want. The grid refuses the same slot inside the completion; that refusal is the cancel
			 * gesture, not a bug to be tidied away. */
			string completion = MethodBody(ReadSource(GridPath),
				"protected void CompleteDropOntoSlot(");
			LogAssert.IsTrue(completion.Contains("sourceSlot == slotIndex"),
				"a press on the slot the item was picked up from must be refused as a move — that " +
				"refusal is the only way left to put a carried item back");
			LogAssert.IsTrue(completion.Contains("dragObject.Clear()"),
				"and it must end the drag, or the refusal leaves the player carrying the item still");
		}

		/// <summary>
		/// Asserts a router's press handler both completes a drop and starts one.
		/// </summary>
		/// <remarks>
		/// The two branches are the same gesture one press apart, so a handler that lost either one
		/// is broken in a way no other test here would see: no completion and the item cannot be put
		/// down, no pick-up and nothing can be carried.
		/// </remarks>
		private static void AssertPressPicksUpAndPutsDown(string panelName, string handler)
		{
			LogAssert.IsTrue(handler.Contains("IsDragging"),
				$"{panelName}'s press handler must dispatch on whether something is being carried");
			LogAssert.IsTrue(handler.Contains("CompleteDropOntoSlot("),
				$"{panelName}'s press handler must complete the drop when something is");
			LogAssert.IsTrue(handler.Contains("BeginDragFromSlot("),
				$"{panelName}'s press handler must start the drag when nothing is — it is the only " +
				"way a drag begins, and therefore the only way the next press has anything to put down");
		}

		/// <summary>
		/// Asserts no method on a type, or on anything nested inside it, takes a
		/// <c>PointerUpEvent</c>.
		/// </summary>
		/// <remarks>
		/// The name-free half of the release pin. Reading the compiled type rather than the source
		/// catches a handler that is declared but registered somewhere this fixture does not know to
		/// look, and catches one whose name says nothing about what it handles. What it cannot catch
		/// is a registration of somebody else's handler, which is why the source scan above runs too.
		/// <para>
		/// <b>The nested types are the point, not a thoroughness flourish.</b> A lambda is not a
		/// method of the panel it is written in: the compiler emits it onto a generated nested type
		/// — <c>&lt;&gt;c</c> when it captures nothing, <c>&lt;&gt;c__DisplayClass…</c> when it
		/// captures locals — and those are reached through <see cref="Type.GetNestedTypes()"/>, never
		/// through <see cref="Type.GetMethods()"/>. A <c>DeclaredOnly</c> walk of the panel alone
		/// therefore returns nothing at all for <c>RegisterCallback&lt;PointerUpEvent&gt;(evt =&gt;
		/// …)</c>, which is the most likely shape of the regression this pin exists to stop: a
		/// control run with exactly that line in a drop target left this assertion green. The source
		/// scan is what caught it. Both halves are wanted — the scan knows the file, this knows the
		/// type — but only if this one actually looks where the lambda went.
		/// </para>
		/// </remarks>
		private static void AssertHasNoPointerUpHandler(Type type)
		{
			MethodInfo[] methods = type.GetMethods(AllDeclared);
			for (int i = 0; i < methods.Length; ++i)
			{
				ParameterInfo[] parameters = methods[i].GetParameters();
				for (int p = 0; p < parameters.Length; ++p)
				{
					LogAssert.IsFalse(parameters[p].ParameterType == typeof(PointerUpEvent),
						$"{type.Name}.{methods[i].Name} handles a pointer-up event; a release must not " +
						"move an item in any panel");
				}
			}

			Type[] nested = type.GetNestedTypes(AllDeclared);
			for (int n = 0; n < nested.Length; ++n)
			{
				AssertHasNoPointerUpHandler(nested[n]);
			}
		}

		// ── Reflection helpers ────────────────────────────────────────────────

		private const BindingFlags AllDeclared =
			BindingFlags.Instance | BindingFlags.Static |
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

		/// <summary>Every member name a type declares itself, nested types included.</summary>
		private static HashSet<string> DeclaredMemberNames(Type owner)
		{
			HashSet<string> names = new HashSet<string>();

			MemberInfo[] members = owner.GetMembers(AllDeclared);
			for (int i = 0; i < members.Length; ++i)
			{
				if (!IsCompilerGenerated(members[i]) && !IsConstructor(members[i]))
				{
					names.Add(members[i].Name);
				}
			}

			Type[] nested = owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
			for (int i = 0; i < nested.Length; ++i)
			{
				names.Add(nested[i].Name);
			}

			return names;
		}

		/// <summary>
		/// Adds one panel's shadowing members to <paramref name="offenders"/>.
		/// </summary>
		/// <remarks>
		/// A method or property that shares a base member's name is fine as long as it OVERRIDES
		/// it, which <see cref="MethodInfo.GetBaseDefinition"/> answers: for an override it reports
		/// a declaring type further up the chain. A field or a nested type cannot override anything,
		/// so a name collision there is always a shadow.
		/// </remarks>
		private static void CollectShadowedMembers(Type panel, HashSet<string> owned, List<string> offenders)
		{
			MemberInfo[] members = panel.GetMembers(AllDeclared);
			for (int i = 0; i < members.Length; ++i)
			{
				MemberInfo member = members[i];
				if (IsCompilerGenerated(member) || IsConstructor(member) || !owned.Contains(member.Name))
				{
					continue;
				}

				MethodInfo method = member as MethodInfo;
				if (method != null)
				{
					if (method.GetBaseDefinition().DeclaringType == method.DeclaringType)
					{
						offenders.Add($"{panel.Name}.{member.Name}() hides the base's");
					}
					continue;
				}

				PropertyInfo property = member as PropertyInfo;
				if (property != null)
				{
					MethodInfo getter = property.GetGetMethod(true);
					if (getter == null || getter.GetBaseDefinition().DeclaringType == getter.DeclaringType)
					{
						offenders.Add($"{panel.Name}.{member.Name} hides the base's");
					}
					continue;
				}

				offenders.Add($"{panel.Name}.{member.Name} hides the base's member of that name");
			}
		}

		/// <summary>
		/// Whether a member was written by the compiler rather than by a person.
		/// </summary>
		/// <remarks>
		/// Lambdas and their closure classes are how every slot registers its callbacks here, so
		/// the panels are full of them. They are named with angle brackets, which no C# identifier
		/// can contain, and they carry the attribute as well — both are checked because the display
		/// classes hold the attribute while some of their members only have the name.
		/// </remarks>
		private static bool IsCompilerGenerated(MemberInfo member)
		{
			return member.Name.IndexOf('<') >= 0 ||
				member.IsDefined(typeof(CompilerGeneratedAttribute), false);
		}

		/// <summary>
		/// Whether a member is a constructor.
		/// </summary>
		/// <remarks>
		/// Every one of these types has one, they are all called <c>.ctor</c>, and a constructor
		/// cannot override anything — so without this the shadowing scan would report each panel
		/// hiding the base's constructor, which is not a thing that can happen.
		/// </remarks>
		private static bool IsConstructor(MemberInfo member)
		{
			return member.MemberType == MemberTypes.Constructor;
		}
	}
}
