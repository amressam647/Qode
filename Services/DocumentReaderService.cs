using System;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace LocalCursor.Services
{
    public class DocumentReaderService
    {
        /// <summary>
        /// Reads content from various file types and returns as text.
        /// Supports: .pdf, .xlsx, .xls, .csv, .txt, .log, .json, .xml, .md
        /// </summary>
        public string ReadDocument(string filePath)
        {
            if (!File.Exists(filePath))
                return $"Error: File not found: {filePath}";

            var ext = Path.GetExtension(filePath).ToLower();

            try
            {
                return ext switch
                {
                    ".pdf" => ReadPdf(filePath),
                    ".xlsx" or ".xls" => ReadExcel(filePath),
                    ".csv" => File.ReadAllText(filePath),
                    ".txt" or ".log" or ".json" or ".xml" or ".md" or ".ini" or ".cfg" => File.ReadAllText(filePath),
                    ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => $"[IMAGE: {filePath}] - Image files cannot be read as text. Use vision-capable models.",
                    _ => $"Unsupported file type: {ext}"
                };
            }
            catch (Exception ex)
            {
                return $"Error reading file: {ex.Message}";
            }
        }

        private string ReadPdf(string filePath)
        {
            var sb = new StringBuilder();
            using (var doc = PdfDocument.Open(filePath))
            {
                foreach (Page page in doc.GetPages())
                {
                    sb.AppendLine($"--- Page {page.Number} ---");
                    sb.AppendLine(page.Text);
                }
            }
            return sb.ToString();
        }

        private string ReadExcel(string filePath)
        {
            var sb = new StringBuilder();
            using (var workbook = new XLWorkbook(filePath))
            {
                foreach (var worksheet in workbook.Worksheets)
                {
                    sb.AppendLine($"=== Sheet: {worksheet.Name} ===");
                    var usedRange = worksheet.RangeUsed();
                    if (usedRange == null) continue;

                    foreach (var row in usedRange.Rows())
                    {
                        var cells = new System.Collections.Generic.List<string>();
                        foreach (var cell in row.Cells())
                        {
                            cells.Add(cell.GetString());
                        }
                        sb.AppendLine(string.Join("\t", cells));
                    }
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// For log files, reads the last N lines (tail).
        /// </summary>
        public string TailLog(string filePath, int lines = 100)
        {
            if (!File.Exists(filePath))
                return $"Error: File not found: {filePath}";

            var allLines = File.ReadAllLines(filePath);
            var startIndex = Math.Max(0, allLines.Length - lines);
            var result = new StringBuilder();
            for (int i = startIndex; i < allLines.Length; i++)
            {
                result.AppendLine(allLines[i]);
            }
            return result.ToString();
        }

        /// <summary>
        /// Converts an image to Base64 for vision models.
        /// </summary>
        public string ImageToBase64(string filePath)
        {
            var r = GetImageBase64AndMime(filePath);
            if (r == null)
                return !File.Exists(filePath) ? "Error: File not found" : "Error: Unsupported image format";
            return $"data:{r.Value.MimeType};base64,{r.Value.Base64}";
        }

        /// <summary>
        /// Returns (base64, mimeType) for vision APIs. Returns (null, null) if file not found or not an image.
        /// </summary>
        public (string Base64, string MimeType)? GetImageBase64AndMime(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            var ext = Path.GetExtension(filePath).ToLower().TrimStart('.');
            var mimeType = ext switch
            {
                "png" => "image/png",
                "jpg" or "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "bmp" => "image/bmp",
                "webp" => "image/webp",
                _ => null
            };
            if (mimeType == null)
                return null;

            var bytes = File.ReadAllBytes(filePath);
            var base64 = Convert.ToBase64String(bytes);
            return (base64, mimeType);
        }
    }
}
