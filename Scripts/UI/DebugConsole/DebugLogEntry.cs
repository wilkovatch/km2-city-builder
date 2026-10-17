using System.Globalization;

// Port of yasirkula's In-game Debug Console (MIT)
namespace IngameDebugConsole {
    public enum DebugLogType {
        Info,
        Warning,
        Error
    }

    [System.Flags]
    public enum DebugLogFilter {
        None = 0,
        Info = 1,
        Warning = 2,
        Error = 4,
        All = 7
    }

    // Container for a simple debug entry
    public class DebugLogEntry : System.IEquatable<DebugLogEntry> {
        private const int HASH_NOT_CALCULATED = -623218;
        private static readonly CompareInfo caseInsensitiveComparer = CultureInfo.InvariantCulture.CompareInfo;

        public string logString;
        public string stackTrace;
        public DebugLogType logType;

        // Collapsed count
        public int count;

        private string completeLog;
        private int hashValue;

        public void Initialize(string logString, string stackTrace) {
            this.logString = logString;
            this.stackTrace = stackTrace;

            completeLog = null;
            count = 1;
            hashValue = HASH_NOT_CALCULATED;
        }

        // Check if two entries have the same origin
        public bool Equals(DebugLogEntry other) {
            return other != null && logString == other.logString && stackTrace == other.stackTrace;
        }

        public override bool Equals(object obj) => Equals(obj as DebugLogEntry);

        // Checks if logString or stackTrace contains the search term
        public bool MatchesSearchTerm(string searchTerm) {
            return Matches(logString, stackTrace, searchTerm);
        }

        internal static bool Matches(string logString, string stackTrace, string searchTerm) {
            return (logString != null && caseInsensitiveComparer.IndexOf(logString, searchTerm, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0) ||
                (stackTrace != null && caseInsensitiveComparer.IndexOf(stackTrace, searchTerm, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
        }

        // Return a string containing complete information about this debug entry
        public override string ToString() {
            completeLog ??= string.Concat(logString, "\n", stackTrace);
            return completeLog;
        }

        public override int GetHashCode() {
            if (hashValue == HASH_NOT_CALCULATED) {
                unchecked {
                    hashValue = 17;
                    hashValue = hashValue * 23 + (logString == null ? 0 : logString.GetHashCode());
                    hashValue = hashValue * 23 + (stackTrace == null ? 0 : stackTrace.GetHashCode());
                }
            }
            return hashValue;
        }
    }

    public readonly struct QueuedDebugLogEntry {
        public readonly string logString;
        public readonly string stackTrace;
        public readonly DebugLogType logType;

        public QueuedDebugLogEntry(string logString, string stackTrace, DebugLogType logType) {
            this.logString = logString;
            this.stackTrace = stackTrace;
            this.logType = logType;
        }

        public bool MatchesSearchTerm(string searchTerm) {
            return DebugLogEntry.Matches(logString, stackTrace, searchTerm);
        }
    }
}
