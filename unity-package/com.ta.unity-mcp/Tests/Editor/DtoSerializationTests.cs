using System.Collections.Generic;
using NUnit.Framework;
using TA.UnityMcp.Contracts;
using UnityEngine;

namespace TA.UnityMcp.Tests
{
    public sealed class DtoSerializationTests
    {
        [Test]
        public void ShaderGraphInfoDto_RoundTripsThroughJson()
        {
            var dto = new ShaderGraphInfoDto
            {
                format = "multi-json",
                rootType = "UnityEditor.ShaderGraph.GraphData",
                nodeCount = 1,
                edgeCount = 1,
                propertyCount = 1,
                keywordCount = 1,
                warnings = new List<string> { "best-effort" },
                properties = new List<ShaderGraphPropertyDto>
                {
                    new ShaderGraphPropertyDto
                    {
                        objectId = "property-1",
                        displayName = "Base Color",
                        referenceName = "_BaseColor",
                        valueType = "Color"
                    }
                },
                nodes = new List<ShaderGraphNodeDto>
                {
                    new ShaderGraphNodeDto
                    {
                        objectId = "node-1",
                        displayName = "Sample Texture",
                        slots = 2,
                        inputSlots = new List<ShaderGraphSlotDto>(),
                        outputSlots = new List<ShaderGraphSlotDto>()
                    }
                },
                edges = new List<ShaderGraphEdgeDto>
                {
                    new ShaderGraphEdgeDto
                    {
                        objectId = "edge-1",
                        outputNodeId = "node-1",
                        inputNodeId = "node-2"
                    }
                }
            };

            var json = JsonUtility.ToJson(dto);
            var roundTripped = JsonUtility.FromJson<ShaderGraphInfoDto>(json);

            Assert.AreEqual("multi-json", roundTripped.format);
            Assert.AreEqual(1, roundTripped.nodeCount);
            Assert.AreEqual("_BaseColor", roundTripped.properties[0].referenceName);
            Assert.AreEqual("node-1", roundTripped.edges[0].outputNodeId);
            Assert.AreEqual("best-effort", roundTripped.warnings[0]);
        }
    }
}
