using JavaScriptEngineSwitcher.Core;
using SSR.Net.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace SSR.Net.Services
{
    public class JavaScriptEnginePool
    {
        private List<string> _activeScripts;
        private List<string> _stagedScripts = new List<string>();
        private int _minEngines = 5;
        private int _maxEngines = 25;
        private int _maxUsages = 100;
        private int _garbageCollectionInterval = 20;
        private int _standbyEngineTargetCount = 3;
        private List<JavaScriptEngine> _engines = new List<JavaScriptEngine>();
        private object _lock = new object();
        private int _bundleNumber = 0;
        private JsEngineSwitcher _jsEngineSwitcher;
        public bool IsStarted { get; private set; }

        public JavaScriptEnginePool AddScript(string script)
        {
            lock (_lock)
            {
                _stagedScripts.Add(script);
            }
            return this;
        }

        public string EvaluateJs(string js,
                                 int timeoutMs = 200,
                                 bool returnNullInsteadOfException = false)
        {
            var engine = GetEngine(timeoutMs);
            if (!(engine is null))
                return engine.EvaluateAndRelease(js);
            if (returnNullInsteadOfException)
                return null;
            throw new Exception($"Could not get engine withing {timeoutMs}ms");
        }

        private JavaScriptEngine GetEngine(int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var sw2 = Stopwatch.StartNew();
                lock (_lock)
                {
                    RemoveDepletedEngines();
                    RefillToMinEngines();
                    EnsureEnoughStandbyEngines();
                    var engine = TryToFindReadyEngine();
                    if (engine != null)
                    {
                        Console.WriteLine($"Loop took {sw2.ElapsedMilliseconds} ({sw2.ElapsedTicks} ticks)");
                        var result = engine.Lease();
                        EnsureEnoughStandbyEngines();//We ensure that there are enough standby engines after the lease
                        return result;
                    }
                }
                Console.WriteLine($"Loop took {sw2.ElapsedMilliseconds} ({sw2.ElapsedTicks} ticks)");
                Thread.Sleep(5);
            }
            Console.WriteLine("Leasing took " + sw.ElapsedMilliseconds + "ms, but no engine was found");
            return null;
        }

        private void EnsureEnoughStandbyEngines()
        {
            var neededStandbyEngines = _standbyEngineTargetCount - _engines.Count(e => !e.IsDepleted && !e.IsLeased);
            if (neededStandbyEngines <= 0)
                return;
            var maxNewStandbyEngines = _maxEngines - _engines.Count();
            var toInstantiate = Math.Min(neededStandbyEngines, maxNewStandbyEngines);
            for (int i = 0; i < toInstantiate; ++i)
                AddNewJsEngine();
        }

        private JavaScriptEngine TryToFindReadyEngine() =>
            GetEnginesSortedByUsageThenAge()
                .FirstOrDefault(e => e.IsReady);

        private JavaScriptEngine[] GetEnginesSortedByUsageThenAge() =>
            _engines
                .OrderByDescending(e => e.UsageCount)
                .ThenBy(e => e.Instantiated)
                .ToArray();

        private void RefillToMinEngines()
        {
            while (_engines.Count() < _minEngines)
                AddNewJsEngine();
        }

        private void RemoveDepletedEngines()
        {
            var toRemove = _engines.Where(e => e.IsDepleted).ToList();
            toRemove.ForEach(e =>
            {
                _engines.Remove(e);
                e.Dispose();
            });
        }

        public JavaScriptEnginePool Start()
        {
            lock (_lock)
            {
                _engines.Where(e => !e.IsLeased).ToList().ForEach(e => { _engines.Remove(e); e.Dispose(); });
                _engines.ForEach(e => e.SetDepleted());
                _activeScripts = _stagedScripts;
                _stagedScripts = new List<string>();
                _bundleNumber++;
                for (int i = 0; i < _minEngines; ++i)
                    AddNewJsEngine();
                IsStarted = true;
            }
            return this;
        }

        public JavaScriptEnginePool WithMaxEngineCount(int maxEngines)
        {
            _maxEngines = maxEngines;
            return this;
        }

        public JavaScriptEnginePool WithMinEngineCount(int minEngines)
        {
            _minEngines = minEngines;
            return this;
        }

        public JavaScriptEnginePool WithMaxUsagesCount(int maxUsages)
        {
            _maxUsages = maxUsages;
            return this;
        }

        private void AddNewJsEngine() =>
            _engines.Add(new JavaScriptEngine(() =>
            {
                var jsEngine = _jsEngineSwitcher.CreateDefaultEngine();
                _activeScripts.ForEach(s => jsEngine.Execute(s));
                return jsEngine;
            }, _maxUsages, _garbageCollectionInterval, _bundleNumber));

        public string GetStats()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Engines ({_engines.Count})");
            lock (_lock)
            {
                foreach (var engine in _engines)
                    sb.AppendLine($"{engine.GetState()}, {engine.Instantiated.Second - DateTime.UtcNow.Second}, {engine.UsageCount}, {engine.BundleNumber}");
            }
            return sb.ToString();
        }

        public JavaScriptEnginePool(IJsEngineFactory jsEngineFactory)
        {
            _jsEngineSwitcher = new JsEngineSwitcher();
            _jsEngineSwitcher.EngineFactories.Add(jsEngineFactory);
            _jsEngineSwitcher.DefaultEngineName = jsEngineFactory.EngineName;
        }
    }
}
