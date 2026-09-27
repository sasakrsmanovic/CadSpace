var tests=new List<(string Name,Action Run)>();
PerformanceRegression.Register((name,run)=>tests.Add((name,run)));
InteractionRegression.Register((name,run)=>tests.Add((name,run)));
RecoveryRegression.Register((name,run)=>tests.Add((name,run)));
LinetypeRegression.Register((name,run)=>tests.Add((name,run)));
var failures=0;
foreach(var (name,run) in tests)
    try {run();Console.WriteLine($"PASS {name}");}
    catch(Exception error){failures++;Console.Error.WriteLine($"FAIL {name}: {error}");}
Console.WriteLine($"{tests.Count-failures}/{tests.Count} performance/modeling regressions passed.");
return failures==0?0:1;
