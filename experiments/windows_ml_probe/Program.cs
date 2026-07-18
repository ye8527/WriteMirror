using Microsoft.Windows.AI.MachineLearning;

ExecutionProviderCatalog catalog = ExecutionProviderCatalog.GetDefault();
ExecutionProvider[] providers = catalog.FindAllProviders();
bool ensure = args.Contains("--ensure", StringComparer.OrdinalIgnoreCase);

foreach (ExecutionProvider provider in providers)
{
    Console.WriteLine($"{provider.Name}\t{provider.ReadyState}");

    if (!ensure || provider.Name != "QNNExecutionProvider" ||
        provider.ReadyState == ExecutionProviderReadyState.Ready)
    {
        continue;
    }

    Console.WriteLine("QNN execution provider preparation started.");
    var operation = provider.EnsureReadyAsync();
    operation.Progress = (_, progress) => Console.WriteLine($"Progress\t{progress:F0}%");
    ExecutionProviderReadyResult result = await operation;

    Console.WriteLine($"Result\t{result.Status}");
    Console.WriteLine($"ExtendedError\t0x{result.ExtendedError.HResult:X8}");
    Console.WriteLine($"DiagnosticText\t{result.DiagnosticText}");
    Console.WriteLine($"ReadyStateAfter\t{provider.ReadyState}");
}
