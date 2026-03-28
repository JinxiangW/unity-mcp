using TA.ReadOnlyUnityMcp.Compat.Shader;
using TA.ReadOnlyUnityMcp.Compat.ShaderGraph;

namespace TA.ReadOnlyUnityMcp.Compat
{
    internal static class CompatServices
    {
        static CompatServices()
        {
            Context = UnityVersionContext.Create();
            Shader = CreateShaderIntrospector(Context);
            ShaderGraph = CreateShaderGraphReader(Context);
        }

        public static UnityVersionContext Context { get; }

        public static IShaderIntrospector Shader { get; }

        public static IShaderGraphReader ShaderGraph { get; }

        private static IShaderIntrospector CreateShaderIntrospector(UnityVersionContext context)
        {
            if (context.hasShaderGetPropertyApi)
            {
                return new ModernShaderIntrospector();
            }

            return new LegacyShaderIntrospector();
        }

        private static IShaderGraphReader CreateShaderGraphReader(UnityVersionContext context)
        {
            return new TextShaderGraphReader();
        }
    }
}
