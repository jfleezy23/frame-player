using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;
using Avalonia.Skia;
using FramePlayer.Avalonia;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class AvaloniaHeadlessFixture : IDisposable
    {
        private static readonly TimeSpan DefaultDispatchTimeout = TimeSpan.FromSeconds(10d);
        private readonly string? _previousSkipRuntimeBootstrap;
        private readonly HeadlessUnitTestSession _session;
        private bool _dispatchTimedOut;

        public AvaloniaHeadlessFixture()
        {
            _previousSkipRuntimeBootstrap = Environment.GetEnvironmentVariable("FRAMEPLAYER_AVALONIA_SKIP_RUNTIME_BOOTSTRAP");
            Environment.SetEnvironmentVariable("FRAMEPLAYER_AVALONIA_SKIP_RUNTIME_BOOTSTRAP", "1");
            _session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp));
        }

        public void Run(Action action)
        {
            Run(action, DefaultDispatchTimeout, "Avalonia headless dispatch");
        }

        public Task RunAsync(Func<Task> action)
        {
            return RunAsync(action, DefaultDispatchTimeout, "Avalonia headless dispatch");
        }

        public async Task RunAsync(Func<Task> action, TimeSpan timeout, string operationName)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be greater than zero.");
            }

            using var cancellation = new CancellationTokenSource();
            var dispatchTask = _session.Dispatch(
                async () =>
                {
                    await action();
                    return true;
                },
                cancellation.Token);
            var timeoutTask = Task.Delay(timeout);
            var completedTask = await Task.WhenAny(dispatchTask, timeoutTask).ConfigureAwait(false);
            if (!ReferenceEquals(completedTask, dispatchTask))
            {
                cancellation.Cancel();
                _dispatchTimedOut = true;
                throw CreateTimeoutException(operationName, timeout);
            }

            try
            {
                await dispatchTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (cancellation.IsCancellationRequested)
            {
                _dispatchTimedOut = true;
                throw CreateTimeoutException(operationName, timeout, ex);
            }
        }

        public void Run(Action action, TimeSpan timeout, string operationName)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be greater than zero.");
            }

            using var cancellation = new CancellationTokenSource();
            var dispatchTask = _session.Dispatch(action, cancellation.Token);
            var dispatchCompleted = false;
            try
            {
                var timeoutTask = Task.Delay(timeout);
                var completedTask = Task.WhenAny(dispatchTask, timeoutTask).GetAwaiter().GetResult();
                dispatchCompleted = ReferenceEquals(completedTask, dispatchTask);
                if (!dispatchCompleted)
                {
                    cancellation.Cancel();
                    _dispatchTimedOut = true;
                    throw CreateTimeoutException(operationName, timeout);
                }

                dispatchTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException ex) when (cancellation.IsCancellationRequested && !dispatchCompleted)
            {
                _dispatchTimedOut = true;
                throw CreateTimeoutException(operationName, timeout, ex);
            }
        }

        private static TimeoutException CreateTimeoutException(
            string operationName,
            TimeSpan timeout,
            Exception? innerException = null)
        {
            return new TimeoutException(
                string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0} did not complete within {1:N1} seconds.",
                    string.IsNullOrWhiteSpace(operationName) ? "Avalonia headless dispatch" : operationName,
                    timeout.TotalSeconds),
                innerException);
        }

        public void Dispose()
        {
            try
            {
                _session.Dispose();
            }
            catch (Exception) when (_dispatchTimedOut)
            {
                // The timeout failure already identifies the blocked dispatch; suppress follow-on cleanup noise.
            }
            finally
            {
                Environment.SetEnvironmentVariable("FRAMEPLAYER_AVALONIA_SKIP_RUNTIME_BOOTSTRAP", _previousSkipRuntimeBootstrap);
            }
        }
    }

    public static class HeadlessTestApp
    {
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions
                {
                    UseHeadlessDrawing = false,
                    FrameBufferFormat = PixelFormat.Bgra8888
                })
                .WithInterFont()
                .LogToTrace();
        }
    }
}
