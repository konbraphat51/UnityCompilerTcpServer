// License: MIT License
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
    /// <summary>
    /// TCP server that listens for request and returns compilation results.
    /// </summary>
    /// <remarks>
    /// - request content is not specified.
    /// - response is in JSON format.
    ///     - { "messages": [ { "type": "Error|Warning|Info", "message": "...", "file": "...", "line": 0, "column": 0 }, ... ] }
    /// - If there are no compilation errors, this forcefully stops because of domain reload.
    /// </remarks>
    [InitializeOnLoad]
    public static class CompilerServer
    {
        private const int BUFFER_SIZE = 1024;

        public static bool isRunning { get; private set; } = false;
        public static bool isResponsing
        {
            get { return CompilerServerWindow.singletonInstance.isResponsing; }
            set { CompilerServerWindow.singletonInstance.isResponsing = value; }
        }

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

        // called on Unity Editor focused
        static CompilerServer()
        {
            // when there is a compilation error (ex. syntax error)
            CompilationPipeline.assemblyCompilationFinished += OnCompileError;

            // when compilation starts because of no error
            CompilationPipeline.assemblyCompilationNotRequired += OnCompileSuccess;
            AssemblyReloadEvents.beforeAssemblyReload += OnCompileSuccess;
        }

        private static bool isUsingServer =>
            CompilerServerWindow.isOpened && CompilerServerWindow.singletonInstance.isRunning;

        /// <summary>
        /// Starts the TCP server.
        /// </summary>
        /// <param name="port">
        /// The port number on which the TCP server will listen for incoming connections.
        /// </param>
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

        /// <summary>
        /// Stops the TCP server.
        /// </summary>
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
            // isRunning will be set to false on StopServer()
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

            try
            {
                pendingStream = client.GetStream();
                byte[] buffer = new byte[BUFFER_SIZE];
                int bytesRead;

                // repeat for each request
                while ((bytesRead = await pendingStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Debug.Log($"Received: {request}");

                    isResponsing = true;
                    RequestRecompilation();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error handling client: {e.Message}");
            }
            finally
            {
                pendingStream?.Close();
                client?.Close();
                Debug.Log("Client disconnected");
            }
        }

        private static async Task SendResponseAsync(string response)
        {
            try
            {
                byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                await pendingStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                Debug.Log($"Sent: {response}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error sending response: {e.Message}");
            }
        }

        // this needs to be async
        private static void RequestRecompilation()
        {
            // assets will not be updated in background
            AssetDatabase.Refresh();

            // recompilation
            CompilationPipeline.RequestScriptCompilation();

            Debug.Log("Recompilation requested");
        }

        private static void OnCompileError(string assemblyPath, CompilerMessage[] compilerMessages)
        {
            // skip if not appropriate time to return
            if (!isUsingServer || !isRunning || !isResponsing)
            {
                return;
            }

            Debug.Log("Compilation finished, sending results to client...");
            SendCompilationResult(compilerMessages);
        }

        private static void OnCompileSuccess(string assemblyPath)
        {
            OnCompileSuccess();
        }

        private static void OnCompileSuccess()
        {
            // skip if not appropriate time to return
            if (!isUsingServer || !isRunning || !isResponsing)
            {
                return;
            }

            Debug.Log("Compilation succeeded, sending success response to client...");
            SendCompilationResult(new CompilerMessage[0]);
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

            isResponsing = false;
            try
            {
                string response = CreateResponse(messages);
                await SendResponseAsync(response);
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

        [SerializeField]
        public bool isResponsing = false;

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

        private void OnEnable()
        {
            // restart server after domain reload
            if (_isRunning && !CompilerServer.isRunning)
            {
                StartServer(serverPort);
            }
        }

        private void OnDestroy()
        {
            // stop server on window close
            StopServer();
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
