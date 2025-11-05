using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Grasshopper.Kernel;
using System.Text.Json;
// removed CodeDOM-based compilation
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using System.Collections.Generic;
using Rhino;
using LLM.Templates;
using Grasshopper.GUI.Canvas;
using System.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace LLM.OllamaComps
{
    public class OllamaComponentMakerComponent : GH_Component_HTTPAsync
    {
        // Tracks compilation iterations
        private int _compileAttempts = 0;
        // Agent mode enabled by default
        private bool _agentMode = true;
        // Cache for loaded PDF context
        private string _contextCache = null;

        // Store current generation parameters for agent retries
        private string _currentDescription = string.Empty;
        private string _currentComponentName = string.Empty;
        private string _currentCategory = string.Empty;
        private string _currentSubcategory = string.Empty;

        // Error tracker for compile errors
        private CompilationErrorTracker _errorTracker = new CompilationErrorTracker();
        // Internal class to track compilation errors for adaptive retries
        private class CompilationErrorTracker
        {
            public List<string> PreviousErrors { get; } = new List<string>();
            public HashSet<string> CommonErrors { get; } = new HashSet<string>();

            public void AddError(string error)
            {
                PreviousErrors.Add(error);
                if (error.Contains("namespace") && error.Contains("not exist"))
                    CommonErrors.Add("namespace_not_found");
                if (error.Contains("type or namespace") && error.Contains("could not be found"))
                    CommonErrors.Add("missing_reference");
            }

            public string GenerateErrorPrompt()
            {
                var sb = new StringBuilder();
                sb.AppendLine("Previous compilation errors:");
                foreach (var err in PreviousErrors.Skip(Math.Max(0, PreviousErrors.Count - 3)))
                    sb.AppendLine($"- {err}");
                if (CommonErrors.Contains("namespace_not_found"))
                    sb.AppendLine("Ensure all namespaces are properly defined and accessible.");
                if (CommonErrors.Contains("missing_reference"))
                    sb.AppendLine("Make sure to use only types from referenced assemblies: System, System.Core, Grasshopper, Rhino, GH_IO.");
                return sb.ToString();
            }
        }
        // Reference guide for C# Grasshopper scripting
        private const string _guideReference =
            "Reference the C# Scripting for Grasshopper guide available at docs/C#ScriptingForGrasshopper.pdf for namespaces, class structure, and API usage. ";
        // Store last request details for retry
        private string _lastUrl = string.Empty;
        private string _lastBody = string.Empty;
        // Store parameters for retry prompt
        private string _model = string.Empty;
        private double _temperature = 0.7;
        private int _maxTokens = 2048;
        private int _timeoutMs = 60000;
        private string _systemPrompt = string.Empty;
        private string _userPrompt = string.Empty;
        // Stored context file reference
        private string _contextReference = string.Empty;
        public OllamaComponentMakerComponent()
          : base("Component that Makes (Local)", "Local",
              "Generates and compiles a Grasshopper component via Ollama",
              "AI Tools", "LLM") { }
        /// <summary>
        /// Automatically add and wire an OllamaModelParam dropdown on placement.
        /// </summary>
        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            // If already wired, skip
            if (Params.Input.Count > 1 && Params.Input[1].SourceCount > 0) return;
            // Create and wire model dropdown list
            var list = new OllamaModelParam();
            document.AddObject(list, false);
            document.NewSolution(false);
            // Connect dropdown to Model input
            if (Params.Input.Count > 1)
                Params.Input[1].AddSource(list);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Generate", "G", "Toggle to generate and compile component", GH_ParamAccess.item, false);
            pManager.AddParameter(new OllamaModelParam(), "Model", "M", "Ollama model to use", GH_ParamAccess.item);
            pManager.AddTextParameter("Description", "D", "Natural-language description of the component", GH_ParamAccess.item);
            pManager.AddTextParameter("Component Name", "N", "Optional component class/name", GH_ParamAccess.item, string.Empty);
            pManager[3].Optional = true;
            pManager.AddTextParameter("Category", "C", "Grasshopper ribbon category", GH_ParamAccess.item, "AI Tools");
            pManager.AddTextParameter("Subcategory", "S", "Grasshopper ribbon subcategory", GH_ParamAccess.item, "LLM");
            pManager.AddNumberParameter("Temperature", "T", "Generation temperature (0-1)", GH_ParamAccess.item, 0.7);
            pManager.AddIntegerParameter("Max Tokens", "MT", "Maximum number of tokens to generate", GH_ParamAccess.item, 2048);
            pManager.AddTextParameter("URL", "U", "Ollama API endpoint", GH_ParamAccess.item, "http://localhost:11434/api/generate");
            pManager.AddIntegerParameter("Timeout", "TO", "Request timeout (ms)", GH_ParamAccess.item, 60000);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Generated Code", "C", "Generated C# code", GH_ParamAccess.item);
            pManager.AddTextParameter("Component Class", "CC", "Generated component class name", GH_ParamAccess.item);
            pManager.AddTextParameter("Source Path", "CS", "File path of saved .cs source", GH_ParamAccess.item);
            pManager.AddTextParameter("Assembly Path", "DLL", "File path of compiled .gha assembly", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Success", "S", "Compilation success", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Delegates to agent-based logic if enabled
            if (_agentMode)
            {
                SolveInstanceAgent(DA);
                return;
            }
            if (_shouldExpire)
            {
                switch (_currentState)
                {
                    case RequestState.Off:
                        // No active request
                        this.Message = "Inactive";
                        _currentState = RequestState.Idle;
                        break;
                    case RequestState.Error:
                        // Treat HTTP or network errors as a compile retry
                        this.Message = $"Retrying after error...";
                        _currentState = RequestState.Idle;
                        ProcessAndCompileCode(DA);
                        break;
                    case RequestState.Done:
                        // Initial generation complete; proceed to compile
                        this.Message = "Processing code...";
                        _currentState = RequestState.Idle;
                        ProcessAndCompileCode(DA);
                        break;
                }
                _shouldExpire = false;
                return;
            }

            bool active = false;
            string model = string.Empty;
            string description = string.Empty;
            string componentName = string.Empty;
            double temperature = 0.7;
            string url = string.Empty;
            int timeout = 60000;

            if (!DA.GetData("Generate", ref active)) return;
            if (!active)
            {
                _currentState = RequestState.Off;
                _shouldExpire = true;
                _response = string.Empty;
                ExpireSolution(true);
                return;
            }
            if (!DA.GetData("Model", ref model)) return;
            if (!DA.GetData("Description", ref description)) return;
            DA.GetData("Component Name", ref componentName);
            DA.GetData("Temperature", ref temperature);
            if (!DA.GetData("URL", ref url)) return;
            DA.GetData("Timeout", ref timeout);
            int maxTokens = 2048;
            DA.GetData("Max Tokens", ref maxTokens);

            if (string.IsNullOrEmpty(url))
            {
                _response = "Empty URL";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }
            if (string.IsNullOrEmpty(description))
            {
                _response = "Empty component description";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }

            // Build context reference for local files
            string contextReference = string.Empty;
            try
            {
                var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                var ctxDir = Path.Combine(pluginDir, "context_for_llm");
                if (Directory.Exists(ctxDir))
                {
                    var files = Directory.GetFiles(ctxDir).Select(Path.GetFileName);
                    contextReference = "Available context files: " + string.Join(", ", files) + ". Please consider their content. ";
                }
            }
            catch { }
            // Combine context, guide reference, and core instructions into one prompt
            // Store for retry usage
            _contextReference = contextReference;
            string systemPrompt = contextReference + _guideReference +
                                  "You are an expert C# programmer specializing in Grasshopper plugin development. " +
                                  "Create a complete, well-structured Grasshopper component based on the description provided. " +
                                  "Respond with only the full C# code for the component (no explanations or markdown).";
            // Build user prompt
            string userPrompt = "Component description: " + description;
            if (!string.IsNullOrEmpty(componentName))
                userPrompt += $"\nComponent name: {componentName}";
            // Merge into one prompt for Ollama
            string fullPrompt = systemPrompt + "\n" + userPrompt;
            // Prepare request body for Ollama /api/generate
            // Build JSON payload using serializer to handle escaping
            var requestPayload = new
            {
                model = model,
                prompt = fullPrompt,
                max_tokens = maxTokens,
                temperature = temperature,
                stream = false
            };
            string body = JsonSerializer.Serialize(requestPayload);
            _currentState = RequestState.Requesting;
            this.Message = "Generating Component...";
            // Store request details for potential retries
            _lastUrl = url;
            _lastBody = body;
            _compileAttempts = 0;
            _model = model;
            _temperature = temperature;
            _maxTokens = maxTokens;
            _timeoutMs = timeout;
            _systemPrompt = systemPrompt;
            _userPrompt = userPrompt;
            // Send initial generation request
            POSTAsync(url, body, "application/json", string.Empty, timeout);
        }

        private void ProcessAndCompileCode(IGH_DataAccess DA)
        {
            try
            {
                // Try to parse JSON response, otherwise treat raw response as code
                string generatedText;
                try
                {
                    using var jsonDocument = JsonDocument.Parse(_response);
                    if (jsonDocument.RootElement.TryGetProperty("response", out var respProp))
                        generatedText = respProp.GetString() ?? string.Empty;
                    else
                        generatedText = jsonDocument.RootElement.GetRawText();
                }
                catch (JsonException)
                {
                    generatedText = _response;
                }
                string cleanCode = generatedText.Trim();
                if (cleanCode.StartsWith("```"))
                    cleanCode = cleanCode[(cleanCode.IndexOf('\n') + 1)..];
                if (cleanCode.EndsWith("```"))
                    cleanCode = cleanCode[..cleanCode.LastIndexOf("```")];
                cleanCode = cleanCode.Trim();
                // Remove any leading/trailing HTML/XML tags or metadata lines (e.g., <think> wrappers)
                var lines = cleanCode.Split(new[] {'\r','\n'}, StringSplitOptions.RemoveEmptyEntries).ToList();
                // Strip leading tags
                while (lines.Count > 0 && lines[0].TrimStart().StartsWith("<") && lines[0].Contains(">"))
                    lines.RemoveAt(0);
                // Strip trailing tags
                while (lines.Count > 0 && lines[^1].TrimEnd().StartsWith("<") && lines[^1].Contains(">"))
                    lines.RemoveAt(lines.Count - 1);
                cleanCode = string.Join("\n", lines).Trim();
                string className = ExtractClassName(cleanCode);
                string csPath = SaveComponentCode(cleanCode, className);
                string assemblyPath = Path.ChangeExtension(csPath, ".gha");
                // Attempt compilation
                bool success = true;
                Exception compileEx = null;
                try
                {
                    CompileCode(cleanCode, assemblyPath);
                }
                catch (Exception ex)
                {
                    compileEx = ex;
                    success = false;
                }
                // Handle compile failures with retry via LLM
                if (!success)
                {
                    _compileAttempts++;
                    // Build retry prompt including last generated code and errors
                    string retryPrompt = BuildRetryPrompt(cleanCode, compileEx?.Message ?? "Unknown compilation error");
                    this.Message = $"Retrying, attempt #{_compileAttempts}...";
                    var retryPayload = new
                    {
                        model = _model,
                        prompt = retryPrompt,
                        max_tokens = _maxTokens,
                        temperature = _temperature,
                        stream = false
                    };
                    string retryBody = JsonSerializer.Serialize(retryPayload);
                    _currentState = RequestState.Requesting;
                    POSTAsync(_lastUrl, retryBody, "application/json", string.Empty, _timeoutMs);
                    _lastBody = retryBody;
                    _shouldExpire = false;
                    return;
                }
                if (!success)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Compilation failed after retries: " + (compileEx?.Message ?? ""));
                    assemblyPath = string.Empty;
                }
                // Output results
                DA.SetData(0, cleanCode);
                DA.SetData(1, className);
                DA.SetData(2, csPath);
                DA.SetData(3, assemblyPath);
                DA.SetData(4, success);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error processing response: " + ex.Message);
                DA.SetData(0, _response);
                DA.SetData(4, false);
            }
        }
        
        /// <summary>
        /// Agent processing and compilation with retry logic.
        /// </summary>
        private void AgentProcessAndCompileCode(IGH_DataAccess DA)
        {
            try
            {
                using var jsonDocument = JsonDocument.Parse(_response);
                var generatedText = jsonDocument.RootElement.GetProperty("response").GetString() ?? string.Empty;
                string cleanCode = CleanGeneratedCode(generatedText);

                string className = ExtractClassName(cleanCode);
                string csPath = SaveComponentCode(cleanCode, className);
                string assemblyPath = Path.ChangeExtension(csPath, ".gha");

                bool success = true;
                Exception compileEx = null;

                try
                {
                    CompileCode(cleanCode, assemblyPath);
                    this.Message = $"Compilation succeeded after {_compileAttempts + 1} attempts";
                }
                catch (Exception ex)
                {
                    compileEx = ex;
                    success = false;
                    _errorTracker.AddError(ex.Message);
                }

                // If compilation failed, retry indefinitely until success or user stops
                if (!success)
                {
                    _compileAttempts++;
                    this.Message = $"Retrying, attempt #{_compileAttempts}...";

                    string retryPrompt = BuildRetryPrompt(cleanCode, compileEx?.Message ?? "Unknown error");
                    var retryPayload = new
                    {
                        model = _model,
                        prompt = retryPrompt,
                        max_tokens = _maxTokens,
                        temperature = _temperature,
                        stream = false
                    };
                    string retryBody = JsonSerializer.Serialize(retryPayload);
                    _currentState = RequestState.Requesting;
                    POSTAsync(_lastUrl, retryBody, "application/json", string.Empty, _timeoutMs);
                    return;
                }

                DA.SetData(0, cleanCode);
                DA.SetData(1, className);
                DA.SetData(2, csPath);
                DA.SetData(3, success ? assemblyPath : string.Empty);
                DA.SetData(4, success);

                if (success)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Successfully generated and compiled {className} after {_compileAttempts + 1} attempts.");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "Failed to compile component after maximum retry attempts.");
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, compileEx?.Message ?? "Unknown error");
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error processing response: " + ex.Message);
                DA.SetData(0, _response);
                DA.SetData(4, false);
            }
        }

        /// <summary>
        /// Cleans generated code by removing markdown blocks and metadata.
        /// </summary>
        private string CleanGeneratedCode(string generatedText)
        {
            string cleanCode = generatedText.Trim();

            if (cleanCode.StartsWith("```"))
            {
                int firstNewLine = cleanCode.IndexOf('\n');
                if (firstNewLine > 0)
                {
                    cleanCode = cleanCode.Substring(firstNewLine + 1);
                }
            }

            if (cleanCode.EndsWith("```"))
            {
                int lastBackticks = cleanCode.LastIndexOf("```");
                if (lastBackticks > 0)
                {
                    cleanCode = cleanCode.Substring(0, lastBackticks);
                }
            }

            var lines = cleanCode.Split(new[] {'\r','\n'}, StringSplitOptions.RemoveEmptyEntries).ToList();

            while (lines.Count > 0 && (lines[0].TrimStart().StartsWith("<") && lines[0].Contains(">")))
                lines.RemoveAt(0);

            while (lines.Count > 0 && (lines[^1].TrimEnd().EndsWith(">") && lines[^1].Contains("<")))
                lines.RemoveAt(lines.Count - 1);

            return string.Join("\n", lines).Trim();
        }

        /// <summary>
        /// Builds the initial system prompt with PDF context and guidelines.
        /// </summary>
        private string BuildSystemPrompt(string description, string componentName, string category, string subcategory)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are an expert C# programmer specializing in Grasshopper plugin development.");
            sb.AppendLine("Create a complete, well-structured Grasshopper component based on the description provided.");

            if (!string.IsNullOrEmpty(_contextCache))
            {
                sb.AppendLine("\n--- REFERENCE CONTEXT ---");
                sb.AppendLine(_contextCache);
                sb.AppendLine("--- END REFERENCE CONTEXT ---\n");
            }

            sb.AppendLine("CODING REQUIREMENTS:");
            sb.AppendLine("1. Use standard Grasshopper component patterns with RegisterInputParams, RegisterOutputParams, and SolveInstance methods");
            sb.AppendLine("2. Include all necessary namespaces, especially: Rhino.Geometry, Grasshopper.Kernel");
            sb.AppendLine($"3. Use \"{category}\" as Category and \"{subcategory}\" as Subcategory");
            sb.AppendLine("4. Generate a unique GUID for the component using: new Guid(\"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx\")");
            sb.AppendLine("5. Error-handle appropriately. Use try/catch blocks for risky operations");
            sb.AppendLine("6. ONLY output complete, compilable C# code - no explanations or markdown");
            sb.AppendLine("7. NEVER use \"throw new NotImplementedException()\", always implement all methods");
            sb.AppendLine("8. Only reference Grasshopper, Rhino, System, System.Core, and GH_IO assemblies");
            sb.AppendLine("9. Use simple namespaces without deeply nested structures");

            sb.AppendLine($"\nComponent Description: {description}");
            if (!string.IsNullOrEmpty(componentName))
                sb.AppendLine($"Component Class Name: {componentName}");

            return sb.ToString();
        }

        /// <summary>
        /// Builds a retry prompt including errors and previous code.
        /// </summary>
        private string BuildRetryPrompt(string previousCode, string error)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are an expert C# programmer specializing in Grasshopper plugin development.");
            sb.AppendLine("Fix the compilation errors in the previously generated component.");

            sb.AppendLine("\n--- COMPILATION ERRORS ---");
            sb.AppendLine(error);
            sb.AppendLine(_errorTracker.GenerateErrorPrompt());
            sb.AppendLine("--- END COMPILATION ERRORS ---\n");

            sb.AppendLine("--- PREVIOUS CODE ---");
            sb.AppendLine(previousCode);
            sb.AppendLine("--- END PREVIOUS CODE ---\n");

            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("1. Carefully analyze the compilation errors");
            sb.AppendLine("2. Fix ALL errors in the code");
            sb.AppendLine("3. Return ONLY the complete, fixed code without any explanations or comments about the changes");
            sb.AppendLine("4. Ensure all namespaces are correct and accessible");
            sb.AppendLine("5. Use only types from Rhino, Grasshopper, System, System.Core, and GH_IO assemblies");
            sb.AppendLine("6. Do not use any third-party libraries");

            return sb.ToString();
        }

        private string ExtractClassName(string code)
        {
            var match = Regex.Match(code, @"public\s+class\s+(\w+)\s*:");
            return match.Success ? match.Groups[1].Value : "UnknownComponent";
        }

        private string SaveComponentCode(string code, string className)
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                string dir = Path.Combine(pluginDir, "GeneratedComponents");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, className + ".cs");
                File.WriteAllText(path, code, Encoding.UTF8);
                return path;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to save component: " + ex.Message);
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Compiles C# source code into a Grasshopper plugin assembly (.gha).
        /// </summary>
        private void CompileCode(string code, string outputPath)
        {
            // Use Roslyn for cross-platform compilation
            // Parse the source
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            // Gather references from loaded assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location) && File.Exists(a.Location))
                .Select(a => a.Location)
                .Distinct();
            var refs = assemblies.Select(path => MetadataReference.CreateFromFile(path)).ToList();
            // Explicitly ensure Grasshopper, GH_IO, and plugin assembly are referenced
            try
            {
                var ghLoc = typeof(Grasshopper.Kernel.GH_Component).Assembly.Location;
                if (File.Exists(ghLoc)) refs.Add(MetadataReference.CreateFromFile(ghLoc));
                var ghIoAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name.Equals("GH_IO", StringComparison.OrdinalIgnoreCase));
                if (ghIoAsm != null && File.Exists(ghIoAsm.Location))
                    refs.Add(MetadataReference.CreateFromFile(ghIoAsm.Location));
                var pluginAsm = Assembly.GetExecutingAssembly().Location;
                if (File.Exists(pluginAsm)) refs.Add(MetadataReference.CreateFromFile(pluginAsm));
            }
            catch { /* best-effort references */ }
            // Create compilation
            var compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(outputPath),
                new[] { syntaxTree },
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var result = compilation.Emit(outputPath);
            if (!result.Success)
            {
                var failures = result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
                var sb = new StringBuilder("Compilation failed:");
                foreach (var diag in failures)
                    sb.AppendLine(diag.ToString());
                throw new Exception(sb.ToString());
            }
        }
        
        /// <summary>
        /// Agent-based SolveInstance logic extracted to separate method.
        /// </summary>
        private void SolveInstanceAgent(IGH_DataAccess DA)
        {
            // Agent-based generation and compilation logic
            if (_shouldExpire)
            {
                switch (_currentState)
                {
                    case RequestState.Off:
                        this.Message = "Inactive";
                        _currentState = RequestState.Idle;
                        break;
                    case RequestState.Error:
                        this.Message = "ERROR";
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _response);
                        _currentState = RequestState.Idle;
                        break;
                    case RequestState.Done:
                        this.Message = "Complete!";
                        _currentState = RequestState.Idle;
                        AgentProcessAndCompileCode(DA);
                        break;
                }
                _shouldExpire = false;
                return;
            }

            bool active = false;
            string model = string.Empty;
            string description = string.Empty;
            string componentName = string.Empty;
            string category = "AI Tools";
            string subcategory = "LLM";
            double temperature = 0.7;
            string url = string.Empty;
            int timeout = 60000;
            int maxTokens = 2048;

            if (!DA.GetData("Generate", ref active)) return;
            if (!active)
            {
                _currentState = RequestState.Off;
                _shouldExpire = true;
                _response = string.Empty;
                ExpireSolution(true);
                return;
            }

            if (!DA.GetData("Model", ref model)) return;
            if (!DA.GetData("Description", ref description)) return;
            DA.GetData("Component Name", ref componentName);
            DA.GetData("Category", ref category);
            DA.GetData("Subcategory", ref subcategory);
            DA.GetData("Temperature", ref temperature);
            if (!DA.GetData("URL", ref url)) return;
            DA.GetData("Timeout", ref timeout);
            DA.GetData("Max Tokens", ref maxTokens);

            if (string.IsNullOrEmpty(url))
            {
                _response = "Empty URL";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }
            if (string.IsNullOrEmpty(description))
            {
                _response = "Empty component description";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }

            // Initialize agent parameters
            _currentDescription = description;
            _currentComponentName = componentName;
            _currentCategory = category;
            _currentSubcategory = subcategory;
            _compileAttempts = 0;
            _errorTracker = new CompilationErrorTracker();

            // Load PDF context once
            if (_contextCache == null)
            {
                this.Message = "Loading context...";
                _contextCache = PdfContextManager.LoadPdfContext();
                if (string.IsNullOrEmpty(_contextCache))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No context files found in 'context_for_llm' folder. Agent will function with limited context.");
                else
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Loaded context data from PDFs ({_contextCache.Length / 1024} KB).");
            }

            // Build system prompt with context
            string systemPrompt = BuildSystemPrompt(description, componentName, category, subcategory);

            // Prepare request payload
            var requestPayload = new
            {
                model,
                prompt = systemPrompt,
                max_tokens = maxTokens,
                temperature,
                stream = false
            };

            string body = JsonSerializer.Serialize(requestPayload);
            _currentState = RequestState.Requesting;
            this.Message = "Generating Component...";

            // Store details for potential retries
            _lastUrl = url;
            _lastBody = body;
            _model = model;
            _temperature = temperature;
            _maxTokens = maxTokens;
            _timeoutMs = timeout;
            _systemPrompt = systemPrompt;

            // Initial request
            POSTAsync(url, body, "application/json", string.Empty, timeout);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("BF4C9D32-A84B-4BA2-BD5F-E75DEF32CC71");
    }
}