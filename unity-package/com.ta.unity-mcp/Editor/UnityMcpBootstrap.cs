using UnityEditor;

namespace TA.UnityMcp
{
    [InitializeOnLoad]
    internal static class UnityMcpBootstrap
    {
        static UnityMcpBootstrap()
        {
            EditorApplication.delayCall += StartServer;
            AssemblyReloadEvents.beforeAssemblyReload += StopServer;
            EditorApplication.quitting += StopServer;
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.delayCall += StartServer;
        }

        private static void StartServer()
        {
            UnityMcpServer.Instance.Start();
        }

        private static void StopServer()
        {
            UnityMcpServer.Instance.Stop();
        }
    }
}
