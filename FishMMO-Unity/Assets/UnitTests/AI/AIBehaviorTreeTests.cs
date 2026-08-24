using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Proofs that a malformed behavior tree degrades instead of taking the server down.
	/// </summary>
	/// <remarks>
	/// Behavior tree nodes are ScriptableObject references, so nothing structurally prevents a node
	/// from being its own descendant. Evaluation is recursive, so a cycle is a stack overflow — and
	/// a stack overflow cannot be caught, it terminates the process. On a dedicated server that is
	/// every player in the zone disconnected. The editor now refuses to create such a connection,
	/// but assets can also be hand-edited or arrive from a bad merge, so the runtime refuses to
	/// trust the shape it is handed.
	/// </remarks>
	[TestFixture]
	public class AIBehaviorTreeTests
	{
		/// <summary>Nodes created by a test, destroyed afterwards.</summary>
		private System.Collections.Generic.List<ScriptableObject> created;

		/// <summary>
		/// Starts a fresh tracking list.
		/// </summary>
		[SetUp]
		public void SetUp()
		{
			created = new System.Collections.Generic.List<ScriptableObject>();
		}

		/// <summary>
		/// Destroys everything the test created so EditMode runs do not leak.
		/// </summary>
		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < created.Count; ++i)
			{
				if (created[i] != null)
				{
					Object.DestroyImmediate(created[i]);
				}
			}
			created.Clear();
		}

		/// <summary>
		/// Creates a tracked in-memory ScriptableObject.
		/// </summary>
		/// <typeparam name="T">Type to create.</typeparam>
		/// <returns>The new instance.</returns>
		private T Create<T>() where T : ScriptableObject
		{
			T instance = ScriptableObject.CreateInstance<T>();
			created.Add(instance);
			return instance;
		}

		[Test]
		public void EmptyTree_Fails()
		{
			AIBehaviorTree tree = Create<AIBehaviorTree>();

			Assert.AreEqual(AINodeResult.Failure, tree.Evaluate(null),
				"A tree with no root must fail cleanly so the state machine keeps running.");
		}

		[Test]
		public void SelectorWithNoChildren_Fails()
		{
			AIBehaviorTree tree = Create<AIBehaviorTree>();
			tree.Root = Create<AISelector>();

			Assert.AreEqual(AINodeResult.Failure, tree.Evaluate(null));
		}

		[Test]
		public void SelectorWithNullChildren_DoesNotThrow()
		{
			// An inspector list sized before its entries are filled in is normal, not an error.
			AISelector selector = Create<AISelector>();
			selector.Children = new AIBehaviorNode[] { null, null };

			AIBehaviorTree tree = Create<AIBehaviorTree>();
			tree.Root = selector;

			Assert.DoesNotThrow(() => tree.Evaluate(null));
		}

		[Test]
		public void DirectSelfCycle_DoesNotOverflowTheStack()
		{
			/* The simplest cycle: a decorator pointing at itself. Before the depth guard this was
			 * an immediate StackOverflowException, which .NET cannot catch — the process dies. */
			AIInverter inverter = Create<AIInverter>();
			inverter.Child = inverter;

			AIBehaviorTree tree = Create<AIBehaviorTree>();
			tree.Root = inverter;

			Assert.DoesNotThrow(() => tree.Evaluate(null),
				"A self-referencing node must be refused, not recursed into.");
		}

		[Test]
		public void IndirectCycle_DoesNotOverflowTheStack()
		{
			// Selector -> Inverter -> back to the Selector.
			AISelector selector = Create<AISelector>();
			AIInverter inverter = Create<AIInverter>();

			selector.Children = new AIBehaviorNode[] { inverter };
			inverter.Child = selector;

			AIBehaviorTree tree = Create<AIBehaviorTree>();
			tree.Root = selector;

			Assert.DoesNotThrow(() => tree.Evaluate(null));
		}

		[Test]
		public void CycleThroughARepeater_DoesNotOverflowTheStack()
		{
			AIRepeater repeater = Create<AIRepeater>();
			AISequence sequence = Create<AISequence>();

			repeater.RepeatCount = 1;
			repeater.Child = sequence;
			sequence.Children = new AIBehaviorNode[] { repeater };

			AIBehaviorTree tree = Create<AIBehaviorTree>();
			tree.Root = repeater;

			Assert.DoesNotThrow(() => tree.Evaluate(null));
		}

		[Test]
		public void DepthGuard_ReportsWhenItFires()
		{
			AIInverter inverter = Create<AIInverter>();
			inverter.Child = inverter;

			AIBehaviorTree tree = Create<AIBehaviorTree>();
			tree.Root = inverter;

			// The guard logs an error explaining the cycle; swallow it so the test can assert.
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			try
			{
				tree.Evaluate(null);
			}
			finally
			{
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
			}

			Assert.IsTrue(AIBehaviorNode.DepthExceeded,
				"Hitting the limit must be observable, not silently absorbed.");
		}

		[Test]
		public void DepthGuard_ResetsBetweenEvaluations()
		{
			/* The depth counter is static. A tree that trips the guard must not leave the counter
			 * raised, or the next NPC's perfectly valid tree would be refused as well. */
			AIInverter cyclic = Create<AIInverter>();
			cyclic.Child = cyclic;

			AIBehaviorTree badTree = Create<AIBehaviorTree>();
			badTree.Root = cyclic;

			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			try
			{
				badTree.Evaluate(null);
			}
			finally
			{
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
			}

			// A shallow, valid tree evaluated afterwards must behave normally.
			AISelector selector = Create<AISelector>();
			selector.Children = new AIBehaviorNode[0];

			AIBehaviorTree goodTree = Create<AIBehaviorTree>();
			goodTree.Root = selector;

			Assert.AreEqual(AINodeResult.Failure, goodTree.Evaluate(null));
			Assert.IsFalse(AIBehaviorNode.DepthExceeded,
				"A clean evaluation must clear the flag left by a previous bad one.");
		}

		[Test]
		public void DepthLimit_IsDeepEnoughForRealTrees()
		{
			Assert.GreaterOrEqual(AIBehaviorNode.MAX_EVALUATION_DEPTH, 32,
				"The limit must be far above any hand-authored tree, or it becomes a design constraint.");
		}
	}
}
