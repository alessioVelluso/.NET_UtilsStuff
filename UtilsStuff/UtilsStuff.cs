using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;

namespace UtilsStuff
{
    public static class UtilsStuff
    {
        #region EXC

        private static Func<string, string>? EXC_LocalizerFunction { get; set; } = null;
        public static void SetExcLocalizerFunction(Func<string, string> callbackFunction) => EXC_LocalizerFunction = callbackFunction;
        public class UtilsException(string template, int? status = null, params object?[] args) : Exception(Build(template, EXC_LocalizerFunction, args))
        {
            public int? Status { get; private set; } = status;
            public string RealMessage { get { return this.InnerException?.Message ?? this.Message; } }


            private static string Build(string template, Func<string, string>? localizer, object?[] args)
            {
                // SPLIT: separa SOLO parti fisse da variabili {0}, {name}, ecc.
                var fixedParts = SplitFixedParts(template);

                // LOCALIZZA: applica callback SOLO alle parti fisse
                if (localizer != null)
                {
                    for (int i = 0; i < fixedParts.Length; i++)
                    {
                        fixedParts[i] = localizer(fixedParts[i]);
                    }
                }

                // RCOMPONI template localizzato
                var localizedTemplate = string.Join("", fixedParts);

                // FORMAT con variabili
                return string.Format(localizedTemplate, args);
            }


            /// <summary>
            /// SPLIT template in PARTI FISSE escludendo {variabili}
            /// "Ciao sono {name} ed ho {age} anni" → ["Ciao sono ", " ed ho ", " anni"]
            /// </summary>
            private static string[] SplitFixedParts(string template)
            {
                char[] charList = ['{', '}'];

                // Regex: (parte fissa)({qualcosa}|fine)
                var matches = Regex.Matches(template, @"([^}]*(?:\{[^}]+\}[^}]*)*)(?:\{[^}]+\}|$)");
                var parts = new List<string>();

                foreach (Match match in matches)
                {
                    var fixedPart = match.Value.Split(charList)[0].TrimEnd();
                    if (!string.IsNullOrEmpty(fixedPart)) parts.Add(fixedPart);
                }


                return [.. parts];
            }
        }

        #endregion
    }



    public static class ValueExtensions
    {
        // Per nullable: gestisce null o default(T)
        public static bool IsNullOrDefault<T>(this T? value) where T : struct
        {
            return !value.HasValue || value.Value.Equals(default(T));
        }

        // Per value types: solo == default(T)
        public static bool IsDefault<T>(this T value) where T : struct
        {
            return value.Equals(default(T));
        }

        public static string ToCompactString(this TimeSpan ts)
        {
            var parts = new List<string>();

            if (ts.Days >= 7)
            {
                var weeks = ts.Days / 7;
                parts.Add($"{weeks}w");
                ts = ts.Add(TimeSpan.FromDays(-(weeks * 7)));
            }

            if (ts.Days > 0) parts.Add($"{ts.Days}d");
            if (ts.Hours > 0) parts.Add($"{ts.Hours}h");
            if (ts.Minutes > 0) parts.Add($"{ts.Minutes}m");
            if (ts.Seconds > 0) parts.Add($"{ts.Seconds}s");
            if (ts.Milliseconds > 0) parts.Add($"{ts.Milliseconds}ms");

            return parts.Count > 0 ? string.Join(" ", parts) : "0";
        }

        public static int? YearsTillNow(this DateTime? dataNascita) => dataNascita == null ? null : ((DateTime)dataNascita).YearsTillNow();
        public static int YearsTillNow(this DateTime dataNascita)
        {
            DateTime dataOggi = DateTime.Now;
            int eta = dataOggi.Year - dataNascita.Year;
            if (dataOggi.Month < (dataNascita).Month || (dataOggi.Month == dataNascita.Month && dataOggi.Day < dataNascita.Day))
            {
                eta--;
            }

            return eta;
        }


        /// <summary>
        /// Given a specific <ENTITY> to a method of a class, it will be created a new <ENTITY>() and the property of <ENTITY> matching the one of the class
        /// will be setted to the same value.
        /// </summary>
        public static OriginalEntity WrapEntity<OriginalEntity>(this object self) where OriginalEntity : class, new()
        {
            var baseEntity = new OriginalEntity();

            List<string> sourcePropsNames = [.. typeof(OriginalEntity).GetProperties().Select(x => x.Name)];

            foreach (var prop in self.GetType().GetProperties())
            {
                bool isValid = sourcePropsNames.Contains(prop.Name) && prop.CanRead && prop.CanWrite;
                if (isValid) prop.SetValue(baseEntity, prop.GetValue(self));
            }

            return baseEntity;
        }

        /// <summary>
        /// Given a specific entity-object to a class, it will unwrap its properties and the values will be setted to the current class.
        /// Use it especially in the constructor
        /// </summary>
        public static void UnwrapEntity<OriginalEntity>(this object self, OriginalEntity entity)
        {
            List<string> destinationPropsNames = [.. self.GetType().GetProperties().Select(x => x.Name)];

            foreach (var prop in typeof(OriginalEntity).GetProperties())
            {
                bool isValid = destinationPropsNames.Contains(prop.Name) && prop.CanRead && prop.CanWrite;
                if (isValid) prop.SetValue(self, prop.GetValue(entity));
            }
        }


        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        public static T DeepCopy<T>(this T original)
        {
            if (original == null!) return default(T) ?? default!;

            var type = original.GetType();
            if (type.IsValueType || type == typeof(string)) return original;


#pragma warning disable S3011 // Reflection sicura per deep copy interni
            var copy = (T?)Activator.CreateInstance(type) ?? throw new Exception("Original Instance Not Found")!;
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var fieldValue = field.GetValue(original);
                field.SetValue(copy, fieldValue);
            }
#pragma warning restore S3011

            return copy;
        }
    }


    public class Debouncer(TimeSpan delay) : IDisposable
    {
        private readonly TimeSpan _delay = delay;
        private Timer? _timer;
        private Action? _pendingAction;
        private readonly Lock _lock = new();
        private bool _disposed = false;


        public void Debounce(Action action)
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, nameof(Debouncer));

                _pendingAction = action;
                _timer?.Dispose();  // Cancella precedente
                _timer = new Timer(OnTimerElapsed, null, _delay, Timeout.InfiniteTimeSpan);
            }
        }

        public async Task DebounceAsync(Func<Task> asyncAction)
        {
            Timer? oldTimer;
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, nameof(Debouncer));

                _pendingAction = () => _ = Task.Run(asyncAction);  // Wrapper sync
                oldTimer = _timer;
                _timer = new Timer(OnTimerElapsed, null, _delay, Timeout.InfiniteTimeSpan);
            }

            if (oldTimer != null) await oldTimer.DisposeAsync();
        }

        private void OnTimerElapsed(object? _)
        {
            Action? action;
            lock (_lock)
            {
                action = _pendingAction;
                _pendingAction = null;
            }
            action?.Invoke();  // Fuori lock, evita deadlock
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    lock (_lock)
                    {
                        _timer?.Dispose();
                        _timer = null;
                        _pendingAction = null;
                    }
                }
                _disposed = true;
            }
        }

        ~Debouncer() => Dispose(false);
    }

}
