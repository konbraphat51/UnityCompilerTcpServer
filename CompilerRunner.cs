#if UNITY_EDITOR
using System;
using UnityEditor.Compilation;

namespace CompilerServer
{
    public class CompilerRunner
    {
        public event Action<CompilerMessage[]> OnCompilationFinished;

        /// <summary>
        /// Request Unity to compile scripts.
        /// Result will be returned via OnCompilationFinished event.
        /// </summary>
        public void Compile()
        {
            CompilationPipeline.RequestScriptCompilation(
                RequestScriptCompilationOptions.CleanBuildCache
            );
        }

        /// <summary>
        /// Must be called to subscribe to events.
        /// </summary>
        public void Awake()
        {
            // listen to Unity assembly
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        /// <summary>
        /// Must be called to unsubscribe from events when done.
        /// </summary>
        public void Dispose()
        {
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
        }

        private void OnAssemblyCompilationFinished(
            string assemblyPath,
            CompilerMessage[] compilerMessages
        )
        {
            OnCompilationFinished?.Invoke(compilerMessages);
        }
    }
}
#endif
