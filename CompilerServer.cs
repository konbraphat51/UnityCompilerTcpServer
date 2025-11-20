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
        private const int DEFAULT_PORT = 5000;
        private const int BUFFER_SIZE = 1024;

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

        [SerializeField]
        private bool isRunning = false;

        [SerializeField]
        private int serverPort = DEFAULT_PORT;

        [NonSerialized]
        private TcpListener tcpListener;

        [NonSerialized]
        private NetworkStream pendingStream = null;

        [MenuItem("Window/Compiler TCP Server")]
        public static void ShowWindow()
        {
            GetWindow<CompilerServer>("Compiler TCP Server");
        }

        private void OnGUI()
        {
            // port number input
            if (!isRunning)
            {
                serverPort = EditorGUILayout.IntField("Port", serverPort);
            }

            // run/stop button
            if (!isRunning)
            {
                // wait for running
                if (GUILayout.Button("Start Server"))
                {
                    StartServer(serverPort);
                }
            }
            else
            {
                // wait for stopping
                EditorGUILayout.HelpBox(
                    $"Server is running on port {serverPort}",
                    MessageType.Info
                );
                if (GUILayout.Button("Stop Server"))
                {
                    StopServer();
                }
            }
        }

        private void Awake()
        {
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        private void OnDestroy()
        {
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            StopServer();
        }

        private void StartServer(int port)
        {
            serverPort = port;
            isRunning = true;

            try
            {
                tcpListener = new TcpListener(IPAddress.Any, port);
                tcpListener.Start();

                Debug.Log($"TCP Server started on port {port}");

                ListenForClients();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to start TCP Server: {e.Message}");
                StopServer();
            }
        }

        private void StopServer()
        {
            // guard
            if (!isRunning)
            {
                return;
            }

            // kill listener
            if (tcpListener != null)
            {
                tcpListener.Stop();
                tcpListener = null;
            }

            // kill stream
            if (pendingStream != null)
            {
                pendingStream.Close();
                pendingStream = null;
            }

            Debug.Log("TCP Server stopped");

            isRunning = false;
        }

        private async void ListenForClients()
        {
            // continuously listen
            // this loop does not be finished by re-compilation
            while (isRunning)
            {
                try
                {
                    TcpClient client = await tcpListener.AcceptTcpClientAsync();
                    await HandleClientAsync(client);
                }
                // listening stopped
                catch (ObjectDisposedException)
                {
                    Debug.Log("TCP Listener has been stopped.");
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error accepting client: {e.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            Debug.Log($"Client connected from {client.Client.RemoteEndPoint}");
            NetworkStream stream = null;

            try
            {
                stream = client.GetStream();
                byte[] buffer = new byte[BUFFER_SIZE];
                int bytesRead;

                // re-compile on each request
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Debug.Log($"Received: {request}");

                    RequestRecompilation(stream);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error handling client: {e.Message}");
            }
            finally
            {
                stream?.Close();
                client?.Close();
                Debug.Log("Client disconnected");
            }
        }

        private async Task SendResponseAsync(NetworkStream stream, string response)
        {
            try
            {
                byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                Debug.Log($"Sent: {response}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error sending response: {e.Message}");
            }
        }

        // this needs to be async
        private void RequestRecompilation(NetworkStream stream)
        {
            // hold the stream to send response after compilation
            pendingStream = stream;

            // assets will not be updated in background
            AssetDatabase.Refresh();

            // forceful recompilation
            CompilationPipeline.RequestScriptCompilation(
                RequestScriptCompilationOptions.CleanBuildCache
            );
        }

        private void OnAssemblyCompilationFinished(
            string assemblyPath,
            CompilerMessage[] compilerMessages
        )
        {
            Debug.Log("Compilation finished, sending results to client...");
            SendCompilationResult(compilerMessages);
        }

        private async void SendCompilationResult(CompilerMessage[] messages)
        {
            // guard
            if (!pendingStream.CanWrite)
            {
                Debug.LogError("Cannot send compilation result: stream is not writable");
                pendingStream = null;
                return;
            }

            try
            {
                string response = CreateResponse(messages);
                await SendResponseAsync(pendingStream, response);
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

        private string CreateResponse(CompilerMessage[] compilerMessages)
        {
            MessageResponse response = new MessageResponse
            {
                messages = new MessageItem[compilerMessages.Length],
            };

            for (int cnt = 0; cnt < compilerMessages.Length; cnt++)
            {
                CompilerMessage msg = compilerMessages[cnt];
                response.messages[cnt] = new MessageItem
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
