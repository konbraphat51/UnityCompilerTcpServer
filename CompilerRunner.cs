#if UNITY_EDITOR
using System.Threading.Tasks;
using UnityEditor.Compilation;

namespace CompilerServer
{
    public class CompilerRunner
    {
        private bool isRequestedCompiling = false;
        private CompilerMessage[] lastCompilerMessages;

        /// <summary>
        /// Request Unity to compile scripts and wait for the result.
        /// </summary>
        /// <returns>
        /// An array of CompilerMessage objects representing the result of the compilation.
        /// </returns>
        public async Task<CompilerMessage[]> Compile()
        {
            CompilationPipeline.RequestScriptCompilation();
            isRequestedCompiling = true;

            // wait for compilation to finish
            while (isRequestedCompiling)
            {
                await Task.Delay(100);
            }

            return lastCompilerMessages;
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
            lastCompilerMessages = compilerMessages;
            isRequestedCompiling = false;
        }
    }
}
#endif
