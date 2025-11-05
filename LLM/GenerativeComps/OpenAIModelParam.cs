using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Collections.Generic;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel;

namespace LLM.OllamaComps
{
    /// <summary>
    /// Dropdown parameter listing available OpenAI models via API or static fallback.
    /// </summary>
    public class OpenAIModelParam : GH_ValueList
    {
        public OpenAIModelParam()
        {
            Name = "OpenAI Models";
            NickName = "M";
            Description = "Select an OpenAI model via API or static list";
            ListItems.Clear();
            RefreshModels();
        }

        private void RefreshModels()
        {
            // List of (model id, owner)
            var entries = new List<(string Id, string Owner)>();
            string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrEmpty(apiKey))
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    var resp = client.GetAsync("https://api.openai.com/v1/models").Result;
                    if (resp.IsSuccessStatusCode)
                    {
                        var content = resp.Content.ReadAsStringAsync().Result;
                        using var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.TryGetProperty("data", out var data))
                        {
                            foreach (var element in data.EnumerateArray())
                            {
                                if (element.TryGetProperty("id", out var idProp))
                                {
                                    var id = idProp.GetString();
                                    // get owner if available
                                    var owner = element.TryGetProperty("owned_by", out var oProp)
                                        ? oProp.GetString() ?? string.Empty
                                        : string.Empty;
                                    if (!string.IsNullOrEmpty(id))
                                        entries.Add((id, owner));
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            // Fallback static list if API call fails or returns nothing
            if (entries.Count == 0)
            {
                var fallback = new[] { "gpt-4", "gpt-3.5-turbo", "gpt-3.5-turbo-16k" };
                entries = fallback.Select(id => (Id: id, Owner: string.Empty)).ToList();
            }

            // Populate ValueList entries: show model id and owner
            ListItems.Clear();
            foreach (var (id, owner) in entries)
            {
                var label = string.IsNullOrEmpty(owner)
                    ? id
                    : $"{id} ({owner})";
                var expression = $"\"{id}\"";
                ListItems.Add(new GH_ValueListItem(label, expression));
            }
            if (ListItems.Count > 0)
                ListItems[0].Selected = true;
        }

        public override Guid ComponentGuid => new Guid("5FB5C93F-4C9F-4BE4-8EB2-E95E6B9DDF6D");
        public override GH_Exposure Exposure => GH_Exposure.primary;
    }
}