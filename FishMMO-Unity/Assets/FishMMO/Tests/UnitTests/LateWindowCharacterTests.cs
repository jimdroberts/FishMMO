using System;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A character window that registers after world entry is handed the character in play (2026-11-17).
	/// </summary>
	/// <remarks>
	/// Module windows (ModulePanelHost) are placed after an Addressables load that can finish after the player spawned.
	/// UIManager.SetCharacter had already walked the registered windows, so such a window kept a null character for the
	/// session: party and guild invitations (subscribed in OnPostSetCharacter) never arrived, and the trade window dropped
	/// requests while the server kept them pending ("already has a pending trade request" on the next try).
	/// </remarks>
	[TestFixture]
	public class LateWindowCharacterTests
	{
		private static string Code(string path)
		{
			var kept = new System.Text.StringBuilder();
			foreach (string line in ProjectFiles.ReadAllText(path).Replace("\r\n", "\n").Split('\n'))
			{
				string t = line.TrimStart();
				if (!(t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal)))
				{
					kept.Append(line).Append('\n');
				}
			}
			return kept.ToString();
		}

		[Test]
		public void UIManager_RemembersTheCharacter_AndHandsItToALateWindow()
		{
			string ui = Code("Assets/FishMMO/Client/GUI/Core/UIManager.cs");
			LogAssert.IsTrue(ui.Contains("currentCharacter = character;") && ui.Contains("currentCharacter = null;"),
				"SetCharacter records the character in play and UnsetCharacter clears it");
			int register = ui.IndexOf("internal static void RegisterTK(", StringComparison.Ordinal);
			int unregister = ui.IndexOf("internal static void UnregisterTK(", StringComparison.Ordinal);
			string body = ui.Substring(register, unregister - register);
			LogAssert.IsTrue(body.Contains("characterControl.SetCharacter(currentCharacter)"),
				"a character window registering after world entry is handed the character, as it is handed the client");
		}

		[Test]
		public void AHudPanelShownBeforeItRegistered_IsShownWhenItDoes()
		{
			/* The spawn shows the HUD by name (PlayerInputController.OnEnable: the chat, the hotkey bar, the minimap). Those
			 * are module panels now, placed by ModulePanelHost after an Addressables load that can finish after the spawn;
			 * they start hidden, so a Show that missed them left them hidden for the session. */
			string ui = Code("Assets/FishMMO/Client/GUI/Core/UIManager.cs");
			int show = ui.IndexOf("public static void Show(string name)", StringComparison.Ordinal);
			int apply = ui.IndexOf("internal static void ApplyPendingShow(UITKControl control)", StringComparison.Ordinal);
			LogAssert.IsTrue(show >= 0 && apply > show && ui.Substring(show, apply - show).Contains("pendingShows.Add(name);"),
				"a Show that finds no control remembers the name");
			int hide = ui.IndexOf("public static void Hide(string name)", StringComparison.Ordinal);
			LogAssert.IsTrue(hide >= 0 && ui.IndexOf("pendingShows.Remove(name);", hide, StringComparison.Ordinal) > hide,
				"a Hide of the name cancels it");
			LogAssert.IsTrue(ui.Contains("pendingShows.Clear();"), "and the character leaving clears them all");

			string control = Code("Assets/FishMMO/Client/GUI/UITKControl.cs");
			int awake = control.IndexOf("private void Awake()", StringComparison.Ordinal);
			int startOpen = control.IndexOf("if (!StartOpen)", awake, StringComparison.Ordinal);
			int applied = control.IndexOf("UIManager.ApplyPendingShow(this);", awake, StringComparison.Ordinal);
			LogAssert.IsTrue(awake >= 0 && startOpen > awake && applied > startOpen,
				"the panel applies a pending Show after its own StartOpen hide, which would otherwise undo it");
		}

		[Test]
		public void TheTradeWindow_DeclinesARequestItCannotShow_RatherThanDroppingIt()
		{
			const string path = "Assets/FishMMO/Modules/Commerce/Client/UITKTrade.cs";
			if (!ProjectFiles.Exists(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path)))
			{
				Assert.Ignore("The Commerce module is not installed.");
			}
			string code = Code(path);
			int handler = code.IndexOf("private void OnClientTradeInviteReceived(", StringComparison.Ordinal);
			int guard = code.IndexOf("if (Character == null)", handler, StringComparison.Ordinal);
			int decline = code.IndexOf("AnswerInvite(requesterID, false);", guard, StringComparison.Ordinal);
			int ret = code.IndexOf("return;", guard, StringComparison.Ordinal);
			LogAssert.IsTrue(guard > handler && decline > guard && ret > decline,
				"with no character the request is declined, so the requester is answered and the server frees the slot");
		}
	}
}
