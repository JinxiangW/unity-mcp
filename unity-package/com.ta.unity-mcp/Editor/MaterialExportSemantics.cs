namespace TA.UnityMcp
{
    internal static class MaterialExportSemantics
    {
        internal static string MapPropertyName(string propertyName)
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
    }
}
