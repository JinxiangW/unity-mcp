using UnityEditor;

namespace TA.ReadOnlyUnityMcp
{
    [InitializeOnLoad]
    internal static class UnityReadOnlyMcpBootstrap
    {
        static UnityReadOnlyMcpBootstrap()
        {
            EditorApplication.delayCall += StartServer;
            AssemblyReloadEvents.beforeAssemblyReload += StopServer;
            EditorApplication.quitting += StopServer;
        }

        private static void StartServer()
        {
            UnityReadOnlyMcpServer.Instance.Start();
        }

        private static void StopServer()
        {
            UnityReadOnlyMcpServer.Instance.Stop();
        }
    }
}
