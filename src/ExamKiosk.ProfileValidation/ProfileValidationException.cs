namespace ExamKiosk.ProfileValidation;

public sealed class ProfileValidationException : Exception
{
    public ProfileValidationException(string message)
        : base(message)
    {
    }

    public ProfileValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
