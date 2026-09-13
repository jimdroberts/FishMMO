using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
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
	/// <c>UIEquipment.uxml</c> and found by class name; the grids build one element per container
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

		/// <summary>Every panel that draws item slots, base excluded.</summary>
		private static readonly string[] PanelPaths =
		{
			GridPath, EquipmentPath, BankPath, InventoryPath,
		};

		/// <summary>The same panels as types, for the reflection half.</summary>
		private static readonly Type[] PanelTypes =
		{
			typeof(UITKItemGridPanel), typeof(UITKEquipment), typeof(UITKBank), typeof(UITKInventory),
		};

		/// <summary>
		/// Members whose one implementation belongs to the shared base and nowhere else.
		/// </summary>
		/// <remarks>
		/// Each of these existed twice before the base did. They are the acts a slot performs once
		/// it exists — join the tracker, decide whether it can be clicked, paint it, describe it,
		/// pick it up, let it go — as opposed to the acts that differ by panel, which are the DROP
		/// (a swap or a split in a grid, an equip in a socket) and the CREATION of the slot itself.
		/// Those two are deliberately absent from this list.
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
			 * UIEquipment.uxml and queried by class name, so there is no eleventh one to create,
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
