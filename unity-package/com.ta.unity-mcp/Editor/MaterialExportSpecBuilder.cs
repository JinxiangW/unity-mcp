using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TA.UnityMcp.Compat;
using TA.UnityMcp.Contracts;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TA.UnityMcp
{
    internal static class MaterialExportSpecBuilder
    {
        public static JObject Build(Material material, string assetPath, string exportProfile, bool includeShaderGraph, bool recursiveShaderGraphs, bool includeRawProperties)
        {
            exportProfile = string.IsNullOrWhiteSpace(exportProfile) ? "ue-pbr" : exportProfile.Trim();

            var properties = CompatServices.Shader.ReadMaterialProperties(material);
            var propertyMap = properties.ToDictionary(property => property.name, StringComparer.Ordinal);
            var shader = material.shader;
            var shaderPath = shader != null ? AssetDatabase.GetAssetPath(shader) : null;
            var isShaderGraph = IsShaderGraphAssetPath(shaderPath);
            var warnings = new JArray();

            if (shader == null)
            {
                warnings.Add(BuildWarning("MATERIAL_WITHOUT_SHADER", "Material does not reference a shader.", "error"));
            }

            var baseColorProperty = FindProperty(propertyMap, "_BaseColor");
            var baseMapProperty = FindProperty(propertyMap, "_BaseMap");
            var normalMapProperty = FindProperty(propertyMap, "_BumpMap");
            var metallicProperty = FindProperty(propertyMap, "_Metallic");
            var metallicMapProperty = FindProperty(propertyMap, "_MetallicGlossMap");
            var smoothnessProperty = FindProperty(propertyMap, "_Smoothness");
            var occlusionMapProperty = FindProperty(propertyMap, "_OcclusionMap");
            var occlusionStrengthProperty = FindProperty(propertyMap, "_OcclusionStrength");
            var emissionToggleProperty = FindProperty(propertyMap, "_Use_Emission");
            var emissionMapProperty = FindProperty(propertyMap, "_EmissionMap");
            var emissionColorProperty = FindProperty(propertyMap, "_EmissionColor");
            var guideTextureProperty = FindProperty(propertyMap, "_GuideTexture");
            var guideTilingProperty = FindProperty(propertyMap, "_GuideTiling");
            var guideStrengthProperty = FindProperty(propertyMap, "_GuideStrength");
            var tilingProperty = FindProperty(propertyMap, "_Tiling");
            var offsetProperty = FindProperty(propertyMap, "_Offset");
            var cutoffProperty = FindProperty(propertyMap, "_Cutoff");
            var metallicTextureToggleProperty = FindProperty(propertyMap, "_Use_Metallic_Texture");

            var smoothness = GetFloatValue(smoothnessProperty);
            var roughness = smoothness.HasValue ? 1f - smoothness.Value : (float?)null;
            var alphaClip = HasKeyword(material.shaderKeywords, "_BUILTIN_AlphaClip")
                            || HasKeyword(material.shaderKeywords, "_BUILTIN_ALPHATEST_ON")
                            || (GetFloatValue(cutoffProperty) ?? 0f) > 0f;
            var surfaceType = material.renderQueue >= 3000 ? "Transparent" : "Opaque";
            var blendMode = alphaClip ? "Masked" : (material.renderQueue >= 3000 ? "Translucent" : "Opaque");
            var twoSided = HasKeyword(material.shaderKeywords, "_DOUBLESIDED_ON") || material.doubleSidedGI;
            var pipeline = InferPipeline(shader, shaderPath);
            var transferMode = isShaderGraph ? "custom_graph_needed" : "material_instance";
            var confidence = isShaderGraph ? 0.45 : 0.9;
            var usesBaseMapAlphaAsOpacity = alphaClip || string.Equals(surfaceType, "Transparent", StringComparison.OrdinalIgnoreCase);
            var opacityValue = usesBaseMapAlphaAsOpacity ? (GetColorAlphaValue(baseColorProperty) ?? 1f) : 1f;

            if (isShaderGraph)
            {
                warnings.Add(BuildWarning(
                    "CUSTOM_SHADERGRAPH_MATERIAL",
                    $"{material.name} uses the custom Shader Graph '{shader.name}'; only the transferable PBR subset is normalized for export.",
                    "warning"));
            }

            if (metallicTextureToggleProperty != null && (GetFloatValue(metallicTextureToggleProperty) ?? 0f) > 0f && GetTextureReference(metallicMapProperty) == null)
            {
                warnings.Add(BuildWarning(
                    "METALLIC_TEXTURE_MISSING",
                    "The shader exposes a metallic texture toggle, but no metallic texture is assigned on this material instance.",
                    "warning"));
            }

            if (roughness.HasValue)
            {
                warnings.Add(BuildWarning(
                    "ROUGHNESS_DERIVED_FROM_SMOOTHNESS",
                    "Roughness is derived as 1 - Unity smoothness because no dedicated roughness texture was assigned.",
                    "info"));
            }

            if (alphaClip && GetTextureReference(baseMapProperty) != null)
            {
                warnings.Add(BuildWarning(
                    "ALPHA_FROM_BASEMAP_INFERENCE",
                    "Opacity/alpha clip is inferred from the base map alpha because no dedicated opacity texture slot exists.",
                    "info"));
            }

            var textures = new JArray();
            AddTextureExport(textures, material.name, "baseColor", "_BaseMap", baseMapProperty, BuildChannelPacking("baseColor.r", "baseColor.g", "baseColor.b", usesBaseMapAlphaAsOpacity ? "opacity" : null));
            AddTextureExport(textures, material.name, "normal", "_BumpMap", normalMapProperty, new JObject { ["rgb"] = "normal" });
            AddTextureExport(textures, material.name, "metallicRoughnessMask", "_MetallicGlossMap", metallicMapProperty, new JObject { ["r"] = "metallic", ["a"] = "smoothness" });
            AddTextureExport(textures, material.name, "occlusion", "_OcclusionMap", occlusionMapProperty, new JObject { ["g"] = "occlusion" });
            AddTextureExport(textures, material.name, "emission", "_EmissionMap", emissionMapProperty, new JObject { ["rgb"] = "emission" });
            AddTextureExport(textures, material.name, "custom.guideTexture", "_GuideTexture", guideTextureProperty, new JObject { ["rgba"] = "custom.guideTexture" });

            var customSemanticGroups = new JArray();
            AddCustomSemanticGroup(customSemanticGroups, "custom.guideTexture", guideTextureProperty, new[] { "_GuideTexture", "_GuideTiling", "_GuideStrength" }, new JObject
            {
                ["guideTiling"] = ToJToken(GetFloatValue(guideTilingProperty)),
                ["guideStrength"] = ToJToken(GetFloatValue(guideStrengthProperty))
            });
            AddCustomSemanticGroup(customSemanticGroups, "dissolve", null, new[]
            {
                "_Invert",
                "_UseBackColor",
                "_BackColor",
                "_UseDithering",
                "_EdgeColor",
                "_EdgeWidth",
                "_EdgeSmoothness",
                "_AffectAlbedo",
                "_GlareColor",
                "_GlareGuideStrength",
                "_GlareWidth",
                "_GlareSmoothness",
                "_GlareOffset"
            }, null);
            AddCustomSemanticGroup(customSemanticGroups, "displacement", null, new[]
            {
                "_DisplacementPerVertex",
                "_DisplacementSmoothness",
                "_DisplacementOffset",
                "_RotationAxis",
                "_RotationMin",
                "_RotationMax",
                "_RandomPositionOffset",
                "_PositionOffset",
                "_Scale",
                "_NormalOffset"
            }, null);

            var spec = new JObject
            {
                ["schemaVersion"] = "unity-material-export-spec/1.0",
                ["exportProfile"] = exportProfile,
                ["exportedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["unityContext"] = new JObject
                {
                    ["unityVersion"] = Application.unityVersion,
                    ["projectPath"] = Directory.GetCurrentDirectory().Replace('\\', '/'),
                    ["renderPipelinePackageVersion"] = ToJToken(CompatServices.Context.renderPipelinePackageVersion),
                    ["shaderGraphPackageVersion"] = ToJToken(CompatServices.Context.shaderGraphPackageVersion)
                },
                ["material"] = JObject.FromObject(new
                {
                    name = material.name,
                    path = assetPath,
                    guid = AssetDatabase.AssetPathToGUID(assetPath),
                    assetType = material.GetType().FullName
                }),
                ["shader"] = shader != null ? JObject.FromObject(new
                {
                    name = shader.name,
                    path = shaderPath,
                    guid = string.IsNullOrWhiteSpace(shaderPath) ? null : AssetDatabase.AssetPathToGUID(shaderPath),
                    isShaderGraph,
                    shaderFamily = isShaderGraph ? "CustomLit" : InferShaderFamily(shader),
                    pipeline
                }) : JValue.CreateNull(),
                ["classification"] = new JObject
                {
                    ["supported"] = true,
                    ["transferMode"] = transferMode,
                    ["confidence"] = confidence,
                    ["targetModel"] = exportProfile,
                    ["notes"] = new JArray(BuildClassificationNotes(isShaderGraph, includeShaderGraph, recursiveShaderGraphs))
                },
                ["surface"] = new JObject
                {
                    ["surfaceType"] = surfaceType,
                    ["blendMode"] = blendMode,
                    ["alphaClip"] = alphaClip,
                    ["alphaCutoff"] = ToJToken(GetFloatValue(cutoffProperty)),
                    ["twoSided"] = twoSided,
                    ["cullMode"] = JValue.CreateNull(),
                    ["renderQueue"] = material.renderQueue,
                    ["enableInstancing"] = material.enableInstancing,
                    ["doubleSidedGI"] = material.doubleSidedGI,
                    ["globalIlluminationFlags"] = material.globalIlluminationFlags.ToString()
                },
                ["semantics"] = new JObject(),
                ["textures"] = textures,
                ["keywords"] = new JObject
                {
                    ["activeMaterialKeywords"] = new JArray(material.shaderKeywords ?? Array.Empty<string>()),
                    ["declaredShaderKeywords"] = shader != null ? new JArray(CompatServices.Shader.ReadKeywords(shader).Select(keyword => keyword.name)) : new JArray()
                },
                ["unityRawProperties"] = includeRawProperties ? BuildRawPropertyExports(properties) : new JArray(),
                ["warnings"] = warnings,
                ["worldDissolveRuntime"] = BuildWorldDissolveRuntimeExport(material, assetPath)
            };

            spec["semantics"]["baseColor"] = BuildTextureSemantic(
                baseMapProperty,
                "baseColor",
                new[] { "_BaseColor", "_BaseMap" },
                baseColorProperty != null ? ToJToken(baseColorProperty.value) : JValue.CreateNull(),
                null,
                BuildUvTransform(baseMapProperty));
            spec["semantics"]["normal"] = BuildTextureSemantic(
                normalMapProperty,
                "normal",
                new[] { "_BumpMap" },
                JValue.CreateNull(),
                null,
                BuildUvTransform(normalMapProperty),
                new JObject { ["scale"] = 1.0 });
            spec["semantics"]["metallic"] = new JObject
            {
                ["value"] = ToJToken(GetFloatValue(metallicProperty)),
                ["textureId"] = GetTextureReference(metallicMapProperty) != null ? "metallicRoughnessMask" : null,
                ["channel"] = GetTextureReference(metallicMapProperty) != null ? "r" : null,
                ["rawPropertyNames"] = new JArray("_Metallic", "_MetallicGlossMap", "_Use_Metallic_Texture")
            };
            spec["semantics"]["roughness"] = new JObject
            {
                ["value"] = ToJToken(roughness),
                ["source"] = roughness.HasValue ? "derived_from_smoothness" : null,
                ["textureId"] = GetTextureReference(metallicMapProperty) != null ? "metallicRoughnessMask" : null,
                ["channel"] = GetTextureReference(metallicMapProperty) != null ? "a" : null,
                ["rawPropertyNames"] = new JArray("_Smoothness", "_MetallicGlossMap"),
                ["conversion"] = roughness.HasValue ? new JObject
                {
                    ["kind"] = "one_minus_smoothness",
                    ["sourceValue"] = ToJToken(smoothness)
                } : JValue.CreateNull()
            };
            spec["semantics"]["emission"] = new JObject
            {
                ["enabled"] = (GetFloatValue(emissionToggleProperty) ?? 0f) > 0f,
                ["color"] = emissionColorProperty != null ? ToJToken(emissionColorProperty.value) : JValue.CreateNull(),
                ["textureId"] = GetTextureReference(emissionMapProperty) != null ? "emission" : null,
                ["rawPropertyNames"] = new JArray("_Use_Emission", "_EmissionColor", "_EmissionMap")
            };
            spec["semantics"]["opacity"] = new JObject
            {
                ["value"] = opacityValue,
                ["textureId"] = usesBaseMapAlphaAsOpacity && GetTextureReference(baseMapProperty) != null ? "baseColor" : null,
                ["channel"] = usesBaseMapAlphaAsOpacity && GetTextureReference(baseMapProperty) != null ? "a" : null,
                ["rawPropertyNames"] = new JArray("_BaseMap", "_Cutoff")
            };
            spec["semantics"]["occlusion"] = new JObject
            {
                ["value"] = ToJToken(GetFloatValue(occlusionStrengthProperty)),
                ["textureId"] = GetTextureReference(occlusionMapProperty) != null ? "occlusion" : null,
                ["channel"] = GetTextureReference(occlusionMapProperty) != null ? "g" : null,
                ["rawPropertyNames"] = new JArray("_OcclusionMap", "_OcclusionStrength")
            };
            spec["semantics"]["uvTransform"] = new JObject
            {
                ["value"] = new JObject
                {
                    ["tiling"] = tilingProperty != null ? ToJToken(tilingProperty.value) : JValue.CreateNull(),
                    ["offset"] = offsetProperty != null ? ToJToken(offsetProperty.value) : JValue.CreateNull()
                },
                ["rawPropertyNames"] = new JArray("_Tiling", "_Offset")
            };
            spec["semantics"]["custom"] = customSemanticGroups;

            if (includeShaderGraph && isShaderGraph && !string.IsNullOrWhiteSpace(shaderPath))
            {
                spec["shaderGraph"] = ShaderGraphBundleBuilder.BuildSection(shaderPath, recursiveShaderGraphs);
            }

            return spec;
        }

        internal static object GetImportSettings(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
            {
                return null;
            }

            if (importer is TextureImporter textureImporter)
            {
                var platforms = new List<object>();
                foreach (var platformName in new[] { "Standalone", "Android", "iPhone", "WebGL" })
                {
                    var platformSettings = textureImporter.GetPlatformTextureSettings(platformName);
                    if (!platformSettings.overridden)
                    {
                        continue;
                    }

                    platforms.Add(new
                    {
                        name = platformName,
                        overridden = platformSettings.overridden,
                        maxTextureSize = platformSettings.maxTextureSize,
                        resizeAlgorithm = platformSettings.resizeAlgorithm.ToString(),
                        format = platformSettings.format.ToString(),
                        textureCompression = platformSettings.textureCompression.ToString(),
                        compressionQuality = platformSettings.compressionQuality,
                        crunchedCompression = platformSettings.crunchedCompression,
                        allowsAlphaSplitting = platformSettings.allowsAlphaSplitting
                    });
                }

                return new
                {
                    importerType = importer.GetType().FullName,
                    textureType = textureImporter.textureType.ToString(),
                    alphaSource = textureImporter.alphaSource.ToString(),
                    mipmapEnabled = textureImporter.mipmapEnabled,
                    sRGBTexture = textureImporter.sRGBTexture,
                    npotScale = textureImporter.npotScale.ToString(),
                    wrapMode = textureImporter.wrapMode.ToString(),
                    wrapModeU = textureImporter.wrapModeU.ToString(),
                    wrapModeV = textureImporter.wrapModeV.ToString(),
                    wrapModeW = textureImporter.wrapModeW.ToString(),
                    filterMode = textureImporter.filterMode.ToString(),
                    anisoLevel = textureImporter.anisoLevel,
                    maxTextureSize = textureImporter.maxTextureSize,
                    isReadable = textureImporter.isReadable,
                    streamingMipmaps = textureImporter.streamingMipmaps,
                    mipMapBias = textureImporter.mipMapBias,
                    platformOverrides = platforms
                };
            }

            return new
            {
                importerType = importer.GetType().FullName
            };
        }

        private static JArray BuildRawPropertyExports(IEnumerable<MaterialPropertyDto> properties)
        {
            var exports = new JArray();

            foreach (var property in properties)
            {
                exports.Add(new JObject
                {
                    ["name"] = property.name,
                    ["type"] = property.type,
                    ["mappedSemantic"] = ToJToken(GetMappedSemantic(property.name)),
                    ["value"] = ToJToken(property.value)
                });
            }

            return exports;
        }

        private static JObject BuildTextureSemantic(MaterialPropertyDto textureProperty, string textureId, IEnumerable<string> rawPropertyNames, JToken constantValue, string channel, JObject uv, JObject extraFields = null)
        {
            var result = new JObject
            {
                ["value"] = constantValue ?? JValue.CreateNull(),
                ["textureId"] = GetTextureReference(textureProperty) != null ? textureId : null,
                ["channel"] = ToJToken(channel),
                ["rawPropertyNames"] = new JArray(rawPropertyNames),
                ["uv"] = ToJToken(uv)
            };

            if (extraFields != null)
            {
                foreach (var property in extraFields.Properties())
                {
                    result[property.Name] = property.Value;
                }
            }

            return result;
        }

        private static JObject BuildUvTransform(MaterialPropertyDto textureProperty)
        {
            return new JObject
            {
                ["set"] = 0,
                ["scale"] = ToJToken(GetTextureTransform(textureProperty, "scale")),
                ["offset"] = ToJToken(GetTextureTransform(textureProperty, "offset"))
            };
        }

        private static void AddTextureExport(JArray textures, string materialName, string semantic, string propertyName, MaterialPropertyDto property, JObject channelPacking)
        {
            var textureReference = GetTextureReference(property);
            if (textureReference == null)
            {
                return;
            }

            var absolutePath = UnityMcpQueries.ToAbsoluteProjectPath(textureReference.path);
            var importSettings = GetImportSettings(textureReference.path);
            var textureType = ReadOptionalMember(importSettings, "textureType")?.ToString();
            var isNormalMap = string.Equals(textureType, "NormalMap", StringComparison.OrdinalIgnoreCase);
            var sRgb = ReadOptionalMember(importSettings, "sRGBTexture") as bool?;
            var extension = Path.GetExtension(textureReference.path);

            textures.Add(new JObject
            {
                ["id"] = GetTextureExportId(semantic),
                ["semantic"] = semantic,
                ["unityPropertyName"] = propertyName,
                ["asset"] = JObject.FromObject(textureReference),
                ["sourceFile"] = new JObject
                {
                    ["absolutePath"] = absolutePath.Replace('\\', '/'),
                    ["extension"] = extension,
                    ["exists"] = File.Exists(absolutePath)
                },
                ["exportFile"] = new JObject
                {
                    ["fileName"] = $"{materialName}__{GetSuggestedTextureExportName(semantic)}{extension}",
                    ["relativePath"] = $"{materialName}__{GetSuggestedTextureExportName(semantic)}{extension}"
                },
                ["usage"] = new JObject
                {
                    ["colorSpace"] = isNormalMap || sRgb == false ? "Linear" : "sRGB",
                    ["isNormalMap"] = isNormalMap,
                    ["uvSet"] = 0,
                    ["channelPacking"] = channelPacking
                },
                ["importSettings"] = ToJToken(importSettings)
            });
        }

        private static void AddCustomSemanticGroup(JArray customSemanticGroups, string semantic, MaterialPropertyDto textureProperty, IEnumerable<string> rawPropertyNames, JObject parameters)
        {
            customSemanticGroups.Add(new JObject
            {
                ["semantic"] = semantic,
                ["textureId"] = ToJToken(GetTextureReference(textureProperty) != null ? GetTextureExportId(semantic) : null),
                ["rawPropertyNames"] = new JArray(rawPropertyNames),
                ["parameters"] = ToJToken(parameters)
            });
        }

        private static MaterialPropertyDto FindProperty(IReadOnlyDictionary<string, MaterialPropertyDto> propertyMap, string name)
        {
            return propertyMap.TryGetValue(name, out var property) ? property : null;
        }

        private static AssetReferenceDto GetTextureReference(MaterialPropertyDto property)
        {
            if (!(property?.value is JObject valueObject))
            {
                return null;
            }

            var textureObject = valueObject["texture"] as JObject;
            return textureObject != null ? textureObject.ToObject<AssetReferenceDto>() : null;
        }

        private static object GetTextureTransform(MaterialPropertyDto property, string key)
        {
            if (!(property?.value is JObject valueObject))
            {
                return null;
            }

            switch (key)
            {
                case "scale":
                    return valueObject["scale"];
                case "offset":
                    return valueObject["offset"];
                default:
                    return null;
            }
        }

        private static string GetMappedSemantic(string propertyName)
        {
            switch (propertyName)
            {
                case "_BaseColor":
                case "_BaseMap":
                    return "baseColor";
                case "_BumpMap":
                    return "normal";
                case "_Metallic":
                case "_MetallicGlossMap":
                    return "metallic";
                case "_Smoothness":
                    return "roughness";
                case "_OcclusionMap":
                case "_OcclusionStrength":
                    return "occlusion";
                case "_EmissionColor":
                case "_EmissionMap":
                case "_Use_Emission":
                    return "emission";
                case "_Cutoff":
                    return "opacity";
                default:
                    return null;
            }
        }

        private static float? GetFloatValue(MaterialPropertyDto property)
        {
            return property?.value switch
            {
                float floatValue => floatValue,
                double doubleValue => (float)doubleValue,
                long longValue => longValue,
                int intValue => intValue,
                _ => null
            };
        }

        private static float? GetColorAlphaValue(MaterialPropertyDto property)
        {
            if (property?.value is JObject jsonColor)
            {
                return jsonColor.Value<float?>("a");
            }

            return property?.value is Color color ? color.a : null;
        }

        private static bool HasKeyword(IEnumerable<string> keywords, string keyword)
        {
            return keywords != null && keywords.Any(candidate => string.Equals(candidate, keyword, StringComparison.OrdinalIgnoreCase));
        }

        private static string InferPipeline(Shader shader, string shaderPath)
        {
            var activePipelineAsset = GraphicsSettings.currentRenderPipeline ?? QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline;
            var activePipelineType = activePipelineAsset != null ? activePipelineAsset.GetType().FullName : null;
            if (!string.IsNullOrWhiteSpace(activePipelineType))
            {
                if (activePipelineType.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "URP";
                }

                if (activePipelineType.IndexOf("HDRenderPipeline", StringComparison.OrdinalIgnoreCase) >= 0
                    || activePipelineType.IndexOf("HighDefinition", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "HDRP";
                }
            }

            var name = shader?.name ?? string.Empty;
            var path = shaderPath ?? string.Empty;

            if (name.StartsWith("HDRP/", StringComparison.OrdinalIgnoreCase)
                || path.IndexOf("render-pipelines.high-definition", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "HDRP";
            }

            if (name.StartsWith("Universal Render Pipeline/", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("URP/", StringComparison.OrdinalIgnoreCase)
                || path.IndexOf("render-pipelines.universal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "URP";
            }

            return "BuiltIn";
        }

        private static string InferShaderFamily(Shader shader)
        {
            if (shader == null)
            {
                return null;
            }

            var name = shader.name ?? string.Empty;
            if (name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Unlit";
            }

            if (name.IndexOf("Lit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Lit";
            }

            return "Custom";
        }

        private static IEnumerable<string> BuildClassificationNotes(bool isShaderGraph, bool includeShaderGraph, bool recursiveShaderGraphs)
        {
            yield return "Core PBR channels are exported as transferable semantics.";

            if (isShaderGraph)
            {
                yield return "This material is driven by a Shader Graph and may require custom graph reconstruction in downstream tools.";
            }

            if (includeShaderGraph)
            {
                yield return recursiveShaderGraphs
                    ? "Shader Graph bundle includes recursively referenced subgraphs."
                    : "Shader Graph bundle only includes the main graph; subgraph references are preserved but not expanded.";
            }
        }

        private static JObject BuildWarning(string code, string message, string severity)
        {
            return new JObject
            {
                ["code"] = code,
                ["message"] = message,
                ["severity"] = severity
            };
        }

        private static JObject BuildChannelPacking(string r, string g, string b, string a)
        {
            return new JObject
            {
                ["r"] = ToJToken(r),
                ["g"] = ToJToken(g),
                ["b"] = ToJToken(b),
                ["a"] = ToJToken(a)
            };
        }

        private static string GetTextureExportId(string semantic)
        {
            switch (semantic)
            {
                case "baseColor":
                    return "baseColor";
                case "normal":
                    return "normal";
                case "metallicRoughnessMask":
                    return "metallicRoughnessMask";
                case "occlusion":
                    return "occlusion";
                case "emission":
                    return "emission";
                default:
                    return semantic.Replace('.', '_');
            }
        }

        private static string GetSuggestedTextureExportName(string semantic)
        {
            switch (semantic)
            {
                case "baseColor":
                    return "BaseColor";
                case "normal":
                    return "Normal";
                case "metallicRoughnessMask":
                    return "MetallicRoughnessMask";
                case "occlusion":
                    return "Occlusion";
                case "emission":
                    return "Emission";
                case "custom.guideTexture":
                    return "GuideTexture";
                default:
                    return semantic.Replace('.', '_');
            }
        }

        private static JObject BuildWorldDissolveRuntimeExport(Material material, string materialAssetPath)
        {
            var worldDissolveType = ResolveTypeByName("INab.WorldDissolve.WorldDissolve");
            if (worldDissolveType == null)
            {
                return new JObject
                {
                    ["available"] = false,
                    ["reason"] = "world_dissolve_type_not_loaded",
                    ["instances"] = new JArray()
                };
            }

            var instances = new JArray();
            foreach (var obj in Resources.FindObjectsOfTypeAll(worldDissolveType))
            {
                if (!(obj is Component component) || !component.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (!ComponentUsesMaterial(component, material, materialAssetPath))
                {
                    continue;
                }

                instances.Add(BuildWorldDissolveInstanceExport(component, materialAssetPath));
            }

            return new JObject
            {
                ["available"] = true,
                ["instanceCount"] = instances.Count,
                ["instances"] = instances
            };
        }

        private static JObject BuildWorldDissolveInstanceExport(Component component, string materialAssetPath)
        {
            var componentType = component.GetType();
            var baseType = componentType.BaseType;
            var gameObject = component.gameObject;

            return new JObject
            {
                ["componentType"] = componentType.FullName,
                ["gameObjectName"] = gameObject.name,
                ["scenePath"] = BuildGameObjectScenePath(gameObject.transform),
                ["sceneName"] = gameObject.scene.name,
                ["enabled"] = component is Behaviour behaviour ? behaviour.enabled : true,
                ["activeInHierarchy"] = gameObject.activeInHierarchy,
                ["shaderType"] = ToJToken(ReadFieldOrPropertyAsString(component, componentType, "propertiesType")),
                ["globalPropertiesId"] = ToJToken(ReadFieldOrPropertyAsString(component, componentType, "globalPropertiesId")),
                ["activeMasks"] = ToJToken(ReadFieldValue<int?>(component, baseType, "activeMasks")),
                ["maskType"] = ToJToken(ReadFieldOrPropertyAsString(component, baseType, "type")),
                ["useDisplacement"] = ToJToken(ReadFieldValue<bool?>(component, baseType, "useDisplacement")),
                ["materialMatches"] = BuildReferencedMaterialsExport(component, baseType, materialAssetPath),
                ["sdfVectors"] = new JObject
                {
                    ["positions"] = SerializeVector4Array(ReadFieldValue<Vector4[]>(component, componentType, "positions")),
                    ["scales"] = SerializeVector4Array(ReadFieldValue<Vector4[]>(component, componentType, "scales")),
                    ["ups"] = SerializeVector4Array(ReadFieldValue<Vector4[]>(component, componentType, "upVectors")),
                    ["rights"] = SerializeVector4Array(ReadFieldValue<Vector4[]>(component, componentType, "rightVectors")),
                    ["forwards"] = SerializeVector4Array(ReadFieldValue<Vector4[]>(component, componentType, "forwardVectors")),
                    ["positions2"] = SerializeVector4Array(ReadFieldValue<Vector4[]>(component, componentType, "positions_2"))
                },
                ["masks"] = BuildMasksExport(ReadFieldValue<System.Collections.IEnumerable>(component, componentType, "masksList"))
            };
        }

        private static JArray BuildReferencedMaterialsExport(Component component, Type baseType, string materialAssetPath)
        {
            var materials = ReadFieldValue<System.Collections.IEnumerable>(component, baseType, "materialsList");
            var exports = new JArray();
            if (materials == null)
            {
                return exports;
            }

            foreach (var entry in materials)
            {
                if (!(entry is Material material))
                {
                    continue;
                }

                var assetPath = AssetDatabase.GetAssetPath(material);
                exports.Add(new JObject
                {
                    ["name"] = material.name,
                    ["path"] = assetPath,
                    ["matchesExportedMaterial"] = string.Equals(assetPath, materialAssetPath, StringComparison.OrdinalIgnoreCase)
                });
            }

            return exports;
        }

        private static JArray BuildMasksExport(System.Collections.IEnumerable masks)
        {
            var exports = new JArray();
            if (masks == null)
            {
                return exports;
            }

            foreach (var entry in masks)
            {
                if (!(entry is Component maskComponent))
                {
                    continue;
                }

                var transform = maskComponent.transform;
                exports.Add(new JObject
                {
                    ["componentType"] = maskComponent.GetType().FullName,
                    ["name"] = maskComponent.gameObject.name,
                    ["scenePath"] = BuildGameObjectScenePath(transform),
                    ["position"] = SerializeVector3(transform.position),
                    ["rotationEuler"] = SerializeVector3(transform.rotation.eulerAngles),
                    ["lossyScale"] = SerializeVector3(transform.lossyScale),
                    ["type"] = ToJToken(ReadFieldOrPropertyAsString(maskComponent, maskComponent.GetType(), "type")),
                    ["colliderScale"] = ToJToken(ReadFieldValue<float?>(maskComponent, maskComponent.GetType(), "colliderScale")),
                    ["angle"] = ToJToken(ReadFieldValue<float?>(maskComponent, maskComponent.GetType(), "angle")),
                    ["angleAdjust"] = ToJToken(ReadFieldValue<float?>(maskComponent, maskComponent.GetType(), "angleAdjust")),
                    ["radiusAdjust"] = ToJToken(ReadFieldValue<float?>(maskComponent, maskComponent.GetType(), "radiusAdjust"))
                });
            }

            return exports;
        }

        private static bool ComponentUsesMaterial(Component component, Material targetMaterial, string targetMaterialAssetPath)
        {
            if (component == null || targetMaterial == null)
            {
                return false;
            }

            var materials = ReadFieldValue<System.Collections.IEnumerable>(component, component.GetType().BaseType, "materialsList");
            if (materials == null)
            {
                return false;
            }

            foreach (var entry in materials)
            {
                if (!(entry is Material material))
                {
                    continue;
                }

                if (material == targetMaterial)
                {
                    return true;
                }

                var materialAssetPath = AssetDatabase.GetAssetPath(material);
                if (!string.IsNullOrWhiteSpace(materialAssetPath)
                    && string.Equals(materialAssetPath, targetMaterialAssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static Type ResolveTypeByName(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static T ReadFieldValue<T>(object source, Type declaredType, string memberName)
        {
            if (source == null || declaredType == null || string.IsNullOrWhiteSpace(memberName))
            {
                return default(T);
            }

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var current = declaredType; current != null; current = current.BaseType)
            {
                var field = current.GetField(memberName, Flags);
                if (field == null)
                {
                    continue;
                }

                var value = field.GetValue(source);
                if (value is T typed)
                {
                    return typed;
                }

                if (value == null)
                {
                    return default(T);
                }

                try
                {
                    return (T)value;
                }
                catch
                {
                    return default(T);
                }
            }

            return default(T);
        }

        private static string ReadFieldOrPropertyAsString(object source, Type declaredType, string memberName)
        {
            if (source == null || declaredType == null || string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var current = declaredType; current != null; current = current.BaseType)
            {
                var field = current.GetField(memberName, Flags);
                if (field != null)
                {
                    return field.GetValue(source)?.ToString();
                }

                var property = current.GetProperty(memberName, Flags);
                if (property != null && property.CanRead)
                {
                    try
                    {
                        return property.GetValue(source, null)?.ToString();
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        private static string BuildGameObjectScenePath(Transform transform)
        {
            if (transform == null)
            {
                return null;
            }

            var segments = new List<string>();
            for (var current = transform; current != null; current = current.parent)
            {
                segments.Add(current.name);
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        private static JArray SerializeVector4Array(Vector4[] values)
        {
            return values == null
                ? new JArray()
                : new JArray(values.Select(value => new JObject
                {
                    ["x"] = value.x,
                    ["y"] = value.y,
                    ["z"] = value.z,
                    ["w"] = value.w
                }));
        }

        private static JObject SerializeVector3(Vector3 value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y,
                ["z"] = value.z
            };
        }

        private static bool IsShaderGraphAssetPath(string assetPath)
        {
            return string.Equals(Path.GetExtension(assetPath), ".shadergraph", StringComparison.OrdinalIgnoreCase);
        }

        private static object ReadOptionalMember(object instance, string memberName)
        {
            if (instance == null)
            {
                return null;
            }

            var type = instance.GetType();
            var property = type.GetProperty(memberName);
            if (property != null)
            {
                return property.GetValue(instance, null);
            }

            var field = type.GetField(memberName);
            return field?.GetValue(instance);
        }

        private static JToken ToJToken(object value)
        {
            return value != null ? JToken.FromObject(value) : JValue.CreateNull();
        }
    }
}
