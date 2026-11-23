using System;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The Travel module's client (issue #241): asks a summoned party member whether to answer a Call to Arms.
	/// </summary>
	/// <remarks>
	/// <see cref="GateController.OnCallToArms"/> brings the summons; this shows it in the shared dialog box with Go / Stay.
	/// Staying sends nothing. When the dialog is already asking something else, the summons is put in chat instead and
	/// declined, so a prompt never stacks over another question.
	/// </remarks>
	public static class CallToArmsPrompt
	{
		/// <summary>The shared dialog box's panel name.</summary>
		public const string DialogName = "UIDialogBox";

		/// <summary>Subscribes once. Runs before the first scene loads; safe to run again.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			GateController.OnCallToArms -= Show;
			GateController.OnCallToArms += Show;
		}

		/// <summary>The question the member is asked.</summary>
		public static string Question(string casterName, string sceneName, float seconds)
		{
			string who = string.IsNullOrWhiteSpace(casterName) ? "A party member" : casterName;
			string where = string.IsNullOrWhiteSpace(sceneName) ? string.Empty : $" in {sceneName}";
			return $"{who} calls you to arms{where}. Go to their side? ({Mathf.CeilToInt(Mathf.Max(0.0f, seconds))} s)";
		}

		private static void Show(string casterName, string sceneName, float seconds, Action accept)
		{
			string question = Question(casterName, sceneName, seconds);
			if (!UIManager.TryGetTK(DialogName, out UITKDialogBox dialog) ||
				!dialog.Open(question, accept, null, "GO", "STAY"))
			{
				ChatWindow.ShowSystem($"{question} The summons was declined: another question was open.");
			}
		}
	}
}
