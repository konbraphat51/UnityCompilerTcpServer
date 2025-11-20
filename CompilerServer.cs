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
            EditorGUILayout.LabelField("Compiler TCP Server", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            serverPort = EditorGUILayout.IntField("Port", serverPort);
            EditorGUILayout.Space();

            if (!isRunning)
            {
                if (GUILayout.Button("Start Server"))
                {
                    StartServer(serverPort);
                }
            }
            else
            {
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

            if (isRunning && tcpListener == null)
            {
                Debug.LogWarning(
                    "Server state was running, but listener is null. Restarting listener..."
                );
                RestartListener();
            }
        }

        private void OnDestroy()
        {
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            StopServer();
        }

        private void StartServer(int port)
        {
            if (isRunning)
            {
                Debug.LogWarning("Server is already running.");
                return;
            }

            try
            {
                serverPort = port;
                tcpListener = new TcpListener(IPAddress.Any, port);
                tcpListener.Start();
                isRunning = true;
                Debug.Log($"TCP Server started on port {port}");
                ListenForClients();
            }
            catch (Exception e)
            {
                isRunning = false;
                Debug.LogError($"Failed to start TCP Server: {e.Message}");
            }
        }

        private void StopServer()
        {
            if (!isRunning)
            {
                return;
            }

            try
            {
                if (tcpListener != null)
                {
                    tcpListener.Stop();
                    tcpListener = null;
                }

                if (pendingStream != null)
                {
                    pendingStream.Close();
                    pendingStream = null;
                }

                isRunning = false;
                Debug.Log("TCP Server stopped");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error stopping TCP Server: {e.Message}");
            }
        }

        private void RestartListener()
        {
            try
            {
                tcpListener = new TcpListener(IPAddress.Any, serverPort);
                tcpListener.Start();
                Debug.Log($"TCP Server listener restarted on port {serverPort}");
                ListenForClients();
            }
            catch (Exception e)
            {
                isRunning = false;
                Debug.LogError($"Failed to restart TCP Server listener: {e.Message}");
            }
        }

        private async void ListenForClients()
        {
            while (isRunning && tcpListener != null)
            {
                try
                {
                    TcpClient client = await tcpListener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client);
                }
                catch (ObjectDisposedException)
                {
                    Debug.Log("Listener stopped, exiting accept loop");
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

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Debug.Log($"Received: {request}");

                    string response = await ProcessRequestAsync(request, stream);

                    if (response != null)
                    {
                        await SendResponseAsync(stream, response);
                    }
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

        private async Task<string> ProcessRequestAsync(string request, NetworkStream stream)
        {
            pendingStream = stream;
            CompilationPipeline.RequestScriptCompilation(
                RequestScriptCompilationOptions.CleanBuildCache
            );
            return null;
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
            if (pendingStream == null)
            {
                return;
            }

            if (!pendingStream.CanWrite)
            {
                Debug.LogWarning("Cannot send compilation result: stream is not writable");
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
            if (compilerMessages == null || compilerMessages.Length == 0)
            {
                return "{}";
            }

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
