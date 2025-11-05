using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace LLM.OllamaComps
{
    /// <summary>
    /// Dropdown parameter listing local Ollama models via 'ollama list'.
    /// </summary>
    public class OllamaModelParam : GH_ValueList
    {
        public OllamaModelParam()
        {
            Name = "Ollama Models";
            NickName = "M";
            Description = "Select an Ollama model installed locally";
            ListItems.Clear();
            RefreshModels();
        }

        private void RefreshModels()
        {
            try
            {
                var psi = new ProcessStartInfo("ollama", "list")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    AddNoModels();
                    return;
                }
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var token = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (string.IsNullOrEmpty(token)) continue;
                    ListItems.Add(new GH_ValueListItem(token, $"\"{token}\""));
                }
                if (ListItems.Count == 0)
                    AddNoModels();
                else
                    ListItems[0].Selected = true;
            }
            catch
            {
                AddNoModels();
            }
        }

        private void AddNoModels()
        {
            ListItems.Clear();
            ListItems.Add(new GH_ValueListItem("<no models>", "\"\""));
            ListItems[0].Selected = true;
        }

        public override Guid ComponentGuid => new Guid("5D2A6B9E-3E47-4CDA-9B14-2F6C4D9B6EB2");
        public override GH_Exposure Exposure => GH_Exposure.primary;
    }
}