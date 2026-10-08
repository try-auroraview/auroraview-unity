using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace AuroraView.Unity
{
    // Local-user transport only. Off by default and never persists through reload/restart.
    [InitializeOnLoad]
    public static class AgentEndpoint
    {
        private sealed class Pending
        {
            public string request;
            public string response;
            public volatile bool cancelled;
            public readonly ManualResetEventSlim ready = new ManualResetEventSlim(false);
        }
        private static readonly ConcurrentQueue<Pending> Requests = new ConcurrentQueue<Pending>();
        private static readonly object Lifecycle = new object();
        private static NamedPipeServerStream pipe;
        private static Thread worker;
        private static volatile bool stopping;
        private static volatile bool listening;
        private static string transportError;
        public static bool Enabled => worker != null;
        public static bool IsListening => listening;
        public static string PipeName => "auroraview-unity-" + System.Diagnostics.Process.GetCurrentProcess().Id;

        static AgentEndpoint()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Disable;
            EditorApplication.quitting += Disable;
        }
        [MenuItem("Window/AuroraView/Enable Agent Endpoint")]
        public static void Enable()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                throw new PlatformNotSupportedException("The local agent endpoint currently requires Windows Editor.");
            lock (Lifecycle)
            {
                if (Enabled) return;
                stopping = false;
                worker = new Thread(Listen) { IsBackground = true, Name = "AuroraView agent transport" };
                worker.Start();
            }
            Debug.Log("AuroraView agent endpoint enabled for this session: " + PipeName + ". Explicit scene methods only; changes support Undo.");
        }
        [MenuItem("Window/AuroraView/Disable Agent Endpoint")]
        public static void Disable()
        {
            Thread previous;
            NamedPipeServerStream connection;
            lock (Lifecycle)
            {
                stopping = true;
                listening = false;
                previous = worker;
                connection = pipe;
                while (Requests.TryDequeue(out var request))
                { request.cancelled = true; request.response = "{\"ok\":false,\"error\":{\"message\":\"Editor endpoint closed\"}}"; request.ready.Set(); }
            }
            connection?.Dispose();
            if (previous != null && !previous.Join(2000))
            { transportError = "Transport shutdown is still pending; disable again before enabling another endpoint."; return; }
            lock (Lifecycle) { worker = null; pipe = null; }
        }
        private static void Tick()
        {
            var error = Interlocked.Exchange(ref transportError, null);
            if (error != null) Debug.LogError("AuroraView agent transport: " + error);
            for (var count = 0; count < 16 && Requests.TryDequeue(out var request); count++)
            { if (!stopping && !request.cancelled) request.response = SceneContracts.Dispatch(request.request); request.ready.Set(); }
        }
        private static void Listen()
        {
            while (!stopping)
            {
                try
                {
                    // PipeOptions.CurrentUserOnly is unavailable in Unity's .NET Standard 2.1.
                    // Apply an explicit Windows ACL for the current user's SID instead.
                    var security = new System.IO.Pipes.PipeSecurity();
                    var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User;
                    security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
                    using (var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536, security))
                    {
                        lock (Lifecycle)
                        { if (stopping) break; pipe = server; listening = true; }
                        server.WaitForConnection();
                        using (var reader = new StreamReader(server, new UTF8Encoding(false), false, 1024, true))
                        using (var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
                        {
                            var json = ReadBoundedLine(reader);
                            if (json == null) continue;
                            var pending = new Pending { request = json };
                            lock (Lifecycle)
                            { if (stopping) break; Requests.Enqueue(pending); }
                            if (pending.ready.Wait(TimeSpan.FromSeconds(10))) writer.WriteLine(pending.response);
                            else { pending.cancelled = true; writer.WriteLine("{\"ok\":false,\"error\":{\"message\":\"Editor main-thread timeout\"}}"); }
                        }
                    }
                    listening = false;
                }
                catch (Exception error) { listening = false; if (!stopping) { transportError = error.Message; Thread.Sleep(100); } }
            }
        }
        private static string ReadBoundedLine(StreamReader reader)
        {
            var text = new StringBuilder();
            while (text.Length <= 65536)
            {
                var character = reader.Read();
                if (character < 0) return null;
                if (character == '\n') return text.ToString();
                text.Append((char)character);
            }
            throw new InvalidDataException("Request exceeds 64 KiB.");
        }
    }
}
