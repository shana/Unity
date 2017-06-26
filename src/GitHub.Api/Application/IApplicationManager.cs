using System;
using System.Threading;
using System.Threading.Tasks;

namespace GitHub.Unity
{
    interface IApplicationManager : IDisposable
    {
        CancellationToken CancellationToken { get; }
        IEnvironment Environment { get; }
        IPlatform Platform { get; }
        IProcessEnvironment GitEnvironment { get; }
        IProcessManager ProcessManager { get; }
        ISettings SystemSettings { get; }
        ISettings LocalSettings { get; }
        ISettings UserSettings { get; }
        ITaskManager TaskManager { get; }
        CacheManager CacheManager { get; }
        IGitClient GitClient { get; }
        IUsageTracker UsageTracker { get; }
        ITask RestartRepository();
        ITask Run();
    }
}