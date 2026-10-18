namespace Api {
    //An error of an API call, with a stable code (see the Scripting API page of the wiki for the list)
    public class ApiException : System.Exception {
        public readonly string code;
        public readonly object details;

        public ApiException(string code, string message, object details = null) : base(message) {
            this.code = code;
            this.details = details;
        }

        public static ApiException NotFound(string what) {
            return new ApiException("not_found", what + " not found");
        }

        public static ApiException WrongKind(string expected, string actual) {
            return new ApiException("wrong_kind", "expected " + expected + ", got " + actual);
        }

        public static ApiException InvalidValue(string message) {
            return new ApiException("invalid_value", message);
        }

        public static ApiException InvalidParam(string message) {
            return new ApiException("invalid_param", message);
        }

        public static ApiException InvalidOperation(string message) {
            return new ApiException("invalid_operation", message);
        }
    }
}
