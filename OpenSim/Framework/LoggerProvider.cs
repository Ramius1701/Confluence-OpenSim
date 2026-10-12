/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using log4net;
using Microsoft.Extensions.Logging;

namespace OpenSim.Framework
{
    /// <summary>
    /// Microsoft.Extensions.Logging loggers for code ported from Tranquillity, which logs through ILogger and this class
    /// (Tranquillity's OpenSim.Framework.LoggerProvider; same members). Confluence logs through log4net, so by default
    /// each line goes to log4net under the logger's category. Setting <see cref="LoggerFactory"/> sends lines to that
    /// factory instead, resolved when each line is logged (the Phlox and Jolt tests capture log lines that way).
    /// </summary>
    public static class LoggerProvider
    {
        /// <summary>When set, log lines go to this factory instead of log4net. Null (log4net) by default.</summary>
        public static ILoggerFactory LoggerFactory { get; set; }

        public static ILogger CreateLogger<T>() => CreateLogger(typeof(T));

        public static ILogger CreateLogger(Type type) =>
            new Log4NetBridgeLogger(LogManager.GetLogger(type), type.FullName);

        public static ILogger CreateLogger(string categoryName) =>
            new Log4NetBridgeLogger(LogManager.GetLogger(typeof(LoggerProvider).Assembly, categoryName), categoryName);

        private sealed class Log4NetBridgeLogger : ILogger
        {
            private readonly ILog m_log;
            private readonly string m_category;

            public Log4NetBridgeLogger(ILog log, string category)
            {
                m_log = log;
                m_category = category;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel)
            {
                ILoggerFactory factory = LoggerFactory;
                if (factory != null)
                    return factory.CreateLogger(m_category).IsEnabled(logLevel);
                return logLevel switch
                {
                    LogLevel.Trace or LogLevel.Debug => m_log.IsDebugEnabled,
                    LogLevel.Information => m_log.IsInfoEnabled,
                    LogLevel.Warning => m_log.IsWarnEnabled,
                    LogLevel.Error => m_log.IsErrorEnabled,
                    LogLevel.Critical => m_log.IsFatalEnabled,
                    _ => false,
                };
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                                    Func<TState, Exception, string> formatter)
            {
                ILoggerFactory factory = LoggerFactory;
                if (factory != null)
                {
                    factory.CreateLogger(m_category).Log(logLevel, eventId, state, exception, formatter);
                    return;
                }
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
}
