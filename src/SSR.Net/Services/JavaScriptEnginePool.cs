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
        private int _StandbyEngineTargetCount = 3;
        private List<JavaScriptEngine> _engines = new List<JavaScriptEngine>();//Active engines in use
        private List<JavaScriptEngine> _standbyEngines = new List<JavaScriptEngine>();//Engines on standby
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

        public string EvaluateJs(string js, int timeoutMs = 200)
        {
            var engine = GetEngine(timeoutMs);
            if (engine is null) throw new Exception($"Could not get engine withing {timeoutMs}ms");
            return engine.EvaluateAndRelease(js);
        }

        private JavaScriptEngine GetEngine(int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            lock (_lock)
            {
                RemoveDepletedEngines();
                TryToRefillToMinEnginesWithStandbyEngines();
                var idealUsageGap = CalculateIdealUsageGapBetweenEngines();
                JavaScriptEngine engine = null;
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    engine = TryToFindReadyEngineAmongRunningEngines(idealUsageGap);
                    if (engine is null && _engines.Count() < _maxEngines)
                        engine = TryToActivateStandbyEngine();
                    if (engine != null)
                    {
                        Console.WriteLine("Leasing took " + sw.ElapsedMilliseconds + "ms");
                        return engine.Lease();
                    }
                    Console.WriteLine("Leasing took " + sw.ElapsedMilliseconds + "ms, but no engine was found");
                    Thread.Sleep(5);
                }
            }
            Console.WriteLine("Leasing took " + sw.ElapsedMilliseconds + "ms, but no engine was found");
            return null;
        }

        private int CalculateIdealUsageGapBetweenEngines() =>
            Math.Max(_maxUsages / Math.Max(_engines.Count, 1), 1);

        private JavaScriptEngine TryToFindReadyEngineAmongRunningEngines(int idealUsageGap)
        {
            JavaScriptEngine candidate = null;
            var previousUsage = _maxUsages;
            foreach (var engine in GetEnginesSortedByUsageThenAge())
            {
                //We try to space out the engine usage count to even out the GC and initialization
                if (engine.UsageCount + idealUsageGap < previousUsage)
                {
                    if (engine.IsReady)
                        return engine;
                }
                //We use the first ready engine we find as a fallback
                else if (candidate is null && engine.IsReady)
                    candidate = engine;
                previousUsage = engine.UsageCount;
            }

            return candidate;
        }

        private JavaScriptEngine[] GetEnginesSortedByUsageThenAge() => 
            _engines.OrderByDescending(e => e.UsageCount).ThenBy(e => e.Instantiated).ToArray();

        private void TryToRefillToMinEnginesWithStandbyEngines()
        {
            while (_engines.Count() < _minEngines)
                if (TryToActivateStandbyEngine() is null) break;
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

        private JavaScriptEngine TryToActivateStandbyEngine()
        {
            JavaScriptEngine candidate = _standbyEngines.First(e => e.IsReady);
            if (candidate != null)
            {
                _standbyEngines.Remove(candidate);
                _engines.Add(candidate);
                _standbyEngines.Add(CreateJsEngine());//This engine will initialize on a background thread
            }
            return candidate;
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
                _standbyEngines.ForEach(e => e.Dispose());
                _standbyEngines.Clear();
                for (int i = 0; i < _minEngines; ++i)
                    _engines.Add(CreateJsEngine());
                for (int i = 0; i < _StandbyEngineTargetCount; ++i)
                    _standbyEngines.Add(CreateJsEngine());
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

        private JavaScriptEngine CreateJsEngine() =>
            new JavaScriptEngine(() =>
            {
                var jsEngine = _jsEngineSwitcher.CreateDefaultEngine();
                _activeScripts.ForEach(s => jsEngine.Execute(s));
                return jsEngine;
            }, _maxUsages, _garbageCollectionInterval, _bundleNumber);

        public string GetStats()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Engines:");
            lock (_lock)
            {
                foreach (var engine in _engines)
                    sb.AppendLine($"{engine.GetState()}, {engine.Instantiated.Second - DateTime.UtcNow.Second}, {engine.UsageCount}, {engine.BundleNumber}");
                sb.AppendLine("Standby engines");
                foreach (var engine in _standbyEngines)
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
