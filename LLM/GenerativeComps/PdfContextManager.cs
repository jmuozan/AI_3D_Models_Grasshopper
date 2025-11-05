using System;
using System.IO;
using System.Reflection;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace LLM.OllamaComps
{
    public static class PdfContextManager
    {
        // Base folder for PDF context, relative to application base directory
        private static readonly string _contextFolderPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "context_for_llm");

        public static string LoadPdfContext(string[] specificFiles = null)
        {
            StringBuilder contextBuilder = new StringBuilder();

            if (!Directory.Exists(_contextFolderPath))
            {
                Directory.CreateDirectory(_contextFolderPath);
                return "No context files found. Created context_for_llm folder.";
            }

            string[] pdfFiles = specificFiles ??
                Directory.GetFiles(_contextFolderPath, "*.pdf", SearchOption.TopDirectoryOnly);

            foreach (string pdfPath in pdfFiles)
            {
                try
                {
                    using (PdfDocument document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import))
                    {
                        contextBuilder.AppendLine($"=== CONTEXT FROM: {Path.GetFileName(pdfPath)} ===");

                        for (int i = 0; i < document.PageCount; i++)
                        {
                            var page = document.Pages[i];
                            // Note: PdfSharp doesn't have built-in text extraction
                            // You may need to use iText7 for text extraction instead
                            contextBuilder.AppendLine("PDF text extraction requires additional implementation");
                        }

                        contextBuilder.AppendLine("=== END CONTEXT ===\n");
                    }
                }
                catch (Exception ex)
                {
                    contextBuilder.AppendLine($"Error loading PDF {Path.GetFileName(pdfPath)}: {ex.Message}");
                }
            }

            return contextBuilder.ToString();
        }
    }
}