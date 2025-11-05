using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using LLM.Templates;
using System.Drawing;

namespace LLM.GenerativeComps
{
    /// <summary>
    /// OpenAI Vision API component - converts images to CAD modeling prompts
    /// </summary>
    public class ImageToPromptComponent : GH_Component_HTTPAsync
    {
        public ImageToPromptComponent()
          : base("Image to Prompt", "Img2Prompt",
              "Analyze an image and generate a CAD modeling description using OpenAI Vision API",
              "AI Tools", "LLM") { }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Generate", "G", "Toggle to generate prompt from image", GH_ParamAccess.item, false);
            pManager.AddTextParameter("API Key", "K", "OpenAI API Key", GH_ParamAccess.item);
            pManager.AddTextParameter("Image Path", "I", "Path to image file (PNG, JPG, etc.)", GH_ParamAccess.item);
            pManager.AddTextParameter("Model", "M", "OpenAI Vision model to use", GH_ParamAccess.item, "gpt-4o-mini");
            pManager.AddIntegerParameter("Max Tokens", "MT", "Maximum tokens in response", GH_ParamAccess.item, 300);
            pManager.AddIntegerParameter("Timeout", "TO", "Request timeout (ms)", GH_ParamAccess.item, 60000);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Prompt", "P", "Generated CAD modeling prompt", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Success", "S", "Request success status", GH_ParamAccess.item);
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
                        DA.SetData(1, false);
                        _currentState = RequestState.Idle;
                        break;
                    case RequestState.Done:
                        this.Message = "Complete!";
                        ProcessResponse(DA);
                        _currentState = RequestState.Idle;
                        break;
                }
                _shouldExpire = false;
                return;
            }

            bool active = false;
            string apiKey = string.Empty;
            string imagePath = string.Empty;
            string model = "gpt-4o-mini";
            int maxTokens = 300;
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

            if (!DA.GetData("API Key", ref apiKey))
            {
                _response = "API Key is required";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }

            if (!DA.GetData("Image Path", ref imagePath))
            {
                _response = "Image Path is required";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }

            DA.GetData("Model", ref model);
            DA.GetData("Max Tokens", ref maxTokens);
            DA.GetData("Timeout", ref timeout);

            // Validate image path
            if (!File.Exists(imagePath))
            {
                _response = $"Image file not found: {imagePath}";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }

            // Encode image to base64
            string base64Image;
            try
            {
                byte[] imageBytes = File.ReadAllBytes(imagePath);
                base64Image = Convert.ToBase64String(imageBytes);
            }
            catch (Exception ex)
            {
                _response = $"Failed to read image: {ex.Message}";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }

            // Get image extension for MIME type
            string extension = Path.GetExtension(imagePath).ToLower();
            string mimeType = extension switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/png"
            };

            // Build request payload
            var requestPayload = new
            {
                model = model,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "text",
                                text = "Describe an object that can be modeled in CAD with simple operations, being as explicit as possible, using measures if possible and focusing on single, self-contained items rather than assemblies. Try to make descriptions as operations in a CAD software. Try not to build super long prompts."
                            },
                            new
                            {
                                type = "image_url",
                                image_url = new
                                {
                                    url = $"data:{mimeType};base64,{base64Image}"
                                }
                            }
                        }
                    }
                },
                max_tokens = maxTokens
            };

            string body = JsonSerializer.Serialize(requestPayload);
            string url = "https://api.openai.com/v1/chat/completions";

            _currentState = RequestState.Requesting;
            this.Message = "Analyzing Image...";

            // Send request with auth token
            POSTAsync(url, body, "application/json", apiKey, timeout);
        }

        private void ProcessResponse(IGH_DataAccess DA)
        {
            try
            {
                var jsonDocument = JsonDocument.Parse(_response);
                var choices = jsonDocument.RootElement.GetProperty("choices");
                var firstChoice = choices[0];
                var message = firstChoice.GetProperty("message");
                var content = message.GetProperty("content").GetString() ?? string.Empty;

                DA.SetData(0, content);
                DA.SetData(1, true);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to parse response: {ex.Message}");
                DA.SetData(0, _response);
                DA.SetData(1, false);
            }
        }

        protected override Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("E5A7B2C1-3D4E-4F5A-8B9C-1D2E3F4A5B6C");
    }
}
