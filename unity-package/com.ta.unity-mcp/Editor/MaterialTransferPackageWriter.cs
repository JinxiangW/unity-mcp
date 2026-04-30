using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace TA.UnityMcp
{
    internal static class MaterialTransferPackageWriter
    {
        private const string DefaultOutputFolder = "Assets/MCPExports";
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static object Create(JObject request)
        {
            var materialPath = ResolveAssetPath(ReadString(request, "path"), ReadString(request, "guid"));
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                throw new InvalidOperationException($"Asset at '{materialPath}' is not a Material.");
            }

            var outputFolder = NormalizeAssetPath(ReadString(request, "outputFolder", DefaultOutputFolder));
            ValidateAssetsOutputFolder(outputFolder);

            var packageName = SanitizePathSegment(ReadString(request, "packageName", material.name));
            if (string.IsNullOrWhiteSpace(packageName))
            {
                packageName = "MaterialTransfer";
            }

            var targetFolder = CombineAssetPath(outputFolder, packageName);
            var targetAbsolutePath = ToAbsoluteProjectPath(targetFolder);
            var dryRun = ReadBool(request, "dryRun", false);
            var overwrite = ReadBool(request, "overwrite", false);

            if (File.Exists(targetAbsolutePath))
            {
                ThrowMcp("CONFLICT", $"Transfer package target is a file: '{targetFolder}'.");
            }

            if (Directory.Exists(targetAbsolutePath) && !overwrite && !dryRun)
            {
                ThrowMcp("CONFLICT", $"Transfer package already exists at '{targetFolder}'. Set overwrite=true to update it.");
            }

            var exportProfile = ReadString(request, "exportProfile", "ue-pbr");
            var includeShaderGraph = ReadBool(request, "includeShaderGraph", true);
            var recursiveShaderGraphs = ReadBool(request, "recursiveShaderGraphs", true);
            var includeRawProperties = ReadBool(request, "includeRawProperties", true);

            var spec = MaterialExportSpecBuilder.Build(
                material,
                materialPath,
                exportProfile,
                includeShaderGraph,
                recursiveShaderGraphs,
                includeRawProperties);

            NormalizeTextureExportPaths(spec);

            var missingSources = new JArray();
            var customFunctionCopies = BuildCustomFunctionSourcePlans(spec, targetFolder, targetAbsolutePath, missingSources);
            var plans = BuildWritePlans(spec, targetFolder, targetAbsolutePath);
            plans.AddRange(customFunctionCopies);

            var plannedWrites = new JArray(plans.Select(plan => plan.ToJson(false)));
            var wouldConflict = Directory.Exists(targetAbsolutePath) && !overwrite;
            if (dryRun)
            {
                return BuildResult(
                    material,
                    materialPath,
                    targetFolder,
                    true,
                    true,
                    wouldConflict,
                    plannedWrites,
                    new JArray(),
                    new JArray(),
                    new JArray(),
                    missingSources,
                    ExtractWarnings(spec),
                    BuildVerification(false, true, plans, missingSources));
            }

            Directory.CreateDirectory(targetAbsolutePath);

            var createdAssets = new JArray();
            var updatedAssets = new JArray();
            var skippedAssets = new JArray();

            foreach (var plan in plans)
            {
                if (plan.CopySourceAbsolutePath != null && !File.Exists(plan.CopySourceAbsolutePath))
                {
                    skippedAssets.Add(plan.ToJson(true));
                    continue;
                }

                var existed = File.Exists(plan.AbsolutePath);
                Directory.CreateDirectory(Path.GetDirectoryName(plan.AbsolutePath));

                if (plan.JsonPayload != null)
                {
                    File.WriteAllText(plan.AbsolutePath, JsonConvert.SerializeObject(plan.JsonPayload, Formatting.Indented) + "\n", Utf8NoBom);
                }
                else
                {
                    File.Copy(plan.CopySourceAbsolutePath, plan.AbsolutePath, true);
                }

                if (existed)
                {
                    updatedAssets.Add(plan.AssetPath);
                }
                else
                {
                    createdAssets.Add(plan.AssetPath);
                }
            }

            var verification = BuildVerification(missingSources.Count == 0, false, plans, missingSources);
            var report = BuildVerificationReport(
                material,
                materialPath,
                targetFolder,
                plans,
                createdAssets,
                updatedAssets,
                skippedAssets,
                missingSources,
                ExtractWarnings(spec),
                verification);
            var reportPlan = CreateJsonWritePlan(
                "verification_report.json",
                targetFolder,
                targetAbsolutePath,
                "verification_report",
                report);
            var reportExisted = File.Exists(reportPlan.AbsolutePath);
            File.WriteAllText(reportPlan.AbsolutePath, JsonConvert.SerializeObject(report, Formatting.Indented) + "\n", Utf8NoBom);
            if (reportExisted)
            {
                updatedAssets.Add(reportPlan.AssetPath);
            }
            else
            {
                createdAssets.Add(reportPlan.AssetPath);
            }

            AssetDatabase.Refresh();
            foreach (var assetPath in createdAssets.Values<string>().Concat(updatedAssets.Values<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AssetDatabase.ImportAsset(assetPath);
            }

            return BuildResult(
                material,
                materialPath,
                targetFolder,
                true,
                false,
                false,
                plannedWrites,
                createdAssets,
                updatedAssets,
                skippedAssets,
                missingSources,
                ExtractWarnings(spec),
                verification);
        }

        private static List<WritePlan> BuildWritePlans(JObject spec, string targetFolder, string targetAbsolutePath)
        {
            var plans = new List<WritePlan>
            {
                CreateJsonWritePlan("manifest.json", targetFolder, targetAbsolutePath, "manifest", StripBundlePayload(spec))
            };

            var bundle = spec["shaderGraph"]?["bundle"] as JObject;
            if (bundle != null)
            {
                var mainGraphFile = bundle.Value<string>("mainGraphFile");
                var mainGraph = bundle["mainGraph"];
                if (!string.IsNullOrWhiteSpace(mainGraphFile) && mainGraph != null)
                {
                    plans.Add(CreateJsonWritePlan(mainGraphFile, targetFolder, targetAbsolutePath, "shadergraph", mainGraph));
                }

                var indexFile = bundle.Value<string>("indexFile");
                var index = bundle["index"];
                if (!string.IsNullOrWhiteSpace(indexFile) && index != null)
                {
                    plans.Add(CreateJsonWritePlan(indexFile, targetFolder, targetAbsolutePath, "shadergraph_index", new JObject
                    {
                        ["schemaVersion"] = "unity-shadergraph-export-index/1.0",
                        ["baseDirectory"] = bundle.Value<string>("subgraphsDirectory") ?? "subgraphs",
                        ["entries"] = index.DeepClone()
                    }));
                }

                foreach (var subgraph in bundle["subgraphs"] as JArray ?? new JArray())
                {
                    var exportFile = subgraph.Value<string>("exportFile");
                    if (!string.IsNullOrWhiteSpace(exportFile))
                    {
                        plans.Add(CreateJsonWritePlan(exportFile, targetFolder, targetAbsolutePath, "shadergraph_subgraph", subgraph));
                    }
                }
            }

            foreach (var texture in spec["textures"] as JArray ?? new JArray())
            {
                var sourcePath = texture["sourceFile"]?.Value<string>("absolutePath");
                var relativePath = texture["exportFile"]?.Value<string>("relativePath");
                if (!string.IsNullOrWhiteSpace(sourcePath) && !string.IsNullOrWhiteSpace(relativePath))
                {
                    plans.Add(CreateCopyPlan(relativePath, targetFolder, targetAbsolutePath, "texture", sourcePath));
                }
            }

            return plans;
        }

        private static List<WritePlan> BuildCustomFunctionSourcePlans(JObject spec, string targetFolder, string targetAbsolutePath, JArray missingSources)
        {
            var plansBySource = new Dictionary<string, WritePlan>(StringComparer.OrdinalIgnoreCase);
            var copiedByNode = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var missingByNode = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var bundleItems = EnumerateBundleItems(spec).ToList();

            foreach (var item in bundleItems)
            {
                foreach (var node in item["graph"]?["nodes"] as JArray ?? new JArray())
                {
                    var customFunction = node["customFunction"] as JObject;
                    var sourcePath = customFunction?.Value<string>("functionSourcePath");
                    if (string.IsNullOrWhiteSpace(sourcePath))
                    {
                        continue;
                    }

                    CopyShaderSourceRecursive(
                        NormalizeAssetPath(sourcePath),
                        targetFolder,
                        targetAbsolutePath,
                        plansBySource,
                        copiedByNode,
                        missingByNode);

                    var normalizedSourcePath = NormalizeAssetPath(sourcePath);
                    customFunction["sourceExportFile"] = MakeShaderSourceExportPath(normalizedSourcePath);
                    var availableFiles = copiedByNode.TryGetValue(normalizedSourcePath, out var copied)
                        ? copied.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList()
                        : new List<string>();
                    customFunction["availableSourceFiles"] = new JArray(availableFiles);
                }
            }

            foreach (var entry in missingByNode.SelectMany(entry => entry.Value.Select(source => new { Root = entry.Key, Source = source }))
                         .Distinct()
                         .OrderBy(entry => entry.Source, StringComparer.OrdinalIgnoreCase))
            {
                missingSources.Add(new JObject
                {
                    ["rootSourcePath"] = entry.Root,
                    ["sourcePath"] = entry.Source,
                    ["absolutePath"] = ToAbsoluteProjectPath(entry.Source)
                });
            }

            return plansBySource.Values.OrderBy(plan => plan.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void CopyShaderSourceRecursive(
            string sourceAssetPath,
            string targetFolder,
            string targetAbsolutePath,
            IDictionary<string, WritePlan> plansBySource,
            IDictionary<string, List<string>> copiedByRoot,
            IDictionary<string, List<string>> missingByRoot,
            string rootAssetPath = null)
        {
            if (string.IsNullOrWhiteSpace(sourceAssetPath))
            {
                return;
            }

            sourceAssetPath = NormalizeAssetPath(sourceAssetPath);
            rootAssetPath = NormalizeAssetPath(rootAssetPath ?? sourceAssetPath);
            var exportPath = MakeShaderSourceExportPath(sourceAssetPath);

            if (!copiedByRoot.TryGetValue(rootAssetPath, out var copied))
            {
                copied = new List<string>();
                copiedByRoot[rootAssetPath] = copied;
            }

            var absoluteSourcePath = ToAbsoluteProjectPath(sourceAssetPath);
            if (!File.Exists(absoluteSourcePath))
            {
                if (!missingByRoot.TryGetValue(rootAssetPath, out var missing))
                {
                    missing = new List<string>();
                    missingByRoot[rootAssetPath] = missing;
                }

                missing.Add(sourceAssetPath);
                return;
            }

            if (!copied.Contains(exportPath, StringComparer.OrdinalIgnoreCase))
            {
                copied.Add(exportPath);
            }

            if (plansBySource.ContainsKey(sourceAssetPath))
            {
                return;
            }

            plansBySource[sourceAssetPath] = CreateCopyPlan(exportPath, targetFolder, targetAbsolutePath, "shader_source", absoluteSourcePath);

            string sourceText;
            try
            {
                sourceText = File.ReadAllText(absoluteSourcePath);
            }
            catch
            {
                return;
            }

            foreach (var includePath in ParseLocalIncludes(sourceText))
            {
                var includeAssetPath = ResolveIncludedAssetPath(sourceAssetPath, includePath);
                if (!string.IsNullOrWhiteSpace(includeAssetPath))
                {
                    CopyShaderSourceRecursive(includeAssetPath, targetFolder, targetAbsolutePath, plansBySource, copiedByRoot, missingByRoot, rootAssetPath);
                }
            }
        }

        private static IEnumerable<string> ParseLocalIncludes(string sourceText)
        {
            foreach (Match match in Regex.Matches(sourceText ?? string.Empty, "^\\s*#include\\s+\"([^\"]+)\"", RegexOptions.Multiline))
            {
                yield return match.Groups[1].Value;
            }
        }

        private static string ResolveIncludedAssetPath(string fromAssetPath, string includePath)
        {
            var normalizedInclude = NormalizeAssetPath(includePath);
            if (string.IsNullOrWhiteSpace(normalizedInclude))
            {
                return null;
            }

            if (normalizedInclude.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || normalizedInclude.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedInclude;
            }

            var parent = Path.GetDirectoryName(NormalizeAssetPath(fromAssetPath))?.Replace('\\', '/');
            return NormalizeAssetPath(parent + "/" + normalizedInclude);
        }

        private static IEnumerable<JObject> EnumerateBundleItems(JObject spec)
        {
            var bundle = spec["shaderGraph"]?["bundle"] as JObject;
            if (bundle == null)
            {
                yield break;
            }

            if (bundle["mainGraph"] is JObject mainGraph)
            {
                yield return mainGraph;
            }

            foreach (var subgraph in bundle["subgraphs"] as JArray ?? new JArray())
            {
                if (subgraph is JObject subgraphObject)
                {
                    yield return subgraphObject;
                }
            }
        }

        private static void NormalizeTextureExportPaths(JObject spec)
        {
            foreach (var texture in spec["textures"] as JArray ?? new JArray())
            {
                var exportFile = texture["exportFile"] as JObject;
                if (exportFile == null)
                {
                    continue;
                }

                var fileName = exportFile.Value<string>("fileName");
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = Path.GetFileName(exportFile.Value<string>("relativePath"));
                }

                fileName = SanitizeFileName(fileName);
                exportFile["fileName"] = fileName;
                exportFile["relativePath"] = CombinePackageRelativePath("textures", fileName);
            }
        }

        private static JObject StripBundlePayload(JObject spec)
        {
            var manifest = (JObject)spec.DeepClone();
            var bundle = manifest["shaderGraph"]?["bundle"] as JObject;
            if (bundle != null)
            {
                bundle.Remove("mainGraph");
                bundle.Remove("subgraphs");
                bundle.Remove("index");
            }

            return manifest;
        }

        private static JObject BuildVerificationReport(
            Material material,
            string materialPath,
            string targetFolder,
            IList<WritePlan> plans,
            JArray createdAssets,
            JArray updatedAssets,
            JArray skippedAssets,
            JArray missingSources,
            JArray warnings,
            JObject verification)
        {
            return new JObject
            {
                ["summary"] = new JObject
                {
                    ["success"] = true,
                    ["material"] = material.name,
                    ["materialPath"] = materialPath,
                    ["outputFolder"] = targetFolder,
                    ["plannedWrites"] = plans.Count,
                    ["created"] = createdAssets.Count,
                    ["updated"] = updatedAssets.Count,
                    ["skipped"] = skippedAssets.Count,
                    ["missingSources"] = missingSources.Count
                },
                ["items"] = new JArray(plans.Select(plan => plan.ToJson(false))),
                ["postState"] = new JObject
                {
                    ["createdAssets"] = createdAssets.DeepClone(),
                    ["updatedAssets"] = updatedAssets.DeepClone(),
                    ["skippedAssets"] = skippedAssets.DeepClone(),
                    ["missingSources"] = missingSources.DeepClone()
                },
                ["verification"] = verification.DeepClone(),
                ["warnings"] = warnings.DeepClone()
            };
        }

        private static object BuildResult(
            Material material,
            string materialPath,
            string targetFolder,
            bool success,
            bool dryRun,
            bool wouldConflict,
            JArray plannedWrites,
            JArray createdAssets,
            JArray updatedAssets,
            JArray skippedAssets,
            JArray missingSources,
            JArray warnings,
            JObject verification)
        {
            return new JObject
            {
                ["success"] = success,
                ["dryRun"] = dryRun,
                ["wouldConflict"] = wouldConflict,
                ["material"] = new JObject
                {
                    ["name"] = material.name,
                    ["path"] = materialPath,
                    ["guid"] = AssetDatabase.AssetPathToGUID(materialPath)
                },
                ["outputFolder"] = targetFolder,
                ["plannedWrites"] = plannedWrites,
                ["createdAssets"] = createdAssets,
                ["updatedAssets"] = updatedAssets,
                ["skippedAssets"] = skippedAssets,
                ["missingSources"] = missingSources,
                ["warnings"] = warnings,
                ["verification"] = verification
            };
        }

        private static JObject BuildVerification(bool verified, bool dryRun, IList<WritePlan> plans, JArray missingSources)
        {
            return new JObject
            {
                ["verified"] = verified,
                ["dryRun"] = dryRun,
                ["checks"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "manifest.planned",
                        ["ok"] = plans.Any(plan => plan.RelativePath == "manifest.json")
                    },
                    new JObject
                    {
                        ["name"] = "writes.withinPackage",
                        ["ok"] = plans.All(plan => plan.IsInsidePackageRoot)
                    },
                    new JObject
                    {
                        ["name"] = "missingSources",
                        ["ok"] = missingSources.Count == 0,
                        ["count"] = missingSources.Count
                    }
                }
            };
        }

        private static JArray ExtractWarnings(JObject spec)
        {
            return spec["warnings"] is JArray warnings ? (JArray)warnings.DeepClone() : new JArray();
        }

        private static WritePlan CreateJsonWritePlan(string relativePath, string targetFolder, string targetAbsolutePath, string kind, JToken payload)
        {
            return CreateWritePlan(relativePath, targetFolder, targetAbsolutePath, kind, payload, null);
        }

        private static WritePlan CreateCopyPlan(string relativePath, string targetFolder, string targetAbsolutePath, string kind, string sourceAbsolutePath)
        {
            return CreateWritePlan(relativePath, targetFolder, targetAbsolutePath, kind, null, sourceAbsolutePath);
        }

        private static WritePlan CreateWritePlan(string relativePath, string targetFolder, string targetAbsolutePath, string kind, JToken payload, string sourceAbsolutePath)
        {
            var safeRelativePath = NormalizePackageRelativePath(relativePath);
            var absolutePath = Path.GetFullPath(Path.Combine(targetAbsolutePath, safeRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(targetAbsolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var isInsideRoot = absolutePath.Equals(root, StringComparison.OrdinalIgnoreCase)
                               || absolutePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (!isInsideRoot)
            {
                throw new InvalidOperationException($"Refusing to write outside transfer package root: '{relativePath}'.");
            }

            return new WritePlan
            {
                RelativePath = safeRelativePath,
                AbsolutePath = absolutePath,
                AssetPath = CombineAssetPath(targetFolder, safeRelativePath),
                Kind = kind,
                JsonPayload = payload,
                CopySourceAbsolutePath = sourceAbsolutePath,
                IsInsidePackageRoot = isInsideRoot
            };
        }

        private static string ResolveAssetPath(string path, string guid)
        {
            if (!string.IsNullOrWhiteSpace(guid))
            {
                path = AssetDatabase.GUIDToAssetPath(guid.Trim());
            }

            path = NormalizeAssetPath(path);
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Asset path or GUID is required.");
            }

            var resolvedGuid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrWhiteSpace(resolvedGuid))
            {
                throw new FileNotFoundException($"Asset was not found at '{path}'.");
            }

            return path;
        }

        private static void ValidateAssetsOutputFolder(string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(outputFolder)
                || (!outputFolder.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                    && !outputFolder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                || outputFolder.Split('/').Any(segment => segment == ".."))
            {
                throw new ArgumentException("outputFolder must be a Unity asset path under Assets/.");
            }
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? null : path.Trim().Replace('\\', '/').TrimEnd('/');
        }

        private static string NormalizePackageRelativePath(string path)
        {
            var normalized = NormalizeAssetPath(path);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new InvalidOperationException("Package relative path is required.");
            }

            while (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1);
            }

            if (normalized.Split('/').Any(segment => segment == ".."))
            {
                throw new InvalidOperationException($"Package relative path cannot contain '..': '{path}'.");
            }

            return normalized;
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            if (Path.IsPathRooted(assetPath))
            {
                return Path.GetFullPath(assetPath);
            }

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
        }

        private static string CombineAssetPath(string left, string right)
        {
            return NormalizeAssetPath((left ?? string.Empty).TrimEnd('/') + "/" + (right ?? string.Empty).TrimStart('/'));
        }

        private static string CombinePackageRelativePath(string left, string right)
        {
            return NormalizePackageRelativePath((left ?? string.Empty).TrimEnd('/') + "/" + (right ?? string.Empty).TrimStart('/'));
        }

        private static string MakeShaderSourceExportPath(string assetPath)
        {
            return CombinePackageRelativePath("shader_sources", NormalizeAssetPath(assetPath));
        }

        private static string SanitizePathSegment(string value)
        {
            return SanitizeFileName(value).Trim('.');
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = (value ?? string.Empty).Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray();
            var sanitized = new string(chars).Replace("/", "_").Replace("\\", "_").Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "Unnamed" : sanitized;
        }

        private static string ReadString(JObject request, string name, string defaultValue = null)
        {
            var value = request[name]?.Value<string>();
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
        }

        private static bool ReadBool(JObject request, string name, bool defaultValue)
        {
            var token = request[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return defaultValue;
            }

            return token.Type == JTokenType.Boolean
                ? token.Value<bool>()
                : string.Equals(token.Value<string>(), "true", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(token.Value<string>(), "1", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(token.Value<string>(), "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static void ThrowMcp(string code, string message)
        {
            var exception = new InvalidOperationException(message);
            exception.Data["errorCode"] = code;
            throw exception;
        }

        private sealed class WritePlan
        {
            public string RelativePath;
            public string AbsolutePath;
            public string AssetPath;
            public string Kind;
            public JToken JsonPayload;
            public string CopySourceAbsolutePath;
            public bool IsInsidePackageRoot;

            public JObject ToJson(bool includeSource)
            {
                var payload = new JObject
                {
                    ["kind"] = Kind,
                    ["relativePath"] = RelativePath,
                    ["assetPath"] = AssetPath
                };

                if (includeSource && !string.IsNullOrWhiteSpace(CopySourceAbsolutePath))
                {
                    payload["sourcePath"] = CopySourceAbsolutePath.Replace('\\', '/');
                }

                return payload;
            }
        }
    }
}
