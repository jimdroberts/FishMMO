using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>What a staff test region does to whoever walks into it.</summary>
	public enum StaffTestRegionKind : byte
	{
		/// <summary>Only the "You have entered" name toast (every kind shows it too).</summary>
		Name = 0,
		/// <summary>A <see cref="BuffVolume"/>: the buff is on while inside, predicted on both peers, off on leaving.</summary>
		Buff = 1,
		/// <summary>A region-owned modifier on a non-resource attribute (<see cref="ApplyRegionAttributeAction"/>), released on leaving.</summary>
		Attribute = 2,
		/// <summary>A pulsed change to a resource's current value (<see cref="ApplyRegionAttributeAction"/> on stay): drain or regeneration.</summary>
		Resource = 3,
		/// <summary>Pulsed damage, or healing when negative (<see cref="ApplyRegionDamageAction"/> on stay): a poison mist.</summary>
		Damage = 4,
		/// <summary>Region fog (<see cref="ChangeFogAction"/>) on entering, cleared on leaving.</summary>
		Fog = 5,
	}

	/// <summary>Everything a staff test region is built from. Written into the spawn payload, so both peers build the same region.</summary>
	public struct StaffTestRegionSettings
	{
		public StaffTestRegionKind Kind;
		/// <summary>The box's full extent, metres (x wide, y tall, z deep).</summary>
		public Vector3 Size;
		/// <summary>The region's name: what the toast shows.</summary>
		public string Label;
		/// <summary>The buff, attribute, resource or damage type's template ID; 0 for none.</summary>
		public int TemplateID;
		/// <summary>The attribute bonus, or the resource change / damage per pulse.</summary>
		public int Amount;
		/// <summary>Ticks between pulses of the resource and damage kinds.</summary>
		public int PulseTicks;
		/// <summary>The fog kind's density.</summary>
		public float FogDensity;

		public void Write(Writer writer)
		{
			writer.WriteUInt8Unpacked((byte)Kind);
			writer.WriteVector3(Size);
			writer.WriteString(Label ?? string.Empty);
			writer.WriteInt32(TemplateID);
			writer.WriteInt32(Amount);
			writer.WriteInt32(PulseTicks);
			writer.WriteSingle(FogDensity);
		}

		public static StaffTestRegionSettings Read(Reader reader)
		{
			return new StaffTestRegionSettings
			{
				Kind = (StaffTestRegionKind)reader.ReadUInt8Unpacked(),
				Size = reader.ReadVector3(),
				Label = reader.ReadStringAllocated(),
				TemplateID = reader.ReadInt32(),
				Amount = reader.ReadInt32(),
				PulseTicks = reader.ReadInt32(),
				FogDensity = reader.ReadSingle(),
			};
		}
	}

	/// <summary>
	/// A <see cref="Region"/> a staff member puts down at run time (<c>/admin region</c>) to test what regions do: the
	/// name toast, a buff while inside, an attribute bonus, a resource drain, damage over time, fog.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Built from content's own parts.</b> Nothing here is a test-only path: the region is a real <see cref="Region"/>
	/// with a real <c>NetworkTrigger</c>, and its triggers carry the shipped actions (<see cref="DisplayRegionNameAction"/>,
	/// <see cref="ApplyRegionAttributeAction"/>, <see cref="ApplyRegionDamageAction"/>, <see cref="ChangeFogAction"/>) or,
	/// for a buff, a <see cref="BuffVolume"/>. What a test region shows is what an authored region in a scene would do.
	/// </para>
	/// <para>
	/// <b>Both peers build it.</b> The settings ride the spawn payload, and each peer builds the same box, name and
	/// triggers from them. The client needs its copy: the toast and fog run only on the owning client, and a buff volume is
	/// tested inside the prediction replicate on both sides, so a server-only volume would apply the buff the client never
	/// predicted. The gameplay actions gate themselves to the server (<see cref="RegionActionGate"/>), so building them on
	/// the client too is harmless.
	/// </para>
	/// <para>
	/// The prefab is spawned and destroyed, never pooled: the triggers made here belong to this instance alone.
	/// </para>
	/// </remarks>
	[RequireComponent(typeof(Region))]
	[DisallowMultipleComponent]
	public sealed class StaffTestRegion : NetworkBehaviour
	{
		/// <summary>Smallest box edge, metres.</summary>
		public const float MinimumEdge = 1f;
		/// <summary>Largest box edge, metres. Within the interest grid, so every client near enough to stand in it has it.</summary>
		public const float MaximumEdge = 200f;
		/// <summary>Longest name kept.</summary>
		public const int MaximumLabelLength = 48;

		/// <summary>The settings this region was built from; default until it is configured.</summary>
		public StaffTestRegionSettings Settings { get; private set; }

		/// <summary>True once built. A region is built once.</summary>
		public bool IsConfigured { get; private set; }

		private readonly List<Trigger> madeTriggers = new List<Trigger>();
		private Material outlineMaterial;
		private Material shellMaterial;
		private Mesh shellMesh;

		/// <summary>How opaque the filled box is: enough to see the volume, little enough to see through it from inside.</summary>
		public const float ShellAlpha = 0.12f;

		/// <summary>The colour a kind is drawn and announced in.</summary>
		public static Color ColorOf(StaffTestRegionKind kind, int amount = 0)
		{
			switch (kind)
			{
				case StaffTestRegionKind.Buff: return new Color(0.35f, 1f, 0.45f);
				case StaffTestRegionKind.Attribute: return new Color(0.4f, 0.65f, 1f);
				case StaffTestRegionKind.Resource: return new Color(0.3f, 0.95f, 0.95f);
				case StaffTestRegionKind.Damage: return amount < 0 ? new Color(0.75f, 1f, 0.35f) : new Color(1f, 0.3f, 0.25f);
				case StaffTestRegionKind.Fog: return new Color(0.75f, 0.75f, 0.8f);
				default: return new Color(1f, 0.9f, 0.5f);
			}
		}

		/// <summary>Clamps the box to its limits and the label to its length. Pure.</summary>
		public static StaffTestRegionSettings Sanitize(StaffTestRegionSettings settings)
		{
			settings.Size = new Vector3(
				Mathf.Clamp(float.IsNaN(settings.Size.x) ? MinimumEdge : settings.Size.x, MinimumEdge, MaximumEdge),
				Mathf.Clamp(float.IsNaN(settings.Size.y) ? MinimumEdge : settings.Size.y, MinimumEdge, MaximumEdge),
				Mathf.Clamp(float.IsNaN(settings.Size.z) ? MinimumEdge : settings.Size.z, MinimumEdge, MaximumEdge));
			string label = string.IsNullOrWhiteSpace(settings.Label) ? "Test Region" : settings.Label.Trim();
			settings.Label = label.Length > MaximumLabelLength ? label.Substring(0, MaximumLabelLength) : label;
			settings.PulseTicks = Mathf.Max(1, settings.PulseTicks);
			settings.FogDensity = Mathf.Clamp(float.IsNaN(settings.FogDensity) ? 0.05f : settings.FogDensity, 0.001f, 1f);
			return settings;
		}

		/// <summary>
		/// Builds the region from <paramref name="settings"/>: the server before it spawns it, the client from the payload.
		/// </summary>
		public void Configure(StaffTestRegionSettings settings)
		{
			if (IsConfigured)
			{
				return;
			}
			IsConfigured = true;
			settings = Sanitize(settings);
			Settings = settings;

			gameObject.name = settings.Label;

			Region region = GetComponent<Region>();
			BoxCollider box = GetComponent<BoxCollider>();
			if (region == null || box == null)
			{
				Log.Error("StaffTestRegion", "The test region prefab needs a Region and a BoxCollider.");
				return;
			}
			box.isTrigger = true;
			box.center = Vector3.zero;
			box.size = settings.Size;

			// Every kind announces itself, so the moment the region sees the character walk in is on screen.
			Trigger toast = MakeTrigger("Name");
			toast.OnConditionsMetActions.Add(new DisplayRegionNameAction
			{
				DisplayColor = ColorOf(settings.Kind, settings.Amount),
				Style = FontStyle.Bold,
				FontSize = 24,
				LifeTime = 2.5f,
				FadeColor = true,
			});
			region.OnRegionEnter.Add(toast);

			switch (settings.Kind)
			{
				case StaffTestRegionKind.Buff:
					if (TryGetTemplate(settings.TemplateID, out BaseBuffTemplate buff))
					{
						BuffVolume volume = gameObject.AddComponent<BuffVolume>();
						volume.Shape = box;
						volume.Buff = buff;
					}
					break;

				case StaffTestRegionKind.Attribute:
					if (TryGetTemplate(settings.TemplateID, out CharacterAttributeTemplate attribute))
					{
						Trigger enter = MakeTrigger("Attribute");
						enter.OnConditionsMetActions.Add(new ApplyRegionAttributeAction { Attribute = attribute, Value = settings.Amount });
						region.OnRegionEnter.Add(enter);
					}
					break;

				case StaffTestRegionKind.Resource:
					if (TryGetTemplate(settings.TemplateID, out CharacterAttributeTemplate resource))
					{
						Trigger stay = MakeTrigger("Resource");
						stay.Conditions.Add(new TickIntervalCondition { IntervalTicks = settings.PulseTicks });
						stay.OnConditionsMetActions.Add(new ApplyRegionAttributeAction { Attribute = resource, Value = settings.Amount });
						region.OnRegionStay.Add(stay);
					}
					break;

				case StaffTestRegionKind.Damage:
				{
					// A damage type is optional: without one the damage is true damage.
					TryGetTemplate(settings.TemplateID, out DamageAttributeTemplate damageType);
					Trigger stay = MakeTrigger("Damage");
					stay.Conditions.Add(new TickIntervalCondition { IntervalTicks = settings.PulseTicks });
					stay.OnConditionsMetActions.Add(new ApplyRegionDamageAction { Amount = settings.Amount, DamageType = damageType });
					region.OnRegionStay.Add(stay);
					break;
				}

				case StaffTestRegionKind.Fog:
				{
					Trigger enter = MakeTrigger("Fog On");
					enter.OnConditionsMetActions.Add(new ChangeFogAction
					{
						FogSettings = new FogSettings
						{
							Enabled = true,
							ChangeRate = 1.5f,
							Mode = FogMode.Exponential,
							Color = new Color(0.55f, 0.6f, 0.5f),
							Density = settings.FogDensity,
						},
					});
					region.OnRegionEnter.Add(enter);
					Trigger exit = MakeTrigger("Fog Off");
					exit.OnConditionsMetActions.Add(new ChangeFogAction { FogSettings = new FogSettings { Enabled = false, ChangeRate = 1.5f } });
					region.OnRegionExit.Add(exit);
					break;
				}
			}
		}

		/// <inheritdoc />
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			base.WritePayload(connection, writer);
			Settings.Write(writer);
		}

		/// <inheritdoc />
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			base.ReadPayload(connection, reader);
			// Read whatever happens, so the behaviours after this one in the payload are not misread.
			StaffTestRegionSettings settings = StaffTestRegionSettings.Read(reader);
			Configure(settings);
		}

		/// <inheritdoc />
		public override void OnStartClient()
		{
			base.OnStartClient();
#if !UNITY_SERVER
			DrawShape();
#endif
		}

		private void OnDestroy()
		{
			for (int i = 0; i < madeTriggers.Count; ++i)
			{
				if (madeTriggers[i] != null)
				{
					Destroy(madeTriggers[i]);
				}
			}
			madeTriggers.Clear();
			DestroyOwned(ref outlineMaterial);
			DestroyOwned(ref shellMaterial);
			DestroyOwned(ref shellMesh);
		}

		private static void DestroyOwned<T>(ref T owned) where T : UnityEngine.Object
		{
			if (owned != null)
			{
				Destroy(owned);
				owned = null;
			}
		}

		private Trigger MakeTrigger(string what)
		{
			Trigger trigger = ScriptableObject.CreateInstance<Trigger>();
			trigger.name = $"{Settings.Label} ({what})";
			trigger.hideFlags = HideFlags.DontSave;
			madeTriggers.Add(trigger);
			return trigger;
		}

		/// <summary>A cached template by its ID, of the type asked for. False (and logged) when this peer has none.</summary>
		private bool TryGetTemplate<T>(int id, out T template) where T : CachedScriptableObject
		{
			template = null;
			if (id == 0)
			{
				return false;
			}
			if (CachedScriptableObject.TryGetByID(id, out CachedScriptableObject found) && found is T typed)
			{
				template = typed;
				return true;
			}
			Log.Warning("StaffTestRegion", $"'{Settings.Label}': no {typeof(T).Name} with ID {id} is loaded on this peer.");
			return false;
		}

#if !UNITY_SERVER
		/// <summary>
		/// The region made visible: a semi-transparent box in the kind's colour, and its twelve edges as one solid line, so
		/// the tester can see where it starts and ends from outside and from within. Best effort: drawn only when the
		/// built-in sprite shader is in the build (it is always included).
		/// </summary>
		/// <remarks>
		/// The sprite shader suits both: unlit, alpha-blended, no depth write, and it culls nothing, so the box's faces still
		/// show from inside it. Neither child has a collider, so neither can be walked into, shot or trip the region's own
		/// trigger.
		/// </remarks>
		private void DrawShape()
		{
			Shader shader = Shader.Find("Sprites/Default");
			if (shader == null)
			{
				return;
			}
			Color color = ColorOf(Settings.Kind, Settings.Amount);

			shellMesh = new Mesh { name = "Test Region Shell" };
			shellMesh.SetVertices(BoxCorners(Settings.Size));
			shellMesh.SetTriangles(BoxTriangles, 0);
			shellMesh.RecalculateBounds();
			Color tint = color;
			tint.a = ShellAlpha;
			shellMaterial = new Material(shader) { color = tint };
			GameObject shell = new GameObject("Shell");
			shell.transform.SetParent(transform, false);
			shell.AddComponent<MeshFilter>().sharedMesh = shellMesh;
			MeshRenderer shellRenderer = shell.AddComponent<MeshRenderer>();
			shellRenderer.sharedMaterial = shellMaterial;
			shellRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			shellRenderer.receiveShadows = false;
			shellRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			shellRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

			outlineMaterial = new Material(shader) { color = color };

			GameObject child = new GameObject("Outline");
			child.transform.SetParent(transform, false);
			LineRenderer line = child.AddComponent<LineRenderer>();
			line.useWorldSpace = false;
			line.loop = false;
			line.sharedMaterial = outlineMaterial;
			line.startColor = line.endColor = color;
			line.widthMultiplier = 0.08f;
			line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			line.receiveShadows = false;
			Vector3[] points = OutlinePath(Settings.Size);
			line.positionCount = points.Length;
			line.SetPositions(points);
		}
#endif

		/// <summary>The eight corners of a box centred on the origin: bottom four, then top four, each anticlockwise from -x -z.</summary>
		public static Vector3[] BoxCorners(Vector3 size)
		{
			Vector3 h = size * 0.5f;
			return new[]
			{
				new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z),
				new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z),
			};
		}

		/// <summary>The six faces of <see cref="BoxCorners"/>, two triangles each, wound outwards.</summary>
		public static readonly int[] BoxTriangles =
		{
			0, 1, 2, 0, 2, 3, // bottom (-y)
			4, 6, 5, 4, 7, 6, // top (+y)
			0, 4, 5, 0, 5, 1, // -z
			1, 5, 6, 1, 6, 2, // +x
			2, 6, 7, 2, 7, 3, // +z
			3, 7, 4, 3, 4, 0, // -x
		};

		/// <summary>
		/// One path over all twelve edges of a box centred on the origin. A box has no path that crosses each edge once (all
		/// eight corners have three edges), so three uprights are walked twice.
		/// </summary>
		public static Vector3[] OutlinePath(Vector3 size)
		{
			Vector3 h = size * 0.5f;
			Vector3 b0 = new Vector3(-h.x, -h.y, -h.z), b1 = new Vector3(h.x, -h.y, -h.z), b2 = new Vector3(h.x, -h.y, h.z), b3 = new Vector3(-h.x, -h.y, h.z);
			Vector3 t0 = new Vector3(-h.x, h.y, -h.z), t1 = new Vector3(h.x, h.y, -h.z), t2 = new Vector3(h.x, h.y, h.z), t3 = new Vector3(-h.x, h.y, h.z);
			return new[] { b0, b1, b2, b3, b0, t0, t1, b1, t1, t2, b2, t2, t3, b3, t3, t0 };
		}
	}
}
