```

BenchmarkDotNet v0.14.0, Ubuntu 24.04 LTS (Noble Numbat)
INTEL XEON PLATINUM 8582C, 3 CPU, 3 logical and 3 physical cores
.NET SDK 8.0.134
  [Host]   : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  ShortRun : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method          | Mean      | Error      | StdDev    | Gen0   | Allocated |
|---------------- |----------:|-----------:|----------:|-------:|----------:|
| &#39;Xbc 查询100行&#39;    |  4.870 ms |  5.0018 ms | 0.2742 ms | 7.8125 | 124.15 KB |
| &#39;Dapper 查询100行&#39; |  4.987 ms |  5.8842 ms | 0.3225 ms | 7.8125 | 126.84 KB |
| &#39;原生 查询100行&#39;     |  4.363 ms | 10.9486 ms | 0.6001 ms | 7.8125 | 116.23 KB |
| &#39;Xbc 插入单行&#39;      |  9.240 ms |  8.9141 ms | 0.4886 ms |      - | 101.08 KB |
| &#39;Dapper 插入单行&#39;   | 11.379 ms | 54.9028 ms | 3.0094 ms |      - | 100.48 KB |
| &#39;原生 插入单行&#39;       |  8.606 ms |  0.8825 ms | 0.0484 ms |      - | 100.37 KB |
