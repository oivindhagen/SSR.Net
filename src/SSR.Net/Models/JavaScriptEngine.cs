using JavaScriptEngineSwitcher.Core;
using System;
using System.Threading.Tasks;

namespace SSR.Net.Models
{
    public class JavaScriptEngine : IDisposable
    {
        private IJsEngine _engine;
        private JavaScriptEngineState _state;
        private readonly int _maxUsages;
        private readonly int _garbageCollectionInterval;
        public int BundleNumber { get; }
        public int UsageCount { get; private set; }
        private bool _depleted;
        private Task _initializer;
        public DateTime InstantiationTime { get; private set; }
        public DateTime InitializedTime { get; private set; }

        public JavaScriptEngine(Func<IJsEngine> createEngine, int maxUsages, int garbageCollectionInterval, int bundleNumber)
        {
            _maxUsages = maxUsages;
            BundleNumber = bundleNumber;
            InstantiationTime = DateTime.UtcNow;
            _garbageCollectionInterval = garbageCollectionInterval;
            _state = JavaScriptEngineState.Uninitialized;
            _initializer = Task.Run(() =>
            {
                _engine = createEngine();
                _state = JavaScriptEngineState.Ready;
                InitializedTime = DateTime.UtcNow;
            });
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
            //This engine instance might be depleted if the pool was restarted, but the engine should finish
            //its render to avoid 500 errors on the web
            if (_state != JavaScriptEngineState.Leased)
                throw new InvalidOperationException($"Cannot evaluate script on engine in state {GetState()}");
            string result;
            try
            {
                result = _engine.Evaluate<string>(script);
            }
            finally
            {

                if (UsageCount >= _maxUsages)
                    _depleted = true;
                else if (UsageCount % _garbageCollectionInterval == 0)
                {
                    _state = JavaScriptEngineState.RequiresGarbageCollection;
                    Task.Run(() =>
                    {
                        RunGarbageCollection();
                        _state = JavaScriptEngineState.Ready;
                    });
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
            _initializer.Wait();
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
