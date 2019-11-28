﻿/*
    Copyright (C) 2019  erri120

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

/*
 * This file contains parts of the Oblivion Mod Manager licensed under GPLv2
 * and has been modified for use in this OMODFramework
 * Original source: https://www.nexusmods.com/oblivion/mods/2097
 * GPLv2: https://opensource.org/licenses/gpl-2.0.php
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OMODFramework.Classes;
using Directory = Alphaleonis.Win32.Filesystem.Directory;
using File = Alphaleonis.Win32.Filesystem.File;
using Path = Alphaleonis.Win32.Filesystem.Path;

namespace OMODFramework.Scripting
{
    internal static class OBMMScriptHandler
    {
        private class FlowControlStruct
        {
            public readonly int Line;
            public readonly byte Type;
            public readonly string[] Values;
            public readonly string Var;
            public bool Active;
            public bool HitCase;
            public int ForCount;

            //Inactive
            public FlowControlStruct(byte type)
            {
                Line = -1;
                this.Type = type;
                Values = null;
                Var = null;
                Active = false;
            }

            //If
            public FlowControlStruct(int line, bool active)
            {
                this.Line = line;
                Type = 0;
                Values = null;
                Var = null;
                this.Active = active;
            }

            //Select
            public FlowControlStruct(int line, string[] values)
            {
                this.Line = line;
                Type = 1;
                this.Values = values;
                Var = null;
                Active = false;
            }

            //For
            public FlowControlStruct(string[] values, string var, int line)
            {
                this.Line = line;
                Type = 2;
                this.Values = values;
                this.Var = var;
                Active = false;
            }
        }

        private static ScriptReturnData _srd;
        private static Dictionary<string, string> _variables;

        private static string _dataFiles;
        private static string _plugins;
        private static string _cLine = "0";

        private static IScriptFunctions _scriptFunctions;

        internal static ScriptReturnData Execute(string inputScript, string dataPath, string pluginsPath, IScriptFunctions scriptFunctions)
        {
            _srd = new ScriptReturnData();
            if (string.IsNullOrWhiteSpace(inputScript))
                return _srd;

            _scriptFunctions = scriptFunctions ?? throw new OMODFrameworkException("The provided script functions can not be null!");

            _dataFiles = dataPath;
            _plugins = pluginsPath;
            _variables = new Dictionary<string, string>();

            var flowControl = new Stack<FlowControlStruct>();
            var extraLines = new Queue<string>();

            _variables["NewLine"] = Environment.NewLine;
            _variables["Tab"] = "\t";

            var script = inputScript.Replace("\r", "").Split('\n');
            string skipTo = null;
            bool allowRunOnLines = false;
            bool Break = false;

            for (var i = 0; i < script.Length || extraLines.Count > 0; i++)
            {
                string s;
                if (extraLines.Count > 0)
                {
                    i--;
                    s = extraLines.Dequeue().Replace('\t', ' ').Trim();
                }
                else
                {
                    s = script[i].Replace('\t', ' ').Trim();
                }

                _cLine = i.ToString();
                if (allowRunOnLines)
                {
                    while (s.EndsWith("\\"))
                    {
                        s = s.Remove(s.Length - 1);
                        if (extraLines.Count > 0)
                        {
                            s += extraLines.Dequeue().Replace('\t', ' ').Trim();
                        }
                        else
                        {
                            if (++i == script.Length)
                                Warn("Run-on line passed end of script");
                            else
                                s += script[i].Replace('\t', ' ').Trim();
                        }
                    }
                }

                if (skipTo != null)
                {
                    if (s == skipTo) skipTo = null;
                    else continue;
                }

                var line = SplitLine(s);
                if (line.Length == 0) continue;

                if (flowControl.Count != 0 && !flowControl.Peek().Active)
                {
                    switch (line[0])
                    {
                    case "":
                        Warn("Empty function");
                        break;
                    case "If":
                    case "IfNot":
                        flowControl.Push(new FlowControlStruct(0));
                        break;
                    case "Else":
                        if (flowControl.Count != 0 && flowControl.Peek().Type == 0)
                            flowControl.Peek().Active = flowControl.Peek().Line != -1;
                        else Warn("Unexpected Else");
                        break;
                    case "EndIf":
                        if (flowControl.Count != 0 && flowControl.Peek().Type == 0) flowControl.Pop();
                        else Warn("Unexpected EndIf");
                        break;
                    case "Select":
                    case "SelectMany":
                    case "SelectWithPreview":
                    case "SelectManyWithPreview":
                    case "SelectWithDescriptions":
                    case "SelectManyWithDescriptions":
                    case "SelectWithDescriptionsAndPreviews":
                    case "SelectManyWithDescriptionsAndPreviews":
                    case "SelectVar":
                    case "SelectString":
                        flowControl.Push(new FlowControlStruct(1));
                        break;
                    case "Case":
                        if (flowControl.Count != 0 && flowControl.Peek().Type == 1)
                        {
                            if (flowControl.Peek().Line != -1 && Array.IndexOf(flowControl.Peek().Values, s) != -1)
                            {
                                flowControl.Peek().Active = true;
                                flowControl.Peek().HitCase = true;
                            }
                        }
                        else Warn("Unexpected Break");

                        break;
                    case "Default":
                        if (flowControl.Count != 0 && flowControl.Peek().Type == 1)
                        {
                            if (flowControl.Peek().Line != -1 && !flowControl.Peek().HitCase)
                                flowControl.Peek().Active = true;
                        }
                        else Warn("Unexpected Default");

                        break;
                    case "EndSelect":
                        if (flowControl.Count != 0 && flowControl.Peek().Type == 1) flowControl.Pop();
                        else Warn("Unexpected EndSelect");
                        break;
                    case "For":
                        flowControl.Push(new FlowControlStruct(2));
                        break;
                    case "EndFor":
                        if (flowControl.Count != 0 && flowControl.Peek().Type == 2) flowControl.Pop();
                        else Warn("Unexpected EndFor");
                        break;
                    case "Break":
                    case "Continue":
                    case "Exit":
                        break;
                    }
                }
                else
                {
                    switch (line[0])
                    {
                    case "Goto":
                        if (line.Length < 2)
                            Warn("Not enough arguments to function 'Goto'!");
                        else
                        {
                            if (line.Length > 2) Warn("Unexpected extra arguments to function 'Goto'");
                            skipTo = $"Label {line[1]}";
                            flowControl.Clear();
                        }
                        break;
                    case "Label":
                        break;
                    case "If":
                        flowControl.Push(new FlowControlStruct(i, FunctionIf(line)));
                        break;
                    case "IfNot":
                        flowControl.Push(new FlowControlStruct(i, !FunctionIf(line)));
                        break;
                    case "Else":
                        if (flowControl.Count != 0 && flowControl.Peek().Type == 0) flowControl.Peek().Active = false;
                        else Warn("Unexpected Else");
                        break;
                    case "EndIf":
                        if (flowControl.Count != 0 && flowControl.Peek().Type == 0) flowControl.Pop();
                        else Warn("Unexpected EndIf");
                        break;
                    case "Select":
                        flowControl.Push(new FlowControlStruct(i, FunctionSelect(line, false, false, false)));
                        break;
                    case "SelectMany":
                        flowControl.Push(new FlowControlStruct(i, FunctionSelect(line, true, false, false)));
                        break;
                    case "SelectWithPreview":
                        flowControl.Push(new FlowControlStruct(i, FunctionSelect(line, false, true, false)));
                        break;
                    case "SelectManyWithPreview":
                        flowControl.Push(new FlowControlStruct(i, FunctionSelect(line, true, true, false)));
                        break;
                    case "SelectWithDescriptions":
                        flowControl.Push(new FlowControlStruct(i, FunctionSelect(line, false, false, true)));
                        break;
                    case "SelectManyWithDescriptions":
                        flowControl.Push(new FlowControlStruct(i, FunctionSelect(line, true, false, true)));
                        break;
                    case "SelectWithDescriptionsAndPreviews":
                        flowControl.Push(new FlowControlStruct(i, FunctionSelect(line, false, true, true)));
                        break;
                    case "SelectManyWithDescriptionsAndPreviews":
                        flowControl.Push(new FlowControlStruct(i, FunctionSelect(line, true, true, true)));
                        break;
                    case "SelectVar":
                        flowControl.Push(new FlowControlStruct(i, FunctionSelectVar(line, true)));
                        break;
                    case "SelectString":
                        flowControl.Push(new FlowControlStruct(i, FunctionSelectVar(line, false)));
                        break;
                    case "Break": {
                        bool found = false;
                        var fcs = flowControl.ToArray();
                        for (int k = 0; k < fcs.Length; k++)
                        {
                            if (fcs[k].Type != 1)
                                continue;

                            for (int j = 0; j <= k; j++) fcs[j].Active = false;
                            found = true;
                            break;
                        }

                        if (!found) Warn("Unexpected Break");
                        break;
                        }
                    case "Case":
                        if (flowControl.Count == 0 || flowControl.Peek().Type != 1) Warn("Unexpected Case");
                        break;
                    case "Default":
                        if (flowControl.Count == 0 || flowControl.Peek().Type != 1)
                            Warn("Unexpected Default");
                        break;
                    case "EndSelect":
                        if(flowControl.Count!=0&&flowControl.Peek().Type==1) flowControl.Pop();
                        else Warn("Unexpected EndSelect");
                        break;
                    case "For": {
                        var fc = FunctionFor(line, i);
                        flowControl.Push(fc);
                        if (fc.Line != -1 && fc.Values.Length > 0)
                        {
                            _variables[fc.Var] = fc.Values[0];
                            fc.Active = true;
                        }

                        break;
                        }
                    case "Continue": {
                        var found = false;
                        var fcs = flowControl.ToArray();
                        for (int k = 0; k < fcs.Length; k++)
                        {
                            if (fcs[k].Type != 2)
                                continue;

                            fcs[k].ForCount++;
                            if (fcs[k].ForCount == fcs[k].Values.Length)
                            {
                                for (int j = 0; j <= k; j++) fcs[j].Active = false;
                            }
                            else
                            {
                                i = fcs[k].Line;
                                _variables[fcs[k].Var] = fcs[k].Values[fcs[k].ForCount];
                                for (int j = 0; j < k; j++) flowControl.Pop();
                            }

                            found = true;
                            break;
                        }

                        if (!found) Warn("Unexpected Continue");
                        break;
                        }
                    case "Exit": {
                        bool found = false;
                        var fcs = flowControl.ToArray();
                        for (int k = 0; k < fcs.Length; k++)
                        {
                            if (fcs[k].Type != 2)
                                continue;

                            for (int j = 0; j <= k; j++) flowControl.Peek().Active = false;
                            found = true;
                            break;
                        }

                        if (!found) Warn("Unexpected Exit");
                        break;
                        }
                    case "EndFor":
                        if (flowControl.Count != 0 && flowControl.Peek().Type == 2)
                        {
                            var fc = flowControl.Peek();
                            fc.ForCount++;
                            if (fc.ForCount == fc.Values.Length) flowControl.Pop();
                            else
                            {
                                i = fc.Line;
                                _variables[fc.Var] = fc.Values[fc.ForCount];
                            }
                        }
                        else Warn("Unexpected EndFor");
                        break;
                    //Functions
                    case "Message":
                        FunctionMessage(line);
                        break;
                    case "LoadEarly":
                        FunctionLoadEarly(line);
                        break;
                    case "LoadBefore":
                        FunctionLoadOrder(line, false);
                        break;
                    case "LoadAfter":
                        FunctionLoadOrder(line, true);
                        break;
                    case "ConflictsWith":
                        FunctionConflicts(line, true, false);
                        break;
                    case "DependsOn":
                        FunctionConflicts(line, false, false);
                        break;
                    case "ConflictsWithRegex":
                        FunctionConflicts(line, true, true);
                        break;
                    case "DependsOnRegex":
                        FunctionConflicts(line, false, true);
                        break;
                    case "DontInstallAnyPlugins":
                        _srd.InstallAllPlugins = false;
                        break;
                    case "DontInstallAnyDataFiles":
                        _srd.InstallAllData = false;
                        break;
                    case "InstallAllPlugins":
                        _srd.InstallAllPlugins = true;
                        break;
                    case "InstallAllDataFiles":
                        _srd.InstallAllData = true;
                        break;
                    case "InstallPlugin":
                        FunctionModifyInstall(line, true, true);
                        break;
                    case "DontInstallPlugin":
                        FunctionModifyInstall(line, true, false);
                        break;
                    case "InstallDataFile":
                        FunctionModifyInstall(line, false, true);
                        break;
                    case "DontInstallDataFile":
                        FunctionModifyInstall(line, false, false);
                        break;
                    case "DontInstallDataFolder":
                        FunctionModifyInstallFolder(line, false);
                        break;
                    case "InstallDataFolder":
                        FunctionModifyInstallFolder(line, true);
                        break;
                    case "RegisterBSA":
                        FunctionRegisterBSA(line, true);
                        break;
                    case "UnregisterBSA":
                        FunctionRegisterBSA(line, false);
                        break;
                    case "FatalError":
                        _srd.CancelInstall = true;
                        break;
                    case "Return":
                        Break = true;
                        break;
                    case "UncheckESP":
                        FunctionUncheckESP(line);
                        break;
                    case "SetDeactivationWarning":
                        FunctionSetDeactivationWarning(line);
                        break;
                    case "CopyDataFile":
                        FunctionCopyDataFile(line, false);
                        break;
                    case "CopyPlugin":
                        FunctionCopyDataFile(line, true);
                        break;
                    case "CopyDataFolder":
                        FunctionCopyDataFolder(line);
                        break;
                    case "PatchPlugin":
                        FunctionPatch(line, true);
                        break;
                    case "PatchDataFile":
                        FunctionPatch(line, false);
                        break;
                    case "EditINI":
                        FunctionEditINI(line);
                        break;
                    case "EditSDP":
                    case "EditShader":
                        FunctionEditShader(line);
                        break;
                    case "SetGMST":
                        FunctionSetESPVar(line, true);
                        break;
                    case "SetGlobal":
                        FunctionSetESPVar(line, false);
                        break;
                    case "SetPluginByte":
                        FunctionSetESPData(line, typeof(byte));
                        break;
                    case "SetPluginShort":
                        FunctionSetESPData(line, typeof(short));
                        break;
                    case "SetPluginInt":
                        FunctionSetESPData(line, typeof(int));
                        break;
                    case "SetPluginLong":
                        FunctionSetESPData(line, typeof(long));
                        break;
                    case "SetPluginFloat":
                        FunctionSetESPData(line, typeof(float));
                        break;
                    case "DisplayImage":
                        FunctionDisplayFile(line, true);
                        break;
                    case "DisplayText":
                        FunctionDisplayFile(line, false);
                        break;
                    case "SetVar":
                        FunctionSetVar(line);
                        break;
                    case "GetFolderName":
                    case "GetDirectoryName":
                        FunctionGetDirectoryName(line);
                        break;
                    case "GetFileName":
                        FunctionGetFileName(line);
                        break;
                    case "GetFileNameWithoutExtension":
                        FunctionGetFileNameWithoutExtension(line);
                        break;
                    case "CombinePaths":
                        FunctionCombinePaths(line);
                        break;
                    case "Substring":
                        FunctionSubRemoveString(line, false);
                        break;
                    case "RemoveString":
                        FunctionSubRemoveString(line, true);
                        break;
                    case "StringLength":
                        FunctionStringLength(line);
                        break;
                    case "InputString":
                        FunctionInputString(line);
                        break;
                    case "ReadINI":
                        FunctionReadINI(line);
                        break;
                    case "ReadRendererInfo":
                        FunctionReadRenderer(line);
                        break;
                    case "ExecLines":
                        FunctionExecLines(line, ref extraLines);
                        break;
                    case "iSet":
                        FunctionSet(line, true);
                        break;
                    case "fSet":
                        FunctionSet(line, false);
                        break;
                    case "EditXMLLine":
                        FunctionEditXMLLine(line);
                        break;
                    case "EditXMLReplace":
                        FunctionEditXMLReplace(line);
                        break;
                    case "AllowRunOnLines":
                        allowRunOnLines = true;
                        break;
                    default:
                        Warn($"Unrecognized function: {line[0]}!");
                        break;
                    }
                }

                if (Break || _srd.CancelInstall) break;
            }

            if (skipTo != null) Warn($"Expected: {skipTo}!");

            var temp = _srd;
            _srd = null;
            _variables = null;

            return temp;
        }

        private static void Warn(string msg)
        {
            if(Framework.EnableWarnings)
                _scriptFunctions.Warn($"'{msg}' at {_cLine}");
        }

        private static string[] SplitLine(string s)
        {
            var temp = new List<string>();
            bool wasLastSpace = false;
            bool inQuotes = false;
            bool wasLastEscape = false;
            bool doubleBreak = false;
            bool inVar = false;
            string currentWord = "";
            string currentVar = "";

            if (s == "") return new string[0];
            s += " ";
            foreach (var t in s)
            {
                switch (t)
                {
                    case '%':
                        wasLastSpace = false;
                        if (inVar)
                        {
                            if (_variables.ContainsKey(currentWord))
                                currentWord = currentVar + _variables[currentWord];
                            else
                                currentWord = currentVar + "%" + currentWord + "%";
                            currentVar = "";
                            inVar = false;
                        }
                        else
                        {
                            if (inQuotes && wasLastEscape)
                            {
                                currentWord += "%";
                            }
                            else
                            {
                                inVar = true;
                                currentVar = currentWord;
                                currentWord = "";
                            }
                        }

                        wasLastEscape = false;
                        break;
                    case ',':
                    case ' ':
                        wasLastEscape = false;
                        if (inVar)
                        {
                            currentWord = currentVar + "%" + currentWord;
                            currentVar = "";
                            inVar = false;
                        }

                        if (inQuotes)
                        {
                            currentWord += t;
                        }
                        else if (!wasLastSpace)
                        {
                            temp.Add(currentWord);

                            currentWord = "";
                            wasLastSpace = true;
                        }

                        break;
                    case ';':
                        wasLastEscape = false;
                        if (!inQuotes)
                        {
                            doubleBreak = true;
                        }
                        else
                            currentWord += t;

                        break;
                    case '"':
                        if (inQuotes && wasLastEscape)
                        {
                            currentWord += t;
                        }
                        else
                        {
                            if (inVar) Warn("String marker found in the middle of a variable name");
                            inQuotes = !inQuotes;
                        }

                        wasLastSpace = false;
                        wasLastEscape = false;
                        break;
                    case '\\':
                        if (inQuotes && wasLastEscape)
                        {
                            currentWord += t;
                            wasLastEscape = false;
                        }
                        else if (inQuotes)
                        {
                            wasLastEscape = true;
                        }
                        else
                        {
                            currentWord += t;
                        }

                        wasLastSpace = false;
                        break;
                    default:
                        wasLastEscape = false;
                        wasLastSpace = false;
                        currentWord += t;
                        break;
                }

                if (doubleBreak) break;
            }

            if (inVar) Warn("Unterminated variable");
            if (inQuotes) Warn("Unterminated quote");
            return temp.ToArray();
        }

        private static bool FunctionIf(IReadOnlyCollection<string> line)
        {
            if (line.Count == 1)
            {
                Warn("Missing arguments for 'If'");
                return false;
            }

            switch (line.ElementAt(1))
            {
            case "DialogYesNo":
                int dialogResult;
                switch (line.Count)
                {
                case 2:
                    Warn("Missing arguments for 'If DialogYesNo'");
                    return false;
                case 3:
                    dialogResult = _scriptFunctions.DialogYesNo(line.ElementAt(2));
                    if (dialogResult == -1)
                    {
                        _srd.CancelInstall = true;
                        return false;
                    }
                    else
                        return dialogResult == 1;
                case 4:
                    dialogResult = _scriptFunctions.DialogYesNo(line.ElementAt(2), line.ElementAt(3));
                    if (dialogResult == -1)
                    {
                        _srd.CancelInstall = true;
                        return false;
                    }
                    else
                        return dialogResult == 1;
                default:
                    Warn("Unexpected extra arguments after 'If DialogYesNo'");
                    goto case 4;
                }
            case "DataFileExists":
                if (line.Count != 2)
                    return _scriptFunctions.DataFileExists(line.ElementAt(2));

                Warn("Missing arguments for 'If DataFileExists'");
                return false;
            case "VersionLessThan":
            case "VersionGreaterThan":
                var funcName = line.ElementAt(1) == "VersionGreaterThan" ? "VersionGreaterThan" : "VersionLessThan";
                if (line.Count == 2)
                {
                    Warn($"Missing arguments for 'If {funcName}'");
                    return false;
                }

                try
                {
                    var v = new Version($"{line.ElementAt(2)}.0");
                    var v2 = new Version($"{Framework.Version}.0");
                    return line.ElementAt(1) == "VersionGreaterThan" ? v2 > v : v2 < v;
                }
                catch
                {
                    Warn($"Invalid argument for 'If {funcName}'");
                    return false;
                }
            case "ScriptExtenderPresent":
                if (line.Count > 2) Warn("Unexpected extra arguments for 'If ScriptExtenderPresent'");
                return _scriptFunctions.HasScriptExtender();
            case "ScriptExtenderNewerThan":
                if (line.Count == 2)
                {
                    Warn("Missing arguments for 'If ScriptExtenderNewerThan'");
                    return false;
                }
                if(line.Count > 3) Warn("Unexpected extra arguments for 'If ScriptExtenderNewerThan'");
                if (!_scriptFunctions.HasScriptExtender()) return false;
                try
                {
                    var v = _scriptFunctions.ScriptExtenderVersion();
                    var v2 = new Version(line.ElementAt(2));
                    return v >= v2;
                }
                catch
                {
                    Warn("Invalid argument for 'If ScriptExtenderNewerThan'");
                    return false;
                }
            case "GraphicsExtenderPresent":
                if (line.Count > 2) Warn("Unexpected arguments for 'If GraphicsExtenderPresent'");
                return _scriptFunctions.HasGraphicsExtender();
            case "GraphicsExtenderNewerThan":
                if (line.Count == 2)
                {
                    Warn("Missing arguments for 'If GraphicsExtenderNewerThan'");
                    return false;
                }
                if(line.Count > 3) Warn("Unexpected extra arguments for 'If GraphicsExtenderNewerThan'");
                if (!_scriptFunctions.HasGraphicsExtender()) return false;
                try
                {
                    var v = _scriptFunctions.GraphicsExtenderVersion();
                    var v2 = new Version(line.ElementAt(2));
                    return v >= v2;
                }
                catch
                {
                    Warn("Invalid argument for 'If GraphicsExtenderNewerThan'");
                    return false;
                }
            case "OblivionNewerThan":
                if (line.Count == 2)
                {
                    Warn("Missing arguments for 'If OblivionNewerThan'");
                    return false;
                }
                if(line.Count > 3) Warn("Unexpected extra arguments for 'If OblivionNewerThan'");
                try
                {
                    var v = _scriptFunctions.OblivionVersion();
                    var v2 = new Version(line.ElementAt(2));
                    return v >= v2;
                }
                catch
                {
                    Warn("Invalid argument for 'If OblivionNewerThan'");
                    return false;
                }
            case "Equal":
                if (line.Count >= 4)
                    return line.ElementAt(2) == line.ElementAt(3);

                Warn("Missing arguments for 'If Equal'");
                return false;
            case "GreaterEqual":
            case "GreaterThan":
                if (line.Count < 4)
                {
                    Warn("Missing arguments for 'If Greater'");
                    return false;
                }
                if(line.Count > 4) Warn("Unexpected extra arguments for 'If Greater'");
                if (!int.TryParse(line.ElementAt(2), out var iArg1) || !int.TryParse(line.ElementAt(3), out var iArg2))
                {
                    Warn("Invalid argument supplied to function 'If Greater'");
                    return false;
                }

                if (line.ElementAt(1) == "GreaterEqual") return iArg1 >= iArg2;
                else return iArg1 > iArg2;
            case "fGreaterEqual":
            case "fGreaterThan":
                if (line.Count < 4)
                {
                    Warn("Missing arguments for 'If fGreater'");
                    return false;
                }
                if(line.Count > 4) Warn("Unexpected extra arguments for 'If fGreater'");
                if (!double.TryParse(line.ElementAt(2), out var fArg1) || !double.TryParse(line.ElementAt(3), out var fArg2))
                {
                    Warn("Invalid argument supplied to function 'If fGreater'");
                    return false;
                }

                if (line.ElementAt(1) == "fGreaterEqual") return fArg1 >= fArg2;
                else return fArg1 > fArg2;
            default:
                Warn($"Unknown argument '{line.ElementAt(1)}' for 'If'");
                return false;
            }
        }

        private static FlowControlStruct FunctionFor(IList<string> line, int lineNo)
        {
            var nullLoop = new FlowControlStruct(3);
            if (line.Count < 3)
            {
                Warn("Missing arguments for 'For'");
                return nullLoop;
            }

            if (line[1] == "Each") line[1] = line[2];
            switch (line[1])
            {
            case "Count":
            {
                if (line.Count < 5)
                {
                    Warn("Missing arguments to function 'For Count'");
                    return nullLoop;
                }
                if (line.Count > 6) Warn("Unexpected extra arguments for 'For Count'");
                int step = 1;
                if (!int.TryParse(line[3], out var start) || !int.TryParse(line[4], out var end) ||
                    line.Count >= 6 && !int.TryParse(line[5], out step))
                {
                    Warn("Invalid argument to 'For Count'");
                    return nullLoop;
                }
                var steps = new List<string>();
                for (int i = start; i < +end; i += step)
                {
                    steps.Add(i.ToString());
                }

                return new FlowControlStruct(steps.ToArray(), line[2], lineNo);
            }
            case "DataFolder":
            case "PluginFolder":
            case "DataFile":
            case "Plugin":
            {
                string root;
                if (line[1] == "DataFolder" || line[1] == "DataFile")
                    root = _dataFiles;
                else
                    root = _plugins;

                if (line.Count < 5)
                {
                    Warn($"Missing arguments for 'For Each {line[1]}'");
                    return nullLoop;
                }
                if(line.Count > 7) Warn($"Unexpected extra arguments to 'For Each {line[1]}'");
                if (!Utils.IsSafeFolderName(line[4]))
                {
                    Warn($"Invalid argument for 'For Each {line[1]}'\nDirectory '{line[4]}' is not valid");
                    return nullLoop;
                }

                if (!Directory.Exists(Path.Combine(root, line[4])))
                {
                    Warn($"Invalid argument for 'For Each {line[1]}'\nDirectory '{line[4]}' does not exist");
                }

                var option = SearchOption.TopDirectoryOnly;
                if (line.Count > 5)
                {
                    switch (line[5])
                    {
                    case "True":
                        option = SearchOption.AllDirectories;
                        break;
                    case "False":
                        break;
                    default:
                        Warn($"Invalid argument '{line[5]}' for 'For Each {line[1]}'.\nExpected 'True' or 'False'");
                        break;
                    }
                }

                try
                {
                    var paths = Directory.GetDirectories(Path.Combine(root, line[4]),
                        line.Count > 6 ? line[6] : "*", option);
                    for (var i = 0; i < paths.Length; i++)
                    {
                        if (Path.IsPathRooted(paths[i]))
                            paths[i] = paths[i].Substring(root.Length);
                    }
                    return new FlowControlStruct(paths, line[3], lineNo);
                }
                catch
                {
                    Warn($"Invalid argument for 'For Each {line[1]}'");
                    return nullLoop;
                }
            }
            default:
                Warn("Unexpected function for 'For'");
                return nullLoop;
            }
        }

        private static string[] FunctionSelect(IList<string> line, bool isMultiSelect, bool hasPreviews, bool hasDescriptions)
        {
            if (line.Count < 3)
            {
                Warn("Missing arguments for 'Select'");
                return new string[0];
            }

            int argsPerOption = 1 + (hasPreviews ? 1 : 0) + (hasDescriptions ? 1 : 0);

            var title = line[1];
            var items = new List<string>(line.Count - 2);
            var line1 = line;
            line.Where(s => line1.IndexOf(s) >= 2).Do(items.Add);
            line = items;

            if (line.Count % argsPerOption != 0)
            {
                Warn("Unexpected extra arguments for 'Select'");
                do
                {
                    line.RemoveAt(line.Count - line.Count % argsPerOption);
                } while (line.Count % argsPerOption != 0);
            }

            items = new List<string>(line.Count/argsPerOption);
            var previews = hasPreviews ? new List<string>(line.Count / argsPerOption) : null;
            var descriptions = hasDescriptions ? new List<string>(line.Count / argsPerOption) : null;

            for (var i = 0; i < line.Count / argsPerOption; i++)
            {
                items.Add(line[i * argsPerOption]);
                if (hasPreviews)
                {
                    previews.Add(line[i * argsPerOption + 1]);
                    if (hasDescriptions) descriptions.Add(line[i * argsPerOption + 2]);
                }
                else
                {
                    if (hasDescriptions) descriptions.Add(line[i * argsPerOption + 1]);
                }
            }

            if (previews != null)
            {
                for (var i = 0; i < previews.Count; i++)
                {
                    if (previews[i] == "None")
                    {
                        previews[i] = null;
                    } else if (!Utils.IsSafeFileName(previews[i])) {
                        Warn($"Preview file path '{previews[i]}' is invalid");
                        previews[i] = null;
                    } else if (!File.Exists(Path.Combine(_dataFiles, previews[i]))) {
                        Warn($"Preview file path '{previews[i]}' does not exist");
                        previews[i] = null;
                    }
                    else
                    {
                        previews[i] = Path.Combine(_dataFiles, previews[i]);
                    }
                }
            }

            var selectedIndex = _scriptFunctions.Select(items, title, isMultiSelect, previews, descriptions);
            if (selectedIndex == null || selectedIndex.Count == 0)
            {
                _srd.CancelInstall = true;
                return new string[0];
            }

            var result = new string[selectedIndex.Count];
            for (int i = 0; i < selectedIndex.Count; i++)
            {
                result[i] = $"Case {items[selectedIndex[i]]}";
            }

            return result;
        }

        private static string[] FunctionSelectVar(IReadOnlyList<string> line, bool isVariable)
        {
            string funcName = isVariable ? "SelectVar" : "SelectString";
            if (line.Count < 2)
            {
                Warn($"Missing arguments for '{funcName}'");
                return new string[0];
            }

            if(line.Count > 2) Warn($"Unexpected arguments for '{funcName}'");
            if (!isVariable)
                return new[] {$"Case {line[1]}"};

            if (_variables.ContainsKey(line[1]))
                return new[] {$"Case {_variables[line[1]]}"};

            Warn($"Invalid argument for '{funcName}'\nVariable '{line[1]}' does not exist");
            return new string[0];

        }

        private static void FunctionMessage(IReadOnlyList<string> line)
        {
            switch(line.Count)
            {
            case 1:
                Warn("Missing arguments to function 'Message'");
                break;
            case 2:
                _scriptFunctions.Message(line[1]);
                break;
            case 3:
                _scriptFunctions.Message(line[1], line[2]);
                break;
            default:
                _scriptFunctions.Message(line[1], line[2]);
                Warn("Unexpected arguments after 'Message'");
                break;
            }
        }

        private static void FunctionSetVar(IReadOnlyList<string> line)
        {
            if (line.Count < 3)
            {
                Warn("Missing arguments for 'SetVar'");
                return;
            }

            if(line.Count > 3) Warn("Unexpected extra arguments for 'SetVar'");
            _variables[line[1]] = line[2];
        }

        private static void FunctionCombinePaths(IReadOnlyList<string> line)
        {
            if (line.Count < 4)
            {
                Warn("Missing arguments for 'CombinePaths'");
                return;
            }

            if(line.Count > 4) Warn("Unexpected arguments for 'CombinePaths'");
            try
            {
                _variables[line[1]] = Path.Combine(line[2], line[3]);
            }
            catch
            {
                Warn("Invalid arguments for 'CombinePaths'");
            }
        }

        private static void FunctionSubRemoveString(IList<string> line, bool remove)
        {
            string funcName = remove ? "RemoveString" : "Substring";
            if (line.Count < 4)
            {
                Warn($"Missing arguments for '{funcName}'");
                return;
            }

            if (line.Count > 5) Warn($"Unexpected extra arguments for '{funcName}'");
            if (line.Count == 4)
            {
                if (!int.TryParse(line[3], out int start))
                {
                    Warn($"Invalid arguments for '{funcName}'");
                    return;
                }

                _variables[line[1]] = remove ? line[2].Remove(start) : line[2].Substring(start);
            }
            else
            {
                if (!int.TryParse(line[3], out int start) || !int.TryParse(line[4], out int end))
                {
                    Warn($"Invalid arguments for '{funcName}'");
                    return;
                }
                _variables[line[1]] = remove ? line[2].Remove(start,end) : line[2].Substring(start, end);
            }
        }

        private static void FunctionStringLength(IList<string> line)
        {
            if (line.Count < 3)
            {
                Warn("Missing arguments for 'StringLength'");
                return;
            }

            if(line.Count > 3) Warn("Unexpected extra arguments for 'StringLength'");
            _variables[line[1]] = line[2].Length.ToString();
        }

        private static int Set(List<string> func)
        {
            if (func.Count == 0) throw new OMODFrameworkException($"Empty iSet in script at {_cLine}");
            if (func.Count == 1) return int.Parse(func[0]);

            var index = func.IndexOf("(");
            while (index != -1)
            {
                int count = 1;
                var newFunc = new List<string>();
                for (int i = index + 1; i < func.Count; i++)
                {
                    if (func[i] == "(") count++;
                    else if (func[i] == ")") count--;

                    if (count != 0)
                        continue;

                    func.RemoveRange(index, (i-index) +1);
                    func.Insert(index, Set(newFunc).ToString());
                    break;
                }

                if(count != 0) throw new OMODFrameworkException($"Mismatched brackets in script at {_cLine}");
                index = func.IndexOf("(");
            }

            //not
            index = func.IndexOf("not");
            while (index != -1)
            {
                int i = int.Parse(func[index + 1]);
                i = ~i;
                func[index + 1] = i.ToString();
                func.RemoveAt(index);
                index = func.IndexOf("not");
            }

            //and
            index = func.IndexOf("not");
            while (index != -1)
            {
                int i = int.Parse(func[index - 1]) & int.Parse(func[index + 1]);
                func[index + 1] = i.ToString();
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("not");
            }

            //or
            index = func.IndexOf("or");
            while (index != -1)
            {
                int i = int.Parse(func[index - 1]) | int.Parse(func[index + 1]);
                func[index + 1] = i.ToString();
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("or");
            }

            //xor
            index = func.IndexOf("xor");
            while (index != -1)
            {
                int i = int.Parse(func[index - 1]) ^ int.Parse(func[index + 1]);
                func[index + 1] = i.ToString();
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("xor");
            }

            //mod
            index = func.IndexOf("mod");
            while (index != -1)
            {
                int i = int.Parse(func[index - 1]) % int.Parse(func[index + 1]);
                func[index + 1] = i.ToString();
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("mod");
            }

            //mod
            index = func.IndexOf("%");
            while (index != -1)
            {
                int i = int.Parse(func[index - 1]) % int.Parse(func[index + 1]);
                func[index + 1] = i.ToString();
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("%");
            }

            //power
            index = func.IndexOf("^");
            while (index != -1)
            {
                int i = (int)Math.Pow(int.Parse(func[index - 1]), int.Parse(func[index + 1]));
                func[index + 1] = i.ToString();
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("^");
            }

            //division
            index = func.IndexOf("/");
            while (index != -1)
            {
                int i = int.Parse(func[index - 1]) / int.Parse(func[index + 1]);
                func[index + 1] = i.ToString();
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("/");
            }

            //multiplication
            index = func.IndexOf("*");
            while (index != -1)
            {
                int i = int.Parse(func[index - 1]) * int.Parse(func[index + 1]);
                func[index + 1] = i.ToString();
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("*");
            }

            //add
            index = func.IndexOf("+");
            while (index != -1)
            {
                int i = int.Parse(func[index - 1]) + int.Parse(func[index + 1]);
                func[index + 1] = i.ToString();
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("+");
            }

            //sub
            index = func.IndexOf("-");
            while (index != -1)
            {
                int i = int.Parse(func[index - 1]) - int.Parse(func[index + 1]);
                func[index + 1] = i.ToString();
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("-");
            }

            if(func.Count != 1) throw new OMODFrameworkException($"Leftovers in iSet function for script at {_cLine}");
            return int.Parse(func[0]);
        }

        private static double FSet(List<string> func)
        {
            if (func.Count == 0) throw new OMODFrameworkException($"Empty fSet in script at {_cLine}");
            if (func.Count == 1) return int.Parse(func[0]);
            //check for brackets

            var index = func.IndexOf("(");
            while (index != -1)
            {
                int count = 1;
                var newFunc = new List<string>();
                for (int i = index; i < func.Count; i++)
                {
                    if (func[i] == "(") count++;
                    else if (func[i] == ")") count--;
                    if (count == 0)
                    {
                        func.RemoveRange(index, i - index);
                        func.Insert(index, FSet(newFunc).ToString(CultureInfo.CurrentCulture));
                        break;
                    }

                    newFunc.Add(func[i]);
                }

                if (count != 0) throw new OMODFrameworkException($"Mismatched brackets in script at {_cLine}");
                index = func.IndexOf("(");
            }

            //sin
            index = func.IndexOf("sin");
            while (index != -1)
            {
                func[index + 1] = Math.Sin(double.Parse(func[index + 1])).ToString(CultureInfo.CurrentCulture);
                func.RemoveAt(index);
                index = func.IndexOf("sin");
            }

            //cos
            index = func.IndexOf("cos");
            while (index != -1)
            {
                func[index + 1] = Math.Cos(double.Parse(func[index + 1])).ToString(CultureInfo.CurrentCulture);
                func.RemoveAt(index);
                index = func.IndexOf("cos");
            }

            //tan
            index = func.IndexOf("tan");
            while (index != -1)
            {
                func[index + 1] = Math.Tan(double.Parse(func[index + 1])).ToString(CultureInfo.CurrentCulture);
                func.RemoveAt(index);
                index = func.IndexOf("tan");
            }

            //sinh
            index = func.IndexOf("sinh");
            while (index != -1)
            {
                func[index + 1] = Math.Sinh(double.Parse(func[index + 1])).ToString(CultureInfo.CurrentCulture);
                func.RemoveAt(index);
                index = func.IndexOf("sinh");
            }

            //cosh
            index = func.IndexOf("cosh");
            while (index != -1)
            {
                func[index + 1] = Math.Cosh(double.Parse(func[index + 1])).ToString(CultureInfo.CurrentCulture);
                func.RemoveAt(index);
                index = func.IndexOf("cosh");
            }

            //tanh
            index = func.IndexOf("tanh");
            while (index != -1)
            {
                func[index + 1] = Math.Tanh(double.Parse(func[index + 1])).ToString(CultureInfo.CurrentCulture);
                func.RemoveAt(index);
                index = func.IndexOf("tanh");
            }

            //exp
            index = func.IndexOf("exp");
            while (index != -1)
            {
                func[index + 1] = Math.Exp(double.Parse(func[index + 1])).ToString(CultureInfo.CurrentCulture);
                func.RemoveAt(index);
                index = func.IndexOf("exp");
            }

            //log
            index = func.IndexOf("log");
            while (index != -1)
            {
                func[index + 1] = Math.Log10(double.Parse(func[index + 1])).ToString(CultureInfo.CurrentCulture);
                func.RemoveAt(index);
                index = func.IndexOf("log");
            }

            //ln
            index = func.IndexOf("ln");
            while (index != -1)
            {
                func[index + 1] = Math.Log(double.Parse(func[index + 1])).ToString(CultureInfo.CurrentCulture);
                func.RemoveAt(index);
                index = func.IndexOf("ln");
            }

            //mod
            index = func.IndexOf("mod");
            while (index != -1)
            {
                double i = double.Parse(func[index - 1]) % double.Parse(func[index + 1]);
                func[index + 1] = i.ToString(CultureInfo.CurrentCulture);
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("mod");
            }

            //mod2
            index = func.IndexOf("%");
            while (index != -1)
            {
                double i = double.Parse(func[index - 1]) % double.Parse(func[index + 1]);
                func[index + 1] = i.ToString(CultureInfo.CurrentCulture);
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("%");
            }

            //power
            index = func.IndexOf("^");
            while (index != -1)
            {
                double i = Math.Pow(double.Parse(func[index - 1]), double.Parse(func[index + 1]));
                func[index + 1] = i.ToString(CultureInfo.CurrentCulture);
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("^");
            }

            //division
            index = func.IndexOf("/");
            while (index != -1)
            {
                double i = double.Parse(func[index - 1]) / double.Parse(func[index + 1]);
                func[index + 1] = i.ToString(CultureInfo.CurrentCulture);
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("/");
            }

            //multiplication
            index = func.IndexOf("*");
            while (index != -1)
            {
                double i = double.Parse(func[index - 1]) * double.Parse(func[index + 1]);
                func[index + 1] = i.ToString(CultureInfo.CurrentCulture);
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("*");
            }

            //add
            index = func.IndexOf("+");
            while (index != -1)
            {
                double i = double.Parse(func[index - 1]) + double.Parse(func[index + 1]);
                func[index + 1] = i.ToString(CultureInfo.CurrentCulture);
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("+");
            }

            //sub
            index = func.IndexOf("-");
            while (index != -1)
            {
                double i = double.Parse(func[index - 1]) - double.Parse(func[index + 1]);
                func[index + 1] = i.ToString(CultureInfo.CurrentCulture);
                func.RemoveRange(index - 1, 2);
                index = func.IndexOf("-");
            }

            if (func.Count != 1) throw new OMODFrameworkException($"Leftovers in iSet function for script at {_cLine}");
            return double.Parse(func[0]);
        }

        private static void FunctionSet(IReadOnlyList<string> line, bool integer)
        {
            if (line.Count < 3)
            {
                Warn("Missing arguments for "+(integer ? "iSet":"fSet"));
                return;
            }

            var func = new List<string>();
            for(int i = 2; i < line.Count; i++) func.Add(line[i]);
            try
            {
                string result;
                if (integer)
                {
                    int i = Set(func);
                    result = i.ToString();
                }
                else
                {
                    float f = (float)FSet(func);
                    result = f.ToString(CultureInfo.CurrentCulture);
                }

                _variables[line[1]] = result;
            } catch
            {
                Warn("Invalid arguments for "+(integer ? "iSet":"fSet"));
            }
        }

        private static void FunctionExecLines(IList<string> line, ref Queue<string> queue)
        {
            if (line.Count < 2)
            {
                Warn("Missing arguments for 'ExecLines'");
                return;
            }

            if (line.Count > 2) Warn("Unexpected extra arguments for 'ExecLines'");
            string[] lines = line[1].Split(new[] {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries);
            lines.Do(queue.Enqueue);
        }

        private static void FunctionLoadEarly(IList<string> line)
        {
            if (line.Count < 2)
            {
                Warn("Missing arguments for 'LoadEarly'");
                return;
            }

            if (line.Count > 2)
            {
                Warn("Unexpected arguments for 'LoadEarly'");
            }

            line[1] = line[1].ToLower();
            if (!_srd.EarlyPlugins.Contains(line[1]))
                _srd.EarlyPlugins.Add(line[1]);
        }

        private static void FunctionLoadOrder(IReadOnlyList<string> line, bool loadAfter)
        {
            string funcName = loadAfter ? "LoadAfter" : "LoadEarly";
            if (line.Count < 3)
            {
                Warn($"Missing arguments for '{funcName}'");
                return;
            }

            if (line.Count > 3)
            {
                Warn($"Unexpected arguments for '{funcName}'");
            }

            _srd.LoadOrderSet.Add(new PluginLoadInfo(line[1], line[2], loadAfter));
        }

        private static void FunctionConflicts(IReadOnlyList<string> line, bool conflicts, bool regex)
        {
            var funcName = conflicts ? "ConflictsWith" : "DependsOn";
            if (regex) funcName += "Regex";

            var cd = new ConflictData {Level = ConflictLevel.MajorConflict};
            switch (line.Count)
            {
            case 1:
                Warn($"Missing arguments for '${funcName}'");
                return;
            case 2:
                cd.File = line[1];
                break;
            case 3:
                cd.Comment = line[2];
                goto case 2;
            case 4:
                switch (line[3])
                {
                case "Unusable":
                    cd.Level = ConflictLevel.Unusable;
                    break;
                case "Major":
                    cd.Level = ConflictLevel.MajorConflict;
                    break;
                case "Minor":
                    cd.Level = ConflictLevel.MinorConflict;
                    break;
                default:
                    Warn($"Unknown conflict level after '{funcName}'");
                    break;
                }

                goto case 3;
            case 5:
                Warn($"Unexpected arguments for '{funcName}'");
                break;
            case 6:
                cd.File = line[1];
                try
                {
                    cd.MinMajorVersion = Convert.ToInt32(line[2]);
                    cd.MinMinorVersion = Convert.ToInt32(line[3]);
                    cd.MaxMajorVersion = Convert.ToInt32(line[4]);
                    cd.MaxMinorVersion = Convert.ToInt32(line[5]);
                }
                catch
                {
                    Warn($"Arguments for '{funcName}' could not been parsed");
                }

                break;
            case 7:
                cd.Comment = line[6];
                goto case 6;
            case 8:
                switch (line[7])
                {
                case "Unusable":
                    cd.Level = ConflictLevel.Unusable;
                    break;
                case "Major":
                    cd.Level = ConflictLevel.MajorConflict;
                    break;
                case "Minor":
                    cd.Level = ConflictLevel.MinorConflict;
                    break;
                default:
                    Warn($"Unknown conflict level after '{funcName}'");
                    break;
                }

                goto case 7;
            default:
                Warn($"Unexpected arguments for '{funcName}'");
                goto case 8;
            }

            cd.Partial = regex;
            if (conflicts)
                _srd.ConflictsWith.Add(cd);
            else
                _srd.DependsOn.Add(cd);
        }

        private static void FunctionUncheckESP(IList<string> line)
        {
            if (line.Count == 1)
            {
                Warn("Missing arguments for 'UncheckESP'");
                return;
            }

            if(line.Count > 2) Warn("Unexpected arguments for 'UncheckESP'");
            if (!File.Exists(Path.Combine(_plugins, line[1])))
            {
                Warn($"Invalid argument for 'UncheckESP': {line[1]} does not exist");
                return;
            }

            line[1] = line[1].ToLower();
            if (!_srd.UncheckedPlugins.Contains(line[1]))
                _srd.UncheckedPlugins.Add(line[1]);
        }

        private static void FunctionSetDeactivationWarning(IList<string> line)
        {
            if (line.Count < 3)
            {
                Warn("Missing arguments for 'SetDeactivationWarning'");
                return;
            }

            if(line.Count > 3) Warn("Unexpected arguments for 'SetDeactivationWarning'");
            if (!File.Exists(Path.Combine(_plugins, line[1])))
            {
                Warn($"Invalid argument for 'SetDeactivationWarning'\nFile '{line[1]}' does not exist");
                return;
            }

            line[1] = line[1].ToLower();

            _srd.ESPDeactivation.RemoveWhere(a => a.Plugin == line[1]);
            switch (line[2])
            {
            case "Allow":
                _srd.ESPDeactivation.Add(new ScriptESPWarnAgainst(line[1], DeactivationStatus.Allow));
                break;
            case "WarnAgainst":
                _srd.ESPDeactivation.Add(new ScriptESPWarnAgainst(line[1], DeactivationStatus.WarnAgainst));
                break;
            case "Disallow":
                _srd.ESPDeactivation.Add(new ScriptESPWarnAgainst(line[1], DeactivationStatus.Disallow));
                break;
            default:
                Warn("Invalid argument for 'SetDeactivationWarning'");
                return;
            }
        }

        private static void FunctionEditXMLLine(IList<string> line)
        {
            if (line.Count < 4)
            {
                Warn("Missing arguments for 'EditXMLLine'");
                return;
            }

            var file = Path.Combine(_dataFiles, line[1]);

            if (line.Count > 4) Warn("Unexpected extra arguments for 'EditXMLLine'");
            line[1] = line[1].ToLower();
            if (!Utils.IsSafeFileName(line[1]) || !File.Exists(file))
            {
                Warn("Invalid filename supplied for 'EditXMLLine'");
                return;
            }

            var ext = Path.GetExtension(line[1]);
            if (ext != ".xml" && ext != ".txt" && ext != ".ini" && ext != ".bat")
            {
                Warn("Invalid filename supplied for 'EditXMLLine'");
                return;
            }

            if (!int.TryParse(line[2], out var index) || index < 1)
            {
                Warn("Invalid line number supplied for 'EditXMLLine'");
                return;
            }

            index -= 1;
            var lines = File.ReadAllLines(file);
            if (lines.Length <= index)
            {
                Warn("Invalid line number supplied for 'EditXMLLine'");
                return;
            }

            lines[index] = line[3];
            File.WriteAllLines(file, lines);
        }

        private static void FunctionEditXMLReplace(IList<string> line)
        {
            if (line.Count < 4)
            {
                Warn("Missing arguments for 'EditXMLReplace'");
                return;
            }

            var file = Path.Combine(_dataFiles, line[1]);
            if (line.Count > 4) Warn("Unexpected extra arguments for 'EditXMLReplace'");
            line[1] = line[1].ToLower();
            if (!Utils.IsSafeFileName(line[1]) || !File.Exists(file))
            {
                Warn("Invalid filename supplied for 'EditXMLReplace'");
                return;
            }

            var ext = Path.GetExtension(file);
            if (ext != ".xml" && ext != ".txt" && ext != ".ini" && ext != ".bat")
            {
                Warn("Invalid filename supplied for 'EditXMLLine'");
                return;
            }

            var text = File.ReadAllText(file);
            text = text.Replace(line[2], line[3]);
            File.WriteAllText(file, text);
        }

        private static void FunctionModifyInstall(IReadOnlyCollection<string> line, bool plugins, bool install)
        {
            var funcName = install ? "Install" : "DontInstall";
            funcName += plugins ? "Plugin" : "DataFile";

            if (line.Count == 1)
            {
                Warn($"Missing arguments for '{funcName}'");
                return;
            }

            var l = line.ElementAt(1).ToLower();
            if(line.Count > 2) Warn($"Unexpected arguments for '{funcName}'");
            if (plugins)
            {
                var path = Path.Combine(_plugins, l);
                if (!File.Exists(path))
                {
                    Warn($"Invalid argument for '{funcName}'\nFile '{path}' does not exist");
                    return;
                }

                if (l.IndexOfAny(new [] {Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar}) != -1)
                {
                    Warn($"Invalid argument for '{funcName}'\nThis function cannot be used on plugins stored in subdirectories");
                }

                if (install)
                {
                    _srd.IgnorePlugins.RemoveWhere(s => s == l);
                    if (!_srd.InstallPlugins.Contains(l))
                        _srd.InstallPlugins.Add(l);
                }
                else
                {
                    _srd.InstallPlugins.RemoveWhere(s => s == l);
                    if (!_srd.IgnorePlugins.Contains(l))
                        _srd.IgnorePlugins.Add(l);
                }
            }
            else
            {
                var path = Path.Combine(_dataFiles, l);
                if(!File.Exists(path)) {
                    Warn($"Invalid argument for '{funcName}'\nFile '{path}' does not exist");
                    return;
                }

                if (install)
                {
                    _srd.IgnoreData.RemoveWhere(s => s == l);
                    if (!_srd.InstallData.Contains(l))
                        _srd.InstallData.Add(l);
                } else
                {
                    _srd.InstallData.RemoveWhere(s => s == l);
                    if (!_srd.IgnoreData.Contains(l))
                        _srd.IgnoreData.Add(l);
                }
            }
        }

        private static void FunctionModifyInstallFolder(IList<string> line, bool install)
        {
            var funcName = (install ? "Install" : "DontInstall") + "DataFolder";

            if (line.Count == 1)
            {
                Warn($"Missing arguments for '{funcName}'");
                return;
            }
            if(line.Count > 3) Warn($"Unexpected arguments for '{funcName}'");

            line[1] = Utils.MakeValidFolderPath(line[1]);
            var path = Path.Combine(_dataFiles, line[1]);

            if (!Directory.Exists(path))
            {
                Warn($"Invalid argument for '{funcName}'\nFolder '{path}' does not exist");
                return;
            }

            if (line.Count >= 2)
            {
                switch (line[2])
                {
                case "True":
                    Directory.GetDirectories(path).Do(d =>
                        FunctionModifyInstallFolder(
                            new List<string> {"", d.Substring(_dataFiles.Length), "True"}, install));
                    break;
                case "False":
                    break;
                default:
                    Warn($"Invalid argument for '{funcName}'\nExpected True or False");
                    break;
                }
            }
            Directory.GetFiles(path).Do(f =>
            {
                var name = Path.GetFileName(f);
                if (install)
                {
                    _srd.IgnoreData.RemoveWhere(s => s == name);
                    if (!_srd.InstallData.Contains(name))
                        _srd.InstallData.Add(name);
                }
                else
                {
                    _srd.InstallData.RemoveWhere(s => s == name);
                    if (!_srd.IgnoreData.Contains(name))
                        _srd.IgnoreData.Add(name);
                }
            });
        }

        private static void FunctionCopyDataFile(IReadOnlyCollection<string> line, bool plugin)
        {
            var funcName = "Copy";
            funcName += plugin ? "Plugin" : "DataFile";

            if (line.Count < 3)
            {
                Warn($"Missing arguments for '{funcName}'");
                return;
            }

            if(line.Count > 3) Warn($"Unexpected arguments for '{funcName}'");
            var from = line.ElementAt(1);
            var to = line.ElementAt(2);

            if (!Utils.IsSafeFileName(from) || !Utils.IsSafeFileName(to))
            {
                Warn($"Invalid argument for '{funcName}'");
                return;
            }

            if (from == to)
            {
                Warn($"Invalid argument for '{funcName}'\nYou can not copy a file over itself");
                return;
            }

            if(plugin)
            {
                var path = Path.Combine(_plugins, from);
                if (!File.Exists(path))
                {
                    Warn($"Invalid argument for '{funcName}'\nFile '{from}' does not exist");
                    return;
                }

                if (to.IndexOfAny(new [] {Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar}) != -1)
                {
                    Warn("Plugins cannot be copied to subdirectories of the data folder");
                    return;
                }

                if (!(to.EndsWith("esp") || to.EndsWith(".esm")))
                {
                    Warn("Copied plugins must have a .esp or .esm extension");
                    return;
                }
            }
            else
            {
                var path = Path.Combine(_dataFiles, from);
                if (!File.Exists(path))
                {
                    Warn($"Invalid argument for '{funcName}'\nFile '{from}' does not exist");
                    return;
                }

                if (to.EndsWith("esp") || to.EndsWith(".esm"))
                {
                    Warn("Copied data files cannot have a .esp or .esm extension");
                    return;
                }
            }

            if (plugin)
            {
                _srd.CopyPlugins.RemoveWhere(s => s.CopyTo == to.ToLower());
                _srd.CopyPlugins.Add(new ScriptCopyDataFile(from.ToLower(), to.ToLower()));
            }
            else
            {
                _srd.CopyDataFiles.RemoveWhere(s => s.CopyTo == to.ToLower());
                _srd.CopyDataFiles.Add(new ScriptCopyDataFile(from.ToLower(), to.ToLower()));
            }
        }

        private static void FunctionCopyDataFolder(IReadOnlyCollection<string> line)
        {
            if (line.Count < 3)
            {
                Warn("Missing arguments for 'CopyDataFolder'");
                return;
            }

            if(line.Count > 4) Warn("Unexpected arguments for 'CopyDataFolder'");
            var validFrom = Utils.MakeValidFolderPath(line.ElementAt(1).ToLower());
            var validTo = Utils.MakeValidFolderPath(line.ElementAt(2).ToLower());

            if (!Utils.IsSafeFolderName(validFrom) || !Utils.IsSafeFolderName(validTo))
            {
                Warn("Invalid argument for 'CopyDataFolder'");
                return;
            }

            var from = Path.Combine(_dataFiles, validFrom);
            var to = Path.Combine(_dataFiles, validTo);

            if(!Directory.Exists(from))
            {
                Warn($"Invalid argument for 'CopyDataFolder'\nFolder '{from}' does not exist!");
                return;
            }

            if (from == to)
            {
                Warn("Invalid argument for 'CopyDataFolder'\nYou cannot copy a folder over itself");
                return;
            }

            if (line.Count >= 4)
            {
                switch(line.ElementAt(3)) {
                case "True":
                    Directory.GetDirectories(from).Do(d =>
                    {
                        var arg2 = d.Substring(_dataFiles.Length);
                        if (arg2.StartsWith("\\"))
                            arg2 = arg2.Substring(1);
                        var l = _dataFiles.Length + line.ElementAt(1).Length;
                        var t = d.Substring(l);
                        var arg3 = line.ElementAt(2) + t;
                        FunctionCopyDataFolder(new [] {"", arg2, arg3, "True"});
                    });
                    break;
                case "False":
                    break;
                default:
                    Warn("Invalid argument for 'CopyDataFolder'\nExpected True or False");
                    return;
                }
            }

            Directory.GetFiles(from).Do(f =>
            {
                var fFrom = Path.Combine(line.ElementAt(1), Path.GetFileName(f));
                var fTo = Path.Combine(line.ElementAt(2), Path.GetFileName(f)).ToLower();

                _srd.CopyDataFiles.RemoveWhere(s => s.CopyTo == fTo);

                _srd.CopyDataFiles.Add(new ScriptCopyDataFile(fFrom, fTo));
            });
        }

        private static void FunctionGetDirectoryName(IReadOnlyCollection<string> line)
        {
            if (line.Count < 3)
            {
                Warn("Missing arguments for 'GetDirectoryName'");
                return;
            }

            if(line.Count > 3) Warn("Unexpected arguments for 'GetDirectoryName'");

            try
            {
                _variables[line.ElementAt(1)] = Path.GetDirectoryName(line.ElementAt(2));
            }
            catch
            {
                Warn("Invalid argument for 'GetDirectoryName'");
            }
        }

        private static void FunctionGetFileName(IReadOnlyCollection<string> line) {
            if (line.Count < 3)
            {
                Warn("Missing arguments for 'GetFileName'");
                return;
            }

            if (line.Count > 3) Warn("Unexpected arguments for 'GetFileName'");
            try
            {
                _variables[line.ElementAt(1)] = Path.GetFileName(line.ElementAt(2));
            }
            catch
            {
                Warn("Invalid argument for 'GetFileName'");
            }
        }

        private static void FunctionGetFileNameWithoutExtension(IReadOnlyCollection<string> line) {
            if (line.Count < 3)
            {
                Warn("Missing arguments for 'GetFileNameWithoutExtension'");
                return;
            }

            if (line.Count > 3) Warn("Unexpected arguments for 'GetFileNameWithoutExtension'");
            try
            {
                _variables[line.ElementAt(1)] = Path.GetFileName(line.ElementAt(2));
            }
            catch
            {
                Warn("Invalid argument for 'GetFileNameWithoutExtension'");
            }
        }

        private static void FunctionPatch(IReadOnlyCollection<string> line, bool plugin)
        {
            var funcName = "Patch";
            funcName += plugin ? "Plugin" : "DataFile";

            if (line.Count < 3)
            {
                Warn($"Missing arguments for '{funcName}'");
                return;
            }

            if(line.Count > 4) Warn($"Unexpected arguments for '{funcName}'");

            var from = line.ElementAt(1);
            var to = line.ElementAt(2);

            if (!Utils.IsSafeFileName(from) || !Utils.IsSafeFileName(to))
            {
                Warn($"Invalid argument for '{funcName}'");
                return;
            }

            var pathFrom = plugin ? Path.Combine(_plugins, from) : Path.Combine(_dataFiles, from);
            if(plugin) {
                if (!File.Exists(pathFrom))
                {
                    Warn($"Invalid argument for 'PatchPlugin'\nFile '{from}' does not exist");
                    return;
                }

                if (to.IndexOfAny(new[] {Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar}) != -1)
                {
                    Warn("Plugins cannot be copied to subdirectories of the data folder");
                    return;
                }

                if (!(to.EndsWith(".esp") || to.EndsWith(".esm")))
                {
                    Warn("Plugins must have a .esp or .esm extension");
                    return;
                }
            }
            else
            {
                if (!File.Exists(pathFrom))
                {
                    Warn($"Invalid argument to PatchDataFile\nFile '{from}' does not exist");
                    return;
                }

                if (to.EndsWith(".esp") || to.EndsWith(".esm"))
                {
                    Warn("Data files cannot have a .esp or .esm extension");
                    return;
                }
            }

            switch (Framework.CurrentPatchMethod)
            {
            case Framework.PatchMethod.CreatePatchGameFolder:
                if(string.IsNullOrWhiteSpace(Framework.OblivionGameFolder))
                    throw new OMODFrameworkException($"{Framework.OblivionGameFolder} can not be null or whitespace!");

                var patchFolder = Path.Combine(Framework.OblivionDataFolder, "Patch");

                if (!Directory.Exists(patchFolder))
                    Directory.CreateDirectory(patchFolder);

                var patchPath = Path.Combine(patchFolder, to);
                if(File.Exists(patchPath))
                    throw new OMODFrameworkException($"The file {patchPath} already exists");

                var toDataPath = Path.Combine(Framework.OblivionDataFolder, to);
                DateTime toTimeStamp = default;
                if (File.Exists(toDataPath))
                {
                    toTimeStamp = File.GetLastWriteTime(toDataPath);
                }

                try
                {
                    File.Copy(pathFrom, patchPath);
                    if(toTimeStamp != default)
                        File.SetLastWriteTime(patchPath, toTimeStamp);
                }
                catch (Exception e)
                {
                    throw new OMODFrameworkException($"The file {pathFrom} could not be copied to {patchPath}\n{e}");
                }

                break;
            case Framework.PatchMethod.OverwriteGameFolder:
                if(string.IsNullOrWhiteSpace(Framework.OblivionGameFolder))
                    throw new OMODFrameworkException($"{Framework.OblivionGameFolder} can not be null or whitespace!");

                var dataPath = Path.Combine(Framework.OblivionDataFolder, to);
                DateTime timeStamp = default;
                if (File.Exists(dataPath))
                {
                    timeStamp = File.GetLastWriteTime(dataPath);
                    try
                    {
                        File.Delete(dataPath);
                    }
                    catch (Exception e)
                    {
                        throw new OMODFrameworkException($"The file {dataPath} could not be deleted!\n{e}");
                    }
                }
                else if (line.Count < 4 || line.ElementAt(3) != "True") return;

                try
                {
                    File.Move(pathFrom, dataPath);
                    File.SetLastWriteTime(dataPath, timeStamp);
                }
                catch (Exception e)
                {
                    throw new OMODFrameworkException($"The file {pathFrom} could not be moved to {dataPath}\n{e}");
                }

                break;
            case Framework.PatchMethod.CreatePatchInMod:
                _srd.PatchFiles.RemoveWhere(s => s.CopyTo == to.ToLower());
                _srd.PatchFiles.Add(new ScriptCopyDataFile(from.ToLower(), to.ToLower()));
                break;
            case Framework.PatchMethod.PatchWithInterface:
                _scriptFunctions.Patch(pathFrom, to);
                break;
            default:
                throw new OMODFrameworkException("Unknown PatchMethod for Framework.CurrentPatchMethod!");
            }
        }

        private static void FunctionEditShader(IReadOnlyCollection<string> line)
        {
            if(line.Count < 4) {
                Warn("Missing arguments for 'EditShader'");
                return;
            }

            if (line.Count > 4) Warn("Unexpected arguments for 'EditShader'");
            var shaderPath = Path.Combine(_dataFiles, line.ElementAt(3));
            if (!Utils.IsSafeFileName(line.ElementAt(3)))
            {
                Warn($"Invalid argument for 'EditShader'\n'{line.ElementAt(3)}' is not a valid file name");
                return;
            }

            if (!File.Exists(shaderPath))
            {
                Warn($"Invalid argument for 'EditShader'\nFile '{line.ElementAt(3)}' does not exist");
                return;
            }

            if (!byte.TryParse(line.ElementAt(1), out var package))
            {
                Warn($"Invalid argument for function 'EditShader'\n'{line.ElementAt(1)}' is not a valid shader package ID");
                return;
            }

            _srd.SDPEdits.Add(new SDPEditInfo(package, line.ElementAt(2), shaderPath));

        }

        private static void FunctionEditINI(IReadOnlyCollection<string> line)
        {
            if (line.Count < 4)
            {
                Warn("Missing arguments for 'EditINI'");
                return;
            }

            if(line.Count > 4) Warn("Unexpected argument for EditINI");
            _srd.INIEdits.Add(new INIEditInfo(line.ElementAt(1), line.ElementAt(2), line.ElementAt(3)));
        }

        private static void FunctionSetESPVar(IReadOnlyCollection<string> line, bool gmst)
        {
            var funcName = "Set";
            funcName += gmst ? "GMST" : "Global";
            if (line.Count < 4)
            {
                Warn($"Missing argument for '{funcName}'");
                return;
            }

            if(line.Count > 4) Warn($"Unexpected extra arguments for '{funcName}'");
            if (!Utils.IsSafeFileName(line.ElementAt(1)))
            {
                Warn($"Illegal plugin name supplied to '{funcName}'");
                return;
            }

            if (!File.Exists(Path.Combine(_plugins, line.ElementAt(1))))
            {
                Warn($"Invalid argument for '{funcName}'\nFile '{line.ElementAt(1)}' does not exist");
                return;
            }

            _srd.ESPEdits.Add(new ScriptESPEdit(gmst, line.ElementAt(1).ToLower(), line.ElementAt(2).ToLower(),
                line.ElementAt(3)));
        }

        private static void FunctionSetESPData(IReadOnlyCollection<string> line, Type type)
        {
            var funcName = "SetPlugin";
            if (type == typeof(byte)) funcName += "Byte";
            else if (type == typeof(short)) funcName += "Short";
            else if (type == typeof(int)) funcName += "Int";
            else if (type == typeof(long)) funcName += "Long";
            else if (type == typeof(float)) funcName += "Float";

            if (line.Count < 4)
            {
                Warn($"Missing arguments for '{funcName}'");
                return;
            }

            if(line.Count > 4) Warn($"Unexpected extra arguments for '{funcName}'");

            var plugin = line.ElementAt(1);
            if (!Utils.IsSafeFileName(plugin))
            {
                Warn($"Illegal plugin name supplied to '{funcName}'");
                return;
            }

            var pluginPath = Path.Combine(_plugins, plugin);
            if (!File.Exists(pluginPath))
            {
                Warn($"Invalid argument for '{funcName}'\nFile {plugin} does not exist");
                return;
            }

            byte[] data = null;
            if (!long.TryParse(line.ElementAt(2), out var offset) || offset < 0)
            {
                Warn($"Invalid argument for '{funcName}'\nOffset {line.ElementAt(2)} is not valid");
                return;
            }

            var val = line.ElementAt(3);
            if (type == typeof(byte))
            {
                if (!byte.TryParse(val, out var value))
                {
                    Warn($"Invalid argument for '{funcName}'\nValue '{val}' is not valid");
                    return;
                }

                data = BitConverter.GetBytes(value);
            }

            if (type == typeof(short))
            {
                if (!short.TryParse(val, out var value))
                {
                    Warn($"Invalid argument for '{funcName}'\nValue '{val}' is not valid");
                    return;
                }

                data = BitConverter.GetBytes(value);
            }

            if (type == typeof(int))
            {
                if (!int.TryParse(val, out var value))
                {
                    Warn($"Invalid argument for '{funcName}'\nValue '{val}' is not valid");
                    return;
                }

                data = BitConverter.GetBytes(value);
            }

            if (type == typeof(long))
            {
                if (!long.TryParse(val, out var value))
                {
                    Warn($"Invalid argument for '{funcName}'\nValue '{val}' is not valid");
                    return;
                }

                data = BitConverter.GetBytes(value);
            }

            if (type == typeof(float))
            {
                if (!float.TryParse(val, out var value))
                {
                    Warn($"Invalid argument for '{funcName}'\nValue '{val}' is not valid");
                    return;
                }

                data = BitConverter.GetBytes(value);
            }

            if (data == null)
            {
                throw new OMODFrameworkException($"Data in '{funcName}' can not be null!");
            }

            using (var fs = File.OpenWrite(pluginPath))
            {
                if (offset + data.Length >= fs.Length)
                {
                    Warn($"Invalid argument for '{funcName}'\nOffset {line.ElementAt(2)} is out of range");
                    return;
                }

                fs.Position = offset;

                try
                {
                    fs.Write(data, 0, data.Length);
                }
                catch (Exception e)
                {
                    throw new OMODFrameworkException($"Could not write to file {pluginPath} in '{funcName}' at {_cLine}\n{e}");
                }
            }
        }

        private static void FunctionInputString(IReadOnlyCollection<string> line)
        {
            if (line.Count < 2)
            {
                Warn("Missing arguments for 'InputString'");
                return;
            }

            if(line.Count > 4) Warn("Unexpected arguments for 'InputString'");
            var title = line.Count > 2 ? line.ElementAt(2) : "";
            var initialText = line.Count > 3 ? line.ElementAt(3) : "";

            var result = _scriptFunctions.InputString(title, initialText, false);
            _variables[line.ElementAt(1)] = result ?? "";
        }

        private static void FunctionDisplayFile(IReadOnlyCollection<string> line, bool image)
        {
            var funcName = "Display";
            funcName += image ? "Image" : "Text";

            if (line.Count < 2)
            {
                Warn($"Missing arguments for '{funcName}'");
                return;
            }

            if(line.Count > 3) Warn($"Unexpected extra arguments for '{funcName}'");
            if (!Utils.IsSafeFileName(line.ElementAt(1)))
            {
                Warn($"Illegal path supplied to '{funcName}'");
                return;
            }

            var path = Path.Combine(_dataFiles, line.ElementAt(1));
            if (!File.Exists(path))
            {
                Warn($"Invalid argument for '{funcName}'\nFile {path} does not exist");
                return;
            }

            var title = line.Count > 2 ? line.ElementAt(2) : line.ElementAt(1);

            if(image)
                _scriptFunctions.DisplayImage(path, title);
            else
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                _scriptFunctions.DisplayText(text, title);
            }
        }

        private static void FunctionRegisterBSA(IReadOnlyCollection<string> line, bool register)
        {
            var funcName = register ? "Register" : "Unregister";
            funcName += "BSA";

            if (line.Count == 1)
            {
                Warn($"Missing arguments for '{funcName}'");
                return;
            }

            var esp = line.ElementAt(1).ToLower();
            if (esp.Contains(",") || esp.Contains(";") || esp.Contains("="))
            {
                Warn($"Invalid argument for '{funcName}'\nBSA file names are not allowed to include the characters ',' '=' or ';'");
                return;
            }

            if(line.Count > 2) Warn($"Unexpected arguments after '{funcName}'");

            if (register && !_srd.RegisterBSASet.Contains(esp))
                _srd.RegisterBSASet.Add(esp);
            else
                _srd.RegisterBSASet.RemoveWhere(s => s == esp);
        }

        private static void FunctionReadINI(IReadOnlyCollection<string> line)
        {
            if (line.Count < 4)
            {
                Warn("Missing arguments for 'ReadINI'");
                return;
            }

            if(line.Count > 4) Warn("Unexpected extra arguments for 'ReadINI'");

            switch (Framework.CurrentReadINIMethod)
            {
            case Framework.ReadINIMethod.ReadOriginalINI:
                _variables[line.ElementAt(1)] = OblivionINI.GetINIValue(line.ElementAt(2), line.ElementAt(3));
                break;
            case Framework.ReadINIMethod.ReadWithInterface:
                var s = _scriptFunctions.ReadOblivionINI(line.ElementAt(2), line.ElementAt(3));
                _variables[line.ElementAt(1)] = s ?? throw new OMODFrameworkException("Could not read the oblivion.ini file using the function IScriptFunctions.ReadOblivionINI");
                break;
            default:
                throw new OMODFrameworkException("Unknown ReadINIMethod for Framework.CurrentReadINIMethod!");
            }
            
        }

        private static void FunctionReadRenderer(IReadOnlyCollection<string> line)
        {
            if (line.Count < 3)
            {
                Warn("Missing arguments for 'ReadRendererInfo'");
                return;
            }

            if(line.Count > 3) Warn("Unexpected extra arguments for 'ReadRendererInfo'");

            switch (Framework.CurrentReadRendererMethod)
            {
            case Framework.ReadRendererMethod.ReadOriginalRenderer:
                _variables[line.ElementAt(1)] = OblivionRenderInfo.GetInfo(line.ElementAt(2));
                break;
            case Framework.ReadRendererMethod.ReadWithInterface:
                var s = _scriptFunctions.ReadRendererInfo(line.ElementAt(2));
                _variables[line.ElementAt(1)] = s ?? throw new OMODFrameworkException("Could not read the RenderInfo.txt file using the function IScriptFunctions.ReadRendererInfo");
                break;
            default:
                throw new OMODFrameworkException("Unknown ReadRendererMethod for Framework.CurrentReadRendererMethod!");
            }
        }
    }
}
