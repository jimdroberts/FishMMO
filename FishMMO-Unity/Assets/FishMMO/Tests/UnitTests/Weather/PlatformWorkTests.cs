using System;
using System.Threading;
using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
    public class PlatformWorkTests
    {
        [Test]
        public void InlineFieldBuildCompletesOnCallerWithItsResult()
        {
            int caller = Thread.CurrentThread.ManagedThreadId, actual = -1;
            var task = PlatformWork.Run(() => { actual = Thread.CurrentThread.ManagedThreadId; return 731; },true);
            Assert.That(task.IsCompleted,Is.True);
            Assert.That(task.Result,Is.EqualTo(731));
            Assert.That(actual,Is.EqualTo(caller));
        }
        [Test]
        public void InlineActionPreservesTaskFaultForExistingCompletionHandlers()
        {
            var error = new InvalidOperationException("field build failure");
            var task = PlatformWork.Run((Action)(() => throw error),true);
            Assert.That(task.IsFaulted,Is.True);
            Assert.That(task.Exception.InnerException,Is.SameAs(error));
        }
        [Test]
        public void InlineResultPreservesTaskFaultForExistingCompletionHandlers()
        {
            var error = new InvalidOperationException("field build failure");
            var task = PlatformWork.Run<int>(() => throw error,true);
            Assert.That(task.IsFaulted,Is.True);
            Assert.That(task.Exception.InnerException,Is.SameAs(error));
        }
        [Test]
        public void SerialAndWorkerFieldRowsProduceIdenticalValues()
        {
            var serial = new double[257]; var workers = new double[257];
            double Cell(int i) => Math.Sin(i*.071)*Math.Exp(-i*.01)+Math.Sqrt(i+1);
            PlatformWork.For(0,serial.Length,i => serial[i]=Cell(i),true);
            PlatformWork.For(0,workers.Length,i => workers[i]=Cell(i));
            CollectionAssert.AreEqual(serial,workers);
        }
        [Test]
        public void SerialRowsRespectRangeAndEmptyBatches()
        {
            var visited = new int[9];
            PlatformWork.For(2,7,i => visited[i]++,true);
            PlatformWork.For(7,7,i => Assert.Fail("Empty range ran"),true);
            CollectionAssert.AreEqual(new[]{0,0,1,1,1,1,1,0,0},visited);
        }
    }
}
