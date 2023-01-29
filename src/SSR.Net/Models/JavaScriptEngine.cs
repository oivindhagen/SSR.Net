using JavaScriptEngineSwitcher.Core;
using System;
using System.Threading.Tasks;

namespace SSR.Net.Models
{
    public class JavaScriptEngine : IDisposable
    {
        private IJsEngine _engine;
        private readonly int _maxUsages;
        public readonly int BundleNumber;
        private readonly int _garbageCollectionInterval;
        public int UsageCount { get; private set; } = 0;
        private JavaScriptEngineState _state;
        private bool _depleted = false;
        private Task _instantiator;
        public DateTime Instantiated { get; private set; }

        public JavaScriptEngine(Func<IJsEngine> createFunction, int maxUsages, int garbageCollectionInterval, int bundleNumber)
        {
            _maxUsages = maxUsages;
            BundleNumber = bundleNumber;
            Instantiated = DateTime.UtcNow;
            _garbageCollectionInterval = garbageCollectionInterval;
            _state = JavaScriptEngineState.Uninitialized;
            _instantiator = new Task(() =>
            {
                _engine = createFunction();
                _state = JavaScriptEngineState.Ready;
            });
            _instantiator.Start();
        }


        public JavaScriptEngineState GetState() => _depleted ? JavaScriptEngineState.Depleted : _state;

        public bool IsLeased => GetState() == JavaScriptEngineState.Leased;
        public bool IsReady => GetState() == JavaScriptEngineState.Ready;
        public bool IsDepleted => GetState() == JavaScriptEngineState.Depleted;

        public JavaScriptEngine Lease()
        {
            if (!IsReady)
                throw new InvalidOperationException($"Cannot lease engine when the engine is in state {_state}");
            _state = JavaScriptEngineState.Leased;
            UsageCount++;
            return this;
        }

        public string EvaluateAndRelease(string script)
        {
            if (!IsLeased)
                throw new InvalidOperationException($"Cannot evaluate script on engine in state {script}");
            string result;
            try
            {
                result = _engine.Evaluate<string>(script);
                System.Threading.Thread.Sleep(600);
            }
            finally
            {

                if (UsageCount >= _maxUsages)
                    _depleted = true;
                else if (UsageCount % _garbageCollectionInterval == 0 && UsageCount > 0)
                {
                    _state = JavaScriptEngineState.RequiresGarbageCollection;
                    Task t = new Task(() =>
                    {
                        RunGarbageCollection();
                        _state = JavaScriptEngineState.Ready;
                    });
                    t.Start();
                }
                else
                {
                    _state = JavaScriptEngineState.Ready;
                }
            }
            return result;
        }

        public void Dispose()
        {
            _instantiator.Wait();
            _engine.Dispose();
        }

        public void SetDepleted() =>
            _depleted = true;

        public void RunGarbageCollection()
        {
            if (_engine.SupportsGarbageCollection)
            {
                _engine.CollectGarbage();
                _state = JavaScriptEngineState.Ready;
            }
        }
    }
}
