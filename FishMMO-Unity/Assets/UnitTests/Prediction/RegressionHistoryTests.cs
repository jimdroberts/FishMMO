using System;
using NUnit.Framework;
using FishMMO.Shared;
using UnityEngine;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
    [TestFixture]
    public class RegressionHistoryTests
    {
        private static int TemplateID = 1;
        private const float TickDelta30 = 1.0f / 30f;
        private const float DurationSeconds = 30f;

        [SetUp]
        public void SetUp()
        {
            var template = ScriptableObject.CreateInstance<MockBuffTemplate>();
            template.Duration = DurationSeconds;
            template.TickRate = 1.0f;
            template.name = "RegressionHistTemplate";
            template.AddToCache(template.name);
            TemplateID = template.ID;
        }

        // Minimal mock template for the historical regression test. Scoped
        // inside the fixture class so it does not pollute the global namespace
        // or collide with the identically-named private mock in BuffExpiryTests.
        private class MockBuffTemplate : BaseBuffTemplate
        {
            public override void OnApply(Buff buff, FishMMO.Shared.Core.ICharacter target) { }
            public override void OnRemove(Buff buff, FishMMO.Shared.Core.ICharacter target) { }
        }

        [Test]
        public void FreshApply_DivergentLocalTick_ExpiryDiffers_BroadcastPathKnownIssue()
        {
            try
            {
                AuthTestTrace.LogTestStart(
                    nameof(FreshApply_DivergentLocalTick_ExpiryDiffers_BroadcastPathKnownIssue),
                    "Broadcast path uses LocalTick which diverges from the server replicate tick. " +
                    "Demonstrates why owner state MUST come from reconcile, never from broadcast.")
                    .GetAwaiter().GetResult();

                uint serverReplicateTick = 11_000u;
                uint clientLocalTick = 1_000u;

                var serverBuff = new Buff(TemplateID, serverReplicateTick, TickDelta30);
                var clientBuff = new Buff(TemplateID, clientLocalTick, TickDelta30);

                uint expectedOffset = serverReplicateTick - clientLocalTick;
                uint actualOffset = serverBuff.ExpiryTick - clientBuff.ExpiryTick;

                LogAssert.AreEqual(expectedOffset, actualOffset,
                    "ExpiryTick offset must equal the server/client LocalTick divergence.");

                LogAssert.IsTrue(clientBuff.HasExpired(serverBuff.ExpiryTick),
                    "Client buff (broadcast path) appears expired well before server expiry — " +
                    "demonstrates why reconcile correction is mandatory.");

                AuthTestTrace.Log("BuffExpiryTests", "SUCCESS",
                    nameof(FreshApply_DivergentLocalTick_ExpiryDiffers_BroadcastPathKnownIssue))
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AuthTestTrace.Log("BuffExpiryTests", "FAILURE",
                    $"{nameof(FreshApply_DivergentLocalTick_ExpiryDiffers_BroadcastPathKnownIssue)}: {ex.Message}\n{ex.StackTrace}")
                    .GetAwaiter().GetResult();
                throw;
            }
            finally
            {
                AuthTestTrace.LogTestEnd(
                    nameof(FreshApply_DivergentLocalTick_ExpiryDiffers_BroadcastPathKnownIssue))
                    .GetAwaiter().GetResult();
            }
        }
    }
}
