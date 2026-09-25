using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(PoWatch.Benchmarks.RollupBenchmarks).Assembly).Run(args);
