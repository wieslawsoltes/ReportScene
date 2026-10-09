using ReportScene.Validation;
var results = Conformance.Run();
foreach (var result in results) Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {result.Name}{(result.Error is null ? "" : "\n" + result.Error)}");
Console.WriteLine($"{results.Count(r => r.Passed)}/{results.Count} tests passed.");
return results.All(r => r.Passed) ? 0 : 1;
