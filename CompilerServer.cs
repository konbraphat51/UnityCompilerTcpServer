#if UNITY_EDITOR
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditorTcpServer;
using UnityEngine;

namespace CompilerServer
{
    public class CompilerServer : EditorTcpServer
    {
        [Serializable]
        private class MessageItem
        {
            public string type;
            public string message;
            public string file;
            public int line;
            public int column;
        }

        [Serializable]
        private class MessageResponse
        {
            public MessageItem[] messages;
        }

        // Store the client stream to send response after compilation
        private NetworkStream pendingStream = null;

        [MenuItem("Window/Compiler TCP Server")]
        public static void ShowWindow()
        {
            GetWindow<CompilerServer>("Compiler TCP Server");
        }

        public override async Task<string> ProcessRequest(string request, NetworkStream stream)
        {
            // Store the stream for later response
            pendingStream = stream;

            // Start compilation (will continue even after domain reload)
            CompilationPipeline.RequestScriptCompilation(
                RequestScriptCompilationOptions.CleanBuildCache
            );

            // Return null to indicate response will be sent later
            return null;
        }

        private void Awake()
        {
            // Subscribe to Unity assembly compilation events
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        private void OnDestroy()
        {
            // Unsubscribe from compilation events
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
        }

        private void OnAssemblyCompilationFinished(
            string assemblyPath,
            CompilerMessage[] compilerMessages
        )
        {
            OnCompilationFinished(compilerMessages);
        }

        private async void OnCompilationFinished(CompilerMessage[] messages)
        {
            // Send response to the waiting client
            if (pendingStream != null && pendingStream.CanWrite)
            {
                try
                {
                    string response = CreateResponse(messages);
                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                    await pendingStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    Debug.Log($"Sent compilation result: {response}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to send compilation result: {e.Message}");
                }
                finally
                {
                    pendingStream = null;
                }
            }
        }

        private string CreateResponse(CompilerMessage[] compilerMessages)
        {
            // null guard
            if ((compilerMessages == null) || (compilerMessages.Length == 0))
            {
                return "{}";
            }

            MessageResponse response = new MessageResponse();
            response.messages = new MessageItem[compilerMessages.Length];

            for (int cnt = 0; cnt < compilerMessages.Length; cnt++)
            {
                CompilerMessage msg = compilerMessages[cnt];
                response.messages[cnt] = new MessageItem()
                {
                    type = msg.type.ToString(),
                    message = msg.message,
                    file = msg.file,
                    line = msg.line,
                    column = msg.column,
                };
            }

            return JsonUtility.ToJson(response);
        }
    }
}

#endif
