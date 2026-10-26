#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using System.Linq;
using System.Text;
using FishMMO.Shared.CustomBuildTool.Config;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace FishMMO.UnitTests
{
    public class ServerCfgDashboardTests
    {
        private string folder;
        [SetUp] public void Setup() { folder = Path.Combine(Path.GetTempPath(), "fishmmo-cfg-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); }
        [TearDown] public void Cleanup() { Directory.Delete(folder, true); }
        private ServerCfgDocument Make(string text, string name = "LoginServer.cfg", bool bom = false)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, text, new UTF8Encoding(bom));
            return new ServerCfgDocument(path);
        }

        [Test]
        public void UnchangedFileRoundTripsByteExactlyWithCommentsMixedEndingsAndBom()
        {
            var doc = Make("# Header\r\nServerName=LoginServer\n; Comment\rPort = 7770\r\nSmtp:Password=abc=def==\r\n", bom: true);
            var before = File.ReadAllBytes(doc.PathName);
            Assert.That(doc.Get("smtp:password"), Is.EqualTo("abc=def=="));
            doc.Save(false);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(doc.PathName));
            Assert.That(doc.IsDirty, Is.False);
        }
        [Test]
        public void EditingPreservesCommentsWhitespaceOrderingAndLineEndings()
        {
            var doc = Make("ServerName=LoginServer\r\n# Port hint\r\n  Port =  7770  \r\n\r\n", bom: true);
            doc.Set("port", "7799"); doc.Save(false);
            Assert.That(File.ReadAllText(doc.PathName), Is.EqualTo("ServerName=LoginServer\r\n# Port hint\r\n  Port =  7799  \r\n\r\n"));
            Assert.That(doc.Description("Port"), Is.EqualTo("Port hint"));
        }
        [Test]
        public void DuplicateKeysMatchRuntimeCaseInsensitiveLastDefinitionAndDeleteAllCopies()
        {
            var doc = Make("ServerName=LoginServer\nCustom=first\ncustom=last\n");
            Assert.That(doc.Get("CUSTOM"), Is.EqualTo("last"));
            doc.Set("Custom", "updated"); doc.Save(false);
            Assert.That(File.ReadAllText(doc.PathName), Does.Contain("Custom=first\ncustom=updated"));
            doc.Remove("CUSTOM"); doc.Save(false);
            Assert.That(new ServerCfgDocument(doc.PathName).Keys, Does.Not.Contain("Custom"));
        }
        [TestCase("ServerName=LoginServer")]
        [TestCase("ServerName=LoginServer\n")]
        public void AddHandlesFilesWithOrWithoutTrailingNewline(string text)
        {
            var doc = Make(text); doc.Set("Custom", "x=y"); doc.Save(false);
            Assert.That(new ServerCfgDocument(doc.PathName).Get("Custom"), Is.EqualTo("x=y"));
        }
        [TestCase("bad\nkey", "x")]
        [TestCase("#comment", "x")]
        [TestCase(";comment", "x")]
        [TestCase("has=equals", "x")]
        [TestCase("okay", "a\nb")]
        public void RejectsMultilineAndCommentOrMalformedKeys(string key, string value)
        {
            var doc = Make("ServerName=LoginServer\n");
            Assert.Throws<ArgumentException>(() => doc.Set(key, value));
        }
        [TestCase("-1")]
        [TestCase("65536")]
        [TestCase("not-a-port")]
        public void InvalidPortsCannotBeSaved(string port)
        {
            var doc = Make("ServerName=LoginServer\nPort=7770\n");
            var before = File.ReadAllBytes(doc.PathName); doc.Set("Port", port);
            Assert.Throws<InvalidOperationException>(() => doc.Save(false));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(doc.PathName));
        }
        [Test]
        public void ProductionRejectsVerificationBypassWhileDevelopmentAllowsIt()
        {
            var doc = Make("ServerName=LoginServer\nAutoVerifyAccounts=false\n");
            doc.Set("AutoVerifyAccounts", "true");
            Assert.Throws<InvalidOperationException>(() => doc.Save(true));
            doc.Save(false);
            Assert.That(new ServerCfgDocument(doc.PathName).Get("AutoVerifyAccounts"), Is.EqualTo("true"));
        }
        [Test]
        public void ExternalEditsAreNotSilentlyOverwritten()
        {
            var doc = Make("ServerName=LoginServer\nPort=7770\n");
            File.AppendAllText(doc.PathName, "# changed externally\n"); doc.Set("Port", "7788");
            Assert.Throws<IOException>(() => doc.Save(false));
            Assert.That(File.ReadAllText(doc.PathName), Does.Contain("# changed externally"));
        }
        [Test]
        public void ApplyChecksAllTargetsBeforeChangingAnyBuiltCfg()
        {
            var login = Make("ServerName=LoginServer\nPort=7770\n");
            var world = Make("ServerName=World Server\nPort=7780\n", "WorldServer.cfg");
            string build = Path.Combine(folder, "Build"); Directory.CreateDirectory(build);
            File.WriteAllText(Path.Combine(build, "LoginServer.cfg"), "old");
            Assert.Throws<IOException>(() => ServerCfgDocument.CopyToBuild(new[] { login, world }, build, false));
            Assert.That(File.ReadAllText(Path.Combine(build, "LoginServer.cfg")), Is.EqualTo("old"));
            File.WriteAllText(Path.Combine(build, "WorldServer.cfg"), "old");
            ServerCfgDocument.CopyToBuild(new[] { login, world }, build, false);
            CollectionAssert.AreEqual(login.GetBytes(), File.ReadAllBytes(Path.Combine(build, "LoginServer.cfg")));
            CollectionAssert.AreEqual(world.GetBytes(), File.ReadAllBytes(Path.Combine(build, "WorldServer.cfg")));
        }
        public sealed class CfgTestWindow : EditorWindow { }

        [UnityTest]
        public IEnumerator FastToggleWritesActualCfgAndAllOtherFieldsAreRenderedWithSecretsMasked()
        {
            var doc = Make("ServerName=LoginServer\nAutoVerifyAccounts=false\nPort=7770\nBetaMode=false\nSmtp:Password=123456\nCustom=value\n");
            var view = ServerCfgEditorView.Create(doc, false, _ => { }, () => { });
            var host = ScriptableObject.CreateInstance<CfgTestWindow>();
            host.Show();
            host.rootVisualElement.Add(view);
            try
            {
            yield return null; // UI Toolkit value changes dispatch on attached panels.
            foreach (string key in doc.Keys) Assert.That(view.Q<VisualElement>("server-cfg-" + key), Is.Not.Null, key);
            Assert.That(view.Q<TextField>("server-cfg-Smtp:Password").isPasswordField, Is.True);
            Assert.That(view.Q<TextField>("server-cfg-ServerName"), Is.Not.Null);
            Assert.That(view.Q<Toggle>("server-cfg-AutoVerifyAccounts").panel, Is.Not.Null, "UI test must use an attached editor panel.");
            view.Q<Toggle>("server-cfg-AutoVerifyAccounts").value = true;
            yield return null;
            Assert.That(new ServerCfgDocument(doc.PathName).Get("AutoVerifyAccounts"), Is.EqualTo("true"), view.Q<HelpBox>().text);
            view.Q<Toggle>("server-cfg-BetaMode").value = true;
            yield return null;
            Assert.That(new ServerCfgDocument(doc.PathName).Get("BetaMode"), Is.EqualTo("true"), view.Q<HelpBox>().text);
            }
            finally { host.Close(); }
        }
        [Test]
        public void ProductionFastToggleCannotEnableBypass()
        {
            var doc = Make("ServerName=LoginServer\nAutoVerifyAccounts=false\n");
            var view = ServerCfgEditorView.Create(doc, true, _ => { }, () => { });
            var fast = view.Q<Toggle>("server-cfg-AutoVerifyAccounts");
            Assert.That(fast.enabledSelf, Is.False);
            fast.value = true;
            Assert.That(new ServerCfgDocument(doc.PathName).Get("AutoVerifyAccounts"), Is.EqualTo("false"));
        }
    }
}
#endif
