// [Confluence] A log4net stand-in for the Microsoft.Extensions.Logging CapturingLogger in Tranquillity #302/#327's
// viewer address tests: Confluence's ports log through log4net's ILog, so the tests capture through one.

using System;
using System.Collections.Generic;
using System.Linq;
using log4net;
using log4net.Core;

namespace OpenSim.Framework.Tests;

public enum CapturedLevel { Debug, Information, Warning, Error }

/// <summary>An ILog that records every line it is given, with its level.</summary>
public sealed class CapturingLogger : ILog
{
    public readonly List<(CapturedLevel Level, string Message)> Entries = new();

    public int Warnings { get { lock (Entries) return Entries.Count(e => e.Level == CapturedLevel.Warning); } }
    public List<string> Infos() { lock (Entries) return Entries.Where(e => e.Level == CapturedLevel.Information).Select(e => e.Message).ToList(); }
    public IEnumerable<string> Messages { get { lock (Entries) return Entries.Select(e => e.Message).ToList(); } }

    private void Add(CapturedLevel level, string text)
    {
        lock (Entries)
            Entries.Add((level, text ?? string.Empty));
    }

    public ILogger Logger => null;

    public bool IsDebugEnabled => true;
    public void Debug(object message) => Add(CapturedLevel.Debug, message?.ToString());
    public void Debug(object message, Exception exception) => Add(CapturedLevel.Debug, message?.ToString());
    public void DebugFormat(string format, params object[] args) => Add(CapturedLevel.Debug, string.Format(format, args));
    public void DebugFormat(string format, object arg0) => Add(CapturedLevel.Debug, string.Format(format, arg0));
    public void DebugFormat(string format, object arg0, object arg1) => Add(CapturedLevel.Debug, string.Format(format, arg0, arg1));
    public void DebugFormat(string format, object arg0, object arg1, object arg2) => Add(CapturedLevel.Debug, string.Format(format, arg0, arg1, arg2));
    public void DebugFormat(IFormatProvider provider, string format, params object[] args) => Add(CapturedLevel.Debug, string.Format(provider, format, args));
    public bool IsInfoEnabled => true;
    public void Info(object message) => Add(CapturedLevel.Information, message?.ToString());
    public void Info(object message, Exception exception) => Add(CapturedLevel.Information, message?.ToString());
    public void InfoFormat(string format, params object[] args) => Add(CapturedLevel.Information, string.Format(format, args));
    public void InfoFormat(string format, object arg0) => Add(CapturedLevel.Information, string.Format(format, arg0));
    public void InfoFormat(string format, object arg0, object arg1) => Add(CapturedLevel.Information, string.Format(format, arg0, arg1));
    public void InfoFormat(string format, object arg0, object arg1, object arg2) => Add(CapturedLevel.Information, string.Format(format, arg0, arg1, arg2));
    public void InfoFormat(IFormatProvider provider, string format, params object[] args) => Add(CapturedLevel.Information, string.Format(provider, format, args));
    public bool IsWarnEnabled => true;
    public void Warn(object message) => Add(CapturedLevel.Warning, message?.ToString());
    public void Warn(object message, Exception exception) => Add(CapturedLevel.Warning, message?.ToString());
    public void WarnFormat(string format, params object[] args) => Add(CapturedLevel.Warning, string.Format(format, args));
    public void WarnFormat(string format, object arg0) => Add(CapturedLevel.Warning, string.Format(format, arg0));
    public void WarnFormat(string format, object arg0, object arg1) => Add(CapturedLevel.Warning, string.Format(format, arg0, arg1));
    public void WarnFormat(string format, object arg0, object arg1, object arg2) => Add(CapturedLevel.Warning, string.Format(format, arg0, arg1, arg2));
    public void WarnFormat(IFormatProvider provider, string format, params object[] args) => Add(CapturedLevel.Warning, string.Format(provider, format, args));
    public bool IsErrorEnabled => true;
    public void Error(object message) => Add(CapturedLevel.Error, message?.ToString());
    public void Error(object message, Exception exception) => Add(CapturedLevel.Error, message?.ToString());
    public void ErrorFormat(string format, params object[] args) => Add(CapturedLevel.Error, string.Format(format, args));
    public void ErrorFormat(string format, object arg0) => Add(CapturedLevel.Error, string.Format(format, arg0));
    public void ErrorFormat(string format, object arg0, object arg1) => Add(CapturedLevel.Error, string.Format(format, arg0, arg1));
    public void ErrorFormat(string format, object arg0, object arg1, object arg2) => Add(CapturedLevel.Error, string.Format(format, arg0, arg1, arg2));
    public void ErrorFormat(IFormatProvider provider, string format, params object[] args) => Add(CapturedLevel.Error, string.Format(provider, format, args));
    public bool IsFatalEnabled => true;
    public void Fatal(object message) => Add(CapturedLevel.Error, message?.ToString());
    public void Fatal(object message, Exception exception) => Add(CapturedLevel.Error, message?.ToString());
    public void FatalFormat(string format, params object[] args) => Add(CapturedLevel.Error, string.Format(format, args));
    public void FatalFormat(string format, object arg0) => Add(CapturedLevel.Error, string.Format(format, arg0));
    public void FatalFormat(string format, object arg0, object arg1) => Add(CapturedLevel.Error, string.Format(format, arg0, arg1));
    public void FatalFormat(string format, object arg0, object arg1, object arg2) => Add(CapturedLevel.Error, string.Format(format, arg0, arg1, arg2));
    public void FatalFormat(IFormatProvider provider, string format, params object[] args) => Add(CapturedLevel.Error, string.Format(provider, format, args));
}
