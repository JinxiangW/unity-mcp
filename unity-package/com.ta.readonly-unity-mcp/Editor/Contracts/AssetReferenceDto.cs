namespace TA.ReadOnlyUnityMcp.Contracts
{
    internal sealed class AssetReferenceDto
    {
        public string name;
        public string path;
        public string guid;
        public string type;
    }

    internal sealed class ShaderReferenceDto
    {
        public string name;
        public string path;
        public string guid;
    }

    internal sealed class MaterialReferenceDto
    {
        public string name;
        public string path;
        public string guid;
        public ShaderReferenceDto shader;
    }
}
