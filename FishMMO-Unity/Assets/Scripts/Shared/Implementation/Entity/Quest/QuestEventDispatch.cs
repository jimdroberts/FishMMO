using System;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Raises the <see cref="IQuestController"/> events so that no handler can throw into whatever
	/// raised them.
	/// </summary>
	/// <remarks>
	/// <para>Quest events are raised from the middle of other systems' work. An objective advances
	/// from an ECA action on a kill or a loot trigger, so a handler that threw — a quest template
	/// with a broken objective, a reward that no longer resolves — used to unwind straight through
	/// the damage or loot path that raised it, and a single broken quest could stop combat or
	/// looting for everyone who had it.</para>
	///
	/// <para>Each subscriber is called in its own try/catch, so one that throws neither escapes nor
	/// stops the next. What happened is reported by quest to <see cref="Reporter"/>, which the
	/// server's quest system installs: it keeps one fault log per quest, so a quest that throws on
	/// every kill is summarised rather than flooding the log, and another quest's first fault still
	/// arrives in full. Without a reporter a fault is logged in full.</para>
	///
	/// <para>Main thread only, like the events themselves.</para>
	/// </remarks>
	public static class QuestEventDispatch
	{
		/// <summary>
		/// Told how one quest's handlers ended: with the exception a handler threw (once per
		/// throwing handler), or with null when every handler returned.
		/// </summary>
		public static Action<string, Exception> Reporter;

		/// <summary>Raises an event carrying a quest template.</summary>
		public static void Raise(Action<ICharacter, QuestTemplate> handlers, ICharacter character, QuestTemplate template, Action<string, Exception> report)
		{
			if (handlers == null)
			{
				return;
			}

			string questName = template != null ? template.Name : null;
			Delegate[] subscribers = handlers.GetInvocationList();
			bool faulted = false;
			for (int i = 0; i < subscribers.Length; ++i)
			{
				try
				{
					((Action<ICharacter, QuestTemplate>)subscribers[i])(character, template);
				}
				catch (Exception ex)
				{
					faulted = true;
					Deliver(report, questName, ex);
				}
			}
			if (!faulted)
			{
				Deliver(report, questName, null);
			}
		}

		/// <summary>Raises an event carrying a quest name.</summary>
		public static void Raise(Action<ICharacter, string> handlers, ICharacter character, string questName, Action<string, Exception> report)
		{
			if (handlers == null)
			{
				return;
			}

			Delegate[] subscribers = handlers.GetInvocationList();
			bool faulted = false;
			for (int i = 0; i < subscribers.Length; ++i)
			{
				try
				{
					((Action<ICharacter, string>)subscribers[i])(character, questName);
				}
				catch (Exception ex)
				{
					faulted = true;
					Deliver(report, questName, ex);
				}
			}
			if (!faulted)
			{
				Deliver(report, questName, null);
			}
		}

		/// <summary>Raises an objective event.</summary>
		public static void Raise(Action<ICharacter, string, int, long> handlers, ICharacter character, string questName, int objectiveIndex, long amount, Action<string, Exception> report)
		{
			if (handlers == null)
			{
				return;
			}

			Delegate[] subscribers = handlers.GetInvocationList();
			bool faulted = false;
			for (int i = 0; i < subscribers.Length; ++i)
			{
				try
				{
					((Action<ICharacter, string, int, long>)subscribers[i])(character, questName, objectiveIndex, amount);
				}
				catch (Exception ex)
				{
					faulted = true;
					Deliver(report, questName, ex);
				}
			}
			if (!faulted)
			{
				Deliver(report, questName, null);
			}
		}

		/// <summary>
		/// Hands one outcome to the reporter. Nothing escapes: a reporter that throws is logged,
		/// and so is a fault with no reporter to take it.
		/// </summary>
		private static void Deliver(Action<string, Exception> report, string questName, Exception fault)
		{
			if (report == null)
			{
				if (fault != null)
				{
					Log.Error("QuestEventDispatch", $"A handler for quest '{questName}' threw: {fault}");
				}
				return;
			}

			try
			{
				report(questName, fault);
			}
			catch (Exception reporterFault)
			{
				Log.Error("QuestEventDispatch", $"The quest fault reporter threw for quest '{questName}': {reporterFault}" +
					(fault != null ? $"; the handler's fault was: {fault}" : string.Empty));
			}
		}
	}
}
