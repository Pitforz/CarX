using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarX.EditorTools
{
    // Dev helper: lets the editor be driven through files in the Logs folder (ignored by git).
    //   Logs/carx_cmd.txt  -> commands, one per line; executed, then the file is deleted.
    //   Logs/carx_log.txt  <- compile results, errors and "CarX" messages.
    // Commands: refresh | save | open <scene> | menu <path> | call <Type.Method> [int args] |
    //           shot|shotf <name> <x> <y> <z> <yaw> <pitch> [fov] [w] [h]   (shotf = with a flashlight)
    [InitializeOnLoad]
    static class CarXRemoteBridge
    {
        const string CmdPath = "Logs/carx_cmd.txt";
        const string LogPath = "Logs/carx_log.txt";
        static double nextPoll;

        static CarXRemoteBridge()
        {
            Directory.CreateDirectory("Logs");
            CompilationPipeline.assemblyCompilationFinished -= OnCompiled;
            CompilationPipeline.assemblyCompilationFinished += OnCompiled;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            Write("bridge loaded");
        }

        static void Write(string s)
        {
            try { File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + "\n"); } catch { }
        }

        static void OnCompiled(string asm, CompilerMessage[] msgs)
        {
            int errors = msgs.Count(m => m.type == CompilerMessageType.Error);
            Write("compiled " + Path.GetFileName(asm) + ": " + errors + " errors, " + msgs.Length + " messages");
            foreach (var m in msgs)
                if (m.type == CompilerMessageType.Error || m.message.Contains("CarX") || (m.file != null && m.file.Contains("_Project")))
                    Write("  " + m.type + ": " + m.file + "(" + m.line + "): " + m.message);
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            bool important = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            if (!important && !msg.StartsWith("CarX")) return;
            Write(type + ": " + msg + (important ? "\n" + stack : ""));
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.5;
            if (!File.Exists(CmdPath)) return;
            string[] lines;
            try { lines = File.ReadAllLines(CmdPath); File.Delete(CmdPath); }
            catch { return; }
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                Write("cmd: " + line);
                try { Run(line); }
                catch (Exception e) { Write("cmd failed: " + e); }
            }
        }

        static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

        static void Run(string line)
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string rest = line.Length > parts[0].Length ? line.Substring(parts[0].Length).Trim() : "";
            switch (parts[0])
            {
                case "refresh": AssetDatabase.Refresh(); Write("refreshed"); break;
                case "save": EditorSceneManager.SaveOpenScenes(); Write("saved"); break;
                case "open": EditorSceneManager.OpenScene(rest); Write("opened " + rest); break;
                case "menu": Write("menu " + rest + ": " + EditorApplication.ExecuteMenuItem(rest)); break;
                case "call": Call(parts); break;
                case "shot": Shot(parts, false); break;
                case "shotf": Shot(parts, true); break;
                default: Write("unknown command " + parts[0]); break;
            }
        }

        static void Call(string[] p)
        {
            string full = p[1];
            int dot = full.LastIndexOf('.');
            string typeName = full.Substring(0, dot), method = full.Substring(dot + 1);
            Type type = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null) break;
            }
            if (type == null) { Write("type not found " + typeName); return; }
            var mi = type.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi == null) { Write("method not found " + method); return; }
            var pars = mi.GetParameters();
            var args = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
            {
                string s = p.Length > i + 2 ? p[i + 2] : null;
                if (pars[i].ParameterType == typeof(int)) args[i] = s != null ? int.Parse(s) : 0;
                else if (pars[i].ParameterType == typeof(float)) args[i] = s != null ? F(s) : 0f;
                else if (pars[i].ParameterType == typeof(bool)) args[i] = s != null && s == "true";
                else args[i] = s;
            }
            var result = mi.Invoke(null, args);
            Write("call " + full + " -> " + (result ?? "done"));
        }

        // shot name x y z yaw pitch [fov] [w] [h]
        static void Shot(string[] p, bool flashlight)
        {
            string name = p[1];
            var pos = new Vector3(F(p[2]), F(p[3]), F(p[4]));
            var rot = Quaternion.Euler(F(p[6]), F(p[5]), 0f);
            float fov = p.Length > 7 ? F(p[7]) : 70f;
            int w = p.Length > 9 ? int.Parse(p[8]) : 1280;
            int h = p.Length > 9 ? int.Parse(p[9]) : 720;

            var go = new GameObject("_ShotCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.allowHDR = true;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.requiresDepthTexture = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            if (flashlight) TunnelMazeBuilder.AddFlashlight(go.transform);

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            bool done = false;
            try
            {
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req))
                {
                    RenderPipeline.SubmitRenderRequest(cam, req);
                    done = true;
                }
            }
            catch (Exception e) { Write("render request failed: " + e.Message); }
            if (!done)
            {
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
            }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory("Logs/shots");
            File.WriteAllBytes("Logs/shots/" + name + ".png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(go);
            Write("shot saved " + name + (done ? " (render request)" : " (camera.Render)"));
        }
    }
}
