namespace LinenLady.API.Api.Filters;

using LinenLady.API.Customers.Handler;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

/// <summary>
/// Maps typed exceptions thrown by handlers onto the same HTTP status codes
/// the original Azure Functions used. Applied globally via MVC options in
/// Program.cs. Covers customer/reservation domain exceptions plus the generic
/// BCL exceptions that image and inventory handlers throw.
/// </summary>
public sealed class DomainExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (status, message) = context.Exception switch
        {
            // Customer/reservation domain
            CustomerNotFoundException ex     => (404, ex.Message),
            ReservationNotFoundException ex  => (404, ex.Message),
            ItemNotFoundException ex         => (404, ex.Message),
            ReservationConflictException ex  => (409, ex.Message),
            ItemAlreadyReservedException ex  => (409, ex.Message),
            EmailNotVerifiedException ex     => (403, ex.Message),

            // Generic — used by image/inventory handlers that predate the typed exceptions
            KeyNotFoundException ex          => (404, ex.Message),
            ArgumentException ex             => (400, ex.Message),

            _ => (0, "")
        };

        if (status == 0) return; // not our concern — let the framework handle it

        context.Result = new ContentResult
        {
            StatusCode = status,
            Content = message,
            ContentType = "text/plain"
        };
        context.ExceptionHandled = true;
    }
}
