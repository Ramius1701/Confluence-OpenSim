/*
 * Confluence-only file, not part of the Legion Jolt sources.
 *
 * The Jolt module comes from JohnLegionH's Tranquillity branch (lane/jolt), where every type logs through
 * Microsoft.Extensions.Logging via OpenSim.Framework.LoggerProvider. Confluence logs through log4net and has no
 * LoggerProvider, so this class, in the module's own namespace (which takes precedence over the
 * `using OpenSim.Framework;` in the Legion files), hands those calls to log4net. The Legion files then compile
 * unchanged, which keeps later syncs from lane/jolt to plain three-way merges.
 */

using System;
using log4net;
using Microsoft.Extensions.Logging;

namespace OpenSim.Region.PhysicsModules.Jolt
{
    internal static class LoggerProvider
    {
        public static ILogger CreateLogger(Type type) => new Log4NetLogger(LogManager.GetLogger(type));

        public static ILogger CreateLogger(string categoryName) =>
            new Log4NetLogger(LogManager.GetLogger(typeof(LoggerProvider).Assembly, categoryName));
    }

    internal sealed class Log4NetLogger : ILogger
    {
        private readonly ILog m_log;

        public Log4NetLogger(ILog log) => m_log = log;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace or LogLevel.Debug => m_log.IsDebugEnabled,
            LogLevel.Information => m_log.IsInfoEnabled,
            LogLevel.Warning => m_log.IsWarnEnabled,
            LogLevel.Error => m_log.IsErrorEnabled,
            LogLevel.Critical => m_log.IsFatalEnabled,
            _ => false,
        };

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                                Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            string message = formatter != null ? formatter(state, exception) : state?.ToString();
            switch (logLevel)
            {
                case LogLevel.Trace:
                case LogLevel.Debug: m_log.Debug(message, exception); break;
                case LogLevel.Information: m_log.Info(message, exception); break;
                case LogLevel.Warning: m_log.Warn(message, exception); break;
                case LogLevel.Error: m_log.Error(message, exception); break;
                case LogLevel.Critical: m_log.Fatal(message, exception); break;
            }
        }
    }
}
