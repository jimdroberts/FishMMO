using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for how quest events are raised: no handler can throw into whatever raised the event.
	/// </summary>
	/// <remarks>
	/// Quest objectives advance from ECA actions on kills and loot, so a quest handler that threw —
	/// one broken quest template — used to unwind through the combat or loot path that raised it.
	/// </remarks>
	[TestFixture]
	public class QuestEventDispatchTests
	{
		private readonly List<(string Quest, Exception Fault)> reports = new List<(string, Exception)>();

		private void Report(string quest, Exception fault)
		{
			reports.Add((quest, fault));
		}

		[SetUp]
		public void SetUp()
		{
			reports.Clear();
		}

		[Test]
		public void AThrowingHandler_DoesNotEscapeTheDispatch()
		{
			Action<ICharacter, string> handlers = (character, quest) => throw new InvalidOperationException("broken quest");

			Assert.DoesNotThrow(() => QuestEventDispatch.Raise(handlers, null, "Rat Problem", Report));

			Assert.AreEqual(1, reports.Count);
			Assert.AreEqual("Rat Problem", reports[0].Quest, "faults are reported by quest, for its own fault log");
			Assert.IsInstanceOf<InvalidOperationException>(reports[0].Fault);
		}

		[Test]
		public void AThrowingHandler_DoesNotStopTheNextOne()
		{
			int reached = 0;
			Action<ICharacter, string, int, long> handlers = (character, quest, index, amount) => throw new NullReferenceException();
			handlers += (character, quest, index, amount) => reached++;

			QuestEventDispatch.Raise(handlers, null, "Rat Problem", 0, 1, Report);

			Assert.AreEqual(1, reached, "each subscriber is isolated from the others");
			Assert.AreEqual(1, reports.Count, "one fault, and no success report for a dispatch that faulted");
			Assert.IsNotNull(reports[0].Fault);
		}

		[Test]
		public void AClean_Dispatch_ReportsSuccessOnce()
		{
			int reached = 0;
			Action<ICharacter, string> handlers = (character, quest) => reached++;
			handlers += (character, quest) => reached++;

			QuestEventDispatch.Raise(handlers, null, "Rat Problem", Report);

			Assert.AreEqual(2, reached);
			Assert.AreEqual(1, reports.Count, "a success is reported once, so a quest's fault log can record its recovery");
			Assert.AreEqual("Rat Problem", reports[0].Quest);
			Assert.IsNull(reports[0].Fault);
		}

		[Test]
		public void NoSubscribers_ReportsNothing()
		{
			QuestEventDispatch.Raise((Action<ICharacter, string>)null, null, "Rat Problem", Report);

			Assert.AreEqual(0, reports.Count, "the client raises these with nobody listening; that is not a success to record");
		}

		[Test]
		public void AThrowingReporter_DoesNotEscapeEither()
		{
			Action<ICharacter, string> handlers = (character, quest) => throw new InvalidOperationException("broken quest");

			// The dispatch logs the reporter's fault as an error, which is the point here.
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			try
			{
				Assert.DoesNotThrow(() => QuestEventDispatch.Raise(handlers, null, "Rat Problem", (quest, fault) => throw new Exception("broken reporter")));
			}
			finally
			{
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
			}
		}

		[Test]
		public void TheAcceptedEvent_IsReportedUnderItsTemplatesName()
		{
			QuestTemplate template = ScriptableObject.CreateInstance<QuestTemplate>();
			try
			{
				template.name = "Rat Problem";
				Action<ICharacter, QuestTemplate> handlers = (character, accepted) => throw new InvalidOperationException();

				QuestEventDispatch.Raise(handlers, null, template, Report);

				Assert.AreEqual(1, reports.Count);
				Assert.AreEqual("Rat Problem", reports[0].Quest);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(template);
			}
		}

		/// <summary>
		/// Every event the controller raises goes through the dispatch; a direct invoke would put the
		/// unwinding back.
		/// </summary>
		[Test]
		public void TheController_NeverInvokesAQuestEventDirectly()
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Shared/Implementation/Entity/Quest/QuestController.cs");
			LogAssert.IsTrue(File.Exists(path), $"QuestController.cs not found at {path}.");
			string source = File.ReadAllText(path);

			LogAssert.IsFalse(source.Contains("?.Invoke(Character"),
				"QuestController must raise quest events through QuestEventDispatch, never invoke them directly");
			foreach (string evt in new[] { "OnQuestAccepted", "OnObjectiveUpdated", "OnQuestComplete", "OnQuestTurnedIn", "OnQuestFailed", "OnQuestAbandoned" })
			{
				LogAssert.IsTrue(source.Contains($"QuestEventDispatch.Raise(IQuestController.{evt},"),
					$"IQuestController.{evt} must be raised through QuestEventDispatch");
			}
		}
	}
}
