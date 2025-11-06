using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using Rhino.DocObjects;
using src.Templates;
using System.Drawing;

namespace src.GenerativeComps
{
    /// <summary>
    /// KittyCAD (Zoo) Text-to-CAD component - generates 3D models from text prompts
    /// </summary>
    public class TextToCADComponent : GH_Component
    {
        private bool _isGenerating = false;
        private string _statusMessage = "Ready";
        private Task _generationTask;
        private bool _lastToggleState = false;
        private List<Brep> _lastGeneratedBreps = null;
        private string _lastFilePath = string.Empty;
        private string _lastError = null;

        public TextToCADComponent()
          : base("Text to CAD", "Text2CAD",
              "Generate 3D models from text descriptions using KittyCAD API (Zoo)",
              "AI Tools", "LLM") { }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Generate", "G", "Button trigger - pulse true to start generation", GH_ParamAccess.item, false);
            pManager.AddTextParameter("API Key", "K", "KittyCAD (Zoo) API Key", GH_ParamAccess.item);
            pManager.AddTextParameter("Prompt", "P", "Text description of 3D model to generate", GH_ParamAccess.item);
            pManager.AddTextParameter("Output Directory", "D", "Directory to save generated files", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Format", "F", "Output format: 1=STL, 2=STEP (recommended), 3=OBJ", GH_ParamAccess.item, 2);
            pManager.AddBooleanParameter("Keep File", "KF", "Keep the saved file after import (true = keep, false = delete)", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Breps", "B", "Generated Brep geometries", GH_ParamAccess.list);
            pManager.AddTextParameter("File Path", "FP", "Path to saved model file", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Success", "S", "Generation success status", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "ST", "Current status message", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool generate = false;
            string apiKey = string.Empty;
            string prompt = string.Empty;
            string outputDir = string.Empty;
            int formatCode = 2;
            bool keepFile = true;

            if (!DA.GetData("Generate", ref generate)) return;

            // Detect rising edge (false -> true transition) for button behavior
            bool risingEdge = generate && !_lastToggleState;
            _lastToggleState = generate;

            // Always output cached results or errors, regardless of button state
            if (!_isGenerating)
            {
                _statusMessage = "Ready";
                this.Message = _statusMessage;

                // Output last generated results or error if available
                if (_lastError != null)
                {
                    // Show last error
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastError);
                    DA.SetData(2, false);
                    DA.SetData(3, _lastError);
                }
                else if (_lastGeneratedBreps != null && _lastGeneratedBreps.Count > 0)
                {
                    DA.SetDataList(0, _lastGeneratedBreps);
                    DA.SetData(1, _lastFilePath);
                    DA.SetData(2, true);
                    DA.SetData(3, $"Cached: {_lastGeneratedBreps.Count} Breps");
                }
                else
                {
                    DA.SetData(2, false);
                    DA.SetData(3, _statusMessage);
                }

                // If no rising edge detected and not generating, just return
                if (!risingEdge)
                {
                    return;
                }
            }

            if (!DA.GetData("API Key", ref apiKey))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "API Key is required");
                DA.SetData(2, false);
                return;
            }

            if (!DA.GetData("Prompt", ref prompt))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Prompt is required");
                DA.SetData(2, false);
                return;
            }

            if (!DA.GetData("Output Directory", ref outputDir))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Output Directory is required");
                DA.SetData(2, false);
                return;
            }

            DA.GetData("Format", ref formatCode);
            DA.GetData("Keep File", ref keepFile);

            // Validate format
            if (formatCode < 1 || formatCode > 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Format must be 1 (STL), 2 (STEP), or 3 (OBJ)");
                DA.SetData(2, false);
                return;
            }

            // Create output directory if it doesn't exist
            try
            {
                Directory.CreateDirectory(outputDir);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to create directory: {ex.Message}");
                DA.SetData(2, false);
                return;
            }

            // If already generating, check status
            if (_isGenerating && _generationTask != null)
            {
                if (_generationTask.IsCompleted)
                {
                    _isGenerating = false;

                    if (_generationTask.Exception != null)
                    {
                        // Store error for persistence
                        _lastError = _generationTask.Exception.GetBaseException().Message;
                        _lastGeneratedBreps = null;

                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastError);
                        DA.SetData(2, false);
                        DA.SetData(3, "ERROR: " + _lastError);
                        this.Message = "Error";
                    }
                    else if (_lastError != null)
                    {
                        // Error was set during async execution
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastError);
                        DA.SetData(2, false);
                        DA.SetData(3, "ERROR: " + _lastError);
                        this.Message = "Error";
                    }
                    else
                    {
                        // Task completed successfully - output cached results
                        if (_lastGeneratedBreps != null && _lastGeneratedBreps.Count > 0)
                        {
                            _lastError = null;

                            DA.SetDataList(0, _lastGeneratedBreps);
                            DA.SetData(1, _lastFilePath);
                            DA.SetData(2, true);
                            DA.SetData(3, $"Success! Generated {_lastGeneratedBreps.Count} Breps");
                            this.Message = "Complete";
                        }
                        else
                        {
                            _lastError = "No geometry was generated";

                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _lastError);
                            DA.SetData(2, false);
                            DA.SetData(3, _lastError);
                            this.Message = "No Geometry";
                        }
                    }
                }
                else
                {
                    this.Message = _statusMessage;
                    DA.SetData(3, _statusMessage);
                    ExpireSolution(true);
                }
                return;
            }

            // Only start new generation on rising edge (button press)
            // This section is now handled at the top of SolveInstance
            // risingEdge check ensures we only trigger once per button press

            // Start new generation
            _isGenerating = true;
            _statusMessage = "Starting generation...";
            this.Message = _statusMessage;
            _lastGeneratedBreps = null; // Clear previous results
            _lastError = null; // Clear previous errors

            string format = formatCode switch
            {
                1 => "stl",
                2 => "step",
                3 => "obj",
                _ => "step"
            };

            _generationTask = Task.Run(async () =>
            {
                try
                {
                    await GenerateAndImportModelAsync(apiKey, prompt, outputDir, format, keepFile, DA);
                }
                catch (Exception ex)
                {
                    RhinoApp.InvokeOnUiThread((Action)(() =>
                    {
                        // Store error for persistence
                        string errorMsg = ex.InnerException?.Message ?? ex.Message;
                        _lastError = errorMsg;
                        _lastGeneratedBreps = null;

                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, errorMsg);
                        _statusMessage = "Error: " + errorMsg;
                        _isGenerating = false;
                        this.Message = "Error";
                        ExpireSolution(true);
                    }));
                }
            });

            ExpireSolution(true);
        }

        private async Task GenerateAndImportModelAsync(string apiKey, string prompt, string outputDir, string format, bool keepFile, IGH_DataAccess DA)
        {
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            client.Timeout = TimeSpan.FromMinutes(10);

            // Step 1: Create text-to-CAD request (NEW API - format in URL path)
            _statusMessage = "Submitting request...";
            RhinoApp.InvokeOnUiThread((Action)(() => { this.Message = _statusMessage; }));

            var createPayload = new
            {
                prompt = prompt,
                kcl = false  // Optional: set to true if you want KCL code output
            };

            // NEW: output_format is now a path parameter
            string createUrl = $"https://api.zoo.dev/ai/text-to-cad/{format}";
            var createContent = new System.Net.Http.StringContent(
                JsonSerializer.Serialize(createPayload),
                Encoding.UTF8,
                "application/json");

            var createResponse = await client.PostAsync(createUrl, createContent);
            string createResponseBody = await createResponse.Content.ReadAsStringAsync();

            if (!createResponse.IsSuccessStatusCode)
            {
                throw new Exception($"API Error: {createResponse.StatusCode} - {createResponseBody}");
            }

            var createResult = JsonDocument.Parse(createResponseBody);
            string modelId = createResult.RootElement.GetProperty("id").GetString();

            // Check if it completed immediately (cache hit)
            string initialStatus = createResult.RootElement.GetProperty("status").GetString();

            if (initialStatus == "completed")
            {
                await ProcessCompletedModel(createResult.RootElement, format, outputDir, keepFile, DA);
                return;
            }
            else if (initialStatus == "failed")
            {
                string error = createResult.RootElement.TryGetProperty("error", out var errorProp)
                    ? errorProp.GetString()
                    : "Unknown error";
                throw new Exception($"Generation failed: {error}");
            }

            // Step 2: Poll for completion using async operations endpoint
            string getUrl = $"https://api.zoo.dev/async/operations/{modelId}";
            bool isComplete = false;
            int pollCount = 0;
            const int maxPolls = 120; // 10 minutes at 5 second intervals

            while (!isComplete && pollCount < maxPolls)
            {
                await Task.Delay(5000); // Wait 5 seconds
                pollCount++;

                _statusMessage = $"Generating... ({pollCount * 5}s)";
                RhinoApp.InvokeOnUiThread((Action)(() => { this.Message = _statusMessage; }));

                var getResponse = await client.GetAsync(getUrl);
                string getResponseBody = await getResponse.Content.ReadAsStringAsync();

                if (!getResponse.IsSuccessStatusCode)
                {
                    throw new Exception($"Polling Error: {getResponse.StatusCode} - {getResponseBody}");
                }

                var getResult = JsonDocument.Parse(getResponseBody);
                string status = getResult.RootElement.GetProperty("status").GetString();

                if (status == "completed")
                {
                    isComplete = true;
                    await ProcessCompletedModel(getResult.RootElement, format, outputDir, keepFile, DA);
                }
                else if (status == "failed")
                {
                    string error = getResult.RootElement.TryGetProperty("error", out var errorProp)
                        ? errorProp.GetString()
                        : "Unknown error";
                    throw new Exception($"Generation failed: {error}");
                }
                // If status is still "uploaded", "queued", or other, continue polling
            }

            if (!isComplete)
            {
                throw new Exception("Generation timed out after 10 minutes");
            }
        }

        private async Task ProcessCompletedModel(JsonElement result, string format, string outputDir, bool keepFile, IGH_DataAccess DA)
        {
            // Step 3: Download the file
            _statusMessage = "Downloading file...";
            RhinoApp.InvokeOnUiThread((Action)(() => { this.Message = _statusMessage; }));

            var outputs = result.GetProperty("outputs");
            string fileKey = $"source.{format}";

            if (!outputs.TryGetProperty(fileKey, out var fileData))
            {
                throw new Exception($"Output file '{fileKey}' not found in response. Available outputs: {string.Join(", ", outputs.EnumerateObject().Select(p => p.Name))}");
            }

            byte[] fileBytes;
            string filePath = Path.Combine(outputDir, $"text-to-cad-output.{format}");

            // Check if the output is a string (base64) or an object
            if (fileData.ValueKind == JsonValueKind.String)
            {
                // The file data is base64 encoded
                string base64Data = fileData.GetString();

                try
                {
                    // Remove any whitespace, newlines, or carriage returns that might be in the base64 string
                    base64Data = base64Data.Replace("\r", "").Replace("\n", "").Replace(" ", "").Replace("\t", "");

                    // Fix padding if needed - Base64 strings should be divisible by 4
                    int padding = base64Data.Length % 4;
                    if (padding > 0)
                    {
                        base64Data += new string('=', 4 - padding);
                    }

                    fileBytes = Convert.FromBase64String(base64Data);
                }
                catch (FormatException ex)
                {
                    // If still failing, try to diagnose the issue
                    var invalidChars = new System.Text.StringBuilder();
                    foreach (char c in base64Data.Take(200))
                    {
                        if (!char.IsLetterOrDigit(c) && c != '+' && c != '/' && c != '=')
                        {
                            invalidChars.Append($"'{c}'({(int)c}) ");
                        }
                    }

                    throw new Exception($"Invalid Base64 data received from API. Data length: {base64Data?.Length ?? 0}, Invalid chars found: {invalidChars}. First 100 chars: {(base64Data?.Length > 100 ? base64Data.Substring(0, 100) : base64Data)}. Error: {ex.Message}");
                }
            }
            else if (fileData.ValueKind == JsonValueKind.Object)
            {
                // The API might be returning a URL or other format
                // Check if there's a URL we should download from
                if (fileData.TryGetProperty("url", out var urlElement))
                {
                    string fileUrl = urlElement.GetString();
                    using var httpClient = new System.Net.Http.HttpClient();
                    fileBytes = await httpClient.GetByteArrayAsync(fileUrl);
                }
                else if (fileData.TryGetProperty("data", out var dataElement))
                {
                    string base64Data = dataElement.GetString();
                    fileBytes = Convert.FromBase64String(base64Data);
                }
                else
                {
                    throw new Exception($"Unexpected output format. Object properties: {string.Join(", ", fileData.EnumerateObject().Select(p => p.Name))}");
                }
            }
            else
            {
                throw new Exception($"Unexpected output value type: {fileData.ValueKind}");
            }

            await File.WriteAllBytesAsync(filePath, fileBytes);

            // Step 4: Import into Rhino on UI thread
            _statusMessage = "Importing to Rhino...";
            List<Brep> breps = null;

            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                this.Message = _statusMessage;

                breps = ImportStepFile(filePath);

                // Store results in cache
                _lastGeneratedBreps = breps;
                _lastFilePath = filePath;

                _statusMessage = "Complete";
                _isGenerating = false;
                this.Message = _statusMessage;

                // Handle file cleanup based on keepFile setting
                if (keepFile)
                {
                    // Move and rename file to MODELS folder
                    MoveAndRenameFile(filePath, outputDir, format);
                }
                else
                {
                    // Delete the temporary file
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to delete temporary file: {ex.Message}");
                    }
                }

                // Trigger one final solution to output the results
                ExpireSolution(true);
            }));
        }

        private List<Brep> ImportStepFile(string stepFilePath)
        {
            var breps = new List<Brep>();

            if (!File.Exists(stepFilePath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"File not found: {stepFilePath}");
                return breps;
            }

            try
            {
                // Store object GUIDs before import to identify new objects
                var beforeImport = new HashSet<Guid>();
                foreach (var obj in RhinoDoc.ActiveDoc.Objects)
                {
                    beforeImport.Add(obj.Id);
                }

                // Import using command with options that prevent dialogs
                // Using _-Import with explicit options for batch mode
                string command = string.Format("_-Import \"{0}\" _Enter", stepFilePath);
                bool success = RhinoApp.RunScript(command, false);

                if (!success)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to import STEP file");
                    return breps;
                }

                // Small delay to ensure import completes
                System.Threading.Thread.Sleep(500);

                // Get newly imported objects
                var importedObjects = new List<RhinoObject>();
                foreach (var obj in RhinoDoc.ActiveDoc.Objects)
                {
                    if (!beforeImport.Contains(obj.Id))
                    {
                        importedObjects.Add(obj);
                    }
                }

                // Extract Breps from imported objects
                foreach (var obj in importedObjects)
                {
                    // Handle different geometry types
                    if (obj.Geometry is Brep brep)
                    {
                        breps.Add(brep.DuplicateBrep());
                    }
                    else if (obj.Geometry is Extrusion extrusion)
                    {
                        // Convert extrusion to Brep
                        Brep extrusionBrep = extrusion.ToBrep();
                        if (extrusionBrep != null)
                        {
                            breps.Add(extrusionBrep);
                        }
                    }
                    else if (obj.ObjectType == ObjectType.InstanceReference)
                    {
                        // Handle block instances (common in STEP imports)
                        var instance = obj as InstanceObject;
                        if (instance != null)
                        {
                            var instanceDef = instance.InstanceDefinition;
                            var xform = instance.InstanceXform;

                            foreach (var defObj in instanceDef.GetObjects())
                            {
                                if (defObj.Geometry is Brep instanceBrep)
                                {
                                    var transformedBrep = instanceBrep.DuplicateBrep();
                                    transformedBrep.Transform(xform);
                                    breps.Add(transformedBrep);
                                }
                                else if (defObj.Geometry is Extrusion instanceExtrusion)
                                {
                                    var transformedBrep = instanceExtrusion.ToBrep();
                                    if (transformedBrep != null)
                                    {
                                        transformedBrep.Transform(xform);
                                        breps.Add(transformedBrep);
                                    }
                                }
                            }
                        }
                    }
                }

                // Delete imported objects from document
                foreach (var obj in importedObjects)
                {
                    RhinoDoc.ActiveDoc.Objects.Delete(obj, true);
                }

                RhinoDoc.ActiveDoc.Views.Redraw();
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Import error: {ex.Message}");
            }

            return breps;
        }

        private void MoveAndRenameFile(string sourceFile, string outputDir, string extension)
        {
            try
            {
                string modelsFolder = Path.Combine(outputDir, "MODELS");
                Directory.CreateDirectory(modelsFolder);

                var existingFiles = Directory.GetFiles(modelsFolder, $"*.{extension}")
                    .Concat(Directory.GetFiles(modelsFolder, "*.stp"));
                int modelNumber = existingFiles.Count() + 1;

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
                string newFileName = $"model-{modelNumber}_{timestamp}.{extension}";
                string destPath = Path.Combine(modelsFolder, newFileName);

                if (File.Exists(sourceFile))
                {
                    File.Move(sourceFile, destPath);
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to move file: {ex.Message}");
            }
        }

        protected override Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("F6B8C3D2-4E5F-4A6B-9C1D-2E3F4A5B6C7D");
    }
}
