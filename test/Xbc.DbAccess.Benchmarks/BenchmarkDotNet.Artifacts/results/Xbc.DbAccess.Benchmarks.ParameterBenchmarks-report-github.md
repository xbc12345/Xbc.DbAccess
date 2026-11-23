```

BenchmarkDotNet v0.14.0, Ubuntu 24.04 LTS (Noble Numbat)
INTEL XEON PLATINUM 8582C, 3 CPU, 3 logical and 3 physical cores
.NET SDK 8.0.134
  [Host]   : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  ShortRun : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                        | Mean         | Error        | StdDev     | Gen0   | Gen1   | Allocated |
|------------------------------ |-------------:|-------------:|-----------:|-------:|-------:|----------:|
| &#39;Xbc 参数构建x1&#39;                  |    446.17 ns |    687.69 ns |  37.695 ns | 0.0982 |      - |     824 B |
| &#39;Xbc 参数构建x10&#39;                 |  2,271.65 ns |  1,497.48 ns |  82.082 ns | 0.5264 | 0.0038 |    4416 B |
| &#39;Xbc 参数构建x50&#39;                 | 10,577.37 ns | 16,166.55 ns | 886.143 ns | 2.5024 | 0.1068 |   20948 B |
| &#39;Dapper DynamicParametersx1&#39;  |     72.77 ns |     70.08 ns |   3.841 ns | 0.0257 |      - |     216 B |
| &#39;Dapper DynamicParametersx10&#39; |     71.42 ns |     64.19 ns |   3.519 ns | 0.0257 |      - |     216 B |
| &#39;Dapper DynamicParametersx50&#39; |     70.28 ns |    136.94 ns |   7.506 ns | 0.0257 |      - |     216 B |
| 手写参数构建x1                      |     33.82 ns |     35.22 ns |   1.931 ns | 0.0124 |      - |     104 B |
| 手写参数构建x10                     |    199.97 ns |    141.83 ns |   7.774 ns | 0.0985 |      - |     824 B |
| 手写参数构建x50                     |  1,071.49 ns |    831.73 ns |  45.590 ns | 0.4807 | 0.0057 |    4024 B |
