using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Grasshopper.Kernel;
using Grasshopper.GUI.Canvas;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Rhino;
using LLM.Templates;
using System.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace LLM.OllamaComps
{
    /// <summary>
    /// OpenAI-powered Grasshopper component generator with PDF context integration and enhanced error handling
    /// </summary>
    public class OpenAIComponentGeneratorComponent : GH_Component_HTTPAsync
    {
        // Compilation tracking
        private int _compileAttempts = 0;
        private const int _maxCompileAttempts = 5;
        
        // Agent mode settings
        private bool _agentMode = true;
        private bool _isAgentRunning = false;
        
        // Context management
        private string _contextCache = null;
        // Maximum number of characters from PDF context to include
        private const int _maxContextLength = 5000;
        
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
        
        // Store last request details for retry
        private string _lastUrl = string.Empty;
        private string _lastBody = string.Empty;
        
        // Store parameters for retry prompt
        private string _openAIApiKey = string.Empty;
        private string _openAIModel = "gpt-4o";
        private double _temperature = 0.7;
        private int _maxTokens = 2048;
        private int _timeoutMs = 60000;
        private string _systemPrompt = string.Empty;
        private string _userPrompt = string.Empty;
        
        public OpenAIComponentGeneratorComponent()
          : base("Component that Makes (API)", "API",
              "Generates and compiles a Grasshopper component via OpenAI",
              "crft", "LLM") { }
        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            if (Params.Input.Count > 2 && Params.Input[2].SourceCount > 0) return;
            var list = new OpenAIModelParam();
            document.AddObject(list, false);
            document.NewSolution(false);
            if (Params.Input.Count > 2)
                Params.Input[2].AddSource(list);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Generate", "G", "Toggle to generate and compile component", GH_ParamAccess.item, false);
            pManager.AddTextParameter("API Key", "K", "OpenAI API Key", GH_ParamAccess.item);
            pManager.AddParameter(new OpenAIModelParam(), "Model", "M", "OpenAI model to use", GH_ParamAccess.item);
            pManager.AddTextParameter("Description", "D", "Natural-language description of the component", GH_ParamAccess.item);
            pManager.AddTextParameter("Component Name", "N", "Optional component class/name", GH_ParamAccess.item, string.Empty);
            pManager[4].Optional = true;
            pManager.AddTextParameter("Category", "C", "Grasshopper ribbon category", GH_ParamAccess.item, "crft");
            pManager.AddTextParameter("Subcategory", "S", "Grasshopper ribbon subcategory", GH_ParamAccess.item, "LLM");
            pManager.AddNumberParameter("Temperature", "T", "Generation temperature (0-1)", GH_ParamAccess.item, 0.7);
            pManager.AddIntegerParameter("Max Tokens", "MT", "Maximum number of tokens to generate", GH_ParamAccess.item, 2048);
            // Folder path containing PDF context files (optional)
            pManager.AddTextParameter("Context Folder", "CF", "Folder path containing PDF context files", GH_ParamAccess.item, string.Empty);
            pManager[9].Optional = true;
            pManager.AddBooleanParameter("Use Context", "UC", "Use PDF context from coding books", GH_ParamAccess.item, true);
            pManager.AddIntegerParameter("Timeout", "TO", "Request timeout (ms)", GH_ParamAccess.item, 60000);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Generated Code", "C", "Generated C# code", GH_ParamAccess.item);
            pManager.AddTextParameter("Component Class", "CC", "Generated component class name", GH_ParamAccess.item);
            pManager.AddTextParameter("Source Path", "CS", "File path of saved .cs source", GH_ParamAccess.item);
            pManager.AddTextParameter("Assembly Path", "DLL", "File path of compiled .gha assembly", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Success", "S", "Compilation success", GH_ParamAccess.item);
            pManager.AddTextParameter("Context Info", "CI", "Information about loaded context", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
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
                        
                        // In agent mode, process the response but don't immediately set outputs
                        if (_agentMode && _isAgentRunning)
                        {
                            AgentProcessAndCompileCode(DA);
                        }
                        else
                        {
                            // Regular processing for non-agent mode
                            ProcessAndCompileCode(DA);
                        }
                        break;
                }
                _shouldExpire = false;
                return;
            }

            bool active = false;
            string apiKey = string.Empty;
            string model = "gpt-4o";
            string description = string.Empty;
            string componentName = string.Empty;
            string category = "crft";
            string subcategory = "LLM";
            double temperature = 0.7;
            int maxTokens = 2048;
            // User-specified folder for PDF context
            string contextFolder = string.Empty;
            int timeout = 60000;
            bool useContext = true;

            if (!DA.GetData("Generate", ref active)) return;
            if (!active)
            {
                _currentState = RequestState.Off;
                _shouldExpire = true;
                _response = string.Empty;
                _isAgentRunning = false;
                ExpireSolution(true);
                return;
            }
            
            if (!DA.GetData("API Key", ref apiKey)) 
            {
                _response = "API Key is required";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }
            
            DA.GetData("Model", ref model);
            if (!DA.GetData("Description", ref description)) return;
            DA.GetData("Component Name", ref componentName);
            DA.GetData("Category", ref category);
            DA.GetData("Subcategory", ref subcategory);
            DA.GetData("Temperature", ref temperature);
            DA.GetData("Max Tokens", ref maxTokens);
            DA.GetData("Context Folder", ref contextFolder);
            DA.GetData("Use Context", ref useContext);
            DA.GetData("Timeout", ref timeout);

            if (string.IsNullOrEmpty(apiKey))
            {
                _response = "Empty API Key";
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

            // Store current params for agent retries
            _currentDescription = description;
            _currentComponentName = componentName;
            _currentCategory = category;
            _currentSubcategory = subcategory;
            _compileAttempts = 0;
            _errorTracker = new CompilationErrorTracker();
            
            // Load PDF context if enabled
            string contextInfo = "No context used";
            if (useContext)
            {
                this.Message = "Loading context...";
                string[] pdfFiles = null;
                if (!string.IsNullOrWhiteSpace(contextFolder) && Directory.Exists(contextFolder))
                {
                    pdfFiles = Directory.GetFiles(contextFolder, "*.pdf", SearchOption.TopDirectoryOnly);
                }
                _contextCache ??= PdfContextManager.LoadPdfContext(pdfFiles);
                // Truncate context to avoid exceeding model limits
                if (!string.IsNullOrEmpty(_contextCache) && _contextCache.Length > _maxContextLength)
                {
                    _contextCache = _contextCache.Substring(0, _maxContextLength)
                                   + Environment.NewLine + "...[context truncated]...";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"PDF context truncated to {_maxContextLength} characters to avoid exceeding model limits.");
                }

                if (string.IsNullOrEmpty(_contextCache))
                    contextInfo = "No PDF context loaded";
                else
                    contextInfo = $"Using context from PDFs ({_contextCache.Length / 1024} KB)";
            }

            // Output context info
            DA.SetData("Context Info", contextInfo);

            // Build the system prompt with context info and coding guidelines
            string systemPrompt = BuildSystemPrompt(description, componentName, category, subcategory);
            
            // Agent mode started
            _isAgentRunning = _agentMode;
            
            // OpenAI API URL
            string openAiApiUrl = "https://api.openai.com/v1/chat/completions";
            
            // Store for later retry if needed
            _openAIApiKey = apiKey;
            _openAIModel = model;
            _temperature = temperature;
            _maxTokens = maxTokens;
            _timeoutMs = timeout;
            
            // Prepare request for OpenAI API
            var requestPayload = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = description }
                },
                max_tokens = maxTokens,
                temperature = temperature
            };
            
            string body = JsonSerializer.Serialize(requestPayload);
            _currentState = RequestState.Requesting;
            this.Message = "Generating Component...";
            
            // Store request details for potential retries
            _lastUrl = openAiApiUrl;
            _lastBody = body;
            _systemPrompt = systemPrompt;
            
            // Add API key to headers
            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {apiKey}",
                ["Content-Type"] = "application/json"
            };
            
            // Send initial generation request
            POSTWithHeadersAsync(openAiApiUrl, body, headers, timeout);
        }

        /// <summary>
        /// Sends an HTTP POST with custom headers asynchronously.
        /// </summary>
        protected void POSTWithHeadersAsync(string url, string body, Dictionary<string, string> headers, int timeout)
        {
            try
            {
                var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(timeout) };
                
                // Add request headers (exclude content headers like Content-Type)
                foreach (var header in headers)
                {
                    if (!header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        client.DefaultRequestHeaders.Add(header.Key, header.Value);
                    }
                }
                
                var content = new System.Net.Http.StringContent(body, Encoding.UTF8, "application/json");
                
                Task.Run(async () =>
                {
                    try
                    {
                        var resp = await client.PostAsync(url, content).ConfigureAwait(false);
                        string respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        
                        if (resp.IsSuccessStatusCode)
                        {
                            _response = respBody;
                            _currentState = RequestState.Done;
                        }
                        else
                        {
                            _response = $"HTTP Error {(int)resp.StatusCode} {resp.ReasonPhrase}: {respBody}";
                            _currentState = RequestState.Error;
                        }
                    }
                    catch (Exception ex)
                    {
                        _response = ex.Message;
                        _currentState = RequestState.Error;
                    }
                    finally
                    {
                        _shouldExpire = true;
                        ExpireSolution(true);
                        client.Dispose();
                    }
                });
            }
            catch (Exception ex)
            {
                _response = ex.Message;
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
            }
        }

        private void AgentProcessAndCompileCode(IGH_DataAccess DA)
        {
            try
            {
                // Parse OpenAI response
                var jsonDocument = JsonDocument.Parse(_response);
                var choices = jsonDocument.RootElement.GetProperty("choices");
                var firstChoice = choices[0];
                var message = firstChoice.GetProperty("message");
                var generatedText = message.GetProperty("content").GetString() ?? string.Empty;
                
                string cleanCode = CleanGeneratedCode(generatedText);
                
                string className = ExtractClassName(cleanCode);
                string csPath = SaveComponentCode(cleanCode, className);
                string assemblyPath = Path.ChangeExtension(csPath, ".gha");
                
                // Attempt compilation
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
                    
                    // Add the error to our tracker
                    _errorTracker.AddError(ex.Message);
                }
                
                // Handle agent retry logic
                if (!success && _compileAttempts < _maxCompileAttempts)
                {
                    _compileAttempts++;
                    this.Message = $"Retrying ({_compileAttempts}/{_maxCompileAttempts})...";
                    
                    // Create an improved prompt with error feedback
                    string retryPrompt = BuildRetryPrompt(cleanCode, compileEx?.Message ?? "Unknown error");
                    
                    // Send the retry request to OpenAI
                    var retryPayload = new
                    {
                        model = _openAIModel,
                        messages = new[]
                        {
                            new { role = "system", content = "You are an expert C# programmer specializing in Grasshopper component development. Fix compilation errors in the given code." },
                            new { role = "user", content = retryPrompt }
                        },
                        max_tokens = _maxTokens,
                        temperature = Math.Max(0.1, _temperature - 0.1) // Lower temperature for fixes
                    };
                    
                    string retryBody = JsonSerializer.Serialize(retryPayload);
                    _currentState = RequestState.Requesting;
                    
                    // Add API key to headers
                    var headers = new Dictionary<string, string>
                    {
                        ["Authorization"] = $"Bearer {_openAIApiKey}",
                        ["Content-Type"] = "application/json" 
                    };
                    
                    // Send retry request
                    POSTWithHeadersAsync(_lastUrl, retryBody, headers, _timeoutMs);
                    
                    // Don't set outputs yet - wait for the retry
                    return;
                }
                
                // All retries exhausted or successful compilation
                _isAgentRunning = false;
                
                // Set outputs
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
                _isAgentRunning = false;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error processing response: " + ex.Message);
                DA.SetData(0, _response);
                DA.SetData(4, false);
            }
        }

        private void ProcessAndCompileCode(IGH_DataAccess DA)
        {
            try
            {
                // Parse OpenAI response
                var jsonDocument = JsonDocument.Parse(_response);
                var choices = jsonDocument.RootElement.GetProperty("choices");
                var firstChoice = choices[0];
                var message = firstChoice.GetProperty("message");
                var generatedText = message.GetProperty("content").GetString() ?? string.Empty;
                
                string cleanCode = CleanGeneratedCode(generatedText);
                
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
                
                // Output results
                DA.SetData(0, cleanCode);
                DA.SetData(1, className);
                DA.SetData(2, csPath);
                DA.SetData(3, success ? assemblyPath : string.Empty);
                DA.SetData(4, success);
                
                if (!success)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        "Compilation failed: " + (compileEx?.Message ?? "Unknown error"));
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
            
            // Remove markdown code blocks
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
            
            // Remove any leading/trailing tags or metadata
            var lines = cleanCode.Split(new[] {'\r','\n'}, StringSplitOptions.RemoveEmptyEntries).ToList();
            
            // Strip leading XML tags
            while (lines.Count > 0 && (lines[0].TrimStart().StartsWith("<") && lines[0].Contains(">")))
            {
                lines.RemoveAt(0);
            }
            
            // Strip trailing XML tags
            while (lines.Count > 0 && (lines[^1].TrimEnd().EndsWith(">") && lines[^1].Contains("<")))
            {
                lines.RemoveAt(lines.Count - 1);
            }
            
            return string.Join("\n", lines).Trim();
        }

        /// <summary>
        /// Builds the initial system prompt with PDF context and guidelines.
        /// </summary>
        private string BuildSystemPrompt(string description, string componentName, string category, string subcategory)
        {
            var sb = new StringBuilder();
            
            // Core system context
            sb.AppendLine("You are an expert C# programmer specializing in Grasshopper plugin development.");
            sb.AppendLine("Create a complete, well-structured Grasshopper component based on the description provided.");
            
            // Add PDF context if available
            if (!string.IsNullOrEmpty(_contextCache))
            {
                sb.AppendLine("\n--- REFERENCE CONTEXT ---");
                sb.AppendLine(_contextCache);
                sb.AppendLine("--- END REFERENCE CONTEXT ---\n");
            }
            
            // Coding guidelines
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
            
            // Component details
            sb.AppendLine($"\nComponent Description: {description}");
            if (!string.IsNullOrEmpty(componentName))
            {
                sb.AppendLine($"Component Class Name: {componentName}");
            }
            
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

        protected override Bitmap Icon => null;
        
        public override Guid ComponentGuid => new Guid("C28A75F3-4957-41B2-A1D2-9E32CF5A2EF0");
    }
}