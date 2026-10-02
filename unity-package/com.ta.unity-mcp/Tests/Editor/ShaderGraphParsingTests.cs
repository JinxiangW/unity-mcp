using NUnit.Framework;
using TA.UnityMcp.Compat.ShaderGraph;

namespace TA.UnityMcp.Tests
{
    public sealed class ShaderGraphParsingTests
    {
        [Test]
        public void ParseObjects_ReturnsParseErrorForEmptyText()
        {
            var result = GraphEnvelopeReader.ParseObjects("");

            Assert.AreEqual(0, result.objectCount);
            Assert.AreEqual("Shader Graph source text was empty.", result.parseError);
        }

        [Test]
        public void ParseObjects_ContinuesAfterMalformedObject()
        {
            var result = GraphEnvelopeReader.ParseObjects("{ invalid } { \"m_ObjectId\": \"node-1\" }");

            Assert.AreEqual(1, result.objectCount);
            Assert.AreEqual("node-1", result.FirstObjectString("m_ObjectId"));
            Assert.IsTrue(result.warnings.Count > 0);
            Assert.IsNull(result.parseError);
        }
    }
}
