using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Issue #208: an NPC interaction panel can open the inventory alongside itself, and the
	/// shop and bank do so by default.
	/// </summary>
	/// <remarks>
	/// Pinned at the source because the behaviour lives in a virtual hook on the base control
	/// and a serialized default on each panel — the two halves that a later edit would most
	/// easily drop one of. The rule itself: the inventory opens BEFORE the panel that asked for
	/// it, so the panel the player just opened is the one on top, and only on a real open, so a
	/// panel that is already visible cannot re-raise an inventory the player has since closed.
	/// </remarks>
	[TestFixture]
	public class InteractPanelInventoryTests
	{
		private const string BasePath = "Assets/Scripts/Client/GUI/UITKControl.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		[Test]
		public void TheBaseControlOpensTheInventoryBeforeItself_AndOnlyOnARealOpen()
		{
			string source = ReadSource(BasePath);

			int show = source.IndexOf("public virtual void Show()", StringComparison.Ordinal);
			LogAssert.IsTrue(show >= 0, "Show must exist");
			int end = source.IndexOf("protected virtual void OnAfterShow()", show, StringComparison.Ordinal);
			string body = source.Substring(show, end - show);

			int earlyReturn = body.IndexOf("if (Visible || Document == null)", StringComparison.Ordinal);
			int companion = body.IndexOf("ShowInventoryIfClosed();", StringComparison.Ordinal);
			int enable = body.IndexOf("Document.enabled = true;", StringComparison.Ordinal);
			int front = body.IndexOf("BringToFront();", StringComparison.Ordinal);

			LogAssert.IsTrue(earlyReturn >= 0 && companion > earlyReturn,
				"the inventory must open only after the already-visible early return");
			LogAssert.IsTrue(enable > companion && front > companion,
				"the inventory must open before this panel is enabled and brought to the front");
			LogAssert.IsTrue(source.Contains("protected virtual bool OpensInventoryOnShow => false;"),
				"the base default is off, so HUD elements and dialogs never open the inventory");
		}

		[Test]
		public void TheInventoryIsLeftAloneWhenAlreadyOpen()
		{
			string source = ReadSource(BasePath);
			int helper = source.IndexOf("protected static void ShowInventoryIfClosed()", StringComparison.Ordinal);
			LogAssert.IsTrue(helper >= 0, "the helper must exist");
			string body = source.Substring(helper, source.IndexOf("public virtual void Show()", helper, StringComparison.Ordinal) - helper);
			LogAssert.IsTrue(body.Contains("!inventory.Visible"),
				"an open inventory must not be touched — not hidden, not re-shown, not re-ordered");
		}

		[TestCase("Assets/Scripts/Client/GUI/World/Merchant/UITKMerchant.cs", true)]
		[TestCase("Assets/Scripts/Client/GUI/World/Bank/UITKBank.cs", true)]
		[TestCase("Assets/Scripts/Client/GUI/World/NPCDialogue/UITKNPCDialogue.cs", false)]
		[TestCase("Assets/Scripts/Client/GUI/World/Mail/UITKMail.cs", false)]
		[TestCase("Assets/Scripts/Client/GUI/World/Container/UITKContainer.cs", false)]
		[TestCase("Assets/Scripts/Client/GUI/World/Loot/UITKLoot.cs", false)]
		public void EveryInteractPanelHasTheToggle_WithTheAgreedDefault(string path, bool defaultOn)
		{
			string source = ReadSource(path);
			LogAssert.IsTrue(source.Contains($"private bool openInventoryOnShow = {(defaultOn ? "true" : "false")};"),
				$"{path} must expose the toggle with default {(defaultOn ? "on" : "off")} (shop and bank on, everything else off)");
			LogAssert.IsTrue(source.Contains("protected override bool OpensInventoryOnShow => openInventoryOnShow;"),
				$"{path} must hand its toggle to the base hook");
		}
	}
}
