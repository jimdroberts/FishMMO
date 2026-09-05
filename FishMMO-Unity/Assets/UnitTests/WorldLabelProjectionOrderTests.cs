using System;
using System.Collections.Generic;
using System.Reflection;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the execution-order contract that keeps world labels from shimmering (issue #227).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="UITKWorldLabelLayer"/> projects every world label from its world position onto a
	/// screen-space panel using the camera transform, in <c>LateUpdate</c>. The camera itself is
	/// placed in <c>LateUpdate</c> too — by <see cref="PlayerInputController"/> in the world, and by
	/// <see cref="ArenaSpectatorCamera"/> in an arena. Nothing about <c>LateUpdate</c> orders those
	/// three against one another: Unity runs same-order behaviours in registration order, and both
	/// camera writers live on objects created at world entry, long after the label layer's own
	/// scene. So the layer ran first and read a camera transform that was one frame old.
	/// </para>
	/// <para>
	/// That is worse than a one-frame lag. The owner's character transform is written only on the
	/// network tick (30 Hz) and the camera is locked to it, so the camera's per-frame delta is zero
	/// on most frames and a whole tick of travel on the frames straight after a tick. Reading it a
	/// frame late turned that into a tick-rate shimmer on every world label whenever the player
	/// moved — visible on text, invisible on the meshes the labels sit above, which is exactly how
	/// the bug was reported.
	/// </para>
	/// <para>
	/// The fix is an explicit <see cref="DefaultExecutionOrder"/> on the layer, which is the kind of
	/// thing that is silently deleted by an inspector edit or lost when a component is rewritten.
	/// Hence a test: it asserts the ordering, not the number.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class WorldLabelProjectionOrderTests
	{
		/// <summary>
		/// The components that place a camera before the labels are projected. Each one writes a
		/// camera transform from its own <c>LateUpdate</c>.
		/// </summary>
		private static readonly Type[] CameraWriters =
		{
			typeof(PlayerInputController),
			typeof(ArenaSpectatorCamera),
		};

		/// <summary>The execution order Unity will use for a type: the attribute, or zero.</summary>
		private static int ExecutionOrderOf(Type type)
		{
			DefaultExecutionOrder attribute = (DefaultExecutionOrder)Attribute.GetCustomAttribute(type, typeof(DefaultExecutionOrder));
			return attribute != null ? attribute.order : 0;
		}

		[Test]
		public void WorldLabelLayer_DeclaresAnExplicitExecutionOrder()
		{
			DefaultExecutionOrder attribute = (DefaultExecutionOrder)Attribute.GetCustomAttribute(
				typeof(UITKWorldLabelLayer), typeof(DefaultExecutionOrder));

			Assert.IsNotNull(attribute,
				"UITKWorldLabelLayer must declare [DefaultExecutionOrder]. Without it Unity is free to " +
				"project labels before the camera has been placed for the frame, which is issue #227.");
			Assert.AreEqual(UITKWorldLabelLayer.ExecutionOrder, attribute.order,
				"The attribute and the documented constant must agree.");
		}

		[Test]
		public void WorldLabelLayer_ProjectsAfterEveryCameraWriter()
		{
			int layerOrder = ExecutionOrderOf(typeof(UITKWorldLabelLayer));

			foreach (Type writer in CameraWriters)
			{
				int writerOrder = ExecutionOrderOf(writer);
				Assert.Less(writerOrder, layerOrder,
					$"{writer.Name} places a camera in LateUpdate, so it must run before " +
					$"UITKWorldLabelLayer projects against that camera. Orders were {writerOrder} and {layerOrder}.");
			}
		}

		[Test]
		public void EveryCameraWriterIsStillDrivenFromLateUpdate()
		{
			/* The ordering above only means anything while the camera writers are LateUpdate
			 * components. If one moves to Update or to a render callback the contract changes
			 * shape, and the list above needs revisiting rather than silently passing. */
			foreach (Type writer in CameraWriters)
			{
				MethodInfo lateUpdate = writer.GetMethod("LateUpdate",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

				Assert.IsNotNull(lateUpdate,
					$"{writer.Name} no longer declares LateUpdate. Re-check where it places its camera " +
					"and whether UITKWorldLabelLayer still runs after it.");
			}
		}

		[Test]
		public void NoOtherClientBehaviourOutRunsTheLabelLayer()
		{
			/* A guard rather than a rule: any LateUpdate component ordered at or after the label
			 * layer runs after the labels are projected, which is only correct if it does not touch
			 * a camera. Nothing needs that today, so the safe state is an empty list. */
			int layerOrder = ExecutionOrderOf(typeof(UITKWorldLabelLayer));
			List<string> offenders = new List<string>();

			foreach (Type type in typeof(UITKWorldLabelLayer).Assembly.GetTypes())
			{
				if (type == typeof(UITKWorldLabelLayer) || !typeof(MonoBehaviour).IsAssignableFrom(type))
				{
					continue;
				}

				MethodInfo lateUpdate = type.GetMethod("LateUpdate",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
				if (lateUpdate == null)
				{
					continue;
				}

				if (ExecutionOrderOf(type) >= layerOrder)
				{
					offenders.Add($"{type.FullName} (order {ExecutionOrderOf(type)})");
				}
			}

			Assert.IsEmpty(offenders,
				"These components run their LateUpdate at or after UITKWorldLabelLayer, so anything they " +
				"do to a camera lands after the labels were projected against it: " + string.Join(", ", offenders));
		}
	}
}
