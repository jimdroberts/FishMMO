using System;
using NUnit.Framework;
using FishMMO.Logging;
using FishMMO.UnitTests.Harness;

namespace FishMMO.UnitTests.Harness
{
    /// <summary>
    /// Provides assertion methods that always log the check and result (pass/fail) using FishMMO-Logger.
    /// </summary>
    public static class LogAssert
    {
        public static void AreEqual<T>(T expected, T actual, string message = null)
        {
            bool result = object.Equals(expected, actual);
            string logMsg = $"Assert.AreEqual | Expected: {expected} | Actual: {actual} | {(result ? "PASS" : "FAIL")}{(message != null ? " | " + message : "")}";
            AuthTestTrace.Log("LogAssert", result ? "PASS" : "FAIL", logMsg);
            Assert.AreEqual(expected, actual, message);
        }

        public static void AreNotEqual<T>(T notExpected, T actual, string message = null)
        {
            bool result = !object.Equals(notExpected, actual);
            string logMsg = $"Assert.AreNotEqual | NotExpected: {notExpected} | Actual: {actual} | {(result ? "PASS" : "FAIL")}{(message != null ? " | " + message : "")}";
            AuthTestTrace.Log("LogAssert", result ? "PASS" : "FAIL", logMsg);
            Assert.AreNotEqual(notExpected, actual, message);
        }

        public static void AreSame(object expected, object actual, string message = null)
        {
            bool result = object.ReferenceEquals(expected, actual);
            string logMsg = $"Assert.AreSame | Expected(ref): {(expected == null ? "null" : expected.GetType().Name)} | Actual(ref): {(actual == null ? "null" : actual.GetType().Name)} | {(result ? "PASS" : "FAIL")}{(message != null ? " | " + message : "")}";
            AuthTestTrace.Log("LogAssert", result ? "PASS" : "FAIL", logMsg);
            Assert.AreSame(expected, actual, message);
        }

        public static void AreNotSame(object notExpected, object actual, string message = null)
        {
            bool result = !object.ReferenceEquals(notExpected, actual);
            string logMsg = $"Assert.AreNotSame | NotExpected(ref): {(notExpected == null ? "null" : notExpected.GetType().Name)} | Actual(ref): {(actual == null ? "null" : actual.GetType().Name)} | {(result ? "PASS" : "FAIL")}{(message != null ? " | " + message : "")}";
            AuthTestTrace.Log("LogAssert", result ? "PASS" : "FAIL", logMsg);
            Assert.AreNotSame(notExpected, actual, message);
        }

        public static void IsTrue(bool condition, string message = null)
        {
            string logMsg = $"Assert.IsTrue | Condition: {condition} | {(condition ? "PASS" : "FAIL")}{(message != null ? " | " + message : "")}";
            AuthTestTrace.Log("LogAssert", condition ? "PASS" : "FAIL", logMsg);
            Assert.IsTrue(condition, message);
        }

        public static void IsFalse(bool condition, string message = null)
        {
            string logMsg = $"Assert.IsFalse | Condition: {condition} | {(!condition ? "PASS" : "FAIL")}{(message != null ? " | " + message : "")}";
            AuthTestTrace.Log("LogAssert", !condition ? "PASS" : "FAIL", logMsg);
            Assert.IsFalse(condition, message);
        }

        public static void IsNull(object obj, string message = null)
        {
            bool result = obj == null;
            string logMsg = $"Assert.IsNull | Value: {(obj == null ? "null" : obj.ToString())} | {(result ? "PASS" : "FAIL")}{(message != null ? " | " + message : "")}";
            AuthTestTrace.Log("LogAssert", result ? "PASS" : "FAIL", logMsg);
            Assert.IsNull(obj, message);
        }

        public static void IsNotNull(object obj, string message = null)
        {
            bool result = obj != null;
            string logMsg = $"Assert.IsNotNull | Value: {(obj == null ? "null" : obj.ToString())} | {(result ? "PASS" : "FAIL")}{(message != null ? " | " + message : "")}";
            AuthTestTrace.Log("LogAssert", result ? "PASS" : "FAIL", logMsg);
            Assert.IsNotNull(obj, message);
        }

        public static void Fail(string message = null)
        {
            string logMsg = $"Assert.Fail | FAIL{(message != null ? " | " + message : "")}";
            AuthTestTrace.Log("LogAssert", "FAIL", logMsg);
            Assert.Fail(message);
        }
    }
}