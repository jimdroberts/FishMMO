using FishNet.Component.Observing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A <see cref="DistanceCondition"/> that also declares what kind of thing it is gating and how
	/// many of that kind one viewer may observe at a time.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One asset per classification is the whole configuration surface.</b> Adding a kind of
	/// entity to the interest-management system is: create one of these under
	/// <c>Assets/Settings/ObserverConditions</c>, pick its <see cref="Classification"/>, set its
	/// distance and its budget, and reference it from the prefab's <c>NetworkObserver</c>. Nothing
	/// in code needs to learn the new kind — the registry reads the classification off the object's
	/// own condition list.
	/// </para>
	/// <para>
	/// <b>Range and budget are different questions and both are needed.</b> The range answers "how
	/// far can this be seen from", and alone it bounds nothing: a hundred players can stand inside
	/// any radius you pick. The budget answers "how many of these may one client hold at once", and
	/// alone it says nothing about where they are. Keeping both on one asset is what makes the pair
	/// legible — a reviewer can see that Titans reach 1000 m and are never culled, while dropped
	/// items reach 40 m and are capped, without cross-referencing two files.
	/// </para>
	/// <para>
	/// <b>The grid still applies unless the prefab opts out.</b> FishNet's <c>GridCondition</c> is
	/// ANDed with this one and rejects anything more than <c>HashGrid._accuracy</c> away on an axis,
	/// so a range beyond half the accuracy is dead configuration — the trap that once made a 100 m
	/// player range really 35–70 m. A classification that needs to outrun the grid (Titan) must be
	/// on a prefab whose <c>NetworkObserver</c> is set to <c>IgnoreManager</c> and which declares
	/// its own conditions; raising the global accuracy instead would make the grid a no-op for
	/// every other object in the world.
	/// <c>PredictionAuditRegressionTests.HashGridAccuracy_CannotClipTheDistanceConditions</c>
	/// enforces this per prefab.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(menuName = "FishMMO/Observers/Classified Distance Condition", fileName = "NewClassifiedDistanceCondition")]
	public class ClassifiedDistanceCondition : DistanceCondition
	{
		[Header("Classification")]
		[Tooltip("What kind of entity this condition gates. Each kind is budgeted separately.")]
		[SerializeField]
		private ObserverClassification classification = ObserverClassification.Monster;

		/// <summary>What kind of entity this condition gates.</summary>
		public ObserverClassification Classification => classification;

		[Header("Visibility budget")]
		[Tooltip("How many objects of this classification one client may observe at once. " +
			"0 means unlimited — use it for kinds that must never be culled, such as Titans. " +
			"Overridden at runtime by the ObserverVisibilityBudgets server configuration key.")]
		[Min(0)]
		[SerializeField]
		private int visibilityBudget = 40;

		/// <summary>
		/// How many objects of this classification one client may observe at once, after any server
		/// configuration override. 0 means unlimited.
		/// </summary>
		/// <remarks>
		/// The authored value is the default and the server key is the override, in the same
		/// direction as every other tunable here: designers set what the game should be, operators
		/// adjust what a particular deployment can afford. Read through
		/// <see cref="ObserverStreamingPolicy.ResolveVisibilityBudget"/> so both paths agree.
		/// </remarks>
		public int VisibilityBudget =>
			ObserverStreamingPolicy.ResolveVisibilityBudget(classification, visibilityBudget);

		/// <summary>The budget exactly as authored on this asset, ignoring any server override.</summary>
		public int AuthoredVisibilityBudget => visibilityBudget;
	}
}
