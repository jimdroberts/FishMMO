using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Composite node that wraps a list of children. Base class for <see cref="AISelector"/>
	/// and <see cref="AISequence"/>.
	/// </summary>
	public abstract class AICompositeNode : AIBehaviorNode
	{
		/// <summary>
		/// Child nodes to evaluate. Order matters — see subclass documentation.
		/// </summary>
		[Tooltip("Ordered child nodes.")]
		public AIBehaviorNode[] Children;
	}
}