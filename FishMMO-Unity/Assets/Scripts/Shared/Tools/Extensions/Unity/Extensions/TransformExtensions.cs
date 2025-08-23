using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for Unity Transforms, including bone hierarchy and child GameObject search utilities.
	/// </summary>
	public static class TransformExtensions
	{
		/// <summary>
		/// The number of components expected for a bone (should only contain a Transform).
		/// </summary>
		private const int BONE_COMPONENT_COUNT = 1; // bones should only contain a transform component...

		/// <summary>
		/// Searches the GameObject's hierarchy and looks for a root bone.
		/// A root bone is assumed to be a GameObject with only a Transform component.
		/// Returns null if no root is found.
		/// </summary>
		/// <param name="gameObject">The transform to search for a root bone.</param>
		/// <returns>The root bone Transform, or null if not found.</returns>
		public static Transform GetRootBone(this Transform gameObject)
		{
			foreach (Transform child in gameObject.transform)
			{
				Component[] components = child.GetComponents<Component>();
				if (components.Length == BONE_COMPONENT_COUNT && components[0] is Transform)
				{
					return child;
				}
			}
			return null;
		}

		/// <summary>
		/// Recursively collects all bones in the hierarchy starting from the root bone.
		/// Only includes transforms with exactly one component (Transform).
		/// </summary>
		/// <param name="transform">The transform to search for bones.</param>
		/// <returns>Dictionary mapping bone names to their Transform objects.</returns>
		public static Dictionary<string, Transform> GetBones(this Transform transform)
		{
			Stack<Transform> stack = new Stack<Transform>();
			Dictionary<string, Transform> bones = new Dictionary<string, Transform>();

			Transform root = GetRootBone(transform);
			if (root != null)
			{
				stack.Push(root);

				// Iterate children using a stack for depth-first traversal
				while (stack.Count > 0)
				{
					Transform current = stack.Pop();
					bones.Add(current.name, current);
					foreach (Transform child in current)
					{
						Component[] components = child.GetComponents<Component>();
						if (components.Length == BONE_COMPONENT_COUNT && components[0] is Transform)
						{
							stack.Push(child);
						}
					}
				}
			}
			return bones;
		}

		/// <summary>
		/// Finds all child GameObjects in the hierarchy, optionally filtering by name prefix.
		/// </summary>
		/// <param name="root">The root transform to start searching from.</param>
		/// <param name="prefix">Optional name prefix to filter child GameObjects.</param>
		/// <returns>List of found child GameObjects.</returns>
		public static List<GameObject> FindAllChildGameObjects(this Transform root, string prefix = null)
		{
			List<GameObject> foundObjects = new List<GameObject>();
			Stack<Transform> stack = new Stack<Transform>();
			stack.Push(root);

			while (stack.Count > 0)
			{
				Transform current = stack.Pop();

				// Check if the current object's name matches the prefix
				if (string.IsNullOrWhiteSpace(prefix) ||
					current.gameObject.name.StartsWith(prefix))
				{
					foundObjects.Add(current.gameObject);
				}

				// Push all children to the stack for traversal
				foreach (Transform child in current)
				{
					stack.Push(child);
				}
			}

			return foundObjects;
		}
	}
}