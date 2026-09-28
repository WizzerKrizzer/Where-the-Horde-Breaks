using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace TowerDefense.Tests
{
    [DefaultExecutionOrder(-32000)]
    public sealed class CrowdGpuFrameTimer : MonoBehaviour
    {
        private CustomSampler sampler;
        private Recorder recorder;
        private CommandBuffer command;
        private bool open;
        public float Milliseconds => recorder != null && recorder.gpuSampleBlockCount > 0 ? recorder.gpuElapsedNanoseconds / 1000000f : 0;
        private void Awake()
        {
            sampler = CustomSampler.Create("CrowdSimulationAndCamera", true);
            recorder = sampler.GetRecorder();
            recorder.enabled = true;
            command = new CommandBuffer { name = "Crowd GPU timestamps" };
        }
        private void Update()
        {
            End();
            command.Clear(); command.BeginSample(sampler);
            Graphics.ExecuteCommandBuffer(command);
            open = true;
        }
        public void End()
        {
            if (!open) return;
            command.Clear(); command.EndSample(sampler);
            Graphics.ExecuteCommandBuffer(command);
            open = false;
        }
        private void OnDestroy()
        {
            End();
            if (recorder != null) recorder.enabled = false;
            command?.Dispose();
        }
    }
}
