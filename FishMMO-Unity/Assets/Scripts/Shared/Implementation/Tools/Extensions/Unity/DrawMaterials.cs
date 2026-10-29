using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Runtime copies of a shared material, one per draw slot, for the procedural draws (<see cref="Graphics.RenderMesh"/>
	/// and its kin) that need their own numbers on every draw without a <see cref="MaterialPropertyBlock"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> A property block takes its draw out of the SRP Batcher: URP then sets up that draw's whole shader state on
	/// the CPU (StdRender.ApplyShader in the profiler). A draw of a material of its own stays in the batcher, which uploads
	/// only the material's changed constant buffer and binds its textures.
	/// </para>
	/// <para>
	/// <b>What the shader must do.</b> Every per-draw number lives in the shader's <c>UnityPerMaterial</c> constant buffer
	/// (the same layout in every pass, with every non-texture property of its Properties block): under the batcher a
	/// material's value for a loose uniform is not what the draw reads. Textures and buffers set on a copy bind as they
	/// would from a block.
	/// </para>
	/// <para>
	/// <b>A copy keeps what was set on it.</b> A slot's copy is made the first time it is asked for, and refreshed from its
	/// source when the source changes (another material, another shader, or in the editor an edit to the source material,
	/// seen through its dirty count). Between refreshes it keeps every value last set, where a cleared block fell back to the
	/// material: set every per-draw value on every draw, and where a value was set only on a condition, set the source's own
	/// value otherwise.
	/// </para>
	/// <para>
	/// <b>One slot per draw in flight.</b> A block was copied when its draw was queued; a material is read when the camera
	/// renders. So two draws of one camera that differ need two slots, while one draw per camera may reuse its slot for the
	/// next camera (each camera's hook runs after the previous camera has rendered).
	/// </para>
	/// Allocates only when a slot is first used or the slot count grows. Main thread only.
	/// </remarks>
	public sealed class DrawMaterials
	{
		private readonly string label;
		private Material[] copies = System.Array.Empty<Material>();
		private Material[] sources = System.Array.Empty<Material>();
		private Shader[] shaders = System.Array.Empty<Shader>();
#if UNITY_EDITOR
		private int[] dirtyCounts = System.Array.Empty<int>();
#endif

		/// <param name="label">Put in each copy's name, for the frame debugger.</param>
		public DrawMaterials(string label)
		{
			this.label = label;
		}

		/// <summary>The slots made so far (the highest slot asked for, plus one).</summary>
		public int Count => copies.Length;

		/// <summary>
		/// The copy of <paramref name="source"/> for <paramref name="slot"/>, made or refreshed as needed; null when
		/// <paramref name="source"/> is null.
		/// </summary>
		public Material For(Material source, int slot = 0)
		{
			if (source == null || slot < 0)
			{
				return null;
			}
			if (slot >= copies.Length)
			{
				Grow(slot + 1);
			}
			Material copy = copies[slot];
			if (copy == null)
			{
				copy = new Material(source)
				{
					name = $"{source.name} ({label} {slot})",
					hideFlags = HideFlags.HideAndDontSave,
				};
				copies[slot] = copy;
				Remember(slot, source);
				return copy;
			}
			if (!ReferenceEquals(sources[slot], source) || !ReferenceEquals(shaders[slot], source.shader)
#if UNITY_EDITOR
				|| dirtyCounts[slot] != UnityEditor.EditorUtility.GetDirtyCount(source)
#endif
				)
			{
				if (!ReferenceEquals(copy.shader, source.shader))
				{
					copy.shader = source.shader;
				}
				copy.CopyPropertiesFromMaterial(source);
				Remember(slot, source);
			}
			return copy;
		}

		/// <summary>Destroys every copy.</summary>
		public void Dispose()
		{
			for (int i = 0; i < copies.Length; i++)
			{
				if (copies[i] != null)
				{
					if (Application.isPlaying)
					{
						Object.Destroy(copies[i]);
					}
					else
					{
						Object.DestroyImmediate(copies[i]);
					}
				}
				copies[i] = null;
				sources[i] = null;
				shaders[i] = null;
			}
		}

		private void Remember(int slot, Material source)
		{
			sources[slot] = source;
			shaders[slot] = source.shader;
#if UNITY_EDITOR
			dirtyCounts[slot] = UnityEditor.EditorUtility.GetDirtyCount(source);
#endif
		}

		private void Grow(int count)
		{
			System.Array.Resize(ref copies, count);
			System.Array.Resize(ref sources, count);
			System.Array.Resize(ref shaders, count);
#if UNITY_EDITOR
			System.Array.Resize(ref dirtyCounts, count);
#endif
		}
	}
}
