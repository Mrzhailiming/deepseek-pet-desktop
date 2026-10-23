using System;
using System.Net.Http;
using System.Text;
using DeepseekPet.Bridge;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace DeepseekPet.EditorTools
{
    /// <summary>
    /// 手动往桥上发一条统一格式的事件（走真实的 HTTP + 口令 + 闸门），不用开任何 agent 也能调表现层。
    /// 菜单：Tools / 桌宠 / 发测试事件。需要先进 Play，让桥起来。
    /// </summary>
    public sealed class TestEventWindow : EditorWindow
    {
        static readonly string[] Kinds =
        {
            PetEventKind.UserInput, PetEventKind.Generation, PetEventKind.ToolCall, PetEventKind.ToolResult,
            PetEventKind.TurnStart, PetEventKind.TurnEnd,
        };

        static readonly string[] Sources = { PetSource.Dsh, PetSource.ClaudeCode, PetSource.Codex };

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        int _kind;
        int _source;
        long _tokens = 500;
        string _tool = "edit";
        string _file = "store.js";
        bool _failed;
        string _result = "";

        [MenuItem("Tools/桌宠/发测试事件")]
        static void Open()
        {
            GetWindow<TestEventWindow>("桌宠测试事件");
        }

        void OnGUI()
        {
            _source = EditorGUILayout.Popup("来源", _source, Sources);
            _kind = EditorGUILayout.Popup("种类", _kind, Kinds);
            _tokens = EditorGUILayout.LongField("tokens", _tokens);
            _tool = EditorGUILayout.TextField("工具", _tool);
            _file = EditorGUILayout.TextField("文件", _file);
            _failed = EditorGUILayout.Toggle("失败", _failed);
            if (GUILayout.Button("发送")) Send(false);
            if (GUILayout.Button("用错误口令发送（应当 401）")) Send(true);
            EditorGUILayout.HelpBox(_result == "" ? "先进 Play，让桥起来。" : _result, MessageType.Info);
        }

        void Send(bool wrongToken)
        {
            var config = BridgeConfig.LoadOrCreate(BridgeConfig.DefaultPath());
            var e = new PetEvent
            {
                Id = Sources[_source] + ":editor:" + Guid.NewGuid().ToString("N"),
                Source = Sources[_source],
                Session = "editor",
                Kind = Kinds[_kind],
                Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Tokens = _tokens,
                Output = Kinds[_kind] == PetEventKind.Generation ? _tokens / 4 : 0,
                Tool = string.IsNullOrEmpty(_tool) ? null : _tool,
                File = string.IsNullOrEmpty(_file) ? null : _file,
                Failed = _failed,
            };
            var req = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + config.Port + "/event")
            {
                Content = new StringContent(JsonConvert.SerializeObject(e), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Pet-Token", wrongToken ? "wrong" : config.Token);
            try
            {
                var res = Http.SendAsync(req).Result;
                _result = "HTTP " + (int)res.StatusCode + "  " + e;
            }
            catch (Exception error)
            {
                _result = "发不出去（桥没起来？）：" + error.GetBaseException().Message;
            }
        }
    }
}
