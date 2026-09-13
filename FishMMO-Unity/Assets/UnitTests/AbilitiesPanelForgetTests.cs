using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that the Abilities tab offers a way to forget a crafted ability, that the offer is only
	/// on the rows the server can act on, and that the press cannot do two things at once.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The craft panel had been telling players to forget an ability since the UI Toolkit conversion
	/// — the craft list filters already-known templates out and its hint says so — while nothing in
	/// the interface could forget one. This is the control that makes the advertised flow reachable.
	/// </para>
	/// <para>
	/// The press rules are the load-bearing half. A row's own <c>PointerDownEvent</c> picks the
	/// ability up onto the drag object, and a <c>Button</c> does NOT consume its own press — Unity's
	/// <c>Clickable</c> stops propagation of the MOVE event only — so a forget button that did
	/// nothing about it would arm a drag and open a confirmation from one click. Source-scanning,
	/// like its neighbours: it reads what was written, not what runs.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AbilitiesPanelForgetTests
	{
		private const string PanelPath =
			"Assets/Scripts/Client/GUI/World/Ability/UITKAbilities.cs";

		private const string StylePath =
			"Assets/Scripts/Client/GUI/World/Ability/UIAbilities.uss";

		/// <summary>Source text with line endings normalised, so bounds do not depend on checkout.</summary>
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The body of a named method, bounded by an anchor inside it or the next member.</summary>
		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int end = source.IndexOf(nextSymbol, start, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");

			return source.Substring(start, end - start);
		}

		[Test]
		public void OnlyCraftedAbilitiesOfferAForget()
		{
			/* Learned knowledge is permanent and must not look as though it is: a base ability or an
			 * effect the character has bought is never removed, and the Knowledge tab is most of the
			 * list. A button the server would refuse is an invitation to a refusal. */
			string body = MethodBody(ReadSource(PanelPath),
				"private void CreateEntry(AbilityEntry entry, string name, string meta, string badge)",
				"entry.Root = row;");

			int guard = body.IndexOf("if (entry.Tab == AbilityTabType.Ability)", StringComparison.Ordinal);
			int button = body.IndexOf("new Button(() => OnForgetClicked(entry, name))", StringComparison.Ordinal);
			LogAssert.IsTrue(guard >= 0 && button > guard,
				"the forget button must be gated on the row being a usable ability, not a piece of knowledge");

			/* Appended last so it lands at the right edge: the text column grows and pushes everything
			 * after it over. */
			int badge = body.IndexOf("row.Add(badgeLabel);", StringComparison.Ordinal);
			LogAssert.IsTrue(badge >= 0 && button > badge,
				"it belongs after the text and the badge, which are what it is pushed away from");
		}

		[Test]
		public void APressOnTheForgetButtonDoesNotPickTheAbilityUp()
		{
			/* The row's pointer-down handler arms the drag. A Button does not stop its own press, so
			 * the button has to say so itself — and it says it about the pointer DOWN, which is the
			 * event the row listens for. */
			string body = MethodBody(ReadSource(PanelPath),
				"private void CreateEntry(AbilityEntry entry, string name, string meta, string badge)",
				"entry.Root = row;");

			LogAssert.IsTrue(body.Contains("forgetButton.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());"),
				"the forget button must stop its own press, or the click also starts a drag");

			/* And it must be the button, not the row: stopping it on the row would swallow the press
			 * the drag path depends on. */
			LogAssert.IsTrue(body.Contains("forgetButton.tooltip = FORGET_TOOLTIP;"),
				"the button is a real control with its own affordance, not a decorated row");
		}

		[Test]
		public void ForgettingIsConfirmedBeforeItIsSent()
		{
			/* A forgotten ability is gone for good: the row is the only record of it, re-crafting it
			 * costs the price again, and events bought with it are not refunded. Every other
			 * destructive control in this interface confirms. */
			string body = MethodBody(ReadSource(PanelPath),
				"private void OnForgetClicked(AbilityEntry entry, string name)",
				"private void OnClientAbilityForgetResultReceived(");

			int dialog = body.IndexOf("UIManager.TryGetTK(DIALOG_BOX_NAME, out UITKDialogBox dialog)", StringComparison.Ordinal);
			LogAssert.IsTrue(dialog >= 0, "the confirmation must come from the shared dialog box");

			int opened = body.IndexOf("dialog.Open(", StringComparison.Ordinal);
			int sent = body.IndexOf("Client.Broadcast(new AbilityForgetBroadcast()", StringComparison.Ordinal);
			LogAssert.IsTrue(opened >= 0 && sent > opened,
				"the request must be sent from the dialog's accept callback, never on the click itself");

			/* Captured by value: the row this came from is removed by the answer the broadcast brings
			 * back, so a captured AbilityEntry would reference a detached tree by then. */
			int captured = body.IndexOf("long abilityID = entry.ReferenceID;", StringComparison.Ordinal);
			LogAssert.IsTrue(captured >= 0 && captured < opened,
				"the identity must be copied out before the dialog is opened");
			LogAssert.IsTrue(body.Contains("AbilityID = abilityID,"),
				"and the request must carry the copy rather than reaching back into the row");
		}

		[Test]
		public void TheRowGoesWhenTheServerSaysSoAndNotBefore()
		{
			/* The server owns the row and may still refuse. A panel that dropped it on the click
			 * would show an ability as gone that the next login hands straight back. */
			string body = MethodBody(ReadSource(PanelPath),
				"private void OnForgetClicked(AbilityEntry entry, string name)",
				"private void OnClientAbilityForgetResultReceived(");

			LogAssert.IsFalse(body.Contains("RemoveAbility("),
				"the click must not remove the row; the answer does that through the controller's event");

			LogAssert.IsFalse(body.Contains("entries.Remove("),
				"nor may it reach into the panel's own list directly");

			string source = ReadSource(PanelPath);
			LogAssert.IsTrue(source.Contains("RegisterBroadcast<AbilityForgetResultBroadcast>(OnClientAbilityForgetResultReceived)"),
				"the refusal has to be registered for, or a refused forget is silence");
			LogAssert.IsTrue(source.Contains("UnregisterBroadcast<AbilityForgetResultBroadcast>"),
				"and unregistered, or a panel that closes and reopens leaks a handler onto a dead instance");
		}

		[Test]
		public void ARefusedForgetSaysWhyTheAbilityIsStillThere()
		{
			/* The accepted case needs no wording here — the controller removes the ability and the
			 * panel's own row handler takes it off the list. A refusal has nowhere else to be said. */
			string body = MethodBody(ReadSource(PanelPath),
				"private void OnClientAbilityForgetResultReceived(",
				"private void ExplainRefusedPickup(AbilityEntry entry)");

			LogAssert.IsTrue(body.Contains("if (msg.Failure == AbilityForgetFailure.None)"),
				"success is the controller's to apply; answering it here would be a second weaker writer");

			LogAssert.IsTrue(body.Contains("AbilityForgetFailure.Busy => FORGET_BUSY"),
				"a busy refusal is not a failure and must not read as one");

			LogAssert.IsTrue(body.Contains("_ => FORGET_FAILED,"),
				"and everything else must still say something, which is the default arm");
		}

		[Test]
		public void TheForgetButtonIsADestructiveControlRevealedOnHover()
		{
			/* Revealed rather than always drawn: a column of delete buttons down a list of rows the
			 * player is reading turns every mis-aimed click into a loss. It shares the interface's
			 * one destructive look rather than inventing a second. */
			string source = ReadSource(PanelPath);
			LogAssert.IsTrue(source.Contains("forgetButton.AddToClassList(\"fish-close-btn\")"),
				"the destructive look is the shared one, not a panel-local invention");

			string style = ReadSource(StylePath);
			int rule = style.IndexOf(".ability-entry__forget {", StringComparison.Ordinal);
			LogAssert.IsTrue(rule >= 0, "the button needs its own geometry, or the row's layout moves");

			string body = style.Substring(rule, Math.Min(400, style.Length - rule));
			LogAssert.IsTrue(body.Contains("opacity: 0;"),
				"hidden until the row is hovered");

			LogAssert.IsTrue(style.Contains(".ability-entry:hover .ability-entry__forget"),
				"and revealed by the row's hover, so moving onto the button does not un-hover the row");
		}
	}
}
