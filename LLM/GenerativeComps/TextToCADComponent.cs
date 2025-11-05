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
using LLM.Templates;
using System.Drawing;

namespace LLM.GenerativeComps
{
    /// <summary>
    /// KittyCAD (Zoo) Text-to-CAD component - generates 3D models from text prompts
    /// </summary>
    public class TextToCADComponent : GH_Component
    {
        private bool _isGenerating = false;
        private string _statusMessage = "Ready";
        private Task _generationTask;

        public TextToCADComponent()
          : base("Text to CAD", "Text2CAD",
              "Generate 3D models from text descriptions using KittyCAD API (Zoo)",
              "AI Tools", "LLM") { }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Generate", "G", "Toggle to start generation", GH_ParamAccess.item, false);
            pManager.AddTextParameter("API Key", "K", "KittyCAD (Zoo) API Key", GH_ParamAccess.item);
            pManager.AddTextParameter("Prompt", "P", "Text description of 3D model to generate", GH_ParamAccess.item);
            pManager.AddTextParameter("Output Directory", "D", "Directory to save generated files", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Format", "F", "Output format: 1=STL, 2=STEP (recommended), 3=OBJ", GH_ParamAccess.item, 2);
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

            if (!DA.GetData("Generate", ref generate)) return;

            // If not generating, reset state
            if (!generate)
            {
                _isGenerating = false;
                _statusMessage = "Inactive";
                this.Message = _statusMessage;
                DA.SetData(2, false);
                DA.SetData(3, _statusMessage);
                return;
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
                    this.Message = "Complete";

                    if (_generationTask.Exception != null)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _generationTask.Exception.GetBaseException().Message);
                        DA.SetData(2, false);
                        DA.SetData(3, "Failed: " + _generationTask.Exception.GetBaseException().Message);
                    }
                    else
                    {
                        // Task completed successfully - results already set in async method
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

            // Start new generation
            _isGenerating = true;
            _statusMessage = "Starting generation...";
            this.Message = _statusMessage;

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
                    await GenerateAndImportModelAsync(apiKey, prompt, outputDir, format, DA);
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                    RhinoApp.InvokeOnUiThread((Action)(() =>
                    {
                        DA.SetData(2, false);
                        DA.SetData(3, "Error: " + ex.Message);
                        _isGenerating = false;
                    }));
                }
            });

            ExpireSolution(true);
        }

        private async Task GenerateAndImportModelAsync(string apiKey, string prompt, string outputDir, string format, IGH_DataAccess DA)
        {
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            client.Timeout = TimeSpan.FromMinutes(10);

            // Step 1: Create text-to-CAD request
            _statusMessage = "Submitting request...";
            RhinoApp.InvokeOnUiThread((Action)(() => { this.Message = _statusMessage; ExpireSolution(true); }));

            var createPayload = new
            {
                output_format = format,
                prompt = prompt
            };

            string createUrl = "https://api.zoo.dev/ai/text-to-cad";
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

            // Step 2: Poll for completion
            string getUrl = $"https://api.zoo.dev/ai/text-to-cad/{modelId}";
            bool isComplete = false;
            int pollCount = 0;
            const int maxPolls = 120; // 10 minutes at 5 second intervals

            while (!isComplete && pollCount < maxPolls)
            {
                await Task.Delay(5000); // Wait 5 seconds
                pollCount++;

                _statusMessage = $"Generating... ({pollCount * 5}s)";
                RhinoApp.InvokeOnUiThread((Action)(() => { this.Message = _statusMessage; ExpireSolution(true); }));

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

                    // Step 3: Download the file
                    _statusMessage = "Downloading file...";
                    RhinoApp.InvokeOnUiThread((Action)(() => { this.Message = _statusMessage; ExpireSolution(true); }));

                    var outputs = getResult.RootElement.GetProperty("outputs");
                    string fileKey = $"source.{format}";

                    if (!outputs.TryGetProperty(fileKey, out var fileData))
                    {
                        throw new Exception($"Output file '{fileKey}' not found in response");
                    }

                    byte[] fileBytes = Convert.FromBase64String(fileData.GetString());
                    string filePath = Path.Combine(outputDir, $"text-to-cad-output.{format}");

                    await File.WriteAllBytesAsync(filePath, fileBytes);

                    // Step 4: Import into Rhino on UI thread
                    _statusMessage = "Importing to Rhino...";
                    List<Brep> breps = null;

                    RhinoApp.InvokeOnUiThread((Action)(() =>
                    {
                        this.Message = _statusMessage;
                        ExpireSolution(true);

                        breps = ImportStepFile(filePath);

                        // Set outputs
                        DA.SetDataList(0, breps);
                        DA.SetData(1, filePath);
                        DA.SetData(2, true);
                        DA.SetData(3, $"Success! Generated {breps.Count} Breps");

                        _statusMessage = "Complete";
                        _isGenerating = false;
                        this.Message = _statusMessage;
                        ExpireSolution(true);

                        // Move and rename file
                        MoveAndRenameFile(filePath, outputDir, format);
                    }));
                }
                else if (status == "failed")
                {
                    string error = getResult.RootElement.TryGetProperty("error", out var errorProp)
                        ? errorProp.GetString()
                        : "Unknown error";
                    throw new Exception($"Generation failed: {error}");
                }
            }

            if (!isComplete)
            {
                throw new Exception("Generation timed out after 10 minutes");
            }
        }

        private List<Brep> ImportStepFile(string stepFilePath)
        {
            var breps = new List<Brep>();

            if (!File.Exists(stepFilePath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"File not found: {stepFilePath}");
                return breps;
            }

            // Import using Rhino command
            RhinoApp.RunScript($"_-Import \"{stepFilePath}\" _Enter", false);

            // Wait a moment for import to complete
            System.Threading.Thread.Sleep(2000);
            RhinoDoc.ActiveDoc.Views.Redraw();

            // Get all Brep objects that were just imported
            var settings = new ObjectEnumeratorSettings
            {
                ObjectTypeFilter = ObjectType.Brep
            };

            var importedObjects = RhinoDoc.ActiveDoc.Objects.GetObjectList(settings);

            foreach (var obj in importedObjects)
            {
                if (obj.Geometry is Brep brep)
                {
                    breps.Add(brep.DuplicateBrep());
                }

                // Delete the imported object from the document
                RhinoDoc.ActiveDoc.Objects.Delete(obj, true);
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
