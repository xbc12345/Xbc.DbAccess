```

BenchmarkDotNet v0.14.0, Ubuntu 24.04 LTS (Noble Numbat)
INTEL XEON PLATINUM 8582C, 3 CPU, 3 logical and 3 physical cores
.NET SDK 8.0.134
  [Host]   : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  ShortRun : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method         | Mode     | Mean     | Error     | StdDev    | Gen0   | Allocated |
|--------------- |--------- |---------:|----------:|----------:|-------:|----------:|
| **执行管道（含连接管理+诊断）** | **activity** | **2.639 μs** | **2.8277 μs** | **0.1550 μs** | **0.1945** |    **1656 B** |
| **执行管道（含连接管理+诊断）** | **full**     | **2.550 μs** | **0.9864 μs** | **0.0541 μs** | **0.1945** |    **1656 B** |
| **执行管道（含连接管理+诊断）** | **metrics**  | **1.798 μs** | **1.3554 μs** | **0.0743 μs** | **0.0954** |     **800 B** |
| **执行管道（含连接管理+诊断）** | **off**      | **1.775 μs** | **1.4093 μs** | **0.0772 μs** | **0.0954** |     **800 B** |
