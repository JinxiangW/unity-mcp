using System.IO;
using TA.UnityMcp.Contracts;

namespace TA.UnityMcp.Compat.ShaderGraph
{
    internal sealed class TextShaderGraphReader : IShaderGraphReader
    {
        private readonly GraphSchemaAdapter adapter = new GraphSchemaAdapter();

        public ShaderGraphReadResultDto Read(string assetPath)
        {
            var absolutePath = UnityMcpQueries.ToAbsoluteProjectPath(assetPath);
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
            var parseResult = GraphEnvelopeReader.ParseObjects(text);
            var envelope = adapter.BuildEnvelope(parseResult);
            var normalizer = new GraphNormalizer(adapter);
            return normalizer.Normalize(envelope);
        }
    }
}
