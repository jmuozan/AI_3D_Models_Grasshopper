using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace src.Utils
{
    /// <summary>
    /// Utility class for extracting text from PDFs and managing context for LLM generation
    /// </summary>
    public class PdfContextExtractor
    {
        private readonly Dictionary<string, string> _contextCache = new Dictionary<string, string>();
        private readonly List<(string Filename, string Content, DateTime LastModified)> _contextFiles = new List<(string, string, DateTime)>();
        
        /// <summary>
        /// Gets total number of context files loaded
        /// </summary>
        public int FileCount => _contextFiles.Count;
        
        /// <summary>
        /// Gets total size of all context content in characters
        /// </summary>
        public int TotalContextSize => _contextFiles.Sum(f => f.Content?.Length ?? 0);
        
        /// <summary>
        /// Load all context files from a directory
        /// </summary>
        public void LoadContextDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                throw new DirectoryNotFoundException($"Context directory not found: {directoryPath}");
                
            // Clear existing context files
            _contextFiles.Clear();
            
            // Get all PDF, text, and code files
            string[] files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .ToArray();
                
            // Process each file
            foreach (string filePath in files)
            {
                try
                {
                    string fileName = Path.GetFileName(filePath);
                    FileInfo fileInfo = new FileInfo(filePath);
                    
                    // Check if we've processed this file before and it hasn't changed
                    string cacheKey = $"{filePath}_{fileInfo.LastWriteTimeUtc.Ticks}";
                    
                    if (_contextCache.TryGetValue(cacheKey, out string cachedContent))
                    {
                        // Use cached content if available
                        _contextFiles.Add((fileName, cachedContent, fileInfo.LastWriteTimeUtc));
                        continue;
                    }
                    
                    // Extract content based on file type
                    string content;
                    
                    if (filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract text from PDF
                        content = ExtractTextFromPdf(filePath);
                    }
                    else
                    {
                        // Read text files directly
                        content = File.ReadAllText(filePath);
                    }
                    
                    // Cache the content
                    _contextCache[cacheKey] = content;
                    
                    // Add to context files list
                    _contextFiles.Add((fileName, content, fileInfo.LastWriteTimeUtc));
                }
                catch (Exception ex)
                {
                    // Log error but continue with other files
                    Console.WriteLine($"Error processing file {filePath}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Extract text from a PDF file
        /// </summary>
        public string ExtractTextFromPdf(string pdfPath)
        {
            StringBuilder text = new StringBuilder();
            
            using (PdfReader pdfReader = new PdfReader(pdfPath))
            {
                using (PdfDocument pdfDoc = new PdfDocument(pdfReader))
                {
                    int pageCount = pdfDoc.GetNumberOfPages();
                    
                    // Process each page
                    for (int i = 1; i <= pageCount; i++)
                    {
                        // Use a text extraction strategy
                        ITextExtractionStrategy strategy = new SimpleTextExtractionStrategy();
                        string pageText = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i), strategy);
                        
                        // Add page number reference and clean up the text
                        text.AppendLine($"--- Page {i} ---");
                        text.AppendLine(CleanupPdfText(pageText));
                        text.AppendLine();
                    }
                }
            }
            
            return text.ToString();
        }
        
        /// <summary>
        /// Extract relevant context from loaded files based on keywords
        /// </summary>
        public string GetRelevantContext(string query, string[] additionalKeywords = null, int maxContextLength = 12000)
        {
            if (_contextFiles.Count == 0)
                return string.Empty;
                
            // Extract keywords from query
            var keywords = ExtractKeywords(query, additionalKeywords);
            
            // Score and rank chunks from all files
            var rankedChunks = new List<(string Source, string Chunk, int Score)>();
            
            foreach (var (filename, content, _) in _contextFiles)
            {
                if (string.IsNullOrEmpty(content))
                    continue;
                    
                // Split content into manageable chunks with overlap
                string[] chunks = SplitIntoChunks(content, 1000, 200);
                
                foreach (var chunk in chunks)
                {
                    // Score chunk based on keyword matches
                    int score = ScoreChunk(chunk, keywords);
                    
                    if (score > 0)
                    {
                        rankedChunks.Add((filename, chunk, score));
                    }
                }
            }
            
            // Sort chunks by relevance score (descending)
            var sortedChunks = rankedChunks
                .OrderByDescending(c => c.Score)
                .ToList();
                
            // Build context string with the most relevant chunks
            var contextBuilder = new StringBuilder();
            int currentLength = 0;
            
            foreach (var (source, chunk, score) in sortedChunks)
            {
                // Check if adding this chunk would exceed max length
                if (currentLength + chunk.Length + 50 > maxContextLength)
                    break;
                    
                // Add chunk with source information
                contextBuilder.AppendLine($"--- From {source} (relevance score: {score}) ---");
                contextBuilder.AppendLine(chunk);
                contextBuilder.AppendLine();
                
                currentLength += chunk.Length + 50; // Account for source and formatting
            }
            
            // If no relevant chunks found, include some general context from highest scoring files
            if (currentLength == 0 && _contextFiles.Count > 0)
            {
                // Score whole files for relevance
                var rankedFiles = _contextFiles
                    .Select(f => (f.Filename, Score: ScoreChunk(f.Content, keywords)))
                    .OrderByDescending(f => f.Score)
                    .Take(2) // Take top 2 most relevant files
                    .ToList();
                    
                foreach (var (filename, score) in rankedFiles)
                {
                    var file = _contextFiles.First(f => f.Filename == filename);
                    
                    // Take beginning of the file up to a reasonable size
                    string sample = file.Content.Length <= 5000 
                        ? file.Content 
                        : file.Content.Substring(0, 5000) + "... [truncated]";
                        
                    contextBuilder.AppendLine($"--- General context from {filename} (score: {score}) ---");
                    contextBuilder.AppendLine(sample);
                    contextBuilder.AppendLine();
                    
                    currentLength += sample.Length + 50;
                    
                    if (currentLength > maxContextLength)
                        break;
                }
            }
            
            return contextBuilder.ToString();
        }
        
        /// <summary>
        /// Extract keywords from a query string
        /// </summary>
        private HashSet<string> ExtractKeywords(string query, string[] additionalKeywords = null)
        {
            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // Add additional keywords if provided
            if (additionalKeywords != null)
            {
                foreach (var keyword in additionalKeywords)
                {
                    if (!string.IsNullOrWhiteSpace(keyword) && keyword.Length > 2)
                        keywords.Add(keyword.Trim().ToLowerInvariant());
                }
            }
            
            // Extract camelCase and PascalCase words
            foreach (Match match in Regex.Matches(query, @"\b[A-Z][a-z]+[A-Z]\w*\b|\b[a-z]+[A-Z]\w*\b"))
            {
                // Split camelCase or PascalCase words
                foreach (var word in SplitCamelCase(match.Value))
                {
                    if (word.Length > 2)
                        keywords.Add(word.ToLowerInvariant());
                }
            }
            
            // Extract regular words
            string[] words = query.Split(new[] { ' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '<', '>', '/', '\\', '-', '_', '=' }, 
                StringSplitOptions.RemoveEmptyEntries);
                
            foreach (var word in words)
            {
                string trimmed = word.Trim().ToLowerInvariant();
                if (trimmed.Length > 2 && !IsStopWord(trimmed))
                    keywords.Add(trimmed);
            }
            
            return keywords;
        }
        
        /// <summary>
        /// Check if a word is a common stop word
        /// </summary>
        private bool IsStopWord(string word)
        {
            // Common English stop words
            string[] stopWords = { "the", "and", "for", "with", "this", "that", "from", "your", "have", "are", "not", "use" };
            return stopWords.Contains(word);
        }
        
        /// <summary>
        /// Split camelCase or PascalCase words
        /// </summary>
        private string[] SplitCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return Array.Empty<string>();
                
            return Regex.Split(input, @"(?<!^)(?=[A-Z])");
        }
        
        /// <summary>
        /// Score a chunk based on keyword matches
        /// </summary>
        private int ScoreChunk(string chunk, HashSet<string> keywords)
        {
            if (string.IsNullOrEmpty(chunk) || keywords.Count == 0)
                return 0;
                
            int score = 0;
            
            // Check each keyword
            foreach (var keyword in keywords)
            {
                // Count occurrences
                int count = CountOccurrences(chunk, keyword);
                
                if (count > 0)
                {
                    // Add to score (weight multiple occurrences)
                    score += Math.Min(count, 5) * 10;
                    
                    // Additional points for keyword in code blocks
                    if (chunk.Contains("```") && CountOccurrences(ExtractCodeBlocks(chunk), keyword) > 0)
                    {
                        score += 20;
                    }
                }
            }
            
            return score;
        }
        
        /// <summary>
        /// Count occurrences of a keyword in text
        /// </summary>
        private int CountOccurrences(string text, string keyword)
        {
            // Simple word boundary match
            return Regex.Matches(text, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase).Count;
        }
        
        /// <summary>
        /// Extract code blocks from text
        /// </summary>
        private string ExtractCodeBlocks(string text)
        {
            var codeBlocks = new StringBuilder();
            
            // Match Markdown code blocks
            var matches = Regex.Matches(text, @"```[\s\S]*?```");
            
            foreach (Match match in matches)
            {
                codeBlocks.AppendLine(match.Value);
            }
            
            return codeBlocks.ToString();
        }
        
        /// <summary>
        /// Split text into overlapping chunks
        /// </summary>
        private string[] SplitIntoChunks(string text, int chunkSize, int overlap)
        {
            if (string.IsNullOrEmpty(text) || chunkSize <= 0)
                return Array.Empty<string>();
                
            var chunks = new List<string>();
            int position = 0;
            
            while (position < text.Length)
            {
                // Calculate actual chunk size
                int size = Math.Min(chunkSize, text.Length - position);
                
                // Ensure we don't cut words
                if (position + size < text.Length)
                {
                    // Look for a good break point (whitespace)
                    int breakPoint = text.LastIndexOfAny(new[] { ' ', '\n', '\r', '\t', '.', '!', '?' }, position + size - 1, Math.Min(50, size));
                    
                    if (breakPoint > position)
                    {
                        size = breakPoint - position + 1;
                    }
                }
                
                // Extract chunk
                chunks.Add(text.Substring(position, size));
                
                // Move position
                position += size - overlap;
                
                // Ensure we make progress even if no good break point was found
                if (position <= 0 || position >= text.Length)
                    break;
            }
            
            return chunks.ToArray();
        }
        
        /// <summary>
        /// Clean up text extracted from PDFs
        /// </summary>
        private string CleanupPdfText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
                
            // Replace multiple whitespace with single space
            text = Regex.Replace(text, @"\s+", " ");
            
            // Fix common PDF extraction issues
            text = text.Replace("•", "- ");  // Fix bullet points
            
            // Fix broken paragraphs (heuristic)
            text = Regex.Replace(text, @"(\w)- (\w)", "$1$2");
            
            // Remove headers/footers (heuristic)
            text = Regex.Replace(text, @"\d+ \| Page(\s|$)", "");
            
            return text.Trim();
        }
    }
}