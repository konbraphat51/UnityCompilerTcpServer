// License: Boost Software License 1.0
// Author: Konbraphat51

#if UNITY_EDITOR

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UnityEditorTcpServer
{
    /// <summary>
    /// Base class for an UnityEditor TCP Server.
    /// </summary>
    /// <remarks>
    /// Inherit from this class and implement the `ProcessRequest()` and `ShowWindow()` methods
    /// Don't forget to add `[MenuItem("Window/WINDOW_NAME")]` attribute to `ShowWindow()`!
    /// </remarks>
    public abstract class EditorTcpServer : EditorWindow
    {
        public bool isRunning { get; protected set; }
        private TcpListener tcpListener;

        /// <summary>
        /// Buisiness logic to process incoming requests.
        /// </summary>
        /// <param name="request">
        /// The incoming request string to be processed.
        /// </param>
        /// <returns>
        /// The response string to be sent back to the client.
        /// </returns>
        public abstract Task<string> ProcessRequest(string request);

        protected virtual void OnGUI()
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
        public void StartServer(int port)
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
        public void StopServer()
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

                    string response = await ProcessRequest(request);
                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                    await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    Debug.Log($"Sent: {response}");
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
    }
}

#endif
