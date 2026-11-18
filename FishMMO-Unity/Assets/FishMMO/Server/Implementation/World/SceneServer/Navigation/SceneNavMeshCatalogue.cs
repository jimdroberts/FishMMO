using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.Server.Implementation.World.SceneServer.Navigation
{
	/// <summary>
	/// The baked NavMesh of every world scene, by scene name.
	/// </summary>
	/// <remarks>
	/// <para>
	/// NavMeshes ship to the scene server only. A scene keeps its <c>NavMeshSurface</c> for authoring (the bake
	/// settings, the bake button, the editor's view of the mesh), but builds and play mode strip the surface, so
	/// the scene never carries its data. The data reaches the server through this catalogue instead: it is
	/// referenced by <see cref="NavMeshSystem"/>, and both, with every NavMesh asset, are explicit entries in the
	/// server-only addressables group that client builds drop.
	/// </para>
	/// <para>
	/// The editor keeps it in step: a scene's entry is written when it is saved with a baked surface, and when a
	/// generated scene is baked.
	/// </para>
	/// </remarks>
	public class SceneNavMeshCatalogue : ScriptableObject
	{
		/// <summary>One scene's NavMesh, and the pose its surface added it at.</summary>
		[Serializable]
		public class Entry
		{
			/// <summary>The world scene's name.</summary>
			public string SceneName;

			/// <summary>The baked NavMesh.</summary>
			public NavMeshData Data;

			/// <summary>Where the surface stood: a surface adds its data at its own transform.</summary>
			public Vector3 Position;

			/// <summary>The surface's rotation.</summary>
			public Quaternion Rotation = Quaternion.identity;
		}

		/// <summary>One entry per world scene with a NavMesh.</summary>
		public List<Entry> Entries = new List<Entry>();

		/// <summary>Entries by scene name, built on first lookup.</summary>
		[NonSerialized]
		private Dictionary<string, Entry> byScene;

		/// <summary>The NavMesh baked for <paramref name="sceneName"/>, if there is one.</summary>
		public bool TryGet(string sceneName, out Entry entry)
		{
			entry = null;
			if (string.IsNullOrEmpty(sceneName))
			{
				return false;
			}
			if (byScene == null)
			{
				byScene = new Dictionary<string, Entry>(Entries.Count);
				for (int i = 0; i < Entries.Count; ++i)
				{
					Entry candidate = Entries[i];
					if (candidate != null && candidate.Data != null && !string.IsNullOrEmpty(candidate.SceneName))
					{
						byScene[candidate.SceneName] = candidate;
					}
				}
			}
			return byScene.TryGetValue(sceneName, out entry);
		}

		/// <summary>Drops the lookup after the entries change.</summary>
		public void Invalidate()
		{
			byScene = null;
		}

		private void OnValidate()
		{
			Invalidate();
		}
	}
}
