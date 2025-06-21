namespace WorldBuilder.Lib.Validation {
    public class FieldValidationError {
        public string Field { get; set; }
        public string Message { get; set; }

        public FieldValidationError(string field, string message) {
            Field = field;
            Message = message;
        }
    }
}