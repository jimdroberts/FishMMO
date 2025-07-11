﻿using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public static class TransformExtensions
	{
		private const int BONE_COMPONENT_COUNT = 1;//bones should only contain a transform component...

		/// <summary>
		/// Searches the GameObject's hierarchy and looks for a root bone.
		/// A root bone is assumed to be a GameObject with only a Transform component.
		/// Returns null if no root is found.
		/// </summary>
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

		public static Dictionary<string, Transform> GetBones(this Transform transform)
		{
			Stack<Transform> stack = new Stack<Transform>();
			Dictionary<string, Transform> bones = new Dictionary<string, Transform>();

			Transform root = GetRootBone(transform);
			if (root != null)
			{
				stack.Push(root);

				//iterate children
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

				// Push all children to the stack
				foreach (Transform child in current)
				{
					stack.Push(child);
				}
			}

			return foundObjects;
		}
	}
}