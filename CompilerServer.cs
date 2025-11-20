// License: Boost Software License 1.0
// Author: Konbraphat51

#if UNITY_EDITOR
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace CompilerServer
{
    public class CompilerServer : EditorWindow
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

        // TCP Server state
        public bool isRunning { get; private set; }
        private TcpListener tcpListener;

        // Store the client stream to send response after compilation
        private NetworkStream pendingStream = null;

        [MenuItem("Window/Compiler TCP Server")]
        public static void ShowWindow()
        {
            GetWindow<CompilerServer>("Compiler TCP Server");
        }

        private void OnGUI()
        {
            // input for port number
            int port = EditorGUILayout.IntField("Port", 5000);

            // run / stop button
            if (!isRunning)
            {
                if (GUILayout.Button("Start Server"))
                {
                    StartServer(port);
                }
            }
            else
            {
                if (GUILayout.Button("Stop Server"))
                {
                    StopServer();
                }
            }
        }

        /// <summary>
        /// Starts the TCP server on the specified port.
        /// </summary>
        /// <param name="port">port number</param>
        private void StartServer(int port)
        {
            try
            {
                tcpListener = new TcpListener(IPAddress.Any, port);
                tcpListener.Start();
                isRunning = true;
                Debug.Log($"TCP Server started on port {port}");
                ListenForClients();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to start TCP Server: {e.Message}");
            }
        }

        /// <summary>
        /// Stops the TCP server.
        /// </summary>
        private void StopServer()
        {
            if (isRunning)
            {
                if (tcpListener != null)
                {
                    tcpListener.Stop();
                    tcpListener = null;
                }
                isRunning = false;
                Debug.Log("TCP Server stopped");
            }
        }

        private async void ListenForClients()
        {
            while (isRunning)
            {
                try
                {
                    TcpClient client = await tcpListener.AcceptTcpClientAsync();
                    HandleClient(client);
                }
                catch (ObjectDisposedException)
                {
                    // Listener has been stopped, exit the loop
                    break;
                }
            }
        }

        private async void HandleClient(TcpClient client)
        {
            Debug.Log("Client connected");
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead;

            try
            {
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Debug.Log($"Received: {request}");

                    string response = await ProcessRequest(request, stream);

                    // If response is not null, send it immediately
                    // (for requests that don't need to wait for compilation)
                    if (response != null)
                    {
                        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                        await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                        Debug.Log($"Sent: {response}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error handling client: {e.Message}");
            }
            finally
            {
                client.Close();
                Debug.Log("Client disconnected");
            }
        }

        private async Task<string> ProcessRequest(string request, NetworkStream stream)
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

            // Stop the server
            StopServer();
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
