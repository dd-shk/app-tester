// FlowStorage.cs
using System;
using System.IO;
using System.Text.Json;

namespace FlowRunner
{
    internal static class FlowStorage
    {
        public static readonly string RootDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FlowRunner");

        public static readonly string FlowsDir = Path.Combine(RootDir, "flows");

        public static string SafeFileName(string s)
        {
            s = (s ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) s = "Unnamed";

            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');

            s = s.Replace("/", "_").Replace("\\", "_").Replace("..", "_");
            return s.Trim();
        }

        public static string GetCategoryDir(string category)
        {
            category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
            var dir = Path.Combine(FlowsDir, SafeFileName(category));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetFlowDir(string category, string flowName)
        {
            var catDir = GetCategoryDir(category);
            var flowDir = Path.Combine(catDir, SafeFileName(flowName));
            Directory.CreateDirectory(flowDir);
            return flowDir;
        }

        public static string GetFlowJsonPath(string category, string flowName)
        {
            var flowDir = GetFlowDir(category, flowName);
            return Path.Combine(flowDir, "flow.json");
        }

        public static void SaveFlow(FlowDefinition flow)
        {
            Directory.CreateDirectory(FlowsDir);

            flow.Category = string.IsNullOrWhiteSpace(flow.Category) ? "General" : flow.Category.Trim();
            flow.Name = string.IsNullOrWhiteSpace(flow.Name) ? "MyFlow" : flow.Name.Trim();
            flow.Loops = Math.Max(1, flow.Loops);
            flow.UpdatedUtc = DateTime.UtcNow;

            var path = GetFlowJsonPath(flow.Category, flow.Name);

            var json = JsonSerializer.Serialize(flow, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        public static FlowDefinition LoadFlow(string flowJsonPath)
        {
            var json = File.ReadAllText(flowJsonPath);
            var flow = JsonSerializer.Deserialize<FlowDefinition>(json)
                       ?? throw new InvalidOperationException("Invalid flow.json");

            flow.Category = string.IsNullOrWhiteSpace(flow.Category) ? "General" : flow.Category.Trim();
            flow.Name = string.IsNullOrWhiteSpace(flow.Name) ? "MyFlow" : flow.Name.Trim();
            flow.Loops = Math.Max(1, flow.Loops);
            return flow;
        }

        // Fix: Delete must NOT create directories
        public static void DeleteFlow(string category, string flowName)
        {
            category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
            flowName = string.IsNullOrWhiteSpace(flowName) ? "MyFlow" : flowName.Trim();

            var dir = Path.Combine(
                Path.Combine(FlowsDir, SafeFileName(category)),
                SafeFileName(flowName)
            );

            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}