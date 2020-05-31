﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OMODFramework.Scripting;
using Xunit;

namespace OMODFramework.Test
{
    public class ScriptTests
    {
        private class Functions : IScriptFunctions
        {
            public void Warn(string msg)
            {
                throw new NotImplementedException();
            }

            public void Message(string msg) { }

            public void Message(string msg, string title)
            {
                throw new NotImplementedException();
            }

            public IEnumerable<int> Select(IEnumerable<string> items, string title, bool isMultiSelect, IEnumerable<string> previews, IEnumerable<string> descriptions)
            {
                return new List<int>{0};
            }

            public string InputString(string title, string initialText)
            {
                throw new NotImplementedException();
            }

            public DialogResult DialogYesNo(string title)
            {
                return DialogResult.Yes;
            }

            public DialogResult DialogYesNo(string title, string message)
            {
                return DialogResult.Yes;
            }

            public void DisplayImage(FileInfo file, string title) { }

            public void DisplayText(string text, string title) { }

            public void Patch(FileInfo @from, FileInfo to)
            {
                throw new NotImplementedException();
            }

            public string ReadOblivionINI(string section, string name)
            {
                throw new NotImplementedException();
            }

            public string ReadRenderInfo(string name)
            {
                throw new NotImplementedException();
            }

            public bool DataFileExists(FileInfo file)
            {
                return false;
            }

            public bool HasScriptExtender()
            {
                throw new NotImplementedException();
            }

            public bool HasGraphicsExtender()
            {
                throw new NotImplementedException();
            }

            public Version ScriptExtenderVersion()
            {
                throw new NotImplementedException();
            }

            public Version GraphicsExtenderVersion()
            {
                throw new NotImplementedException();
            }

            public Version OblivionVersion()
            {
                throw new NotImplementedException();
            }

            public Version OBSEPluginVersion(FileInfo file)
            {
                throw new NotImplementedException();
            }

            public IEnumerable<ScriptESP> GetESPs()
            {
                throw new NotImplementedException();
            }

            public IEnumerable<string> GetActiveOMODNames()
            {
                throw new NotImplementedException();
            }

            public byte[] ReadExistingDataFile(FileInfo file)
            {
                throw new NotImplementedException();
            }

            public byte[] GetDataFileFromBSA(FileInfo file)
            {
                throw new NotImplementedException();
            }

            public byte[] GetDataFileFromBSA(string bsa, FileInfo file)
            {
                throw new NotImplementedException();
            }
        }

        private class Settings : IScriptSettings
        {
            public FrameworkSettings FrameworkSettings => FrameworkSettings.DefaultFrameworkSettings;
            public IScriptFunctions ScriptFunctions => new Functions();
        }

        [Fact]
        public void ScriptTest()
        {
            var list = new List<FileInfo>
            {
                //new FileInfo("M:\\Projects\\omod\\NoMaaM BBB Animation Replacer V3_1 OMOD-35551-3-1.omod"),
                //new FileInfo("M:\\Projects\\omod\\NoMaaM Breathing Idles V1 OMOD-40462-1-0.omod"),
                new FileInfo("M:\\Projects\\omod\\HGEC Body with BBB v1dot12-34442.omod"),
                //new FileInfo("M:\\Projects\\omod\\EVE_HGEC_BodyStock and Clothing OMOD-24078.omod"),
                //new FileInfo("M:\\Projects\\omod\\Robert Male Body Replacer v52 OMOD-40532-1.omod"),
            };

            var srdList = list.Select(x =>
            {
                using var omod = new OMOD(x);
                omod.GetDataFileList();
                if(omod.HasFile(OMODFile.PluginsCRC))
                    omod.GetPlugins();
                return ScriptRunner.ExecuteScript(omod, new Settings());
            }).ToList();

            Assert.NotEmpty(srdList);
        }
    }
}