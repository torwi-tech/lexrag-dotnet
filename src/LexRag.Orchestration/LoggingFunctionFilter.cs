using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace LexRag.Orchestration;

public sealed class LoggingFunctionFilter(ILogger<LoggingFunctionFilter> log) : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        var sw = Stopwatch.StartNew();
        await next(context);
        log.LogInformation("SK function {Plugin}.{Function} ran in {ElapsedMs}ms",
            context.Function.PluginName, context.Function.Name, sw.ElapsedMilliseconds);
    }
}
