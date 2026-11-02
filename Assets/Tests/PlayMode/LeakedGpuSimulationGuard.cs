using NUnit.Framework;
using NUnit.Framework.Interfaces;
using TowerDefense.Runtime;

[assembly: TowerDefense.Tests.DisposeLeakedGpuSimulations]

namespace TowerDefense.Tests
{
    /// <summary>
    /// A test that fails an assertion before its own Dispose call would leave its GPU
    /// buffers alive. Those leaks accumulate across the suite and starve later tests
    /// (d3d12 fence timeouts), so every test releases anything it left behind.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Assembly)]
    public sealed class DisposeLeakedGpuSimulationsAttribute : System.Attribute, ITestAction
    {
        public ActionTargets Targets => ActionTargets.Test;

        public void BeforeTest(ITest test)
        {
        }

        public void AfterTest(ITest test)
        {
            var leaked = GpuHordeSimulation.DisposeLeakedSimulations();
            if (leaked > 0)
            {
                UnityEngine.Debug.Log($"Released {leaked} leaked GPU simulation(s) after {test.Name}.");
            }
        }
    }
}
