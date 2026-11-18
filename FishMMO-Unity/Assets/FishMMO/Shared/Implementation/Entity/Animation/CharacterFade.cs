#if !UNITY_SERVER
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared
{
	/// <summary>
	/// Draws a character's model see-through: every Lit-style material on its mesh and skinned mesh renderers swapped for a
	/// transparent copy at a share of its own opacity, and put back on <see cref="Restore"/>. Used for a game master hidden by
	/// <c>/gm invis</c>, whom only they and staff still see.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>No new shader variant.</b> A copy keeps its original's keywords: FishMMO/Weather Lit (and URP Lit) takes its blend
	/// and depth-write state from material properties (<c>_SrcBlend</c>, <c>_DstBlend</c>, <c>_ZWrite</c>) and writes alpha
	/// from <c>_Surface</c> at run time (<c>OutputAlpha(…, IsSurfaceTypeTransparent(_Surface))</c>). The
	/// <c>_SURFACE_TYPE_TRANSPARENT</c> keyword only steers screen-space shadows, SSAO, decals and screen-space GI away from
	/// the opaque depth, none of which this project's renderers use, so leaving it off draws the same picture with a variant
	/// the build already holds (shader_feature variants exist only for keyword sets some built material uses).
	/// </para>
	/// <para>
	/// <b>Idempotent.</b> <see cref="Apply"/> may be called again at any time: renderers added since (equipment), or whose
	/// materials were replaced (an armour swap), are faded then, and those already faded are left alone. A copy is made once per
	/// original material and shared by every renderer that used it.
	/// </para>
	/// </remarks>
	internal sealed class CharacterFade
	{
		private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
		private static readonly int BlendId = Shader.PropertyToID("_Blend");
		private static readonly int PreserveSpecularId = Shader.PropertyToID("_BlendModePreserveSpecular");
		private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
		private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
		private static readonly int SrcBlendAlphaId = Shader.PropertyToID("_SrcBlendAlpha");
		private static readonly int DstBlendAlphaId = Shader.PropertyToID("_DstBlendAlpha");
		private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
		private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
		private static readonly int ColorId = Shader.PropertyToID("_Color");

		private static readonly List<Renderer> scratchRenderers = new List<Renderer>();
		private static readonly List<Material> scratchMaterials = new List<Material>();

		/// <summary>Original material → its faded copy.</summary>
		private readonly Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
		/// <summary>Faded copy → the original it was made from.</summary>
		private readonly Dictionary<Material, Material> originals = new Dictionary<Material, Material>();
		/// <summary>Every renderer given a faded copy, to put its materials back.</summary>
		private readonly HashSet<Renderer> touched = new HashSet<Renderer>();

		/// <summary>Whether anything is faded now.</summary>
		public bool IsApplied => touched.Count > 0;

		/// <summary>Fades every mesh and skinned mesh renderer under <paramref name="root"/> to <paramref name="opacity"/> of its own.</summary>
		public void Apply(Transform root, float opacity)
		{
			if (root == null)
			{
				return;
			}
			root.GetComponentsInChildren(true, scratchRenderers);
			for (int r = 0; r < scratchRenderers.Count; r++)
			{
				Renderer renderer = scratchRenderers[r];
				if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
				{
					// Particles, trails and lines keep their own look.
					continue;
				}
				renderer.GetSharedMaterials(scratchMaterials);
				bool changed = false;
				for (int i = 0; i < scratchMaterials.Count; i++)
				{
					Material faded = FadedOf(scratchMaterials[i], opacity);
					if (!ReferenceEquals(faded, scratchMaterials[i]))
					{
						scratchMaterials[i] = faded;
						changed = true;
					}
				}
				if (changed)
				{
					renderer.sharedMaterials = scratchMaterials.ToArray();
					touched.Add(renderer);
				}
			}
			scratchRenderers.Clear();
			scratchMaterials.Clear();
		}

		/// <summary>Puts every faded renderer's own materials back and destroys the copies.</summary>
		public void Restore()
		{
			foreach (Renderer renderer in touched)
			{
				if (renderer == null)
				{
					continue;
				}
				renderer.GetSharedMaterials(scratchMaterials);
				bool changed = false;
				for (int i = 0; i < scratchMaterials.Count; i++)
				{
					Material material = scratchMaterials[i];
					if (material != null && originals.TryGetValue(material, out Material original))
					{
						scratchMaterials[i] = original;
						changed = true;
					}
				}
				if (changed)
				{
					renderer.sharedMaterials = scratchMaterials.ToArray();
				}
			}
			touched.Clear();
			scratchMaterials.Clear();
			foreach (Material copy in originals.Keys)
			{
				if (copy != null)
				{
					Object.Destroy(copy);
				}
			}
			originals.Clear();
			copies.Clear();
		}

		/// <summary>The faded copy of <paramref name="material"/>, or the material itself when it is one, or cannot be faded.</summary>
		private Material FadedOf(Material material, float opacity)
		{
			if (material == null || originals.ContainsKey(material))
			{
				return material;
			}
			if (copies.TryGetValue(material, out Material copy) && copy != null)
			{
				return copy;
			}
			if (!material.HasProperty(SurfaceId))
			{
				// Not a Lit-style material: no property-driven transparency to switch on.
				return material;
			}
			copy = MakeFaded(material, opacity);
			copies[material] = copy;
			originals[copy] = material;
			return copy;
		}

		/// <summary>A copy of <paramref name="original"/> drawn alpha-blended in the transparent queue at <paramref name="opacity"/> of its own.</summary>
		internal static Material MakeFaded(Material original, float opacity)
		{
			var copy = new Material(original)
			{
				name = original.name + " (Faded)",
				hideFlags = HideFlags.DontSave,
			};
			// URP's Alpha blend mode without preserved specular: straight alpha, no premultiply keyword (see the remarks).
			copy.SetFloat(SurfaceId, 1f);
			if (copy.HasProperty(BlendId)) copy.SetFloat(BlendId, 0f);
			if (copy.HasProperty(PreserveSpecularId)) copy.SetFloat(PreserveSpecularId, 0f);
			if (copy.HasProperty(SrcBlendId)) copy.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
			if (copy.HasProperty(DstBlendId)) copy.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
			if (copy.HasProperty(SrcBlendAlphaId)) copy.SetFloat(SrcBlendAlphaId, (float)BlendMode.One);
			if (copy.HasProperty(DstBlendAlphaId)) copy.SetFloat(DstBlendAlphaId, (float)BlendMode.OneMinusSrcAlpha);
			if (copy.HasProperty(ZWriteId)) copy.SetFloat(ZWriteId, 0f);
			copy.DisableKeyword("_ALPHAPREMULTIPLY_ON");
			copy.DisableKeyword("_ALPHAMODULATE_ON");
			copy.SetOverrideTag("RenderType", "Transparent");
			copy.renderQueue = (int)RenderQueue.Transparent;
			ScaleAlpha(copy, BaseColorId, opacity);
			ScaleAlpha(copy, ColorId, opacity);
			return copy;
		}

		private static void ScaleAlpha(Material material, int id, float opacity)
		{
			if (material.HasProperty(id))
			{
				Color color = material.GetColor(id);
				color.a *= Mathf.Clamp01(opacity);
				material.SetColor(id, color);
			}
		}
	}
}
#endif
