#if UNITY_EDITOR
using System;
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

        private CompilerRunner compilerRunner = new CompilerRunner();

        [MenuItem("Window/Compiler TCP Server")]
        public static void ShowWindow()
        {
            GetWindow<CompilerServer>("Compiler TCP Server");
        }

        public override async Task<string> ProcessRequest(string request)
        {
            CompilerMessage[] messages = await compilerRunner.Compile();

            return CreateResponse(messages);
        }

        private void Awake()
        {
            compilerRunner.Awake();
        }

        private void OnDestroy()
        {
            compilerRunner.Dispose();
        }

        private string CreateResponse(CompilerMessage[] compilerMessages)
        {
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
