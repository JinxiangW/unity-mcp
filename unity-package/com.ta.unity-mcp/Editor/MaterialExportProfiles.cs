using System;
using System.Collections.Generic;

namespace TA.UnityMcp
{
    internal sealed class MaterialExportProfile
    {
        private readonly IReadOnlyDictionary<string, string> textureIds;
        private readonly IReadOnlyDictionary<string, string> textureNameSuffixes;

        internal MaterialExportProfile(
            string name,
            string targetModel,
            bool isKnown,
            IReadOnlyDictionary<string, string> textureIds,
            IReadOnlyDictionary<string, string> textureNameSuffixes)
        {
            this.name = name;
            this.targetModel = targetModel;
            this.isKnown = isKnown;
            this.textureIds = textureIds;
            this.textureNameSuffixes = textureNameSuffixes;
        }

        public readonly string name;
        public readonly string targetModel;
        public readonly bool isKnown;

        internal string GetTextureExportId(string semantic)
        {
            if (string.IsNullOrWhiteSpace(semantic))
            {
                return null;
            }

            return textureIds.TryGetValue(semantic, out var id) ? id : semantic.Replace('.', '_');
        }

        internal string GetSuggestedTextureExportName(string semantic)
        {
            if (string.IsNullOrWhiteSpace(semantic))
            {
                return null;
            }

            return textureNameSuffixes.TryGetValue(semantic, out var suffix) ? suffix : semantic.Replace('.', '_');
        }

        internal IEnumerable<string> BuildClassificationNotes(bool isShaderGraph, bool includeShaderGraph, bool recursiveShaderGraphs)
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
    }

    internal static class MaterialExportProfiles
    {
        internal const string DefaultName = "ue-pbr";

        private static readonly IReadOnlyDictionary<string, string> DefaultTextureIds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["baseColor"] = "baseColor",
            ["normal"] = "normal",
            ["metallicRoughnessMask"] = "metallicRoughnessMask",
            ["occlusion"] = "occlusion",
            ["emission"] = "emission"
        };

        private static readonly IReadOnlyDictionary<string, string> DefaultTextureNameSuffixes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["baseColor"] = "BaseColor",
            ["normal"] = "Normal",
            ["metallicRoughnessMask"] = "MetallicRoughnessMask",
            ["occlusion"] = "Occlusion",
            ["emission"] = "Emission",
            ["custom.guideTexture"] = "GuideTexture"
        };

        private static readonly IReadOnlyDictionary<string, MaterialExportProfile> Profiles = new Dictionary<string, MaterialExportProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultName] = new MaterialExportProfile(
                DefaultName,
                DefaultName,
                true,
                DefaultTextureIds,
                DefaultTextureNameSuffixes)
        };

        internal static MaterialExportProfile Resolve(string requestedProfile)
        {
            var name = string.IsNullOrWhiteSpace(requestedProfile) ? DefaultName : requestedProfile.Trim();
            if (Profiles.TryGetValue(name, out var profile))
            {
                return profile;
            }

            return new MaterialExportProfile(
                name,
                name,
                false,
                DefaultTextureIds,
                DefaultTextureNameSuffixes);
        }
    }
}
