namespace LinenLady.API.Features.Contact;

public sealed class ContactValidationException : Exception
{
    public ContactValidationException(string message) : base(message) { }
}

public sealed class ContactRateLimitedException : Exception
{
    public ContactRateLimitedException(string message) : base(message) { }
}

public sealed class ContactProviderException : Exception
{
    public ContactProviderException(string message) : base(message) { }
    public ContactProviderException(string message, Exception inner) : base(message, inner) { }
}