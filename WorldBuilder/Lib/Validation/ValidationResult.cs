using System.Collections.Generic;

namespace WorldBuilder.Lib.Validation {
    public class ValidationResult {
        public bool IsValid { get; set; }
        public List<FieldValidationError> Errors { get; } = new();

        public void AddError(string field, string message) {
            Errors.Add(new FieldValidationError(field, message));
        }
    }
}