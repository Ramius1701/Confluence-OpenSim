/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

// [Confluence] From OpenSim-Tranquillity #323, adapted to log4net (the logger is an Action pair the tests replace).

using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using log4net;

namespace OpenSim.Framework
{
    /// <summary>
    /// Asks Windows for a finer system timer for the life of the process ([Startup] TimerResolutionMs).
    /// </summary>
    /// <remarks>
    /// A sleeping thread on Windows wakes on a timer tick, 15.625 ms by default, so <c>Thread.Sleep(n)</c> returns on the
    /// next tick after n ms and the scene loop's frames come out one tick longer or shorter than asked for. A process can
    /// ask for a finer tick with <c>timeBeginPeriod</c> (see <see cref="Util.TimeBeginPeriod"/>) and must undo the request
    /// with <c>timeEndPeriod</c> and the same value. This class is that pair, made once at start-up and undone once at
    /// shutdown. It does nothing off Windows, and nothing when the setting is 0, which is the default.
    /// </remarks>
    public sealed class TimerResolution : IDisposable
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>1 ms is the finest period Windows documents; above 15 asks for no improvement on the 15.625 ms tick.</summary>
        public const int MinMilliseconds = 1;
        public const int MaxMilliseconds = 15;

        private readonly uint m_period;
        private readonly Action<uint> m_end;
        private int m_ended;

        /// <summary>The period asked for, in milliseconds.</summary>
        public int Milliseconds => (int)m_period;

        private TimerResolution(uint period, Action<uint> end)
        {
            m_period = period;
            m_end = end;
        }

        /// <summary>
        /// Requests a system timer period of <paramref name="milliseconds"/>. Returns null, and does not touch the
        /// timer, when the value is 0 (off) or out of range, or when the platform is not Windows. The begin and end
        /// calls, the platform test, the measuring of <c>Sleep(1)</c> and the log lines can be replaced by tests.
        /// </summary>
        public static TimerResolution Apply(int milliseconds, Action<uint> begin = null, Action<uint> end = null,
            bool? isWindows = null, Func<double> measureSleepOneMs = null, Action<string> info = null, Action<string> warn = null)
        {
            info ??= m_log.Info;
            warn ??= m_log.Warn;

            if (milliseconds == 0)
                return null;

            if (milliseconds < MinMilliseconds || milliseconds > MaxMilliseconds)
            {
                warn($"[TIMER]: TimerResolutionMs = {milliseconds} is outside {MinMilliseconds} to {MaxMilliseconds}; the timer is left as it is");
                return null;
            }

            if (!(isWindows ?? RuntimeInformation.IsOSPlatform(OSPlatform.Windows)))
            {
                info($"[TIMER]: TimerResolutionMs = {milliseconds} is ignored: it applies to Windows only");
                return null;
            }

            begin ??= Util.TimeBeginPeriod;
            end ??= Util.TimeEndPeriod;
            measureSleepOneMs ??= () => MeasureSleepOneMs();

            begin((uint)milliseconds);
            try
            {
                double sleepMs = measureSleepOneMs();
                info(string.Format(CultureInfo.InvariantCulture,
                    "[TIMER]: Windows timer resolution {0} ms requested (TimerResolutionMs); Thread.Sleep(1) now takes {1:0.00} ms",
                    milliseconds, sleepMs));
            }
            catch
            {
                end((uint)milliseconds);   // keep the begin/end pair even if the measurement fails
                throw;
            }

            return new TimerResolution((uint)milliseconds, end);
        }

        /// <summary>
        /// As <see cref="Apply(int, Action{uint}, Action{uint}, bool?, Func{double}, Action{string}, Action{string})"/>,
        /// for the raw text of the setting. A missing or empty value is off; text that is not a whole number is refused.
        /// </summary>
        public static TimerResolution Apply(string setting, Action<uint> begin = null, Action<uint> end = null,
            bool? isWindows = null, Func<double> measureSleepOneMs = null, Action<string> info = null, Action<string> warn = null)
        {
            if (string.IsNullOrWhiteSpace(setting))
                return null;

            if (!int.TryParse(setting.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int milliseconds))
            {
                (warn ?? m_log.Warn)($"[TIMER]: TimerResolutionMs = \"{setting}\" is not a whole number of milliseconds; the timer is left as it is");
                return null;
            }

            return Apply(milliseconds, begin, end, isWindows, measureSleepOneMs, info, warn);
        }

        /// <summary>Ends the request. The first call ends it; later calls do nothing.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_ended, 1) == 0)
                m_end(m_period);
        }

        /// <summary>Mean wall time of <c>Thread.Sleep(1)</c> over <paramref name="calls"/> calls, in milliseconds.</summary>
        public static double MeasureSleepOneMs(int calls = 20)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < calls; i++)
                Thread.Sleep(1);
            return Stopwatch.GetElapsedTime(start).TotalMilliseconds / calls;
        }
    }
}
