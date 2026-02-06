// Assets/AmplifyShaderEditor/Plugins/Editor/Nodes/Custom/AutoParsedExpressionNode.cs

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AmplifyShaderEditor
{
    [Serializable]
    [NodeAttributes(
        "Auto Parsed Expression",
        "Custom",
        "Auto-generates ports from //@in //@out comments. Supports helper functions + main(). Renames all functions uniquely to avoid collisions."
    )]
    public class AutoParsedExpressionNode : ParentNode
    {
        private const string InTag = @"//@in";
        private const string OutTag = @"//@out";

        [SerializeField] private string m_code = DefaultCode();
        [SerializeField] private string m_cachedSignature = "";
        [SerializeField] private bool m_pendingRebuild = true;
        [SerializeField] private string m_customNodeName = "";

        [NonSerialized] private bool m_rebuildRequested = false;
        [NonSerialized] private Vector2 m_scroll;

        protected override void CommonInit(int uniqueId)
        {
            base.CommonInit(uniqueId);

            // We'll manage ports ourselves
            m_useInternalPortData = true;

            // Delay rebuild to logic update (safe)
            m_pendingRebuild = true;
            m_rebuildRequested = true;

            // Apply custom title if provided
            ApplyNodeTitle();
        }

#if UNITY_EDITOR
        public override void DrawProperties()
        {
            base.DrawProperties();

            EditorGUI.BeginChangeCheck();
            m_customNodeName = EditorGUILayout.TextField("Node Name", m_customNodeName);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyNodeTitle();
                m_isDirty = true;
            }

            EditorGUILayout.HelpBox(
                "Ports are generated from comments:\n" +
                "  //@out <type> <Name>\n" +
                "  //@in  <type> <Name>\n\n" +
                "Write helper functions + a main(...) entry.\n" +
                "All functions are renamed uniquely to avoid collisions.",
                MessageType.Info
            );

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Code", EditorStyles.boldLabel);
            m_scroll = EditorGUILayout.BeginScrollView(m_scroll, GUILayout.Height(220));
            m_code = EditorGUILayout.TextArea(m_code, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            if (EditorGUI.EndChangeCheck())
            {
                m_pendingRebuild = true;
                m_isDirty = true;
            }

            if (GUILayout.Button("Rebuild Ports Now"))
            {
                m_pendingRebuild = true;
                m_rebuildRequested = true;
                m_isDirty = true;
            }

            DrawAutoPropertyButtons();
        }
#endif

        public override void OnNodeLogicUpdate(DrawInfo drawInfo)
        {
            base.OnNodeLogicUpdate(drawInfo);

            if (m_pendingRebuild)
            {
                if (m_rebuildRequested)
                {
                    RebuildPortsIfNeeded(force: true);
                    m_rebuildRequested = false;
                    m_pendingRebuild = false;
                    m_sizeIsDirty = true;
                }
            }
        }

        public override string GenerateShaderForOutput(int outputId, ref MasterNodeDataCollector dataCollector, bool ignoreLocalvar)
        {
            // Ensure ports exist only when explicitly requested
            if (m_rebuildRequested)
            {
                RebuildPortsIfNeeded(force: true);
                m_rebuildRequested = false;
                m_pendingRebuild = false;
            }

            if (m_outputPorts == null || m_outputPorts.Count == 0)
                return "0";

            // Collect args from input ports in order
            var argList = new List<string>(m_inputPorts.Count);
            for (int i = 0; i < m_inputPorts.Count; i++)
            {
                var p = m_inputPorts[i];
                string val = p.GenerateShaderForOutput(ref dataCollector, p.DataType, ignoreLocalvar, true);
                argList.Add(val);
            }

            // Rewrite all function names (main + helpers)
            string uniquePrefix = $"ase_auto_{UniqueId}_";
            string rewrittenCode = RewriteAllFunctionNames(m_code, uniquePrefix, out string rewrittenMainName);

            // If no main() found, treat as inline expression
            if (string.IsNullOrEmpty(rewrittenMainName))
                return m_code.Trim();

            // Inject helper+main block once with markers
            string markerName = GetDisplayName();
            string block = $"// == Auto Parsed Expression ({markerName}) ==\n" +
                           EnsureNewline(rewrittenCode) +
                           $"// == Auto Parsed Expression ({markerName}) ==\n";
            InjectFunctionBlock(ref dataCollector, uniquePrefix + "block", block);

            // Call rewritten main
            return $"{rewrittenMainName}({string.Join(", ", argList)})";
        }

        // --------------------------
        // Rebuild Ports
        // --------------------------

        private struct PortDecl
        {
            public WirePortDataType type;
            public string name;
        }

        private struct ParsedDecls
        {
            public List<PortDecl> inputs;
            public List<PortDecl> outputs;
        }

        private void RebuildPortsIfNeeded(bool force)
        {
            var decls = ParsePortDecls(m_code, out string sig);

            if (!force && sig == m_cachedSignature)
                return;

            m_cachedSignature = sig;

            // Preserve existing ports when names match (keep connections/defaults)
            var existingInputsByName = new Dictionary<string, InputPort>(StringComparer.Ordinal);
            if (m_inputPorts != null)
            {
                for (int i = 0; i < m_inputPorts.Count; i++)
                {
                    var p = m_inputPorts[i];
                    if (!existingInputsByName.ContainsKey(p.Name))
                        existingInputsByName.Add(p.Name, p);
                }
            }

            var existingOutputsByName = new Dictionary<string, OutputPort>(StringComparer.Ordinal);
            if (m_outputPorts != null)
            {
                for (int i = 0; i < m_outputPorts.Count; i++)
                {
                    var p = m_outputPorts[i];
                    if (!existingOutputsByName.ContainsKey(p.Name))
                        existingOutputsByName.Add(p.Name, p);
                }
            }

            var usedInputs = new HashSet<InputPort>();
            var orderedInputs = new List<InputPort>(decls.inputs.Count);

            for (int i = 0; i < decls.inputs.Count; i++)
            {
                var inp = decls.inputs[i];
                InputPort port;

                if (existingInputsByName.TryGetValue(inp.name, out port) && port != null)
                {
                    port.Name = inp.name;
                    port.InternalDataName = inp.name;
                    port.ChangeType(inp.type, false);
                }
                else
                {
                    port = AddInputPort(inp.type, false, inp.name);
                }

                port.OrderId = i;
                orderedInputs.Add(port);
                usedInputs.Add(port);
            }

            // Remove unused inputs (disconnects removed ports)
            if (m_inputPorts != null)
            {
                for (int i = m_inputPorts.Count - 1; i >= 0; i--)
                {
                    if (!usedInputs.Contains(m_inputPorts[i]))
                        DeleteInputPortByArrayIdx(i);
                }
            }

            // OUTPUT: force single output only (preserve by name)
            string outName = "Out";
            WirePortDataType outType = WirePortDataType.FLOAT;
            if (decls.outputs.Count > 0)
            {
                outName = decls.outputs[0].name;
                outType = decls.outputs[0].type;
            }

            OutputPort outPort;
            if (existingOutputsByName.TryGetValue(outName, out outPort) && outPort != null)
            {
                outPort.Name = outName;
                outPort.ChangeType(outType, false);
            }
            else
            {
                AddOutputPort(outType, outName);
                outPort = m_outputPorts[m_outputPorts.Count - 1];
            }

            // Remove unused outputs (keep only the chosen one)
            if (m_outputPorts != null)
            {
                for (int i = m_outputPorts.Count - 1; i >= 0; i--)
                {
                    if (m_outputPorts[i] != outPort)
                        DeleteOutputPortByArrayIdx(i);
                }
            }

            // Reorder lists and rebuild dictionaries
            m_inputPorts = orderedInputs;
            m_inputPortsDict.Clear();
            for (int i = 0; i < m_inputPorts.Count; i++)
                m_inputPortsDict[m_inputPorts[i].PortId] = m_inputPorts[i];

            m_outputPorts = new List<OutputPort> { outPort };
            m_outputPortsDict.Clear();
            m_outputPortsDict[outPort.PortId] = outPort;

            m_sizeIsDirty = true;
        }

        private static ParsedDecls ParsePortDecls(string code, out string signature)
        {
            var inputs = new List<PortDecl>();
            var outputs = new List<PortDecl>();

            var lines = code.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith(InTag, StringComparison.Ordinal))
                {
                    if (TryParseDecl(line, InTag, out var decl))
                        inputs.Add(decl);
                }
                else if (line.StartsWith(OutTag, StringComparison.Ordinal))
                {
                    if (TryParseDecl(line, OutTag, out var decl))
                        outputs.Add(decl);
                }
            }

            // Signature to detect changes (note: outputs only count first, because we force single output)
            var sb = new StringBuilder();
            sb.Append("OUT:");
            if (outputs.Count > 0)
                sb.Append(outputs[0].type).Append(" ").Append(outputs[0].name).Append(";");

            sb.Append("|IN:");
            for (int i = 0; i < inputs.Count; i++)
                sb.Append(inputs[i].type).Append(" ").Append(inputs[i].name).Append(";");

            signature = sb.ToString();

            return new ParsedDecls { inputs = inputs, outputs = outputs };
        }

        private static bool TryParseDecl(string line, string tag, out PortDecl decl)
        {
            decl = default;

            // format: //@in float3 Name
            string rest = line.Substring(tag.Length).Trim();
            var parts = Regex.Split(rest, @"\s+");
            if (parts.Length < 2) return false;

            string typeStr = parts[0].Trim();
            string nameStr = parts[1].Trim();

            if (!TryMapType(typeStr, out var wpdt))
                return false;

            if (!Regex.IsMatch(nameStr, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                return false;

            decl = new PortDecl { type = wpdt, name = nameStr };
            return true;
        }

        private static bool TryMapType(string typeStr, out WirePortDataType type)
        {
            switch (typeStr)
            {
                case "float": type = WirePortDataType.FLOAT; return true;
                case "float2": type = WirePortDataType.FLOAT2; return true;
                case "float3": type = WirePortDataType.FLOAT3; return true;
                case "float4": type = WirePortDataType.FLOAT4; return true;

                case "half": type = WirePortDataType.FLOAT; return true;
                case "half2": type = WirePortDataType.FLOAT2; return true;
                case "half3": type = WirePortDataType.FLOAT3; return true;
                case "half4": type = WirePortDataType.FLOAT4; return true;

                case "int": type = WirePortDataType.INT; return true;
                case "bool": type = WirePortDataType.INT; return true;

                case "sampler2D": type = WirePortDataType.SAMPLER2D; return true;
                case "sampler3D": type = WirePortDataType.SAMPLER3D; return true;
                case "samplerCUBE": type = WirePortDataType.SAMPLERCUBE; return true;

                case "color": type = WirePortDataType.COLOR; return true;

                default:
                    type = WirePortDataType.FLOAT;
                    return false;
            }
        }

        /// <summary>
        /// Strong deletion using ParentNode public API.
        /// Removes connections and ports reliably without reflection.
        /// </summary>
        private void SafeDeleteAllPortsHard()
        {
            // Remove all connections and ports via ParentNode API
            DeleteAllInputConnections(true);
            DeleteAllOutputConnections(true);

            // Safety: if anything remains, delete by array index
            while (m_outputPorts != null && m_outputPorts.Count > 0)
                DeleteOutputPortByArrayIdx(0);

            while (m_inputPorts != null && m_inputPorts.Count > 0)
                DeleteInputPortByArrayIdx(0);
        }

        // --------------------------
        // Function renaming + injection
        // --------------------------

        private static string RewriteAllFunctionNames(string code, string prefix, out string rewrittenMainName)
        {
            rewrittenMainName = null;

                // Match actual function definitions (allow multiline params, requires opening brace)
                var funcDef = new Regex(
                    @"^[ \t]*(?:inline[ \t]+)?(?:static[ \t]+)?([A-Za-z_][A-Za-z0-9_<>\[\]]*)[ \t]+([A-Za-z_][A-Za-z0-9_]*)[ \t]*\([^;]*?\)[ \t\r\n]*\{",
                    RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.Singleline
                );

            var matches = funcDef.Matches(code);
            if (matches.Count == 0) return code;

            var map = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (Match m in matches)
            {
                string fname = m.Groups[2].Value;
                if (string.IsNullOrEmpty(fname)) continue;
                if (fname == "if" || fname == "for" || fname == "while" || fname == "switch") continue;

                if (!map.ContainsKey(fname))
                    map.Add(fname, prefix + fname);
            }

            if (map.TryGetValue("main", out var mm))
                rewrittenMainName = mm;

            string rewritten = code;
            foreach (var kv in map)
            {
                rewritten = Regex.Replace(
                    rewritten,
                    $@"\b{Regex.Escape(kv.Key)}\b(?=\s*\()",
                    kv.Value
                );
            }

            return rewritten;
        }

        private static void InjectFunctionBlock(ref MasterNodeDataCollector dc, string functionId, string fullFunctionBlock)
        {
            Type t = dc.GetType();

            // Prefer AddFunction(string,string)
            var mi = t.GetMethod(
                "AddFunction",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(string) },
                null
            );

            if (mi != null)
            {
                mi.Invoke(dc, new object[] { functionId, fullFunctionBlock });
                return;
            }

            // Try AddFunction(string)
            mi = t.GetMethod(
                "AddFunction",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null
            );

            if (mi != null)
            {
                mi.Invoke(dc, new object[] { fullFunctionBlock });
                return;
            }

            // Last resort: any *(string,string) with "Function" in name
            foreach (var cand in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!cand.Name.ToLowerInvariant().Contains("function")) continue;
                var ps = cand.GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string))
                {
                    cand.Invoke(dc, new object[] { functionId, fullFunctionBlock });
                    return;
                }
            }
        }

        private static string EnsureNewline(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\n";
            return s.EndsWith("\n", StringComparison.Ordinal) ? s : (s + "\n");
        }

        private string GetDisplayName()
        {
            return string.IsNullOrWhiteSpace(m_customNodeName) ? "Auto Parsed Expression" : m_customNodeName.Trim();
        }

        private void ApplyNodeTitle()
        {
            SetTitleText(GetDisplayName());
        }

#if UNITY_EDITOR
        private void DrawAutoPropertyButtons()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Auto Properties", EditorStyles.boldLabel);

            if (m_inputPorts == null || m_inputPorts.Count == 0)
            {
                EditorGUILayout.HelpBox("No input ports available.", MessageType.Info);
                return;
            }

            for (int i = 0; i < m_inputPorts.Count; i++)
            {
                var port = m_inputPorts[i];
                var nodeType = GetPropertyNodeTypeForPort(port.DataType);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{port.Name} ({port.DataType})", GUILayout.Width(240));
                    GUI.enabled = nodeType != null;
                    string buttonLabel = port.IsConnected ? "Replace Property" : "Create Property";
                    if (GUILayout.Button(buttonLabel))
                    {
                        CreatePropertyAndConnect(port, nodeType, i);
                    }
                    GUI.enabled = true;
                }
            }
        }
#endif

        private static Type GetPropertyNodeTypeForPort(WirePortDataType dataType)
        {
            switch (dataType)
            {
                case WirePortDataType.FLOAT: return typeof(RangedFloatNode);
                case WirePortDataType.FLOAT2: return typeof(Vector2Node);
                case WirePortDataType.FLOAT3: return typeof(Vector3Node);
                case WirePortDataType.FLOAT4: return typeof(Vector4Node);
                case WirePortDataType.INT: return typeof(IntNode);
                case WirePortDataType.COLOR: return typeof(ColorNode);
                case WirePortDataType.SAMPLER2D:
                case WirePortDataType.SAMPLER3D:
                case WirePortDataType.SAMPLERCUBE:
                    return typeof(TexturePropertyNode);
                default:
                    return null;
            }
        }

#if UNITY_EDITOR
        private void CreatePropertyAndConnect(InputPort port, Type nodeType, int portIndex)
        {
            if (nodeType == null)
                return;

            Vector2 pos = new Vector2(m_position.x - 260, m_position.y + 40 + portIndex * 60);
            ParentNode newNode = UIUtils.CreateNode(nodeType, true, pos);
            if (newNode == null || newNode.OutputPorts == null || newNode.OutputPorts.Count == 0)
                return;

            if (port.IsConnected)
            {
                UIUtils.DeleteConnection(true, UniqueId, port.PortId, true, true);
            }

            if (newNode is PropertyNode propNode)
            {
                propNode.CurrentParameterType = PropertyType.Property;
                propNode.RegisterPropertyName(true, port.Name, true, false);
            }

            UIUtils.ConnectInputToOutput(UniqueId, port.PortId, newNode.UniqueId, newNode.OutputPorts[0].PortId);
        }
#endif

        // --------------------------
        // Serialization
        // --------------------------

        public override void WriteToString(ref string nodeInfo, ref string connectionsInfo)
        {
            base.WriteToString(ref nodeInfo, ref connectionsInfo);
            IOUtils.AddFieldValueToString(ref nodeInfo, ToB64(m_code));
            IOUtils.AddFieldValueToString(ref nodeInfo, ToB64(m_cachedSignature));
            IOUtils.AddFieldValueToString(ref nodeInfo, ToB64(m_customNodeName));
        }

        public override void ReadFromString(ref string[] nodeParams)
        {
            base.ReadFromString(ref nodeParams);

            int remaining = nodeParams.Length - (int)m_currentReadParamIdx;
            if (remaining > 0)
            {
                string first = GetCurrentParam(ref nodeParams);

                // Legacy format: "AutoParsedExpressionNode|<code>|<sig>|<pending>|<name>|"
                if (!string.IsNullOrEmpty(first) && first.StartsWith("AutoParsedExpressionNode|", StringComparison.Ordinal))
                {
                    string[] parts = first.Split('|');
                    if (parts.Length > 1) m_code = FromB64(parts[1]);
                    if (parts.Length > 2) m_cachedSignature = FromB64(parts[2]);
                    if (parts.Length > 4) m_customNodeName = FromB64(parts[4]);
                }
                else
                {
                    m_code = FromB64(first);
                    if (remaining > 1) m_cachedSignature = FromB64(GetCurrentParam(ref nodeParams));
                    if (remaining > 2) m_customNodeName = FromB64(GetCurrentParam(ref nodeParams));
                }
            }

            // Do not force rebuild on load (preserve saved ports/connections)
            m_pendingRebuild = false;
            m_rebuildRequested = false;
            ApplyNodeTitle();

            // Ensure ports exist before ASE reads port data/connections
            RebuildPortsIfNeeded(force: true);
        }

        private static string ToB64(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        }

        private static string FromB64(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return "";
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
            catch { return ""; }
        }

        // --------------------------
        // Default
        // --------------------------

        private static string DefaultCode()
        {
            return
@"//@out float Out
//@in float X
//@in float Y

float helper(float a, float b)
{
    return a*b;
}

float main(float X, float Y)
{
    return helper(X, Y);
}";
        }
    }
}
