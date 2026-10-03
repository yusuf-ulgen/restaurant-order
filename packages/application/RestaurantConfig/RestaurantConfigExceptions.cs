namespace RestaurantOrder.Application.RestaurantConfig;

public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message) : base(message)
    {
    }
}

public class DuplicateSlugException : Exception
{
    public DuplicateSlugException(string message) : base(message)
    {
    }
}

public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message)
    {
    }
}

public class DuplicateCodeException : Exception
{
    public DuplicateCodeException(string message) : base(message)
    {
    }
}
