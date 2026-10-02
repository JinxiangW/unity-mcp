using System.Linq;
using NUnit.Framework;

namespace TA.UnityMcp.Tests
{
    public sealed class MaterialExportProfilesTests
    {
        [Test]
        public void Resolve_UsesDefaultProfileForBlankName()
        {
            var profile = MaterialExportProfiles.Resolve(null);

            Assert.AreEqual(MaterialExportProfiles.DefaultName, profile.name);
            Assert.AreEqual(MaterialExportProfiles.DefaultName, profile.targetModel);
            Assert.IsTrue(profile.isKnown);
            Assert.AreEqual("metallicRoughnessMask", profile.GetTextureExportId("metallicRoughnessMask"));
            Assert.AreEqual("GuideTexture", profile.GetSuggestedTextureExportName("custom.guideTexture"));
        }

        [Test]
        public void Resolve_PreservesUnknownProfileNameWithDefaultMapping()
        {
            var profile = MaterialExportProfiles.Resolve("custom-target");

            Assert.AreEqual("custom-target", profile.name);
            Assert.AreEqual("custom-target", profile.targetModel);
            Assert.IsFalse(profile.isKnown);
            Assert.AreEqual("custom_unknown", profile.GetTextureExportId("custom.unknown"));
        }

        [Test]
        public void BuildClassificationNotes_DescribesShaderGraphBundleScope()
        {
            var profile = MaterialExportProfiles.Resolve("ue-pbr");
            var notes = profile.BuildClassificationNotes(true, true, false).ToList();

            Assert.Contains("Core PBR channels are exported as transferable semantics.", notes);
            Assert.IsTrue(notes.Any(note => note.Contains("Shader Graph")));
            Assert.IsTrue(notes.Any(note => note.Contains("only includes the main graph")));
        }

        [Test]
        public void MapPropertyName_ReturnsTransferSemantics()
        {
            Assert.AreEqual("baseColor", MaterialExportSemantics.MapPropertyName("_BaseMap"));
            Assert.AreEqual("normal", MaterialExportSemantics.MapPropertyName("_BumpMap"));
            Assert.AreEqual("roughness", MaterialExportSemantics.MapPropertyName("_Smoothness"));
            Assert.AreEqual("opacity", MaterialExportSemantics.MapPropertyName("_Cutoff"));
            Assert.IsNull(MaterialExportSemantics.MapPropertyName("_Unknown"));
        }
    }
}
