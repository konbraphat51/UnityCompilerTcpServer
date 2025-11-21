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
    [InitializeOnLoad]
    public static class CompilerServer
    {
        private const int BUFFER_SIZE = 1024;

        public static bool isRunning { get; private set; } = false;

        private static TcpListener tcpListener;
        private static NetworkStream pendingStream = null;

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

        static CompilerServer()
        {
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        public static void StartServer(int port)
        {
            try
            {
                isRunning = true;

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

        public static void StopServer()
        {
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

            isRunning = false;

            Debug.Log("TCP Server stopped");
        }

        private static async void ListenForClients()
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
                    break;
                }
            }
        }

        private static async Task HandleClientAsync(TcpClient client)
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

        private static async Task SendResponseAsync(NetworkStream stream, string response)
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
        private static void RequestRecompilation(NetworkStream stream)
        {
            // hold the stream to send response after compilation
            pendingStream = stream;

            // assets will not be updated in background
            AssetDatabase.Refresh();

            // forceful recompilation
            CompilationPipeline.RequestScriptCompilation(
                RequestScriptCompilationOptions.CleanBuildCache
            );

            Debug.Log("Recompilation requested");
        }

        private static void OnAssemblyCompilationFinished(
            string assemblyPath,
            CompilerMessage[] compilerMessages
        )
        {
            if (!CompilerServerWindow.isOpened && CompilerServerWindow.singletonInstance.isRunning)
            {
                return;
            }

            Debug.Log("Compilation finished, sending results to client...");
            SendCompilationResult(compilerMessages);
        }

        private static async void SendCompilationResult(CompilerMessage[] messages)
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

        private static string CreateResponse(CompilerMessage[] compilerMessages)
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

    public class CompilerServerWindow : EditorWindow
    {
        private const int DEFAULT_PORT = 5000;

        public static CompilerServerWindow singletonInstance;

        [SerializeField]
        private bool _isRunning = false;
        public bool isRunning => _isRunning;

        [SerializeField]
        private int serverPort = DEFAULT_PORT;

        public static bool isOpened
        {
            get { return HasOpenInstances<CompilerServerWindow>(); }
        }

        [MenuItem("Window/Compiler TCP Server")]
        public static void ShowWindow()
        {
            GetWindow<CompilerServerWindow>("Compiler TCP Server");
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

        private void StartServer(int port)
        {
            singletonInstance = this;

            // let async task handle run while returning result
            // CompilerServer depends on _is
            _isRunning = true;
            CompilerServer.StartServer(port);
        }

        private void StopServer()
        {
            CompilerServer.StopServer();
            _isRunning = false;
        }
    }
}

#endif
