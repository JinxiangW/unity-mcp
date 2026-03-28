using System.IO;
using TA.ReadOnlyUnityMcp.Contracts;

namespace TA.ReadOnlyUnityMcp.Compat.ShaderGraph
{
    internal sealed class TextShaderGraphReader : IShaderGraphReader
    {
        private readonly GraphSchemaAdapter adapter = new GraphSchemaAdapter();

        public ShaderGraphReadResultDto Read(string assetPath)
        {
            var absolutePath = UnityReadOnlyMcpQueries.ToAbsoluteProjectPath(assetPath);
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException("Shader Graph source file was not found.", absolutePath);
            }

            var text = File.ReadAllText(absolutePath);
            return new ShaderGraphReadResultDto
            {
                sourceLength = text.Length,
                graph = Parse(assetPath, text)
            };
        }

        public ShaderGraphInfoDto Parse(string assetPath, string text)
        {
            var objects = GraphEnvelopeReader.ParseObjects(text);
            var envelope = adapter.BuildEnvelope(objects);
            var normalizer = new GraphNormalizer(adapter);
            return normalizer.Normalize(envelope);
        }
    }
}
