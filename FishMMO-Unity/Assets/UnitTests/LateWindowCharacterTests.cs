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
			string ui = Code("Assets/Scripts/Client/GUI/Core/UIManager.cs");
			LogAssert.IsTrue(ui.Contains("currentCharacter = character;") && ui.Contains("currentCharacter = null;"),
				"SetCharacter records the character in play and UnsetCharacter clears it");
			int register = ui.IndexOf("internal static void RegisterTK(", StringComparison.Ordinal);
			int unregister = ui.IndexOf("internal static void UnregisterTK(", StringComparison.Ordinal);
			string body = ui.Substring(register, unregister - register);
			LogAssert.IsTrue(body.Contains("characterControl.SetCharacter(currentCharacter)"),
				"a character window registering after world entry is handed the character, as it is handed the client");
		}

		[Test]
		public void TheTradeWindow_DeclinesARequestItCannotShow_RatherThanDroppingIt()
		{
			const string path = "Assets/Modules/Commerce/Client/UITKTrade.cs";
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
